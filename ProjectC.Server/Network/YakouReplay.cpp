#include "YakouReplay.hpp"

#include <stdexcept>
#include <utility>

namespace ProjectC::Network
{
namespace
{
void Apply(YakouMatch& match, YakouReplayCommand const& command)
{
    switch (command.Operation)
    {
    case YakouReplayOperation::StartTurn:
        match.StartTurn(command.Player);
        break;
    case YakouReplayOperation::Summon:
        match.Summon(command.Player, command.CardId, command.Method, command.Cards, command.Slot);
        break;
    case YakouReplayOperation::ActivateMainSpell:
        match.ActivateMainSpell(command.Player, command.CardId);
        break;
    case YakouReplayOperation::Manifest:
        match.Manifest(command.Player, command.CardId, command.DefinitionId);
        break;
    case YakouReplayOperation::Postpone:
        match.Postpone(command.Player, command.CardId);
        break;
    case YakouReplayOperation::Redraw:
        match.Redraw(command.Player, command.Cards);
        break;
    case YakouReplayOperation::BeginRaid:
        match.BeginRaid(command.Player);
        break;
    case YakouReplayOperation::AssignBlockers:
        match.AssignBlockers(command.Player, command.Blockers);
        break;
    case YakouReplayOperation::PassRaidResponse:
        match.PassRaidResponse(command.Player);
        break;
    case YakouReplayOperation::ResolveRaid:
        match.ResolveRaid(command.Player);
        break;
    case YakouReplayOperation::ResolvePendingEffect:
        match.ResolvePendingEffect(command.Player, command.TargetId);
        break;
    default:
        throw std::invalid_argument("Unknown Yakou replay operation");
    }
}
} // namespace

YakouReplayResult ReplayYakou(YakouPrototype const& catalog,
                               int firstPlayer,
                               std::array<std::vector<int>, 2> const& initialDecks,
                               std::vector<YakouReplayCommand> const& commands)
{
    YakouMatch match(catalog, firstPlayer, initialDecks);
    std::vector<YakouReplayStep> steps;
    steps.reserve(commands.size());
    for (auto const& command : commands)
    {
        YakouReplayStep step;
        step.BeforeHash = match.ServerStateHash();
        try
        {
            Apply(match, command);
            step.Accepted = true;
        }
        catch (std::invalid_argument const& error)
        {
            step.Reason = error.what();
        }
        step.AfterHash = match.ServerStateHash();
        if (!step.Accepted && step.BeforeHash != step.AfterHash)
        {
            throw std::logic_error("Rejected Yakou replay command changed the server state");
        }
        steps.push_back(std::move(step));
    }
    return {std::move(match), std::move(steps)};
}
} // namespace ProjectC::Network
