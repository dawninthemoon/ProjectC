#include "CardCatalog.hpp"
#include "ProjectC/GameReplay.hpp"
#include "ProjectC/Zone.hpp"
#include <algorithm>
#include <filesystem>
#include <functional>
#include <iostream>
#include <source_location>
#include <stdexcept>

using namespace ProjectC::GameCore;
using namespace ProjectC::Network;

namespace
{
constexpr int RevivalFixtureDefinition = 900001;

void Require(bool condition, std::source_location at = std::source_location::current())
{
    if (!condition)
    {
        throw std::runtime_error("New faction assertion at line " + std::to_string(at.line()));
    }
}

struct Fixture
{
    GameState State;
    GameState Initial;
    std::vector<GameCommand> Commands;
    std::vector<CommandResult> Results;

    explicit Fixture(CardCatalog const& catalog)
    {
        auto config = catalog.Config;
        auto definitions = catalog.Definitions;
        // The shipped revival spell is Hyakki-only. Exercise new OnRevived triggers
        // with a generic test-only enabler without changing any authored card rules.
        auto revival = *std::find_if(definitions.begin(),
                                     definitions.end(),
                                     [](auto const& card) { return card.Id == 1101; });
        revival.Id = RevivalFixtureDefinition;
        revival.Name = "Test revival";
        revival.NameKey.clear();
        revival.DescriptionKey.clear();
        revival.DescriptionArgs.clear();
        revival.Effects.front().Actions.front().Targets.RequiredArchetype.clear();
        revival.Effects.front().Actions.front().Targets.MaxAttack = -1;
        definitions.push_back(revival);
        config.InitialHandSize = 0;
        config.DrawPerTurn = 0;
        config.EnableMulligan = false;
        std::vector<int> deck{3001, 3002, 3003, 3004, 3005, 3006, 4001, 4002, 4003, 4004,
                              4005, 4006, 2001, 2002, 2003, 2004, 2005, 1101, 1104, 1001,
                              1004, 1103, 3003, 3006, 4006, 4006, 1003, 2005, 1101, 3005};
        std::replace(deck.begin(), deck.end(), 1101, RevivalFixtureDefinition);
        Require(InitializeGame(
                    State, config, definitions, {PlayerSetup{1, deck}, PlayerSetup{2, deck}}, 1) ==
                InitializationResult::Success);
    }

    int Put(int definition, Zone zone, int owner = 1)
    {
        auto& player = *FindPlayer(State, owner);
        auto card = std::find_if(player.Deck.begin(),
                                 player.Deck.end(),
                                 [&](auto const& entry)
                                 { return entry.CurrentDefinitionId == definition; });
        Require(card != player.Deck.end());
        int id = card->Id;
        Require(MoveCard(State, owner, id, Zone::Deck, zone) == ZoneResult::Success);
        return id;
    }

    CommandResult Run(GameCommand const& command)
    {
        auto result = ProcessCommand(State, command);
        Commands.push_back(command);
        Results.push_back(result);
        return result;
    }

    void Accept(GameCommand const& command)
    {
        auto result = Run(command);
        if (result.Code != CommandError::Success)
        {
            throw std::runtime_error("New faction command rejected: " +
                                     std::to_string(static_cast<int>(result.Code)));
        }
    }

    void Start()
    {
        Initial = State;
        Accept(StartTurnCommand{1});
    }

    void Resolve(int source,
                 int effect,
                 std::vector<int> wanted,
                 std::function<void(CommandResult const&, std::size_t)> inspect = {})
    {
        PlayerActionCommand command{1, source, effect, {}};
        auto const before = State;
        for (std::size_t index = 0; index < wanted.size(); ++index)
        {
            auto result = Run(command);
            if (result.Code != CommandError::ChoiceRequired || State != before)
            {
                throw std::runtime_error(
                    "Choice failed for definition " + std::to_string(wanted[index]) + ", source " +
                    std::to_string(source) + ", effect " + std::to_string(effect) + ", code " +
                    std::to_string(static_cast<int>(result.Code)));
            }
            if (inspect)
            {
                inspect(result, index);
            }
            auto candidate = std::find_if(
                result.Choice.Candidates.begin(),
                result.Choice.Candidates.end(),
                [&](auto const& entry) { return entry.Card.CurrentDefinitionId == wanted[index]; });
            Require(candidate != result.Choice.Candidates.end());
            command.Choices.push_back({candidate->Target});
        }
        Accept(command);
    }

