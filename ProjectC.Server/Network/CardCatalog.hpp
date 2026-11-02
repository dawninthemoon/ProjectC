#pragma once
#include "ProjectC/GameInitializer.hpp"
#include "Protocol.hpp"
#include <array>
#include <cstddef>
namespace ProjectC::Network
{
inline constexpr std::size_t PersonalDeckCardCount = 30;
inline constexpr int MaximumCopiesPerPersonalDeckCard = 3;

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
    std::string Version;
    GameCore::GameConfig Config;
    std::vector<GameCore::CardDefinition> Definitions;
    std::vector<DeckDefinition> Decks;
    DeckDefinition const* FindDeck(std::string const& id) const;
    bool ValidateDeck(std::vector<GameCore::CardDefinitionId> const& cards) const;
    std::string PersonalDeckError(std::vector<GameCore::CardDefinitionId> const& cards) const;
    Json PublicDecks() const;
    GameCore::GameState
    InitializeCards(std::array<int, 2> players,
                    std::array<std::vector<GameCore::CardDefinitionId>, 2> const& decks,
                    std::uint64_t seed,
                    int firstCardId) const;
    // Explicit Fisher-Yates with rejection sampling: deterministic on MSVC and GCC.
    GameCore::GameState Initialize(std::array<int, 2> players,
                                   std::array<std::string, 2> const& decks,
                                   std::uint64_t seed,
                                   int firstCardId) const;
};
std::string DefaultDataDirectory();
} // namespace ProjectC::Network
