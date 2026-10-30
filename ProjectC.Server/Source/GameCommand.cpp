#include "ProjectC/GameCommand.hpp"
#include "ProjectC/GameInitializer.hpp"

#include <algorithm>
#include <utility>

namespace ProjectC::GameCore
{
    static CommandResult ExecuteAttack(GameState& gameState, AttackCommand const& command)
    {
        if (gameState.Status != GameStatus::Finished && !gameState.TurnStarted)
            return CommandResult{ .Code = CommandError::TurnNotStarted };
        PlayerState const* attackerPlayer = FindPlayer(gameState, command.AttackerPlayerId);
        PlayerState const* defenderPlayer = FindPlayer(gameState, command.DefenderPlayerId);
        CardInstance const* attacker = attackerPlayer == nullptr
            ? nullptr : FindCard(*attackerPlayer, command.AttackerCardId);
        CardInstance const* defender = defenderPlayer == nullptr
            ? nullptr : FindCard(*defenderPlayer, command.DefenderCardId);

        // Snapshot damage before the authoritative battle applies either hit.
        int const attackerDamage = attacker == nullptr ? 0 : std::max(0, attacker->Attack);
        int const defenderDamage = defender == nullptr ? 0 : std::max(0, defender->Attack);

        BattleResult const battleResult = ResolveBattle(gameState,
            command.AttackerPlayerId, command.AttackerCardId,
            command.DefenderPlayerId, command.DefenderCardId);
        if (battleResult != BattleResult::Success)
        {
            return CommandResult{ .Result = battleResult, .Events = {},
                .Code = battleResult == BattleResult::GameFinished
                    ? CommandError::GameFinished : CommandError::BattleRejected };
        }
        if (!ValidateGameState(gameState))
            return CommandResult{ .Code = CommandError::InvalidState };

        // ResolveBattle guarantees both cards exist and retains them in their fields.
        // These pointers therefore also expose the final HP after simultaneous damage.
        std::vector<GameEvent> events;
        events.reserve(6);
        events.push_back(GameEvent{
            .Type = GameEventType::BattleStarted,
            .SourcePlayerId = command.AttackerPlayerId,
            .SourceCardId = command.AttackerCardId,
            .TargetPlayerId = command.DefenderPlayerId,
            .TargetCardId = command.DefenderCardId });
        events.push_back(GameEvent{
            .Type = GameEventType::CardDamaged,
            .SourcePlayerId = command.DefenderPlayerId,
            .SourceCardId = command.DefenderCardId,
            .TargetPlayerId = command.AttackerPlayerId,
            .TargetCardId = command.AttackerCardId,
            .Damage = defenderDamage,
            .HpAfter = attacker->Hp });
        events.push_back(GameEvent{
            .Type = GameEventType::CardDamaged,
            .SourcePlayerId = command.AttackerPlayerId,
            .SourceCardId = command.AttackerCardId,
            .TargetPlayerId = command.DefenderPlayerId,
            .TargetCardId = command.DefenderCardId,
            .Damage = attackerDamage,
            .HpAfter = defender->Hp });
        if (attacker->IsDestroyed())
        {
            events.push_back(GameEvent{
                .Type = GameEventType::CardDestroyed,
                .TargetPlayerId = command.AttackerPlayerId,
                .TargetCardId = command.AttackerCardId,
                .HpAfter = attacker->Hp });
        }
        if (defender->IsDestroyed())
        {
            events.push_back(GameEvent{
                .Type = GameEventType::CardDestroyed,
                .TargetPlayerId = command.DefenderPlayerId,
                .TargetCardId = command.DefenderCardId,
                .HpAfter = defender->Hp });
        }
        events.push_back(GameEvent{
            .Type = GameEventType::BattleCompleted,
            .SourcePlayerId = command.AttackerPlayerId,
            .SourceCardId = command.AttackerCardId,
            .TargetPlayerId = command.DefenderPlayerId,
            .TargetCardId = command.DefenderCardId });

        // Snapshot IDs before moving either vector element; zone changes invalidate pointers.
        bool const attackerDestroyed = attacker->IsDestroyed();
        bool const defenderDestroyed = defender->IsDestroyed();
        for (auto const& entry : { std::pair{ command.AttackerPlayerId, command.AttackerCardId },
            std::pair{ command.DefenderPlayerId, command.DefenderCardId } })
        {
            bool const destroyed = entry.first == command.AttackerPlayerId
                ? attackerDestroyed : defenderDestroyed;
            if (!destroyed) continue;
            if (MoveCard(gameState, entry.first, entry.second, Zone::Field, Zone::Graveyard)
                != ZoneResult::Success)
                return CommandResult{ .Code = CommandError::InvalidState };
            CardInstance const& moved = FindPlayer(gameState, entry.first)->Graveyard.back();
            events.push_back(GameEvent{ .Type = GameEventType::CardMoved,
                .TargetPlayerId = entry.first, .TargetCardId = entry.second,
                .FromZone = Zone::Field, .ToZone = Zone::Graveyard,
                .OriginalDefinitionId = moved.OriginalDefinitionId,
                .CurrentDefinitionId = moved.CurrentDefinitionId });
        }

        return CommandResult{ .Result = BattleResult::Success, .Events = std::move(events) };
    }

    CommandResult ProcessCommand(GameState& gameState, AttackCommand const& command)
    {
        GameState candidate = gameState;
        CommandResult result = ExecuteAttack(candidate, command);
        if (result.Code == CommandError::Success) gameState = std::move(candidate);
        return result;
    }
}
