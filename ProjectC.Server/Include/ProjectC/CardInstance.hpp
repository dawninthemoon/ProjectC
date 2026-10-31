#pragma once

#include "ProjectC/CardDefinition.hpp"
#include <cstdint>

namespace ProjectC::GameCore
{
    struct CardInstance
    {
        CardInstanceId Id = 0;
        PlayerId OwnerId = 0;
        int Attack = 0;
        int Hp = 0;
        int MaxHp = 0;
        bool HasAttackedThisTurn = false;
        CardDefinitionId OriginalDefinitionId = 0;
        CardDefinitionId CurrentDefinitionId = 0;
        int EnteredFieldTurnNumber = 0;
        PlayerId EnteredFieldActivePlayerId = 0;
        std::uint64_t OwnerTurnAtEntry = 0;
        bool IgnoreSummoningSickness = false;
        bool HasManifested = false;
        int TemporaryAttack = 0;
        int DirectAttackRestrictedTurn = 0;

        bool operator==(CardInstance const&) const = default;

        [[nodiscard]] bool IsDestroyed() const;
    };

    void ApplyDamage(CardInstance& target, int damage);
}
