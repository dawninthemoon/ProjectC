#include "GameOperations.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <deque>
#include <utility>

namespace ProjectC::GameCore::Detail
{
    namespace
    {
        struct PendingEffect
        {
            PlayerId Owner;
            CardInstanceId Source;
            EffectDefinition Definition;
        };

        TriggerCondition Condition(GameEventType type)
        {
            switch (type)
            {
            case GameEventType::CardSummoned: return TriggerCondition::OnSummoned;
            case GameEventType::CardDestroyed: return TriggerCondition::OnDestroyed;
            case GameEventType::CardDamaged: return TriggerCondition::OnDamaged;
            case GameEventType::TurnStarted: return TriggerCondition::OnTurnStarted;
            case GameEventType::TurnEnded: return TriggerCondition::OnTurnEnded;
            default: return TriggerCondition::None;
            }
        }

        void Register(GameState const& state, PlayerId owner, CardInstanceId card,
            CardDefinitionId id, TriggerCondition condition, std::deque<PendingEffect>& pending)
        {
            CardDefinition const* definition = FindDefinition(state, id);
            if (definition == nullptr) return;
            for (EffectDefinition const& effect : definition->Effects)
                if (effect.Type == EffectType::Triggered && effect.Trigger == condition)
                    pending.push_back(PendingEffect{ owner, card, effect });
        }
    }

    CommandError ResolveTriggeredEffects(GameState& state, std::vector<GameEvent>& events)
    {
        std::deque<PendingEffect> pending;
        std::size_t scanned = 0;
        std::size_t resolved = 0;
        while (state.Status == GameStatus::Running)
        {
            while (scanned < events.size())
            {
                GameEvent const event = events[scanned++];
                TriggerCondition const condition = Condition(event.Type);
                if (condition == TriggerCondition::None) continue;
                if (condition == TriggerCondition::OnDamaged && event.Damage <= 0) continue;
                if (condition == TriggerCondition::OnTurnStarted || condition == TriggerCondition::OnTurnEnded)
                {
                    for (PlayerState const& owner : state.Players)
                        if (owner.Id == event.TargetPlayerId)
                            for (CardInstance const& card : owner.Field)
                                if (!card.IsDestroyed())
                                    Register(state, owner.Id, card.Id, card.CurrentDefinitionId, condition, pending);
                }
                else Register(state, event.TargetPlayerId, event.TargetCardId,
                    event.CurrentDefinitionId, condition, pending);
            }
            if (pending.empty()) return CommandError::Success;
            if (resolved++ >= state.Config.TriggerResolutionLimit) return CommandError::TriggerLimitReached;
            PendingEffect const job = pending.front();
            pending.pop_front();
            GameState candidate = state;
            std::vector<GameEvent> produced;
            CommandError const outcome = ResolveEffect(candidate, job.Owner, job.Source, job.Definition, nullptr, produced);
            if (outcome == CommandError::Success)
            {
                state = std::move(candidate);
                events.insert(events.end(), produced.begin(), produced.end());
            }
            else
            {
                // An automatic trigger with no legal target fizzles; it cannot undo the originating action.
                events.push_back(GameEvent{ .Type = GameEventType::EffectResolved,
                    .SourcePlayerId = job.Owner, .SourceCardId = job.Source,
                    .EffectId = job.Definition.Id, .EffectOutcome = static_cast<int>(outcome) });
            }
        }
        return CommandError::Success;
    }
}
