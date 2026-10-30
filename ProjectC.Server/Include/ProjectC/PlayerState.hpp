#pragma once

#include "ProjectC/CardInstance.hpp"

#include <vector>

namespace ProjectC::GameCore
{
    struct PlayerState
    {
        PlayerId Id = 0;
        int Life = 0;
        std::vector<CardInstance> Field;
        std::vector<CardInstance> Deck;
        std::vector<CardInstance> Hand;
        std::vector<CardInstance> Graveyard;
        std::uint64_t TurnsStarted = 0;
        int NormalSummonsThisTurn = 0;

        bool operator==(PlayerState const&) const = default;
    };

    [[nodiscard]] CardInstance* FindCard(PlayerState& player, CardInstanceId cardId);
    [[nodiscard]] CardInstance const* FindCard(PlayerState const& player, CardInstanceId cardId);
}
