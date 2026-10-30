#pragma once
#include "Protocol.hpp"
#include "ProjectC/GameInitializer.hpp"
#include <array>
namespace ProjectC::Network
{
struct DeckDefinition
{
    std::string Id;
    std::string Name;
    std::vector<GameCore::CardDefinitionId> Cards;
};
class CardCatalog
{
public:
    static CardCatalog Load(std::string const& directory);
    static CardCatalog FromJson(Json const& cards, Json const& decks);
    GameCore::GameConfig Config;
    std::vector<GameCore::CardDefinition> Definitions;
    std::vector<DeckDefinition> Decks;
    DeckDefinition const* FindDeck(std::string const& id) const;
    bool ValidateDeck(std::vector<GameCore::CardDefinitionId> const& cards) const;
    Json PublicDecks() const;
    // Explicit Fisher-Yates with rejection sampling: deterministic on MSVC and GCC.
    GameCore::GameState Initialize(std::array<int, 2> players, std::array<std::string, 2> const& decks, std::uint64_t seed, int firstCardId) const;
};
std::string DefaultDataDirectory();
}
