#pragma once
#include "ProjectC/GameCommand.hpp"
#include <nlohmann/json.hpp>
#include <functional>
#include <memory>
#include <array>
#include <map>
#include <string>
namespace ProjectC::Network
{
using Json = nlohmann::json;
struct ClientSession
{
    std::function<void(Json const&)> Send;
    int Player = 0; // Assigned by MatchSession, never copied from incoming JSON.
};
class MatchSession
{
public:
    void Receive(std::shared_ptr<ClientSession> const& client, std::string const& message);
    void Disconnect(std::shared_ptr<ClientSession> const& client);
    Json Snapshot(int viewer) const;
private:
    struct Seat
    {
        std::string Token;
        std::weak_ptr<ClientSession> Client;
        std::map<std::string, std::pair<std::string, Json>> Requests;
    };
    std::array<Seat, 2> seats;
    GameCore::GameState state;
    bool started = false;
    std::uint64_t sequence = 0;
    Json Message(std::string const& type, std::string const& request = "") const;
    void SendSnapshot(std::shared_ptr<ClientSession> const& client, std::string const& request = "") const;
    void Broadcast(std::vector<GameCore::GameEvent> const& events);
    void Handle(std::shared_ptr<ClientSession> const& client, Json const& input);
};
void RunServer(unsigned short port);
}
