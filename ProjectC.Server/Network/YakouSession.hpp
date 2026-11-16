#pragma once
#include "YakouCommandGateway.hpp"

#include <array>
#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace ProjectC::Network
{
// Server-only opt-in session; not registered in the legacy MatchManager.
// The host authenticates its transport and calls Bind with a trusted seat.
// Connection keys are internal handles, never authorization tokens.
class YakouSession
{
public:
    YakouSession(YakouPrototype const& catalog, int firstPlayer,
                 std::array<std::vector<int>, 2> const& deckDefinitions);

    void Bind(int trustedSeat, std::string const& connectionKey);
    void Unbind(std::string const& connectionKey);
    [[nodiscard]] Json Dispatch(std::string const& connectionKey,
                                std::string const& requestId, Json const& command);
    [[nodiscard]] Json Snapshot(std::string const& connectionKey) const;

    [[nodiscard]] YakouMatch const& Match() const { return gateway_.Match(); }
    [[nodiscard]] std::uint64_t Sequence() const { return sequence_; }

    // Private audit metadata; command contents and hashes must not be sent
    // to the opponent or included in the public Snapshot/Dispatch response.
    struct Attempt
    {
        int Seat = 0;
        std::string RequestId;
        bool Accepted = false;
        std::uint64_t BeforeHash = 0;
        std::uint64_t AfterHash = 0;
    };
    [[nodiscard]] std::vector<Attempt> const& Audit() const { return audit_; }

private:
    struct CachedRequest
    {
        Json Command;
        Json Response;
    };
    struct Seat
    {
        std::string ConnectionKey;
        std::map<std::string, CachedRequest> Requests;
    };
    [[nodiscard]] int SeatFor(std::string const& connectionKey) const;

    YakouCommandGateway gateway_;
    std::array<Seat, 2> seats_;
    std::uint64_t sequence_ = 0;
    std::vector<Attempt> audit_;
};
} // namespace ProjectC::Network
