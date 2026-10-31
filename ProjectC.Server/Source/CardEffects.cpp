#include "ProjectC/CardEffects.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "GameOperations.hpp"

#include <algorithm>
#include <set>
#include <utility>

namespace ProjectC::GameCore
{
    bool ValidateEffectDefinitions(GameState const& state)
    {
        for (CardDefinition const& definition : state.Definitions)
        {
            for (CardDefinitionId const form : definition.AllowedManifestations)
                if (form == definition.Id || FindDefinition(state, form) == nullptr) return false;
            std::set<int> ids;
            for (EffectDefinition const& effect : definition.Effects)
            {
                if (effect.Id <= 0 || !ids.insert(effect.Id).second || effect.Actions.empty()
                    || effect.Type < EffectType::Activated || effect.Type > EffectType::Replacement
                    || effect.Trigger < TriggerCondition::None || effect.Trigger > TriggerCondition::OnManifested
                    || (effect.Type == EffectType::Triggered && effect.Trigger == TriggerCondition::None)) return false;
                if(effect.PerTurnLimit<0 || effect.Costs.size()>16 || (effect.ActivationZone!=Zone::Hand && effect.ActivationZone!=Zone::Field)) return false;
                for(auto const& cost:effect.Costs)
                    if(cost.Kind<CostKind::YokaiPower || cost.Kind>CostKind::HiddenExileTop || cost.Amount<0
                        || cost.Targets.MinCount>cost.Targets.MaxCount || cost.Targets.MaxCount>128 || cost.Targets.MaxCount==0) return false;
                for (EffectAction const& action : effect.Actions)
                {
                    TargetRule const& rule = action.Targets;
                    if (action.Operation < EffectOperation::Draw || action.Operation > EffectOperation::GainYokaiPower
                        || action.Amount < 0 || rule.MinCount > rule.MaxCount || rule.MaxCount == 0
                        || rule.Category < TargetCategory::Self || rule.Category > TargetCategory::EnemyGraveyard
                        || (rule.Type != CardType::Monster && rule.Type != CardType::Spell && rule.Type != CardType::Any)) return false;
                    for (Zone const zone : rule.Zones)
                        if (zone < Zone::Deck || zone > Zone::Exile) return false;
                    if(action.YokaiGainAmount<0 || rule.MaxAttack< -1 || (action.TemporaryStats && action.MaxHpDelta!=0)) return false;
                    if (action.Operation == EffectOperation::Transform
                        && FindDefinition(state, action.TransformDefinitionId) == nullptr) return false;
                }
            }
        }
        return true;
    }

    CommandResult ProcessCommand(GameState& state, ActivateEffectCommand const& command)
    {
        if (state.Status == GameStatus::Finished) return CommandResult{ .Code = CommandError::GameFinished };
        if (!ValidateGameState(state)) return CommandResult{ .Code = CommandError::InvalidState };
        PlayerState const* owner = FindPlayer(state, command.Player);
        if (owner == nullptr) return CommandResult{ .Code = CommandError::PlayerNotFound };
        if (state.ActivePlayerId != command.Player) return CommandResult{ .Code = CommandError::NotActivePlayer };
        if (!state.TurnStarted) return CommandResult{ .Code = CommandError::TurnNotStarted };
        if (state.Phase != TurnPhase::Main) return CommandResult{ .Code = CommandError::WrongPhase };
        CardInstance const* source=nullptr;Zone sourceZone=Zone::Field;
        for(auto zone:{Zone::Hand,Zone::Field}) for(auto const& card:*GetZone(*owner,zone))
            if(card.Id==command.SourceCard) { source=&card;sourceZone=zone; }
        if (source == nullptr || source->IsDestroyed()) return CommandResult{ .Code = CommandError::CardNotFound };
        CardDefinition const* definition = FindDefinition(state, source->CurrentDefinitionId);
        if (definition == nullptr) return CommandResult{ .Code = CommandError::InvalidDefinition };
        auto const effect = std::find_if(definition->Effects.begin(), definition->Effects.end(),
            [&](EffectDefinition const& entry) { return entry.Id == command.EffectId; });
        if (effect == definition->Effects.end()) return CommandResult{ .Code = CommandError::EffectNotFound };
        if (effect->Type != EffectType::Activated) return CommandResult{ .Code = CommandError::UnsupportedEffect };
        if(effect->ActivationZone!=sourceZone) return CommandResult{.Code=CommandError::WrongZone};
        GameState candidate = state;
        CommandResult result;
        bool spell=definition->Type==CardType::Spell;
        if(spell && sourceZone!=Zone::Hand) return CommandResult{.Code=CommandError::WrongZone};
        if(spell) result.Events.push_back(GameEvent{.Type=GameEventType::SpellActivated,.SourcePlayerId=command.Player,.SourceCardId=command.SourceCard});
        result.Code = Detail::ResolveEffect(candidate, command.Player, command.SourceCard, *effect,
            candidate.SelectionContext.Enabled?nullptr:&command.Targets, result.Events);
        if(result.Code==CommandError::Success && spell)
        {
            auto player=FindPlayer(candidate,command.Player);
            auto card=std::find_if(player->Hand.begin(),player->Hand.end(),[&](auto const& c){return c.Id==command.SourceCard;});
            if(card==player->Hand.end()) return CommandResult{.Code=CommandError::InvalidState};
            auto moved=*card;player->Hand.erase(card);player->Graveyard.push_back(moved);
            auto movement=Detail::Movement(moved,Zone::Hand,Zone::Graveyard);
            if(!result.Events.empty() && result.Events.back().Type==GameEventType::GameFinished) result.Events.insert(result.Events.end()-1,movement);
            else result.Events.push_back(movement);
            for(auto const& archetype:definition->Archetypes)
                if(std::find(player->GraveyardArchetypesThisTurn.begin(),player->GraveyardArchetypesThisTurn.end(),archetype)==player->GraveyardArchetypesThisTurn.end()) player->GraveyardArchetypesThisTurn.push_back(archetype);
        }
        return Detail::CommitCommand(state, std::move(candidate), std::move(result));
    }

