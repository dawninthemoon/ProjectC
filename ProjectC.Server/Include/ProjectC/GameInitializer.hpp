#pragma once

#include "ProjectC/GameState.hpp"

namespace ProjectC::GameCore
{
    struct PlayerSetup
    {
        PlayerId Id = 0;
        // Index zero is the top of the deck; repeated definition IDs are legal.
        std::vector<CardDefinitionId> Deck;
    };

    enum class InitializationResult
    {
        Success, InvalidConfig, InvalidPlayers, InvalidDefinitions,
        UnknownDefinition, InsufficientCards, TooManyCards
    };

    [[nodiscard]] bool ValidateConfig(GameConfig const& config);
    [[nodiscard]] bool ValidateGameState(GameState const& gameState);
    [[nodiscard]] CardDefinition const* FindDefinition(GameState const& gameState, CardDefinitionId id);
    // On failure, output is unchanged. On success, StartTurnCommand begins the first turn.
    [[nodiscard]] InitializationResult InitializeGame(GameState& output, GameConfig const& config,
        std::vector<CardDefinition> const& definitions,
        std::array<PlayerSetup, 2> const& players, PlayerId firstPlayerId);
}
