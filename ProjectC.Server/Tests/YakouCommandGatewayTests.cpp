#include "YakouCommandGateway.hpp"
#include "CardCatalog.hpp"

#include <algorithm>
#include <array>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <vector>

using namespace ProjectC::Network;
namespace
{
void Check(bool ok, char const* message)
{
    if (!ok) throw std::runtime_error(message);
}

std::vector<int> OrderedDeck(YakouPrototype const& proto, std::vector<int> opening)
{
    auto deck = proto.Decks.at("Decks").at(0).at("Cards").get<std::vector<int>>();
    for (int id : opening)
    {
        auto it = std::find(deck.begin(), deck.end(), id);
        Check(it != deck.end(), "Required fixture card is missing from authored deck");
        deck.erase(it);
    }
    opening.insert(opening.end(), deck.begin(), deck.end());
    return opening;
}
std::vector<int> OrderedSampleDeck(YakouPrototype const& proto, std::size_t index,
                                   std::vector<int> opening)
{
    auto deck = proto.Decks.at("Decks").at(index).at("Cards").get<std::vector<int>>();
    for (int id : opening)
    {
        auto it = std::find(deck.begin(), deck.end(), id);
        Check(it != deck.end(), "Required sample card is missing");
        deck.erase(it);
    }
    opening.insert(opening.end(), deck.begin(), deck.end());
    return opening;
}

YakouCommandGateway NewGateway(YakouPrototype const& proto)
{
    return YakouCommandGateway(proto, 1,
        {OrderedDeck(proto, {2001, 2003, 2009, 2002, 3001}),
         OrderedDeck(proto, {2001, 2002, 2005, 2009, 3001})});
}
}
int main()
{
    try
    {
        auto proto = YakouPrototype::Load(
            std::filesystem::path(DefaultDataDirectory()) / "YakouPrototype");
        {
            YakouCommandGateway alpha(proto, 1,
                {OrderedSampleDeck(proto, 2, {4008, 4012, 4009, 4013}),
                 OrderedSampleDeck(proto, 0, {})});
            int const first = alpha.Match().Player(1).Hand[0];
            int const spell = alpha.Match().Player(1).Hand[1];
            int const second = alpha.Match().Player(1).Hand[2];
            int const cost = alpha.Match().Player(1).Hand[3];
            auto run = [&](int seat, Json command) {
                Check(alpha.Execute(seat, command).at("Accepted") == true,
                      "Expected Alpha 01 wire command to succeed");
            };
            run(1, {{"Type", "StartTurn"}});
            run(1, {{"Type", "Summon"}, {"CardId", first}, {"Method", "Normal"},
                    {"Materials", Json::array()}, {"Slot", 0}});
            run(1, {{"Type", "BeginRaid"}});
            run(2, {{"Type", "StartTurn"}});
            run(2, {{"Type", "BeginRaid"}});
            run(1, {{"Type", "StartTurn"}});
            run(1, {{"Type", "Summon"}, {"CardId", second}, {"Method", "Normal"},
                    {"Materials", Json::array()}, {"Slot", 1}});
            run(1, {{"Type", "ActivateMainSpell"}, {"CardId", spell},
                    {"Costs", Json::array({cost})}});
            auto const before = alpha.Match().ServerStateHash();
            auto rejected = alpha.Execute(1, {{"Type", "ChooseEffect"},
                {"TargetIds", Json::array({first, first})}});
            Check(rejected.at("Accepted") == false &&
                      alpha.Match().ServerStateHash() == before,
                  "Duplicate multi-target wire command must reject atomically");
            rejected = alpha.Execute(2, {{"Type", "ChooseEffect"},
                {"TargetIds", Json::array({first, second})}});
            Check(rejected.at("Accepted") == false &&
                      alpha.Match().ServerStateHash() == before,
                  "Opponent cannot submit private multi-target choice");
            run(1, {{"Type", "ChooseEffect"},
                    {"TargetIds", Json::array({first, second})}});
            Check(alpha.Match().Player(1).Field[0]->Countdown == 0 &&
                      alpha.Match().Player(1).Field[1]->Countdown == 0,
                  "Two-target wire choice must update both selected units");
        }
        auto gateway = NewGateway(proto);
        {
            YakouCommandGateway paid(proto, 1,
                {OrderedDeck(proto, {3012, 2001}), OrderedDeck(proto, {2001})});
            auto const hand = paid.Match().Player(1).Hand;
            Check(paid.Execute(1, {{"Type", "StartTurn"}}).at("Accepted").get<bool>(), "Paid-spell fixture must start");
            auto const before = paid.Match().ServerStateHash();
            auto const invalid = paid.Execute(1, {{"Type", "ActivateMainSpell"}, {"CardId", hand[0]},
                                                   {"Costs", Json::array({hand[0]})}});
            Check(!invalid.at("Accepted").get<bool>() && paid.Match().ServerStateHash() == before,
                  "A spell cannot discard itself and failed payment must roll back");
            auto const success = paid.Execute(1, {{"Type", "ActivateMainSpell"}, {"CardId", hand[0]},
                                                   {"Costs", Json::array({hand[1]})}});
            Check(success.at("Accepted").get<bool>() &&
                      success.at("Snapshot").at("PendingSelection").at("CandidateCards").size() == 3 &&
                      !paid.Snapshot(2).contains("PendingSelection"),
                  "Paid spell must expose deck candidates only to its controller");
        }
        auto first = gateway.Snapshot(1);
        auto second = gateway.Snapshot(2);
        Check(first.at("Players").at(0).at("Hand").size() == 5 &&
              second.at("Players").at(0).at("Hand").empty() &&
              second.at("Players").at(1).at("Hand").size() == 5 &&
              first.at("Players").at(1).at("Hand").empty(),
              "Viewer snapshots must keep opponent hand identities private");
        auto const firstHand = gateway.Match().Player(1).Hand;
        auto const handHash = gateway.Match().ServerStateHash();
        auto spoof = gateway.Execute(2,
            {{"Type", "Summon"}, {"CardId", firstHand[0]}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 0}});
        Check(!spoof.at("Accepted").get<bool>() &&
              spoof.at("Reason") == "Rejected" &&
              gateway.Match().ServerStateHash() == handHash,
              "Opponent cannot summon a card from another player's hand");
        auto forged = gateway.Execute(1,
            {{"Type", "StartTurn"}, {"Player", 2}});
        Check(!forged.at("Accepted").get<bool>() &&
              gateway.Match().ServerStateHash() == handHash,
              "Client Player field must be rejected, not trusted");

        auto started = gateway.Execute(1, {{"Type", "StartTurn"}});
        Check(started.at("Accepted").get<bool>() &&
              started.at("Snapshot").at("Phase") == "Main",
              "Authenticated seat can begin its own turn");
        auto const beforeSummon = gateway.Match().ServerStateHash();
        auto invalid = gateway.Execute(1,
            {{"Type", "Summon"}, {"CardId", firstHand[0]}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 999}});
        Check(!invalid.at("Accepted").get<bool>() &&
              gateway.Match().ServerStateHash() == beforeSummon &&
              invalid.at("Snapshot").at("Players").at(1).at("Hand").empty(),
              "Invalid slot must reject atomically without leaking enemy hand");
        invalid = gateway.Execute(1,
            {{"Type", "Summon"}, {"CardId", firstHand[0]}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 0}, {"MaxEffects", 1024}});
        Check(!invalid.at("Accepted").get<bool>() &&
              gateway.Match().ServerStateHash() == beforeSummon,
              "Client must not set hidden scheduler budget or unexpected fields");

        auto const deckBefore = gateway.Match().Player(1).Deck.size();
        auto summoned = gateway.Execute(1,
            {{"Type", "Summon"}, {"CardId", firstHand[0]}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 0}});
        Check(summoned.at("Accepted").get<bool>() &&
              gateway.Match().PendingEffects().empty() &&
              gateway.Match().Player(1).Deck.size() == deckBefore - 2 &&
              gateway.Match().Player(1).Graveyard.size() == 2 &&
              summoned.at("Snapshot").at("Players").at(0).at("FieldSlots").at(0).at("Id") == firstHand[0],
              "Summon gateway must run authored Mill automatically");
        auto const afterSummon = gateway.Match().ServerStateHash();
        auto repeated = gateway.Execute(1,
            {{"Type", "Summon"}, {"CardId", firstHand[0]}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 1}});
        Check(!repeated.at("Accepted").get<bool>() &&
              gateway.Match().ServerStateHash() == afterSummon,
              "Duplicate summoned instance must fail without state mutation");

        auto const deckBeforeSpell = gateway.Match().Player(1).Deck.size();
        auto spell = gateway.Execute(1, {{"Type", "ActivateMainSpell"}, {"CardId", firstHand[2]}});
        Check(spell.at("Accepted").get<bool>() &&
              gateway.Match().Player(1).Deck.size() == deckBeforeSpell - 2 &&
              gateway.Match().PendingEffects().empty(),
              "Main spell gateway must apply its automatic OnResolve effect");

        auto const other = gateway.Snapshot(2);
        Check(other.at("Players").at(0).at("Hand").empty() &&
              other.at("Players").at(0).at("HandCount") ==
                  gateway.Match().Player(1).Hand.size(),
              "Opponent must see only the hand count, not identities");
        bool invalidSeatRejected = false;
        try { (void)gateway.Execute(3, {{"Type", "StartTurn"}}); }
        catch (std::invalid_argument const&) { invalidSeatRejected = true; }
        Check(invalidSeatRejected, "Unbound server seat must fail closed");

        // A second match proves choice authority and that an unresolved
        // selected effect does not silently force a target for the controller.
        auto choiceProto = proto;
        for (auto& id : choiceProto.Decks["Decks"][0]["Cards"])
            if (id == 2003) { id = 3002; break; }
        YakouCommandGateway choices(choiceProto, 1,
            {OrderedDeck(choiceProto, {2001, 3002, 2009, 2002, 3001}),
             OrderedDeck(choiceProto, {2001, 2002, 2005, 2009, 3001})});
        Check(choices.Execute(1, {{"Type", "StartTurn"}}).at("Accepted") == true,
              "Second match should start");
        auto const id = choices.Match().Player(1).Hand[1]; // 3002: Normal + mandatory FriendlyMonster healing
        auto pending = choices.Execute(1,
            {{"Type", "Summon"}, {"CardId", id}, {"Method", "Normal"},
             {"Materials", Json::array()}, {"Slot", 0}});
        Check(pending.at("Accepted") == true &&
              choices.Match().PendingEffects().size() == 1 &&
              pending.at("Snapshot").contains("PendingSelection"),
              "Targeted summon effect must remain pending for owner choice");
        auto choiceHash = choices.Match().ServerStateHash();
        auto wrong = choices.Execute(2, {{"Type", "ChooseEffect"}, {"TargetId", id}});
        Check(wrong.at("Accepted") == false &&
              choices.Match().ServerStateHash() == choiceHash,
              "Only the trigger controller may submit its target");
        std::cout << "Yakou authenticated command boundary and snapshot isolation passed\n";
        return 0;
    }
    catch (std::exception const& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
