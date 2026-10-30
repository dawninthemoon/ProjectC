#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameCommand.hpp"

#include <functional>
#include <iostream>
#include <limits>
#include <stdexcept>

using namespace ProjectC::GameCore;

namespace
{
    std::vector<CardDefinition> Definitions()
    {
        return { { .Id = 10, .Name = "Attacker", .Attack = 3, .MaxHp = 5 },
            { .Id = 20, .Name = "Defender", .Attack = 4, .MaxHp = 3 } };
    }

    std::array<PlayerSetup, 2> Players()
    {
        return { PlayerSetup{ .Id = 1, .Deck = { 10, 20, 10, 20 } },
            PlayerSetup{ .Id = 2, .Deck = { 20, 10, 20, 10 } } };
    }

    GameState MakeGame()
    {
        GameState state;
        GameConfig config;
        config.InitialHandSize = 1;
        if (InitializeGame(state, config, Definitions(), Players(), 1) != InitializationResult::Success)
            throw std::runtime_error("test fixture initialization failed");
        return state;
    }

    GameEvent Moved(CardInstance const& card, Zone from, Zone to)
    {
        return GameEvent{ .Type = GameEventType::CardMoved,
            .TargetPlayerId = card.OwnerId, .TargetCardId = card.Id,
            .FromZone = from, .ToZone = to,
            .OriginalDefinitionId = card.OriginalDefinitionId,
            .CurrentDefinitionId = card.CurrentDefinitionId };
    }
}

