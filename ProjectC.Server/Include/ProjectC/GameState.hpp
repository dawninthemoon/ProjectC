
#pragma once

#include "ProjectC/PlayerState.hpp"
#include "ProjectC/GameConfig.hpp"

#include <array>

namespace ProjectC::GameCore
{
    enum class TurnPhase
    {
        Main,
        Battle,
        End
    };

    enum class BattleResult
    {
        Success,
        NotBattlePhase,
        NotActivePlayer,
        AttackerNotFound,
        DefenderNotFound,
        AttackerDestroyed,
        DefenderDestroyed,
        AlreadyAttacked,
        GameFinished,
        SummoningSickness
    };

    enum class GameStatus { Running, Finished };
    enum class GameEndReason { None, LifeDepleted, DeckExhausted };

    struct ChoiceCandidate
    {
        EffectTarget Target;
        CardInstance Card; // Internal candidate data; never serialize directly.
        bool operator==(ChoiceCandidate const&) const = default;
    };
    struct EffectChoiceRequest
    {
        PlayerId Player = 0;
        CardInstanceId Source = 0;
        int EffectId = 0;
        bool IsCost = false;
        TargetRule Rule;
        std::vector<ChoiceCandidate> Candidates;
        bool operator==(EffectChoiceRequest const&) const = default;
    };
    // Candidate-only execution context, cleared before commit/public projection.
    struct EffectSelectionContext
    {
        bool Enabled = false;
        std::vector<std::vector<EffectTarget>> Selections;
        std::size_t Cursor = 0;
        EffectChoiceRequest Request;
        std::vector<CardInstanceId> CostCards;
        bool operator==(EffectSelectionContext const&) const = default;
    };

    struct GameState
    {
        std::array<PlayerState, 2> Players;
        PlayerId ActivePlayerId = 0;
        int TurnNumber = 1;
        TurnPhase Phase = TurnPhase::Main;
        GameConfig Config;
        std::vector<CardDefinition> Definitions;
        GameStatus Status = GameStatus::Running;
        PlayerId WinnerPlayerId = 0;
        bool TurnStarted = false;
        GameEndReason EndReason = GameEndReason::None;
        EffectSelectionContext SelectionContext;

        bool operator==(GameState const&) const = default;
    };

    [[nodiscard]] PlayerState* FindPlayer(GameState& gameState, PlayerId playerId);
    [[nodiscard]] PlayerState const* FindPlayer(GameState const& gameState, PlayerId playerId);

    // Low-level compatibility primitives. Use ProcessCommand for validated game flow/events.
    // Unknown player IDs leave the game state unchanged.
    void StartTurn(GameState& gameState, PlayerId playerId);
    void AdvancePhase(GameState& gameState);
    void EndTurn(GameState& gameState);

    [[nodiscard]] bool IsSummoningSick(GameState const& gameState, CardInstance const& card);
    [[nodiscard]] BattleResult ValidateAttackSource(GameState const& gameState,
        PlayerId playerId, CardInstanceId cardId);
    [[nodiscard]] BattleResult ValidateAttackReadiness(GameState const& gameState, CardInstance const& card);

    // Low-level battle primitive: retains destroyed cards until command-level cleanup.
    // Missing players, ownership mismatches and friendly targets are reported
    // as AttackerNotFound or DefenderNotFound for the corresponding battle role.
    [[nodiscard]] BattleResult ResolveBattle(
        GameState& gameState,
        PlayerId attackerPlayerId,
        CardInstanceId attackerCardId,
        PlayerId defenderPlayerId,
        CardInstanceId defenderCardId);
}
