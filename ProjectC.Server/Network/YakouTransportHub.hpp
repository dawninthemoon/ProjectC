#pragma once
#include "MatchSession.hpp"
#include "YakouSession.hpp"

#include <chrono>
#include <map>
#include <memory>
#include <string>

namespace ProjectC::Network
{
// Explicitly opt-in, non-ranked prototype transport. Never used for legacy
// matchmaking, website accounts or production runtime readiness.
class YakouTransportHub
{
public:
    explicit YakouTransportHub(std::string const& dataDirectory, std::size_t maxRooms,
                               bool alpha02 = false);
    void Receive(std::shared_ptr<ClientSession> const& client, Json const& request,
                 std::string const& requestId, std::string const& type);
    void Disconnect(std::shared_ptr<ClientSession> const& client);
    void Cleanup(Clock::time_point now, std::chrono::seconds waiting,
                 std::chrono::seconds playing);
    void Shutdown();
    [[nodiscard]] std::size_t RoomCount() const { return rooms_.size(); }

private:
    struct Room
    {
        std::unique_ptr<YakouSession> Session;
        std::array<std::weak_ptr<ClientSession>, 2> Clients;
        std::array<std::string, 2> Tokens;
        std::array<std::string, 2> DeckIds;
        std::array<std::uint64_t, 2> DeckSeeds{};
        std::uint64_t MatchSeed = 0;
        std::array<std::string, 2> PreviousTokens;
        std::array<std::string, 2> ResumeRequests;
        std::array<Clock::time_point, 2> ResumeRetryUntil;
        Clock::time_point LastActivity = Clock::now();
        bool Joined = false;
    };
    YakouPrototype catalog_;
    std::size_t maxRooms_;
    std::map<std::string, Room> rooms_;

    void SnapshotBoth(std::string const& id, Room const& room,
                      std::string const& requestId = "") const;
    void CloseRoom(std::string const& id, std::string const& reason);
};
} // namespace ProjectC::Network
