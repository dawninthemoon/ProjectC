#include "Protocol.hpp"
#include <stdexcept>
#include <map>
#include <set>
#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#else
#include <sys/random.h>
#include <cerrno>
#endif
namespace ProjectC::Network
{
void Fields(Json const& value, std::initializer_list<char const*> allowed)
{
    if (!value.is_object()) throw std::runtime_error("ExpectedObject");
    for (auto const& item : value.items())
    {
        bool found = false;
        for (auto key : allowed) if (item.key() == key) found = true;
        if (!found) throw std::runtime_error("UnexpectedField");
    }
}
std::string String(Json const& value, char const* key, std::size_t limit)
{
    auto const& field = value.at(key);
    if (!field.is_string()) throw std::runtime_error("ExpectedString");
    auto result = field.get<std::string>();
    if (result.empty() || result.size() > limit) throw std::runtime_error("StringRange");
    for (unsigned char c : result) if (c < 32 || c > 126) throw std::runtime_error("StringCharacters");
    return result;
}
std::uint64_t Number(Json const& value, char const* key, std::uint64_t max)
{
    auto const& field = value.at(key);
    if (!(field.is_number_unsigned() || field.is_number_integer()) ||
        (field.is_number_integer() && !field.is_number_unsigned() && field.get<std::int64_t>() < 0))
        throw std::runtime_error("ExpectedUnsignedInteger");
    auto n = field.get<std::uint64_t>();
    if (n > max) throw std::runtime_error("IntegerRange");
    return n;
}
std::string Token()
{
    unsigned char bytes[32];
#ifdef _WIN32
    if (BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0)
        throw std::runtime_error("EntropyFailure");
#else
    std::size_t offset = 0;
    while (offset < sizeof(bytes))
    {
        auto count = getrandom(bytes + offset, sizeof(bytes) - offset, 0);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) throw std::runtime_error("EntropyFailure");
        offset += static_cast<std::size_t>(count);
    }
#endif
    std::string result;
    for (auto byte : bytes) { result += "0123456789abcdef"[byte >> 4]; result += "0123456789abcdef"[byte & 15]; }
    return result;
}

bool Boolean(Json const& value, char const* key)
{
    if (!value.at(key).is_boolean()) throw std::runtime_error("ExpectedBoolean");
    return value.at(key).get<bool>();
}
Json ParseJson(std::string const& text, std::size_t limit)
{
    if (text.size() > limit) throw std::runtime_error("MessageTooLarge");
    std::map<int, std::set<std::string>> keys;
    return Json::parse(text, [&](int depth, Json::parse_event_t event, Json& value) {
        if (depth > 32) throw std::runtime_error("JsonDepth");
        if (event == Json::parse_event_t::object_start) keys[depth + 1].clear();
        if (event == Json::parse_event_t::key && !keys[depth].insert(value.get<std::string>()).second)
            throw std::runtime_error("DuplicateField");
        return true;
    });
}
Json Envelope(std::string const& type, std::string const& request, std::string const& match, std::uint64_t sequence, int game)
{
    return {{"Type",type},{"ProtocolVersion",ProtocolVersion},{"MatchId",match},
        {"RequestId",request},{"SequenceNumber",sequence},{"GameNumber",game}};
}
}
