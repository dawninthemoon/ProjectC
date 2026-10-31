#include "GameOperations.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <algorithm>
#include <cstdint>
#include <limits>

namespace ProjectC::GameCore::Detail
{
    namespace
    {
        CardInstance* CardAt(GameState& state, EffectTarget const& target)
        {
            PlayerState* owner = FindPlayer(state, target.Player);
            auto* zone = owner == nullptr ? nullptr : GetZone(*owner, target.Location);
            if (zone != nullptr)
                for (CardInstance& card : *zone) if (card.Id == target.Card) return &card;
            return nullptr;
        }

        int PreserveDamageHp(CardInstance const& card, int newMax)
        {
            std::int64_t const damage = std::max<std::int64_t>(0,
                static_cast<std::int64_t>(card.MaxHp) - card.Hp);
            return static_cast<int>(std::max<std::int64_t>(0, static_cast<std::int64_t>(newMax) - damage));
        }

        CommandError MoveEffectCard(GameState& state, EffectTarget const& target,
            EffectOperation operation, std::vector<GameEvent>& events)
        {
            CardInstance* card = CardAt(state, target);
            if (card == nullptr) return CommandError::InvalidTarget;
            Zone destination = Zone::Hand;
            if (operation == EffectOperation::Salvage && target.Location != Zone::Graveyard)
                return CommandError::InvalidTarget;
            if (operation == EffectOperation::Search && target.Location != Zone::Deck)
                return CommandError::InvalidTarget;
            if (operation == EffectOperation::ReturnToHand && target.Location != Zone::Field)
                return CommandError::InvalidTarget;
            if (operation == EffectOperation::Salvage)
            {
                CardDefinition const* definition = FindDefinition(state, card->OriginalDefinitionId);
                if (definition == nullptr) return CommandError::InvalidDefinition;
                card->CurrentDefinitionId = card->OriginalDefinitionId;
                card->Attack = definition->Attack;
                card->Hp = card->MaxHp = definition->MaxHp;
                card->HasAttackedThisTurn = false;
                card->HasManifested = false;
            }
            if (operation == EffectOperation::SummonFromDeck || operation == EffectOperation::SummonFromGraveyard || operation==EffectOperation::SummonFromHand)
            {
                Zone const required = operation == EffectOperation::SummonFromDeck ? Zone::Deck : operation==EffectOperation::SummonFromHand?Zone::Hand:Zone::Graveyard;
                if (target.Location != required) return CommandError::InvalidTarget;
                destination = Zone::Field;
                if (required == Zone::Graveyard)
                {
                    CardDefinition const* definition = FindDefinition(state, card->OriginalDefinitionId);
                    if (definition == nullptr) return CommandError::InvalidDefinition;
                    card->CurrentDefinitionId = card->OriginalDefinitionId;
                    card->Attack = definition->Attack;
                    card->Hp = card->MaxHp = definition->MaxHp;
                    card->HasManifested = false;
                }
            }
            ZoneResult const moved = MoveCard(state, target.Player, target.Card, target.Location, destination);
            if (moved == ZoneResult::DestinationFull)
                return destination == Zone::Field ? CommandError::FieldFull : CommandError::HandFull;
            if (moved != ZoneResult::Success) return CommandError::InvalidTarget;
            CardInstance const& after = GetZone(*FindPlayer(state, target.Player), destination)->back();
            events.push_back(Movement(after, target.Location, destination));
            if (destination == Zone::Field)
                events.push_back(GameEvent{ .Type = GameEventType::CardSummoned,
                    .TargetPlayerId = target.Player, .TargetCardId = target.Card,
                    .CurrentDefinitionId = after.CurrentDefinitionId, .SummonOrigin=target.Location==Zone::Deck?1:target.Location==Zone::Hand?2:3 });
            return CommandError::Success;
        }

