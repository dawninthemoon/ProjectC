using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    // Numeric values are the existing protocol v2 event/zone values, not new rules.
    internal enum PresentationEvent
    {
        Damage = 1, Destroy = 2, Move = 4, Life = 8, PlayerDamage = 10,
        Summon = 11, Effect = 13, Transform = 15, Heal = 16, Stats = 17, Spell = 19
    }

    internal enum PresentationZone
    {
        Deck, Hand, Field, Graveyard, Exile, HiddenExile
    }

    // This is a disposable visual projection of server DTOs. It never validates commands,
    // predicts rules, or mutates the authoritative StateView retained by the network client.
    internal sealed class BattlePresentationState
    {
        public StateView State { get; private set; }

        public void Reset(StateView snapshot)
        {
            State = snapshot == null ? null : JsonUtility.FromJson<StateView>(JsonUtility.ToJson(snapshot));
        }

        public PlayerView Player(int id)
        {
            return State?.Players == null ? null : Array.Find(State.Players, p => p.PlayerId == id);
        }

        public void RefreshActions(StateView authoritative)
        {
            if (State?.Players == null)
            {
                return;
            }
            foreach (var player in State.Players)
            {
                foreach (var zone in new[] { player.Hand, player.Field })
                {
                    if (zone == null)
                    {
                        continue;
                    }
                    foreach (var card in zone)
                    {
                        var current = Find(authoritative, card.Id);
                        card.CanSummon = current?.CanSummon ?? false;
                        card.CanAttack = current?.CanAttack ?? false;
                        card.CanDirectAttack = current?.CanDirectAttack ?? false;
                        card.Activations = current?.Activations;
                    }
                }
            }
        }

        public static bool IsDraw(EventView e)
        {
            return e.Type == (int)PresentationEvent.Move
                && e.FromZone == (int)PresentationZone.Deck && e.ToZone == (int)PresentationZone.Hand;
        }

        public static bool IsStateChange(EventView e)
        {
            switch ((PresentationEvent)e.Type)
            {
                case PresentationEvent.Move:
                case PresentationEvent.Damage:
                case PresentationEvent.Destroy:
                case PresentationEvent.Summon:
                case PresentationEvent.Transform:
                case PresentationEvent.Heal:
                case PresentationEvent.Stats:
                case PresentationEvent.Life:
                case PresentationEvent.PlayerDamage:
                    return true;
                default:
                    return false;
            }
        }

        public static CardView Find(StateView state, int id)
        {
            if (id == 0 || state?.Players == null)
            {
                return null;
            }
            foreach (var player in state.Players)
            {
                foreach (var zone in new[] { player.Hand, player.Field, player.Graveyard, player.Exile })
                {
                    var card = zone == null ? null : Array.Find(zone, c => c.Id == id);
                    if (card != null)
                    {
                        return card;
                    }
                }
            }
            return null;
        }

        public void Apply(EventView e, StateView snapshot, int ownPlayer)
        {
            var player = Player(e.TargetPlayerId);
            var card = Find(State, e.TargetCardId);
            switch ((PresentationEvent)e.Type)
            {
                case PresentationEvent.Move:
                    if (player == null)
                    {
                        return;
                    }
                    Move(player, e, snapshot, ownPlayer);
                    break;
                case PresentationEvent.Damage:
                case PresentationEvent.Heal:
                    if (card != null)
                    {
                        card.Hp = e.HpAfter;
                    }
                    break;
                case PresentationEvent.Destroy:
                    if (card != null)
                    {
                        card.Hp = 0;
                    }
                    break;
                case PresentationEvent.Stats:
                case PresentationEvent.Transform:
                    if (card != null)
                    {
                        var after = Find(snapshot, e.TargetCardId);
                        // Only copy a definition supplied by the server. A subsequent transform
                        // in this batch may have a different definition than this event.
                        if (after != null && after.DefinitionId == e.DefinitionId)
                        {
                            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(after), card);
                        }
                        card.Attack = e.AttackAfter;
                        card.Hp = e.HpAfter;
                    }
                    break;
                case PresentationEvent.Life:
                case PresentationEvent.PlayerDamage:
                    if (player != null)
                    {
                        player.Life = e.LifeAfter;
                    }
                    break;
            }
        }

        private void Move(PlayerView player, EventView e, StateView snapshot, int ownPlayer)
        {
            var from = (PresentationZone)e.FromZone;
            var to = (PresentationZone)e.ToZone;
            var card = Find(State, e.TargetCardId) ?? Find(snapshot, e.TargetCardId);
            // An own card can leave all visible zones later in this same batch. Its ID
            // is enough for an anonymous visual; never infer its private definition.
            if (card == null && e.TargetCardId != 0 && player.PlayerId == ownPlayer)
            {
                card = new CardView { Id = e.TargetCardId };
            }
            var source = GetZone(player, from);
            if (source != null)
            {
                source.RemoveAll(c => c.Id == e.TargetCardId);
                SetZone(player, from, source.ToArray());
            }
            if (from == PresentationZone.Hand)
            {
                player.HandCount = Mathf.Max(0, player.HandCount - 1);
            }
            if (from == PresentationZone.Deck)
            {
                player.DeckCount = Mathf.Max(0, player.DeckCount - 1);
            }
            if (to == PresentationZone.Hand)
            {
                ++player.HandCount;
            }
            if (to == PresentationZone.Deck)
            {
                ++player.DeckCount;
            }
            // Opponent hand/deck identities are never retained, even when a public card
            // returns there. Hidden cards use count-only widgets.
            if (card != null && !(to == PresentationZone.Hand && player.PlayerId != ownPlayer))
            {
                var destination = GetZone(player, to);
                if (destination != null && !destination.Exists(c => c.Id == card.Id))
                {
                    destination.Add(JsonUtility.FromJson<CardView>(JsonUtility.ToJson(card)));
                    SetZone(player, to, destination.ToArray());
                }
            }
        }

        private static List<CardView> GetZone(PlayerView player, PresentationZone zone)
        {
            CardView[] cards;
            switch (zone)
            {
                case PresentationZone.Hand:
                    cards = player.Hand;
                    break;
                case PresentationZone.Field:
                    cards = player.Field;
                    break;
                case PresentationZone.Graveyard:
                    cards = player.Graveyard;
                    break;
                case PresentationZone.Exile:
                    cards = player.Exile;
                    break;
                default:
                    return null;
            }
            return new List<CardView>(cards ?? Array.Empty<CardView>());
        }

        private static void SetZone(PlayerView player, PresentationZone zone, CardView[] cards)
        {
            switch (zone)
            {
                case PresentationZone.Hand:
                    player.Hand = cards;
                    break;
                case PresentationZone.Field:
                    player.Field = cards;
                    break;
                case PresentationZone.Graveyard:
                    player.Graveyard = cards;
                    break;
                case PresentationZone.Exile:
                    player.Exile = cards;
                    break;
            }
        }
    }
}
