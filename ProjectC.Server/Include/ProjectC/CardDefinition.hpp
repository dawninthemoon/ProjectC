#pragma once

#include "ProjectC/EffectDefinition.hpp"

#include <optional>
#include <string>

namespace ProjectC::GameCore
{
struct DescriptionArgument
{
    std::string Path;
    std::optional<std::string> Default;
    bool operator==(DescriptionArgument const&) const = default;
};

// Immutable printed data. Runtime damage and modifiers belong to CardInstance.
struct CardDefinition
{
    CardDefinitionId Id = 0;
    std::string Name;
    CardType Type = CardType::Monster;
    int Attack = 0;
    int MaxHp = 1;
    std::vector<std::string> Traits;
    std::vector<CardDefinitionId> AllowedManifestations;
    std::vector<EffectDefinition> Effects;
    std::vector<std::string> Archetypes;
    // Yakou prototype metadata. Read by the catalog; mechanics are not yet enabled.
    int AttackInterval = 0;
    std::vector<std::string> SummonMethods;
    std::string SummonCostJson;
    bool IsManifestation = false;
    std::string Rarity;
    std::string ArtKey;
    bool MainDeckEligible = true;
    // Presentation keys never participate in command/effect validation.
    // Name remains for legacy catalogs and unchanged wire/replay compatibility.
    std::string NameKey;
    std::string DescriptionKey;
    std::vector<DescriptionArgument> DescriptionArgs;

    bool operator==(CardDefinition const&) const = default;
};
} // namespace ProjectC::GameCore
