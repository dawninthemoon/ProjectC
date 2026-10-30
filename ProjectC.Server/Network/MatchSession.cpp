#include "MatchSession.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/CardEffects.hpp"
#include <stdexcept>
#include <limits>
#include <set>
#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#else
#include <sys/random.h>
#include <cerrno>
#endif
namespace ProjectC::Network
{
using namespace GameCore;
namespace
{
void Fields(Json const& value, std::initializer_list<char const*> allowed)
{
    if (!value.is_object()) throw std::runtime_error("ExpectedObject");
    for (auto const& item : value.items())
    {
        bool found = false;
        for (auto key : allowed) if (item.key() == key) found = true;
        if (!found) throw std::runtime_error("UnexpectedField");
    }
}
std::string String(Json const& value, char const* key, std::size_t limit = 128)
{
    auto const& field = value.at(key);
    if (!field.is_string()) throw std::runtime_error("ExpectedString");
    auto result = field.get<std::string>();
    if (result.empty() || result.size() > limit) throw std::runtime_error("StringRange");
    for (unsigned char c : result) if (c < 32 || c > 126) throw std::runtime_error("StringCharacters");
    return result;
}
std::uint64_t Number(Json const& value, char const* key, std::uint64_t max)
{
    auto const& field = value.at(key);
    if (!(field.is_number_unsigned() || field.is_number_integer()) ||
        (field.is_number_integer() && !field.is_number_unsigned() && field.get<std::int64_t>() < 0))
        throw std::runtime_error("ExpectedUnsignedInteger");
    auto n = field.get<std::uint64_t>();
    if (n > max) throw std::runtime_error("IntegerRange");
    return n;
}
std::string Token()
{
    unsigned char bytes[32];
#ifdef _WIN32
    if (BCryptGenRandom(nullptr, bytes, sizeof(bytes), BCRYPT_USE_SYSTEM_PREFERRED_RNG) != 0)
        throw std::runtime_error("EntropyFailure");
#else
    std::size_t offset = 0;
    while (offset < sizeof(bytes))
    {
        auto count = getrandom(bytes + offset, sizeof(bytes) - offset, 0);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) throw std::runtime_error("EntropyFailure");
        offset += static_cast<std::size_t>(count);
    }
#endif
    std::string result;
    for (auto byte : bytes) { result += "0123456789abcdef"[byte >> 4]; result += "0123456789abcdef"[byte & 15]; }
    return result;
}
bool VisibleCard(GameState const& state, int viewer, CardInstanceId id)
{
    if (id == 0) return false;
    for (auto const& player : state.Players)
    {
        for (auto const& card : player.Field) if (card.Id == id) return true;
        for (auto const& card : player.Graveyard) if (card.Id == id) return true;
        if (player.Id == viewer) for (auto const& card : player.Hand) if (card.Id == id) return true;
    }
    return false;
}
std::string RuleReason(CommandError error)
{
    static char const* const names[] = {
        "Success", "BattleRejected", "GameFinished", "InvalidState", "PlayerNotFound",
        "NotActivePlayer", "WrongPhase", "TurnNotStarted", "TurnAlreadyStarted",
        "CardNotFound", "WrongZone", "NotOwner", "InvalidCount", "DeckEmpty",
        "InsufficientCards", "HandFull", "FieldFull", "DrawDisabled", "InvalidDefinition",
        "InvalidLifeChange", "TurnOverflow", "NormalSummonLimit", "DefenderHasMonsters",
        "EffectNotFound", "InvalidTarget", "InvalidTargetCount", "UnsupportedEffect",
        "InvalidEffect", "InvalidTransformation", "TriggerLimitReached"};
    return names[static_cast<int>(error)];
}
Json Cards(GameState const& state, std::vector<CardInstance> const& cards)
{
    Json result = Json::array();
    for (auto const& card : cards)
    {
        auto definition = FindDefinition(state, card.CurrentDefinitionId);
        result.push_back({{"Id", card.Id}, {"DefinitionId", card.CurrentDefinitionId},
            {"Name", definition ? definition->Name : ""}, {"Attack", card.Attack}, {"Hp", card.Hp},
            {"MaxHp", card.MaxHp}, {"HasAttacked", card.HasAttackedThisTurn}});
    }
    return result;
}
}
Json MatchSession::Message(std::string const& type, std::string const& request) const
{
    return {{"Type", type}, {"ProtocolVersion", 1}, {"MatchId", "local-1"},
        {"RequestId", request}, {"SequenceNumber", sequence}};
}
Json MatchSession::Snapshot(int viewer) const
{
    Json players = Json::array();
    if (started) for (auto const& player : state.Players)
        players.push_back({{"PlayerId", player.Id}, {"Life", player.Life},
            {"HandCount", player.Hand.size()}, {"DeckCount", player.Deck.size()},
            {"Hand", player.Id == viewer ? Cards(state, player.Hand) : Json::array()},
            {"Field", Cards(state, player.Field)}, {"Graveyard", Cards(state, player.Graveyard)}});
    return {{"Started", started}, {"Players", players}, {"ActivePlayerId", state.ActivePlayerId},
        {"TurnNumber", state.TurnNumber}, {"TurnStarted", state.TurnStarted}, {"Phase", static_cast<int>(state.Phase)},
        {"Finished", state.Status == GameStatus::Finished}, {"WinnerPlayerId", state.WinnerPlayerId}};
}
void MatchSession::SendSnapshot(std::shared_ptr<ClientSession> const& client, std::string const& request) const
{
    auto message = Message("StateSnapshot", request);
    message["Payload"] = Snapshot(client->Player);
    client->Send(message);
}
void MatchSession::Broadcast(std::vector<GameEvent> const& events)
{
    for (int i = 0; i < 2; ++i) if (auto client = seats[i].Client.lock())
    {
        auto message = Message("GameEvents");
        Json visible = Json::array();
        // Only explicitly public fields leave the server. Core event objects are never serialized wholesale.
        for (auto e : EventsForPlayer(events, i + 1))
        {
            if (!VisibleCard(state, i + 1, e.SourceCardId)) e.SourceCardId = 0;
            if (!VisibleCard(state, i + 1, e.TargetCardId)) e.TargetCardId = 0;
            if (e.SourceCardId == 0 && e.TargetCardId == 0) e.CurrentDefinitionId = 0;
            visible.push_back({{"Type", static_cast<int>(e.Type)}, {"SourcePlayerId", e.SourcePlayerId},
                {"SourceCardId", e.SourceCardId}, {"TargetPlayerId", e.TargetPlayerId},
                {"TargetCardId", e.TargetCardId}, {"DefinitionId", e.CurrentDefinitionId},
                {"Damage", e.Damage}, {"HpAfter", e.HpAfter}, {"LifeAfter", e.LifeAfter}});
        }
        message["Payload"] = visible;
        client->Send(message);
        SendSnapshot(client);
        if (state.Status == GameStatus::Finished)
        {
            auto ended = Message("MatchEnded");
            ended["WinnerPlayerId"] = state.WinnerPlayerId;
            ended["EndReason"] = static_cast<int>(state.EndReason);
            client->Send(ended);
        }
    }
}
void MatchSession::Disconnect(std::shared_ptr<ClientSession> const& client)
{
    if (client->Player > 0 && seats[client->Player - 1].Client.lock() == client)
        seats[client->Player - 1].Client.reset();
    client->Player = 0;
}
void MatchSession::Receive(std::shared_ptr<ClientSession> const& client, std::string const& message)
{
    std::string request;
    try
    {
        if (message.size() > 16384) throw std::runtime_error("MessageTooLarge");
        std::map<int, std::set<std::string>> keys;
        auto input = Json::parse(message, [&](int depth, Json::parse_event_t event, Json& value) {
            if (depth > 32) throw std::runtime_error("JsonDepth");
            if (event == Json::parse_event_t::object_start) keys[depth + 1].clear();
            if (event == Json::parse_event_t::key && !keys[depth].insert(value.get<std::string>()).second)
                throw std::runtime_error("DuplicateField");
            return true;
        });
        if (input.contains("RequestId")) request = String(input, "RequestId", 64);
        Handle(client, input);
    }
    catch (std::exception const& error)
    {
        auto response = Message("ServerError", request);
        // No incoming JSON, tokens, parser diagnostics or private state in error output.
        response["Reason"] = "InvalidRequest";
        client->Send(response);
        (void)error;
    }
}
void MatchSession::Handle(std::shared_ptr<ClientSession> const& client, Json const& input)
{
    Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "PlayerId", "SequenceNumber", "Command", "SessionToken"});
    if (Number(input, "ProtocolVersion", 1) != 1) throw std::runtime_error("ProtocolVersion");
    auto type = String(input, "Type");
    auto request = String(input, "RequestId", 64);
    auto reject = [&](std::string const& reason) {
        auto response = Message(type == "SubmitCommand" ? "CommandRejected" : "ServerError", request);
        response["Reason"] = reason; client->Send(response);
    };
    if (type == "JoinMatch")
    {
        Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "SessionToken"});
        if (input.contains("MatchId") && String(input, "MatchId") != "local-1") { reject("WrongMatch"); return; }
        if (client->Player) { reject("AlreadyJoined"); return; }
        int seat = -1;
        if (input.contains("SessionToken"))
        {
            auto token = String(input, "SessionToken", 64);
            for (int i = 0; i < 2; ++i) if (!seats[i].Token.empty() && seats[i].Token == token) seat = i;
            if (seat < 0) { reject("InvalidSession"); return; }
            if (!seats[seat].Client.expired()) { reject("SessionConnected"); return; }
        }
        else
        {
            for (int i = 0; i < 2; ++i) if (seats[i].Token.empty()) { seat = i; break; }
            if (seat < 0) { reject("MatchFull"); return; }
            seats[seat].Token = Token();
        }
        client->Player = seat + 1;
        seats[seat].Client = client;
        auto joined = Message("MatchJoined", request);
        joined["PlayerId"] = client->Player; joined["SessionToken"] = seats[seat].Token;
        client->Send(joined);
        if (!started && !seats[0].Token.empty() && !seats[1].Token.empty())
        {
            GameConfig config; config.InitialLife = 3; config.InitialHandSize = 1; config.AllowManualDraw = false;
            std::vector<CardDefinition> definitions{
                {.Id = 10, .Name = "Test Attacker", .Attack = 3, .MaxHp = 5},
                {.Id = 20, .Name = "Test Defender", .Attack = 4, .MaxHp = 3}};
            std::array<PlayerSetup, 2> players{PlayerSetup{1, {10,10}}, PlayerSetup{2, {20,20}}};
            if (InitializeGame(state, config, definitions, players, 1) != InitializationResult::Success)
                throw std::runtime_error("Initialization");
            auto result = ProcessCommand(state, StartTurnCommand{1});
            if (result.Code != CommandError::Success) throw std::runtime_error("Initialization");
            started = true; ++sequence;
            for (auto& entry : seats) if (auto c = entry.Client.lock()) c->Send(Message("MatchStarted"));
            Broadcast(result.Events);
        }
        else SendSnapshot(client, request);
        return;
    }
    if (client->Player < 1 || seats[client->Player - 1].Client.lock() != client) { reject("NotJoined"); return; }
    if (String(input, "MatchId") != "local-1") { reject("WrongMatch"); return; }
    if (input.contains("PlayerId") && Number(input, "PlayerId", 2) != static_cast<unsigned>(client->Player))
    { reject("PlayerMismatch"); return; }
    if (type == "StateResyncRequest")
    {
        Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "PlayerId"});
        SendSnapshot(client, request); return;
    }
    if (type != "SubmitCommand") { reject("UnknownMessage"); return; }
    Fields(input, {"Type", "ProtocolVersion", "RequestId", "MatchId", "PlayerId", "SequenceNumber", "Command"});
    auto& requests = seats[client->Player - 1].Requests;
    auto fingerprint = input.dump();
    if (auto previous = requests.find(request); previous != requests.end())
    {
        if (previous->second.first != fingerprint) { reject("RequestIdConflict"); return; }
        client->Send(previous->second.second); SendSnapshot(client); return;
    }
    if (requests.size() >= 4096) { reject("RequestLimit"); return; }
    auto cacheReject = [&](std::string const& reason) {
        auto response = Message("CommandRejected", request); response["Reason"] = reason;
        requests.emplace(request, std::make_pair(fingerprint, response)); client->Send(response);
    };
    if (!started) { cacheReject("MatchNotStarted"); return; }
    if (Number(input, "SequenceNumber", 9007199254740991ULL) != sequence) { cacheReject("StaleSequence"); return; }
    auto const& command = input.at("Command");
    auto name = String(command, "Type");
    PlayerId player = client->Player;
    PlayerId opponent = player == 1 ? 2 : 1;
    GameCommand parsed;
    if (name == "PlayCard") { Fields(command, {"Type", "CardId"}); parsed = PlayCardCommand{player, static_cast<CardInstanceId>(Number(command, "CardId", 2147483647))}; }
    else if (name == "StartTurn") { Fields(command, {"Type"}); parsed = StartTurnCommand{player}; }
    else if (name == "EndTurn") { Fields(command, {"Type"}); parsed = EndTurnCommand{player}; }
    else if (name == "AdvancePhase") { Fields(command, {"Type"}); parsed = AdvancePhaseCommand{player}; }
    else if (name == "Attack" || name == "DirectAttack")
    {
        Fields(command, name == "Attack" ? std::initializer_list<char const*>{"Type", "CardId", "TargetCardId"} : std::initializer_list<char const*>{"Type", "CardId"});
        auto card = static_cast<CardInstanceId>(Number(command, "CardId", 2147483647));
        if (name == "Attack") parsed = AttackCommand{player, card, opponent, static_cast<CardInstanceId>(Number(command, "TargetCardId", 2147483647))};
        else parsed = DirectAttackCommand{player, card, opponent};
    }
    else { cacheReject("CommandNotAllowed"); return; }
    auto result = ProcessCommand(state, parsed);
    if (result.Code != CommandError::Success)
    { cacheReject("GameRule:" + RuleReason(result.Code) + ":Battle:" + std::to_string(static_cast<int>(result.Result))); return; }
    ++sequence;
    auto response = Message("CommandAccepted", request);
    requests.emplace(request, std::make_pair(fingerprint, response));
    client->Send(response);
    Broadcast(result.Events);
}
}
