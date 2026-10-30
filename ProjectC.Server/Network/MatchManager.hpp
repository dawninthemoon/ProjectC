#pragma once
#include "MatchSession.hpp"
#include <deque>
namespace ProjectC::Network
{
struct ServerConfig
{
    unsigned short Port = 7777;
    std::size_t MaxMatches = 64;
    std::size_t MaxReplays = 128;
    int WaitingTimeoutSeconds = 900;
    int PlayingTimeoutSeconds = 1800;
    int FinishedTimeoutSeconds = 300;
    int ReplayRetentionSeconds = 3600;
    std::string DataDirectory = DefaultDataDirectory();
};
class MatchManager
{
public:
    explicit MatchManager(ServerConfig config = {});
    void Receive(std::shared_ptr<ClientSession> const& client, std::string const& message);
    void Disconnect(std::shared_ptr<ClientSession> const& client);
    void Cleanup(Clock::time_point now = Clock::now());
    void Shutdown();
    std::size_t ActiveCount() const { return matches.size(); }
    std::deque<ReplayRecord> const& Replays() const { return replays; }
    MatchSession const* Find(std::string const& id) const;
private:
    ServerConfig config;
    std::shared_ptr<CardCatalog const> catalog;
    std::shared_ptr<IdentityAllocator> identities = std::make_shared<IdentityAllocator>();
    std::map<std::string,std::unique_ptr<MatchSession>> matches;
    std::deque<ReplayRecord> replays;
    std::uint64_t nextMatch = 0;
    void Archive(ReplayRecord record);
};
void RunServer(ServerConfig const& config);
}
