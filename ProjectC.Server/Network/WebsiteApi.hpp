#pragma once
#include "Protocol.hpp"
#include <condition_variable>
#include <deque>
#include <filesystem>
#include <functional>
#include <future>
#include <mutex>
#include <thread>

namespace ProjectC::Network
{
struct ApiResponse
{
    long Status = 0;
    int TransportError = 0;
    Json Body;
    bool Success() const
    {
        return Status == 200;
    }
    std::string Reason() const;
};

// This worker owns HTTP only. Session mutations and socket sends stay on the io thread.
class WebsiteApi
{
  public:
    WebsiteApi(std::string url, std::string key, std::filesystem::path outbox);
    ~WebsiteApi();
    std::future<ApiResponse> Post(std::string path, Json body);
    void StoreResult(Json const& body);
    std::vector<Json> TakeResults();

  private:
    std::string url;
    std::string key;
    std::filesystem::path outbox;
    std::mutex mutex;
    std::condition_variable wake;
    bool stopping = false;
    std::deque<std::function<void()>> work;
    std::vector<Json> results;
    std::thread worker;
    ApiResponse Request(std::string const& path, Json const& body) const;
    void Run();
};
std::string MatchUuid();
bool IsUuid(std::string const& value);
std::string UtcNow();
} // namespace ProjectC::Network
