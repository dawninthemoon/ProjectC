#pragma once
#include "Protocol.hpp"
#include <filesystem>
#include <map>
#include <set>
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

// Card-authored exact cost requirement; never derive Devour N from the global default.
struct YakouResourceDefinition
{
    int Initial = 0;
    int Maximum = 0;
    int RegenPerOwnTurn = 0;
};

using YakouResourceDefinitions = std::map<std::string, YakouResourceDefinition>;
using YakouResourceAmounts = std::map<std::string, int>;

// Alpha 02 uses typed Costs; legacy Alpha 01 remains readable.
struct YakouTypedCost
{
    std::string OrdinaryType;
    std::size_t OrdinaryCount = 0;
    YakouResourceAmounts Resources;
};
[[nodiscard]] YakouTypedCost YakouParseTypedCost(Json const& costs);
[[nodiscard]] YakouResourceDefinitions YakouReadResourceDefinitions(Json const& config);
[[nodiscard]] std::set<std::string> YakouDeckResourceTypes(
    Json const& cards, Json const& config, std::vector<int> const& deck);

struct YakouCostRequirement
{
    std::string SourceZone;
    std::size_t ExactCount = 0;
    YakouResourceAmounts Resources;
};
[[nodiscard]] YakouCostRequirement YakouRequiredCost(Json const& card, std::string const& method);

// Reusable server-side quantity/ownership check for the compact selection UI.
// The required count comes from the *card's* cost, never from a global default.
// Candidates must already be server-authorized for the requested zone/owner.
[[nodiscard]] bool ValidateYakouExactSelection(std::vector<int> const& candidates,
                                                std::vector<int> const& selected,
                                                std::size_t requiredCount);
} // namespace ProjectC::Network
