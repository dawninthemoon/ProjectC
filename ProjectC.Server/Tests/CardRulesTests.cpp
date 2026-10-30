#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"

#include <iostream>
#include <stdexcept>

using namespace ProjectC::GameCore;

namespace
{
    void Run(GameState& state, GameCommand const& command)
    {
        if (ProcessCommand(state, command).Code != CommandError::Success)
            throw std::runtime_error("rule scenario setup failed");
    }

    GameState Game()
    {
        GameState state;
        GameConfig config;
        config.InitialHandSize = 2;
        std::vector<CardDefinition> definitions{ { .Id = 1, .Attack = 3, .MaxHp = 5 } };
        std::array<PlayerSetup, 2> players{ PlayerSetup{ 1, { 1, 1, 1 } }, PlayerSetup{ 2, { 1, 1, 1 } } };
        if (InitializeGame(state, config, definitions, players, 1) != InitializationResult::Success)
            throw std::runtime_error("rule initialization failed");
        Run(state, StartTurnCommand{ 1 });
        return state;
    }

    void NextTurn(GameState& state)
    {
        PlayerId const player = state.ActivePlayerId;
        if (state.Phase == TurnPhase::Main) Run(state, AdvancePhaseCommand{ player });
        if (state.Phase == TurnPhase::Battle) Run(state, AdvancePhaseCommand{ player });
        Run(state, EndTurnCommand{ player });
        Run(state, StartTurnCommand{ state.ActivePlayerId });
    }
}

