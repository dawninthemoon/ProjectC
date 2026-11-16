#pragma once
#include "YakouMatch.hpp"

#include <array>
#include <vector>

namespace ProjectC::Network
{
// Server-owned command boundary for future Yakou transport routing. The caller
// supplies the authenticated seat, never a client-authored player field.
// This is intentionally separate from the production GameCore MatchSession.
class YakouCommandGateway
{
public:
    YakouCommandGateway(YakouPrototype const& catalog, int firstPlayer,
                        std::array<std::vector<int>, 2> const& deckDefinitions);

    // A rejected command never mutates the authoritative match. The returned
    // snapshot is always projected for the authenticated seat only.
    [[nodiscard]] Json Execute(int authenticatedPlayer, Json const& command);
    [[nodiscard]] Json Snapshot(int authenticatedPlayer) const;
    [[nodiscard]] YakouMatch const& Match() const { return match_; }

private:
    YakouMatch match_;
};
} // namespace ProjectC::Network
