#include "ProjectC/CardEffects.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <algorithm>
#include <set>

namespace ProjectC::GameCore
{
    CommandError ValidateTargets(GameState const& state, PlayerId actor, CardInstanceId source,
        TargetRule const& rule, std::vector<EffectTarget> const& targets)
    {
        if (FindPlayer(state, actor) == nullptr) return CommandError::PlayerNotFound;
        if (targets.size() < rule.MinCount || targets.size() > rule.MaxCount)
            return CommandError::InvalidTargetCount;
        std::set<std::pair<PlayerId, CardInstanceId>> unique;
        for (EffectTarget const& target : targets)
        {
            PlayerState const* player = FindPlayer(state, target.Player);
            if (player == nullptr || !unique.emplace(target.Player, target.Card).second)
                return CommandError::InvalidTarget;
            bool const friendly = actor == target.Player;
            if (rule.Category == TargetCategory::FriendlyPlayer || rule.Category == TargetCategory::EnemyPlayer)
            {
                if (target.Card != 0 || !rule.RequiredTrait.empty()
                    || friendly != (rule.Category == TargetCategory::FriendlyPlayer)) return CommandError::InvalidTarget;
                continue;
            }
            if (target.Card == 0) return CommandError::InvalidTarget;
            if ((target.Location == Zone::Deck || target.Location == Zone::Hand) && !friendly)
                return CommandError::InvalidTarget;
            if (std::find(rule.Zones.begin(), rule.Zones.end(), target.Location) == rule.Zones.end())
                return CommandError::InvalidTarget;
            auto const* zone = GetZone(*player, target.Location);
            if (zone == nullptr) return CommandError::InvalidTarget;
            auto const card = std::find_if(zone->begin(), zone->end(),
                [&](CardInstance const& entry) { return entry.Id == target.Card; });
            if (card == zone->end() || card->OwnerId != target.Player) return CommandError::InvalidTarget;
            if (target.Location == Zone::Field && card->IsDestroyed()) return CommandError::InvalidTarget;
            switch (rule.Category)
            {
            case TargetCategory::Self:
                if (!friendly || target.Card != source) return CommandError::InvalidTarget;
                break;
            case TargetCategory::FriendlyMonster:
                if (!friendly || target.Location != Zone::Field) return CommandError::InvalidTarget;
                break;
            case TargetCategory::EnemyMonster:
                if (friendly || target.Location != Zone::Field) return CommandError::InvalidTarget;
                break;
            case TargetCategory::AnyMonster:
                if (target.Location != Zone::Field) return CommandError::InvalidTarget;
                break;
            case TargetCategory::FriendlyDeck:
                if (!friendly || target.Location != Zone::Deck) return CommandError::InvalidTarget;
                break;
            case TargetCategory::FriendlyHand:
                if (!friendly || target.Location != Zone::Hand) return CommandError::InvalidTarget;
                break;
            case TargetCategory::FriendlyGraveyard:
                if (!friendly || target.Location != Zone::Graveyard) return CommandError::InvalidTarget;
                break;
            case TargetCategory::EnemyGraveyard:
                if (friendly || target.Location != Zone::Graveyard) return CommandError::InvalidTarget;
                break;
            default: return CommandError::InvalidTarget;
            }
            CardDefinition const* definition = FindDefinition(state, card->CurrentDefinitionId);
            if (definition == nullptr || definition->Type != rule.Type) return CommandError::InvalidTarget;
            if (!rule.RequiredTrait.empty() && std::find(definition->Traits.begin(), definition->Traits.end(),
                rule.RequiredTrait) == definition->Traits.end()) return CommandError::InvalidTarget;
        }
        return CommandError::Success;
    }

    std::vector<GameEvent> EventsForPlayer(std::vector<GameEvent> const& events, PlayerId viewer)
    {
        std::vector<GameEvent> visible;
        for (GameEvent event : events)
        {
            bool const privateMove = event.Type == GameEventType::CardMoved
                && event.TargetPlayerId != viewer
                && (event.FromZone == Zone::Deck || event.FromZone == Zone::Hand
                    || event.ToZone == Zone::Deck || event.ToZone == Zone::Hand);
            if (privateMove)
            {
                event.OriginalDefinitionId = 0;
                event.CurrentDefinitionId = 0;
                // Revealed Field/Graveyard destinations provide identity in a separate public event.
                event.TargetCardId = 0;
            }
            visible.push_back(event);
        }
        return visible;
    }
}
