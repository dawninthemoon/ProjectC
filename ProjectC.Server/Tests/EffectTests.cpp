#include "ProjectC/CardEffects.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/GameReplay.hpp"

#include <algorithm>
#include <iostream>
#include <limits>
#include <stdexcept>

using namespace ProjectC::GameCore;

namespace
{
    EffectAction Action(EffectOperation operation, TargetCategory category,
        Zone zone = Zone::Field, int amount = 1)
    {
        return EffectAction{ .Operation = operation,
            .Targets = TargetRule{ .Category = category, .Zones = { zone } }, .Amount = amount };
    }

    EffectDefinition Effect(std::vector<EffectAction> actions)
    {
        return EffectDefinition{ .Id = 1, .Actions = std::move(actions) };
    }

    GameState Game(std::vector<EffectDefinition> effects, bool field = true, int life = 20)
    {
        GameState state;
        GameConfig config;
        config.InitialLife = life;
        config.InitialHandSize = 1;
        std::vector<CardDefinition> definitions{
            { .Id = 1, .Name = "Caster", .Attack = 2, .MaxHp = 5,
                .Traits = { "Caster" }, .AllowedManifestations = { 2, 3 }, .Effects = std::move(effects) },
            { .Id = 2, .Name = "Manifested", .Attack = 6, .MaxHp = 8, .AllowedManifestations = { 1, 3 } },
            { .Id = 3, .Name = "Fragile", .Attack = 1, .MaxHp = 1 },
            { .Id = 4, .Name = "Yokai", .Attack = 3, .MaxHp = 4, .Traits = { "Yokai" } }
        };
        std::array<PlayerSetup, 2> players{ PlayerSetup{ 1, { 1, 4, 4, 4, 4 } },
            PlayerSetup{ 2, { 4, 4, 4 } } };
        if (InitializeGame(state, config, definitions, players, 1) != InitializationResult::Success
            || ProcessCommand(state, StartTurnCommand{ 1 }).Code != CommandError::Success)
            throw std::runtime_error("effect fixture initialization failed");
        if (field && MoveCard(state, 1, 1, Zone::Hand, Zone::Field) != ZoneResult::Success)
            throw std::runtime_error("effect fixture field entry failed");
        return state;
    }

    void Move(GameState& state, PlayerId owner, CardInstanceId card, Zone from, Zone to)
    {
        if (MoveCard(state, owner, card, from, to) != ZoneResult::Success)
            throw std::runtime_error("effect fixture zone setup failed");
    }

    ActivateEffectCommand Activate(std::vector<std::vector<EffectTarget>> targets)
    {
        return ActivateEffectCommand{ .Player = 1, .SourceCard = 1, .EffectId = 1, .Targets = std::move(targets) };
    }

    bool Replays(GameState const& before, GameCommand const& command, GameState const& after, CommandResult const& result)
    {
        auto const replay = ReplayCommands(before, { command });
        return replay.FinalState == after && replay.Results == std::vector<CommandResult>{ result };
    }
}

