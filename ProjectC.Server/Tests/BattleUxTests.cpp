#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"

#include <iostream>
#include <stdexcept>
#include <vector>

using namespace ProjectC::GameCore;

void RunBattleUxTests(int& testCount, int& failedTests)
{
    auto check = [&](bool passed, char const* name)
    {
        ++testCount;
        if (!passed)
        {
            ++failedTests;
            std::cerr << "FAILED: " << name << '\n';
        }
    };
    GameConfig config;
    config.InitialLife = 100;
    config.InitialHandSize = 3;
    config.SummoningSickness = false;
    GameState state;
    std::vector<CardDefinition> definitions{{.Id = 10, .Attack = 3, .MaxHp = 5}};
    if (InitializeGame(state,
                       config,
                       definitions,
                       {PlayerSetup{1, {10, 10, 10, 10}}, PlayerSetup{2, {10, 10, 10, 10}}},
                       1) != InitializationResult::Success)
    {
        throw std::runtime_error("Battle UX fixture initialization failed");
    }
    state.Players[0].Field = state.Players[0].Hand;
    state.Players[0].Hand.clear();
    state.Players[0].Field.back().DirectAttackRestrictedTurn = state.TurnNumber;
    auto const initial = state;
    std::vector<GameCommand> commands;
    std::vector<CommandResult> results;
    auto run = [&](GameCommand command)
    {
        commands.push_back(command);
        results.push_back(ProcessCommand(state, command));
        return results.back();
    };
    check(run(StartTurnCommand{1}).Code == CommandError::Success, "BattleUxStart");
    check(run(AdvancePhaseCommand{1}).Code == CommandError::Success &&
              state.Phase == TurnPhase::Battle,
          "BattleUxMain1ToBattle");
    check(run(DirectAttackCommand{1, 1, 2}).Code == CommandError::Success,
          "FirstMonsterDirectAttack");
    auto const beforeRepeat = state;
    auto repeat = run(DirectAttackCommand{1, 1, 2});
    check(repeat.Result == BattleResult::AlreadyAttacked && state == beforeRepeat,
          "SameMonsterCannotDirectAttackTwiceAndFailureIsAtomic");
    check(run(DirectAttackCommand{1, 2, 2}).Code == CommandError::Success &&
              state.Players[1].Life == 94,
          "SecondMonsterDirectAttackIsIndependent");
    auto const beforeRestriction = state;
    check(run(DirectAttackCommand{1, 3, 2}).Code == CommandError::DirectAttackRestricted &&
              state == beforeRestriction && !state.Players[0].Field.back().HasAttackedThisTurn,
          "RevivalRestrictionIsPerCardAndDoesNotConsumeAttack");
    check(run(AdvancePhaseCommand{1}).Code == CommandError::Success &&
              state.Phase == TurnPhase::Main2,
          "BattleToMain2");
    auto const main2 = state;
    check(run(DirectAttackCommand{1, 3, 2}).Result == BattleResult::NotBattlePhase &&
              state == main2,
          "Main2CannotAttack");
    check(run(DrawCommand{1, 1}).Code == CommandError::Success &&
              run(PlayCardCommand{1, state.Players[0].Hand.front().Id}).Code ==
                  CommandError::Success,
          "Main2RetainsMainPhaseActions");
    check(run(AdvancePhaseCommand{1}).Code == CommandError::Success &&
              state.Phase == TurnPhase::End,
          "Main2ToEnd");
    check(run(EndTurnCommand{1}).Code == CommandError::Success &&
              run(StartTurnCommand{2}).Code == CommandError::Success &&
              state.Phase == TurnPhase::Main && state.TurnNumber == 2,
          "EndStartsNextPlayerMain1");
    auto const replay = ReplayCommands(initial, commands);
    check(replay.FinalState == state && replay.Results == results,
          "BattleUxReplayIncludesRejectedCommands");
}
