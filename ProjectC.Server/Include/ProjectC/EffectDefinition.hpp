#pragma once

#include "ProjectC/GameTypes.hpp"

#include <cstddef>
#include <string>
#include <vector>

namespace ProjectC::GameCore
{
    enum class EffectType { Activated, Triggered, Continuous, Replacement };
    enum class TriggerCondition { None, OnSummoned, OnDestroyed, OnDamaged, OnTurnStarted, OnTurnEnded };
    enum class EffectOperation
    {
        Draw, Mill, Salvage, Search, SummonFromDeck, SummonFromGraveyard,
        ReturnToHand, Destroy, DealDamage, Heal, ModifyStats, Transform
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
        bool operator==(EffectAction const&) const = default;
    };

    struct EffectDefinition
    {
        int Id = 0; // Unique within this card definition.
        EffectType Type = EffectType::Activated;
        TriggerCondition Trigger = TriggerCondition::None;
        std::vector<EffectAction> Actions;
        bool operator==(EffectDefinition const&) const = default;
    };
}
