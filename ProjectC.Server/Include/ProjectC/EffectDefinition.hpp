#pragma once

#include "ProjectC/GameTypes.hpp"

#include <cstddef>
#include <string>
#include <vector>

namespace ProjectC::GameCore
{
    enum class EffectType { Activated, Triggered, Continuous, Replacement };
    enum class TriggerCondition { None, OnSummoned, OnDestroyed, OnDamaged, OnTurnStarted, OnTurnEnded,
        OnNormalSummoned, OnRevived, OnManifested };
    enum class EffectOperation
    {
        Draw, Mill, Salvage, Search, SummonFromDeck, SummonFromGraveyard,
        ReturnToHand, Destroy, DealDamage, Heal, ModifyStats, Transform,
        Discard, SendToGraveyard, SummonFromHand, Exile, GainYokaiPower
    };
    enum class TargetCategory
    {
        Self, FriendlyMonster, EnemyMonster, AnyMonster, FriendlyPlayer, EnemyPlayer,
        FriendlyDeck, FriendlyHand, FriendlyGraveyard, EnemyGraveyard
    };

    struct TargetRule
    {
        TargetCategory Category = TargetCategory::Self;
        std::vector<Zone> Zones{ Zone::Field };
        CardType Type = CardType::Monster;
        std::string RequiredTrait;
        std::size_t MinCount = 1;
        std::size_t MaxCount = 1;
        std::string RequiredArchetype;
        int MaxAttack = -1;
        bool operator==(TargetRule const&) const = default;
    };

    struct EffectTarget
    {
        PlayerId Player = 0;
        CardInstanceId Card = 0; // Zero denotes a player target.
        Zone Location = Zone::Field;
        bool operator==(EffectTarget const&) const = default;
    };

    struct EffectAction
    {
        EffectOperation Operation = EffectOperation::Draw;
        TargetRule Targets;
        int Amount = 1;
        int AttackDelta = 0;
        int MaxHpDelta = 0;
        CardDefinitionId TransformDefinitionId = 0;
        bool AllTargets = false;
        bool TemporaryStats = false;
        bool PreventDirectAttackThisTurn = false;
        std::string YokaiGainArchetype;
        int YokaiGainAmount = 0;
        bool operator==(EffectAction const&) const = default;
    };

    enum class CostKind { YokaiPower, Life, Discard, Tribute, HiddenExileTop };
    struct CostDefinition
    {
        CostKind Kind = CostKind::YokaiPower;
        int Amount = 0;
        TargetRule Targets;
        bool operator==(CostDefinition const&) const = default;
    };
    struct EffectDefinition
    {
        int Id = 0; // Unique within this card definition.
        EffectType Type = EffectType::Activated;
        TriggerCondition Trigger = TriggerCondition::None;
        std::vector<EffectAction> Actions;
        Zone ActivationZone = Zone::Field;
        std::vector<CostDefinition> Costs;
        int PerTurnLimit = 0; // Zero is unlimited; counters are shared by definition/effect ID.
        std::string RequiredGraveyardArchetype;
        std::size_t MinGraveyardCount = 0;
        std::string RequireGraveyardEntryThisTurn;
        bool operator==(EffectDefinition const&) const = default;
    };
}