    CommandResult ProcessCommand(GameState& state, TransformCommand const& command)
    {
        if (state.Status == GameStatus::Finished) return CommandResult{ .Code = CommandError::GameFinished };
        if (!ValidateGameState(state)) return CommandResult{ .Code = CommandError::InvalidState };
        GameState candidate = state;
        CommandResult result;
        result.Code = Detail::Manifest(candidate, command.Player, command.Card, command.Definition, result.Events);
        return Detail::CommitCommand(state, std::move(candidate), std::move(result));
    }

    CommandResult TransformCard(GameState& state, PlayerId owner, CardInstanceId card, CardDefinitionId destination)
    {
        return ProcessCommand(state, TransformCommand{ owner, card, destination });
    }
}

namespace ProjectC::GameCore::Detail
{
    std::vector<EffectTarget> SelectAutomaticTargets(GameState const& state, PlayerId actor,
        CardInstanceId source, TargetRule const& rule)
    {
        std::vector<EffectTarget> result;
        TargetRule single = rule;
        single.MinCount = 0;
        single.MaxCount = 1;
        auto add = [&](EffectTarget const& target)
        {
            if (result.size() < rule.MinCount
                && ValidateTargets(state, actor, source, single, { target }) == CommandError::Success)
                result.push_back(target);
        };
        for (PlayerState const& player : state.Players)
        {
            add(EffectTarget{ player.Id, 0, Zone::Field });
            for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard })
                for (CardInstance const& card : *GetZone(player, zone)) add(EffectTarget{ player.Id, card.Id, zone });
        }
        return result;
    }

    CommandError ResolveEffect(GameState& state, PlayerId actor, CardInstanceId source,
        EffectDefinition const& effect, std::vector<std::vector<EffectTarget>> const* selections,
        std::vector<GameEvent>& events)
    {
        CardDefinitionId definitionId=0;
        if(auto owner=FindPlayer(state,actor)) for(auto zone:{Zone::Hand,Zone::Field,Zone::Graveyard})
            for(auto const& card:*GetZone(*owner,zone)) if(card.Id==source) definitionId=card.CurrentDefinitionId;
        auto condition=CheckEffectConditions(state,actor,definitionId,effect);
        if(condition!=CommandError::Success) return condition;
        auto paid=PayEffectCosts(state,actor,source,effect,events);
        if(paid!=CommandError::Success) return paid;
        if (selections != nullptr && selections->size() != effect.Actions.size())
            return CommandError::InvalidTargetCount;
        events.push_back(GameEvent{ .Type = GameEventType::EffectActivated,
            .SourcePlayerId = actor, .SourceCardId = source, .EffectId = effect.Id });
        for (std::size_t index = 0; index < effect.Actions.size(); ++index)
        {
            EffectAction const& action = effect.Actions[index];
            bool summons=action.Operation==EffectOperation::SummonFromDeck || action.Operation==EffectOperation::SummonFromHand || action.Operation==EffectOperation::SummonFromGraveyard;
            auto owner=FindPlayer(state,actor);
            if(summons && owner && owner->Field.size()+action.Targets.MinCount>state.Config.FieldLimit) return CommandError::FieldFull;
            std::vector<EffectTarget> targets;
            if(selections==nullptr)
            {
                auto selected=SelectEffectTargets(state,actor,source,effect.Id,action.Targets,false,action.AllTargets,targets);
                if(selected!=CommandError::Success) return selected;
            }
            else targets=(*selections)[index];
            CommandError const validation = ValidateTargets(state, actor, source, action.Targets, targets);
            if (validation != CommandError::Success) return validation;
            for (EffectTarget const& target : targets)
            {
                // Revalidate after each preceding target/action changed the authoritative working state.
                TargetRule single = action.Targets;
                single.MinCount = single.MaxCount = 1;
                CommandError const stillValid = ValidateTargets(state, actor, source, single, { target });
                if (stillValid != CommandError::Success) return stillValid;
                CommandError const outcome = ApplyEffectAction(state, actor, source, action, target, events);
                if (outcome != CommandError::Success) return outcome;
                if (state.Status == GameStatus::Finished) break;
            }
            if (state.Status == GameStatus::Finished) break;
        }
        RecordEffectUse(state,actor,definitionId,effect);
        state.SelectionContext.CostCards.clear();
        GameEvent resolved{ .Type = GameEventType::EffectResolved,
            .SourcePlayerId = actor, .SourceCardId = source, .EffectId = effect.Id };
        if (!events.empty() && events.back().Type == GameEventType::GameFinished)
            events.insert(events.end() - 1, resolved);
        else events.push_back(resolved);
        return CommandError::Success;
    }

    CommandResult CommitCommand(GameState& state, GameState&& candidate, CommandResult result)
    {
        if (result.Code == CommandError::Success)
            result.Code = ResolveTriggeredEffects(candidate, result.Events);
        if (result.Code == CommandError::Success && !ValidateGameState(candidate))
            result.Code = CommandError::InvalidState;
        if (result.Code == CommandError::Success)
        {
            state = std::move(candidate);
        }
        else { if(result.Code==CommandError::ChoiceRequired) result.Choice=candidate.SelectionContext.Request;result.Events.clear(); }
        return result;
    }
}