void RunCardRulesTests(int& testCount, int& failedTests)
{
    auto check = [&](bool value, char const* name)
    {
        ++testCount;
        if (!value) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    auto rejected = [&](GameState state, GameCommand const& command, CommandError code,
        BattleResult battle, char const* name)
    {
        GameState const before = state;
        auto const result = ProcessCommand(state, command);
        check(result.Code == code && result.Result == battle && result.Events.empty() && state == before, name);
    };
    GameState state = Game();
    Run(state, PlayCardCommand{ 1, 1 });
    check(state.Players[0].NormalSummonsThisTurn == 1 && state.Players[0].Life == 20
        && !state.Players[0].Field[0].HasAttackedThisTurn && IsSummoningSick(state, state.Players[0].Field[0]),
        "FreeNormalSummonAndSeparateSickness");
    rejected(state, PlayCardCommand{ 1, 2 }, CommandError::NormalSummonLimit,
        BattleResult::Success, "OneNormalSummonPerTurn");
    Run(state, AdvancePhaseCommand{ 1 });
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::BattleRejected,
        BattleResult::SummoningSickness, "SameTurnDirectAttackRejected");
    if (MoveCard(state, 2, 4, Zone::Hand, Zone::Field) != ZoneResult::Success)
        throw std::runtime_error("rule field setup failed");
    rejected(state, AttackCommand{ 1, 1, 2, 4 }, CommandError::BattleRejected,
        BattleResult::SummoningSickness, "SameTurnMonsterAttackRejected");
    StartTurn(state, 1); // Resetting attack flags alone cannot cure sickness or reset normal summons.
    check(IsSummoningSick(state, state.Players[0].Field[0])
        && state.Players[0].NormalSummonsThisTurn == 1, "FlagResetDoesNotCureSickness");
    NextTurn(state);
    check(IsSummoningSick(state, state.Players[0].Field[0])
        && !IsSummoningSick(state, state.Players[1].Field[0]), "OpponentTurnEntryReadiness");
    NextTurn(state);
    check(!IsSummoningSick(state, state.Players[0].Field[0])
        && state.Players[0].NormalSummonsThisTurn == 0, "NextOwnerTurnReadinessAndSummonReset");
    Run(state, PlayCardCommand{ 1, 2 });
    check(state.Players[0].NormalSummonsThisTurn == 1 && IsSummoningSick(state, state.Players[0].Field[1]),
        "NormalSummonAvailableNextOwnTurn");
    Run(state, AdvancePhaseCommand{ 1 });
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::DefenderHasMonsters,
        BattleResult::Success, "DirectAttackBlockedByLivingMonster");
    if (MoveCard(state, 2, 4, Zone::Field, Zone::Hand) != ZoneResult::Success)
        throw std::runtime_error("rule return setup failed");
    GameState const before = state;
    auto result = ProcessCommand(state, DirectAttackCommand{ 1, 1, 2 });
    GameState expected = before;
    expected.Players[0].Field[0].HasAttackedThisTurn = true;
    expected.Players[1].Life = 17;
    check(result.Code == CommandError::Success && state == expected
        && result.Events == std::vector<GameEvent>{
            GameEvent{ .Type = GameEventType::PlayerDamaged, .SourcePlayerId = 1, .SourceCardId = 1,
                .TargetPlayerId = 2, .Damage = 3, .LifeBefore = 20, .LifeAfter = 17 },
            GameEvent{ .Type = GameEventType::LifeChanged, .TargetPlayerId = 2, .LifeBefore = 20, .LifeAfter = 17 } },
        "DirectAttackExactStateAndEvents");
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::BattleRejected,
        BattleResult::AlreadyAttacked, "DirectAttackTwiceRejected");
    state = before;
    state.Players[1].Life = 2;
    result = ProcessCommand(state, DirectAttackCommand{ 1, 1, 2 });
    check(result.Code == CommandError::Success && state.Players[1].Life == 0 && state.WinnerPlayerId == 1
        && state.Status == GameStatus::Finished && ValidateGameState(state)
        && result.Events.back().Type == GameEventType::GameFinished, "DirectAttackLethal");
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::GameFinished,
        BattleResult::Success, "DirectAttackAfterGameEndRejected");
    state = before;
    state.Players[0].Field[0].Attack = -5;
    result = ProcessCommand(state, DirectAttackCommand{ 1, 1, 2 });
    check(result.Code == CommandError::Success && state.Players[1].Life == 20
        && result.Events[0].Damage == 0, "DirectAttackNegativeDamageClamped");
    state = before;
    state.Players[0].Field[0].Hp = 0;
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::BattleRejected,
        BattleResult::AttackerDestroyed, "DestroyedDirectAttackerRejected");
    state = before;
    state.Phase = TurnPhase::Main;
    rejected(state, DirectAttackCommand{ 1, 1, 2 }, CommandError::BattleRejected,
        BattleResult::NotBattlePhase, "DirectAttackWrongPhase");
    state = before;
    rejected(state, DirectAttackCommand{ 2, 4, 1 }, CommandError::BattleRejected,
        BattleResult::NotActivePlayer, "InactiveDirectAttacker");
    rejected(state, DirectAttackCommand{ 1, 999, 2 }, CommandError::BattleRejected,
        BattleResult::AttackerNotFound, "MissingDirectAttacker");
    rejected(state, DirectAttackCommand{ 1, 1, 1 }, CommandError::BattleRejected,
        BattleResult::DefenderNotFound, "FriendlyDirectAttackRejected");
    state = before;
    if (MoveCard(state, 1, 1, Zone::Field, Zone::Hand) != ZoneResult::Success
        || MoveCard(state, 1, 1, Zone::Hand, Zone::Field) != ZoneResult::Success)
        throw std::runtime_error("rule reentry setup failed");
    check(IsSummoningSick(state, state.Players[0].Field.back()), "FieldReentryRestoresSickness");

    state = Game();
    GameState const initial = state;
    result = ProcessCommand(state, DrawCommand{ 1, 2 });
    check(result.Code == CommandError::Success && state.Players[0].Hand.size() == 3
        && state.Players[0].Deck.empty() && state.Players[0].Life == 20
        && state.EndReason == GameEndReason::DeckExhausted
        && result.Events.size() == 2 && result.Events[0].Type == GameEventType::CardMoved
        && result.Events[1].Type == GameEventType::GameFinished, "PartialDrawThenDeckLossExactOrder");
    auto const replay = ReplayCommands(initial, { DrawCommand{ 1, 2 } });
    check(replay.FinalState == state && replay.Results == std::vector<CommandResult>{ result }, "ReplayDeckLoss");
    state = Game();
    Run(state, DrawCommand{ 1, 1 });
    check(state.Players[0].Deck.empty() && state.Status == GameStatus::Running, "EmptyDeckAloneDoesNotLose");
}