        CommandError ApplyStatAction(GameState& state, PlayerId actor, CardInstanceId source,
            EffectAction const& action, EffectTarget const& target, std::vector<GameEvent>& events)
        {
            if (target.Card == 0)
            {
                if (action.Operation != EffectOperation::DealDamage && action.Operation != EffectOperation::Heal)
                    return CommandError::InvalidTarget;
                return ChangeLife(state, target.Player,
                    action.Operation == EffectOperation::DealDamage ? -action.Amount : action.Amount, events);
            }
            CardInstance* card = CardAt(state, target);
            if (card == nullptr || target.Location != Zone::Field) return CommandError::InvalidTarget;
            if (action.Operation == EffectOperation::DealDamage)
            {
                ApplyDamage(*card, action.Amount);
                events.push_back(GameEvent{ .Type = GameEventType::CardDamaged,
                    .SourcePlayerId = actor, .SourceCardId = source,
                    .TargetPlayerId = target.Player, .TargetCardId = target.Card,
                    .Damage = action.Amount, .HpAfter = card->Hp,
                    .CurrentDefinitionId = card->CurrentDefinitionId });
            }
            else if (action.Operation == EffectOperation::Heal)
            {
                card->Hp = static_cast<int>(std::min<std::int64_t>(card->MaxHp,
                    static_cast<std::int64_t>(card->Hp) + action.Amount));
                events.push_back(GameEvent{ .Type = GameEventType::CardHealed,
                    .SourcePlayerId = actor, .SourceCardId = source,
                    .TargetPlayerId = target.Player, .TargetCardId = target.Card, .HpAfter = card->Hp });
            }
            else
            {
                std::int64_t const attack = static_cast<std::int64_t>(card->Attack) + action.AttackDelta;
                std::int64_t const maxHp = static_cast<std::int64_t>(card->MaxHp) + action.MaxHpDelta;
                if (attack < std::numeric_limits<int>::min() || attack > std::numeric_limits<int>::max()
                    || maxHp < 0 || maxHp > std::numeric_limits<int>::max()) return CommandError::InvalidEffect;
                card->Hp = PreserveDamageHp(*card, static_cast<int>(maxHp));
                if(action.TemporaryStats)
                {
                    auto temporary=static_cast<std::int64_t>(card->TemporaryAttack)+action.AttackDelta;
                    if(action.MaxHpDelta!=0 || temporary<std::numeric_limits<int>::min() || temporary>std::numeric_limits<int>::max()) return CommandError::InvalidEffect;
                    card->TemporaryAttack=static_cast<int>(temporary);
                }
                card->Attack = static_cast<int>(attack);
                card->MaxHp = static_cast<int>(maxHp);
                events.push_back(GameEvent{ .Type = GameEventType::StatsModified,
                    .SourcePlayerId = actor, .SourceCardId = source,
                    .TargetPlayerId = target.Player, .TargetCardId = target.Card, .HpAfter = card->Hp,
                    .AttackAfter = card->Attack, .MaxHpAfter = card->MaxHp });
            }
            if (card->IsDestroyed()) return DestroyCard(state, target, events);
            return CommandError::Success;
        }
    }

    CommandError DestroyCard(GameState& state, EffectTarget const& target, std::vector<GameEvent>& events)
    {
        CardInstance* card = CardAt(state, target);
        if (card == nullptr || target.Location != Zone::Field) return CommandError::InvalidTarget;
        card->Hp = 0;
        events.push_back(GameEvent{ .Type = GameEventType::CardDestroyed,
            .TargetPlayerId = target.Player, .TargetCardId = target.Card,
            .CurrentDefinitionId = card->CurrentDefinitionId });
        if (MoveCard(state, target.Player, target.Card, Zone::Field, Zone::Graveyard) != ZoneResult::Success)
            return CommandError::InvalidState;
        events.push_back(Movement(FindPlayer(state, target.Player)->Graveyard.back(), Zone::Field, Zone::Graveyard));
        return CommandError::Success;
    }

    CommandError Manifest(GameState& state, PlayerId owner, CardInstanceId id,
        CardDefinitionId destination, std::vector<GameEvent>& events)
    {
        PlayerState* player = FindPlayer(state, owner);
        CardInstance* card = player == nullptr ? nullptr : FindCard(*player, id);
        if (card == nullptr || card->OwnerId != owner || card->IsDestroyed()) return CommandError::InvalidTarget;
        CardDefinition const* before = FindDefinition(state, card->CurrentDefinitionId);
        CardDefinition const* after = FindDefinition(state, destination);
        if (before == nullptr || after == nullptr || after->Type != CardType::Monster
            || destination == card->OriginalDefinitionId || destination == card->CurrentDefinitionId
            || (card->HasManifested && !state.Config.AllowRemanifestation)
            || std::find(before->AllowedManifestations.begin(), before->AllowedManifestations.end(), destination)
                == before->AllowedManifestations.end()) return CommandError::InvalidTransformation;
        card->Hp = PreserveDamageHp(*card, after->MaxHp);
        card->Attack = after->Attack;
        card->TemporaryAttack=0;
        card->MaxHp = after->MaxHp;
        card->CurrentDefinitionId = destination;
        card->HasManifested = true;
        events.push_back(GameEvent{ .Type = GameEventType::CardTransformed,
            .TargetPlayerId = owner, .TargetCardId = id, .HpAfter = card->Hp,
            .OriginalDefinitionId = card->OriginalDefinitionId, .CurrentDefinitionId = destination,
            .AttackAfter = card->Attack, .MaxHpAfter = card->MaxHp });
        if (card->IsDestroyed()) return DestroyCard(state, EffectTarget{ owner, id, Zone::Field }, events);
        return CommandError::Success;
    }

