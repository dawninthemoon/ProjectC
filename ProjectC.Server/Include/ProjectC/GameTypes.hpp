#pragma once

namespace ProjectC::GameCore
{
    using PlayerId = int;
    using CardInstanceId = int;
    using CardDefinitionId = int;
    enum class CardType { Monster };
    enum class Zone { Deck, Hand, Field, Graveyard };
}
