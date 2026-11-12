#include "YakouMatch.hpp"
#include "YakouReplay.hpp"
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
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2005, 2001, 2009, 3001, 3007}), Deck(proto, {2001, 2002})};
            auto command = [](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand value;
                value.Operation = operation;
                value.Player = player;
                return value;
            };
            std::vector<YakouReplayCommand> commands;
            commands.push_back(command(YakouReplayOperation::StartTurn, 1));
            auto summonOne = command(YakouReplayOperation::Summon, 1);
            summonOne.CardId = 1;
            summonOne.Method = "Normal";
            commands.push_back(summonOne);
            commands.push_back(summonOne); // Rejected duplicate: already on field.
            commands.push_back(command(YakouReplayOperation::BeginRaid, 1));
            commands.push_back(command(YakouReplayOperation::StartTurn, 2));
            auto summonTwo = command(YakouReplayOperation::Summon, 2);
            summonTwo.CardId = 31;
            summonTwo.Method = "Normal";
            commands.push_back(summonTwo);
            commands.push_back(command(YakouReplayOperation::ResolvePendingEffect, 2));
            commands.push_back(command(YakouReplayOperation::BeginRaid, 2));
            commands.push_back(command(YakouReplayOperation::StartTurn, 1));
            commands.push_back(command(YakouReplayOperation::BeginRaid, 1));
            auto blockers = command(YakouReplayOperation::AssignBlockers, 2);
            blockers.Blockers = {{1, 31}};
            commands.push_back(blockers);
            commands.push_back(command(YakouReplayOperation::PassRaidResponse, 2));
            std::vector<YakouReplayCommand> const pendingCommands(commands.begin(), commands.begin() + 6);
            auto const pending = ReplayYakou(proto, 1, decks, pendingCommands);
            Check(pending.FinalState.PendingEffects().size() == 1 &&
                      pending.FinalState.PendingEffectChoices(2).Operation == "Mill",
                  "Replaying a partial journal must reconstruct its pending effect");
            Reject([&] { (void)pending.FinalState.PendingEffectChoices(1); });
            auto const mid = ReplayYakou(proto, 1, decks, commands);
            Check(mid.FinalState.PriorityPlayer() == 1 && !mid.Steps[2].Accepted &&
                      mid.Steps[2].BeforeHash == mid.Steps[2].AfterHash,
                  "Replay must preserve raid priority and rejected-command atomicity");
            commands.push_back(command(YakouReplayOperation::PassRaidResponse, 1));
            commands.push_back(command(YakouReplayOperation::ResolveRaid, 2));
            auto const first = ReplayYakou(proto, 1, decks, commands);
            auto const second = ReplayYakou(proto, 1, decks, commands);
            constexpr std::uint64_t expectedReferenceHash = 18184515181999606374ULL;
            Check(first.FinalState.ServerStateHash() == expectedReferenceHash,
                  "Canonical replay fingerprint must agree across supported platforms");
            Check(first.FinalState.ServerStateHash() == second.FinalState.ServerStateHash() &&
                      first.FinalState.ServerStateHash() != mid.FinalState.ServerStateHash() &&
                      first.Steps.size() == commands.size(),
                  "Identical command journals must produce the same full server state hash");
            Check(!first.FinalState.Player(1).Field[0] &&
                      first.FinalState.Player(2).Field[0]->Damage == 1,
                  "Replay must reproduce actual combat and persistent damage");
            auto const privateVariant = ReplayYakou(proto, 1,
                {decks[0], Deck(proto, {2002, 2001})}, {});
            auto const original = ReplayYakou(proto, 1, decks, {});
            Check(privateVariant.FinalState.ViewerSnapshot(1) == original.FinalState.ViewerSnapshot(1) &&
                      privateVariant.FinalState.ServerStateHash() != original.FinalState.ServerStateHash(),
                  "Replay hash must include private state even though viewer snapshots do not");
        }
        {
            auto match = Make(proto, {2001, 2008, 3008, 3007, 2009}, {2002, 2004});
            auto const one = match.ViewerSnapshot(1);
            auto const two = match.ViewerSnapshot(2);
            Check(one.at("Players")[0].at("Hand").size() == 5 &&
                      one.at("Players")[1].at("Hand").empty() &&
                      two.at("Players")[0].at("Hand").empty() &&
                      two.at("Players")[1].at("Hand").size() == 5,
                  "A viewer may see only their own hand identities");
            Check(!one.at("Players")[0].contains("Deck") && !one.at("Players")[1].contains("Deck") &&
                      one.at("Players")[0].at("DeckCount") == 25 &&
                      !one.at("Players")[1].contains("CanNormalSummon") &&
                      one.at("Players")[1].at("FieldSlots").size() == 5,
                  "Neither player's deck order may appear in the viewer snapshot");
            Reject([&] { (void)match.ViewerSnapshot(3); });
            match.StartTurn(1);
            Check(match.ViewerSnapshot(1).at("Players")[0].at("CanNormalSummon") == true,
                  "Owner summon availability must come from actual legal choices");
            int const card = match.Player(1).Hand.front();
            match.Summon(1, card, "Normal", {}, 2);
            Check(match.ViewerSnapshot(1).at("Players")[0].at("CanNormalSummon") == false,
                  "A consumed normal summon must immediately clear availability");
            auto const visible = match.ViewerSnapshot(2);
            Check(visible.at("Players")[0].at("FieldSlots")[0].is_null() &&
                      visible.at("Players")[0].at("FieldSlots")[2].at("Id") == card &&
                      visible.at("Players")[0].at("FieldSlots")[2].at("AttackCounter") == 2,
                  "Public field cards and fixed slots must be projected to either viewer");
        }
        {
            auto match = Make(proto, {}, {});
            for (int step = 0; step < 100 && match.Phase() != YakouPhase::Finished; ++step)
            {
                int const active = match.ActivePlayer();
                match.StartTurn(active);
                if (match.Phase() != YakouPhase::Finished)
                {
                    match.BeginRaid(active);
                }
            }
            Check(match.Phase() == YakouPhase::Finished && match.WinnerPlayer() == 1 &&
                      match.ViewerSnapshot(1).at("WinnerPlayerId") == 1,
                  "Deck exhaustion must finish the game and identify the opposing winner");
        }
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
        {
            // The devoured source is in the graveyard when its effect resolves;
            // its event context must still identify the actual consuming monster.
            auto match = Make(proto, {3008, 2008, 2001, 2009, 2002}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.Summon(1, hand[1], "Devour", {hand[0]}, 0);
            Check(match.PendingEffects().size() == 2 &&
                      match.PendingEffects()[0].SourceCard == hand[0] &&
                      match.PendingEffects()[0].RelatedCard == hand[1],
                  "Devour event must retain the original source and consuming monster");
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "ModifyCountdown" && choice.MaxCount == 0,
                  "The devouring monster is an automatic server-derived target");
            int const before = match.Player(1).Field[0]->Countdown;
            Reject([&] { match.ResolvePendingEffect(1, hand[2]); });
            Check(match.Player(1).Field[0]->Countdown == before && match.PendingEffects().size() == 2,
                  "A spoofed devouring target must not mutate the match");
            match.ResolvePendingEffect(1);
            Check(match.Player(1).Field[0]->Countdown == before - 1 &&
                      match.PendingEffects().front().SourceCard == hand[1],
                  "The graveyard source effect must adjust only its consuming monster");
            match.ResolvePendingEffect(1); // Consuming monster's own authored mill.
            Check(match.PendingEffects().empty(), "Both distinct devour triggers should resolve");
        }
        {
            // A while-on-field passive comes from the defender's authored
            // definition; it permits two distinct attackers in one raid.
            auto changed = proto;
            auto& cards = changed.Decks["Decks"][0]["Cards"];
            for (auto& id : cards)
            {
                if (id == 3020)
                {
                    id = 3010;
                    break;
                }
            }
            auto match = Make(changed, {2005, 3007, 2001, 2009, 2002}, {3010});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.Summon(1, hand[1], "Discard", {hand[3]}, 1);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const blocker = match.Player(2).Hand[0];
            match.Summon(2, blocker, "Normal", {}, 0);
            Check(match.Player(2).Field[0]->BaseMaxBlocks == 2 &&
                      match.Player(2).Field[0]->MaxBlocks == 2,
                  "Printed while-on-field passive must provide two blocks");
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            Reject([&] { match.AssignBlockers(2, {{hand[0], blocker}, {hand[0], blocker}}); });
            Check(match.Attacks()[0].Blocker == 0 && match.Attacks()[1].Blocker == 0,
                  "Rejected duplicate-attacker allocation must not partially apply");
            match.AssignBlockers(2, {{hand[0], blocker}, {hand[1], blocker}});
            Check(match.Attacks()[0].Blocker == blocker && match.Attacks()[1].Blocker == blocker,
                  "Both attackers must be assignable to the two-block defender");
        }
        {
            auto changed = proto;
            auto& cards = changed.Decks["Decks"][0]["Cards"];
            for (auto& id : cards)
            {
                if (id == 3020)
                {
                    id = 3010;
                    break;
                }
            }
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 3010)
                {
                    card["Effects"][0]["Actions"][0]["Target"] = "EnemyMonster";
                }
            }
            auto match = Make(changed, {3010}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            Reject([&] { match.Summon(1, hand[0], "Normal", {}, 0); });
            Check(match.Player(1).Hand == hand && !match.Player(1).Field[0] &&
                      match.Player(1).NormalSummons == 0,
                  "Unsupported passive must reject a summon before any state change");
        }
        {
            // Authoritative effect resolution: two-card mill and targeted timer
            // reduction must apply only after an accepted resolution command.
            auto match = Make(proto, {2001, 3007, 2009, 2002, 3001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            Check(match.PendingEffects().size() == 1, "Summon must enqueue mill");
            auto const priorDeck = match.Player(1).Deck.size();
            auto const priorGrave = match.Player(1).Graveyard.size();
            Reject([&] { match.ResolvePendingEffect(2); });
            Reject([&] { match.ResolvePendingEffect(1, hand[0]); });
            Check(match.Player(1).Deck.size() == priorDeck &&
                      match.PendingEffects().size() == 1,
                  "Rejected mill must be atomic");
            match.ResolvePendingEffect(1);
            Check(match.Player(1).Deck.size() == priorDeck - 2 &&
                      match.Player(1).Graveyard.size() == priorGrave + 2 &&
                      match.PendingEffects().empty(), "Mill must move two cards to graveyard");
            Reject([&] { match.ResolvePendingEffect(1); });

            match.Summon(1, hand[1], "Discard", {hand[2]}, 1);
            Check(match.PendingEffects().size() == 1,
                  "Discard-summoned countdown trigger must be queued");
            int const previous = match.Player(1).Field[0]->Countdown;
            Reject([&] { match.ResolvePendingEffect(1, 999999); });
            Reject([&] { match.ResolvePendingEffect(1, hand[1]); });
            Check(match.Player(1).Field[0]->Countdown == previous &&
                      match.PendingEffects().size() == 1,
                  "Invalid target must leave both game state and queue unchanged");
            match.ResolvePendingEffect(1, hand[0]);
            Check(match.Player(1).Field[0]->Countdown ==
                      std::max(0, previous - 1) && match.PendingEffects().empty(),
                  "Friendly target countdown must decrease and consume trigger");
        }
        {
            // Separately defined cards sharing an authored name key share the
            // same name-scoped effect allowance, even if their IDs differ.
            auto changed = proto;
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 3007)
                {
                    card["NameKey"] = "card_2003_name";
                }
            }
            auto match = Make(changed, {2001, 2003, 3007, 2009, 3001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            match.Summon(1, hand[2], "Discard", {hand[3]}, 1);
            match.ResolvePendingEffect(1, hand[0]);
            auto const grave = match.Player(1).Graveyard;
            match.Summon(1, hand[1], "Necromancy", {grave[0], grave[1]}, 2);
            int const before = match.Player(1).Field[0]->Countdown;
            Reject([&] { (void)match.PendingEffectChoices(1); });
            Reject([&] { match.ResolvePendingEffect(1, hand[0]); });
            Check(match.Player(1).Field[0]->Countdown == before && !match.PendingEffects().empty(),
                  "Shared-name effect limit rejection must leave state and trigger intact");
        }
        {
            // Declining an optional trigger is not an activation and must not
            // consume that printed card name's per-turn effect allowance.
            auto changed = proto;
            auto& cards = changed.Decks["Decks"][0]["Cards"];
            for (auto& id : cards)
            {
                if (id == 3020)
                {
                    id = 3007;
                    break;
                }
            }
            auto match = Make(changed, {3007, 3007, 2001, 2009, 3001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Discard", {hand[2]}, 0);
            match.ResolvePendingEffect(1);
            match.Summon(1, hand[1], "Discard", {hand[3]}, 1);
            match.ResolvePendingEffect(1, hand[0]);
            Check(match.Player(1).Field[0]->Countdown == 0 && match.PendingEffects().empty(),
                  "A declined optional effect must not prevent a later valid activation");
        }
        {
            // Salvage is validated against the authoritative graveyard and the
            // authored monster/archetype filters, not client-supplied card data.
            auto match = Make(proto, {2001, 2002, 3001, 3007, 2004, 2005, 2009}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            int const material = match.Player(1).Graveyard[0];
            int const spell = match.Player(1).Graveyard[1];
            match.Summon(1, hand[3], "Discard", {hand[4]}, 1);
            match.ResolvePendingEffect(1); // Decline optional countdown adjustment.
            match.Summon(1, hand[1], "Necromancy", {material}, 2);
            match.ResolvePendingEffect(1); // Decline the paid card's queued optional trigger.
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "Salvage" && choice.SourceZone == "FriendlyGraveyard" &&
                      choice.MinCount == 1 && choice.MaxCount == 1 &&
                      choice.Candidates == std::vector<int>{hand[4]},
                  "Salvage candidates must match server-authorized graveyard and card filters");
            Reject([&] { (void)match.PendingEffectChoices(2); });
            auto const grave = match.Player(1).Graveyard;
            auto const held = match.Player(1).Hand;
            auto const pending = match.PendingEffects().size();
            Reject([&] { match.ResolvePendingEffect(2, hand[4]); });
            Reject([&] { match.ResolvePendingEffect(1, material); });
            Reject([&] { match.ResolvePendingEffect(1, spell); });
            Check(match.Player(1).Graveyard == grave && match.Player(1).Hand == held &&
                      match.PendingEffects().size() == pending,
                  "Rejected salvage must leave all zones and queued effects unchanged");
            match.ResolvePendingEffect(1, hand[4]);
            Check(std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), hand[4]) !=
                      match.Player(1).Hand.end() &&
                      std::find(match.Player(1).Graveyard.begin(), match.Player(1).Graveyard.end(), hand[4]) ==
                          match.Player(1).Graveyard.end() && match.PendingEffects().empty(),
                  "Valid salvage must move the selected authored monster to hand");
        }
        {
            auto match = Make(proto, {2004, 2001, 2009, 3007, 2002}, {});
            match.StartTurn(1);
            match.Summon(1, match.Player(1).Hand[0], "Normal", {}, 0);
            Check(match.PendingEffects().empty(),
                  "An unmet authored graveyard condition must not enqueue a trigger");
        }
        {
            auto match = Make(proto, {2001, 2004, 2009, 3007, 2002}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            match.Summon(1, hand[1], "Discard", {hand[2]}, 1);
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "Salvage" && choice.MinCount == 0 && choice.MaxCount == 1 &&
                      !choice.Candidates.empty(),
                  "A met graveyard condition must offer optional valid salvage candidates");
            int const selected = choice.Candidates.front();
            match.ResolvePendingEffect(1, selected);
            Check(std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), selected) !=
                      match.Player(1).Hand.end() && match.PendingEffects().empty(),
                  "Conditional salvage must move only the selected legal card");
        }
        {
            auto changed = proto;
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 2004)
                {
                    card["Effects"][0]["Actions"][0]["Condition"]["UnknownRule"] = true;
                }
            }
            auto match = Make(changed, {2004}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            auto const journal = match.Journal();
            Reject([&] { match.Summon(1, hand[0], "Normal", {}, 0); });
            Check(match.Player(1).Hand == hand && !match.Player(1).Field[0] &&
                      match.Player(1).NormalSummons == 0 && match.Journal() == journal,
                  "A late unsupported trigger condition must roll back the entire summon");
        }
        {
            // Future authored costs must not be ignored by a partially
            // implemented operation or its candidate-query path.
            auto changed = proto;
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 2001)
                {
                    card["Effects"][0]["Actions"][0]["OptionalCost"] = {{"Discard", 1}};
                }
            }
            auto match = Make(changed, {2001}, {});
            match.StartTurn(1);
            int const source = match.Player(1).Hand[0];
            match.Summon(1, source, "Normal", {}, 0);
            auto const deck = match.Player(1).Deck;
            Reject([&] { (void)match.PendingEffectChoices(1); });
            Reject([&] { match.ResolvePendingEffect(1); });
            Check(match.Player(1).Deck == deck && match.Player(1).Graveyard.empty() &&
                      match.PendingEffects().size() == 1,
                  "An unsupported authored cost must not be silently omitted");
        }
        {
            // The authored deck-top choice is exercised in a local trigger
            // fixture until spell activation and its response window exist.
            auto changed = proto;
            Json look;
            for (auto const& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 3012)
                {
                    look = card["Effects"][0];
                }
            }
            look["Id"] = 2;
            look["Trigger"] = "OnSummoned";
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 2001)
                {
                    card["Effects"].push_back(look);
                }
            }
            auto match = Make(changed, {2001, 2002, 3001, 3007, 2004}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1); // The original authored mill resolves first.
            auto const deck = match.Player(1).Deck;
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "LookAndChoose" && choice.SourceZone == "FriendlyDeckTop" &&
                      choice.MinCount == 1 && choice.MaxCount == 1 &&
                      choice.Candidates == std::vector<int>({deck[0], deck[1], deck[2]}),
                  "Only the authorized top three cards must be offered");
            Reject([&] { (void)match.PendingEffectChoices(2); });
            auto const enemyProjection = match.ViewerSnapshot(2).dump();
            for (int hidden : choice.Candidates)
            {
                Check(enemyProjection.find("\"Id\":" + std::to_string(hidden)) == std::string::npos,
                      "A deck-top choice must never expose its identities to the opponent");
            }
            Reject([&] { match.ResolvePendingEffect(1, deck[3]); });
            Check(match.Player(1).Deck == deck && match.PendingEffects().size() == 1,
                  "An illegal deck selection must be atomic");
            match.ResolvePendingEffect(1, deck[1]);
            auto const& remaining = match.Player(1).Deck;
            Check(remaining.front() == deck[3] && remaining[remaining.size() - 2] == deck[0] &&
                      remaining.back() == deck[2] && match.Player(1).Hand.back() == deck[1] &&
                      match.PendingEffects().empty(),
                  "Unchosen cards must reach the deck bottom in their original order");
        }
        {
            // Exercise the authored manifestation countdown operation while
            // keeping its original action intact in a local trigger fixture.
            auto changed = proto;
            for (auto& card : changed.Cards["Cards"])
            {
                if (card["Id"] == 2102)
                {
                    card["Effects"][0]["Trigger"] = "OnManifested";
                }
            }
            auto match = Make(changed, {2001, 2008, 2004, 2005, 2009}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            match.Summon(1, hand[1], "Devour", {hand[0]}, 0);
            match.ResolvePendingEffect(1);
            match.Manifest(1, hand[1], 2102);
            Check(match.Player(1).Field[0]->Countdown == 2, "Manifest countdown setup");
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "SetCountdown" && choice.MaxCount == 0,
                  "Self countdown effect must not request an external target");
            Reject([&] { match.ResolvePendingEffect(1, hand[2]); });
            match.ResolvePendingEffect(1);
            Check(match.Player(1).Field[0]->Countdown == 1 && match.PendingEffects().empty(),
                  "Authored set-countdown operation must apply to the same field instance");
        }
        {
            // Damage originates in actual simultaneous combat, then healing
            // reduces accumulated damage without restoring beyond printed HP.
            auto changed = proto;
            auto& cards = changed.Decks["Decks"][0]["Cards"];
            for (auto& id : cards)
            {
                if (id == 3020)
                {
                    id = 3002;
                    break;
                }
            }
            auto match = Make(changed, {2002, 3002, 2009, 3001, 3007}, {2001});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const blocker = match.Player(2).Hand[0];
            match.Summon(2, blocker, "Normal", {}, 0);
            match.ResolvePendingEffect(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.AssignBlockers(2, {{hand[0], blocker}});
            match.PassRaidResponse(2);
            match.PassRaidResponse(1);
            match.ResolveRaid(2);
            Check(match.Player(1).Field[0]->Damage == 1, "Combat setup must inflict one damage");
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.Summon(1, hand[1], "Normal", {}, 1);
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Operation == "HealAccumulatedDamage" && choice.SourceZone == "FriendlyField" &&
                      choice.MinCount == 1 && choice.MaxCount == 1 &&
                      choice.Candidates == std::vector<int>({hand[0], hand[1]}),
                  "Healing candidates must be controlled monsters in field slot order");
            Reject([&] { (void)match.PendingEffectChoices(2); });
            Reject([&] { match.ResolvePendingEffect(1, blocker); });
            Check(match.Player(1).Field[0]->Damage == 1 && !match.PendingEffects().empty(),
                  "Illegal healing target must not mutate the match");
            match.ResolvePendingEffect(1, hand[0]);
            Check(match.Player(1).Field[0]->Damage == 0 && match.Player(1).Field[0]->Hp() == 3,
                  "Healing must remove accumulated damage without overhealing");
        }
        {
            // The resolver must never silently treat an unsupported authored
            // operation (Salvage) as an empty effect.
            auto match = Make(proto, {2001, 2002, 2009, 3001, 3007}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            Check(!match.Player(1).Graveyard.empty(),
                  "Mill setup for Necromancy");
            int const material = match.Player(1).Graveyard.front();
            match.Summon(1, hand[1], "Necromancy", {material}, 1);
            Check(!match.PendingEffects().empty(), "Salvage trigger must be queued");
            auto const count = match.PendingEffects().size();
            auto const graveCount = match.Player(1).Graveyard.size();
            Reject([&] { match.ResolvePendingEffect(1); });
            Check(match.PendingEffects().size() == count &&
                      match.Player(1).Graveyard.size() == graveCount,
                  "Unsupported effect must fail without mutations");
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
