
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
        GameFinished
    };

    enum class GameStatus { Running, Finished };

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

        bool operator==(GameState const&) const = default;
    };

    [[nodiscard]] PlayerState* FindPlayer(GameState& gameState, PlayerId playerId);
    [[nodiscard]] PlayerState const* FindPlayer(GameState const& gameState, PlayerId playerId);

    // Low-level compatibility primitives. Use ProcessCommand for validated game flow/events.
    // Unknown player IDs leave the game state unchanged.
    void StartTurn(GameState& gameState, PlayerId playerId);
    void AdvancePhase(GameState& gameState);
    void EndTurn(GameState& gameState);

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