    CardInstance const& In(int id, Zone zone, int owner = 1) const
    {
        auto const& cards = *GetZone(*FindPlayer(State, owner), zone);
        auto card = std::find_if(
            cards.begin(), cards.end(), [&](auto const& entry) { return entry.Id == id; });
        Require(card != cards.end());
        return *card;
    }

    void End()
    {
        auto player = State.ActivePlayerId;
        while (State.Phase != TurnPhase::End)
        {
            Accept(AdvancePhaseCommand{player});
        }
        Accept(EndTurnCommand{player});
    }

    void Replay() const
    {
        auto replay = ReplayCommands(Initial, Commands);
        Require(replay.FinalState == State && replay.Results == Results);
    }
};
} // namespace

void RunNewFactionTests()
{
    auto catalog =
        CardCatalog::Load((std::filesystem::path(DefaultDataDirectory()) / "HyakkiYagyo").string());
    Require(catalog.Config.FieldLimit == 5 && catalog.Config.MinDeckSize == 30 &&
            catalog.Config.MaxDeckSize == 30 && catalog.Config.MaxCardCopies == 3 &&
            catalog.Config.InitialYokaiPower == 2 && !catalog.Config.SummoningSickness);
    for (auto name : {"HyakkiYagyo", "HyakkiSample", "CrimsonSample", "FrostSample"})
    {
        Require(catalog.FindDeck(name) && catalog.ValidateDeck(catalog.FindDeck(name)->Cards));
    }
    {
        Fixture f(catalog);
        int source = f.Put(3001, Zone::Hand);
        f.Start();
        f.Accept(PlayerActionCommand{1, source, 0, {}});
        Require(f.In(source, Zone::Field).Attack == 4 &&
                f.In(source, Zone::Field).TemporaryAttack == 1);
        f.End();
        Require(f.In(source, Zone::Field).Attack == 3 &&
                f.In(source, Zone::Field).TemporaryAttack == 0);
        f.Replay();
    }
    for (auto definition : {3002, 4001})
    {
        Fixture f(catalog);
        int source = f.Put(definition, Zone::Hand);
        f.Start();
        std::string archetype = definition == 3002 ? "CrimsonForge" : "FrostArchive";
        f.Resolve(source,
                  0,
                  {definition == 3002 ? 3006 : 4005},
                  [&](auto const& result, auto)
                  {
                      for (auto const& candidate : result.Choice.Candidates)
                      {
                          auto def = FindDefinition(f.State, candidate.Card.CurrentDefinitionId);
                          Require(def->Type == CardType::Spell &&
                                  std::find(def->Archetypes.begin(),
                                            def->Archetypes.end(),
                                            archetype) != def->Archetypes.end());
                      }
                  });
        Require(f.State.Players[0].Hand.size() == 1 && f.State.Players[0].Field.size() == 1);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int source = f.Put(3003, Zone::Field), target = f.Put(4001, Zone::Field, 2);
        f.Start();
        f.Resolve(source, 1, {4001});
        Require(f.In(target, Zone::Field, 2).Hp == 3 && f.State.Players[0].YokaiPower == 1);
        f.Resolve(source, 1, {4001});
        Require(f.In(target, Zone::Field, 2).Hp == 1 && f.State.Players[0].YokaiPower == 0);
        auto before = f.State;
        Require(f.Run(PlayerActionCommand{1, source, 1, {}}).Code ==
                    CommandError::InsufficientResource &&
                f.State == before);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int spell = f.Put(RevivalFixtureDefinition, Zone::Hand),
            target = f.Put(3004, Zone::Graveyard);
        auto& damaged = FindPlayer(f.State, 1)->Graveyard.front();
        damaged.Hp = 1;
        damaged.MaxHp = 9;
        damaged.Attack = 99;
        f.Start();
        f.Resolve(spell, 1, {3004});
        auto revived = f.In(target, Zone::Field);
        Require(revived.Hp == 3 && revived.MaxHp == 3 && revived.Attack == 6 &&
                revived.TemporaryAttack == 2);
        f.End();
        Require(f.In(target, Zone::Field).Attack == 4);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int first = f.Put(3005, Zone::Hand), second = f.Put(3005, Zone::Hand);
        f.Start();
        auto inspect = [&](auto const& result, auto)
        {
            for (auto const& candidate : result.Choice.Candidates)
            {
                auto def = FindDefinition(f.State, candidate.Card.CurrentDefinitionId);
                Require(def->Type == CardType::Monster && def->Attack <= 3 &&
                        std::find(def->Archetypes.begin(), def->Archetypes.end(), "CrimsonForge") !=
                            def->Archetypes.end());
            }
        };
        f.Resolve(first, 1, {3003}, inspect);
        f.Resolve(second, 1, {3002}, inspect); // No global normal-spell-per-turn cap.
        Require(f.State.Players[0].Hand.size() == 2 && f.State.Players[0].Graveyard.size() == 2);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int spell = f.Put(3006, Zone::Hand), target = f.Put(4001, Zone::Field, 2);
        f.Start();
        f.Resolve(spell, 1, {4001});
        Require(f.In(target, Zone::Field, 2).Hp == 2 && f.State.Players[0].YokaiPower == 1);
        f.In(spell, Zone::Graveyard);
        f.Replay();
    }
    for (bool revived : {false, true})
    {
        Fixture f(catalog);
        int target = f.Put(3004, Zone::Field, 2);
        int source = f.Put(4002, revived ? Zone::Graveyard : Zone::Hand);
        int spell = revived ? f.Put(RevivalFixtureDefinition, Zone::Hand) : 0;
        f.Start();
        if (revived)
        {
            f.Resolve(spell, 1, {4002, 3004});
        }
        else
        {
            f.Resolve(source, 0, {3004});
        }
        Require(f.In(target, Zone::Field, 2).Hp == 2);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int source = f.Put(4003, Zone::Field), target = f.Put(4001, Zone::Field, 2);
        f.Start();
        f.Resolve(source, 1, {4001});
        Require(f.State.Players[0].YokaiPower == 0 && f.State.Players[1].Field.empty());
        f.In(target, Zone::Graveyard, 2);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int spell = f.Put(RevivalFixtureDefinition, Zone::Hand),
            source = f.Put(4004, Zone::Graveyard);
        int salvaged = f.Put(4006, Zone::Graveyard);
        f.Put(3006, Zone::Graveyard);
        f.Start();
        f.Resolve(spell,
                  1,
                  {4004, 4006},
                  [&](auto const& result, std::size_t step)
                  {
                      if (step == 1)
                      {
                          Require(result.Choice.Candidates.size() == 1);
                      }
                  });
        f.In(source, Zone::Field);
        f.In(salvaged, Zone::Hand);
        Require(f.State.Players[0].YokaiPower == 1);
        f.Replay();
    }
    for (int definition : {2001, 3004})
    {
        Fixture f(catalog);
        int spell = f.Put(4005, Zone::Hand), target = f.Put(definition, Zone::Graveyard, 2);
        f.Start();
        f.Resolve(spell, 1, {definition});
        f.In(target, Zone::Exile, 2);
        Require(f.State.Players[1].Graveyard.empty() && f.State.Players[0].YokaiPower == 2);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int first = f.Put(4006, Zone::Hand), second = f.Put(4006, Zone::Hand);
        f.Start();
        auto deckSize = f.State.Players[0].Deck.size();
        f.Resolve(first, 1, {});
        Require(f.State.Players[0].Deck.size() == deckSize - 2 &&
                f.State.Players[0].YokaiPower == 1);
        auto before = f.State;
        Require(f.Run(PlayerActionCommand{1, second, 1, {}}).Code ==
                    CommandError::UsageLimitReached &&
                f.State == before);
        f.End();
        f.Accept(StartTurnCommand{2});
        f.End();
        f.Accept(StartTurnCommand{1});
        f.Resolve(second, 1, {});
        Require(f.State.Players[0].YokaiPower ==
                1); // Carry-over plus existing turn gain, not reset.
        f.Replay();
    }
    {
        Fixture f(catalog);
        int source = f.Put(3006, Zone::Hand);
        f.Start();
        auto before = f.State;
        auto result = f.Run(PlayerActionCommand{1, source, 1, {}});
        Require(result.Code != CommandError::Success &&
                result.Code != CommandError::ChoiceRequired && f.State == before);
        f.Replay();
    }
    {
        Fixture f(catalog);
        int source = f.Put(4002, Zone::Hand);
        f.Start();
        f.Accept(PlayerActionCommand{1, source, 0, {}});
        f.In(source, Zone::Field); // No legal trigger target fizzles without undoing the summon.
        f.Replay();
    }
    std::cout << "CrimsonForge/FrostArchive: all twelve definitions, costs, targets, triggers, "
                 "limits, temporary stats and replay passed\n";
}
