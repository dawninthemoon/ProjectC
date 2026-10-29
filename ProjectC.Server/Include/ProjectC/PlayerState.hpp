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
    };

    [[nodiscard]] CardInstance* FindCard(PlayerState& player, CardInstanceId cardId);
    [[nodiscard]] CardInstance const* FindCard(PlayerState const& player, CardInstanceId cardId);
}
