#pragma once
#include "CardCatalog.hpp"
#include "ProjectC/GameReplay.hpp"
#include <chrono>
#include <functional>
#include <memory>
#include <map>
#include <array>
namespace ProjectC::Network
{
using Clock = std::chrono::steady_clock;
struct ClientSession
{
    std::function<void(Json const&)> Send;
    int Player = 0;
    std::string MatchId; // Bound by server; never assigned from SubmitCommand.
};
enum class MatchStatus { WaitingForPlayers, Ready, Playing, Finished, Closed };
struct IdentityAllocator
{
    int NextPlayer = 1;
    int NextCard = 1;
    std::array<int,2> Players();
    int Cards(std::size_t count);
};
struct ReplayRecord
{
    std::string MatchId;
    int GameNumber = 0;
    std::uint64_t Seed = 0; // Server-private: never in network DTO/logs.
    GameCore::GameState InitialState;
    std::vector<GameCore::GameCommand> Commands;
    std::vector<GameCore::CommandResult> Results;
    GameCore::GameState FinalState;
    Clock::time_point StoredAt = Clock::now();
    bool Verify() const;
};
class MatchSession
{
public:
    MatchSession(std::string id, std::shared_ptr<CardCatalog const> catalog,
        std::shared_ptr<IdentityAllocator> identities, std::function<void(ReplayRecord)> archive);
    void Handle(std::shared_ptr<ClientSession> const& client, Json const& input);
    void Disconnect(std::shared_ptr<ClientSession> const& client);
    void Close(std::string const& reason);
    Json Snapshot(int viewer) const;
    Json Message(std::string const& type, std::string const& request = "") const;
    MatchStatus Status() const { return status; }
    Clock::time_point LastActivity() const { return lastActivity; }
    std::string const& Id() const { return id; }
private:
    struct Seat
    {
        int Player = 0;
        std::string Token;
        std::string DeckId;
        bool Ready = false;
        bool Rematch = false;
        std::weak_ptr<ClientSession> Client;
        std::map<std::string, std::pair<std::string, Json>> Requests;
    };
    std::string id;
    std::shared_ptr<CardCatalog const> catalog;
    std::shared_ptr<IdentityAllocator> identities;
    std::function<void(ReplayRecord)> archive;
    std::array<Seat,2> seats;
    GameCore::GameState state;
    MatchStatus status = MatchStatus::WaitingForPlayers;
    Clock::time_point lastActivity = Clock::now();
    std::uint64_t sequence = 0;
    int gameNumber = 0;
    int hostPlayer = 0;
    ReplayRecord journal;
    bool archived = true;
    int SeatIndex(int player) const;
    void SendSnapshot(std::shared_ptr<ClientSession> const& client, std::string const& request = "") const;
    void Broadcast(std::vector<GameCore::GameEvent> const& events);
    void Lobby() const;
    void StartGame();
    void Archive();
    void Submit(std::shared_ptr<ClientSession> const& client, Json const& input);
};
}
