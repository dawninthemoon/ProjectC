#include "ProjectC/GameReplay.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <iostream>
#include <limits>
#include <stdexcept>

using namespace ProjectC::GameCore;

namespace
{
    GameState InitialState()
    {
        GameState state;
        GameConfig config;
        config.InitialHandSize = 1;
        std::vector<CardDefinition> definitions{ { .Id = 10, .Name = "Unit", .Attack = 2, .MaxHp = 4 } };
        std::array<PlayerSetup, 2> players{ PlayerSetup{ 1, { 10, 10 } }, PlayerSetup{ 2, { 10, 10 } } };
        if (InitializeGame(state, config, definitions, players, 1) != InitializationResult::Success)
            throw std::runtime_error("replay fixture initialization failed");
        return state;
    }
}

void RunGameReplayTests(int& testCount, int& failedTests)
{
    auto check = [&](bool passed, char const* name)
    {
        ++testCount;
        if (!passed) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    auto rejected = [&](GameState state, GameCommand const& command, CommandError error, char const* name)
    {
        GameState const before = state;
        CommandResult const result = ProcessCommand(state, command);
        check(result.Code == error && result.Events.empty() && state == before, name);
    };
    GameState state = InitialState();
    GameState expected = state;
    expected.Players[1].Life = 17;
    CommandResult result = ChangePlayerLife(state, 2, -3);
    check(result.Code == CommandError::Success && state == expected
        && result.Events == std::vector<GameEvent>{ GameEvent{ .Type = GameEventType::LifeChanged,
            .TargetPlayerId = 2, .LifeBefore = 20, .LifeAfter = 17 } }, "LifeDamageFullStateAndEvent");
    result = ChangePlayerLife(state, 2, 2);
    check(result.Code == CommandError::Success && state.Players[1].Life == 19
        && state.Status == GameStatus::Running, "LifeHealing");
    rejected(state, ChangeLifeCommand{ 99, -1 }, CommandError::PlayerNotFound, "MissingLifeTarget");
    rejected(state, ChangeLifeCommand{ 2, std::numeric_limits<int>::max() },
        CommandError::InvalidLifeChange, "LifePositiveOverflow");
    result = ChangePlayerLife(state, 2, std::numeric_limits<int>::min());
    expected.Players[1].Life = 0;
    expected.Status = GameStatus::Finished;
    expected.WinnerPlayerId = 1;
    check(result.Code == CommandError::Success && state == expected
        && result.Events == std::vector<GameEvent>{
            GameEvent{ .Type = GameEventType::LifeChanged, .TargetPlayerId = 2,
                .LifeBefore = 19, .LifeAfter = 0 },
            GameEvent{ .Type = GameEventType::GameEnded, .WinnerPlayerId = 1 } },
        "LethalLifeChangeClampAndVictoryOrder");
    for (GameCommand const& command : std::vector<GameCommand>{
        AttackCommand{ 1, 1, 2, 3 }, DrawCommand{ 1 }, PlayCardCommand{ 1, 1 },
        StartTurnCommand{ 1 }, EndTurnCommand{ 1 }, AdvancePhaseCommand{ 1 }, ChangeLifeCommand{ 2, 1 } })
        rejected(state, command, CommandError::GameFinished, "AllCommandsRejectFinishedGame");
    GameState const finished = state;
    StartTurn(state, 2);
    AdvancePhase(state);
    EndTurn(state);
    check(ResolveBattle(state, 1, 1, 2, 3) == BattleResult::GameFinished && state == finished,
        "LowLevelTurnAndBattleRejectFinishedGame");
    check(MoveCard(state, 1, 1, Zone::Hand, Zone::Field) == ZoneResult::GameFinished
        && state == finished, "ZoneMoveRejectFinishedGame");

    state = InitialState();
    result = ChangePlayerLife(state, 1, -20);
    check(result.Code == CommandError::Success && state.WinnerPlayerId == 2,
        "EitherPlayerCanWin");
    state = InitialState();
    result = ChangePlayerLife(state, 1, std::numeric_limits<int>::max() - 20);
    check(result.Code == CommandError::Success && state.Players[0].Life == std::numeric_limits<int>::max(),
        "LifeMaximumBoundary");

    GameState const initial = InitialState();
    std::vector<GameCommand> const commands = {
        DrawCommand{ 1 }, // Intentionally rejected, and preserved in the replay transcript.
        StartTurnCommand{ 1 }, DrawCommand{ 1 }, PlayCardCommand{ 1, 1 },
        AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 },
        AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 }, StartTurnCommand{ 2 },
        PlayCardCommand{ 2, 3 }, ChangeLifeCommand{ 1, -20 }, DrawCommand{ 2 }
    };
    ReplayResult const replay = ReplayCommands(initial, commands);
    check(replay == ReplayCommands(initial, commands) && initial == InitialState(),
        "ReplaySameInitialStateAndSequence");
    GameState direct = initial;
    std::vector<CommandResult> results;
    std::vector<GameState> snapshots;
    for (GameCommand const& command : commands)
    {
        results.push_back(ProcessCommand(direct, command));
        snapshots.push_back(direct);
    }
    check(replay.FinalState == direct && replay.Results == results,
        "ReplayMatchesDirectCommandResultsAndFullState");
    bool everyPrefixMatches = true;
    for (std::size_t length = 1; length <= commands.size(); ++length)
    {
        std::vector<GameCommand> const prefix(commands.begin(), commands.begin() + length);
        ReplayResult const prefixReplay = ReplayCommands(initial, prefix);
        everyPrefixMatches = everyPrefixMatches && prefixReplay.FinalState == snapshots[length - 1]
            && prefixReplay.Results == std::vector<CommandResult>(results.begin(), results.begin() + length);
    }
    check(everyPrefixMatches, "ReplayEveryCommandPrefixFullStateAndPayloads");
    check(replay.Results.front().Code == CommandError::TurnNotStarted
        && replay.Results[5].Code == CommandError::WrongPhase
        && replay.Results.back().Code == CommandError::GameFinished
        && replay.FinalState.Status == GameStatus::Finished && replay.FinalState.WinnerPlayerId == 2,
        "ReplayIncludesFailuresAndTerminalCommands");
    ReplayResult const empty = ReplayCommands(initial, {});
    check(empty.FinalState == initial && empty.Results.empty(), "EmptyReplay");
}
