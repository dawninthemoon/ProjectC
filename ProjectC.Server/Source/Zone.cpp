#include "ProjectC/Zone.hpp"
#include "ProjectC/GameState.hpp"
#include "GameOperations.hpp"
#include "ProjectC/CardEffects.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <algorithm>
#include <set>

namespace ProjectC::GameCore
{
    std::vector<CardInstance>* GetZone(PlayerState& player, Zone zone)
    {
        switch (zone)
        {
        case Zone::Deck: return &player.Deck;
        case Zone::Hand: return &player.Hand;
        case Zone::Field: return &player.Field;
        case Zone::Graveyard: return &player.Graveyard;
        case Zone::Exile: return &player.Exile;
        case Zone::HiddenExile: return &player.HiddenExile;
        }
        return nullptr;
    }

    std::vector<CardInstance> const* GetZone(PlayerState const& player, Zone zone)
    {
        switch (zone)
        {
        case Zone::Deck: return &player.Deck;
        case Zone::Hand: return &player.Hand;
        case Zone::Field: return &player.Field;
        case Zone::Graveyard: return &player.Graveyard;
        case Zone::Exile: return &player.Exile;
        case Zone::HiddenExile: return &player.HiddenExile;
        }
        return nullptr;
    }

    bool ValidateCardIntegrity(GameState const& gameState)
    {
        std::set<CardDefinitionId> definitions;
        for (CardDefinition const& definition : gameState.Definitions)
        {
            if (definition.Id <= 0 || definition.Attack < 0 || definition.MaxHp <= 0
                || (definition.Type != CardType::Monster && definition.Type != CardType::Spell) || !definitions.insert(definition.Id).second)
            {
                return false;
            }
        }
        if (!ValidateEffectDefinitions(gameState)) return false;
        std::set<PlayerId> players;
        std::set<CardInstanceId> cards;
        for (PlayerState const& player : gameState.Players)
        {
            if (player.Id <= 0 || !players.insert(player.Id).second)
            {
                return false;
            }
            for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard, Zone::Exile, Zone::HiddenExile })
            {
                for (CardInstance const& card : *GetZone(player, zone))
                {
                    if (card.Id <= 0 || card.OwnerId != player.Id || !cards.insert(card.Id).second)
                    {
                        return false;
                    }
                    // Definition-less instances are supported only for legacy battle fixtures.
                    bool const legacy = gameState.Definitions.empty()
                        && card.OriginalDefinitionId == 0 && card.CurrentDefinitionId == 0;
                    if (!legacy && (!definitions.contains(card.OriginalDefinitionId)
                        || !definitions.contains(card.CurrentDefinitionId)))
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    ZoneResult MoveCard(GameState& gameState, PlayerId playerId,
        CardInstanceId cardId, Zone from, Zone to)
    {
        if (gameState.Status == GameStatus::Finished) return ZoneResult::GameFinished;
        if (!ValidateCardIntegrity(gameState)) return ZoneResult::InvalidState;
        PlayerState* player = FindPlayer(gameState, playerId);
        if (player == nullptr) return ZoneResult::PlayerNotFound;
        auto* source = GetZone(*player, from);
        auto* destination = GetZone(*player, to);
        if (source == nullptr || destination == nullptr) return ZoneResult::InvalidZone;
        if (from == to) return ZoneResult::SameZone;
        auto const card = std::find_if(source->begin(), source->end(),
            [cardId](CardInstance const& candidate) { return candidate.Id == cardId; });
        if (card == source->end()) return ZoneResult::CardNotFound;
        if ((to == Zone::Field && destination->size() >= gameState.Config.FieldLimit)
            || (to == Zone::Hand && destination->size() >= gameState.Config.HandLimit))
        {
            return ZoneResult::DestinationFull;
        }
        if (to == Zone::Field && !gameState.Definitions.empty())
        {
            auto definition=FindDefinition(gameState,card->CurrentDefinitionId);
            if(!definition || definition->Type!=CardType::Monster) return ZoneResult::InvalidZone;
        }
        if (to == Zone::Field && card->IsDestroyed()) return ZoneResult::DestroyedCard;
        CardInstance moved = *card;
        if (to == Zone::Field) Detail::MarkFieldEntry(gameState, *player, moved);
        if (to == Zone::Graveyard)
        {
            moved.CurrentDefinitionId = moved.OriginalDefinitionId;
            if(auto definition=FindDefinition(gameState,moved.OriginalDefinitionId))
                for(auto const& archetype:definition->Archetypes)
                    if(std::find(player->GraveyardArchetypesThisTurn.begin(),player->GraveyardArchetypesThisTurn.end(),archetype)==player->GraveyardArchetypesThisTurn.end())
                        player->GraveyardArchetypesThisTurn.push_back(archetype);
        }
        destination->push_back(moved);
        source->erase(card);
        return ZoneResult::Success;
    }
}
