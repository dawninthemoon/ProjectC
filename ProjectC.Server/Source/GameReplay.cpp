#include "ProjectC/GameReplay.hpp"

namespace ProjectC::GameCore
{
    CommandResult ProcessCommand(GameState& gameState, GameCommand const& command)
    {
        return std::visit([&](auto const& input) { return ProcessCommand(gameState, input); }, command);
    }

    ReplayResult ReplayCommands(GameState const& initialState, std::vector<GameCommand> const& commands)
    {
        ReplayResult replay{ .FinalState = initialState };
        replay.Results.reserve(commands.size());
        for (GameCommand const& command : commands)
            replay.Results.push_back(ProcessCommand(replay.FinalState, command));
        return replay;
    }
}
