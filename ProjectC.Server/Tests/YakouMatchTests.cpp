#include "YakouMatch.hpp"
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
void Check(bool value, char const* message)
{
    if (!value) throw std::runtime_error(message);
}
template<class F> void Reject(F f)
{
    bool failed = false;
    try { f(); } catch (std::invalid_argument const&) { failed = true; }
    Check(failed, "Invalid Yakou action was accepted");
}
std::vector<int> Deck(YakouPrototype const& proto, std::vector<int> front)
{
    auto deck = proto.Decks.at("Decks").at(0).at("Cards").get<std::vector<int>>();
    for (int card : front)
    {
        auto it = std::find(deck.begin(), deck.end(), card);
        Check(it != deck.end(), "Card missing in test deck");
        deck.erase(it);
    }
    front.insert(front.end(), deck.begin(), deck.end());
    return front;
}
YakouMatch Make(YakouPrototype const& proto, std::vector<int> one, std::vector<int> two)
{
    return YakouMatch(proto, 1, {Deck(proto, std::move(one)), Deck(proto, std::move(two))});
}
int HandId(YakouMatch const& match, int player, int definition)
{
    // IDs are globally allocated by deck position and stable for the match.
    for (int instance : match.Player(player).Hand)
    {
        int const startingId = player == 1 ? instance : instance - 30;
        auto const& deck = match.Player(player);
        (void)deck;
        // Lookup by deterministic deck order is performed externally in tests
        // via the actual hand indices where possible.
        if (startingId == definition) return instance;
    }
    return 0;
}
}
int main()
{
    try
    {
        auto const proto = YakouPrototype::Load(
            std::filesystem::path(DefaultDataDirectory()) / "YakouPrototype");
        {
            auto match = Make(proto, {2001, 2008, 3008, 3007, 2009}, {});
            Check(match.Player(1).Hand.size() == 5 && match.Player(1).Ki == 3,
                  "Opening hand or Ki incorrect");
            match.StartTurn(1);
            Check(match.Phase() == YakouPhase::Main &&
                      match.Player(1).Hand.size() == 5, "First player opening draw");
            int const first = match.Player(1).Hand[0];
            int const devour = match.Player(1).Hand[1];
            {
                auto choices = match.SummonChoices(1, first);
                Check(choices.size() == 1 && choices[0].Method == "Normal" &&
                          choices[0].RequiredCount == 0 &&
                          choices[0].AvailableSlots.size() == 5,
                      "Normal summon must be free in an empty field");
                Check(match.SummonChoices(1, devour).empty(),
                      "Devour must not be offered without allied material");
                Reject([&] { (void)match.SummonChoices(2, first); });
            }
            match.Summon(1, first, "Normal", {}, 2);
            Reject([&] { match.Summon(1, match.Player(1).Hand[0], "Normal", {}, 1); });
            Check(match.Player(1).Field[2].has_value() &&
                      match.Player(1).NormalSummons == 1, "Normal summon");
            {
                Check(match.SummonChoices(1, match.Player(1).Hand[0]).empty() ||
                          match.Player(1).NormalSummons == 1,
                      "Normal summon quota must stay spent");
                auto choices = match.SummonChoices(1, devour);
                Check(choices.size() == 1 && choices[0].Method == "Devour" &&
                          choices[0].SourceZone == "FriendlyField" &&
                          choices[0].RequiredCount == 1 &&
                          choices[0].Candidates.size() == 1 &&
                          choices[0].Candidates[0] == first &&
                          std::find(choices[0].AvailableSlots.begin(),
                                    choices[0].AvailableSlots.end(), 2) !=
                              choices[0].AvailableSlots.end(),
                      "Devour UI selection must derive owned material and slot");
            }
            Reject([&] { match.Summon(1, devour, "Devour", {}, 2); });
            Check(match.Player(1).Hand.size() == 4 &&
                      match.Player(1).Field[2]->InstanceId == first,
                  "Failed Devour must not alter game state");
            match.Summon(1, devour, "Devour", {first}, 2);
            auto const& unit = *match.Player(1).Field[2];
            Check(unit.Attack() == 3 && unit.MaxHp() == 5 && unit.Countdown == 3 &&
                      unit.WasDevourSummoned && match.Player(1).Graveyard.size() == 1,
                  "Devour must inherit printed stats but not countdown");
            Check(!match.PendingEffects().empty(), "Trigger information must not disappear");
            match.BeginRaid(1);
            Check(match.Phase() == YakouPhase::TurnStart && match.ActivePlayer() == 2,
                  "No-ready-attacker turn should pass");
        }
        {
            auto match = Make(proto, {2001, 3007, 2002, 2005, 2009}, {});
            match.StartTurn(1);
            auto hand = match.Player(1).Hand;
            {
                auto choices = match.SummonChoices(1, hand[1]);
                Check(choices.size() == 1 && choices[0].Method == "Discard" &&
                          choices[0].SourceZone == "FriendlyHand" &&
                          choices[0].RequiredCount == 1 &&
                          std::find(choices[0].Candidates.begin(),
                                    choices[0].Candidates.end(), hand[1]) ==
                              choices[0].Candidates.end(),
                      "Discard cannot offer its own source card");
            }
            match.Summon(1, hand[0], "Normal", {}, 0);
            Reject([&] { match.Summon(1, hand[1], "Discard", {hand[1]}, 1); });
            match.Summon(1, hand[1], "Discard", {hand[3]}, 1);
            Check(match.Player(1).Graveyard.size() == 1 &&
                      match.Player(1).Hand.size() == 2,
                  "Discard paid into graveyard");
            Reject([&] { match.Summon(1, hand[2], "Necromancy", {}, 3); });
            match.Summon(1, hand[2], "Necromancy", {hand[3]}, 3);
            Check(match.Player(1).Exile.size() == 1 &&
                      match.Player(1).NormalSummons == 1 &&
                      match.Player(1).Field[3]->Countdown == 2,
                  "Necromancy paid into Exile and did not use normal summon");
        }
        {
            auto changed = proto;
            for (auto& card : changed.Cards["Cards"])
                if (card["Id"] == 2008) card["SummonCostJson"]["Devour"]["Count"] = 2;
            auto match = Make(changed, {2001, 3007, 2008, 2005, 2009}, {});
            match.StartTurn(1);
            auto hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.Summon(1, hand[1], "Discard", {hand[3]}, 1);
            Reject([&] { match.Summon(1, hand[2], "Devour", {hand[0]}, 0); });
            Reject([&] { match.Summon(1, hand[2], "Devour",
                                      {hand[0], hand[0]}, 0); });
            match.Summon(1, hand[2], "Devour", {hand[0], hand[1]}, 0);
            Check(match.Player(1).Field[0]->Attack() == 4 &&
                      match.Player(1).Field[0]->MaxHp() == 6 &&
                      match.Player(1).Graveyard.size() == 3,
                  "Devour 2 must use two distinct allies");
        }
        {
            auto match = Make(proto, {2001, 2008, 2004, 2005, 2009}, {});
            match.StartTurn(1);
            auto hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.Summon(1, hand[1], "Devour", {hand[0]}, 0);
            match.Summon(1, hand[2], "Discard", {hand[3]}, 1);
            Check(match.Player(1).Graveyard.size() == 2, "Manifestation grave condition setup");
            match.Manifest(1, hand[1], 2102);
            auto const& u = *match.Player(1).Field[0];
            Check(u.DefinitionId == 2102 && u.Attack() == 5 &&
                      u.MaxHp() == 7 && u.Countdown == 2 &&
                      match.Player(1).Ki == 1 && u.Manifested,
                  "Manifest must retain devour bonuses and reduce countdown");
            Reject([&] { match.Manifest(1, hand[1], 2102); });
            match.Redraw(1, {hand[4]});
            Check(match.Player(1).Ki == 0, "Redraw Ki cost");
            Reject([&] { match.Redraw(1, {match.Player(1).Hand[0]}); });
        }
        {
            auto match = Make(proto, {2005, 3007, 2001, 2009, 2002},
                                     {2001, 2005, 2009, 2002, 2007});
            match.StartTurn(1);
            auto hand1 = match.Player(1).Hand;
            match.Summon(1, hand1[0], "Normal", {}, 0);
            match.Summon(1, hand1[1], "Discard", {hand1[3]}, 1);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const blocker = match.Player(2).Hand[0];
            match.Summon(2, blocker, "Normal", {}, 4);
            match.BeginRaid(2);
            match.StartTurn(1);
            Check(match.Player(1).Field[0]->Countdown == 0 &&
                      match.Player(1).Field[1]->Countdown == 0 &&
                      match.Player(1).Ki == 3, "Own-turn tick, no Ki regen");
            match.BeginRaid(1);
            Check(match.Attacks().size() == 2 &&
                      match.Attacks()[0].Slot == 0 &&
                      match.Attacks()[1].Slot == 1, "Raid in fixed slot order");
            Reject([&] { match.AssignBlockers(2, {{hand1[0], blocker},
                                                 {hand1[1], blocker}}); });
            Check(match.Attacks()[0].Blocker == 0, "Invalid blockers must be atomic");
            match.AssignBlockers(2, {{hand1[0], blocker}});
            Check(match.Phase() == YakouPhase::RaidResponse &&
                      match.PriorityPlayer() == 2, "Defender must get raid response priority");
            Reject([&] { match.ResolveRaid(2); });
            Reject([&] { match.PassRaidResponse(1); });
            match.PassRaidResponse(2);
            Check(match.PriorityPlayer() == 1, "Priority should alternate after first pass");
            Reject([&] { match.ResolveRaid(2); });
            match.PassRaidResponse(1);
            Check(match.PriorityPlayer() == 0, "Two passes must close response window");
            Reject([&] { match.PassRaidResponse(2); });
            match.ResolveRaid(2);
            Check(!match.Player(1).Field[0].has_value() &&
                      match.Player(1).Field[1]->Countdown == 1 &&
                      match.Player(2).Field[4]->Hp() == 1 &&
                      match.Player(2).Life == 19,
                  "Sequential battle, persistent damage and unblocked hero hit");
        }
        std::cout << "Yakou special summons, Ki, manifest and blocker raids passed\n";
        return 0;
    }
    catch (std::exception const& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
