#include "MatchManager.hpp"
#include <charconv>
#include <filesystem>
#include <iostream>
#include <string_view>
int main(int argc, char** argv)
{
    using namespace ProjectC::Network;
    ServerConfig config;
    auto data=std::filesystem::absolute(argv[0]).parent_path()/"Data";
    if(std::filesystem::exists(data)) config.DataDirectory=data.string();
    try
    {
        for(int i=1;i<argc;i+=2)
        {
            if(i+1>=argc) throw std::runtime_error("Missing argument value");
            std::string_view option(argv[i]),text(argv[i+1]);
            if(option=="--data") {config.DataDirectory=std::string(text);continue;}
            unsigned value=0;auto [end,error]=std::from_chars(text.data(),text.data()+text.size(),value);
            if(error!=std::errc{} || end!=text.data()+text.size() || value==0 || value>86400) throw std::runtime_error("Invalid numeric option");
            if(option=="--port" && value<=65535) config.Port=static_cast<unsigned short>(value);
            else if(option=="--max-matches" && value<=1024) config.MaxMatches=value;
            else if(option=="--max-replays" && value<=4096) config.MaxReplays=value;
            else if(option=="--waiting-timeout") config.WaitingTimeoutSeconds=static_cast<int>(value);
            else if(option=="--playing-timeout") config.PlayingTimeoutSeconds=static_cast<int>(value);
            else if(option=="--finished-timeout") config.FinishedTimeoutSeconds=static_cast<int>(value);
            else if(option=="--replay-retention") config.ReplayRetentionSeconds=static_cast<int>(value);
            else throw std::runtime_error("Unknown option or out-of-range value");
        }
        RunServer(config);
    }
    catch(std::exception const& error)
    {
        std::cerr << "Server failed: " << error.what() << '\n'
            << "Usage: ProjectCNetworkServer [--port 7777] [--max-matches 64] [--data Data]\n"
            << "  [--waiting-timeout 900] [--playing-timeout 1800] [--finished-timeout 300]\n"
            << "  [--max-replays 128] [--replay-retention 3600]\n";
        return 1;
    }
}
