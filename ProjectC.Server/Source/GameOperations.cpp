#include "GameOperations.hpp"

#include <algorithm>
#include <cstdint>
#include <limits>

namespace ProjectC::GameCore::Detail
{
    GameEvent Movement(CardInstance const& card, Zone from, Zone to)
    {
        return GameEvent{ .Type = GameEventType::CardMoved,
            .TargetPlayerId = card.OwnerId, .TargetCardId = card.Id,
            .FromZone = from, .ToZone = to,
            .OriginalDefinitionId = card.OriginalDefinitionId,
            .CurrentDefinitionId = card.CurrentDefinitionId };
    }

    void MarkFieldEntry(GameState const& state, PlayerState const& owner, CardInstance& card)
    {
        card.EnteredFieldTurnNumber = state.TurnNumber;
        card.EnteredFieldActivePlayerId = state.ActivePlayerId;
        card.OwnerTurnAtEntry = owner.TurnsStarted;
        card.HasAttackedThisTurn = false;
        card.IgnoreSummoningSickness = false;
    }

    void FinishGame(GameState& state, PlayerId loser, GameEndReason reason, std::vector<GameEvent>& events)
    {
        if (state.Status == GameStatus::Finished) return;
        state.Status = GameStatus::Finished;
        state.EndReason = reason;
        state.WinnerPlayerId = 0;
        for (PlayerState const& player : state.Players)
            if (player.Id != loser && player.Life > 0) state.WinnerPlayerId = player.Id;
        events.push_back(GameEvent{ .Type = GameEventType::GameFinished,
            .WinnerPlayerId = state.WinnerPlayerId, .EndReason = reason });
    }

    CommandError DrawCards(GameState& state, PlayerId id, std::size_t count, std::vector<GameEvent>& events)
    {
        PlayerState* player = FindPlayer(state, id);
        if (player == nullptr) return CommandError::PlayerNotFound;
        std::size_t const drawable = std::min(count, player->Deck.size());
        if (player->Hand.size() > state.Config.HandLimit
            || drawable > state.Config.HandLimit - player->Hand.size()) return CommandError::HandFull;
        for (std::size_t index = 0; index < drawable; ++index)
        {
            CardInstance const card = player->Deck.front();
            player->Hand.push_back(card);
            player->Deck.erase(player->Deck.begin());
            events.push_back(Movement(card, Zone::Deck, Zone::Hand));
        }
        if (drawable < count) FinishGame(state, id, GameEndReason::DeckExhausted, events);
        return CommandError::Success;
    }

    CommandError ChangeLife(GameState& state, PlayerId id, int delta, std::vector<GameEvent>& events)
    {
        PlayerState* player = FindPlayer(state, id);
        if (player == nullptr) return CommandError::PlayerNotFound;
        std::int64_t const total = static_cast<std::int64_t>(player->Life) + delta;
        if (total > std::numeric_limits<int>::max()) return CommandError::InvalidLifeChange;
        int const before = player->Life;
        player->Life = static_cast<int>(std::max<std::int64_t>(0, total));
        events.push_back(GameEvent{ .Type = GameEventType::LifeChanged,
            .TargetPlayerId = id, .LifeBefore = before, .LifeAfter = player->Life });
        if (player->Life == 0) FinishGame(state, id, GameEndReason::LifeDepleted, events);
        return CommandError::Success;
    }
}
