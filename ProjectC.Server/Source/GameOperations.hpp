#pragma once

#include "ProjectC/GameCommand.hpp"

namespace ProjectC::GameCore::Detail
{
    [[nodiscard]] GameEvent Movement(CardInstance const& card, Zone from, Zone to);
    void MarkFieldEntry(GameState const& state, PlayerState const& owner, CardInstance& card);
    void FinishGame(GameState& state, PlayerId loser, GameEndReason reason, std::vector<GameEvent>& events);
    [[nodiscard]] CommandError DrawCards(GameState& state, PlayerId player, std::size_t count,
        std::vector<GameEvent>& events);
    [[nodiscard]] CommandError ChangeLife(GameState& state, PlayerId player, int delta,
        std::vector<GameEvent>& events);
    [[nodiscard]] CommandError DestroyCard(GameState& state, EffectTarget const& target,
        std::vector<GameEvent>& events);
    [[nodiscard]] CommandError ApplyEffectAction(GameState& state, PlayerId actor,
        CardInstanceId source, EffectAction const& action, EffectTarget const& target,
        std::vector<GameEvent>& events);
    [[nodiscard]] CommandError Manifest(GameState& state, PlayerId owner, CardInstanceId card,
        CardDefinitionId destination, std::vector<GameEvent>& events);
    [[nodiscard]] std::vector<EffectTarget> SelectAutomaticTargets(GameState const& state,
        PlayerId actor, CardInstanceId source, TargetRule const& rule);
    [[nodiscard]] CommandError ResolveEffect(GameState& state, PlayerId actor,
        CardInstanceId source, EffectDefinition const& effect,
        std::vector<std::vector<EffectTarget>> const* selections, std::vector<GameEvent>& events);
    [[nodiscard]] CommandError ResolveTriggeredEffects(GameState& state, std::vector<GameEvent>& events);
    [[nodiscard]] CommandResult CommitCommand(GameState& state, GameState&& candidate, CommandResult result);
}
