#pragma once
#include "MatchSession.hpp"
#include "WebsiteApi.hpp"
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
    std::optional<bool> EnableMulligan; // Overrides data only when explicitly configured.
    int MulliganTimeoutSeconds = 60;
    int TurnDurationSeconds = 180;
    bool MulliganAutoKeep = true;
    std::string DataDirectory = DefaultDataDirectory();
    std::string WebsiteApiUrl; // Empty retains the isolated development server.
    std::string BattleApiKey;
    std::string ResultOutboxDirectory = "BattleOutbox";
};

struct MatchmakingEntry
{
    std::weak_ptr<ClientSession> Client;
    std::string DeckId;
    Json WebsiteDeck;
};

class MatchManager
{
  public:
    explicit MatchManager(ServerConfig config = {});
    void Receive(std::shared_ptr<ClientSession> const& client, std::string const& message);
    void Disconnect(std::shared_ptr<ClientSession> const& client);
    void Cleanup(Clock::time_point now = Clock::now());
    void Shutdown();
    void TryMatchmaking();

    std::size_t ActiveCount() const
    {
        return matches.size();
    }
    std::deque<ReplayRecord> const& Replays() const
    {
        return replays;
    }
    MatchSession const* Find(std::string const& id) const;

  private:
    ServerConfig config;
    std::shared_ptr<CardCatalog const> catalog;
    std::shared_ptr<IdentityAllocator> identities = std::make_shared<IdentityAllocator>();
    std::map<std::string, std::unique_ptr<MatchSession>> matches;
    std::deque<MatchmakingEntry> matchmakingQueue;
    std::deque<ReplayRecord> replays;
    std::uint64_t nextMatch = 0;
    std::unique_ptr<WebsiteApi> website;
    struct PendingApi
    {
        std::weak_ptr<ClientSession> Client;
        std::uint64_t Generation;
        std::string RequestId;
        std::string DeckId;
        bool Verifying = true;
        std::future<ApiResponse> Response;
    };
    std::deque<PendingApi> pendingApi;
    struct RankedMatch
    {
        Json Participants;
        std::array<int, 2> Players;
        std::array<std::weak_ptr<ClientSession>, 2> Clients;
    };
    std::map<std::string, RankedMatch> rankedMatches;
    bool AccountBusy(std::string const& playerId, ClientSession const* except) const;
    void PollWebsite();
    void Archive(ReplayRecord record);
};
void RunServer(ServerConfig const& config);
} // namespace ProjectC::Network
