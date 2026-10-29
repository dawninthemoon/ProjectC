#include "ProjectC/PlayerState.hpp"

namespace ProjectC::GameCore
{
    CardInstance* FindCard(PlayerState& player, CardInstanceId cardId)
    {
        for (CardInstance& card : player.Field)
        {
            if (card.Id == cardId)
            {
                return &card;
            }
        }
        return nullptr;
    }

    CardInstance const* FindCard(PlayerState const& player, CardInstanceId cardId)
    {
        for (CardInstance const& card : player.Field)
        {
            if (card.Id == cardId)
            {
                return &card;
            }
        }
        return nullptr;
    }
}
