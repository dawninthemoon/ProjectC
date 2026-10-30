#pragma once

#include "ProjectC/GameCommand.hpp"

namespace ProjectC::GameCore
{
    struct ReplayResult
    {
        GameState FinalState;
        std::vector<CommandResult> Results;

        bool operator==(ReplayResult const&) const = default;
    };

    // Includes rejected commands and continues in the supplied order.
    [[nodiscard]] ReplayResult ReplayCommands(GameState const& initialState,
        std::vector<GameCommand> const& commands);
}
