#include "WebsiteApi.hpp"
#include <chrono>
#include <ctime>
#include <curl/curl.h>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#ifdef _WIN32
#include <windows.h>
#else
#include <fcntl.h>
#include <unistd.h>
#endif

namespace ProjectC::Network
{
namespace
{
constexpr long ConnectTimeoutSeconds = 10;
constexpr long RequestTimeoutSeconds = 30;
constexpr std::size_t MaximumResponseBytes = 256 * 1024;
constexpr std::size_t MaximumPendingRequests = 256;
constexpr int MaximumResultAttempts = 6;
constexpr int RetryBaseSeconds = 2;
constexpr int OutboxPollSeconds = 1;

std::size_t Append(char* data, std::size_t size, std::size_t count, void* context)
{
    auto& text = *static_cast<std::string*>(context);
    auto bytes = size * count;
    if (bytes > MaximumResponseBytes - text.size())
    {
        return 0;
    }
    text.append(data, bytes);
    return bytes;
}

// Flush before the atomic rename; the queue must survive process/host restarts.
void Persist(std::filesystem::path const& path, Json const& value)
{
    auto temporary = path;
    temporary += ".tmp";
    {
        std::ofstream file(temporary, std::ios::binary | std::ios::trunc);
        file.exceptions(std::ios::failbit | std::ios::badbit);
        file << value.dump();
        file.flush();
    }
#ifdef _WIN32
    auto handle =
        CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    if (handle == INVALID_HANDLE_VALUE)
    {
        throw std::runtime_error("OutboxOpenFailed");
    }
    auto flushed = FlushFileBuffers(handle);
    CloseHandle(handle);
    if (!flushed || !MoveFileExW(temporary.c_str(),
                                 path.c_str(),
                                 MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH))
    {
        throw std::runtime_error("OutboxCommitFailed");
    }
#else
    auto descriptor = open(temporary.c_str(), O_WRONLY);
    auto flushed = descriptor >= 0 && fsync(descriptor) == 0;
    if (descriptor >= 0)
    {
        close(descriptor);
    }
    if (!flushed)
    {
        throw std::runtime_error("OutboxFlushFailed");
    }
    std::filesystem::rename(temporary, path);
    descriptor = open(path.parent_path().c_str(), O_RDONLY | O_DIRECTORY);
    flushed = descriptor >= 0 && fsync(descriptor) == 0;
    if (descriptor >= 0)
    {
        close(descriptor);
    }
    if (!flushed)
    {
        throw std::runtime_error("OutboxDirectoryFlushFailed");
    }
#endif
}
} // namespace

bool IsUuid(std::string const& value)
{
    constexpr std::size_t UuidLength = 36;
    if (value.size() != UuidLength)
    {
        return false;
    }
    for (std::size_t i = 0; i < value.size(); ++i)
    {
        bool separator = i == 8 || i == 13 || i == 18 || i == 23;
        if (separator
                ? value[i] != '-'
                : !((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f')))
        {
            return false;
        }
    }
    return true;
}
std::string MatchUuid()
{
    auto text = Token().substr(0, 32);
    text[12] = '4';
    text[16] = "89ab"[std::stoi(text.substr(16, 1), nullptr, 16) & 3];
    return text.substr(0, 8) + "-" + text.substr(8, 4) + "-" + text.substr(12, 4) + "-" +
           text.substr(16, 4) + "-" + text.substr(20);
}
std::string UtcNow()
{
    auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
    std::tm utc{};
#ifdef _WIN32
    gmtime_s(&utc, &now);
#else
    gmtime_r(&now, &utc);
#endif
    std::ostringstream text;
    text << std::put_time(&utc, "%Y-%m-%dT%H:%M:%SZ");
    return text.str();
}
std::string ApiResponse::Reason() const
{
    // Never return upstream error bodies (which may contain private details).
    switch (Status)
    {
    case 401:
        return "AuthenticationFailed";
    case 404:
        return "DeckNotFound";
    case 409:
        return "CatalogVersionMismatch";
    case 422:
        return "InvalidDeck";
    case 429:
        return "ApiRateLimited";
    default:
        return "WebsiteUnavailable";
    }
}
WebsiteApi::WebsiteApi(std::string url, std::string key, std::filesystem::path outbox)
    : url(std::move(url)), key(std::move(key)), outbox(std::move(outbox))
{
    bool local = (this->url.starts_with("http://127.0.0.1:") ||
                  this->url.starts_with("http://localhost:")) &&
                 this->url.find_first_of("@\\\r\n") == std::string::npos;
    if ((!this->url.starts_with("https://") && !local) ||
        this->key.size() < TokenEntropyByteCount ||
        this->key.find_first_of("\r\n") != std::string::npos)
    {
        throw std::runtime_error("Invalid Website API configuration");
    }
    while (this->url.ends_with('/'))
    {
        this->url.pop_back();
    }
    if (curl_global_init(CURL_GLOBAL_DEFAULT) != CURLE_OK)
    {
        throw std::runtime_error("HTTP initialization failed");
    }
    std::filesystem::create_directories(this->outbox);
    auto probe = this->outbox / ("probe-" + Token() + ".tmp");
    Persist(probe, Json::object());
    std::filesystem::remove(probe);
    worker = std::thread([this] { Run(); });
}
WebsiteApi::~WebsiteApi()
{
    {
        std::lock_guard lock(mutex);
        stopping = true;
    }
    wake.notify_one();
    worker.join();
    curl_global_cleanup();
}
ApiResponse WebsiteApi::Request(std::string const& path, Json const& body) const
{
    ApiResponse response;
    auto* handle = curl_easy_init();
    if (!handle)
    {
        return response;
    }
    auto endpoint = url + path;
    auto text = body.dump();
    std::string output;
    curl_slist* headers = nullptr;
    headers = curl_slist_append(headers, "Content-Type: application/json");
    headers = curl_slist_append(headers, ("X-Battle-Api-Key: " + key).c_str());
    curl_easy_setopt(handle, CURLOPT_URL, endpoint.c_str());
    curl_easy_setopt(handle, CURLOPT_HTTPHEADER, headers);
    curl_easy_setopt(handle, CURLOPT_POSTFIELDS, text.c_str());
    curl_easy_setopt(handle, CURLOPT_POSTFIELDSIZE, static_cast<long>(text.size()));
    curl_easy_setopt(handle, CURLOPT_CONNECTTIMEOUT, ConnectTimeoutSeconds);
    curl_easy_setopt(handle, CURLOPT_TIMEOUT, RequestTimeoutSeconds);
    curl_easy_setopt(handle, CURLOPT_NOSIGNAL, 1L);
    curl_easy_setopt(handle, CURLOPT_SSL_VERIFYPEER, 1L);
    curl_easy_setopt(handle, CURLOPT_SSL_VERIFYHOST, 2L);
    curl_easy_setopt(handle, CURLOPT_FOLLOWLOCATION, 0L);
    curl_easy_setopt(handle, CURLOPT_WRITEFUNCTION, Append);
    curl_easy_setopt(handle, CURLOPT_WRITEDATA, &output);
    auto error = curl_easy_perform(handle);
    response.TransportError = static_cast<int>(error);
    if (error == CURLE_OK)
    {
        curl_easy_getinfo(handle, CURLINFO_RESPONSE_CODE, &response.Status);
        response.Body = Json::parse(output, nullptr, false);
        if (response.Body.is_discarded())
        {
            response.Body = Json();
            if (response.Status == 200)
            {
                response.Status = 0;
            }
        }
    }
    curl_slist_free_all(headers);
    curl_easy_cleanup(handle);
    return response;
}
std::future<ApiResponse> WebsiteApi::Post(std::string path, Json body)
{
    auto task = std::make_shared<std::packaged_task<ApiResponse()>>(
        [this, path = std::move(path), body = std::move(body)] { return Request(path, body); });
    auto future = task->get_future();
    {
        std::lock_guard lock(mutex);
        if (work.size() >= MaximumPendingRequests)
        {
            throw std::runtime_error("API capacity reached");
        }
        work.push_back([task] { (*task)(); });
    }
    wake.notify_one();
    return future;
}
void WebsiteApi::StoreResult(Json const& body)
{
    auto id = body.at("matchId").get<std::string>();
    if (!IsUuid(id))
    {
        throw std::runtime_error("Invalid result identity");
    }
    auto path = outbox / (id + ".json");
    if (!std::filesystem::exists(path))
    {
        Persist(path, Json{{"body", body}, {"attempts", 0}, {"nextAttempt", 0}});
    }
    wake.notify_one();
}
std::vector<Json> WebsiteApi::TakeResults()
{
    std::lock_guard lock(mutex);
    auto completed = std::move(results);
    results.clear();
    return completed;
}
void WebsiteApi::Run()
{
    for (;;)
    {
        std::function<void()> task;
        {
            std::unique_lock lock(mutex);
            wake.wait_for(lock,
                          std::chrono::seconds(OutboxPollSeconds),
                          [this] { return stopping || !work.empty(); });
            if (stopping)
            {
                return;
            }
            if (!work.empty())
            {
                task = std::move(work.front());
                work.pop_front();
            }
        }
        if (task)
        {
            task();
        }
        try
        {
            for (auto const& entry : std::filesystem::directory_iterator(outbox))
            {
                if (entry.path().extension() != ".json")
                {
                    continue;
                }
                std::ifstream file(entry.path());
                auto job = Json::parse(file);
                // Windows cannot replace/remove a job while its input stream is open.
                file.close();
                int attempts = job.at("attempts").get<int>();
                auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
                if (attempts >= MaximumResultAttempts ||
                    now < job.at("nextAttempt").get<std::int64_t>())
                {
                    continue;
                }
                ++attempts;
                job["attempts"] = attempts;
                job["nextAttempt"] = now + RetryBaseSeconds * (1 << attempts);
                // Count the attempt durably before HTTP, including an interrupted request.
                Persist(entry.path(), job);
                auto response = Request("/api/internal/battle/matches", job.at("body"));
                auto id = job.at("body").at("matchId").get<std::string>();
                if (response.Success() && response.Body.value("matchId", "") == id &&
                    response.Body.contains("rankChanges"))
                {
                    std::filesystem::remove(entry.path());
                    {
                        std::lock_guard lock(mutex);
                        results.push_back(response.Body);
                    }
                    std::cout << "Result stored match=" << id << '\n';
                }
                else
                {
                    bool retryable =
                        response.Status == 0 || response.Status >= 500 || response.Status == 429;
                    job["attempts"] = retryable ? attempts : MaximumResultAttempts;
                    job["nextAttempt"] = now + RetryBaseSeconds * (1 << attempts);
                    job["lastStatus"] = response.Status;
                    job["transportError"] = response.TransportError;
                    Persist(entry.path(), job);
                    std::cerr << "Result submission failed match=" << id
                              << " status=" << response.Status << " attempt=" << attempts
                              << " transport=" << response.TransportError << " retained=true\n";
                }
                // Bound shutdown latency to one request, even with a large backlog.
                std::lock_guard lock(mutex);
                if (stopping)
                {
                    return;
                }
            }
        }
        catch (std::exception const&)
        {
            std::cerr << "Result outbox I/O or format failure; retained for operator review\n";
        }
    }
}
} // namespace ProjectC::Network
