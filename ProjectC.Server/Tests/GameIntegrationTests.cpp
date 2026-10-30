#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"

#include <iostream>

using namespace ProjectC::GameCore;

void RunGameIntegrationTests(int& testCount, int& failedTests)
{
    auto check = [&](bool passed, char const* name)
    {
        ++testCount;
        if (!passed) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    GameConfig config;
    config.InitialHandSize = 1;
    GameState state;
    std::vector<CardDefinition> const definitions{
        { .Id = 10, .Name = "Attacker", .Attack = 3, .MaxHp = 5 },
        { .Id = 20, .Name = "Defender", .Attack = 4, .MaxHp = 3 }
    };
    std::array<PlayerSetup, 2> const players{
        PlayerSetup{ 1, { 10, 10 } }, PlayerSetup{ 2, { 20, 20 } }
    };
    bool const initialized = InitializeGame(state, config, definitions, players, 1)
        == InitializationResult::Success;
    check(initialized, "IntegrationInitializeTwoDecks");
    if (!initialized) return;
    GameState const initial = state;
    std::vector<GameCommand> commands{
        StartTurnCommand{ 1 }, PlayCardCommand{ 1, 1 },
        AdvancePhaseCommand{ 1 }, AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 },
        StartTurnCommand{ 2 }, PlayCardCommand{ 2, 3 },
        AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 },
        StartTurnCommand{ 1 }, AdvancePhaseCommand{ 1 }
    };
    std::vector<CommandResult> results;
    bool allSucceeded = true;
    for (GameCommand const& command : commands)
    {
        results.push_back(ProcessCommand(state, command));
        allSucceeded = allSucceeded && results.back().Code == CommandError::Success;
    }
    check(allSucceeded && state.Players[0].Field.size() == 1 && state.Players[1].Field.size() == 1
        && state.TurnNumber == 3 && state.ActivePlayerId == 1 && state.Phase == TurnPhase::Battle,
        "IntegrationBothSummonAndAdvanceTurns");
    if (!allSucceeded) return;
    GameState expected = state;
    expected.Players[0].Field[0].Hp = 1;
    expected.Players[0].Field[0].HasAttackedThisTurn = true;
    CardInstance destroyed = expected.Players[1].Field[0];
    destroyed.Hp = 0;
    expected.Players[1].Field.clear();
    expected.Players[1].Graveyard.push_back(destroyed);
    commands.push_back(AttackCommand{ 1, 1, 2, 3 });
    CommandResult result = ProcessCommand(state, commands.back());
    results.push_back(result);
    std::vector<GameEvent> const battleEvents{
        { .Type = GameEventType::BattleStarted,
            .SourcePlayerId = 1, .SourceCardId = 1, .TargetPlayerId = 2, .TargetCardId = 3 },
        { .Type = GameEventType::CardDamaged,
            .SourcePlayerId = 2, .SourceCardId = 3, .TargetPlayerId = 1, .TargetCardId = 1,
            .Damage = 4, .HpAfter = 1 },
        { .Type = GameEventType::CardDamaged,
            .SourcePlayerId = 1, .SourceCardId = 1, .TargetPlayerId = 2, .TargetCardId = 3,
            .Damage = 3, .HpAfter = 0 },
        { .Type = GameEventType::CardDestroyed, .TargetPlayerId = 2, .TargetCardId = 3 },
        { .Type = GameEventType::BattleCompleted,
            .SourcePlayerId = 1, .SourceCardId = 1, .TargetPlayerId = 2, .TargetCardId = 3 },
        { .Type = GameEventType::CardMoved, .TargetPlayerId = 2, .TargetCardId = 3,
            .FromZone = Zone::Field, .ToZone = Zone::Graveyard,
            .OriginalDefinitionId = 20, .CurrentDefinitionId = 20 }
    };
    check(result.Code == CommandError::Success && result.Events == battleEvents
        && state == expected && ValidateGameState(state), "IntegrationBattleAndGraveyardExactContract");
    GameState const beforeInvalidAttack = state;
    result = ProcessCommand(state, AttackCommand{ 1, 1, 2, 3 });
    check(result.Result == BattleResult::DefenderNotFound && result.Events.empty()
        && state == beforeInvalidAttack, "CannotAttackGraveyardCard");
    check(state.Players[0].Life == 20 && state.Players[1].Life == 20,
        "MonsterCombatDoesNotInventPlayerDamage");

    // Independent Life API scenario: no direct attacks or special card effects are assumed.
    commands.push_back(ChangeLifeCommand{ 2, -7 });
    results.push_back(ProcessCommand(state, commands.back()));
    commands.push_back(ChangeLifeCommand{ 2, -13 });
    results.push_back(ProcessCommand(state, commands.back()));
    expected.Players[1].Life = 0;
    expected.Status = GameStatus::Finished;
    expected.WinnerPlayerId = 1;
    check(state == expected && results.back().Code == CommandError::Success
        && ValidateGameState(state), "IntegrationLifeScenarioEndsGame");
    ReplayResult const replay = ReplayCommands(initial, commands);
    check(replay.FinalState == expected && replay.Results == results
        && replay == ReplayCommands(initial, commands), "IntegrationFullGameReplay");

    // A future manifested form must still place the original definition in the graveyard.
    state = initial;
    CardInstance card = state.Players[0].Hand[0];
    state.Players[0].Hand.clear();
    card.CurrentDefinitionId = 20;
    card.Hp = 0;
    card.HasAttackedThisTurn = true;
    state.Players[0].Field.push_back(card);
    auto const moved = MoveCard(state, 1, card.Id, Zone::Field, Zone::Graveyard);
    CardInstance expectedCard = card;
    expectedCard.CurrentDefinitionId = card.OriginalDefinitionId;
    check(moved == ZoneResult::Success && state.Players[0].Graveyard[0] == expectedCard,
        "GraveyardOriginalDefinitionAndInstanceIdentity");
}