    CommandError ApplyEffectAction(GameState& state, PlayerId actor, CardInstanceId source,
        EffectAction const& action, EffectTarget const& target, std::vector<GameEvent>& events)
    {
        if(action.Operation==EffectOperation::GainYokaiPower)
            return target.Card==0?ChangeYokaiPower(state,target.Player,action.Amount,events):CommandError::InvalidTarget;
        if(action.Operation==EffectOperation::Discard || action.Operation==EffectOperation::SendToGraveyard || action.Operation==EffectOperation::Exile)
        {
            auto card=CardAt(state,target);if(!card || target.Card==source) return CommandError::InvalidTarget;
            if(action.Operation==EffectOperation::Discard && (target.Player!=actor || target.Location!=Zone::Hand)) return CommandError::InvalidTarget;
            if(action.Operation==EffectOperation::SendToGraveyard && (target.Player!=actor || target.Location!=Zone::Deck)) return CommandError::InvalidTarget;
            bool gain=false;
            if(auto definition=FindDefinition(state,card->CurrentDefinitionId)) gain=definition->Type==CardType::Monster && !action.YokaiGainArchetype.empty() && std::find(definition->Archetypes.begin(),definition->Archetypes.end(),action.YokaiGainArchetype)!=definition->Archetypes.end();
            Zone destination=action.Operation==EffectOperation::Exile?Zone::Exile:Zone::Graveyard;
            if(MoveCard(state,target.Player,target.Card,target.Location,destination)!=ZoneResult::Success) return CommandError::InvalidTarget;
            events.push_back(Movement(GetZone(*FindPlayer(state,target.Player),destination)->back(),target.Location,destination));
            if(gain) return ChangeYokaiPower(state,actor,action.YokaiGainAmount,events);
            return CommandError::Success;
        }
        if (action.Operation == EffectOperation::Draw || action.Operation == EffectOperation::Mill)
        {
            if (target.Card != 0) return CommandError::InvalidTarget;
            if (action.Operation == EffectOperation::Draw)
                return DrawCards(state, target.Player, static_cast<std::size_t>(action.Amount), events);
            PlayerState* player = FindPlayer(state, target.Player);
            if (player == nullptr) return CommandError::InvalidTarget;
            auto const count = std::min(static_cast<std::size_t>(action.Amount), player->Deck.size());
            for (std::size_t index = 0; index < count; ++index)
            {
                CardInstance const card = player->Deck.front();
                if (MoveCard(state, player->Id, card.Id, Zone::Deck, Zone::Graveyard) != ZoneResult::Success)
                    return CommandError::InvalidState;
                events.push_back(Movement(player->Graveyard.back(), Zone::Deck, Zone::Graveyard));
            }
            return CommandError::Success;
        }
        if (action.Operation == EffectOperation::Destroy) return DestroyCard(state, target, events);
        if (action.Operation == EffectOperation::Transform)
        {
            if (target.Location != Zone::Field || target.Card == 0) return CommandError::InvalidTarget;
            return Manifest(state, target.Player, target.Card, action.TransformDefinitionId, events);
        }
        if (action.Operation == EffectOperation::DealDamage || action.Operation == EffectOperation::Heal
            || action.Operation == EffectOperation::ModifyStats)
            return ApplyStatAction(state, actor, source, action, target, events);
        auto code=MoveEffectCard(state, target, action.Operation, events);
        if(code==CommandError::Success && action.PreventDirectAttackThisTurn)
            if(auto card=FindCard(*FindPlayer(state,target.Player),target.Card)) card->DirectAttackRestrictedTurn=state.TurnNumber;
        return code;
    }
}
