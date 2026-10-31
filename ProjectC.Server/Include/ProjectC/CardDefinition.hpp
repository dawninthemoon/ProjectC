#pragma once

#include "ProjectC/EffectDefinition.hpp"

#include <string>

namespace ProjectC::GameCore
{
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
        std::string Rarity;
        std::string ArtKey;
        bool MainDeckEligible = true;

        bool operator==(CardDefinition const&) const = default;
    };
}
