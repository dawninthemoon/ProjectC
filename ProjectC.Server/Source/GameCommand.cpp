#include "ProjectC/GameCommand.hpp"

#include <algorithm>
#include <utility>

namespace ProjectC::GameCore
{
    CommandResult ProcessCommand(GameState& gameState, AttackCommand const& command)
    {
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
            return CommandResult{ .Result = battleResult, .Events = {} };
        }

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

        return CommandResult{ .Result = BattleResult::Success, .Events = std::move(events) };
    }
}
