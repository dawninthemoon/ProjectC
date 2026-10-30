#pragma once
#include <nlohmann/json.hpp>
#include <string>
#include <cstdint>
namespace ProjectC::Network
{
using Json = nlohmann::json;
inline constexpr int ProtocolVersion = 2;
void Fields(Json const& value, std::initializer_list<char const*> allowed);
std::string String(Json const& value, char const* key, std::size_t limit = 128);
std::uint64_t Number(Json const& value, char const* key, std::uint64_t max);
bool Boolean(Json const& value, char const* key);
std::string Token();
Json ParseJson(std::string const& text, std::size_t limit = 16384);
Json Envelope(std::string const& type, std::string const& request = "", std::string const& match = "", std::uint64_t sequence = 0, int game = 0);
}
