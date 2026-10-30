#include "ProjectC/GameCommand.hpp"
#include "ProjectC/GameInitializer.hpp"
#include "GameOperations.hpp"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <utility>

namespace ProjectC::GameCore
{
    namespace
    {
        CommandResult Reject(CommandError code) { return CommandResult{ .Code = code }; }

        template <typename Action>
        CommandResult ApplyCommand(GameState& state, Action action)
        {
            if (state.Status == GameStatus::Finished) return Reject(CommandError::GameFinished);
            if (!ValidateGameState(state))
                return Reject(CommandError::InvalidState);
            GameState candidate = state;
            CommandResult result = action(candidate);
            return Detail::CommitCommand(state, std::move(candidate), std::move(result));
        }

        CommandError CheckPlayer(GameState const& state, PlayerId id, bool requireStarted = true)
        {
            if (FindPlayer(state, id) == nullptr) return CommandError::PlayerNotFound;
            if (state.ActivePlayerId != id) return CommandError::NotActivePlayer;
            if (requireStarted && !state.TurnStarted) return CommandError::TurnNotStarted;
            return CommandError::Success;
        }

        GameEvent Movement(CardInstance const& card, Zone from, Zone to)
        {
            return GameEvent{ .Type = GameEventType::CardMoved,
                .TargetPlayerId = card.OwnerId, .TargetCardId = card.Id,
                .FromZone = from, .ToZone = to,
                .OriginalDefinitionId = card.OriginalDefinitionId,
                .CurrentDefinitionId = card.CurrentDefinitionId };
        }

        CommandError CheckDraw(GameState const& state, PlayerState const& player, std::size_t count)
        {
            if (count == 0) return CommandError::Success;
            count = std::min(count, player.Deck.size());
            if (player.Hand.size() > state.Config.HandLimit
                || count > state.Config.HandLimit - player.Hand.size()) return CommandError::HandFull;
            return CommandError::Success;
        }

    }