void RunEffectTests(int& testCount, int& failedTests)
{
    auto check = [&](bool value, char const* name)
    {
        ++testCount;
        if (!value) { ++failedTests; std::cerr << "FAILED: " << name << '\n'; }
    };
    auto rejected = [&](GameState state, GameCommand const& command, CommandError code, char const* name)
    {
        GameState const before = state;
        auto const result = ProcessCommand(state, command);
        check(result.Code == code && result.Events.empty() && state == before, name);
    };
    EffectTarget const player{ 1, 0, Zone::Field };
    EffectTarget const enemyPlayer{ 2, 0, Zone::Field };
    EffectTarget const self{ 1, 1, Zone::Field };
    EffectTarget const enemy{ 2, 6, Zone::Field };
    GameState state = Game({ Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer, Zone::Field, 2) }) });
    GameState before = state;
    auto command = Activate({ { player } });
    auto result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Hand.size() == 2
        && state.Players[0].Deck.size() == 2 && result.Events.size() == 4
        && result.Events.front().Type == GameEventType::EffectActivated
        && result.Events.back().Type == GameEventType::EffectResolved
        && Replays(before, command, state, result), "EffectDrawAndReplay");
    auto const privateEvents = EventsForPlayer(result.Events, 2);
    check(privateEvents[1].TargetCardId == 0 && privateEvents[1].CurrentDefinitionId == 0
        && privateEvents[2].TargetCardId == 0 && EventsForPlayer(result.Events, 1) == result.Events,
        "PrivateDrawEventProjection");
    state = Game({ Effect({ Action(EffectOperation::Mill, TargetCategory::FriendlyPlayer, Zone::Field, 99) }) });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Deck.empty()
        && state.Players[0].Graveyard.size() == 4 && state.Status == GameStatus::Running
        && Replays(before, command, state, result), "MillAllCardsWithoutDeckLoss");
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Status == GameStatus::Running,
        "MillEmptyDeckDoesNotLose");
    state = Game({ Effect({ Action(EffectOperation::Salvage, TargetCategory::FriendlyGraveyard, Zone::Graveyard) }) });
    Move(state, 1, 2, Zone::Deck, Zone::Graveyard);
    state.Players[0].Graveyard[0].Hp = 0;
    command = Activate({ { EffectTarget{ 1, 2, Zone::Graveyard } } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Graveyard.empty()
        && state.Players[0].Hand[0].Hp == 4 && Replays(before, command, state, result), "SalvageRestoresPlayableOriginal");
    result = ProcessCommand(state, PlayCardCommand{ 1, 2 });
    check(result.Code == CommandError::Success && IsSummoningSick(state, state.Players[0].Field.back()),
        "RecoveredCardCanBeNormallySummoned");
    auto search = Action(EffectOperation::Search, TargetCategory::FriendlyDeck, Zone::Deck);
    search.Targets.RequiredTrait = "Yokai";
    state = Game({ Effect({ search }) });
    command = Activate({ { EffectTarget{ 1, 3, Zone::Deck } } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Hand[0].Id == 3
        && state.Players[0].Deck[0].Id == 2 && state.Players[0].Deck[1].Id == 4
        && Replays(before, command, state, result), "SearchTraitAndRemainingDeckOrder");
    state = Game({ Effect({ Action(EffectOperation::SummonFromDeck, TargetCategory::FriendlyDeck, Zone::Deck) }) });
    Move(state, 1, 2, Zone::Deck, Zone::Hand);
    result = ProcessCommand(state, PlayCardCommand{ 1, 2 });
    command = Activate({ { EffectTarget{ 1, 3, Zone::Deck } } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].NormalSummonsThisTurn == 1
        && state.Players[0].Field.size() == 3 && IsSummoningSick(state, state.Players[0].Field.back())
        && !state.Players[0].Field.back().HasAttackedThisTurn && Replays(before, command, state, result),
        "SpecialSummonSeparateFromNormalLimitAndSickness");
    state = Game({ Effect({ Action(EffectOperation::SummonFromGraveyard,
        TargetCategory::FriendlyGraveyard, Zone::Graveyard) }) });
    Move(state, 1, 2, Zone::Deck, Zone::Graveyard);
    state.Players[0].Graveyard[0].Hp = 0;
    command = Activate({ { EffectTarget{ 1, 2, Zone::Graveyard } } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Graveyard.empty()
        && state.Players[0].Field.back().Hp == 4 && state.Players[0].NormalSummonsThisTurn == 0
        && IsSummoningSick(state, state.Players[0].Field.back()) && Replays(before, command, state, result),
        "GraveyardSpecialSummonRestoresStats");
    state = Game({ Effect({ Action(EffectOperation::ReturnToHand, TargetCategory::EnemyMonster) }) });
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    command = Activate({ { enemy } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[1].Field.empty()
        && state.Players[1].Hand[0].Id == 6 && Replays(before, command, state, result), "ReturnEnemyToOwnersHand");
    state = Game({ Effect({ Action(EffectOperation::Destroy, TargetCategory::EnemyMonster) }) });
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[1].Field.empty()
        && state.Players[1].Graveyard[0].Hp == 0
        && result.Events[1].Type == GameEventType::CardDestroyed
        && result.Events[2].Type == GameEventType::CardMoved && Replays(before, command, state, result), "DestroyEffect");
    state = Game({ Effect({ Action(EffectOperation::DealDamage, TargetCategory::EnemyMonster, Zone::Field, 3) }) });
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[1].Field[0].Hp == 1
        && result.Events[1].Damage == 3 && Replays(before, command, state, result), "MonsterDamageEffect");
    state = Game({ Effect({ Action(EffectOperation::DealDamage, TargetCategory::EnemyPlayer, Zone::Field, 5) }) });
    command = Activate({ { enemyPlayer } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[1].Life == 15
        && Replays(before, command, state, result), "PlayerDamageEffect");
    state = Game({ Effect({ Action(EffectOperation::Heal, TargetCategory::Self, Zone::Field, 99) }) });
    state.Players[0].Field[0].Hp = 1;
    command = Activate({ { self } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field[0].Hp == 5
        && result.Events[1].Type == GameEventType::CardHealed && Replays(before, command, state, result), "HealCardClampsAtMax");
    state = Game({ Effect({ Action(EffectOperation::Heal, TargetCategory::FriendlyPlayer, Zone::Field, 3) }) });
    command = Activate({ { player } });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Life == 23, "HealPlayerLife");
    auto stats = Action(EffectOperation::ModifyStats, TargetCategory::Self);
    stats.AttackDelta = -10;
    stats.MaxHpDelta = 3;
    state = Game({ Effect({ stats }) });
    state.Players[0].Field[0].Hp = 3;
    command = Activate({ { self } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field[0].Attack == -8
        && state.Players[0].Field[0].MaxHp == 8 && state.Players[0].Field[0].Hp == 6
        && Replays(before, command, state, result), "ModifyStatsPreservesDamage");
    stats.MaxHpDelta = -5;
    state = Game({ Effect({ stats }) });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field.empty()
        && state.Players[0].Graveyard[0].Hp == 0, "StatsReductionCanDestroy");
    stats.MaxHpDelta = std::numeric_limits<int>::max();
    state = Game({ Effect({ stats }) });
    rejected(state, command, CommandError::InvalidEffect, "StatsOverflowAtomic");

    state = Game({});
    state.Players[0].Field[0].Hp = 3;
    state.Players[0].Field[0].HasAttackedThisTurn = true;
    before = state;
    result = TransformCard(state, 1, 1, 2);
    CardInstance expected = before.Players[0].Field[0];
    expected.CurrentDefinitionId = 2;
    expected.Attack = 6;
    expected.MaxHp = 8;
    expected.Hp = 6;
    expected.HasManifested = true;
    check(result.Code == CommandError::Success && state.Players[0].Field[0] == expected
        && IsSummoningSick(state, state.Players[0].Field[0])
        && Replays(before, TransformCommand{ 1, 1, 2 }, state, result), "ManifestIdentityDamageFlagAndSickness");
    rejected(state, TransformCommand{ 1, 1, 3 }, CommandError::InvalidTransformation, "RemanifestationDefaultDenied");
    state.Config.AllowRemanifestation = true;
    rejected(state, TransformCommand{ 1, 1, 1 }, CommandError::InvalidTransformation, "CannotUndoManifestation");
    result = TransformCard(state, 1, 1, 3);
    check(result.Code == CommandError::Success && state.Players[0].Field.empty()
        && state.Players[0].Graveyard[0].Id == 1 && state.Players[0].Graveyard[0].CurrentDefinitionId == 1
        && state.Players[0].Graveyard[0].OriginalDefinitionId == 1
        && state.Players[0].Graveyard[0].HasAttackedThisTurn, "ManifestLethalHpAndOriginalInGraveyard");
    state = Game({});
    rejected(state, TransformCommand{ 1, 1, 999 }, CommandError::InvalidTransformation, "UnknownManifestForm");
    rejected(state, TransformCommand{ 2, 1, 2 }, CommandError::InvalidTarget, "ManifestWrongOwner");
    auto transform = Action(EffectOperation::Transform, TargetCategory::Self);
    transform.TransformDefinitionId = 2;
    state = Game({ Effect({ transform }) });
    command = Activate({ { self } });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field[0].CurrentDefinitionId == 2,
        "DefinitionAuthorizedTransformEffect");

    state = Game({ Effect({ search }) });
    rejected(state, Activate({}), CommandError::InvalidTargetCount, "WrongActionTargetGroupCount");
    rejected(state, Activate({ {} }), CommandError::InvalidTargetCount, "MissingEffectTarget");
    rejected(state, Activate({ { EffectTarget{ 2, 7, Zone::Deck } } }), CommandError::InvalidTarget,
        "CannotReadEnemyPrivateDeck");
    rejected(state, Activate({ { self } }), CommandError::InvalidTarget, "SearchWrongZone");
    auto badCommand = Activate({ { EffectTarget{ 1, 2, Zone::Deck } } });
    badCommand.SourceCard = 2;
    rejected(state, badCommand, CommandError::CardNotFound, "SourceMustBeOnField");
    badCommand.SourceCard = 1;
    badCommand.EffectId = 99;
    rejected(state, badCommand, CommandError::EffectNotFound, "UnknownEffectId");
    badCommand.Player = 2;
    rejected(state, badCommand, CommandError::NotActivePlayer, "InactiveEffectActivation");
    search.Targets.RequiredTrait = "MissingTrait";
    state = Game({ Effect({ search }) });
    rejected(state, Activate({ { EffectTarget{ 1, 2, Zone::Deck } } }), CommandError::InvalidTarget, "TraitFilterFailure");
    search.Targets.RequiredTrait = "Yokai";
    search.Targets.MaxCount = 2;
    state = Game({ Effect({ search }) });
    rejected(state, Activate({ { EffectTarget{ 1, 2, Zone::Deck }, EffectTarget{ 1, 2, Zone::Deck } } }),
        CommandError::InvalidTarget, "DuplicateTargetRejected");

    state = Game({ Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer),
        Action(EffectOperation::Destroy, TargetCategory::EnemyMonster) }) });
    rejected(state, Activate({ { player }, { EffectTarget{ 2, 999, Zone::Field } } }),
        CommandError::InvalidTarget, "LateEffectFailureRollsBackEarlierDraw");
    state = Game({ Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer),
        Action(EffectOperation::Mill, TargetCategory::FriendlyPlayer),
        Action(EffectOperation::Salvage, TargetCategory::FriendlyGraveyard, Zone::Graveyard) }) });
    command = Activate({ { player }, { player }, { EffectTarget{ 1, 3, Zone::Graveyard } } });
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Hand.size() == 2
        && state.Players[0].Hand[0].Id == 2 && state.Players[0].Hand[1].Id == 3
        && state.Players[0].Graveyard.empty() && Replays(before, command, state, result), "ChainedDrawMillSalvageAndReplay");
    state = Game({ Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer, Zone::Field, 99),
        Action(EffectOperation::Destroy, TargetCategory::EnemyMonster) }) });
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    before = state;
    command = Activate({ { player }, { enemy } });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Status == GameStatus::Finished
        && state.EndReason == GameEndReason::DeckExhausted && state.Players[0].Hand.size() == 4
        && state.Players[1].Field.size() == 1 && result.Events.back().Type == GameEventType::GameFinished
        && Replays(before, command, state, result), "EffectDeckLossCommitsAndStopsFollowingActions");
    rejected(state, command, CommandError::GameFinished, "EffectAfterFinishedGame");

    state = Game({ Effect({ Action(EffectOperation::SummonFromDeck, TargetCategory::FriendlyDeck, Zone::Deck) }) });
    state.Config.FieldLimit = 1;
    rejected(state, Activate({ { EffectTarget{ 1, 2, Zone::Deck } } }), CommandError::FieldFull, "EffectFieldLimitAtomic");
    state = Game({ Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer) }) });
    Move(state, 1, 2, Zone::Deck, Zone::Hand);
    state.Config.HandLimit = 1;
    rejected(state, Activate({ { player } }), CommandError::HandFull, "EffectHandLimitAtomic");

    for (EffectType const kind : { EffectType::Continuous, EffectType::Replacement })
    {
        auto unsupported = Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer) });
        unsupported.Type = kind;
        state = Game({ unsupported });
        rejected(state, Activate({ { player } }), CommandError::UnsupportedEffect, "UnimplementedEffectTypeExplicitlyRejected");
    }

    auto onSummoned = Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer) });
    onSummoned.Type = EffectType::Triggered;
    onSummoned.Trigger = TriggerCondition::OnSummoned;
    state = Game({ onSummoned }, false);
    before = state;
    result = ProcessCommand(state, PlayCardCommand{ 1, 1 });
    check(result.Code == CommandError::Success && state.Players[0].Hand[0].Id == 2
        && result.Events.size() == 5 && result.Events[2].Type == GameEventType::EffectActivated
        && Replays(before, PlayCardCommand{ 1, 1 }, state, result), "OnSummonedDrawAndReplay");
    auto death = Effect({ Action(EffectOperation::DealDamage, TargetCategory::EnemyPlayer, Zone::Field, 2) });
    death.Id = 2;
    death.Type = EffectType::Triggered;
    death.Trigger = TriggerCondition::OnDestroyed;
    auto killSelf = Effect({ Action(EffectOperation::Destroy, TargetCategory::Self) });
    state = Game({ killSelf, death });
    before = state;
    command = Activate({ { self } });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field.empty()
        && state.Players[0].Graveyard[0].Id == 1 && state.Players[1].Life == 18
        && Replays(before, command, state, result), "OnDestroyedResolvesAfterSourceLeavesField");
    state = Game({});
    state.Definitions[1].Effects = { killSelf, death };
    result = TransformCard(state, 1, 1, 2);
    before = state;
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Graveyard[0].CurrentDefinitionId == 1
        && state.Players[1].Life == 18 && Replays(before, command, state, result),
        "ManifestedDestroyedTriggerUsesFormSnapshot");

    auto first = Effect({ Action(EffectOperation::Heal, TargetCategory::FriendlyPlayer, Zone::Field, 1) });
    first.Type = EffectType::Triggered;
    first.Trigger = TriggerCondition::OnSummoned;
    first.Id = 7;
    auto second = first;
    second.Id = 3;
    second.Actions[0].Amount = 2;
    state = Game({ first, second }, false);
    result = ProcessCommand(state, PlayCardCommand{ 1, 1 });
    std::vector<int> activationOrder;
    for (GameEvent const& event : result.Events)
        if (event.Type == GameEventType::EffectActivated) activationOrder.push_back(event.EffectId);
    check(result.Code == CommandError::Success && state.Players[0].Life == 23
        && activationOrder == std::vector<int>{ 7, 3 }, "TriggerDefinitionRegistrationOrder");
    auto automatic = first;
    automatic.Actions = { Action(EffectOperation::DealDamage, TargetCategory::EnemyMonster) };
    state = Game({ automatic }, false);
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    Move(state, 2, 7, Zone::Deck, Zone::Field);
    result = ProcessCommand(state, PlayCardCommand{ 1, 1 });
    check(result.Code == CommandError::Success && state.Players[1].Field[0].Hp == 3
        && state.Players[1].Field[1].Hp == 4, "AutomaticTargetUsesStableFieldOrder");

    auto damageSelf = Effect({ Action(EffectOperation::DealDamage, TargetCategory::Self) });
    auto onDamage = Effect({ Action(EffectOperation::Heal, TargetCategory::Self) });
    onDamage.Id = 2;
    onDamage.Type = EffectType::Triggered;
    onDamage.Trigger = TriggerCondition::OnDamaged;
    state = Game({ damageSelf, onDamage });
    command = Activate({ { self } });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field[0].Hp == 5
        && result.Events.back().EffectId == 2, "OnDamagedTriggerHealing");
    damageSelf.Actions[0].Amount = 0;
    state = Game({ damageSelf, onDamage });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && result.Events.back().EffectId == 1,
        "ZeroDamageDoesNotTriggerOnDamaged");
    damageSelf.Actions[0].Amount = 1;
    onDamage.Actions = { Action(EffectOperation::DealDamage, TargetCategory::Self),
        Action(EffectOperation::Heal, TargetCategory::Self) };
    state = Game({ damageSelf, onDamage });
    state.Config.TriggerResolutionLimit = 3;
    rejected(state, command, CommandError::TriggerLimitReached, "TriggerCycleBoundAndWholeCommandRollback");
    auto fizzle = death;
    fizzle.Actions = { Action(EffectOperation::Heal, TargetCategory::Self) };
    state = Game({ killSelf, fizzle });
    result = ProcessCommand(state, command);
    check(result.Code == CommandError::Success && state.Players[0].Field.empty()
        && state.Players[0].Graveyard.size() == 1
        && result.Events.back().Type == GameEventType::EffectResolved
        && result.Events.back().EffectOutcome == static_cast<int>(CommandError::InvalidTargetCount),
        "TriggerWithNoTargetFizzlesWithoutUndoingDestruction");

    auto turnStart = first;
    turnStart.Trigger = TriggerCondition::OnTurnStarted;
    auto turnEnd = second;
    turnEnd.Trigger = TriggerCondition::OnTurnEnded;
    state = Game({ turnStart, turnEnd });
    std::vector<GameCommand> const turnCommands{
        AdvancePhaseCommand{ 1 }, AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 },
        StartTurnCommand{ 2 }, AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 },
        StartTurnCommand{ 1 }
    };
    before = state;
    auto const turns = ReplayCommands(state, turnCommands);
    check(turns.FinalState.Players[0].Life == 23 && turns.FinalState.Players[0].TurnsStarted == 2
        && turns.Results[2].Events.back().EffectId == 3 && turns.Results.back().Events.back().EffectId == 7
        && turns == ReplayCommands(before, turnCommands), "OwnerTurnStartEndTriggersAndReplay");

    auto triggerDraw = onSummoned;
    triggerDraw.Actions[0].Amount = 99;
    state = Game({ triggerDraw }, false);
    before = state;
    result = ProcessCommand(state, PlayCardCommand{ 1, 1 });
    check(result.Code == CommandError::Success && state.EndReason == GameEndReason::DeckExhausted
        && state.Players[0].NormalSummonsThisTurn == 1 && state.Players[0].Field.size() == 1
        && state.Players[0].Hand.size() == 4 && result.Events.back().Type == GameEventType::GameFinished
        && Replays(before, PlayCardCommand{ 1, 1 }, state, result), "SummonTriggerDeckLossCommitsSummon");

    auto chain = Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer),
        Action(EffectOperation::Mill, TargetCategory::FriendlyPlayer),
        Action(EffectOperation::Salvage, TargetCategory::FriendlyGraveyard, Zone::Graveyard),
        Action(EffectOperation::SummonFromDeck, TargetCategory::FriendlyDeck, Zone::Deck),
        Action(EffectOperation::DealDamage, TargetCategory::EnemyMonster, Zone::Field, 4) });
    state = Game({ chain });
    auto unitSummon = first;
    unitSummon.Id = 2;
    auto unitDeath = onSummoned;
    unitDeath.Trigger = TriggerCondition::OnDestroyed;
    state.Definitions[3].Effects = { unitSummon, unitDeath };
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    before = state;
    command = Activate({ { player }, { player }, { EffectTarget{ 1, 3, Zone::Graveyard } },
        { EffectTarget{ 1, 4, Zone::Deck } }, { enemy } });
    result = ProcessCommand(state, command);
    activationOrder.clear();
    for (GameEvent const& event : result.Events)
        if (event.Type == GameEventType::EffectActivated) activationOrder.push_back(event.EffectId);
    check(result.Code == CommandError::Success && state.Players[0].Hand.size() == 2
        && state.Players[0].Field.size() == 2 && state.Players[0].Life == 21
        && state.Players[1].Graveyard.size() == 1 && state.Players[1].Hand[0].Id == 7
        && activationOrder == std::vector<int>{ 1, 2, 1 }
        && Replays(before, command, state, result), "IntegratedEffectChainSummonDestroyTriggersAndReplay");

    // Invalid server card data is rejected at initialization, with output unchanged.
    state = Game({});
    auto definitions = state.Definitions;
    auto invalidEffect = Effect({ Action(EffectOperation::Draw, TargetCategory::FriendlyPlayer) });
    invalidEffect.Actions[0].Amount = -1;
    definitions[0].Effects = { invalidEffect };
    before = state;
    auto const invalidInit = InitializeGame(state, state.Config, definitions,
        { PlayerSetup{ 1, { 1 } }, PlayerSetup{ 2, { 4 } } }, 1);
    check(invalidInit == InitializationResult::InvalidDefinitions && state == before,
        "InvalidEffectDefinitionRejectedAtomically");

    // Full command sequence: free summon, sickness, effects, manifestation, destruction trigger, direct victory.
    auto summonDraw = onSummoned;
    summonDraw.Id = 2;
    auto damageEnemy = Effect({ Action(EffectOperation::DealDamage, TargetCategory::EnemyMonster, Zone::Field, 2) });
    state = Game({ damageEnemy, summonDraw }, false, 6);
    auto graveDraw = onSummoned;
    graveDraw.Trigger = TriggerCondition::OnDestroyed;
    state.Definitions[3].Effects = { graveDraw };
    before = state;
    std::vector<GameCommand> fullCommands;
    std::vector<CommandResult> fullResults;
    std::vector<GameState> snapshots;
    auto record = [&](GameCommand input)
    {
        fullCommands.push_back(std::move(input));
        fullResults.push_back(ProcessCommand(state, fullCommands.back()));
        snapshots.push_back(state);
        return fullResults.back();
    };
    result = record(PlayCardCommand{ 1, 1 });
    check(result.Code == CommandError::Success && state.Players[0].Hand[0].Id == 2,
        "FullRulesNormalSummonTriggersDraw");
    result = record(PlayCardCommand{ 1, 2 });
    check(result.Code == CommandError::NormalSummonLimit && result.Events.empty(), "FullRulesSecondNormalSummonDenied");
    result = record(AdvancePhaseCommand{ 1 });
    result = record(DirectAttackCommand{ 1, 1, 2 });
    check(result.Result == BattleResult::SummoningSickness && result.Events.empty(), "FullRulesSicknessDeniesFirstAttack");
    for (GameCommand const& input : std::vector<GameCommand>{
        AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 }, StartTurnCommand{ 2 }, PlayCardCommand{ 2, 6 },
        AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 }, StartTurnCommand{ 1 } })
        if (record(input).Code != CommandError::Success) throw std::runtime_error("full rules turn progression failed");
    result = record(Activate({ { enemy } }));
    check(result.Code == CommandError::Success && state.Players[1].Field[0].Hp == 2,
        "FullRulesActivatedDamage");
    result = record(TransformCommand{ 1, 1, 2 });
    check(result.Code == CommandError::Success && !IsSummoningSick(state, state.Players[0].Field[0])
        && state.Players[0].Field[0].CurrentDefinitionId == 2, "FullRulesMatureManifestation");
    result = record(AdvancePhaseCommand{ 1 });
    result = record(AttackCommand{ 1, 1, 2, 6 });
    check(result.Code == CommandError::Success && state.Players[0].Field[0].Hp == 5
        && state.Players[1].Graveyard[0].Id == 6 && state.Players[1].Hand[0].Id == 7,
        "FullRulesMonsterBattleAndDestructionDrawTrigger");
    for (GameCommand const& input : std::vector<GameCommand>{
        AdvancePhaseCommand{ 1 }, EndTurnCommand{ 1 }, StartTurnCommand{ 2 },
        AdvancePhaseCommand{ 2 }, AdvancePhaseCommand{ 2 }, EndTurnCommand{ 2 }, StartTurnCommand{ 1 },
        AdvancePhaseCommand{ 1 }, DirectAttackCommand{ 1, 1, 2 } })
        if (record(input).Code != CommandError::Success) throw std::runtime_error("full rules victory progression failed");
    check(state.Status == GameStatus::Finished && state.WinnerPlayerId == 1 && state.Players[1].Life == 0,
        "FullRulesDirectAttackVictory");
    auto const fullReplay = ReplayCommands(before, fullCommands);
    bool prefixesMatch = true;
    for (std::size_t length = 1; length <= fullCommands.size(); ++length)
    {
        auto const prefix = ReplayCommands(before,
            std::vector<GameCommand>(fullCommands.begin(), fullCommands.begin() + length));
        prefixesMatch = prefixesMatch && prefix.FinalState == snapshots[length - 1]
            && prefix.Results == std::vector<CommandResult>(fullResults.begin(), fullResults.begin() + length);
    }
    check(fullReplay.FinalState == state && fullReplay.Results == fullResults && prefixesMatch,
        "FullRulesReplayEveryStateAndEventPrefix");

    // All target vocabularies use the same validator, including private-owner and public-graveyard selection.
    state = Game({});
    Move(state, 1, 2, Zone::Deck, Zone::Hand);
    Move(state, 1, 3, Zone::Deck, Zone::Graveyard);
    Move(state, 2, 6, Zone::Hand, Zone::Field);
    Move(state, 2, 7, Zone::Deck, Zone::Graveyard);
    struct TargetCase { TargetCategory Category; EffectTarget Target; };
    for (TargetCase const& test : {
        TargetCase{ TargetCategory::Self, self },
        TargetCase{ TargetCategory::FriendlyMonster, self },
        TargetCase{ TargetCategory::EnemyMonster, enemy },
        TargetCase{ TargetCategory::AnyMonster, enemy },
        TargetCase{ TargetCategory::FriendlyPlayer, player },
        TargetCase{ TargetCategory::EnemyPlayer, enemyPlayer },
        TargetCase{ TargetCategory::FriendlyDeck, { 1, 4, Zone::Deck } },
        TargetCase{ TargetCategory::FriendlyHand, { 1, 2, Zone::Hand } },
        TargetCase{ TargetCategory::FriendlyGraveyard, { 1, 3, Zone::Graveyard } },
        TargetCase{ TargetCategory::EnemyGraveyard, { 2, 7, Zone::Graveyard } } })
    {
        TargetRule rule{ .Category = test.Category, .Zones = { test.Target.Location } };
        check(ValidateTargets(state, 1, 1, rule, { test.Target }) == CommandError::Success,
            "TargetCategorySupported");
    }
    state.Players[0].Field[0].Hp = 6;
    rejected(state, Activate({ { self } }), CommandError::InvalidState, "InvalidRuntimeHpStateRejected");
}
