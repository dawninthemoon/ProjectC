#include "YakouMatch.hpp"
#include "YakouReplay.hpp"
#include "CardCatalog.hpp"

#include <algorithm>
#include <array>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <tuple>
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
std::vector<int> SampleDeck(YakouPrototype const& proto, std::size_t index,
                            std::vector<int> front)
{
    auto deck = proto.Decks.at("Decks").at(index).at("Cards").get<std::vector<int>>();
    for (int card : front)
    {
        auto it = std::find(deck.begin(), deck.end(), card);
        Check(it != deck.end(), "Card missing in selected sample deck");
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
        auto proto = YakouPrototype::Load(
            std::filesystem::path(DefaultDataDirectory()) / "YakouPrototype");
        for (auto const& [deckIndex, defenderId, reduction] :
             std::vector<std::tuple<std::size_t, int, int>>{{0, 2006, 1}, {3, 4016, 2}})
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, 2, {3003}), SampleDeck(proto, deckIndex, {defenderId})};
            YakouMatch match(proto, 1, decks);
            int const raider = match.Player(1).Hand[0];
            int const blocker = match.Player(2).Hand[0];
            std::vector<YakouReplayCommand> commands;
            auto record = [&](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand value;
                value.Operation = operation;
                value.Player = player;
                commands.push_back(value);
            };
            for (int step = 0; step < 16 && match.Phase() != YakouPhase::AssignBlockers; ++step)
            {
                int const active = match.ActivePlayer();
                match.StartTurn(active);
                record(YakouReplayOperation::StartTurn, active);
                if (active == 1 && !match.Player(1).Field[0])
                {
                    match.Summon(1, raider, "Normal", {}, 0);
                    auto& command = commands.emplace_back();
                    command.Operation = YakouReplayOperation::Summon;
                    command.Player = 1;
                    command.CardId = raider;
                    command.Method = "Normal";
                }
                if (active == 2 && !match.Player(2).Field[0])
                {
                    match.Summon(2, blocker, "Normal", {}, 0);
                    auto& command = commands.emplace_back();
                    command.Operation = YakouReplayOperation::Summon;
                    command.Player = 2;
                    command.CardId = blocker;
                    command.Method = "Normal";
                }
                match.BeginRaid(active);
                record(YakouReplayOperation::BeginRaid, active);
            }
            Check(match.Phase() == YakouPhase::AssignBlockers &&
                      match.ActivePlayer() == 1 && match.Attacks()[0].Card == raider,
                  "Defensive authored cards must reach a real raid");
            auto const beforeWrongBlock = match.ServerStateHash();
            Reject([&] { match.AssignBlockers(1, {{raider, blocker}}); });
            Check(match.ServerStateHash() == beforeWrongBlock,
                  "Attacking player cannot assign the defender's blocker");
            match.AssignBlockers(2, {{raider, blocker}});
            auto& assignment = commands.emplace_back();
            assignment.Operation = YakouReplayOperation::AssignBlockers;
            assignment.Player = 2;
            assignment.Blockers = {{raider, blocker}};
            match.PassRaidResponse(2);
            record(YakouReplayOperation::PassRaidResponse, 2);
            match.PassRaidResponse(1);
            record(YakouReplayOperation::PassRaidResponse, 1);
            match.ResolveRaid(2);
            record(YakouReplayOperation::ResolveRaid, 2);
            Check(match.Player(2).Field[0] &&
                      match.Player(2).Field[0]->Hp() == 5 - (4 - reduction) &&
                      match.ViewerSnapshot(1).at("Players")[1].at("FieldSlots")[0]
                          .at("Hp") == 5 - (4 - reduction),
                  "Blocker must reduce the raider's actual combat damage once");
            auto const replay = ReplayYakou(proto, 1, decks, commands);
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Defensive blocker combat must replay deterministically");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {3007, 2005}), Deck(proto, {2001})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const summoned = match.Player(1).Hand[0];
            int const discarded = match.Player(1).Hand[1];
            int const deckBefore = static_cast<int>(match.Player(1).Deck.size());
            auto const beforeBadCost = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, summoned, "Discard", {summoned}, 0); });
            Check(match.ServerStateHash() == beforeBadCost,
                  "2005 discard fixture must reject spending the summoned card");
            (void)match.SummonAndRunAutomaticTriggers(
                1, summoned, "Discard", {discarded}, 0);
            Check(match.Player(1).Field[0] &&
                      static_cast<int>(match.Player(1).Deck.size()) == deckBefore - 1 &&
                      match.Player(1).Graveyard.size() == 2 &&
                      std::find(match.Player(1).Graveyard.begin(),
                                match.Player(1).Graveyard.end(), discarded) !=
                          match.Player(1).Graveyard.end(),
                  "Discarded 2005 must move to graveyard and mill one own deck card");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = summoned;
            summon.Method = "Discard";
            summon.Cards = {discarded};
            auto const replay = ReplayYakou(proto, 1, decks, {start, summon});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2005 discard trigger must replay deterministically");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, 2, {3001, 3007, 3011, 3003}),
                SampleDeck(proto, 0, {})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            int const countdownBefore = match.Player(1).Field[0]->Countdown;
            auto const beforeCost = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, hand[1], "Discard", {hand[0]}, 1); });
            Check(match.ServerStateHash() == beforeCost,
                  "3007 cannot discard an already summoned field card");
            (void)match.SummonAndRunAutomaticTriggers(
                1, hand[1], "Discard", {hand[3]}, 1);
            Check(match.PendingEffectChoices(1).Candidates.size() == 1 &&
                      match.PendingEffectChoices(1).Candidates[0] == hand[0],
                  "3007 must offer its other friendly monster as countdown target");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[0]);
            Check(match.Player(1).Field[0]->Countdown == countdownBefore - 1 &&
                      std::find(match.Player(1).Graveyard.begin(),
                                match.Player(1).Graveyard.end(), hand[3]) !=
                          match.Player(1).Graveyard.end(),
                  "3007 must pay its discard and reduce the selected counter");
            auto const beforeWrongPhase = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(2, hand[2]); });
            Check(match.ServerStateHash() == beforeWrongPhase,
                  "Opponent cannot activate 3011 from another player's hand");
            match.BeginSpellResponse(1, hand[2]);
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[2]); });
            Check(match.ServerStateHash() == beforeBadTarget,
                  "3011 cannot target its own spell instance");
            int const secondBefore = match.Player(1).Field[1]->Countdown;
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[1]);
            Check(match.Player(1).Field[1]->Countdown == secondBefore - 1,
                  "3011 must reduce the selected friendly monster's countdown");
            auto make = [](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand command;
                command.Operation = operation;
                command.Player = player;
                return command;
            };
            auto normal = make(YakouReplayOperation::SummonAndRunAutomaticTriggers, 1);
            normal.CardId = hand[0];
            normal.Method = "Normal";
            auto discard = make(YakouReplayOperation::SummonAndRunAutomaticTriggers, 1);
            discard.CardId = hand[1];
            discard.Method = "Discard";
            discard.Cards = {hand[3]};
            discard.Slot = 1;
            auto chooseFirst = make(YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers, 1);
            chooseFirst.TargetId = hand[0];
            auto cast = make(YakouReplayOperation::BeginSpellResponse, 1);
            cast.CardId = hand[2];
            auto chooseSecond = chooseFirst;
            chooseSecond.TargetId = hand[1];
            auto const replay = ReplayYakou(proto, 1, decks,
                {make(YakouReplayOperation::StartTurn, 1), normal, discard, chooseFirst,
                 cast, make(YakouReplayOperation::PassSpellResponse, 2),
                 make(YakouReplayOperation::PassSpellResponse, 1), chooseSecond});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "3007 discard and 3011 spell selection must replay deterministically");
        }
        for (auto const& [deckIndex, materialDefinition, devourDefinition] :
             std::vector<std::tuple<std::size_t, int, int>>{{0, 2001, 2008}, {2, 3003, 3005}})
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, deckIndex, {materialDefinition, devourDefinition}),
                SampleDeck(proto, 0, {})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const material = match.Player(1).Hand[0];
            int const devour = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, material, "Normal", {}, 0);
            int const deckBefore = static_cast<int>(match.Player(1).Deck.size());
            auto const beforeBadMaterial = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, devour, "Devour", {match.Player(1).Hand[2]}, 0); });
            Check(match.ServerStateHash() == beforeBadMaterial,
                  "Devour cannot spend a hand card instead of a field monster");
            (void)match.SummonAndRunAutomaticTriggers(1, devour, "Devour", {material}, 0);
            Check(match.Player(1).Field[0] &&
                      match.Player(1).Field[0]->InstanceId == devour &&
                      std::find(match.Player(1).Graveyard.begin(),
                                match.Player(1).Graveyard.end(), material) !=
                          match.Player(1).Graveyard.end(),
                  "Devour must put the material into graveyard and occupy its slot");
            if (devourDefinition == 2008)
            {
                Check(static_cast<int>(match.Player(1).Deck.size()) == deckBefore - 1,
                      "2008 must mill one card after its actual Devour summon");
            }
            else
            {
                Check(static_cast<int>(match.Player(1).Deck.size()) == deckBefore &&
                          match.Player(1).Field[0]->Countdown == 2,
                      "3005 must reduce its printed attack countdown after Devour");
            }
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = material;
            summon.Method = "Normal";
            auto eat = summon;
            eat.CardId = devour;
            eat.Method = "Devour";
            eat.Cards = {material};
            auto const replay = ReplayYakou(proto, 1, decks, {start, summon, eat});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Authored Devour trigger must replay with identical final state");
        }
        {
            // 3006 is authored but absent from all three sample decks. A
            // copied test catalog puts it into a legal deck without changing
            // shipped data or altering the card's own definition.
            auto fixture = proto;
            auto& cards = fixture.Decks["Decks"][0]["Cards"];
            auto replaced = std::find(cards.begin(), cards.end(), Json(3013));
            Check(replaced != cards.end(), "3006 fixture needs a replaceable deck slot");
            *replaced = 3006;
            replaced = std::find(cards.begin(), cards.end(), Json(3020));
            Check(replaced != cards.end(), "3014 fixture needs a replaceable deck slot");
            *replaced = 3014;
            std::array<std::vector<int>, 2> const decks{
                Deck(fixture, {2001, 2006, 3006, 3014}), SampleDeck(fixture, 2, {3003})};
            YakouMatch match(fixture, 1, decks);
            std::vector<YakouReplayCommand> commands;
            auto record = [&](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand command;
                command.Operation = operation;
                command.Player = player;
                commands.push_back(command);
            };
            match.StartTurn(1);
            record(YakouReplayOperation::StartTurn, 1);
            auto const hand = match.Player(1).Hand;
            int const attacker = match.Player(2).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            auto summonMiller = YakouReplayCommand{};
            summonMiller.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summonMiller.Player = 1;
            summonMiller.CardId = hand[0];
            summonMiller.Method = "Normal";
            commands.push_back(summonMiller);
            match.BeginRaid(1);
            record(YakouReplayOperation::BeginRaid, 1);
            match.StartTurn(2);
            record(YakouReplayOperation::StartTurn, 2);
            match.Summon(2, attacker, "Normal", {}, 0);
            auto summonAttacker = YakouReplayCommand{};
            summonAttacker.Operation = YakouReplayOperation::Summon;
            summonAttacker.Player = 2;
            summonAttacker.CardId = attacker;
            summonAttacker.Method = "Normal";
            commands.push_back(summonAttacker);
            match.BeginRaid(2);
            record(YakouReplayOperation::BeginRaid, 2);
            match.StartTurn(1);
            record(YakouReplayOperation::StartTurn, 1);
            match.Summon(1, hand[1], "Normal", {}, 1);
            auto summonPatient = summonMiller;
            summonPatient.Operation = YakouReplayOperation::Summon;
            summonPatient.CardId = hand[1];
            summonPatient.Slot = 1;
            commands.push_back(summonPatient);
            bool injured = false;
            for (int step = 0; step < 16 && !injured; ++step)
            {
                int const active = match.ActivePlayer();
                if (match.Phase() == YakouPhase::TurnStart)
                {
                    match.StartTurn(active);
                    record(YakouReplayOperation::StartTurn, active);
                }
                if (match.Phase() == YakouPhase::Main)
                {
                    match.BeginRaid(active);
                    record(YakouReplayOperation::BeginRaid, active);
                }
                if (match.Phase() == YakouPhase::AssignBlockers)
                {
                    int const defending = 3 - active;
                    bool const canBlock = active == 2 &&
                        std::any_of(match.Attacks().begin(), match.Attacks().end(),
                            [&](YakouAttack const& raid) { return raid.Card == attacker; });
                    match.AssignBlockers(defending, canBlock
                        ? std::vector<std::pair<int, int>>{{attacker, hand[1]}}
                        : std::vector<std::pair<int, int>>{});
                    auto assignment = YakouReplayCommand{};
                    assignment.Operation = YakouReplayOperation::AssignBlockers;
                    assignment.Player = defending;
                    if (canBlock)
                    {
                        assignment.Blockers = {{attacker, hand[1]}};
                    }
                    commands.push_back(assignment);
                    match.PassRaidResponse(defending);
                    record(YakouReplayOperation::PassRaidResponse, defending);
                    match.PassRaidResponse(active);
                    record(YakouReplayOperation::PassRaidResponse, active);
                    match.ResolveRaid(defending);
                    record(YakouReplayOperation::ResolveRaid, defending);
                    injured = canBlock;
                }
            }
            Check(injured && match.Player(1).Field[1] &&
                      match.Player(1).Field[1]->Damage > 0,
                  "3006 fixture must wound the friendly blocker in actual combat");
            if (match.Phase() == YakouPhase::TurnStart)
            {
                match.StartTurn(1);
                record(YakouReplayOperation::StartTurn, 1);
            }
            int const cost = match.Player(1).Graveyard[0];
            auto const beforeBadCost = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, hand[2], "Necromancy", {attacker}, 2); });
            Check(match.ServerStateHash() == beforeBadCost,
                  "3006 cannot spend the opponent's card as necromancy cost");
            int const damageBefore = match.Player(1).Field[1]->Damage;
            (void)match.SummonAndRunAutomaticTriggers(
                1, hand[2], "Necromancy", {cost}, 2);
            auto summonHealer = summonMiller;
            summonHealer.CardId = hand[2];
            summonHealer.Method = "Necromancy";
            summonHealer.Cards = {cost};
            summonHealer.Slot = 2;
            commands.push_back(summonHealer);
            auto const healCandidates = match.PendingEffectChoices(1).Candidates;
            Check(match.Player(1).Exile.back() == cost &&
                      healCandidates.size() == 2 &&
                      std::find(healCandidates.begin(), healCandidates.end(), hand[1]) !=
                          healCandidates.end(),
                  "3006 must exile one own cost and offer the wounded other monster");
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[2]); });
            Check(match.ServerStateHash() == beforeBadTarget,
                  "3006 cannot heal itself through OtherFriendlyMonster target");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[1]);
            auto choose = YakouReplayCommand{};
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = hand[1];
            commands.push_back(choose);
            Check(match.Player(1).Field[1]->Damage == damageBefore - 1,
                  "3006 must heal exactly one accumulated damage after necromancy summon");
            auto const replay = ReplayYakou(fixture, 1, decks, commands);
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "3006 cost, combat wound, chosen healing and zones must replay exactly");
            Check(match.Player(1).Field[1]->Damage > 0,
                  "3014 fixture must retain damage after 3006 heals one");
            match.BeginSpellResponse(1, hand[3]);
            auto healSpell = YakouReplayCommand{};
            healSpell.Operation = YakouReplayOperation::BeginSpellResponse;
            healSpell.Player = 1;
            healSpell.CardId = hand[3];
            commands.push_back(healSpell);
            match.PassSpellResponse(2);
            record(YakouReplayOperation::PassSpellResponse, 2);
            match.PassSpellResponse(1);
            record(YakouReplayOperation::PassSpellResponse, 1);
            auto const beforeBadHealTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[3]); });
            Check(match.ServerStateHash() == beforeBadHealTarget,
                  "3014 must reject its spell instance as a monster target");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[1]);
            commands.push_back(choose);
            Check(match.Player(1).Field[1]->Damage == 0 &&
                      match.Player(1).Field[1]->Hp() ==
                          match.Player(1).Field[1]->MaxHp(),
                  "3014 must heal the remaining combat damage without exceeding maximum HP");
            auto const healedReplay = ReplayYakou(fixture, 1, decks, commands);
            Check(healedReplay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "3014 response choice and healing must replay exactly");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2001, 2003}), Deck(proto, {2001})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const miller = match.Player(1).Hand[0];
            int const guide = match.Player(1).Hand[1];
            int const enemy = match.Player(2).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(1, miller, "Normal", {}, 0);
            auto const grave = match.Player(1).Graveyard;
            Check(grave.size() == 2, "2003 fixture needs two milled necromancy cards");
            auto const beforeWrongCost = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, guide, "Necromancy", {grave[0], enemy}, 1); });
            Check(match.ServerStateHash() == beforeWrongCost,
                  "2003 must reject an opponent's necromancy material atomically");
            int const counterBefore = match.Player(1).Field[0]->Countdown;
            (void)match.SummonAndRunAutomaticTriggers(
                1, guide, "Necromancy", {grave[0], grave[1]}, 1);
            Check(match.Player(1).Exile.size() == 2 &&
                      match.PendingEffectChoices(1).Candidates == std::vector<int>{miller},
                  "2003 must exile two materials and offer the other friendly monster");
            auto const beforeWrongTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, guide); });
            Check(match.ServerStateHash() == beforeWrongTarget,
                  "2003 cannot target itself through OtherFriendlyMonster");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, miller);
            Check(match.Player(1).Field[0]->Countdown == counterBefore - 1,
                  "2003 must reduce the selected other monster's countdown");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = miller;
            summon.Method = "Normal";
            auto necro = summon;
            necro.CardId = guide;
            necro.Method = "Necromancy";
            necro.Cards = {grave[0], grave[1]};
            necro.Slot = 1;
            YakouReplayCommand choose;
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = miller;
            auto const replay = ReplayYakou(proto, 1, decks, {start, summon, necro, choose});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2003 necromancy and selected countdown effect must replay exactly");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2001, 2007, 2009}), Deck(proto, {2001})};
            YakouMatch match(proto, 1, decks);
            std::vector<YakouReplayCommand> commands;
            auto record = [&](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand command;
                command.Operation = operation;
                command.Player = player;
                commands.push_back(command);
            };
            match.StartTurn(1);
            record(YakouReplayOperation::StartTurn, 1);
            auto const hand = match.Player(1).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = hand[0];
            summon.Method = "Normal";
            commands.push_back(summon);
            match.BeginSpellResponse(1, hand[2]);
            auto cast = YakouReplayCommand{};
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = hand[2];
            commands.push_back(cast);
            match.PassSpellResponse(2);
            record(YakouReplayOperation::PassSpellResponse, 2);
            match.PassSpellResponse(1);
            record(YakouReplayOperation::PassSpellResponse, 1);
            auto const grave = match.Player(1).Graveyard;
            Check(grave.size() >= 4, "2007 fixture needs four authored graveyard cards");
            auto const beforeBadCost = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                1, hand[1], "Necromancy", {grave[0], grave[0]}, 1); });
            Check(match.ServerStateHash() == beforeBadCost,
                  "2007 cannot pay the same necromancy card twice");
            (void)match.SummonAndRunAutomaticTriggers(
                1, hand[1], "Necromancy", {grave[0], grave[1]}, 1);
            auto necro = summon;
            necro.CardId = hand[1];
            necro.Method = "Necromancy";
            necro.Cards = {grave[0], grave[1]};
            necro.Slot = 1;
            commands.push_back(necro);
            bool raidReady = false;
            for (int step = 0; step < 12 && !raidReady; ++step)
            {
                int const active = match.ActivePlayer();
                match.BeginRaid(active);
                record(YakouReplayOperation::BeginRaid, active);
                raidReady = match.Phase() == YakouPhase::AssignBlockers &&
                    std::any_of(match.Attacks().begin(), match.Attacks().end(),
                        [&](YakouAttack const& attack) { return attack.Card == hand[1]; });
                if (!raidReady)
                {
                    match.StartTurn(match.ActivePlayer());
                    record(YakouReplayOperation::StartTurn, match.ActivePlayer());
                }
            }
            Check(raidReady && match.ActivePlayer() == 1,
                  "2007 must reach a legal raid after its counter expires");
            int const lifeBefore = match.Player(2).Life;
            match.AssignBlockers(2, {});
            record(YakouReplayOperation::AssignBlockers, 2);
            match.PassRaidResponse(2);
            record(YakouReplayOperation::PassRaidResponse, 2);
            match.PassRaidResponse(1);
            record(YakouReplayOperation::PassRaidResponse, 1);
            match.ResolveRaid(2);
            record(YakouReplayOperation::ResolveRaid, 2);
            Check(lifeBefore - match.Player(2).Life == 5,
                  "2007 must contribute printed attack 3 plus one conditioned raid bonus");
            auto const replay = ReplayYakou(proto, 1, decks, commands);
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2007 cost, conditioned raid damage and turn order must replay exactly");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2001, 2009, 3013}), Deck(proto, {2001})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            match.BeginSpellResponse(1, hand[1]);
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            auto const grave = match.Player(1).Graveyard;
            Check(grave.size() >= 5, "3013 fixture needs post-cost graveyard monsters");
            auto const beforeBadCost = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(1, hand[2], 0,
                                                   {grave[0], grave[0]}); });
            Check(match.ServerStateHash() == beforeBadCost,
                  "3013 cannot spend the same graveyard cost twice");
            match.BeginSpellResponse(1, hand[2], 0, {grave[0], grave[1]});
            Check(match.PriorityPlayer() == 2 && match.Player(1).Exile.size() == 2,
                  "3013 must pay necromancy two before opponent priority");
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            auto const choices = match.PendingEffectChoices(1).Candidates;
            Check(!choices.empty() &&
                      std::find(choices.begin(), choices.end(), grave[0]) == choices.end() &&
                      std::find(choices.begin(), choices.end(), grave[1]) == choices.end(),
                  "3013 must exclude already paid cost cards from salvage choices");
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, grave[0]); });
            Check(match.ServerStateHash() == beforeBadTarget,
                  "3013 cannot recover an exiled cost card");
            int const selected = choices.front();
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, selected);
            Check(std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), selected) !=
                      match.Player(1).Hand.end(),
                  "3013 must move the chosen eligible monster into hand");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = hand[0];
            summon.Method = "Normal";
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = hand[1];
            auto paid = cast;
            paid.CardId = hand[2];
            paid.Cards = {grave[0], grave[1]};
            YakouReplayCommand passTwo;
            passTwo.Operation = YakouReplayOperation::PassSpellResponse;
            passTwo.Player = 2;
            auto passOne = passTwo;
            passOne.Player = 1;
            YakouReplayCommand choose;
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = selected;
            auto const replay = ReplayYakou(proto, 1, decks,
                {start, summon, cast, passTwo, passOne, paid, passTwo, passOne, choose});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "3013 paid response and private salvage must replay exactly");
        }
        for (auto const& [deckIndex, definition] :
             std::vector<std::pair<std::size_t, int>>{{0, 3001}, {1, 4001}})
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, deckIndex, {definition}), SampleDeck(proto, 0, {})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const card = match.Player(1).Hand[0];
            int const deckBefore = static_cast<int>(match.Player(1).Deck.size());
            auto const hashBefore = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                2, card, "Normal", {}, 0); });
            Check(match.ServerStateHash() == hashBefore,
                  "Opponent cannot summon the authored mill monster");
            (void)match.SummonAndRunAutomaticTriggers(1, card, "Normal", {}, 0);
            Check(match.Player(1).Field[0] &&
                      match.Player(1).Field[0]->InstanceId == card &&
                      static_cast<int>(match.Player(1).Deck.size()) == deckBefore - 1 &&
                      match.Player(1).Graveyard.size() == 1 &&
                      match.ViewerSnapshot(2).at("Players")[0].at("Graveyard").size() == 1,
                  "Authored mill monster must move one own deck card into public graveyard");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = card;
            summon.Method = "Normal";
            auto const replay = ReplayYakou(proto, 1, decks, {start, summon});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Authored mill monster must replay with the same hidden deck order");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2009}), Deck(proto, {2001})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const spell = match.Player(1).Hand[0];
            int const deckBefore = static_cast<int>(match.Player(1).Deck.size());
            auto const beforeWrongSeat = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(2, spell); });
            Check(match.ServerStateHash() == beforeWrongSeat,
                  "Opponent cannot activate the authored mill spell");
            match.BeginSpellResponse(1, spell);
            Check(static_cast<int>(match.Player(1).Deck.size()) == deckBefore &&
                      match.PriorityPlayer() == 2,
                  "2009 must wait for opponent priority before milling");
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(static_cast<int>(match.Player(1).Deck.size()) == deckBefore - 2 &&
                      match.Player(1).Graveyard.size() == 3 &&
                      match.ViewerSnapshot(2).at("Players")[0].at("Graveyard").size() == 3,
                  "2009 must mill exactly two cards after consecutive passes");
            auto const beforeRepeat = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(1, spell); });
            Check(match.ServerStateHash() == beforeRepeat,
                  "Resolved 2009 cannot be activated again from graveyard");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = spell;
            YakouReplayCommand passTwo;
            passTwo.Operation = YakouReplayOperation::PassSpellResponse;
            passTwo.Player = 2;
            auto passOne = passTwo;
            passOne.Player = 1;
            auto const replay = ReplayYakou(proto, 1, decks,
                                            {start, cast, passTwo, passOne});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2009 spell resolution must replay with identical hidden deck order");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, 1, {4001, 4007}), SampleDeck(proto, 0, {})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const monster = match.Player(1).Hand[0];
            int const spell = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, monster, "Normal", {}, 0);
            int const deckBeforeSpell = static_cast<int>(match.Player(1).Deck.size());
            int const counterBefore = match.Player(1).Field[0]->Countdown;
            match.BeginSpellResponse(1, spell);
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(static_cast<int>(match.Player(1).Deck.size()) == deckBeforeSpell &&
                      match.PendingEffectChoices(1).Candidates.size() == 1 &&
                      match.PendingEffectChoices(1).Candidates[0] == monster,
                  "4007 must wait for a valid friendly target before resolving both actions");
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, spell); });
            Check(match.ServerStateHash() == beforeBadTarget,
                  "4007 must reject a spell instance as countdown target atomically");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, monster);
            Check(static_cast<int>(match.Player(1).Deck.size()) == deckBeforeSpell - 1 &&
                      match.Player(1).Field[0]->Countdown == std::max(0, counterBefore - 1) &&
                      match.PendingEffects().empty(),
                  "4007 must mill and reduce countdown after valid selection");
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = monster;
            summon.Method = "Normal";
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = spell;
            YakouReplayCommand passTwo;
            passTwo.Operation = YakouReplayOperation::PassSpellResponse;
            passTwo.Player = 2;
            auto passOne = passTwo;
            passOne.Player = 1;
            YakouReplayCommand choose;
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = monster;
            auto const replay = ReplayYakou(proto, 1, decks,
                                            {start, summon, cast, passTwo, passOne, choose});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "4007 mill and countdown selection must replay deterministically");
        }
        for (int definition : {4008, 4009})
        {
            // Both authored vanilla monsters share the normal-summon path.
            YakouMatch match(proto, 1,
                {SampleDeck(proto, 2, {definition}), SampleDeck(proto, 0, {})});
            int const card = match.Player(1).Hand[0];
            auto const beforeTurn = match.ServerStateHash();
            Reject([&] { match.Summon(1, card, "Normal", {}, 0); });
            Check(match.ServerStateHash() == beforeTurn,
                  "Vanilla summon before turn start must reject atomically");
            match.StartTurn(1);
            auto const beforeCost = match.ServerStateHash();
            Reject([&] { match.Summon(1, card, "Normal", {match.Player(1).Hand[1]}, 0); });
            Check(match.ServerStateHash() == beforeCost,
                  "Vanilla normal summon cannot charge an unauthored material");
            match.Summon(1, card, "Normal", {}, 0);
            Check(match.Player(1).Field[0] &&
                      match.Player(1).Field[0]->InstanceId == card &&
                      std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), card) ==
                          match.Player(1).Hand.end(),
                  "Vanilla monster must move from hand into its field slot");
            auto const beforeDuplicate = match.ServerStateHash();
            Reject([&] { match.Summon(1, card, "Normal", {}, 1); });
            Check(match.ServerStateHash() == beforeDuplicate,
                  "Already summoned vanilla card must reject atomically");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, 1, {2001, 4002, 4003, 4005, 4006, 4001, 4001}),
                SampleDeck(proto, 0, {})};
            YakouMatch match(proto, 1, decks);
            match.StartTurn(1);
            int const miller = match.Player(1).Hand[0];
            int const mender = match.Player(1).Hand[1];
            int const paid = match.Player(1).Hand[2];
            (void)match.SummonAndRunAutomaticTriggers(1, miller, "Normal", {}, 0);
            Check(match.Player(1).Graveyard.size() == 2,
                  "Authored mill must establish two graveyard archetype cards");
            (void)match.SummonAndRunAutomaticTriggers(1, mender, "Discard", {paid}, 1);
            Check(match.PendingEffects().size() == 1 &&
                      match.PendingEffectChoices(1).Candidates.size() == 2,
                  "Post-cost third archetype card must enable optional salvage while excluding paid card");
            int const recovered = match.PendingEffectChoices(1).Candidates[0];
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, paid); });
            Check(match.ServerStateHash() == before,
                  "A discarded cost cannot be recovered by the same effect");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, recovered);
            Check(match.PendingEffects().empty() &&
                      std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), recovered) !=
                          match.Player(1).Hand.end(),
                  "Valid selected monster must return to its owner's hand");

            YakouMatch insufficient(proto, 1,
                {SampleDeck(proto, 1, {4002, 4003}), SampleDeck(proto, 0, {})});
            insufficient.StartTurn(1);
            int const source = insufficient.Player(1).Hand[0];
            int const cost = insufficient.Player(1).Hand[1];
            (void)insufficient.SummonAndRunAutomaticTriggers(1, source, "Discard", {cost}, 0);
            Check(insufficient.PendingEffects().empty(),
                  "Conditional salvage must not queue below the post-cost graveyard threshold");
        }
        {
            YakouMatch discarded(proto, 1,
                {SampleDeck(proto, 1, {4003, 2001}), SampleDeck(proto, 0, {})});
            discarded.StartTurn(1);
            int const source = discarded.Player(1).Hand[0];
            int const cost = discarded.Player(1).Hand[1];
            auto const deckSize = discarded.Player(1).Deck.size();
            (void)discarded.SummonAndRunAutomaticTriggers(1, source, "Discard", {cost}, 0);
            Check(discarded.Player(1).Field[0] &&
                      discarded.Player(1).Deck.size() == deckSize - 1 &&
                      discarded.Player(1).Graveyard.size() == 2 &&
                      discarded.PendingEffects().empty(),
                  "Discard-summoned spirit must pay one hand card and automatically mill one");

            YakouMatch necromantic(proto, 1,
                {SampleDeck(proto, 1, {2001, 4005}), SampleDeck(proto, 0, {})});
            necromantic.StartTurn(1);
            int const miller = necromantic.Player(1).Hand[0];
            int const summoned = necromantic.Player(1).Hand[1];
            (void)necromantic.SummonAndRunAutomaticTriggers(1, miller, "Normal", {}, 0);
            auto const materials = necromantic.Player(1).Graveyard;
            Check(materials.size() == 2, "Necromancy fixture needs two milled cards");
            auto const beforeSummonDeck = necromantic.Player(1).Deck.size();
            (void)necromantic.SummonAndRunAutomaticTriggers(1, summoned,
                "Necromancy", materials, 1);
            Check(necromantic.Player(1).Field[1] &&
                      necromantic.Player(1).Exile.size() == 2 &&
                      necromantic.Player(1).Graveyard.size() == 1 &&
                      necromantic.Player(1).Deck.size() == beforeSummonDeck - 1 &&
                      necromantic.PendingEffects().empty(),
                  "Necromancy-summoned pursuer must exile two costs and automatically mill one");
        }
        {
            auto match = YakouMatch(proto, 1,
                {SampleDeck(proto, 1, {2001, 4006, 4003, 4005, 4007, 2002, 2001}),
                 SampleDeck(proto, 0, {})});
            match.StartTurn(1);
            int const miller = match.Player(1).Hand[0];
            int const spell = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, miller, "Normal", {}, 0);
            auto const grave = match.Player(1).Graveyard;
            Check(grave.size() == 2, "Necromancy salvage fixture needs two grave monsters");
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, spell, 64, {grave[0]});
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.Candidates.size() == 1 && choice.Candidates[0] == grave[1],
                  "Necromancy salvage must exclude the paid card from its choices");
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, grave[0]); });
            Check(match.ServerStateHash() == before,
                  "Exiled payment cannot be salvaged by spoofed target");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, grave[1]);
            Check(match.PendingEffects().empty() &&
                      std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), grave[1]) !=
                          match.Player(1).Hand.end(),
                  "Necromancy salvage must recover the remaining eligible monster");

            auto insufficient = YakouMatch(proto, 1,
                {SampleDeck(proto, 1, {4001, 4006, 4003, 4005, 4007, 2001}),
                 SampleDeck(proto, 0, {})});
            insufficient.StartTurn(1);
            (void)insufficient.SummonAndRunAutomaticTriggers(
                1, insufficient.Player(1).Hand[0], "Normal", {}, 0);
            int const onlyMaterial = insufficient.Player(1).Graveyard[0];
            int const failedSpell = insufficient.Player(1).Hand[0];
            auto const unchanged = insufficient.ServerStateHash();
            Reject([&] { (void)insufficient.ActivateMainSpellAndRunAutomaticTriggers(
                1, failedSpell, 64, {onlyMaterial}); });
            Check(insufficient.ServerStateHash() == unchanged,
                  "No eligible post-payment monster must reject without spending necromancy cost");
        }
        {
            auto fixture = proto;
            auto& guardCards = fixture.Decks["Decks"][3]["Cards"];
            auto replaced = std::find(guardCards.begin(), guardCards.end(), Json(3002));
            Check(replaced != guardCards.end(), "Missing guard fixture replacement card");
            *replaced = 3011;
            YakouMatch match(fixture, 1,
                {SampleDeck(fixture, 3, {4008, 4021, 3014, 3011}),
                 SampleDeck(fixture, 0, {})});
            match.StartTurn(1);
            int const monster = match.Player(1).Hand[0];
            int const collector = match.Player(1).Hand[1];
            int const healingSpell = match.Player(1).Hand[2];
            int const countdownSpell = match.Player(1).Hand[3];
            (void)match.SummonAndRunAutomaticTriggers(1, monster, "Normal", {}, 0);
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, healingSpell);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, monster);
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, countdownSpell);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, monster);
            Check(match.Player(1).Graveyard.size() == 2,
                  "Two prior spell resolutions must satisfy authored spell-count condition");
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            (void)match.SummonAndRunAutomaticTriggers(1, collector, "Normal", {}, 1);
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.MinCount == 0 && choice.Candidates.size() == 2 &&
                      match.ViewerSnapshot(2).contains("PendingSelection") == false,
                  "Spell collector must offer only own graveyard spells after threshold");
            int const selected = choice.Candidates[0];
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, selected);
            Check(std::find(match.Player(1).Hand.begin(), match.Player(1).Hand.end(), selected) !=
                      match.Player(1).Hand.end(),
                  "Selected spell must move from graveyard into its owner's hand");
        }
        {
            YakouMatch match(proto, 1,
                {SampleDeck(proto, 2, {4008, 4013, 4009, 3016, 3007}),
                 SampleDeck(proto, 0, {})});
            match.StartTurn(1);
            int const monster = match.Player(1).Hand[0];
            int const spell = match.Player(1).Hand[1];
            int const cost = match.Player(1).Hand[3];
            (void)match.SummonAndRunAutomaticTriggers(1, monster, "Normal", {}, 0);
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ActivateMainSpellAndRunAutomaticTriggers(
                1, spell, 64, {monster}); });
            Check(match.ServerStateHash() == before,
                  "Paid countdown spell must reject a cost outside the owner's hand");
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, spell, 64, {cost});
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, 31); });
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, monster);
            Check(match.Player(1).Field[0]->Countdown == 0,
                  "Authored minus-two countdown must stop at zero");
            match.BeginRaid(1);
            Check(match.Phase() == YakouPhase::AssignBlockers &&
                      match.Attacks().size() == 1 && match.Attacks()[0].Card == monster,
                  "A freshly summoned monster may raid when server countdown reaches zero");
        }
        {
            YakouMatch match(proto, 1,
                {SampleDeck(proto, 3, {4008, 4015}),
                 SampleDeck(proto, 2, {4009})});
            match.StartTurn(1);
            int const patient = match.Player(1).Hand[0];
            int const healer = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, patient, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const attacker = match.Player(2).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(2, attacker, "Normal", {}, 0);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.AssignBlockers(1, {{attacker, patient}});
            match.PassRaidResponse(1);
            match.PassRaidResponse(2);
            match.ResolveRaid(1);
            Check(match.Player(1).Field[0] && match.Player(1).Field[0]->Damage == 2,
                  "Healing fixture must preserve accumulated combat damage");
            match.StartTurn(1);
            (void)match.SummonAndRunAutomaticTriggers(1, healer, "Normal", {}, 1);
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.MinCount == 0 && choice.Candidates.size() == 1 &&
                      choice.Candidates[0] == patient,
                  "Healer must offer only a friendly monster with accumulated damage");
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, healer); });
            Check(match.ServerStateHash() == before,
                  "Undamaged target must reject without consuming the healing effect");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, patient);
            Check(match.Player(1).Field[0]->Damage == 0 && match.PendingEffects().empty(),
                  "Selected monster must heal exactly the available two damage");
        }
        {
            YakouMatch match(proto, 1,
                {SampleDeck(proto, 2, {4008, 4010, 4014, 3005, 3007}),
                 SampleDeck(proto, 0, {})});
            match.StartTurn(1);
            int const first = match.Player(1).Hand[0];
            int const drummer = match.Player(1).Hand[1];
            int const runner = match.Player(1).Hand[2];
            int const cost = match.Player(1).Hand[3];
            (void)match.SummonAndRunAutomaticTriggers(1, first, "Normal", {}, 0);
            (void)match.SummonAndRunAutomaticTriggers(1, runner, "Discard", {cost}, 1);
            Check(match.Player(1).Field[1]->Countdown == 1 &&
                      match.Player(1).Field[0]->Countdown == 2,
                  "Discard-summoned runner must shorten only its own countdown");
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            (void)match.SummonAndRunAutomaticTriggers(1, drummer, "Normal", {}, 2);
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.MinCount == 0 && choice.Candidates.size() == 2 &&
                      std::find(choice.Candidates.begin(), choice.Candidates.end(), drummer) ==
                          choice.Candidates.end(),
                  "Drummer must offer other friendly monsters and allow decline");
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, drummer); });
            Check(match.ServerStateHash() == before,
                  "Drummer cannot target itself");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, first);
            Check(match.Player(1).Field[0]->Countdown == 0 &&
                      match.Player(1).Field[1]->Countdown == 0,
                  "Other-monster countdown reduction must allow same-turn raid at zero");
            match.BeginRaid(1);
            Check(match.Attacks().size() == 2,
                  "Both server-ready monsters must join the declared raid");
        }
        for (auto const& [deckIndex, materialDefinition, devourDefinition] :
             std::vector<std::tuple<std::size_t, int, int>>{{1, 4001, 4004}, {2, 4008, 4011}})
        {
            YakouMatch match(proto, 1,
                {SampleDeck(proto, deckIndex, {materialDefinition, devourDefinition}),
                 SampleDeck(proto, 0, {})});
            match.StartTurn(1);
            int const material = match.Player(1).Hand[0];
            int const devour = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, material, "Normal", {}, 0);
            int const inheritedAttack = match.Player(1).Field[0]->Attack();
            int const inheritedHp = match.Player(1).Field[0]->MaxHp();
            (void)match.SummonAndRunAutomaticTriggers(1, devour, "Devour", {material}, 0);
            auto const& unit = *match.Player(1).Field[0];
            auto const definition = std::find_if(proto.Cards["Cards"].begin(),
                proto.Cards["Cards"].end(), [&](Json const& card) {
                    return card.at("Id") == devourDefinition;
                });
            Check(definition != proto.Cards["Cards"].end() &&
                      unit.InstanceId == devour && unit.WasDevourSummoned &&
                      unit.Attack() == definition->at("Attack").get<int>() + inheritedAttack &&
                      unit.MaxHp() == definition->at("MaxHp").get<int>() + inheritedHp &&
                      unit.Countdown == definition->at("AttackInterval").get<int>() - 1 &&
                      std::find(match.Player(1).Graveyard.begin(), match.Player(1).Graveyard.end(), material) !=
                          match.Player(1).Graveyard.end(),
                  "Authored devour must inherit printed stats before self countdown reduction");
        }
        {
            YakouMatch match(proto, 1,
                {SampleDeck(proto, 2, {4008}), SampleDeck(proto, 3, {4016})});
            match.StartTurn(1);
            int const attacker = match.Player(1).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(1, attacker, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const blocker = match.Player(2).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(2, blocker, "Normal", {}, 0);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.AssignBlockers(2, {{attacker, blocker}});
            match.PassRaidResponse(2);
            match.PassRaidResponse(1);
            match.ResolveRaid(2);
            Check(match.Player(2).Field[0] && match.Player(2).Field[0]->Damage == 1,
                  "Stone-wall blocker must reduce three incoming combat damage by two");
        }
        {
            auto match = YakouMatch(proto, 1,
                {SampleDeck(proto, 0, {2001}), SampleDeck(proto, 3, {4008, 4018})});
            match.StartTurn(1);
            int const attacker = match.Player(1).Hand.front();
            (void)match.SummonAndRunAutomaticTriggers(1, attacker, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const defender = match.Player(2).Hand.front();
            int const response = match.Player(2).Hand[1];
            match.Summon(2, defender, "Normal", {}, 0);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            auto unblocked = match;
            unblocked.AssignBlockers(2, {});
            auto const unblockedOptions = unblocked.ViewerSnapshot(2).at("ResponseOptions");
            Check(std::none_of(unblockedOptions.begin(), unblockedOptions.end(),
                               [&](Json const& option) { return option.at("CardId") == response; }),
                  "All-blocker response must not be offered without a friendly blocker");
            auto const unblockedHash = unblocked.ServerStateHash();
            Reject([&] { unblocked.RespondWithRaidShield(2, response, 0); });
            Check(unblocked.ServerStateHash() == unblockedHash,
                  "No-blocker response must reject without consuming its card");
            match.AssignBlockers(2, {{attacker, defender}});
            auto const offered = match.ViewerSnapshot(2).at("ResponseOptions");
            Check(std::any_of(offered.begin(), offered.end(), [&](Json const& option) {
                return option.at("CardId") == response &&
                    option.at("Type") == "RespondWithRaidShield" &&
                    option.at("TargetCandidates").empty() && option.at("CostCount") == 0;
            }), "All-blocker response must be available without target or cost");
            auto const before = match.ServerStateHash();
            Reject([&] { match.RespondWithRaidShield(2, response, defender); });
            Check(match.ServerStateHash() == before,
                  "All-blocker response must reject a spoofed individual target atomically");
            match.RespondWithRaidShield(2, response, 0);
            match.PassRaidResponse(1);
            match.PassRaidResponse(2);
            match.ResolveRaid(2);
            Check(match.Player(2).Field[0] && match.Player(2).Field[0]->Damage == 0,
                  "Authored all-blocker response must prevent first combat damage");
        }
        {
            std::array<std::vector<int>, 2> const decks{
                SampleDeck(proto, 2, {4008, 4012, 4009, 4013, 3007}),
                SampleDeck(proto, 0, {})};
            auto match = YakouMatch(proto, 1, decks);
            match.StartTurn(1);
            int const first = match.Player(1).Hand[0];
            int const spell = match.Player(1).Hand[1];
            int const second = match.Player(1).Hand[2];
            int const cost = match.Player(1).Hand[3];
            match.Summon(1, first, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.Summon(1, second, "Normal", {}, 1);
            auto const paidOptions = match.ViewerSnapshot(1).at("Players")[0].at("PaidSpellOptions");
            Check(std::any_of(paidOptions.begin(), paidOptions.end(), [&](Json const& option) {
                return option.at("CardId") == spell && option.at("CostCount") == 1;
            }), "Paid multi-target spell must offer a private hand cost");
            Check(!match.ViewerSnapshot(2).at("Players")[0].contains("PaidSpellOptions"),
                  "Opponent cannot inspect another player's paid spell choices");
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, spell, 64, {cost});
            auto const choice = match.PendingEffectChoices(1);
            Check(choice.MinCount == 1 && choice.MaxCount == 2 &&
                      choice.Candidates.size() == 2 &&
                      !match.ViewerSnapshot(2).contains("PendingSelection"),
                  "Multi-target spell must expose only authorized friendly candidates");
            auto const before = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(1, {first, first}); });
            Reject([&] { (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(1, {first, 31}); });
            Check(match.ServerStateHash() == before,
                  "Duplicate or opposing targets must reject without consuming the spell");
            (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(1, {first, second});
            Check(match.Player(1).Field[0]->Countdown == 0 &&
                      match.Player(1).Field[1]->Countdown == 0 &&
                      match.PendingEffects().empty(),
                  "Both selected countdowns must resolve from the authored effect");
            std::vector<YakouReplayCommand> replay;
            auto command = [](YakouReplayOperation operation, int player) {
                YakouReplayCommand result;
                result.Operation = operation;
                result.Player = player;
                return result;
            };
            replay.push_back(command(YakouReplayOperation::StartTurn, 1));
            auto summon = command(YakouReplayOperation::Summon, 1);
            summon.CardId = first;
            summon.Method = "Normal";
            replay.push_back(summon);
            replay.push_back(command(YakouReplayOperation::BeginRaid, 1));
            replay.push_back(command(YakouReplayOperation::StartTurn, 2));
            replay.push_back(command(YakouReplayOperation::BeginRaid, 2));
            replay.push_back(command(YakouReplayOperation::StartTurn, 1));
            summon.CardId = second;
            summon.Slot = 1;
            replay.push_back(summon);
            auto cast = command(YakouReplayOperation::ActivateMainSpellAndRunAutomaticTriggers, 1);
            cast.CardId = spell;
            cast.Cards = {cost};
            replay.push_back(cast);
            auto choose = command(YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers, 1);
            choose.Cards = {first, second};
            replay.push_back(choose);
            Check(ReplayYakou(proto, 1, decks, replay).FinalState.ServerStateHash() ==
                      match.ServerStateHash(),
                  "Two-target spell must replay deterministically");
        }
        {
            auto counterDeck = SampleDeck(proto, 1, {4001, 4007});
            counterDeck.pop_back();
            counterDeck.insert(counterDeck.begin() + 2, 4020);
            auto match = YakouMatch(proto, 1,
                {SampleDeck(proto, 0, {2009}), counterDeck});
            match.StartTurn(1);
            int const openingSpell = match.Player(1).Hand[0];
            match.BeginRaid(1);
            match.StartTurn(2);
            int const unit = match.Player(2).Hand[0];
            int const millSpell = match.Player(2).Hand[1];
            int const counter = match.Player(2).Hand[2];
            (void)match.SummonAndRunAutomaticTriggers(2, unit, "Normal", {}, 0);
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(2, millSpell);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(2, unit);
            Check(match.Player(2).Graveyard.size() >= 3,
                  "Authored mill sequence must create two necromancy materials");
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginSpellResponse(1, openingSpell);
            auto const options = match.ViewerSnapshot(2).at("ResponseOptions");
            Check(std::any_of(options.begin(), options.end(), [&](Json const& option) {
                return option.at("CardId") == counter &&
                    option.at("Type") == "RespondWithNegation" &&
                    option.at("CostCount") == 2 &&
                    option.at("CostCandidates").size() == match.Player(2).Graveyard.size();
            }), "Necromancy counterspell must offer only its owner's graveyard costs");
            int const firstCost = match.Player(2).Graveyard[0];
            int const secondCost = match.Player(2).Graveyard[1];
            auto const before = match.ServerStateHash();
            Reject([&] { match.RespondWithNegation(2, counter, {firstCost, firstCost}); });
            Reject([&] { match.RespondWithNegation(2, counter, {counter, secondCost}); });
            Check(match.ServerStateHash() == before,
                  "Invalid necromancy payments must not mutate the response window");
            match.RespondWithNegation(2, counter, {firstCost, secondCost});
            Check(std::find(match.Player(2).Exile.begin(), match.Player(2).Exile.end(), firstCost) !=
                      match.Player(2).Exile.end() &&
                      std::find(match.Player(2).Exile.begin(), match.Player(2).Exile.end(), secondCost) !=
                      match.Player(2).Exile.end(),
                  "Counterspell must move both selected materials to exile");
            match.PassSpellResponse(1);
            match.PassSpellResponse(2);
            Check(match.Phase() == YakouPhase::Main && match.PendingEffects().empty() &&
                      std::any_of(match.Journal().begin(), match.Journal().end(),
                          [](std::string const& entry) { return entry.starts_with("SpellNegated:"); }),
                  "Necromancy counterspell must negate the answered spell effect");
        }
        {
            auto match = YakouMatch(proto, 1,
                {SampleDeck(proto, 0, {2001}),
                 SampleDeck(proto, 3, {4008, 4016, 4017, 3004, 3009})});
            match.StartTurn(1);
            int const attacker = match.Player(1).Hand[0];
            (void)match.SummonAndRunAutomaticTriggers(1, attacker, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const replacement = match.Player(2).Hand[0];
            int const originalBlocker = match.Player(2).Hand[1];
            int const tactic = match.Player(2).Hand[2];
            int const costOne = match.Player(2).Hand[3];
            int const costTwo = match.Player(2).Hand[4];
            match.Summon(2, replacement, "Normal", {}, 0);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.Summon(2, originalBlocker, "Normal", {}, 1);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.AssignBlockers(2, {{attacker, originalBlocker}});
            auto const options = match.ViewerSnapshot(2).at("ResponseOptions");
            Check(std::any_of(options.begin(), options.end(), [&](Json const& option) {
                if (option.at("CardId") != tactic ||
                    option.at("Type") != "RespondWithRaidTactic" ||
                    option.at("CostCount") != 2) return false;
                auto const& candidates = option.at("ReplacementCandidates");
                return candidates.size() == 1 && candidates.at(0) == replacement &&
                    std::find(candidates.begin(), candidates.end(),
                              Json(originalBlocker)) == candidates.end();
            }), "Replacement tactic must only offer an eligible alternative blocker");
            auto const before = match.ServerStateHash();
            Reject([&] { match.RespondWithRaidTactic(2, tactic, attacker,
                                                     originalBlocker, {costOne, costTwo}); });
            Reject([&] { match.RespondWithRaidTactic(2, tactic, attacker,
                                                     replacement, {costOne, costOne}); });
            Check(match.ServerStateHash() == before,
                  "Invalid blocker replacement or duplicated cost must roll back");
            match.RespondWithRaidTactic(2, tactic, attacker,
                                        replacement, {costOne, costTwo});
            match.PassRaidResponse(1);
            match.PassRaidResponse(2);
            Check(match.Attacks()[0].Blocker == replacement,
                  "Tactic must replace the blocker before combat");
            match.ResolveRaid(2);
            Check(match.Player(2).Field[0] && match.Player(2).Field[0]->Damage == 0,
                  "New blocker must receive the authored first-damage reduction");
        }
        {
            auto guardDeck = SampleDeck(proto, 3, {4019});
            guardDeck.pop_back();
            guardDeck.insert(guardDeck.begin(), 4001);
            auto match = YakouMatch(proto, 1,
                {SampleDeck(proto, 2, {4008}), guardDeck});
            match.StartTurn(1);
            int const attacker = match.Player(1).Hand[0];
            match.Summon(1, attacker, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            int const millUnit = match.Player(2).Hand[0];
            int const blocker = match.Player(2).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(2, millUnit, "Normal", {}, 0);
            Check(!match.Player(2).Graveyard.empty(), "Mill must supply necromancy material");
            int const cost = match.Player(2).Graveyard[0];
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.Summon(2, blocker, "Necromancy", {cost}, 1);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.AssignBlockers(2, {{attacker, blocker}});
            match.PassRaidResponse(2);
            match.PassRaidResponse(1);
            match.ResolveRaid(2);
            Check(match.Player(2).Field[1] && match.Player(2).Field[1]->Damage == 2,
                  "Mirror guardian must reduce only the blocked raider's current combat attack");
        }
        {
            // Keep the cross-platform replay fixture independent of changing
            // presentation-only description arguments in the authored catalog.
            auto replayProto = proto;
            for (auto& card : replayProto.Cards.at("Cards"))
            {
                card["DescriptionArgs"] = Json::array();
            }
            std::array<std::vector<int>, 2> const decks{
                Deck(replayProto, {2005, 2001, 2009, 3001, 3007}), Deck(replayProto, {2001, 2002})};
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
            auto const pending = ReplayYakou(replayProto, 1, decks, pendingCommands);
            Check(pending.FinalState.PendingEffects().size() == 1 &&
                      pending.FinalState.PendingEffectChoices(2).Operation == "Mill",
                  "Replaying a partial journal must reconstruct its pending effect");
            Reject([&] { (void)pending.FinalState.PendingEffectChoices(1); });
            auto const mid = ReplayYakou(replayProto, 1, decks, commands);
            Check(mid.FinalState.PriorityPlayer() == 1 && !mid.Steps[2].Accepted &&
                      mid.Steps[2].BeforeHash == mid.Steps[2].AfterHash,
                  "Replay must preserve raid priority and rejected-command atomicity");
            commands.push_back(command(YakouReplayOperation::PassRaidResponse, 1));
            commands.push_back(command(YakouReplayOperation::ResolveRaid, 2));
            auto const first = ReplayYakou(replayProto, 1, decks, commands);
            auto const second = ReplayYakou(replayProto, 1, decks, commands);
            constexpr std::uint64_t expectedReferenceHash = 1532688032335505412ULL;
            Check(first.FinalState.ServerStateHash() == expectedReferenceHash,
                  "Canonical replay fingerprint must agree across supported platforms");
            Check(first.FinalState.ServerStateHash() == second.FinalState.ServerStateHash() &&
                      first.FinalState.ServerStateHash() != mid.FinalState.ServerStateHash() &&
                      first.Steps.size() == commands.size(),
                  "Identical command journals must produce the same full server state hash");
            Check(!first.FinalState.Player(1).Field[0] &&
                      first.FinalState.Player(2).Field[0]->Damage == 1,
                  "Replay must reproduce actual combat and persistent damage");
            auto const privateVariant = ReplayYakou(replayProto, 1,
                {decks[0], Deck(replayProto, {2002, 2001})}, {});
            auto const original = ReplayYakou(replayProto, 1, decks, {});
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
            auto const offered = match.ViewerSnapshot(1).at("Players")[0].at("SummonChoices");
            Check(!offered.empty() && offered[0].at("Method") == "Normal" &&
                      offered[0].at("RequiredCount") == 0 &&
                      offered[0].at("AvailableSlots").size() == 5,
                  "Owner snapshot must include authorized summon methods and slots");
            Check(!match.ViewerSnapshot(2).at("Players")[0].contains("SummonChoices") &&
                      !match.ViewerSnapshot(1).at("Players")[1].contains("SummonChoices"),
                  "Opponents must not receive another hand's summon candidates");
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
            auto const ownPending = match.ViewerSnapshot(1);
            Check(ownPending.contains("PendingSelection") &&
                      ownPending.at("PendingSelection").at("Status") == "Ready" &&
                      ownPending.at("PendingSelection").at("Operation") == "Mill" &&
                      ownPending.at("PendingSelection").at("Candidates").empty(),
                  "Owner snapshot must expose a valid targetless effect");
            Check(!match.ViewerSnapshot(2).contains("PendingSelection"),
                  "An opponent must not learn queued effect choices");
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
            // Raid-trigger attack bonuses and blocker-trigger damage reduction
            // must affect this combat before simultaneous damage is applied.
            auto withCombat = proto;
            auto& sample = withCombat.Decks["Decks"][0]["Cards"];
            sample[0] = 3003; // Include the two secondary-theme test cards.
            sample[1] = 3004;
            for (int blockerDefinition : {3004, 2006})
            {
                auto match = Make(withCombat, {3003}, {blockerDefinition});
                match.StartTurn(1);
                int const raider = match.Player(1).Hand[0];
                match.Summon(1, raider, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                int const blocker = match.Player(2).Hand[0];
                match.Summon(2, blocker, "Normal", {}, 1);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.StartTurn(2);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                Check(match.Phase() == YakouPhase::AssignBlockers &&
                          match.Attacks().size() == 1, "Raider must be ready");
                match.AssignBlockers(2, {{raider, blocker}});
                match.PassRaidResponse(2);
                match.PassRaidResponse(1);
                match.ResolveRaid(2);
                if (blockerDefinition == 3004)
                {
                    Check(match.Player(1).Field[0] &&
                              match.Player(1).Field[0]->Attack() == 3 &&
                              match.Player(1).Field[0]->Damage == 1,
                          "CurrentCombat attack bonus must not persist on surviving raider");
                }
                else
                {
                    // Card 2006 has 2 ATK and deals lethal combat damage to
                    // the raider's 2 HP. It is not a test fixture for survival.
                    Check(!match.Player(1).Field[0] &&
                              std::find(match.Player(1).Graveyard.begin(),
                                        match.Player(1).Graveyard.end(), raider) !=
                                  match.Player(1).Graveyard.end(),
                          "A 2-attack blocker must destroy the 2-HP raider");
                }
                Check(match.Player(2).Field[1] &&
                          match.Player(2).Field[1]->Damage == 3,
                      "3 printed attack + 1 raid bonus - 1 blocker reduction = 3 damage");
                Check(match.PendingEffects().empty(),
                      "Resolved combat-only triggers must not remain queued");
            }
        }
        {
            // Unknown combat trigger operations must be rejected without any
            // damage, turn advancement or partial trigger consumption.
            auto unsupported = proto;
            auto& sample = unsupported.Decks["Decks"][0]["Cards"];
            sample[0] = 3003;
            for (auto& card : unsupported.Cards["Cards"])
            {
                if (card["Id"] == 3003)
                    card["Effects"][0]["Actions"][0]["Operation"] = "UnsupportedRaidAction";
            }
            auto match = Make(unsupported, {3003}, {});
            match.StartTurn(1);
            int const raider = match.Player(1).Hand[0];
            match.Summon(1, raider, "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            match.BeginRaid(1);
            match.AssignBlockers(2, {});
            match.PassRaidResponse(2);
            match.PassRaidResponse(1);
            auto const before = match.ServerStateHash();
            Reject([&] { match.ResolveRaid(2); });
            Check(match.ServerStateHash() == before &&
                      match.Phase() == YakouPhase::RaidResponse,
                  "Unsupported combat effect must fail before any state mutation");
        }
        {
            // Use the real card 2102 kill-trigger shape on a short-countdown
            // normal monster fixture to exercise the combat event without
            // requiring a second unrelated manifestation setup.
            auto killed = proto;
            Json trigger;
            for (auto const& card : killed.Cards["Cards"])
                if (card["Id"] == 2102) trigger = card["Effects"][0];
            Check(trigger.is_object(), "Missing authored combat-kill trigger fixture");
            for (auto& card : killed.Cards["Cards"])
                if (card["Id"] == 2006)
                {
                    card["AttackInterval"] = 1;
                    card["Effects"].push_back(trigger);
                }
            auto makeCombat = [&](YakouPrototype const& data)
            {
                auto match = Make(data, {2006}, {2001});
                match.StartTurn(1);
                int const raider = match.Player(1).Hand[0];
                match.Summon(1, raider, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                int const blocker = match.Player(2).Hand[0];
                match.Summon(2, blocker, "Normal", {}, 1);
                match.ResolvePendingEffect(2); // The defender's on-summon Mill.
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                Check(match.Phase() == YakouPhase::AssignBlockers, "Ready combat-kill raider");
                match.AssignBlockers(2, {{raider, blocker}});
                match.PassRaidResponse(2);
                match.PassRaidResponse(1);
                return match;
            };
            {
                auto match = makeCombat(killed);
                int const raider = match.Player(1).Field[0]->InstanceId;
                match.ResolveRaid(2);
                Check(match.Player(1).Field[0] &&
                          match.Player(1).Field[0]->InstanceId == raider &&
                          match.Player(1).Field[0]->Countdown == 1 &&
                          match.Player(1).Field[0]->Damage == 1 &&
                          !match.Player(2).Field[1],
                      "Combat kill must apply SetCountdown after surviving raider reset");
                Check(match.PendingEffects().empty(),
                      "Immediately handled kill effect must not be queued for manual resolution");
            }
            {
                auto unsupported = killed;
                for (auto& card : unsupported.Cards["Cards"])
                    if (card["Id"] == 2006)
                        card["Effects"].back()["Actions"][0]["Operation"] = "UnsupportedKill";
                auto match = makeCombat(unsupported);
                auto const before = match.ServerStateHash();
                Reject([&] { match.ResolveRaid(2); });
                Check(match.ServerStateHash() == before,
                      "Unsupported kill effect must roll back combat and leave response state intact");
            }
            {
                auto match = Make(killed, {2006}, {});
                match.StartTurn(1);
                int const raider = match.Player(1).Hand[0];
                match.Summon(1, raider, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.AssignBlockers(2, {});
                match.PassRaidResponse(2);
                match.PassRaidResponse(1);
                match.ResolveRaid(2);
                Check(match.Player(1).Field[0] &&
                          match.Player(1).Field[0]->Countdown == 1,
                      "Unblocked hero damage must not dispatch a combat-kill trigger");
                Check(std::none_of(match.Journal().begin(), match.Journal().end(),
                                   [](std::string const& line)
                                   { return line.find("CombatKillCountdown:") == 0; }),
                      "No combat-kill effect should resolve without a destroyed enemy");
            }
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
            auto const ownerSelection = match.ViewerSnapshot(1).at("PendingSelection");
            Check(ownerSelection.at("CandidateCards").size() == choice.Candidates.size(),
                  "The choice owner must receive visible definitions for the revealed cards");
            for (std::size_t i = 0; i < choice.Candidates.size(); ++i)
            {
                Check(ownerSelection.at("CandidateCards")[i].at("Id") == choice.Candidates[i] &&
                          ownerSelection.at("CandidateCards")[i].at("DefinitionId").get<int>() > 0,
                      "Each revealed candidate must have its own definition");
            }
            auto const enemySnapshot = match.ViewerSnapshot(2);
            Check(!enemySnapshot.contains("PendingSelection"),
                  "The opposing viewer must not receive private selection cards");
            auto const enemyProjection = enemySnapshot.dump();
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
        {
            // A no-cost main-phase spell must enter the effect queue through a
            // real hand instance and preserve server authority throughout.
            auto match = Make(proto, {2009, 2009, 2010, 2001, 2002}, {2009});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            int const spell = hand[0];
            int const repeat = hand[1];
            int const paid = hand[2];
            auto const wrongPlayer = match.ServerStateHash();
            Reject([&] { match.ActivateMainSpell(2, spell); });
            Reject([&] { match.ActivateMainSpell(1, hand[3]); });
            Reject([&] { match.ActivateMainSpell(1, paid); });
            Reject([&] { match.ActivateMainSpell(1, match.Player(2).Hand[0]); });
            Check(match.ServerStateHash() == wrongPlayer,
                  "Illegal, paid or opponent spell activations must be atomic");
            auto const priorDeck = match.Player(1).Deck.size();
            auto const priorGrave = match.Player(1).Graveyard.size();
            match.ActivateMainSpell(1, spell);
            Check(std::find(match.Player(1).Hand.begin(),
                            match.Player(1).Hand.end(), spell) == match.Player(1).Hand.end() &&
                      match.Player(1).Graveyard.back() == spell &&
                      match.PendingEffects().size() == 1 &&
                      match.PendingEffectChoices(1).Operation == "Mill",
                  "Free spell activation must move its card and queue the authored OnResolve");
            auto const beforePending = match.ServerStateHash();
            Reject([&] { match.ActivateMainSpell(1, repeat); });
            Check(match.ServerStateHash() == beforePending,
                  "Cannot activate another spell with an unresolved pending effect");
            match.ResolvePendingEffect(1);
            Check(match.PendingEffects().empty() &&
                      match.Player(1).Deck.size() == priorDeck - 2 &&
                      match.Player(1).Graveyard.size() == priorGrave + 3,
                  "Spell resolution must use the same authoritative queued effect");
            auto const beforeLimit = match.ServerStateHash();
            Reject([&] { match.ActivateMainSpell(1, repeat); });
            Check(match.ServerStateHash() == beforeLimit,
                  "Printed card-name per-turn spell limit must reject duplicate activation");
        }
        {
            // Targeted main spells reuse the existing server-owned target
            // candidate/validation path rather than accepting a supplied ID.
            auto authored = proto;
            for (auto& id : authored.Decks["Decks"][0]["Cards"])
            {
                if (id == 3020) { id = 3011; break; }
            }
            auto match = Make(authored, {2001, 3011, 2009, 2002, 3001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            auto const noTarget = match.ServerStateHash();
            Reject([&] { match.ActivateMainSpell(1, hand[1]); });
            Check(match.ServerStateHash() == noTarget,
                  "Targeted spell cannot consume a hand card without legal targets");
            match.Summon(1, hand[0], "Normal", {}, 0);
            match.ResolvePendingEffect(1);
            match.ActivateMainSpell(1, hand[1]);
            auto const selection = match.PendingEffectChoices(1);
            Check(selection.Operation == "ModifyCountdown" &&
                      selection.Candidates == std::vector<int>{hand[0]},
                  "Activated spell's targets must be authoritative friendly field instances");
            Reject([&] { match.ResolvePendingEffect(1, match.Player(2).Hand[0]); });
            match.ResolvePendingEffect(1, hand[0]);
            Check(match.PendingEffects().empty() &&
                      match.Player(1).Field[0]->Countdown == 1,
                  "Targeted main spell must apply only to a validated field card");
        }
        {
            // The replay records the actual spell activation and effect
            // resolution; repeated same-name activation remains a rejected
            // command without altering the deterministic private state.
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2009, 2009}), Deck(proto, {})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::ActivateMainSpell;
            cast.Player = 1;
            cast.CardId = 1;
            YakouReplayCommand resolve;
            resolve.Operation = YakouReplayOperation::ResolvePendingEffect;
            resolve.Player = 1;
            std::vector<YakouReplayCommand> const commands{start, cast, resolve,
                                                            YakouReplayCommand{cast}};
            auto const first = ReplayYakou(proto, 1, decks, commands);
            auto const again = ReplayYakou(proto, 1, decks, commands);
            Check(first.Steps.size() == 4 && first.Steps[1].Accepted &&
                      first.Steps[2].Accepted && !first.Steps[3].Accepted &&
                      first.Steps[3].BeforeHash == first.Steps[3].AfterHash &&
                      first.FinalState.ServerStateHash() ==
                          again.FinalState.ServerStateHash(),
                  "Main spell replay must preserve deterministic acceptance and rejection");
        }
        {
            // The authored map pays one other hand card before revealing the
            // top three only to its controller. Invalid costs roll back.
            auto match = Make(proto, {3012, 2001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            auto const initial = match.ServerStateHash();
            Reject([&] { match.ActivateMainSpell(1, hand[0]); });
            Reject([&] { match.ActivateMainSpell(1, hand[0], {hand[0]}); });
            Reject([&] { match.ActivateMainSpell(1, hand[0], {hand[1], hand[2]}); });
            Check(match.ServerStateHash() == initial, "Invalid map costs must not mutate state");
            auto const offered = match.ViewerSnapshot(1).at("Players")[0].at("PaidSpellOptions");
            auto const opponentView = match.ViewerSnapshot(2);
            Check(offered.size() == 1 && offered[0].at("CardId") == hand[0] &&
                      !opponentView.at("Players")[0].contains("PaidSpellOptions"),
                  "Only the owner may see map cost candidates");
            (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, hand[0], 64, {hand[1]});
            auto const selection = match.ViewerSnapshot(1).at("PendingSelection");
            Check(selection.at("CandidateCards").size() == 3 &&
                      !match.ViewerSnapshot(2).contains("PendingSelection") &&
                      match.Player(1).Graveyard.size() == 2,
                  "Paid map must reveal only its authorized top cards");
            int const selected = selection.at("Candidates")[1].get<int>();
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, selected);
            Check(match.Player(1).Hand.back() == selected && match.PendingEffects().empty(),
                  "Selected map card must reach the owner's hand");

            std::array<std::vector<int>, 2> const decks{Deck(proto, {3012, 2001}), Deck(proto, {})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::ActivateMainSpellAndRunAutomaticTriggers;
            cast.Player = 1;
            cast.CardId = hand[0];
            cast.Cards = {hand[1]};
            YakouReplayCommand choose;
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = selected;
            auto const replay = ReplayYakou(proto, 1, decks, {start, cast, choose});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Paid map activation and choice must replay deterministically");
        }
        {
            // Paid main spells pay at activation, then wait for opponent
            // priority. A private deck choice is created only after passes.
            auto match = Make(proto, {3012, 2001}, {3020, 2001, 2002});
            match.StartTurn(1);
            int const spell = match.Player(1).Hand[0];
            int const cost = match.Player(1).Hand[1];
            auto const before = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(1, spell); });
            Reject([&] { match.BeginSpellResponse(1, spell, 0, {spell}); });
            Check(match.ServerStateHash() == before,
                  "Rejected paid spell must preserve all zones and priority");
            match.BeginSpellResponse(1, spell, 0, {cost});
            Check(match.Phase() == YakouPhase::SpellResponse &&
                      match.PriorityPlayer() == 2 &&
                      match.Player(1).Graveyard.size() == 2 &&
                      !match.ViewerSnapshot(1).contains("PendingSelection"),
                  "Paid spell must open priority after committing its cost");
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(match.Phase() == YakouPhase::Main &&
                      match.ViewerSnapshot(1).at("PendingSelection").at("CandidateCards").size() == 3 &&
                      !match.ViewerSnapshot(2).contains("PendingSelection"),
                  "Private map choice must begin only after both players pass");
            auto const target = match.ViewerSnapshot(1).at("PendingSelection")
                                    .at("Candidates")[0].get<int>();
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, target);
            Check(match.PendingEffects().empty() && match.Player(1).Hand.back() == target,
                  "Paid spell choice must finish after the response window");

            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {3012, 2001}), Deck(proto, {3020, 2001, 2002})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = spell;
            cast.Cards = {cost};
            YakouReplayCommand passOpponent;
            passOpponent.Operation = YakouReplayOperation::PassSpellResponse;
            passOpponent.Player = 2;
            auto passOwner = passOpponent;
            passOwner.Player = 1;
            YakouReplayCommand choose;
            choose.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            choose.Player = 1;
            choose.TargetId = target;
            auto const replay = ReplayYakou(proto, 1, decks,
                                            {start, cast, passOpponent, passOwner, choose});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Paid spell response and private choice must replay deterministically");

            auto countered = Make(proto, {3012, 2001}, {3020, 2001, 2002});
            countered.StartTurn(1);
            countered.BeginSpellResponse(1, countered.Player(1).Hand[0], 0,
                                         {countered.Player(1).Hand[1]});
            auto const counterHand = countered.Player(2).Hand;
            countered.RespondWithNegation(2, counterHand[0],
                                          {counterHand[1], counterHand[2]});
            countered.PassSpellResponse(1);
            countered.PassSpellResponse(2);
            Check(countered.Phase() == YakouPhase::Main &&
                      countered.PendingEffects().empty() &&
                      !countered.ViewerSnapshot(1).contains("PendingSelection") &&
                      countered.Player(1).Graveyard.size() == 2,
                  "Negated paid spell keeps its cost but creates no private choice");
        }
        {
            // The enemy spell's accepted target event activates 3009 before
            // resolution. Card 3017 may then protect that exact own target.
            auto targetedProto = proto;
            for (auto& id : targetedProto.Decks["Decks"][0]["Cards"])
            {
                if (id == 3013)
                {
                    id = 3009;
                }
                else if (id == 3020)
                {
                    id = 3017;
                }
            }
            auto match = Make(targetedProto, {2001, 3009, 3017}, {2011, 2002});
            auto command = [](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand value;
                value.Operation = operation;
                value.Player = player;
                return value;
            };
            std::vector<YakouReplayCommand> replayCommands;
            match.StartTurn(1);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 1));
            auto const own = match.Player(1).Hand;
            auto const enemy = match.Player(2).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, own[0], "Normal", {}, 0);
            auto firstSummon = command(YakouReplayOperation::SummonAndRunAutomaticTriggers, 1);
            firstSummon.CardId = own[0];
            firstSummon.Method = "Normal";
            replayCommands.push_back(firstSummon);
            match.BeginRaid(1);
            replayCommands.push_back(command(YakouReplayOperation::BeginRaid, 1));
            match.StartTurn(2);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 2));
            match.BeginRaid(2);
            replayCommands.push_back(command(YakouReplayOperation::BeginRaid, 2));
            match.StartTurn(1);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 1));
            match.Summon(1, own[1], "Normal", {}, 1);
            auto secondSummon = command(YakouReplayOperation::Summon, 1);
            secondSummon.CardId = own[1];
            secondSummon.Method = "Normal";
            secondSummon.Slot = 1;
            replayCommands.push_back(secondSummon);
            bool targetingWindow = false;
            for (int step = 0; step < 20 && !targetingWindow; ++step)
            {
                int const active = match.ActivePlayer();
                if (match.Phase() == YakouPhase::TurnStart)
                {
                    match.StartTurn(active);
                    replayCommands.push_back(command(YakouReplayOperation::StartTurn, active));
                }
                if (match.Phase() == YakouPhase::Main)
                {
                    match.BeginRaid(active);
                    replayCommands.push_back(command(YakouReplayOperation::BeginRaid, active));
                }
                if (match.Phase() == YakouPhase::AssignBlockers)
                {
                    targetingWindow = active == 1 &&
                        std::any_of(match.Attacks().begin(), match.Attacks().end(),
                            [&](YakouAttack const& attack) { return attack.Card == own[1]; });
                    int const defending = 3 - active;
                    match.AssignBlockers(defending, {});
                    replayCommands.push_back(command(YakouReplayOperation::AssignBlockers, defending));
                    if (!targetingWindow)
                    {
                        match.PassRaidResponse(defending);
                        replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, defending));
                        match.PassRaidResponse(active);
                        replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, active));
                        match.ResolveRaid(defending);
                        replayCommands.push_back(command(YakouReplayOperation::ResolveRaid, defending));
                    }
                }
            }
            Check(targetingWindow, "3009 must be a raid target for enemy tactic");
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { match.RespondWithRaidTactic(2, enemy[0], 0, 0, {enemy[1]}); });
            Check(match.ServerStateHash() == beforeBadTarget &&
                      match.FilterEffectDamage(own[1], 3) == 3,
                  "Rejected target designation cannot trigger 3009");
            match.RespondWithRaidTactic(2, enemy[0], own[1], 0, {enemy[1]});
            auto targetSpell = command(YakouReplayOperation::RespondWithRaidTactic, 2);
            targetSpell.CardId = enemy[0];
            targetSpell.TargetId = own[1];
            targetSpell.Cards = {enemy[1]};
            replayCommands.push_back(targetSpell);
            Check(match.FilterEffectDamage(own[1], 3) == 2 &&
                      match.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[1]
                          .at("EffectDamageReductionThisChain") == 1 &&
                      std::find(match.Journal().begin(), match.Journal().end(),
                                "EnemySpellTargetConfirmed:" + std::to_string(own[1])) !=
                          match.Journal().end(),
                  "3009 must reduce effect damage at accepted target declaration");
            int const graveCost = match.Player(1).Graveyard[0];
            auto const beforeBadProtection = match.ServerStateHash();
            Reject([&] { match.RespondWithEffectProtection(1, own[2], own[0], graveCost); });
            Check(match.ServerStateHash() == beforeBadProtection,
                  "3017 must reject an own monster not targeted by the enemy spell");
            match.RespondWithEffectProtection(1, own[2], own[1], graveCost);
            auto protect = command(YakouReplayOperation::RespondWithEffectProtection, 1);
            protect.CardId = own[2];
            protect.TargetId = own[1];
            protect.Cards = {graveCost};
            replayCommands.push_back(protect);
            Check(match.Player(1).Exile.back() == graveCost &&
                      match.PriorityPlayer() == 2,
                  "3017 must pay one graveyard card and pass priority");
            match.PassRaidResponse(2);
            replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, 2));
            match.PassRaidResponse(1);
            replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, 1));
            Check(match.FilterEffectDamage(own[1], 3) == 0,
                  "3017 complete effect-damage prevention must override 3009 reduction");
            match.ResolveRaid(2);
            replayCommands.push_back(command(YakouReplayOperation::ResolveRaid, 2));
            Check(match.FilterEffectDamage(own[1], 3) == 3 &&
                      std::find(match.Journal().begin(), match.Journal().end(),
                                "EnemySpellTargetConfirmed:" + std::to_string(own[1])) !=
                          match.Journal().end(),
                  "Chain protection must expire while confirmed target event remains in journal");
            std::array<std::vector<int>, 2> const replayDecks{
                Deck(targetedProto, {2001, 3009, 3017}),
                Deck(targetedProto, {2011, 2002})};
            auto const replay = ReplayYakou(targetedProto, 1, replayDecks, replayCommands);
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "3009 target declaration and 3017 protection must replay exactly");
        }
        {
            // Card 2101 needs an explicit HyakkiYagyo graveyard payment in
            // raid priority. Only lethal post-reduction damage can pierce.
            auto piercingProto = proto;
            for (auto& id : piercingProto.Decks["Decks"][0]["Cards"])
            {
                if (id == 3013)
                {
                    id = 3016;
                }
                else if (id == 3020)
                {
                    id = 4018;
                }
            }
            auto match = Make(piercingProto, {2001, 2009, 2003}, {2002, 3016, 4018});
            auto command = [](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand value;
                value.Operation = operation;
                value.Player = player;
                return value;
            };
            std::vector<YakouReplayCommand> replayCommands;
            match.StartTurn(1);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 1));
            auto const hand = match.Player(1).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            auto summonFirst = command(YakouReplayOperation::SummonAndRunAutomaticTriggers, 1);
            summonFirst.CardId = hand[0];
            summonFirst.Method = "Normal";
            replayCommands.push_back(summonFirst);
            match.BeginSpellResponse(1, hand[1]);
            auto mill = command(YakouReplayOperation::BeginSpellResponse, 1);
            mill.CardId = hand[1];
            replayCommands.push_back(mill);
            match.PassSpellResponse(2);
            replayCommands.push_back(command(YakouReplayOperation::PassSpellResponse, 2));
            match.PassSpellResponse(1);
            replayCommands.push_back(command(YakouReplayOperation::PassSpellResponse, 1));
            match.BeginRaid(1);
            replayCommands.push_back(command(YakouReplayOperation::BeginRaid, 1));
            match.StartTurn(2);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 2));
            int const blocker = match.Player(2).Hand[0];
            match.Summon(2, blocker, "Normal", {}, 0);
            auto summonBlocker = command(YakouReplayOperation::Summon, 2);
            summonBlocker.CardId = blocker;
            summonBlocker.Method = "Normal";
            replayCommands.push_back(summonBlocker);
            match.BeginRaid(2);
            replayCommands.push_back(command(YakouReplayOperation::BeginRaid, 2));
            match.StartTurn(1);
            replayCommands.push_back(command(YakouReplayOperation::StartTurn, 1));
            auto const grave = match.Player(1).Graveyard;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[2], "Necromancy",
                                                       {grave[0], grave[1]}, 1);
            auto summonNecro = command(YakouReplayOperation::SummonAndRunAutomaticTriggers, 1);
            summonNecro.CardId = hand[2];
            summonNecro.Method = "Necromancy";
            summonNecro.Cards = {grave[0], grave[1]};
            summonNecro.Slot = 1;
            replayCommands.push_back(summonNecro);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, 0);
            replayCommands.push_back(command(YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers, 1));
            auto const beforeManifest = match;
            match.Manifest(1, hand[2], 2101);
            auto manifest = command(YakouReplayOperation::Manifest, 1);
            manifest.CardId = hand[2];
            manifest.DefinitionId = 2101;
            replayCommands.push_back(manifest);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, 0);
            replayCommands.push_back(command(YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers, 1));
            int const raider = hand[2];
            auto damagedFixture = beforeManifest;
            bool assigned = false;
            for (int step = 0; step < 24 && !assigned; ++step)
            {
                int const active = match.ActivePlayer();
                if (match.Phase() == YakouPhase::TurnStart)
                {
                    match.StartTurn(active);
                    replayCommands.push_back(command(YakouReplayOperation::StartTurn, active));
                }
                if (match.Phase() == YakouPhase::Main)
                {
                    match.BeginRaid(active);
                    replayCommands.push_back(command(YakouReplayOperation::BeginRaid, active));
                }
                if (match.Phase() == YakouPhase::AssignBlockers)
                {
                    bool const hasRaider = active == 1 &&
                        std::any_of(match.Attacks().begin(), match.Attacks().end(),
                            [&](YakouAttack const& attack) { return attack.Card == raider; });
                    int const defending = 3 - active;
                    match.AssignBlockers(defending, hasRaider
                        ? std::vector<std::pair<int, int>>{{raider, blocker}}
                        : std::vector<std::pair<int, int>>{});
                    auto assignment = command(YakouReplayOperation::AssignBlockers, defending);
                    if (hasRaider)
                    {
                        assignment.Blockers = {{raider, blocker}};
                    }
                    replayCommands.push_back(assignment);
                    if (hasRaider)
                    {
                        assigned = true;
                    }
                    else
                    {
                        match.PassRaidResponse(defending);
                        replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, defending));
                        match.PassRaidResponse(active);
                        replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, active));
                        match.ResolveRaid(defending);
                        replayCommands.push_back(command(YakouReplayOperation::ResolveRaid, defending));
                    }
                }
            }
            Check(assigned, "2101 must reach a blocked raid");
            auto const prepared = match;
            auto noCost = prepared;
            int const noCostLife = noCost.Player(2).Life;
            noCost.PassRaidResponse(2);
            noCost.PassRaidResponse(1);
            noCost.ResolveRaid(2);
            Check(noCost.Player(2).Life == noCostLife - 1 &&
                      std::find_if(noCost.Journal().begin(), noCost.Journal().end(),
                          [](std::string const& line)
                          { return line.starts_with("PiercingDamage:"); }) == noCost.Journal().end(),
                  "Unpaid 2101 must not pierce a destroyed blocker");
            match.PassRaidResponse(2);
            replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, 2));
            auto const beforeInvalidCost = match.ServerStateHash();
            Reject([&] { match.RespondWithPiercing(1, raider, blocker); });
            Check(match.ServerStateHash() == beforeInvalidCost,
                  "Invalid 2101 cost must not change chain or zones");
            int const cost = match.Player(1).Graveyard[0];
            int const lifeBefore = match.Player(2).Life;
            match.RespondWithPiercing(1, raider, cost);
            auto payPiercing = command(YakouReplayOperation::RespondWithPiercing, 1);
            payPiercing.CardId = raider;
            payPiercing.Cards = {cost};
            replayCommands.push_back(payPiercing);
            Check(match.Player(1).Exile.back() == cost && match.PriorityPlayer() == 2,
                  "2101 must exile its cost and pass priority");
            match.PassRaidResponse(2);
            replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, 2));
            match.PassRaidResponse(1);
            replayCommands.push_back(command(YakouReplayOperation::PassRaidResponse, 1));
            match.ResolveRaid(2);
            replayCommands.push_back(command(YakouReplayOperation::ResolveRaid, 2));
            Check(match.Player(2).Life == lifeBefore - 2 &&
                      std::find(match.Journal().begin(), match.Journal().end(),
                                "PiercingDamage:1") != match.Journal().end() &&
                      !match.Player(2).Field[0],
                  "4 attack against 3 HP must pierce one; another unblocked raid deals one");
            std::array<std::vector<int>, 2> const replayDecks{
                Deck(piercingProto, {2001, 2009, 2003}),
                Deck(piercingProto, {2002, 3016, 4018})};
            auto const replay = ReplayYakou(piercingProto, 1, replayDecks, replayCommands);
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2101 cost, response priority, combat and piercing must replay exactly");

            auto reduced = prepared;
            int const shield = reduced.Player(2).Hand[0];
            reduced.RespondWithRaidShield(2, shield, blocker);
            reduced.RespondWithPiercing(1, raider, cost);
            reduced.PassRaidResponse(2);
            reduced.PassRaidResponse(1);
            int const reducedLife = reduced.Player(2).Life;
            reduced.ResolveRaid(2);
            Check(reduced.Player(2).Field[0] &&
                      reduced.Player(2).Life == reducedLife - 1 &&
                      std::find(reduced.Journal().begin(), reduced.Journal().end(),
                                "PiercingDamage:1") == reduced.Journal().end(),
                  "Shielded blocker survives; only the other unblocked raid damages hero");

            auto stacked = prepared;
            int const firstShield = stacked.Player(2).Hand[0];
            int const secondShield = stacked.Player(2).Hand[1];
            stacked.RespondWithRaidShield(2, firstShield, blocker);
            stacked.RespondWithPiercing(1, raider, cost);
            stacked.RespondWithRaidShield(2, secondShield, 0);
            stacked.PassRaidResponse(1);
            stacked.PassRaidResponse(2);
            int const stackedLife = stacked.Player(2).Life;
            stacked.ResolveRaid(2);
            Check(stacked.Player(2).Field[0] &&
                      stacked.Player(2).Field[0]->Damage == 1 &&
                      stacked.Player(2).Life == stackedLife - 1,
                  "Two legal shields must add once before piercing is calculated");

            bool earlierRaid = false;
            for (int step = 0; step < 16 && !earlierRaid; ++step)
            {
                int const active = damagedFixture.ActivePlayer();
                if (damagedFixture.Phase() == YakouPhase::TurnStart)
                {
                    damagedFixture.StartTurn(active);
                }
                if (damagedFixture.Phase() == YakouPhase::Main)
                {
                    damagedFixture.BeginRaid(active);
                }
                if (damagedFixture.Phase() == YakouPhase::AssignBlockers)
                {
                    earlierRaid = active == 1 &&
                        std::any_of(damagedFixture.Attacks().begin(), damagedFixture.Attacks().end(),
                            [&](YakouAttack const& attack) { return attack.Card == hand[0]; });
                    if (!earlierRaid)
                    {
                        int const defending = 3 - active;
                        damagedFixture.AssignBlockers(defending, {});
                        damagedFixture.PassRaidResponse(defending);
                        damagedFixture.PassRaidResponse(active);
                        damagedFixture.ResolveRaid(defending);
                    }
                }
            }
            Check(earlierRaid, "Earlier raider must reach blocker before 2101 manifests");
            damagedFixture.AssignBlockers(2, {{hand[0], blocker}});
            damagedFixture.PassRaidResponse(2);
            damagedFixture.PassRaidResponse(1);
            damagedFixture.ResolveRaid(2);
            Check(damagedFixture.Player(2).Field[0] &&
                      damagedFixture.Player(2).Field[0]->Hp() == 2,
                  "Earlier combat must wound the blocker once");
            for (int step = 0; step < 12 &&
                !(damagedFixture.ActivePlayer() == 1 &&
                  damagedFixture.Phase() == YakouPhase::Main); ++step)
            {
                int const active = damagedFixture.ActivePlayer();
                if (damagedFixture.Phase() == YakouPhase::TurnStart)
                {
                    damagedFixture.StartTurn(active);
                }
                if (damagedFixture.Phase() == YakouPhase::Main && active != 1)
                {
                    damagedFixture.BeginRaid(active);
                }
                if (damagedFixture.Phase() == YakouPhase::AssignBlockers)
                {
                    int const defending = 3 - active;
                    damagedFixture.AssignBlockers(defending, {});
                    damagedFixture.PassRaidResponse(defending);
                    damagedFixture.PassRaidResponse(active);
                    damagedFixture.ResolveRaid(defending);
                }
            }
            Check(damagedFixture.ActivePlayer() == 1 &&
                      damagedFixture.Phase() == YakouPhase::Main,
                  "Wounded-blocker fixture must return to manifest controller");
            damagedFixture.Manifest(1, raider, 2101);
            (void)damagedFixture.ResolveSelectedEffectAndRunAutomaticTriggers(1, 0);
            bool damagedRaid = false;
            for (int step = 0; step < 24 && !damagedRaid; ++step)
            {
                int const active = damagedFixture.ActivePlayer();
                if (damagedFixture.Phase() == YakouPhase::TurnStart)
                {
                    damagedFixture.StartTurn(active);
                }
                if (damagedFixture.Phase() == YakouPhase::Main)
                {
                    damagedFixture.BeginRaid(active);
                }
                if (damagedFixture.Phase() == YakouPhase::AssignBlockers)
                {
                    bool const hasRaider = active == 1 &&
                        std::any_of(damagedFixture.Attacks().begin(), damagedFixture.Attacks().end(),
                            [&](YakouAttack const& attack) { return attack.Card == raider; });
                    int const defending = 3 - active;
                    damagedFixture.AssignBlockers(defending, hasRaider
                        ? std::vector<std::pair<int, int>>{{raider, blocker}}
                        : std::vector<std::pair<int, int>>{});
                    if (hasRaider)
                    {
                        damagedRaid = true;
                    }
                    else
                    {
                        damagedFixture.PassRaidResponse(defending);
                        damagedFixture.PassRaidResponse(active);
                        damagedFixture.ResolveRaid(defending);
                    }
                }
            }
            Check(damagedRaid && damagedFixture.Player(2).Field[0] &&
                      damagedFixture.Player(2).Field[0]->Hp() == 2,
                  "Prior combat must leave blocker with two remaining HP");
            damagedFixture.PassRaidResponse(2);
            damagedFixture.RespondWithPiercing(1, raider,
                                               damagedFixture.Player(1).Graveyard[0]);
            damagedFixture.PassRaidResponse(2);
            damagedFixture.PassRaidResponse(1);
            int const damagedLife = damagedFixture.Player(2).Life;
            damagedFixture.ResolveRaid(2);
            Check(damagedFixture.Player(2).Life == damagedLife - 2 &&
                      std::find(damagedFixture.Journal().begin(), damagedFixture.Journal().end(),
                                "PiercingDamage:2") != damagedFixture.Journal().end(),
                  "4 attack against a wounded 2-HP blocker must pierce two");
        }
        {
            // Card 3015 waives one failed manifestation condition this turn,
            // but its discard cost and the manifestation's Ki still apply.
            auto waiverProto = proto;
            for (auto& id : waiverProto.Decks["Decks"][0]["Cards"])
            {
                if (id == 3013)
                {
                    id = 3015;
                    break;
                }
            }
            for (auto& card : waiverProto.Cards["Cards"])
            {
                if (card["Id"] == 2101)
                {
                    card["SummonCostJson"]["Manifestation"]["Condition"]
                        ["ArchetypeMonsterTotalAcrossZones"]["AtLeast"] = 5;
                }
            }
            auto match = Make(waiverProto, {2001, 2003, 3015, 2002, 3001}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0);
            match.BeginRaid(1);
            match.StartTurn(2);
            match.BeginRaid(2);
            match.StartTurn(1);
            (void)match.SummonAndRunAutomaticTriggers(1, hand[1], "Necromancy",
                                                       {match.Player(1).Graveyard[0],
                                                        match.Player(1).Graveyard[1]}, 1);
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, 0);
            auto const beforeMissingCondition = match.ServerStateHash();
            Reject([&] { match.Manifest(1, hand[1], 2101); });
            Check(match.ServerStateHash() == beforeMissingCondition,
                  "Unpaid 3015 waiver cannot skip a manifest condition");
            match.BeginSpellResponse(1, hand[2], 0, {hand[3], hand[4]});
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(match.ViewerSnapshot(1).at("Players")[0]
                      .at("ManifestConditionWaivers") == 1,
                  "3015 must grant exactly one condition waiver");
            auto satisfied = match;
            for (auto& card : waiverProto.Cards["Cards"])
            {
                if (card["Id"] == 2101)
                {
                    auto& conditions = card["SummonCostJson"]["Manifestation"]["Condition"];
                    conditions["ArchetypeMonsterTotalAcrossZones"]["AtLeast"] = 3;
                    satisfied.Manifest(1, hand[1], 2101);
                    Check(satisfied.ViewerSnapshot(1).at("Players")[0]
                              .at("ManifestConditionWaivers") == 1,
                          "Satisfied manifest conditions must not consume 3015");
                    conditions["ArchetypeMonsterTotalAcrossZones"]["AtLeast"] = 5;
                    conditions["WasDevourSummoned"] = true;
                    auto const twoFailures = match.ServerStateHash();
                    Reject([&] { match.Manifest(1, hand[1], 2101); });
                    Check(match.ServerStateHash() == twoFailures &&
                              match.ViewerSnapshot(1).at("Players")[0]
                                  .at("ManifestConditionWaivers") == 1,
                          "One 3015 waiver cannot ignore two failed conditions");
                    conditions.erase("WasDevourSummoned");
                    break;
                }
            }
            int const kiBefore = match.Player(1).Ki;
            match.Manifest(1, hand[1], 2101);
            Check(match.Player(1).Field[1]->DefinitionId == 2101 &&
                      match.Player(1).Ki == kiBefore - 2 &&
                      match.ViewerSnapshot(1).at("Players")[0]
                          .at("ManifestConditionWaivers") == 0,
                  "3015 must consume the waiver and preserve printed Ki cost");
        }
        {
            // Card 2012 persists through turns and consumes its shield only
            // when the chosen monster actually receives positive damage.
            auto shieldProto = proto;
            for (auto& id : shieldProto.Decks["Decks"][0]["Cards"])
            {
                if (id == 3013)
                {
                    id = 3004;
                    break;
                }
            }
            auto match = Make(shieldProto, {2001, 2012}, {3004});
            match.StartTurn(1);
            int const protectedUnit = match.Player(1).Hand[0];
            int const spell = match.Player(1).Hand[1];
            (void)match.SummonAndRunAutomaticTriggers(1, protectedUnit, "Normal", {}, 0);
            int const cost = match.Player(1).Graveyard[0];
            auto const beforeBadCost = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(1, spell, 0, {spell}); });
            Check(match.ServerStateHash() == beforeBadCost,
                  "2012 must roll back invalid necromancy cost");
            match.BeginSpellResponse(1, spell, 0, {cost});
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(match.ViewerSnapshot(1).at("PendingSelection").at("Candidates").size() == 1,
                  "2012 must ask for a living friendly monster after response");
            auto const beforeBadTarget = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(
                1, match.Player(2).Hand[0]); });
            Check(match.ServerStateHash() == beforeBadTarget,
                  "2012 must reject enemy targets atomically");
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(1, protectedUnit);
            Check(match.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[0]
                      .at("NextDamageShield") == 2,
                  "2012 shield must be visible on its public field target");
            std::array<std::vector<int>, 2> const shieldDecks{
                Deck(shieldProto, {2001, 2012}), Deck(shieldProto, {3004})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = protectedUnit;
            summon.Method = "Normal";
            YakouReplayCommand cast;
            cast.Operation = YakouReplayOperation::BeginSpellResponse;
            cast.Player = 1;
            cast.CardId = spell;
            cast.Cards = {cost};
            YakouReplayCommand passTwo;
            passTwo.Operation = YakouReplayOperation::PassSpellResponse;
            passTwo.Player = 2;
            auto passOne = passTwo;
            passOne.Player = 1;
            YakouReplayCommand select;
            select.Operation = YakouReplayOperation::ResolveSelectedEffectAndRunAutomaticTriggers;
            select.Player = 1;
            select.TargetId = protectedUnit;
            auto const replay = ReplayYakou(shieldProto, 1, shieldDecks,
                {start, summon, cast, passTwo, passOne, select});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "2012 private selection and persistent shield must replay exactly");
            int const blocker = match.Player(2).Hand[0];
            int protectedRaids = 0;
            bool blockerSummoned = false;
            for (int step = 0; step < 24 && protectedRaids < 2; ++step)
            {
                int const active = match.ActivePlayer();
                if (match.Phase() == YakouPhase::TurnStart)
                {
                    match.StartTurn(active);
                    if (active == 2 && !blockerSummoned)
                    {
                        match.Summon(2, blocker, "Normal", {}, 0);
                        blockerSummoned = true;
                    }
                }
                if (match.Phase() == YakouPhase::Main)
                {
                    match.BeginRaid(active);
                }
                if (match.Phase() == YakouPhase::AssignBlockers)
                {
                    int const defending = 3 - active;
                    match.AssignBlockers(defending, active == 1
                        ? std::vector<std::pair<int, int>>{{protectedUnit, blocker}}
                        : std::vector<std::pair<int, int>>{});
                    match.PassRaidResponse(defending);
                    match.PassRaidResponse(active);
                    match.ResolveRaid(defending);
                    if (active == 1)
                    {
                        ++protectedRaids;
                        Check(match.Player(1).Field[0] &&
                                  match.Player(1).Field[0]->Damage == (protectedRaids == 1 ? 0 : 1),
                              "2012 must prevent only the first positive damage event");
                    }
                }
            }
            Check(protectedRaids == 2 &&
                      !match.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[0]
                          .contains("NextDamageShield"),
                  "2012 shield must be consumed after its first positive hit");

            auto zeroDamage = shieldProto;
            for (auto& card : zeroDamage.Cards["Cards"])
            {
                if (card["Id"] == 3004)
                {
                    card["Attack"] = 0; // Test fixture: no incoming damage event.
                }
            }
            auto noHit = Make(zeroDamage, {2001, 2012}, {3004});
            noHit.StartTurn(1);
            int const noHitUnit = noHit.Player(1).Hand[0];
            int const noHitSpell = noHit.Player(1).Hand[1];
            (void)noHit.SummonAndRunAutomaticTriggers(1, noHitUnit, "Normal", {}, 0);
            noHit.BeginSpellResponse(1, noHitSpell, 0, {noHit.Player(1).Graveyard[0]});
            noHit.PassSpellResponse(2);
            noHit.PassSpellResponse(1);
            (void)noHit.ResolveSelectedEffectAndRunAutomaticTriggers(1, noHitUnit);
            int const noHitBlocker = noHit.Player(2).Hand[0];
            for (int step = 0; step < 12 && noHit.Phase() != YakouPhase::AssignBlockers; ++step)
            {
                int const active = noHit.ActivePlayer();
                if (noHit.Phase() == YakouPhase::TurnStart)
                {
                    noHit.StartTurn(active);
                    if (active == 2 && !noHit.Player(2).Field[0])
                    {
                        noHit.Summon(2, noHitBlocker, "Normal", {}, 0);
                    }
                }
                if (noHit.Phase() == YakouPhase::Main)
                {
                    noHit.BeginRaid(active);
                }
            }
            Check(noHit.Phase() == YakouPhase::AssignBlockers && noHit.ActivePlayer() == 1,
                  "Zero-damage fixture must reach protected attack");
            noHit.AssignBlockers(2, {{noHitUnit, noHitBlocker}});
            noHit.PassRaidResponse(2);
            noHit.PassRaidResponse(1);
            noHit.ResolveRaid(2);
            Check(noHit.ViewerSnapshot(1).at("Players")[0].at("FieldSlots")[0]
                      .at("NextDamageShield") == 2,
                  "Zero incoming damage must not consume 2012 protection");
        }
        {
            // A free main-phase spell waits for both players to pass before
            // resolving. The wrong priority must not mutate any private state.
            auto match = Make(proto, {2009, 2009}, {3020, 2001, 2002, 3001, 2004});
            match.StartTurn(1);
            int const spell = match.Player(1).Hand[0];
            int const counter = match.Player(2).Hand[0];
            auto const costOne = match.Player(2).Hand[1];
            auto const costTwo = match.Player(2).Hand[2];
            auto const deckSize = match.Player(1).Deck.size();
            match.BeginSpellResponse(1, spell);
            auto const counterOptions = match.ViewerSnapshot(2).at("ResponseOptions");
            Check(counterOptions.size() == 1 && counterOptions[0].at("CardId") == counter &&
                      counterOptions[0].at("CostCount") == 2 &&
                      counterOptions[0].at("CostCandidates").size() == 4 &&
                      match.ViewerSnapshot(1).at("ResponseOptions").empty(),
                  "Only the priority holder receives private counterspell choices");
            Check(match.Phase() == YakouPhase::SpellResponse &&
                      match.PriorityPlayer() == 2 &&
                      match.PendingEffects().empty() &&
                      match.Player(1).Graveyard.back() == spell &&
                      match.ViewerSnapshot(2).at("Phase") == "SpellResponse",
                  "Original spell must open opponent priority without executing its effect");
            auto const beforeSpoof = match.ServerStateHash();
            Reject([&] { match.PassSpellResponse(1); });
            Reject([&] { match.RespondWithNegation(1, spell, {costOne, costTwo}); });
            Reject([&] { match.RespondWithNegation(2, counter, {costOne, costOne}); });
            Reject([&] { match.RespondWithNegation(2, counter, {counter, costTwo}); });
            Check(match.ServerStateHash() == beforeSpoof,
                  "Unauthorized priority or invalid discard cost must be atomic");
            match.RespondWithNegation(2, counter, {costOne, costTwo});
            auto const publicStack = match.ViewerSnapshot(1).at("ResponseStack");
            Check(publicStack.size() == 2 && publicStack[0].at("Card").at("Id") == spell &&
                      publicStack[1].at("Card").at("Id") == counter &&
                      !publicStack[0].contains("Targets") && !publicStack[1].contains("Targets") &&
                      publicStack == match.ViewerSnapshot(2).at("ResponseStack"),
                  "Both viewers see only public response sources in activation order");
            Check(match.Player(2).Graveyard.size() == 3 &&
                      match.PriorityPlayer() == 1 &&
                      match.Player(1).Deck.size() == deckSize,
                  "Counterspell must pay exact discard cost and reset priority");
            Reject([&] { match.RespondWithNegation(2, counter, {costOne, costTwo}); });
            match.PassSpellResponse(1);
            Check(match.PriorityPlayer() == 2 &&
                      match.Phase() == YakouPhase::SpellResponse,
                  "One pass cannot resolve a counterspell chain");
            match.PassSpellResponse(2);
            Check(match.Phase() == YakouPhase::Main &&
                      match.PriorityPlayer() == 0 &&
                      match.Player(1).Deck.size() == deckSize &&
                      match.PendingEffects().empty() &&
                      match.Player(1).Graveyard.size() == 1,
                  "Resolved negation must cancel mill without undoing activated cards");
            auto const used = match.ServerStateHash();
            Reject([&] { match.BeginSpellResponse(1, match.Player(1).Hand[0]); });
            Check(match.ServerStateHash() == used,
                  "Negated spell must still consume its card-name per-turn allowance");
        }
        {
            // A counterspell can itself be countered. Resolution is LIFO:
            // the second counter cancels the first, then the original mills.
            auto match = Make(proto,
                {2009, 3020, 2001, 2002, 3001},
                {3020, 2001, 2002, 3001, 2004});
            match.StartTurn(1);
            auto const first = match.Player(1).Hand;
            auto const second = match.Player(2).Hand;
            auto const deckBefore = match.Player(1).Deck.size();
            match.BeginSpellResponse(1, first[0]);
            match.RespondWithNegation(2, second[0], {second[1], second[2]});
            Check(match.PriorityPlayer() == 1 &&
                      match.ViewerSnapshot(1).at("ResponseOptions").size() == 1,
                  "Opening player must receive counter-counter priority");
            auto const beforeInvalid = match.ServerStateHash();
            Reject([&] { match.RespondWithNegation(2, second[0], {second[3], second[4]}); });
            Check(match.ServerStateHash() == beforeInvalid,
                  "Wrong-priority nested response must roll back");
            match.RespondWithNegation(1, first[1], {first[2], first[3]});
            Check(match.ViewerSnapshot(1).at("ResponseStack").size() == 3 &&
                      match.PriorityPlayer() == 2,
                  "Two counters must remain public in activation order");
            match.PassSpellResponse(2);
            match.PassSpellResponse(1);
            Check(match.Phase() == YakouPhase::Main &&
                      match.PendingEffects().empty() &&
                      match.Player(1).Deck.size() == deckBefore - 2 &&
                      match.Player(1).Graveyard.size() >= 4,
                  "Counter-counter must restore original spell effect after both passes");
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2009, 3020, 2001, 2002, 3001}),
                Deck(proto, {3020, 2001, 2002, 3001, 2004})};
            auto command = [](YakouReplayOperation operation, int player)
            {
                YakouReplayCommand value;
                value.Operation = operation;
                value.Player = player;
                return value;
            };
            auto start = command(YakouReplayOperation::StartTurn, 1);
            auto cast = command(YakouReplayOperation::BeginSpellResponse, 1);
            cast.CardId = first[0];
            auto counter = command(YakouReplayOperation::RespondWithNegation, 2);
            counter.CardId = second[0];
            counter.Cards = {second[1], second[2]};
            auto counterCounter = command(YakouReplayOperation::RespondWithNegation, 1);
            counterCounter.CardId = first[1];
            counterCounter.Cards = {first[2], first[3]};
            auto passTwo = command(YakouReplayOperation::PassSpellResponse, 2);
            auto passOne = command(YakouReplayOperation::PassSpellResponse, 1);
            auto const replay = ReplayYakou(proto, 1, decks,
                {start, cast, counter, counterCounter, passTwo, passOne});
            Check(replay.FinalState.ServerStateHash() == match.ServerStateHash(),
                  "Nested counterspell sequence must replay deterministically");
        }
        {
            // With no response, the server resolves the original spell after
            // the required two passes, and only then applies its effect.
            auto match = Make(proto, {2009}, {3020, 2001, 2002});
            match.StartTurn(1);
            const int spell = match.Player(1).Hand[0];
            auto const before = match.Player(1).Deck.size();
            match.BeginSpellResponse(1, spell);
            match.PassSpellResponse(2);
            Check(match.Player(1).Deck.size() == before &&
                      match.PriorityPlayer() == 1,
                  "First pass must not resolve the original spell");
            match.PassSpellResponse(1);
            Check(match.Phase() == YakouPhase::Main &&
                      match.Player(1).Deck.size() == before - 2 &&
                      match.PendingEffects().empty(),
                  "Unanswered spell must resolve after double-pass");
        }
        {
            // Server-private replay reconstructs the same priority/negation
            // sequence and rejected commands without changing the match hash.
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2009}), Deck(proto, {3020, 2001, 2002})};
            auto command = [](YakouReplayOperation op, int player)
            {
                YakouReplayCommand value;
                value.Operation = op;
                value.Player = player;
                return value;
            };
            auto start = command(YakouReplayOperation::StartTurn, 1);
            auto cast = command(YakouReplayOperation::BeginSpellResponse, 1);
            cast.CardId = 1;
            auto invalid = command(YakouReplayOperation::PassSpellResponse, 1);
            auto counter = command(YakouReplayOperation::RespondWithNegation, 2);
            counter.CardId = 31;
            counter.Cards = {32, 33};
            auto passOne = command(YakouReplayOperation::PassSpellResponse, 1);
            auto passTwo = command(YakouReplayOperation::PassSpellResponse, 2);
            std::vector<YakouReplayCommand> commands{
                start, cast, invalid, counter, passOne, passTwo};
            auto const first = ReplayYakou(proto, 1, decks, commands);
            auto const again = ReplayYakou(proto, 1, decks, commands);
            Check(first.Steps.size() == 6 && !first.Steps[2].Accepted &&
                      first.Steps[2].BeforeHash == first.Steps[2].AfterHash &&
                      first.Steps[3].Accepted && first.Steps[5].Accepted &&
                      first.FinalState.Phase() == YakouPhase::Main &&
                      first.FinalState.ServerStateHash() == again.FinalState.ServerStateHash(),
                  "Chain replay must preserve invalid priority, paid response and closure");
        }
        {
            // The defender's free spell 3016 must resolve after response
            // priority, then reduce only the first combat damage this raid.
            auto shieldProto = proto;
            for (auto& id : shieldProto.Decks["Decks"][0]["Cards"])
            {
                if (id == 3020) { id = 3016; }
                else if (id == 3013) { id = 3003; }
            }
            auto prepare = [&](bool withShield)
            {
                auto match = Make(shieldProto, {3003}, {2002, 3016});
                match.StartTurn(1);
                int const attacker = match.Player(1).Hand[0];
                match.Summon(1, attacker, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                int const blocker = match.Player(2).Hand[0];
                match.Summon(2, blocker, "Normal", {}, 0);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.StartTurn(2);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.AssignBlockers(2, {{attacker, blocker}});
                Check(match.Phase() == YakouPhase::RaidResponse &&
                          match.PriorityPlayer() == 2,
                      "Defending player must receive priority before raid shield");
                int const spell = match.Player(2).Hand[0];
                if (withShield)
                {
                    auto const beforeInvalid = match.ServerStateHash();
                    Reject([&] { match.RespondWithRaidShield(1, spell, attacker); });
                    Reject([&] { match.RespondWithRaidShield(2, spell, attacker); });
                    Reject([&] { match.RespondWithRaidShield(2, blocker, blocker); });
                    Check(match.ServerStateHash() == beforeInvalid,
                          "Invalid shield priority, ownership and target must be atomic");
                    match.RespondWithRaidShield(2, spell, blocker);
                    Check(match.PriorityPlayer() == 1 &&
                              match.Player(2).Graveyard.back() == spell,
                          "Defender must pay the raid response and yield priority");
                    Reject([&] { match.RespondWithRaidShield(2, spell, blocker); });
                    Check(match.Player(2).Field[0]->Damage == 0,
                          "Shield response must not deal or reduce damage before resolution");
                    match.PassRaidResponse(1);
                    match.PassRaidResponse(2);
                }
                else
                {
                    match.PassRaidResponse(2);
                    match.PassRaidResponse(1);
                }
                return match;
            };
            {
                auto match = prepare(true);
                match.ResolveRaid(2);
                Check(match.Player(2).Field[0] &&
                          match.Player(2).Field[0]->Damage == 2 &&
                          !match.Player(1).Field[0],
                      "3016 shield must reduce 4 incoming damage to 2; simultaneous counterdamage kills raider");
                Check(match.Phase() == YakouPhase::TurnStart &&
                          match.ActivePlayer() == 2,
                      "Defensive shield must not interrupt end-of-raid progression");
            }
            {
                auto match = prepare(false);
                match.ResolveRaid(2);
                Check(!match.Player(2).Field[0] &&
                          match.Player(2).Graveyard.size() == 1,
                      "Without the shield, four damage destroys the 3-HP blocker");
            }
        }

        {
            // Three authored, paid defensive response spells share a real
            // two-turn preparation: defender mills two cards for Necromancy,
            // then places two blockers before the raider becomes ready.
            auto tactical = proto;
            for (auto& id : tactical.Decks["Decks"][0]["Cards"])
            {
                if (id == 3013) id = 3003;
                else if (id == 2010) id = 3018;
                else if (id == 2012) id = 3019;
            }
            auto ready = [&]()
            {
                auto match = Make(tactical, {3003},
                                  {2001, 2002, 2011, 3018, 3019});
                match.StartTurn(1);
                const int attacker = match.Player(1).Hand[0];
                match.Summon(1, attacker, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                const int source = match.Player(2).Hand[0];
                match.Summon(2, source, "Normal", {}, 0);
                match.ResolvePendingEffect(2); // Mill exactly two cost cards.
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.StartTurn(2);
                const int blocker = match.Player(2).Hand[0];
                match.Summon(2, blocker, "Normal", {}, 1);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                Check(match.Attacks().size() == 1 &&
                      match.Attacks()[0].Card == attacker &&
                      match.Player(2).Graveyard.size() >= 2,
                      "Paid raid tactics require a ready raider and paid materials");
                match.AssignBlockers(2, {{attacker, blocker}});
                return match;
            };
            {
                auto match = ready();
                const int attacker = match.Attacks()[0].Card;
                const int blocker = match.Player(2).Field[1]->InstanceId;
                const int spell = match.Player(2).Hand[0]; // 2011
                const int cost = match.Player(2).Hand[1];  // 3018
                const auto before = match.ServerStateHash();
                Reject([&] { match.RespondWithRaidTactic(1, spell, attacker, 0, {cost}); });
                Reject([&] { match.RespondWithRaidTactic(2, spell, 9999, 0, {cost}); });
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, 0, {spell}); });
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, 0, {}); });
                Check(match.ServerStateHash() == before,
                      "Invalid attack debuff payment or target must be atomic");
                match.RespondWithRaidTactic(2, spell, attacker, 0, {cost});
                Check(match.PriorityPlayer() == 1 &&
                      match.Player(2).Graveyard.back() == spell,
                      "2011 must discard one distinct owned hand card and yield priority");
                match.PassRaidResponse(1);
                match.PassRaidResponse(2);
                match.ResolveRaid(2);
                Check(match.Player(2).Field[1] &&
                      match.Player(2).Field[1]->InstanceId == blocker &&
                      match.Player(2).Field[1]->Damage == 2 &&
                      !match.Player(1).Field[0],
                      "2011 -2 attack changes 3003's raid damage from 4 to 2");
            }
            {
                auto match = ready();
                const int attacker = match.Attacks()[0].Card;
                const int spell = match.Player(2).Hand[2]; // 3019
                const auto grave = match.Player(2).Graveyard;
                const auto before = match.ServerStateHash();
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, 0,
                                                         {grave[0], grave[0]}); });
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, 0,
                                                         {grave[0], attacker}); });
                Check(match.ServerStateHash() == before,
                      "3019 must require two distinct owned graveyard cards");
                match.RespondWithRaidTactic(2, spell, attacker, 0, {grave[0], grave[1]});
                Check(match.Player(2).Exile.size() == 2 &&
                      match.Player(2).Graveyard.back() == spell,
                      "3019 pays Necromancy:2 by exiling two exact graveyard instances");
                match.PassRaidResponse(1);
                match.PassRaidResponse(2);
                match.ResolveRaid(2);
                Check(match.Player(2).Field[1] &&
                      match.Player(2).Field[1]->Damage == 2 &&
                      !match.Player(1).Field[0],
                      "3019 current-raid attack debuff must apply before combat");
            }
            {
                auto match = ready();
                const int attacker = match.Attacks()[0].Card;
                const int original = match.Player(2).Field[1]->InstanceId;
                const int replacement = match.Player(2).Field[0]->InstanceId;
                const int spell = match.Player(2).Hand[1]; // 3018
                const int discard = match.Player(2).Hand[0]; // 2011
                const auto before = match.ServerStateHash();
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, original,
                                                         {discard}); });
                Reject([&] { match.RespondWithRaidTactic(2, spell, attacker, 9999,
                                                         {discard}); });
                Check(match.ServerStateHash() == before,
                      "3018 must reject same, missing or illegal replacement blocker");
                match.RespondWithRaidTactic(2, spell, attacker, replacement, {discard});
                Check(match.Attacks()[0].Blocker == original,
                      "Blocker cannot be reassigned before response passes resolve");
                match.PassRaidResponse(1);
                match.PassRaidResponse(2);
                Check(match.Attacks()[0].Blocker == replacement,
                      "3018 reassignment must happen at chain resolution");
                match.ResolveRaid(2);
                Check(match.Player(2).Field[1] &&
                      match.Player(2).Field[1]->InstanceId == original &&
                      !match.Player(2).Field[0] &&
                      match.Player(1).Field[0] &&
                      match.Player(1).Field[0]->Damage == 1,
                      "3018 must redirect the actual combat to the other blocker");
            }
            {
                // Replay the paid 2011 response from the same server-private
                // deterministic opening decks, including invalid cost rejection.
                std::array<std::vector<int>, 2> const decks{
                    Deck(tactical, {3003}),
                    Deck(tactical, {2001, 2002, 2011, 3018, 3019})};
                auto makeCmd = [](YakouReplayOperation op, int player)
                {
                    YakouReplayCommand cmd;
                    cmd.Operation = op;
                    cmd.Player = player;
                    return cmd;
                };
                auto summonOne = makeCmd(YakouReplayOperation::Summon, 1);
                summonOne.CardId = 1; summonOne.Method = "Normal";
                auto summonTwo = makeCmd(YakouReplayOperation::Summon, 2);
                summonTwo.CardId = 31; summonTwo.Method = "Normal";
                auto blocker = makeCmd(YakouReplayOperation::Summon, 2);
                blocker.CardId = 32; blocker.Method = "Normal"; blocker.Slot = 1;
                auto assign = makeCmd(YakouReplayOperation::AssignBlockers, 2);
                assign.Blockers = {{1,32}};
                auto tactic = makeCmd(YakouReplayOperation::RespondWithRaidTactic, 2);
                tactic.CardId = 33; tactic.TargetId = 1; tactic.Cards = {34};
                auto badTactic = tactic;
                badTactic.Cards = {33};
                std::vector<YakouReplayCommand> const commands{
                    makeCmd(YakouReplayOperation::StartTurn,1), summonOne,
                    makeCmd(YakouReplayOperation::BeginRaid,1),
                    makeCmd(YakouReplayOperation::StartTurn,2), summonTwo,
                    makeCmd(YakouReplayOperation::ResolvePendingEffect,2),
                    makeCmd(YakouReplayOperation::BeginRaid,2),
                    makeCmd(YakouReplayOperation::StartTurn,1),
                    makeCmd(YakouReplayOperation::BeginRaid,1),
                    makeCmd(YakouReplayOperation::StartTurn,2), blocker,
                    makeCmd(YakouReplayOperation::BeginRaid,2),
                    makeCmd(YakouReplayOperation::StartTurn,1),
                    makeCmd(YakouReplayOperation::BeginRaid,1),
                    assign, badTactic, tactic,
                    makeCmd(YakouReplayOperation::PassRaidResponse,1),
                    makeCmd(YakouReplayOperation::PassRaidResponse,2),
                    makeCmd(YakouReplayOperation::ResolveRaid,2)};
                const auto one = ReplayYakou(tactical,1,decks,commands);
                const auto two = ReplayYakou(tactical,1,decks,commands);
                Check(one.Steps.size() == commands.size() &&
                      !one.Steps[15].Accepted &&
                      one.Steps[15].BeforeHash == one.Steps[15].AfterHash &&
                      one.Steps[16].Accepted && one.Steps.back().Accepted &&
                      one.FinalState.ServerStateHash() == two.FinalState.ServerStateHash(),
                      "Paid raid response replay must be deterministic and atomic");
            }
        }
        {
            // Multi-link raid chain: 3016 shield then 2011 attack debuff.
            // The second defender response requires the active player to
            // pass priority back. Both effects resolve LIFO on two passes.
            auto tactical = proto;
            for (auto& id : tactical.Decks["Decks"][0]["Cards"])
            {
                if (id == 3020) id = 3016;
                else if (id == 2010) id = 3018;
                else if (id == 3013) id = 3003;
            }
            auto ready = [&]()
            {
                auto match = Make(tactical, {3003}, {2001, 2002, 2011, 3016, 3018});
                match.StartTurn(1);
                const int raider = match.Player(1).Hand[0];
                match.Summon(1, raider, "Normal", {}, 0);
                match.BeginRaid(1);
                match.StartTurn(2);
                const int source = match.Player(2).Hand[0];
                match.Summon(2, source, "Normal", {}, 0);
                match.ResolvePendingEffect(2);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.StartTurn(2);
                const int blocker = match.Player(2).Hand[0];
                match.Summon(2, blocker, "Normal", {}, 1);
                match.BeginRaid(2);
                match.StartTurn(1);
                match.BeginRaid(1);
                match.AssignBlockers(2, {{raider, blocker}});
                return match;
            };
            {
                auto match = ready();
                const int raider = match.Attacks()[0].Card;
                const int blocker = match.Player(2).Field[1]->InstanceId;
                const auto hand = match.Player(2).Hand;
                const int attackDebuff = hand[0]; // 2011
                const int shield = hand[1];       // 3016
                const int discard = hand[2];      // 3018
                auto const options = match.ViewerSnapshot(2).at("ResponseOptions");
                Check(options.size() == 3 && match.ViewerSnapshot(1).at("ResponseOptions").empty(),
                      "Only the defender receives supported response options");
                match.RespondWithRaidShield(2, shield, blocker);
                Check(match.PriorityPlayer() == 1 && match.Phase() == YakouPhase::RaidResponse &&
                      match.ViewerSnapshot(1).at("RaidResponseDepth") == 1 &&
                      match.ViewerSnapshot(2).at("RaidResponseDepth") == 1,
                      "Shield link must pass priority and reveal only public depth");
                const auto wrongTurn = match.ServerStateHash();
                Reject([&] { match.RespondWithRaidTactic(2, attackDebuff, raider, 0,
                                                         {discard}); });
                Check(match.ServerStateHash() == wrongTurn,
                      "Second response must not bypass alternating priority");
                match.PassRaidResponse(1);
                Check(match.PriorityPlayer() == 2,
                      "Active player pass must return priority to defender");
                match.RespondWithRaidTactic(2, attackDebuff, raider, 0, {discard});
                Check(match.PriorityPlayer() == 1 &&
                      match.ViewerSnapshot(1).at("RaidResponseDepth") == 2 &&
                      match.ViewerSnapshot(2).at("RaidResponseDepth") == 2,
                      "Second response must reset passes and publish the shared link count");
                match.PassRaidResponse(1);
                Check(match.PriorityPlayer() == 2,
                      "First pass after second response cannot close chain");
                match.PassRaidResponse(2);
                Check(match.PriorityPlayer() == 0,
                      "Two passes close multi-link raid chain");
                auto const& journal = match.Journal();
                auto debuffEvent = std::find_if(journal.begin(), journal.end(),
                    [](std::string const& line)
                    { return line.find("RaidAttackReduced:") == 0; });
                auto shieldEvent = std::find_if(journal.begin(), journal.end(),
                    [](std::string const& line)
                    { return line.find("RaidShieldResolved:") == 0; });
                Check(debuffEvent != journal.end() && shieldEvent != journal.end() &&
                      debuffEvent < shieldEvent,
                      "Later attack debuff must resolve before earlier shield (LIFO)");
                match.ResolveRaid(2);
                Check(match.Player(2).Field[1] &&
                      match.Player(2).Field[1]->Damage == 0 &&
                      !match.Player(1).Field[0] &&
                      match.ActivePlayer() == 2,
                      "2011 attack reduction and 3016 first-hit shield must compose in combat");
            }
            {
                // Existing command replay must preserve the whole multi-link
                // chain, including the rejected out-of-priority response.
                std::array<std::vector<int>, 2> const decks{
                    Deck(tactical, {3003}), Deck(tactical, {2001, 2002, 2011, 3016, 3018})};
                auto make = [](YakouReplayOperation op, int player)
                {
                    YakouReplayCommand value;
                    value.Operation = op; value.Player = player;
                    return value;
                };
                auto attacker = make(YakouReplayOperation::Summon, 1);
                attacker.CardId = 1; attacker.Method = "Normal";
                auto source = make(YakouReplayOperation::Summon, 2);
                source.CardId = 31; source.Method = "Normal";
                auto blocker = make(YakouReplayOperation::Summon, 2);
                blocker.CardId = 32; blocker.Method = "Normal"; blocker.Slot = 1;
                auto assign = make(YakouReplayOperation::AssignBlockers, 2);
                assign.Blockers = {{1, 32}};
                auto shield = make(YakouReplayOperation::RespondWithRaidShield, 2);
                shield.CardId = 34; shield.TargetId = 32;
                auto debuff = make(YakouReplayOperation::RespondWithRaidTactic, 2);
                debuff.CardId = 33; debuff.TargetId = 1; debuff.Cards = {35};
                std::vector<YakouReplayCommand> const commands{
                    make(YakouReplayOperation::StartTurn,1), attacker,
                    make(YakouReplayOperation::BeginRaid,1),
                    make(YakouReplayOperation::StartTurn,2), source,
                    make(YakouReplayOperation::ResolvePendingEffect,2),
                    make(YakouReplayOperation::BeginRaid,2),
                    make(YakouReplayOperation::StartTurn,1),
                    make(YakouReplayOperation::BeginRaid,1),
                    make(YakouReplayOperation::StartTurn,2), blocker,
                    make(YakouReplayOperation::BeginRaid,2),
                    make(YakouReplayOperation::StartTurn,1),
                    make(YakouReplayOperation::BeginRaid,1),
                    assign, shield, debuff,
                    make(YakouReplayOperation::PassRaidResponse,1),
                    debuff,
                    make(YakouReplayOperation::PassRaidResponse,1),
                    make(YakouReplayOperation::PassRaidResponse,2),
                    make(YakouReplayOperation::ResolveRaid,2)};
                const auto replay = ReplayYakou(tactical, 1, decks, commands);
                const auto again = ReplayYakou(tactical, 1, decks, commands);
                Check(replay.Steps.size() == commands.size() &&
                      !replay.Steps[16].Accepted &&
                      replay.Steps[16].BeforeHash == replay.Steps[16].AfterHash &&
                      replay.Steps[18].Accepted &&
                      replay.Steps.back().Accepted &&
                      replay.FinalState.Player(2).Field[1] &&
                      replay.FinalState.Player(2).Field[1]->Damage == 0 &&
                      replay.FinalState.ServerStateHash() ==
                          again.FinalState.ServerStateHash(),
                      "Multi-link raid replay must deterministically reject invalid priority");
            }
        }
        {
            // An authored untargeted summon trigger is safe to drain without
            // a player choice, including its exact deck/graveyard mutation.
            auto autoProto = proto;
            for (auto& id : autoProto.Decks["Decks"][0]["Cards"])
                if (id == 2008) { id = 3005; break; }
            auto match = Make(autoProto, {2001, 3005}, {});
            match.StartTurn(1);
            const int normal = match.Player(1).Hand[0];
            const int devour = match.Player(1).Hand[1];
            match.Summon(1, normal, "Normal", {}, 0);
            const auto deckBefore = match.Player(1).Deck.size();
            Check(match.PendingEffects().size() == 1, "Mill must be pending first");
            const auto budgetHash = match.ServerStateHash();
            Reject([&] { (void)match.ResolveAutomaticTriggers(0); });
            Reject([&] { (void)match.ResolveAutomaticTriggers(129); });
            Check(match.ServerStateHash() == budgetHash,
                  "Invalid scheduler budget must leave all server state intact");
            Check(match.ResolveAutomaticTriggers(64) == 1 &&
                  match.PendingEffects().empty() &&
                  match.Player(1).Deck.size() == deckBefore - 2 &&
                  match.Player(1).Graveyard.size() == 2,
                  "Automatic Mill must consume exactly the authored count");
            // An authored Self target is also deterministic: do not present it
            // as a selectable target simply because the operation names one.
            match.Summon(1, devour, "Devour", {normal}, 0);
            Check(match.PendingEffects().size() == 1 &&
                  match.PendingEffectChoices(1).MaxCount == 0,
                  "Devour-summoned Self countdown is an automatic effect");
            Check(match.ResolveAutomaticTriggers(1) == 1 &&
                  match.PendingEffects().empty() &&
                  match.Player(1).Field[0] &&
                  match.Player(1).Field[0]->Countdown == 2,
                  "Automatic Self countdown must resolve after Devour");
            Check(match.ResolveAutomaticTriggers(64) == 0,
                  "Empty automatic queue should be a valid no-op");
        }
        {
            // A discarded 2005 queues Mill, followed by a user-targeted
            // healing effect. Drain only the deterministic prefix and do
            // not auto-pick or auto-decline the next controller's choice.
            auto altered = proto;
            Json heal;
            for (auto const& card : altered.Cards.at("Cards"))
                if (card.at("Id") == 3002) heal = card.at("Effects").at(0);
            Check(heal.is_object(), "Healing fixture must use real authored effect");
            heal["Trigger"] = "OnSummoned";
            for (auto& card : altered.Cards.at("Cards"))
                if (card.at("Id") == 2004)
                    card["Effects"] = Json::array({heal});
            auto match = Make(altered, {2004, 2005, 2001, 2002, 3001}, {});
            match.StartTurn(1);
            const auto hand = match.Player(1).Hand;
            match.Summon(1, hand[0], "Discard", {hand[1]}, 0);
            Check(match.PendingEffects().size() == 2 &&
                  match.PendingEffects()[0].SourceCard == hand[1] &&
                  match.PendingEffects()[1].SourceCard == hand[0],
                  "Effect queue must preserve discard-before-summon event order");
            const auto deckBefore = match.Player(1).Deck.size();
            Check(match.ResolveAutomaticTriggers(64) == 1 &&
                  match.PendingEffects().size() == 1 &&
                  match.Player(1).Deck.size() == deckBefore - 1 &&
                  match.PendingEffectChoices(1).Operation == "HealAccumulatedDamage",
                  "Scheduler must stop before mandatory player target selection");
            const auto waitingHash = match.ServerStateHash();
            Check(match.ResolveAutomaticTriggers(64) == 0 &&
                  match.ServerStateHash() == waitingHash,
                  "Repeated scheduler calls must not choose the target silently");
            match.ResolvePendingEffect(1, hand[0]);
            Check(match.PendingEffects().empty(),
                  "Manual selection must resume the queue after an automatic prefix");

            // Trigger processing is all-or-nothing across the entire batch.
            // An impossible second effect cannot leave the first Mill applied.
            auto broken = altered;
            Json impossible = heal;
            impossible["Actions"] = Json::array({
                {{"Operation", "Mill"}, {"From", "FriendlyDeck"},
                 {"To", "Graveyard"}, {"Count", 31}}
            });
            for (auto& card : broken.Cards.at("Cards"))
                if (card.at("Id") == 2004)
                    card["Effects"] = Json::array({impossible});
            auto rejected = Make(broken, {2004, 2005, 2001, 2002, 3001}, {});
            rejected.StartTurn(1);
            auto const illegalHand = rejected.Player(1).Hand;
            rejected.Summon(1, illegalHand[0], "Discard", {illegalHand[1]}, 0);
            Check(rejected.PendingEffects().size() == 2,
                  "Rollback fixture must enqueue both authored effects");
            auto const beforeFailure = rejected.ServerStateHash();
            Reject([&] { (void)rejected.ResolveAutomaticTriggers(64); });
            Check(rejected.ServerStateHash() == beforeFailure &&
                  rejected.PendingEffects().size() == 2,
                  "Failure of later automatic effect must roll back earlier resolved effects");
            Check(rejected.ResolveAutomaticTriggers(1) == 1 &&
                  rejected.PendingEffects().size() == 1,
                  "A bounded one-effect prefix must still commit independently");
        }
        {
            // Replay makes both successful automatic drains and failed budgets
            // reproducible without exposing hidden deck identities to clients.
            auto autoProto = proto;
            for (auto& id : autoProto.Decks["Decks"][0]["Cards"])
                if (id == 2008) { id = 3005; break; }
            std::array<std::vector<int>, 2> const decks{
                Deck(autoProto, {2001, 3005}), Deck(autoProto, {})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::Summon;
            summon.Player = 1;
            summon.CardId = 1;
            summon.Method = "Normal";
            YakouReplayCommand drain;
            drain.Operation = YakouReplayOperation::ResolveAutomaticTriggers;
            drain.MaxEffects = 64;
            auto bad = drain;
            bad.MaxEffects = 0;
            std::vector<YakouReplayCommand> const commands{start, summon, bad, drain};
            auto const once = ReplayYakou(autoProto, 1, decks, commands);
            auto const again = ReplayYakou(autoProto, 1, decks, commands);
            Check(once.Steps.size() == commands.size() &&
                  !once.Steps[2].Accepted &&
                  once.Steps[2].BeforeHash == once.Steps[2].AfterHash &&
                  once.Steps[3].Accepted &&
                  once.FinalState.PendingEffects().empty() &&
                  once.FinalState.ServerStateHash() == again.FinalState.ServerStateHash(),
                  "Automatic trigger replay must be deterministic with atomic rejection");
        }
        {
            // New action boundary: summoning 2001 immediately drains authored
            // untargeted Mill, while legacy Summon remains explicitly manual.
            auto match = Make(proto, {2001, 2009, 2003, 3001, 2002}, {});
            match.StartTurn(1);
            auto const hand = match.Player(1).Hand;
            auto const deckBefore = match.Player(1).Deck.size();
            Check(match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 0) == 1 &&
                  match.PendingEffects().empty() &&
                  match.Player(1).Deck.size() == deckBefore - 2 &&
                  match.Player(1).Graveyard.size() == 2,
                  "Action-boundary summon must immediately resolve its authored Mill");
            const auto beforeInvalid = match.ServerStateHash();
            Reject([&] { (void)match.ActivateMainSpellAndRunAutomaticTriggers(1, hand[1], 0); });
            Check(match.ServerStateHash() == beforeInvalid &&
                  std::find(match.Player(1).Hand.begin(),
                            match.Player(1).Hand.end(), hand[1]) != match.Player(1).Hand.end(),
                  "Invalid drain budget must roll back spell activation and hand removal");
            auto const spellDeckBefore = match.Player(1).Deck.size();
            Check(match.ActivateMainSpellAndRunAutomaticTriggers(1, hand[1]) == 1 &&
                  match.PendingEffects().empty() &&
                  match.Player(1).Deck.size() == spellDeckBefore - 2 &&
                  std::find(match.Player(1).Graveyard.begin(),
                            match.Player(1).Graveyard.end(), hand[1]) !=
                      match.Player(1).Graveyard.end(),
                  "Free main spell action and Mill must commit atomically");
            const auto beforeRepeat = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(1, hand[0], "Normal", {}, 2); });
            Check(match.ServerStateHash() == beforeRepeat,
                  "Invalid repeated summon must preserve full canonical state");
        }
        {
            // A user-selected trigger blocks the new boundary only after
            // deterministic earlier triggers were executed. Manual target
            // selection then resumes the same queue without choosing others.
            auto altered = proto;
            Json heal;
            for (auto const& card : altered.Cards.at("Cards"))
                if (card.at("Id") == 3002) heal = card.at("Effects").at(0);
            Check(heal.is_object(), "Must have real authored Heal action");
            heal["Trigger"] = "OnSummoned";
            for (auto& card : altered.Cards.at("Cards"))
                if (card.at("Id") == 2004)
                    card["Effects"] = Json::array({heal});
            auto match = Make(altered, {2004, 2005, 2001, 2002, 3001}, {});
            match.StartTurn(1);
            const auto hand = match.Player(1).Hand;
            auto const deckBefore = match.Player(1).Deck.size();
            Check(match.SummonAndRunAutomaticTriggers(
                      1, hand[0], "Discard", {hand[1]}, 0) == 1 &&
                  match.PendingEffects().size() == 1 &&
                  match.PendingEffectChoices(1).Operation == "HealAccumulatedDamage" &&
                  match.Player(1).Deck.size() == deckBefore - 1,
                  "Discard Mill must drain and stop before player-selected healing");
            auto const hold = match.ServerStateHash();
            Reject([&] { (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(
                             2, hand[0]); });
            Check(match.ServerStateHash() == hold,
                  "Only trigger owner may continue the choice boundary");
            Check(match.ResolveSelectedEffectAndRunAutomaticTriggers(1, hand[0]) == 0 &&
                  match.PendingEffects().empty(),
                  "Explicit authorized target should resolve and drain remainder");
        }
        {
            // An action is not committed if a later automatic trigger fails,
            // even if the source/card payment would otherwise have succeeded.
            auto broken = proto;
            for (auto& card : broken.Cards.at("Cards"))
                if (card.at("Id") == 2001)
                    card["Effects"][0]["Actions"][0]["Count"] = 31;
            auto match = Make(broken, {2001, 2009}, {});
            match.StartTurn(1);
            int const summon = match.Player(1).Hand[0];
            const auto before = match.ServerStateHash();
            Reject([&] { (void)match.SummonAndRunAutomaticTriggers(
                             1, summon, "Normal", {}, 0); });
            Check(match.ServerStateHash() == before &&
                  match.Player(1).Field[0] == std::nullopt &&
                  match.PendingEffects().empty(),
                  "Automatic trigger failure must roll back its originating summon");
        }
        {
            // Atomic action-boundary commands are replayable without changing
            // old explicit-command replay behavior or private hash semantics.
            std::array<std::vector<int>, 2> const decks{
                Deck(proto, {2001, 2009}), Deck(proto, {})};
            YakouReplayCommand start;
            start.Operation = YakouReplayOperation::StartTurn;
            start.Player = 1;
            YakouReplayCommand summon;
            summon.Operation = YakouReplayOperation::SummonAndRunAutomaticTriggers;
            summon.Player = 1;
            summon.CardId = 1;
            summon.Method = "Normal";
            YakouReplayCommand spell;
            spell.Operation = YakouReplayOperation::ActivateMainSpellAndRunAutomaticTriggers;
            spell.Player = 1;
            spell.CardId = 2;
            auto invalid = spell;
            invalid.MaxEffects = 0;
            std::vector<YakouReplayCommand> const commands{
                start, summon, invalid, spell};
            auto const first = ReplayYakou(proto, 1, decks, commands);
            auto const second = ReplayYakou(proto, 1, decks, commands);
            Check(first.Steps.size() == commands.size() &&
                  first.Steps[1].Accepted && !first.Steps[2].Accepted &&
                  first.Steps[2].BeforeHash == first.Steps[2].AfterHash &&
                  first.Steps[3].Accepted &&
                  first.FinalState.PendingEffects().empty() &&
                  first.FinalState.ServerStateHash() ==
                      second.FinalState.ServerStateHash(),
                  "Action-boundary replay must preserve deterministic acceptance and rollback");
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
