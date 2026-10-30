#pragma once

#include "ProjectC/CardInstance.hpp"

namespace ProjectC::GameCore
{
    enum class GameEventType
    {
        BattleStarted,
        CardDamaged,
        CardDestroyed,
        BattleCompleted
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
    };
}
