#include "ProjectC/GameState.hpp"

#include <limits>

namespace ProjectC::GameCore
{
    PlayerState* FindPlayer(GameState& gameState, PlayerId playerId)
    {
        for (PlayerState& player : gameState.Players)
        {
            if (player.Id == playerId)
            {
                return &player;
            }
        }
        return nullptr;
    }

    PlayerState const* FindPlayer(GameState const& gameState, PlayerId playerId)
    {
        for (PlayerState const& player : gameState.Players)
        {
            if (player.Id == playerId)
            {
                return &player;
            }
        }
        return nullptr;
    }

    void StartTurn(GameState& gameState, PlayerId playerId)
    {
        if (gameState.Status == GameStatus::Finished) return;
        PlayerState* player = FindPlayer(gameState, playerId);
        if (player == nullptr || player->TurnsStarted == std::numeric_limits<std::uint64_t>::max())
        {
            return;
        }
        bool const newTurn = !gameState.TurnStarted || gameState.ActivePlayerId != playerId;
        gameState.ActivePlayerId = playerId;
        gameState.Phase = TurnPhase::Main;
        gameState.TurnStarted = true;
        if (newTurn)
        {
            ++player->TurnsStarted;
            player->NormalSummonsThisTurn = 0;
            for(auto& owner:gameState.Players) { owner.EffectUses.clear();owner.GraveyardArchetypesThisTurn.clear(); }
        }
        for (CardInstance& card : player->Field)
        {
            if (!card.IsDestroyed())
            {
                card.HasAttackedThisTurn = false;
            }
        }
    }

    void AdvancePhase(GameState& gameState)
    {
        if (gameState.Status == GameStatus::Finished) return;
        switch (gameState.Phase)
        {
        case TurnPhase::Main:
            gameState.Phase = TurnPhase::Battle;
            break;
        case TurnPhase::Battle:
            gameState.Phase = TurnPhase::End;
            break;
        case TurnPhase::End:
            break;
        }
    }

    void EndTurn(GameState& gameState)
    {
        if (gameState.Status == GameStatus::Finished) return;
        if (gameState.TurnNumber == std::numeric_limits<int>::max()) return;
        PlayerState const* activePlayer = FindPlayer(gameState, gameState.ActivePlayerId);
        if (activePlayer == nullptr)
        {
            return;
        }
        for (PlayerState const& player : gameState.Players)
        {
            if (player.Id != activePlayer->Id)
            {
                ++gameState.TurnNumber;
                StartTurn(gameState, player.Id);
                return;
            }
        }
    }

    bool IsSummoningSick(GameState const& gameState, CardInstance const& card)
    {
        PlayerState const* owner = FindPlayer(gameState, card.OwnerId);
        return gameState.Config.SummoningSickness && !card.IgnoreSummoningSickness && card.EnteredFieldTurnNumber != 0
            && (owner == nullptr || owner->TurnsStarted <= card.OwnerTurnAtEntry);
    }

    BattleResult ValidateAttackSource(GameState const& gameState, PlayerId playerId, CardInstanceId cardId)
    {
        if (gameState.Status == GameStatus::Finished) return BattleResult::GameFinished;
        if (gameState.Phase != TurnPhase::Battle) return BattleResult::NotBattlePhase;
        if (playerId != gameState.ActivePlayerId) return BattleResult::NotActivePlayer;
        PlayerState const* player = FindPlayer(gameState, playerId);
        CardInstance const* card = player == nullptr ? nullptr : FindCard(*player, cardId);
        if (card == nullptr || card->OwnerId != playerId) return BattleResult::AttackerNotFound;
        return BattleResult::Success;
    }

    BattleResult ValidateAttackReadiness(GameState const& gameState, CardInstance const& card)
    {
        if (card.IsDestroyed()) return BattleResult::AttackerDestroyed;
        if (card.HasAttackedThisTurn) return BattleResult::AlreadyAttacked;
        if (IsSummoningSick(gameState, card)) return BattleResult::SummoningSickness;
        return BattleResult::Success;
    }

    BattleResult ResolveBattle(
        GameState& gameState,
        PlayerId attackerPlayerId,
        CardInstanceId attackerCardId,
        PlayerId defenderPlayerId,
        CardInstanceId defenderCardId)
    {
        BattleResult const sourceResult = ValidateAttackSource(gameState, attackerPlayerId, attackerCardId);
        if (sourceResult != BattleResult::Success) return sourceResult;
        PlayerState* attackerPlayer = FindPlayer(gameState, attackerPlayerId);
        CardInstance* attacker = attackerPlayer == nullptr
            ? nullptr : FindCard(*attackerPlayer, attackerCardId);
        if (attacker == nullptr || attacker->OwnerId != attackerPlayerId)
        {
            return BattleResult::AttackerNotFound;
        }
        PlayerState* defenderPlayer = FindPlayer(gameState, defenderPlayerId);
        CardInstance* defender = defenderPlayer == nullptr
            ? nullptr : FindCard(*defenderPlayer, defenderCardId);
        if (defender == nullptr || defender->OwnerId != defenderPlayerId
            || defenderPlayerId == attackerPlayerId)
        {
            return BattleResult::DefenderNotFound;
        }
        if (attacker->IsDestroyed())
        {
            return BattleResult::AttackerDestroyed;
        }
        if (defender->IsDestroyed())
        {
            return BattleResult::DefenderDestroyed;
        }
        BattleResult const readiness = ValidateAttackReadiness(gameState, *attacker);
        if (readiness != BattleResult::Success) return readiness;

        int const attackerDamage = attacker->Attack;
        int const defenderDamage = defender->Attack;
        ApplyDamage(*defender, attackerDamage);
        ApplyDamage(*attacker, defenderDamage);
        attacker->HasAttackedThisTurn = true;
        return BattleResult::Success;
    }
}