    CommandResult ProcessCommand(GameState& state, DrawCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandError const playerError = CheckPlayer(candidate, command.Player);
            if (playerError != CommandError::Success) return Reject(playerError);
            if (candidate.Phase != TurnPhase::Main) return Reject(CommandError::WrongPhase);
            if (!candidate.Config.AllowManualDraw) return Reject(CommandError::DrawDisabled);
            if (command.Count <= 0) return Reject(CommandError::InvalidCount);
            PlayerState& player = *FindPlayer(candidate, command.Player);
            CommandError const drawError = CheckDraw(candidate, player, static_cast<std::size_t>(command.Count));
            if (drawError != CommandError::Success) return Reject(drawError);
            CommandResult result;
            result.Code = Detail::DrawCards(candidate, player.Id, static_cast<std::size_t>(command.Count), result.Events);
            return result;
        });
    }

    CommandResult ProcessCommand(GameState& state, PlayCardCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandError const playerError = CheckPlayer(candidate, command.Player);
            if (playerError != CommandError::Success) return Reject(playerError);
            if (candidate.Phase != TurnPhase::Main) return Reject(CommandError::WrongPhase);
            PlayerState& player = *FindPlayer(candidate, command.Player);
            auto const card = std::find_if(player.Hand.begin(), player.Hand.end(),
                [&](CardInstance const& entry) { return entry.Id == command.Card; });
            if (card == player.Hand.end())
            {
                for (PlayerState const& owner : candidate.Players)
                    for (Zone const zone : { Zone::Deck, Zone::Hand, Zone::Field, Zone::Graveyard })
                        for (CardInstance const& entry : *GetZone(owner, zone))
                            if (entry.Id == command.Card)
                                return Reject(owner.Id != command.Player
                                    ? CommandError::NotOwner : CommandError::WrongZone);
                return Reject(CommandError::CardNotFound);
            }
            CardDefinition const* definition = FindDefinition(candidate, card->CurrentDefinitionId);
            if (definition == nullptr || definition->Type != CardType::Monster || card->IsDestroyed())
                return Reject(CommandError::InvalidDefinition);
            if (player.Field.size() >= candidate.Config.FieldLimit) return Reject(CommandError::FieldFull);
            if (player.NormalSummonsThisTurn >= 1) return Reject(CommandError::NormalSummonLimit);
            CardInstance summoned = *card;
            Detail::MarkFieldEntry(candidate, player, summoned);
            player.Field.push_back(summoned);
            player.Hand.erase(card);
            ++player.NormalSummonsThisTurn;
            return CommandResult{ .Events = { Movement(summoned, Zone::Hand, Zone::Field),
                GameEvent{ .Type = GameEventType::CardSummoned,
                    .TargetPlayerId = player.Id, .TargetCardId = summoned.Id,
                    .CurrentDefinitionId = summoned.CurrentDefinitionId } } };
        });
    }

    CommandResult ProcessCommand(GameState& state, StartTurnCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandError const playerError = CheckPlayer(candidate, command.Player, false);
            if (playerError != CommandError::Success) return Reject(playerError);
            if (candidate.TurnStarted) return Reject(CommandError::TurnAlreadyStarted);
            PlayerState& player = *FindPlayer(candidate, command.Player);
            if (player.TurnsStarted == std::numeric_limits<std::uint64_t>::max())
                return Reject(CommandError::TurnOverflow);
            CommandError const drawError = CheckDraw(candidate, player, candidate.Config.DrawPerTurn);
            if (drawError != CommandError::Success) return Reject(drawError);
            StartTurn(candidate, command.Player);
            CommandResult result{ .Events = { GameEvent{ .Type = GameEventType::TurnStarted,
                .TargetPlayerId = command.Player, .TurnNumber = candidate.TurnNumber,
                .Phase = candidate.Phase } } };
            result.Code = Detail::DrawCards(candidate, player.Id, candidate.Config.DrawPerTurn, result.Events);
            return result;
        });
    }

    CommandResult ProcessCommand(GameState& state, AdvancePhaseCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandError const playerError = CheckPlayer(candidate, command.Player);
            if (playerError != CommandError::Success) return Reject(playerError);
            if (candidate.Phase == TurnPhase::End) return Reject(CommandError::WrongPhase);
            AdvancePhase(candidate);
            return CommandResult{ .Events = { GameEvent{ .Type = GameEventType::PhaseChanged,
                .TargetPlayerId = command.Player, .TurnNumber = candidate.TurnNumber,
                .Phase = candidate.Phase } } };
        });
    }

    CommandResult ProcessCommand(GameState& state, EndTurnCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandError const playerError = CheckPlayer(candidate, command.Player);
            if (playerError != CommandError::Success) return Reject(playerError);
            if (candidate.Phase != TurnPhase::End) return Reject(CommandError::WrongPhase);
            if (candidate.TurnNumber == std::numeric_limits<int>::max()) return Reject(CommandError::TurnOverflow);
            CommandResult result{ .Events = { GameEvent{ .Type = GameEventType::TurnEnded,
                .TargetPlayerId = command.Player, .TurnNumber = candidate.TurnNumber,
                .Phase = candidate.Phase } } };
            for (PlayerState const& player : candidate.Players)
                if (player.Id != command.Player) { candidate.ActivePlayerId = player.Id; break; }
            ++candidate.TurnNumber;
            candidate.Phase = TurnPhase::Main;
            candidate.TurnStarted = false;
            return result;
        });
    }

    CommandResult ProcessCommand(GameState& state, ChangeLifeCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            CommandResult result;
            result.Code = Detail::ChangeLife(candidate, command.Player, command.Delta, result.Events);
            return result;
        });
    }

    CommandResult ChangePlayerLife(GameState& gameState, PlayerId playerId, int delta)
    {
        return ProcessCommand(gameState, ChangeLifeCommand{ playerId, delta });
    }

    CommandResult ProcessCommand(GameState& state, DirectAttackCommand const& command)
    {
        return ApplyCommand(state, [&](GameState& candidate)
        {
            if (!candidate.TurnStarted) return Reject(CommandError::TurnNotStarted);
            BattleResult battle = ValidateAttackSource(candidate, command.AttackerPlayerId, command.AttackerCardId);
            if (battle != BattleResult::Success)
                return CommandResult{ .Result = battle, .Code = CommandError::BattleRejected };
            PlayerState* defender = FindPlayer(candidate, command.DefenderPlayerId);
            if (defender == nullptr || defender->Id == command.AttackerPlayerId)
                return CommandResult{ .Result = BattleResult::DefenderNotFound, .Code = CommandError::BattleRejected };
            CardInstance& attacker = *FindCard(*FindPlayer(candidate, command.AttackerPlayerId), command.AttackerCardId);
            battle = ValidateAttackReadiness(candidate, attacker);
            if (battle != BattleResult::Success)
                return CommandResult{ .Result = battle, .Code = CommandError::BattleRejected };
            for (CardInstance const& card : defender->Field)
                if (!card.IsDestroyed()) return Reject(CommandError::DefenderHasMonsters);
            int const damage = std::max(0, attacker.Attack);
            int const lifeAfter = std::max(0, defender->Life - damage);
            attacker.HasAttackedThisTurn = true;
            CommandResult result{ .Events = { GameEvent{ .Type = GameEventType::PlayerDamaged,
                .SourcePlayerId = command.AttackerPlayerId, .SourceCardId = command.AttackerCardId,
                .TargetPlayerId = defender->Id, .Damage = damage,
                .LifeBefore = defender->Life, .LifeAfter = lifeAfter } } };
            result.Code = Detail::ChangeLife(candidate, defender->Id, -damage, result.Events);
            return result;
        });
    }
}
