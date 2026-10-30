#pragma once

#include "ProjectC/CardDefinition.hpp"

namespace ProjectC::GameCore
{
    using PlayerId = int;
    using CardInstanceId = int;

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

        bool operator==(CardInstance const&) const = default;

        [[nodiscard]] bool IsDestroyed() const;
    };

    void ApplyDamage(CardInstance& target, int damage);
}
