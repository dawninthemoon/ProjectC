#include "YakouSession.hpp"

#include <stdexcept>
#include <utility>

namespace ProjectC::Network
{
namespace
{
constexpr std::size_t MaximumRequestsPerSeat = 4096;
constexpr std::size_t MaximumRequestIdLength = 64;
constexpr std::size_t MaximumConnectionKeyLength = 128;
void ValidKey(std::string const& value, std::size_t maxLength)
{
    if (value.empty() || value.size() > maxLength)
        throw std::invalid_argument("Invalid server connection/request key");
    for (unsigned char c : value)
        if (c < 0x21 || c > 0x7e)
            throw std::invalid_argument("Invalid server connection/request key");
}
}

YakouSession::YakouSession(
    YakouPrototype const& catalog, int firstPlayer,
    std::array<std::vector<int>, 2> const& deckDefinitions, std::uint64_t randomSeed)
    : catalog_(&catalog), firstPlayer_(firstPlayer), initialDecks_(deckDefinitions),
      randomSeed_(randomSeed), gateway_(catalog, firstPlayer, deckDefinitions, randomSeed)
{
}

void YakouSession::Bind(int trustedSeat, std::string const& connectionKey)
{
    if (trustedSeat != 1 && trustedSeat != 2)
        throw std::invalid_argument("Invalid trusted seat");
    ValidKey(connectionKey, MaximumConnectionKeyLength);
    for (int i = 0; i < 2; ++i)
        if (seats_[i].ConnectionKey == connectionKey && i != trustedSeat - 1)
            throw std::invalid_argument("Connection is already bound to another seat");
    // Reconnecting a seat replaces the old handle. Keep its request cache
    // to prevent replaying earlier accepted actions after reconnect.
    seats_[trustedSeat - 1].ConnectionKey = connectionKey;
}

void YakouSession::Unbind(std::string const& connectionKey)
{
    for (auto& seat : seats_)
        if (seat.ConnectionKey == connectionKey)
            seat.ConnectionKey.clear();
}

int YakouSession::SeatFor(std::string const& connectionKey) const
{
    if (connectionKey.empty())
        throw std::invalid_argument("Unbound Yakou connection");
    for (int i = 0; i < 2; ++i)
        if (seats_[i].ConnectionKey == connectionKey)
            return i + 1;
    throw std::invalid_argument("Unbound Yakou connection");
}

Json YakouSession::Snapshot(std::string const& connectionKey) const
{
    return gateway_.Snapshot(SeatFor(connectionKey));
}

Json YakouSession::Dispatch(std::string const& connectionKey,
                            std::string const& requestId, Json const& command)
{
    int const player = SeatFor(connectionKey);
    ValidKey(requestId, MaximumRequestIdLength);
    auto& cache = seats_[player - 1].Requests;
    auto const existing = cache.find(requestId);
    if (existing != cache.end())
    {
        // Reused request IDs with modified contents are not valid retries.
        // Do not reveal the originally submitted command or its targets.
        if (existing->second.Command == command)
            return existing->second.Response;
        return {{"Accepted", false}, {"Reason", "DuplicateRequestId"},
                {"Sequence", sequence_},
                {"Snapshot", gateway_.Snapshot(player)}};
    }
    if (cache.size() >= MaximumRequestsPerSeat)
        return {{"Accepted", false}, {"Reason", "RequestCacheFull"},
                {"Sequence", sequence_},
                {"Snapshot", gateway_.Snapshot(player)}};

    auto const before = gateway_.Match().ServerStateHash();
    // The gateway validates command type/fields and performs the action on a
    // candidate copy, so rejected commands cannot mutate authoritative state.
    Json result = gateway_.Execute(player, command);
    bool const accepted = result.at("Accepted").get<bool>();
    auto const after = gateway_.Match().ServerStateHash();
    if (!accepted && before != after)
        throw std::logic_error("Rejected Yakou command mutated authoritative state");
    if (accepted)
        ++sequence_;
    result["Sequence"] = sequence_;
    cache.emplace(requestId, CachedRequest{command, result});
    audit_.push_back({player, requestId, accepted, before, after, command});
    return result;
}
bool YakouSession::VerifyReplay() const
{
    YakouCommandGateway replay(*catalog_,firstPlayer_,initialDecks_,randomSeed_);
    std::uint64_t accepted = 0;
    for (auto const& attempt : audit_)
    {
        if (replay.Match().ServerStateHash() != attempt.BeforeHash) return false;
        auto const result = replay.Execute(attempt.Seat,attempt.Command);
        if (result.at("Accepted").get<bool>() != attempt.Accepted ||
            replay.Match().ServerStateHash() != attempt.AfterHash) return false;
        if (attempt.Accepted) ++accepted;
    }
    return accepted == sequence_ && replay.Match().ServerStateHash() == Match().ServerStateHash();
}
} // namespace ProjectC::Network
