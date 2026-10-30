#pragma once

#include "ProjectC/PlayerState.hpp"

namespace ProjectC::GameCore
{
    struct GameState;
    enum class ZoneResult
    {
        Success, InvalidState, PlayerNotFound, CardNotFound, SameZone,
        InvalidZone, DestinationFull, DestroyedCard, GameFinished
    };

    [[nodiscard]] std::vector<CardInstance>* GetZone(PlayerState& player, Zone zone);
    [[nodiscard]] std::vector<CardInstance> const* GetZone(PlayerState const& player, Zone zone);
    [[nodiscard]] bool ValidateCardIntegrity(GameState const& gameState);
    [[nodiscard]] ZoneResult MoveCard(GameState& gameState, PlayerId playerId,
        CardInstanceId cardId, Zone from, Zone to);
}
