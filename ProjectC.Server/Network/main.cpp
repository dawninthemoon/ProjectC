#include "MatchSession.hpp"
#include <charconv>
#include <iostream>
#include <string_view>
int main(int argc, char** argv)
{
    unsigned port = 7777;
    if (argc != 1)
    {
        if (argc != 3 || std::string_view(argv[1]) != "--port") { std::cerr << "Usage: ProjectCNetworkServer [--port 1..65535]\n"; return 2; }
        std::string_view text(argv[2]);
        auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), port);
        if (error != std::errc{} || end != text.data() + text.size() || port == 0 || port > 65535)
        { std::cerr << "Invalid port\n"; return 2; }
    }
    try { ProjectC::Network::RunServer(static_cast<unsigned short>(port)); }
    catch (std::exception const& error) { std::cerr << "Server failed: " << error.what() << '\n'; return 1; }
}
