
#pragma once

#include "ProjectC/GameConfig.hpp"
#include "ProjectC/PlayerState.hpp"

#include <array>

namespace ProjectC::GameCore
{
enum class TurnPhase
{
    Main,
    Battle,
    End,
    Main2 // Appended: preserve existing wire values Main=0, Battle=1, End=2.
};

[[nodiscard]] constexpr bool IsMainPhase(TurnPhase phase)
{
    return phase == TurnPhase::Main || phase == TurnPhase::Main2;
}

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

enum class GameStatus
{
    Running,
    Finished
};
enum class GameEndReason
{
    None,
    LifeDepleted,
    DeckExhausted
};

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

enum class PreparationPhase
{
    Playing,
    Mulligan
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
    PreparationPhase Preparation = PreparationPhase::Playing;
    std::array<std::uint64_t, 2>
        MulliganRandom{}; // Server-private deterministic streams, never a network DTO.

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
[[nodiscard]] BattleResult
ValidateAttackSource(GameState const& gameState, PlayerId playerId, CardInstanceId cardId);
[[nodiscard]] BattleResult ValidateAttackReadiness(GameState const& gameState,
                                                   CardInstance const& card);

// Low-level battle primitive: retains destroyed cards until command-level cleanup.
// Missing players, ownership mismatches and friendly targets are reported
// as AttackerNotFound or DefenderNotFound for the corresponding battle role.
[[nodiscard]] BattleResult ResolveBattle(GameState& gameState,
                                         PlayerId attackerPlayerId,
                                         CardInstanceId attackerCardId,
                                         PlayerId defenderPlayerId,
                                         CardInstanceId defenderCardId);
} // namespace ProjectC::GameCore
