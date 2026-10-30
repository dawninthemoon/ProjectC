#pragma once

#include "ProjectC/GameCommand.hpp"

namespace ProjectC::GameCore
{
    [[nodiscard]] bool ValidateEffectDefinitions(GameState const& gameState);
    [[nodiscard]] CommandError ValidateTargets(GameState const& gameState, PlayerId actor,
        CardInstanceId source, TargetRule const& rule, std::vector<EffectTarget> const& targets);
    // Internal server operation: allowed-form validation is mandatory; costs/activation Zone remain TODO.
    [[nodiscard]] CommandResult TransformCard(GameState& gameState, PlayerId owner,
        CardInstanceId card, CardDefinitionId destination);
    // Authoritative journal -> per-viewer projection. No raw server journal is network-public.
    [[nodiscard]] std::vector<GameEvent> EventsForPlayer(std::vector<GameEvent> const& events, PlayerId viewer);
}
