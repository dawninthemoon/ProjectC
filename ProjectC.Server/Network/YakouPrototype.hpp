#pragma once
#include "Protocol.hpp"
#include <filesystem>
#include <string>
#include <vector>

namespace ProjectC::Network
{
// Authoring dialect deliberately isolated from CardCatalog's legacy runtime dialect.
// No unknown actions may be silently converted to no-ops.
struct YakouPrototype
{
    Json Cards;
    Json Decks;
    Json Localize;
    Json Config;
    std::vector<std::string> UnsupportedRuntimeEffects;
    // False until the new effect interpreter and turn state machine are wired.
    bool RuntimeReady = false;

    static YakouPrototype Load(std::filesystem::path const& directory);
    static YakouPrototype Validate(Json const& cards,
                                   Json const& decks,
                                   Json const& localize,
                                   Json const& config);
};

// Reusable server-side quantity/ownership check for the compact selection UI.
// The required count comes from the *card's* cost, never from a global default.
// Candidates must already be server-authorized for the requested zone/owner.
[[nodiscard]] bool ValidateYakouExactSelection(std::vector<int> const& candidates,
                                                std::vector<int> const& selected,
                                                std::size_t requiredCount);
} // namespace ProjectC::Network
