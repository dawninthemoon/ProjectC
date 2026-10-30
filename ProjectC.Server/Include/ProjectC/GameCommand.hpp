#pragma once

#include "ProjectC/CardInstance.hpp"
#include "ProjectC/GameEvent.hpp"
#include "ProjectC/GameState.hpp"

#include <vector>
#include <variant>

namespace ProjectC::GameCore
{
    enum class CommandError
    {
        Success, BattleRejected, GameFinished, InvalidState, PlayerNotFound,
        NotActivePlayer, WrongPhase, TurnNotStarted, TurnAlreadyStarted,
        CardNotFound, WrongZone, NotOwner, InvalidCount, DeckEmpty,
        InsufficientCards, HandFull, FieldFull, DrawDisabled, InvalidDefinition,
        InvalidLifeChange, TurnOverflow, NormalSummonLimit, DefenderHasMonsters,
        EffectNotFound, InvalidTarget, InvalidTargetCount, UnsupportedEffect,
        InvalidEffect, InvalidTransformation, TriggerLimitReached
    };

    struct AttackCommand
    {
        PlayerId AttackerPlayerId = 0;
        CardInstanceId AttackerCardId = 0;
        PlayerId DefenderPlayerId = 0;
        CardInstanceId DefenderCardId = 0;
    };

    struct CommandResult
    {
        // Legacy attack detail. Code is the success/failure discriminator for every command.
        BattleResult Result = BattleResult::Success;
        std::vector<GameEvent> Events;
        CommandError Code = CommandError::Success;

        bool operator==(CommandResult const&) const = default;
    };

    // Failed validation leaves the state unchanged and returns no events.
    [[nodiscard]] CommandResult ProcessCommand(
        GameState& gameState,
        AttackCommand const& command);

    struct DrawCommand { PlayerId Player = 0; int Count = 1; };
    struct PlayCardCommand { PlayerId Player = 0; CardInstanceId Card = 0; };
    struct StartTurnCommand { PlayerId Player = 0; };
    struct EndTurnCommand { PlayerId Player = 0; };
    struct AdvancePhaseCommand { PlayerId Player = 0; };
    // Trusted server/effect input, not a player-authorized direct attack.
    struct ChangeLifeCommand { PlayerId Player = 0; int Delta = 0; };
    struct DirectAttackCommand
    {
        PlayerId AttackerPlayerId = 0;
        CardInstanceId AttackerCardId = 0;
        PlayerId DefenderPlayerId = 0;
    };
    struct ActivateEffectCommand
    {
        PlayerId Player = 0;
        CardInstanceId SourceCard = 0;
        int EffectId = 0;
        // One group per definition action. Clients select IDs, never operation amounts/results.
        std::vector<std::vector<EffectTarget>> Targets;
    };
    struct TransformCommand
    {
        PlayerId Player = 0;
        CardInstanceId Card = 0;
        CardDefinitionId Definition = 0;
    }; // Trusted server input, not a player-facing permission to transform freely.

    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, DrawCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, PlayCardCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, StartTurnCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, EndTurnCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, AdvancePhaseCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, ChangeLifeCommand const& command);
    [[nodiscard]] CommandResult ChangePlayerLife(GameState& gameState, PlayerId playerId, int delta);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, DirectAttackCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, ActivateEffectCommand const& command);
    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, TransformCommand const& command);

    using GameCommand = std::variant<AttackCommand, DrawCommand, PlayCardCommand,
        StartTurnCommand, EndTurnCommand, AdvancePhaseCommand, ChangeLifeCommand,
        DirectAttackCommand, ActivateEffectCommand, TransformCommand>;

    [[nodiscard]] CommandResult ProcessCommand(GameState& gameState, GameCommand const& command);
}
