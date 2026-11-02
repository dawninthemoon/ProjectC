#pragma once
#include <cstdint>
#include <nlohmann/json.hpp>
#include <string>
namespace ProjectC::Network
{
using Json = nlohmann::json;
inline constexpr int ProtocolVersion = 2;
inline constexpr std::size_t DefaultProtocolStringMaxCharacters = 128;
inline constexpr std::size_t DefaultProtocolJsonMaxBytes = 16 * 1024;
inline constexpr unsigned char MinimumPrintableAsciiCharacter = 0x20;
inline constexpr unsigned char MaximumPrintableAsciiCharacter = 0x7E;
inline constexpr unsigned char AsciiDeleteCharacter = 0x7F;
inline constexpr std::size_t TokenEntropyByteCount = 32;
void Fields(Json const& value, std::initializer_list<char const*> allowed);
std::string
String(Json const& value, char const* key, std::size_t limit = DefaultProtocolStringMaxCharacters);
std::uint64_t Number(Json const& value, char const* key, std::uint64_t max);
bool Boolean(Json const& value, char const* key);
std::string Token();
Json ParseJson(std::string const& text, std::size_t limit = DefaultProtocolJsonMaxBytes);
Json Envelope(std::string const& type,
              std::string const& request = "",
              std::string const& match = "",
              std::uint64_t sequence = 0,
              int game = 0);
} // namespace ProjectC::Network
