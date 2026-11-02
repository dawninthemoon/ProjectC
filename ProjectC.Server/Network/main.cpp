#include "MatchManager.hpp"
#include <charconv>
#include <filesystem>
#include <iostream>
#include <limits>
#include <string_view>
namespace
{
constexpr unsigned MinimumOptionValue = 1;
constexpr unsigned SecondsPerMinute = 60;
constexpr unsigned MinutesPerHour = 60;
constexpr unsigned HoursPerDay = 24;
constexpr unsigned MaximumOptionSeconds = SecondsPerMinute * MinutesPerHour * HoursPerDay;
constexpr unsigned MaximumPort = std::numeric_limits<unsigned short>::max();
constexpr unsigned MaximumMulliganTimeoutSeconds = SecondsPerMinute * MinutesPerHour;
constexpr unsigned MaximumConfiguredMatches = 1024;
constexpr unsigned MaximumConfiguredReplays = 4096;
} // namespace

int main(int argc, char** argv)
{
    using namespace ProjectC::Network;
    ServerConfig config;
    auto data = std::filesystem::absolute(argv[0]).parent_path() / "Data";
    if (std::filesystem::exists(data))
    {
        config.DataDirectory = data.string();
    }
    try
    {
        for (int i = 1; i < argc; i += 2)
        {
            if (i + 1 >= argc)
            {
                throw std::runtime_error("Missing argument value");
            }
            std::string_view option(argv[i]), text(argv[i + 1]);
            if (option == "--data")
            {
                config.DataDirectory = std::string(text);
                continue;
            }
            if (option == "--mulligan" || option == "--mulligan-auto-keep")
            {
                if (text != "on" && text != "off")
                {
                    throw std::runtime_error("Use on/off for mulligan switches");
                }
                if (option == "--mulligan")
                {
                    config.EnableMulligan = text == "on";
                }
                else
                {
                    config.MulliganAutoKeep = text == "on";
                }
                continue;
            }
            unsigned value = 0;
            auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value);
            if (error != std::errc{} || end != text.data() + text.size() ||
                value < MinimumOptionValue || value > MaximumOptionSeconds)
            {
                throw std::runtime_error("Invalid numeric option");
            }
            if (option == "--port" && value <= MaximumPort)
            {
                config.Port = static_cast<unsigned short>(value);
            }
            else if (option == "--mulligan-timeout" && value <= MaximumMulliganTimeoutSeconds)
            {
                config.MulliganTimeoutSeconds = static_cast<int>(value);
            }
            else if (option == "--max-matches" && value <= MaximumConfiguredMatches)
            {
                config.MaxMatches = value;
            }
            else if (option == "--max-replays" && value <= MaximumConfiguredReplays)
            {
                config.MaxReplays = value;
            }
            else if (option == "--turn-duration")
            {
                config.TurnDurationSeconds = static_cast<int>(value);
            }
            else if (option == "--waiting-timeout")
            {
                config.WaitingTimeoutSeconds = static_cast<int>(value);
            }
            else if (option == "--playing-timeout")
            {
                config.PlayingTimeoutSeconds = static_cast<int>(value);
            }
            else if (option == "--finished-timeout")
            {
                config.FinishedTimeoutSeconds = static_cast<int>(value);
            }
            else if (option == "--replay-retention")
            {
                config.ReplayRetentionSeconds = static_cast<int>(value);
            }
            else
            {
                throw std::runtime_error("Unknown option or out-of-range value");
            }
        }
        RunServer(config);
    }
    catch (std::exception const& error)
    {
        std::cerr << "Server failed: " << error.what() << '\n'
                  << "Usage: ProjectCNetworkServer [--port 7777] [--max-matches 64] [--data Data]\n"
                  << "  [--waiting-timeout 900] [--playing-timeout 1800] [--finished-timeout 300]\n"
                  << "  [--mulligan on|off] [--mulligan-timeout 60] [--mulligan-auto-keep on|off]\n"
                  << "  [--turn-duration 180]\n"
                  << "  [--max-replays 128] [--replay-retention 3600]\n";
        return 1;
    }
}
