using System;
using UnityEngine;

namespace ProjectC.Client
{
    // Historical copies of recipient-visible DTOs only. Never resolves redacted IDs against a catalog.
    public sealed class BattleLogEntry
    {
        public int Turn { get; }
        public EventView Event { get; }
        public CardView Card { get; }
        public CardView Source { get; }
        public CardView Target { get; }
        private readonly string _notice;

        internal BattleLogEntry(int turn, EventView value, Func<int, CardView> visibleCard)
        {
            Turn = turn;
            Event = JsonUtility.FromJson<EventView>(JsonUtility.ToJson(value));
            Source = Copy(value.SourceCardId > 0 ? visibleCard(value.SourceCardId) : null);
            Target = Copy(value.TargetCardId > 0 ? visibleCard(value.TargetCardId) : null);
            switch ((PresentationEvent)value.Type)
            {
                case PresentationEvent.Damage:
                case PresentationEvent.Destroy:
                case PresentationEvent.Move:
                case PresentationEvent.Summon:
                case PresentationEvent.Draw:
                case PresentationEvent.Transform:
                case PresentationEvent.Heal:
                case PresentationEvent.Stats:
                    Card = Target ?? Source;
                    break;
                default:
                    Card = Source ?? Target;
                    break;
            }
        }

        internal BattleLogEntry(int turn, string notice)
        {
            Turn = turn;
            _notice = notice;
        }

        private static CardView Copy(CardView card) => card == null ? null
            : JsonUtility.FromJson<CardView>(JsonUtility.ToJson(card));

        private static string CardName(CardView card) => card == null ? BattleText.Get("ui.log.hidden_card")
            : CardPresentation.Name(card, BattleText.Get("ui.log.card")).Replace("<", "＜").Replace(">", "＞");

        private static string Player(int id, int ownPlayer) => id == 0 ? BattleText.Get("ui.log.system")
            : BattleText.Get(id == ownPlayer ? "ui.log.own" : "ui.log.opponent");

        public string Actor(int ownPlayer) => Event == null ? BattleText.Get("ui.log.system")
            : Player(Event.SourcePlayerId != 0 ? Event.SourcePlayerId : Event.TargetPlayerId, ownPlayer);

        public string Text(int ownPlayer)
        {
            if (Event == null)
            {
                return _notice;
            }
            string source = CardName(Source);
            string target = CardName(Target);
            switch ((PresentationEvent)Event.Type)
            {
                case PresentationEvent.BattleStarted:
                    return BattleText.Get("ui.log.attack", source, Event.TargetCardId == 0 ? Player(Event.TargetPlayerId, ownPlayer) : target);
                case PresentationEvent.Damage: return BattleText.Get("ui.log.damage", target, Event.Damage, Event.HpAfter);
                case PresentationEvent.Destroy: return BattleText.Get("ui.log.destroy", target);
                case PresentationEvent.BattleCompleted: return BattleText.Get("ui.log.battle_end");
                case PresentationEvent.Move:
                    if (Event.FromZone == (int)PresentationZone.Deck && Event.ToZone == (int)PresentationZone.Hand)
                    {
                        return BattleText.Get("ui.log.draw", target);
                    }
                    if (Event.FromZone == (int)PresentationZone.Hand && Event.ToZone == (int)PresentationZone.Field)
                    {
                        return BattleText.Get("ui.log.play", target);
                    }
                    return BattleText.Get("ui.log.move", target, Zone(Event.ToZone));
                case PresentationEvent.TurnStarted: return BattleText.Get("ui.log.turn_start");
                case PresentationEvent.TurnEnded: return BattleText.Get("ui.log.turn_end");
                case PresentationEvent.PhaseChanged:
                    return BattleText.Get("ui.log.phase", BattleText.Get(Event.Phase == (int)BattlePhaseKind.Main1 ? "ui_battle_main_phase_1"
                        : Event.Phase == (int)BattlePhaseKind.Battle ? "ui_battle_battle_phase" : Event.Phase == (int)BattlePhaseKind.Main2 ? "ui_battle_main_phase_2" : "ui_battle_end_phase"));
                case PresentationEvent.Life: return BattleText.Get("ui.log.life", Event.LifeAfter);
                case PresentationEvent.GameEnded: return BattleText.Get("ui.log.game_end");
                case PresentationEvent.PlayerDamage: return BattleText.Get("ui.log.player_damage", Event.Damage);
                case PresentationEvent.Summon: return BattleText.Get("ui.log.summon", target);
                case PresentationEvent.Draw: return BattleText.Get("ui.log.draw", target);
                case PresentationEvent.Effect: return BattleText.Get("ui.log.effect", source);
                case PresentationEvent.EffectResolved: return BattleText.Get("ui.log.resolved");
                case PresentationEvent.Transform: return BattleText.Get("ui.log.transform", target);
                case PresentationEvent.Heal: return BattleText.Get("ui.log.heal", target);
                case PresentationEvent.Stats: return BattleText.Get("ui.log.stats", target);
                case PresentationEvent.Yokai: return BattleText.Get("ui.log.resource", Event.YokaiPowerAfter);
                case PresentationEvent.Spell: return BattleText.Get("ui.log.spell", Source != null ? source : target);
                case PresentationEvent.MulliganCompleted: return BattleText.Get("ui.log.mulligan");
                default: return BattleText.Get("ui.log.event");
            }
        }

        private static string Zone(int zone)
        {
            string[] keys = { "ui.log.deck", "ui.log.hand", "ui.log.field", "ui.log.grave", "ui.log.exile", "ui.log.hidden_zone" };
            return BattleText.Get(zone >= 0 && zone < keys.Length ? keys[zone] : "ui.log.hidden_zone");
        }
    }
}
