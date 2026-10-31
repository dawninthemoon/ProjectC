#include "GameOperations.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "ProjectC/CardEffects.hpp"
#include <algorithm>
#include <cstdint>

namespace ProjectC::GameCore::Detail
{
    CommandError ChangeYokaiPower(GameState& state, PlayerId actor, int delta, std::vector<GameEvent>& events)
    {
        auto player=FindPlayer(state,actor);
        if(!player) return CommandError::PlayerNotFound;
        auto value=static_cast<std::int64_t>(player->YokaiPower)+delta;
        if(value<0) return CommandError::InsufficientResource;
        player->YokaiPower=static_cast<int>(std::min<std::int64_t>(value,state.Config.MaxYokaiPower));
        events.push_back(GameEvent{.Type=GameEventType::YokaiPowerChanged,.TargetPlayerId=actor,.YokaiPowerAfter=player->YokaiPower});
        return CommandError::Success;
    }
    CommandError CheckEffectConditions(GameState const& state, PlayerId actor, CardDefinitionId definition, EffectDefinition const& effect)
    {
        auto owner=FindPlayer(state,actor);if(!owner) return CommandError::PlayerNotFound;
        for(auto const& use:owner->EffectUses)
            if(use.Definition==definition && use.Effect==effect.Id && effect.PerTurnLimit>0 && use.Count>=effect.PerTurnLimit)
                return CommandError::UsageLimitReached;
        if(!effect.RequireGraveyardEntryThisTurn.empty() && std::find(owner->GraveyardArchetypesThisTurn.begin(),owner->GraveyardArchetypesThisTurn.end(),effect.RequireGraveyardEntryThisTurn)==owner->GraveyardArchetypesThisTurn.end()) return CommandError::ConditionNotMet;
        std::size_t count=0;
        for(auto const& card:owner->Graveyard)
            if(auto def=FindDefinition(state,card.OriginalDefinitionId);def && (effect.RequiredGraveyardArchetype.empty() || std::find(def->Archetypes.begin(),def->Archetypes.end(),effect.RequiredGraveyardArchetype)!=def->Archetypes.end())) ++count;
        return count>=effect.MinGraveyardCount?CommandError::Success:CommandError::ConditionNotMet;
    }
    void RecordEffectUse(GameState& state, PlayerId actor, CardDefinitionId definition, EffectDefinition const& effect)
    {
        if(effect.PerTurnLimit==0) return;
        auto owner=FindPlayer(state,actor);
        for(auto& use:owner->EffectUses) if(use.Definition==definition && use.Effect==effect.Id) {++use.Count;return;}
        owner->EffectUses.push_back({definition,effect.Id,1});
    }
    CommandError SelectEffectTargets(GameState& state, PlayerId actor, CardInstanceId source, int effect,
        TargetRule const& rule, bool cost, bool all, std::vector<EffectTarget>& targets)
    {
        std::vector<ChoiceCandidate> candidates;
        auto single=rule;single.MinCount=0;single.MaxCount=1;
        auto add=[&](EffectTarget const& target,CardInstance const& card) {
            if((cost || rule.Category==TargetCategory::FriendlyHand) && card.Id==source) return;
            if(!cost && std::find(state.SelectionContext.CostCards.begin(),state.SelectionContext.CostCards.end(),card.Id)!=state.SelectionContext.CostCards.end()) return;
            if(ValidateTargets(state,actor,source,single,{target})==CommandError::Success) candidates.push_back({target,card});
        };
        for(auto const& player:state.Players)
        {
            add({player.Id,0,Zone::Field},{});
            for(auto zone:{Zone::Deck,Zone::Hand,Zone::Field,Zone::Graveyard,Zone::Exile})
                for(auto const& card:*GetZone(player,zone)) add({player.Id,card.Id,zone},card);
        }
        if(candidates.size()<rule.MinCount) return CommandError::InvalidTargetCount;
        bool automatic=candidates.empty() || all || !state.SelectionContext.Enabled || rule.Category==TargetCategory::Self
            || rule.Category==TargetCategory::FriendlyPlayer || rule.Category==TargetCategory::EnemyPlayer;
        if(automatic)
        {
            auto count=all?std::min(candidates.size(),rule.MaxCount):rule.MinCount;
            for(std::size_t i=0;i<count;++i) targets.push_back(candidates[i].Target);
        }
        else
        {
            auto& context=state.SelectionContext;
            if(context.Cursor>=context.Selections.size())
            {
                context.Request={actor,source,effect,cost,rule,std::move(candidates)};
                return CommandError::ChoiceRequired;
            }
            targets=context.Selections[context.Cursor++];
            for(auto const& target:targets)
                if(std::none_of(candidates.begin(),candidates.end(),[&](auto const& candidate){return candidate.Target==target;})) return CommandError::InvalidTarget;
        }
        return ValidateTargets(state,actor,source,rule,targets);
    }
    CommandError PayEffectCosts(GameState& state, PlayerId actor, CardInstanceId source, EffectDefinition const& effect, std::vector<GameEvent>& events)
    {
        auto owner=FindPlayer(state,actor);
        state.SelectionContext.CostCards.clear();
        for(auto const& cost:effect.Costs)
        {
            if(cost.Amount<0) return CommandError::InvalidCost;
            if(cost.Kind==CostKind::YokaiPower)
            { auto code=ChangeYokaiPower(state,actor,-cost.Amount,events);if(code!=CommandError::Success) return code; }
            else if(cost.Kind==CostKind::Life)
            { if(owner->Life<=cost.Amount) return CommandError::InvalidCost;auto code=ChangeLife(state,actor,-cost.Amount,events);if(code!=CommandError::Success) return code; }
            else if(cost.Kind==CostKind::HiddenExileTop)
            {
                if(owner->Deck.size()<static_cast<std::size_t>(cost.Amount)) return CommandError::InvalidCost;
                for(int i=0;i<cost.Amount;++i)
                {
                    auto card=owner->Deck.front();
                    if(MoveCard(state,actor,card.Id,Zone::Deck,Zone::HiddenExile)!=ZoneResult::Success) return CommandError::InvalidCost;
                    events.push_back(Movement(owner->HiddenExile.back(),Zone::Deck,Zone::HiddenExile));
                }
            }
            else
            {
                std::vector<EffectTarget> targets;
                auto code=SelectEffectTargets(state,actor,source,effect.Id,cost.Targets,true,false,targets);
                if(code!=CommandError::Success) return code;
                for(auto const& target:targets)
                {
                    if(target.Player!=actor || target.Card==source || target.Location!=(cost.Kind==CostKind::Tribute?Zone::Field:Zone::Hand)) return CommandError::InvalidCost;
                    if(MoveCard(state,actor,target.Card,target.Location,Zone::Graveyard)!=ZoneResult::Success) return CommandError::InvalidCost;
                    state.SelectionContext.CostCards.push_back(target.Card);
                    events.push_back(Movement(owner->Graveyard.back(),target.Location,Zone::Graveyard));
                }
            }
        }
        return CommandError::Success;
    }
}

namespace ProjectC::GameCore
{
    CommandResult ProcessCommand(GameState& state, PlayerActionCommand const& command)
    {
        if(command.Choices.size()>128) return CommandResult{.Code=CommandError::InvalidTargetCount};
        GameState candidate=state;
        candidate.SelectionContext={true,command.Choices,0,{}, {}};
        CommandResult result;
        if(command.EffectId==0) result=ProcessCommand(candidate,PlayCardCommand{command.Player,command.SourceCard});
        else result=ProcessCommand(candidate,ActivateEffectCommand{command.Player,command.SourceCard,command.EffectId,{}});
        if(result.Code==CommandError::Success)
        {
            if(candidate.SelectionContext.Cursor!=command.Choices.size()) return CommandResult{.Code=CommandError::InvalidTargetCount};
            candidate.SelectionContext={};state=std::move(candidate);
        }
        return result;
    }
}
