#pragma once

#include <cstddef>

namespace ProjectC::GameCore
{
    // Provisional defaults, not final game rules. Deck order is always supplied.
    struct GameConfig
    {
        int InitialLife = 20;
        std::size_t InitialHandSize = 0;
        std::size_t FieldLimit = 5;
        std::size_t HandLimit = 10;
        std::size_t DrawPerTurn = 0;
        bool AllowManualDraw = true;
        bool AllowRemanifestation = false;
        std::size_t TriggerResolutionLimit = 64;

        // Provisional deck-building constraints; enforced by the server data catalog.
        std::size_t MinDeckSize = 1;
        std::size_t MaxDeckSize = 128;
        std::size_t MaxCardCopies = 4;

        bool operator==(GameConfig const&) const = default;
    };
}
