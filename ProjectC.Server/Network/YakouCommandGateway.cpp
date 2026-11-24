#include "YakouCommandGateway.hpp"
#include "Protocol.hpp"

#include <limits>
#include <stdexcept>
#include <string>
#include <utility>

namespace ProjectC::Network
{
namespace
{
constexpr std::uint64_t MaxInstance = static_cast<std::uint64_t>(std::numeric_limits<int>::max());
constexpr std::size_t MaxMaterials = 30;
constexpr std::size_t MaxAssignments = 5;
constexpr int AutomaticTriggerBudget = 64;

int Id(Json const& value, char const* key)
{
    return static_cast<int>(Number(value, key, MaxInstance));
}

std::vector<int> Ids(Json const& command, char const* key)
{
    auto const& array = command.at(key);
    if (!array.is_array() || array.size() > MaxMaterials)
        throw std::invalid_argument("Invalid instance list");
    std::vector<int> result;
    result.reserve(array.size());
    for (auto const& item : array)
    {
        Json single = {{"Id", item}};
        result.push_back(Id(single, "Id"));
    }
    return result;
}

std::vector<std::pair<int, int>> Blockers(Json const& command)
{
    auto const& array = command.at("Blockers");
    if (!array.is_array() || array.size() > MaxAssignments)
        throw std::invalid_argument("Invalid blocker list");
    std::vector<std::pair<int, int>> result;
    for (auto const& entry : array)
    {
        Fields(entry, {"Attacker", "Blocker"});
        result.emplace_back(Id(entry, "Attacker"), Id(entry, "Blocker"));
    }
    return result;
}

void Apply(YakouMatch& match, int seat, Json const& request)
{
    if (!request.is_object())
        throw std::invalid_argument("Command must be an object");
    std::string const type = String(request, "Type", 64);
    if (type == "StartTurn")
    {
        Fields(request, {"Type"});
        match.StartTurn(seat);
    }
    else if (type == "Summon")
    {
        Fields(request, {"Type", "CardId", "Method", "Materials", "Slot"});
        (void)match.SummonAndRunAutomaticTriggers(
            seat, Id(request, "CardId"), String(request, "Method", 32),
            Ids(request, "Materials"), Id(request, "Slot"));
    }
    else if (type == "ActivateMainSpell")
    {
        Fields(request, {"Type", "CardId", "Costs"});
        // Preserve the wire command, but never let it bypass opponent priority.
        match.BeginSpellResponse(seat, Id(request, "CardId"), 0,
                                 request.contains("Costs") ? Ids(request, "Costs") : std::vector<int>{});
    }
    else if (type == "ChooseEffect")
    {
        Fields(request, {"Type", "TargetId", "TargetIds", "Slot"});
        if (request.contains("Slot"))
        {
            if (request.contains("TargetIds") || !request.contains("TargetId"))
            {
                throw std::invalid_argument("Special summon needs one selected target");
            }
            (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(
                seat, {Id(request, "TargetId")}, Id(request, "Slot"), AutomaticTriggerBudget);
        }
        else if (request.contains("TargetIds"))
        {
            if (request.contains("TargetId"))
            {
                throw std::invalid_argument("ChooseEffect accepts one target representation");
            }
            (void)match.ResolveSelectedEffectsAndRunAutomaticTriggers(seat, Ids(request, "TargetIds"));
        }
        else
        {
            (void)match.ResolveSelectedEffectAndRunAutomaticTriggers(seat, Id(request, "TargetId"));
        }
    }
    else if (type == "Manifest")
    {
        Fields(request, {"Type", "CardId", "DefinitionId"});
        (void)match.ManifestAndRunAutomaticTriggers(
            seat, Id(request, "CardId"), Id(request, "DefinitionId"));
    }
    else if (type == "Postpone")
    {
        Fields(request, {"Type", "CardId"});
        match.Postpone(seat, Id(request, "CardId"));
    }
    else if (type == "Redraw")
    {
        Fields(request, {"Type", "Cards"});
        match.Redraw(seat, Ids(request, "Cards"));
    }
    else if (type == "BeginRaid")
    {
        Fields(request, {"Type"});
        match.BeginRaid(seat);
    }
    else if (type == "AssignBlockers")
    {
        Fields(request, {"Type", "Blockers"});
        match.AssignBlockers(seat, Blockers(request));
    }
    else if (type == "RespondWithRaidShield")
    {
        Fields(request, {"Type", "CardId", "TargetId"});
        match.RespondWithRaidShield(seat, Id(request, "CardId"), Id(request, "TargetId"));
    }
    else if (type == "RespondWithRaidTactic")
    {
        Fields(request, {"Type", "CardId", "TargetId", "ReplacementBlocker", "Costs"});
        match.RespondWithRaidTactic(
            seat, Id(request, "CardId"), Id(request, "TargetId"),
            Id(request, "ReplacementBlocker"), Ids(request, "Costs"));
    }
    else if (type == "PassRaidResponse")
    {
        Fields(request, {"Type"});
        match.PassRaidResponse(seat);
    }
    else if (type == "ResolveRaid")
    {
        Fields(request, {"Type"});
        match.ResolveRaid(seat);
    }
    else if (type == "BeginSpellResponse")
    {
        Fields(request, {"Type", "CardId", "TargetId", "Costs"});
        match.BeginSpellResponse(seat, Id(request, "CardId"),
                                 request.contains("TargetId") ? Id(request, "TargetId") : 0,
                                 request.contains("Costs") ? Ids(request, "Costs") : std::vector<int>{});
    }
    else if (type == "RespondWithNegation")
    {
        Fields(request, {"Type", "CardId", "Costs"});
        match.RespondWithNegation(seat, Id(request, "CardId"), Ids(request, "Costs"));
    }
    else if (type == "PassSpellResponse")
    {
        Fields(request, {"Type"});
        match.PassSpellResponse(seat);
    }
    else
    {
        throw std::invalid_argument("Unknown Yakou command");
    }
}
} // namespace

YakouCommandGateway::YakouCommandGateway(
    YakouPrototype const& catalog, int firstPlayer,
    std::array<std::vector<int>, 2> const& deckDefinitions)
    : match_(catalog, firstPlayer, deckDefinitions)
{
}

Json YakouCommandGateway::Snapshot(int authenticatedPlayer) const
{
    if (authenticatedPlayer != 1 && authenticatedPlayer != 2)
        throw std::invalid_argument("Invalid authenticated seat");
    return match_.ViewerSnapshot(authenticatedPlayer);
}

Json YakouCommandGateway::Execute(int authenticatedPlayer, Json const& command)
{
    // Reject bad server bindings before even forming a viewer projection.
    if (authenticatedPlayer != 1 && authenticatedPlayer != 2)
        throw std::invalid_argument("Invalid authenticated seat");
    bool accepted = false;
    YakouMatch candidate = match_;
    try
    {
        Apply(candidate, authenticatedPlayer, command);
        match_ = std::move(candidate);
        accepted = true;
    }
    catch (std::exception const&)
    {
        // No exception detail from internal state, card definitions or hidden
        // targets should escape into the transport response.
    }
    return {{"Accepted", accepted},
            {"Reason", accepted ? "OK" : "Rejected"},
            {"Snapshot", Snapshot(authenticatedPlayer)}};
}
} // namespace ProjectC::Network