void RunGameFlowTests(int& testCount, int& failedTests)
{
    auto check = [&](bool passed, char const* name)
    {
        ++testCount;
        if (!passed) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    auto rejected = [&](GameState state, auto command, CommandError expected, char const* name)
    {
        GameState const before = state;
        CommandResult const result = ProcessCommand(state, command);
        check(result.Code == expected && result.Events.empty() && state == before, name);
    };
    GameState state = MakeGame();
    check(state == MakeGame() && ValidateCardIntegrity(state), "DeterministicInitialization");
    check(state.Players[0].Hand[0].OriginalDefinitionId == 10
        && state.Players[0].Deck[0].OriginalDefinitionId == 20
        && state.Players[1].Hand[0].OriginalDefinitionId == 20
        && state.Players[0].Deck[1].OriginalDefinitionId == 10
        && state.Players[0].Deck[1].Id != state.Players[0].Hand[0].Id,
        "InitialHandDeckOrderAndDistinctInstanceIds");

    using ChangeSetup = std::function<void(GameConfig&, std::vector<CardDefinition>&,
        std::array<PlayerSetup, 2>&, PlayerId&)>;
    auto badInitialization = [&](ChangeSetup const& change, InitializationResult expected, char const* name)
    {
        GameConfig config;
        auto definitions = Definitions();
        auto players = Players();
        PlayerId first = 1;
        change(config, definitions, players, first);
        GameState output = MakeGame();
        GameState const before = output;
        check(InitializeGame(output, config, definitions, players, first) == expected
            && output == before, name);
    };
    badInitialization([](auto& c, auto&, auto&, auto&) { c.InitialLife = 0; },
        InitializationResult::InvalidConfig, "InvalidInitialLife");
    badInitialization([](auto& c, auto&, auto&, auto&) { c.InitialHandSize = c.HandLimit + 1; },
        InitializationResult::InvalidConfig, "InitialHandLimit");
    badInitialization([](auto&, auto&, auto& p, auto&) { p[1].Id = 1; },
        InitializationResult::InvalidPlayers, "DuplicatePlayerIds");
    badInitialization([](auto&, auto&, auto& p, auto&) { p[0].Id = 0; },
        InitializationResult::InvalidPlayers, "ZeroPlayerId");
    badInitialization([](auto&, auto&, auto&, auto& f) { f = 99; },
        InitializationResult::InvalidPlayers, "UnknownFirstPlayer");
    badInitialization([](auto&, auto& d, auto&, auto&) { d.push_back(d[0]); },
        InitializationResult::InvalidDefinitions, "DuplicateDefinitionIds");
    badInitialization([](auto&, auto& d, auto&, auto&) { d[0].MaxHp = 0; },
        InitializationResult::InvalidDefinitions, "InvalidDefinitionHp");
    badInitialization([](auto&, auto& d, auto&, auto&) { d[0].Attack = -1; },
        InitializationResult::InvalidDefinitions, "InvalidDefinitionAttack");
    badInitialization([](auto&, auto&, auto& p, auto&) { p[1].Deck.push_back(99); },
        InitializationResult::UnknownDefinition, "UnknownDeckDefinition");
    badInitialization([](auto& c, auto&, auto& p, auto&) { c.InitialHandSize = 1; p[1].Deck.clear(); },
        InitializationResult::InsufficientCards, "AtomicInitialDealFailure");

    rejected(state, DrawCommand{ 1 }, CommandError::TurnNotStarted, "DrawBeforeStart");
    rejected(state, PlayCardCommand{ 1, 1 }, CommandError::TurnNotStarted, "PlayBeforeStart");
    rejected(state, StartTurnCommand{ 2 }, CommandError::NotActivePlayer, "StartInactivePlayer");
    rejected(state, StartTurnCommand{ 99 }, CommandError::PlayerNotFound, "StartMissingPlayer");
    CommandResult start = ProcessCommand(state, StartTurnCommand{ 1 });
    check(start.Code == CommandError::Success && state.TurnStarted
        && start.Events == std::vector<GameEvent>{ GameEvent{ .Type = GameEventType::TurnStarted,
            .TargetPlayerId = 1, .TurnNumber = 1, .Phase = TurnPhase::Main } }, "StartTurnEvent");
    rejected(state, StartTurnCommand{ 1 }, CommandError::TurnAlreadyStarted, "RepeatedStartRejected");
    rejected(state, DrawCommand{ 1, 0 }, CommandError::InvalidCount, "ZeroDrawRejected");
    rejected(state, DrawCommand{ 1, -1 }, CommandError::InvalidCount, "NegativeDrawRejected");
    rejected(state, DrawCommand{ 1, 4 }, CommandError::InsufficientCards, "PartialDrawRejectedAtomically");
    rejected(state, DrawCommand{ 2 }, CommandError::NotActivePlayer, "InactiveDraw");
    rejected(state, DrawCommand{ 99 }, CommandError::PlayerNotFound, "MissingDrawPlayer");
    rejected(state, PlayCardCommand{ 1, 5 }, CommandError::NotOwner, "EnemyHandPlay");
    rejected(state, PlayCardCommand{ 1, 2 }, CommandError::WrongZone, "DeckCardPlay");
    rejected(state, PlayCardCommand{ 1, 999 }, CommandError::CardNotFound, "MissingCardPlay");
    rejected(state, EndTurnCommand{ 1 }, CommandError::WrongPhase, "EndBeforeEndPhase");

    GameState expected = state;
    std::vector<GameEvent> draws = { Moved(expected.Players[0].Deck[0], Zone::Deck, Zone::Hand),
        Moved(expected.Players[0].Deck[1], Zone::Deck, Zone::Hand) };
    expected.Players[0].Hand.insert(expected.Players[0].Hand.end(),
        expected.Players[0].Deck.begin(), expected.Players[0].Deck.begin() + 2);
    expected.Players[0].Deck.erase(expected.Players[0].Deck.begin(), expected.Players[0].Deck.begin() + 2);
    auto result = ProcessCommand(state, DrawCommand{ 1, 2 });
    check(result.Code == CommandError::Success && result.Events == draws && state == expected,
        "MultiDrawOrderPayloadAndFullState");

    expected = state;
    CardInstance const card = expected.Players[0].Hand[0];
    expected.Players[0].Field.push_back(card);
    expected.Players[0].Hand.erase(expected.Players[0].Hand.begin());
    result = ProcessCommand(state, PlayCardCommand{ 1, card.Id });
    check(result.Code == CommandError::Success && state == expected
        && result.Events == std::vector<GameEvent>{ Moved(card, Zone::Hand, Zone::Field) },
        "PlayCardFullStateAndPayload");
    rejected(state, PlayCardCommand{ 1, card.Id }, CommandError::WrongZone, "ReplayPlayFromField");
    result = ProcessCommand(state, AdvancePhaseCommand{ 1 });
    check(result.Code == CommandError::Success && state.Phase == TurnPhase::Battle
        && result.Events == std::vector<GameEvent>{ GameEvent{ .Type = GameEventType::PhaseChanged,
            .TargetPlayerId = 1, .TurnNumber = 1, .Phase = TurnPhase::Battle } }, "AdvanceToBattle");
    rejected(state, DrawCommand{ 1 }, CommandError::WrongPhase, "DrawInBattle");
    rejected(state, PlayCardCommand{ 1, 2 }, CommandError::WrongPhase, "PlayInBattle");
    result = ProcessCommand(state, AdvancePhaseCommand{ 1 });
    check(result.Code == CommandError::Success && state.Phase == TurnPhase::End,
        "AdvanceToEnd");
    rejected(state, AdvancePhaseCommand{ 1 }, CommandError::WrongPhase, "AdvancePastEnd");
    expected = state;
    expected.ActivePlayerId = 2;
    expected.TurnNumber = 2;
    expected.TurnStarted = false;
    expected.Phase = TurnPhase::Main;
    result = ProcessCommand(state, EndTurnCommand{ 1 });
    check(result.Code == CommandError::Success && state == expected
        && result.Events == std::vector<GameEvent>{ GameEvent{ .Type = GameEventType::TurnEnded,
            .TargetPlayerId = 1, .TurnNumber = 1, .Phase = TurnPhase::End } }, "EndTurnTransition");
    rejected(state, EndTurnCommand{ 2 }, CommandError::TurnNotStarted, "EndUnstartedTurn");

    state = MakeGame();
    state.Config.DrawPerTurn = 2;
    result = ProcessCommand(state, StartTurnCommand{ 1 });
    check(result.Code == CommandError::Success && result.Events.size() == 3
        && state.Players[0].Hand.size() == 3 && state.Players[0].Deck.size() == 1,
        "ConfiguredTurnDraw");
    state = MakeGame();
    state.Config.DrawPerTurn = 4;
    rejected(state, StartTurnCommand{ 1 }, CommandError::InsufficientCards, "AtomicStartDrawFailure");
    state.Config.DrawPerTurn = 1;
    state.Config.HandLimit = 1;
    rejected(state, StartTurnCommand{ 1 }, CommandError::HandFull, "StartDrawHandFull");
    state = MakeGame();
    state.Players[0].Deck.clear();
    state.Config.DrawPerTurn = 1;
    rejected(state, StartTurnCommand{ 1 }, CommandError::DeckEmpty, "StartDrawEmptyDeck");
    state = MakeGame();
    result = ProcessCommand(state, StartTurnCommand{ 1 });
    state.Config.HandLimit = 1;
    rejected(state, DrawCommand{ 1 }, CommandError::HandFull, "ManualDrawHandFull");
    state.Config.AllowManualDraw = false;
    rejected(state, DrawCommand{ 1 }, CommandError::DrawDisabled, "ManualDrawDisabled");
    state.Config.FieldLimit = 0;
    rejected(state, PlayCardCommand{ 1, 1 }, CommandError::FieldFull, "FieldLimit");
    state = MakeGame();
    state.Config.CanAttackOnSummon = false;
    result = ProcessCommand(state, StartTurnCommand{ 1 });
    result = ProcessCommand(state, PlayCardCommand{ 1, 1 });
    check(result.Code == CommandError::Success && state.Players[0].Field[0].HasAttackedThisTurn,
        "ConfiguredSummonAttackRestriction");
    state.Players[0].Hand.push_back(state.Players[0].Field[0]);
    rejected(state, DrawCommand{ 1 }, CommandError::InvalidState, "CommandDuplicateIds");
    state = MakeGame();
    state.Players[0].Hand[0].OwnerId = 2;
    rejected(state, PlayCardCommand{ 1, 1 }, CommandError::InvalidState, "CommandCorruptOwnership");
    state = MakeGame();
    state.TurnStarted = true;
    state.Phase = TurnPhase::End;
    state.TurnNumber = std::numeric_limits<int>::max();
    rejected(state, EndTurnCommand{ 1 }, CommandError::TurnOverflow, "TurnNumberOverflow");

    state = MakeGame();
    state.TurnStarted = true;
    rejected(state, PlayCardCommand{ 2, 5 }, CommandError::NotActivePlayer, "InactivePlay");
    rejected(state, PlayCardCommand{ 99, 1 }, CommandError::PlayerNotFound, "MissingPlayPlayer");
    rejected(state, AdvancePhaseCommand{ 2 }, CommandError::NotActivePlayer, "InactiveAdvance");
    rejected(state, AdvancePhaseCommand{ 99 }, CommandError::PlayerNotFound, "MissingAdvancePlayer");
    rejected(state, EndTurnCommand{ 2 }, CommandError::NotActivePlayer, "InactiveEnd");
    rejected(state, EndTurnCommand{ 99 }, CommandError::PlayerNotFound, "MissingEndPlayer");
    state.TurnStarted = false;
    rejected(state, AdvancePhaseCommand{ 1 }, CommandError::TurnNotStarted, "AdvanceUnstartedTurn");
    state = MakeGame();
    state.TurnNumber = 0;
    rejected(state, StartTurnCommand{ 1 }, CommandError::InvalidState, "InvalidTurnNumber");
    state = MakeGame();
    state.Phase = static_cast<TurnPhase>(99);
    rejected(state, StartTurnCommand{ 1 }, CommandError::InvalidState, "InvalidPhaseState");
    state = MakeGame();
    state.Players[0].Life = 0;
    rejected(state, StartTurnCommand{ 1 }, CommandError::InvalidState, "RunningGameWithDeadPlayer");
    state = MakeGame();
    state.WinnerPlayerId = 1;
    rejected(state, StartTurnCommand{ 1 }, CommandError::InvalidState, "RunningGameWithWinner");
    state = MakeGame();
    state.Players[0].Hand[0].CurrentDefinitionId = 999;
    rejected(state, StartTurnCommand{ 1 }, CommandError::InvalidState, "UnknownCurrentDefinition");

    state = MakeGame();
    state.TurnStarted = true;
    state.Players[0].Deck.clear();
    rejected(state, DrawCommand{ 1 }, CommandError::DeckEmpty, "ManualDrawEmptyDeck");
    state = MakeGame();
    state.TurnStarted = true;
    state.Players[0].Hand[0].Hp = 0;
    rejected(state, PlayCardCommand{ 1, 1 }, CommandError::InvalidDefinition, "DestroyedHandCardCannotBePlayed");
}
