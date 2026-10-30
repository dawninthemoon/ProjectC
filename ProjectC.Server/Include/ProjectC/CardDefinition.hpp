#pragma once

#include <string>

namespace ProjectC::GameCore
{
    using CardDefinitionId = int;

    enum class CardType { Monster };

    // Immutable printed data. Runtime damage and modifiers belong to CardInstance.
    struct CardDefinition
    {
        CardDefinitionId Id = 0;
        std::string Name;
        CardType Type = CardType::Monster;
        int Attack = 0;
        int MaxHp = 1;

        bool operator==(CardDefinition const&) const = default;
    };
}
