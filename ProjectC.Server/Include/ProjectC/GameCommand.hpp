#pragma once

#include "ProjectC/CardInstance.hpp"
#include "ProjectC/GameEvent.hpp"
#include "ProjectC/GameState.hpp"

#include <vector>

namespace ProjectC::GameCore
{
    struct AttackCommand
    {
        PlayerId AttackerPlayerId = 0;
        CardInstanceId AttackerCardId = 0;
        PlayerId DefenderPlayerId = 0;
        CardInstanceId DefenderCardId = 0;
    };

    struct CommandResult
    {
        BattleResult Result = BattleResult::Success;
        std::vector<GameEvent> Events;
    };

    // Failed validation leaves the state unchanged and returns no events.
    [[nodiscard]] CommandResult ProcessCommand(
        GameState& gameState,
        AttackCommand const& command);
}
