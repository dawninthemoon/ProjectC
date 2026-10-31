#pragma once

#include "ProjectC/CardInstance.hpp"
#include "ProjectC/Zone.hpp"
#include "ProjectC/GameState.hpp"

namespace ProjectC::GameCore
{
    enum class GameEventType
    {
        BattleStarted,
        CardDamaged,
        CardDestroyed,
        BattleCompleted,
        CardMoved,
        TurnStarted,
        TurnEnded,
        PhaseChanged,
        LifeChanged,
        GameEnded,
        GameFinished = GameEnded,
        PlayerDamaged,
        CardSummoned,
        CardDrawn,
        EffectActivated,
        EffectResolved,
        CardTransformed,
        CardHealed,
        StatsModified, YokaiPowerChanged, SpellActivated
    };

    struct GameEvent
    {
        GameEventType Type = GameEventType::BattleStarted;
        PlayerId SourcePlayerId = 0;
        CardInstanceId SourceCardId = 0;
        PlayerId TargetPlayerId = 0;
        CardInstanceId TargetCardId = 0;
        // Nonnegative incoming damage, including damage beyond the remaining HP.
        int Damage = 0;
        int HpAfter = 0;
        Zone FromZone = Zone::Deck;
        Zone ToZone = Zone::Deck;
        CardDefinitionId OriginalDefinitionId = 0;
        CardDefinitionId CurrentDefinitionId = 0;
        int LifeBefore = 0;
        int LifeAfter = 0;
        int TurnNumber = 0;
        TurnPhase Phase = TurnPhase::Main;
        PlayerId WinnerPlayerId = 0;
        GameEndReason EndReason = GameEndReason::None;
        int EffectId = 0;
        int AttackAfter = 0;
        int MaxHpAfter = 0;
        int EffectOutcome = 0; // CommandError numeric value; nonzero means an automatic effect fizzled.
        int SummonOrigin = 0; // 0 normal, 1 deck, 2 hand, 3 graveyard.
        int YokaiPowerAfter = 0;

        bool operator==(GameEvent const&) const = default;
    };
}
