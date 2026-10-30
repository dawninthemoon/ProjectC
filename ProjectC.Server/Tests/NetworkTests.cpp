#include "MatchSession.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"
#include <iostream>
#include <stdexcept>
using namespace ProjectC::Network;
void Check(bool condition) { if (!condition) throw std::runtime_error("Network assertion failed"); }
int main()
{
    MatchSession match;
    std::vector<Json> a, b, c;
    auto first = std::make_shared<ClientSession>(); first->Send = [&](auto const& m) { a.push_back(m); };
    auto second = std::make_shared<ClientSession>(); second->Send = [&](auto const& m) { b.push_back(m); };
    auto third = std::make_shared<ClientSession>(); third->Send = [&](auto const& m) { c.push_back(m); };
    Json join{{"Type", "JoinMatch"}, {"ProtocolVersion", 1}, {"RequestId", "join"}};
    match.Receive(first, join.dump()); auto token = a.front().at("SessionToken");
    match.Receive(second, join.dump()); match.Receive(third, join.dump());
    Check(first->Player == 1 && second->Player == 2 && third->Player == 0);
    Check(c.back().at("Reason") == "MatchFull");
    auto s1 = match.Snapshot(1), s2 = match.Snapshot(2);
    Check(s1["Players"][1]["Hand"].empty() && s2["Players"][0]["Hand"].empty());
    Check(s1.dump().find("Test Defender") == std::string::npos);
    Check(s2.dump().find("Test Attacker") == std::string::npos);
    Check(!s1["Players"][0].contains("Deck") && s1["Players"][0]["Hand"].size() == 1);
    Json command{{"Type", "SubmitCommand"}, {"ProtocolVersion", 1}, {"RequestId", "play"},
        {"MatchId", "local-1"}, {"SequenceNumber", 1}, {"Command", {{"Type", "PlayCard"}, {"CardId", 1}}}};
    match.Receive(first, command.dump()); Check(match.Snapshot(1)["Players"][0]["Field"].size() == 1);
    auto const& publicEvents = b[b.size() - 2];
    Check(publicEvents["Type"] == "GameEvents");
    Check(publicEvents["Payload"][0]["TargetCardId"] == 0 && publicEvents["Payload"][0]["DefinitionId"] == 0);
    Check(publicEvents["Payload"][1]["TargetCardId"] == 1 && publicEvents["Payload"][1]["DefinitionId"] == 10);
    auto after = match.Snapshot(1); match.Receive(first, command.dump()); Check(after == match.Snapshot(1));
    command["Command"]["CardId"] = 2; match.Receive(first, command.dump()); Check(a.back()["Reason"] == "RequestIdConflict");
    command["RequestId"] = "stale"; match.Receive(first, command.dump()); Check(a.back()["Reason"] == "StaleSequence");
    command["SequenceNumber"] = 2; command["RequestId"] = "internal"; command["Command"] = {{"Type", "Transform"}};
    match.Receive(first, command.dump()); Check(a.back()["Reason"] == "CommandNotAllowed");
    command["RequestId"] = "spoof"; command["PlayerId"] = 2; match.Receive(first, command.dump()); Check(a.back()["Reason"] == "PlayerMismatch");
    for (auto text : {"{", "{}", "[]", "null", "{\"Type\":2}"}) { match.Receive(first, text); Check(a.back()["Type"] == "ServerError"); }
    match.Disconnect(first); join["SessionToken"] = token;
    match.Receive(third, join.dump()); Check(third->Player == 1 && match.Snapshot(1) == after);
    // Network remains a thin authority adapter; core replay must remain deterministic.
    using namespace ProjectC::GameCore;
    GameState initial; GameConfig config; config.InitialHandSize = 1;
    Check(InitializeGame(initial, config, {{.Id=10,.Name="Test",.Attack=3,.MaxHp=5}},
        {PlayerSetup{1,{10,10}},PlayerSetup{2,{10,10}}},1) == InitializationResult::Success);
    std::vector<GameCommand> commands{StartTurnCommand{1},PlayCardCommand{1,1},AdvancePhaseCommand{1}};
    auto replay = ReplayCommands(initial, commands); auto live = initial;
    std::vector<CommandResult> results; for (auto const& cmd : commands) results.push_back(ProcessCommand(live,cmd));
    Check(live == replay.FinalState && results == replay.Results);
    std::cout << "Protocol, session, privacy, reconnect and replay tests passed\n";
}
