#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public sealed class DrawPresentationPlayModeTests
    {
        private GameObject _instance;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;
        private long _sequence = 1;

        private static CardView Card(int id)
        {
            return new CardView { Id = id, DefinitionId = 10, Name = "Draw " + id,
                Hp = 5, MaxHp = 5, Attack = 3, RulesText = "Draw a card." };
        }

        private static StateView State(params int[] hand)
        {
            return new StateView
            {
                Started = true, TurnStarted = true, TurnNumber = 1, ActivePlayerId = 1, FieldLimit = 5,
                Players = new[]
                {
                    new PlayerView { PlayerId = 1, Life = 3, HandCount = hand.Length,
                        Hand = hand.Select(Card).ToArray(), DeckCount = 20 - hand.Length,
                        Field = new[] { Card(100) }, Graveyard = Array.Empty<CardView>() },
                    new PlayerView { PlayerId = 2, Life = 3, HandCount = 2, DeckCount = 18,
                        Hand = Array.Empty<CardView>(), Field = Array.Empty<CardView>(), Graveyard = Array.Empty<CardView>() }
                }
            };
        }

        private static EventView Draw(int id, int player = 1)
        {
            return new EventView { Type = 4, TargetPlayerId = player, TargetCardId = id, FromZone = 0, ToZone = 1 };
        }

        private void Snapshot(StateView state)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "draw-test",
                GameNumber = 1, SequenceNumber = _sequence, Payload = state
            }));
        }

        private void Events(params EventView[] events)
        {
            ++_sequence;
            _client.ApplyMessage(JsonUtility.ToJson(new EventsMessage
            {
                Type = "GameEvents", ProtocolVersion = 2, MatchId = "draw-test",
                GameNumber = 1, SequenceNumber = _sequence, Payload = events
            }));
        }

        private IEnumerator Setup(StateView state)
        {
            _sequence = 1;
            _instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Battle/BattleScreen.prefab"));
            _ui = _instance.GetComponent<BattlePresenter>();
            _client = _ui.Client;
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"draw-test\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(state);
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator Settled()
        {
            float deadline = Time.realtimeSinceStartup + 6;
            do
            {
                yield return null;
                Assert.Less(Time.realtimeSinceStartup, deadline, "Presentation must finish without blocking state reception");
            }
            while (_ui.PresentationBusy || _ui.Effects.ActiveDraws > 0);
            yield return new WaitForSecondsRealtime(.35f);
        }

        private void AssertSlots(CardZoneView zone)
        {
            for (int i = 0; i < zone.Widgets.Count; ++i)
            {
                var widget = zone.Widgets[i];
                var rect = (RectTransform)widget.transform;
                zone.Layout.GetSlot(i, zone.Widgets.Count, out var position, out var rotation);
                Assert.IsFalse(widget.IsInFlight);
                Assert.AreSame(zone.Content, rect.parent);
                Assert.Less(Vector2.Distance(position, rect.anchoredPosition), .1f);
                Assert.Less(Quaternion.Angle(rotation, rect.localRotation), .1f);
                if (i > 0)
                {
                    Assert.Greater(rect.GetSiblingIndex(), zone.Widgets[i - 1].transform.GetSiblingIndex());
                }
            }
        }

        [TearDown]
        public void Cleanup()
        {
            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
            }
        }

        [UnityTest]
        public IEnumerator DrawOriginIsIndependentOfDeckHudAndRetargetsExpandedHand()
        {
            yield return Setup(State(1, 2, 3, 4, 5));
            var canvas = (RectTransform)_ui.GetComponent<Canvas>().transform;
            Vector3 deckBefore = _ui.Effects.OwnDeck.position;
            Events(Draw(6));
            Snapshot(State(1, 2, 3, 4, 5, 6));
            yield return null;
            yield return null;
            var drawn = _ui.Hand.Widgets.Single(widget => widget.Card?.Id == 6);
            Assert.IsTrue(drawn.IsInFlight);
            Vector3 departure = canvas.InverseTransformPoint(drawn.transform.position);
            Vector3 handCenter = canvas.InverseTransformPoint(_ui.Hand.Content.TransformPoint(_ui.Hand.Content.rect.center));
            Assert.Greater(departure.x, canvas.rect.xMax - 80, "Draw must enter from the right viewport edge.");
            Assert.Less(Mathf.Abs(departure.y - handCenter.y), 80, "Draw must originate at hand height, not the top HUD.");
            Assert.AreEqual(deckBefore, _ui.Effects.OwnDeck.position, "Visual origin must not move the deck HUD.");
            _ui.SetHandExpanded(true);
            yield return Settled();
            AssertSlots(_ui.Hand);
            Assert.AreSame(drawn, _ui.Hand.Widgets.Single(widget => widget.Card?.Id == 6));
        }

        private static readonly int[] HandCounts = { 0, 1, 5, 9 };

        [UnityTest]
        public IEnumerator DrawKeepsIdentityAndLandsInActualSlot([ValueSource(nameof(HandCounts))] int count)
        {
            var ids = Enumerable.Range(1, count).ToArray();
            yield return Setup(State(ids));
            var originals = _ui.Hand.Widgets.ToArray();
            var positions = originals.Select(w => w.transform.position).ToArray();
            var after = State(ids.Concat(new[] { 50 }).ToArray());
            Events(new EventView { Type = 13, SourceCardId = 100 }, Draw(50));
            Snapshot(after);
            Assert.AreEqual(count + 1, _client.State.Players[0].Hand.Length);
            Assert.AreEqual(count, _ui.Hand.Widgets.Count, "Snapshot must not add a card before presentation starts");
            yield return null;
            yield return null;
            var drawn = _ui.Hand.Widgets.Single(w => w.Card?.Id == 50);
            Assert.IsTrue(drawn.IsInFlight);
            Assert.Greater(_ui.Effects.PopupsPresented, 0, "Description must start alongside draw");
            Assert.AreEqual(BattleText.Get("ui.effect.activate") + "\nDraw a card.", _ui.Effects.PopupBody.text);
            Assert.IsFalse(drawn.GetComponent<CanvasGroup>().blocksRaycasts);
            drawn.OnPointerEnter(null);
            Assert.IsFalse(drawn.IsHovered, "Flying card cannot acquire a hover transform");
            Assert.AreEqual(1, _instance.GetComponentsInChildren<CardWidget>().Count(w => w.Card?.Id == 50));
            for (int i = 0; i < originals.Length; ++i)
            {
                Assert.AreSame(originals[i], _ui.Hand.Widgets[i]);
                Assert.Less(Vector3.Distance(positions[i], originals[i].transform.position), 70,
                    "Existing cards must not jump immediately to their new slots");
            }
            yield return Settled();
            Assert.AreSame(drawn, _ui.Hand.Widgets.Single(w => w.Card?.Id == 50));
            Assert.AreEqual(count + 1, _ui.Hand.Widgets.Count);
            AssertSlots(_ui.Hand);
        }

        [UnityTest]
        public IEnumerator OverlappingDrawsKeepSortingHoverAndSnapshotBatches()
        {
            yield return Setup(State(1, 2, 3, 4, 5));
            var first = _ui.Hand.Widgets[0];
            first.OnPointerEnter(null);
            Events(new EventView { Type = 13, SourceCardId = 100 }, Draw(6), Draw(7));
            Snapshot(State(1, 2, 3, 4, 5, 6, 7));
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(2, _ui.Effects.ActiveDraws);
            // A newer authoritative snapshot must not replace either flying widget.
            var sixth = _ui.Hand.Widgets.Single(w => w.Card?.Id == 6);
            Events(Draw(8));
            Snapshot(State(2, 3, 4, 5, 6, 7, 8));
            Assert.AreEqual(8, _client.State.Players[0].Hand.Last().Id);
            Assert.IsFalse(_ui.Hand.Widgets.Any(w => w.Card?.Id == 8));
            Assert.AreSame(sixth, _ui.Hand.Widgets.Single(w => w.Card?.Id == 6));
            first.OnPointerExit(null);
            yield return Settled();
            Assert.AreEqual(3, _ui.Effects.DrawsPresented);
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6, 7, 8 }, _ui.Hand.Widgets.Select(w => w.Card.Id));
            AssertSlots(_ui.Hand);
            var hovered = _ui.Hand.Widgets[1];
            hovered.OnPointerEnter(null);
            Assert.AreEqual(_ui.Hand.Content.childCount - 1, hovered.transform.GetSiblingIndex());
            hovered.OnPointerExit(null);
            AssertSlots(_ui.Hand);
        }

        [UnityTest]
        public IEnumerator DrawDestroyDrawPreservesStateOrder()
        {
            yield return Setup(State(1));
            var after = State(1, 2, 3);
            after.Players[0].Field = Array.Empty<CardView>();
            after.Players[0].Graveyard = new[] { Card(100) };
            Events(Draw(2), new EventView { Type = 2, TargetPlayerId = 1, TargetCardId = 100 },
                new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = 100, FromZone = 2, ToZone = 3 }, Draw(3));
            Snapshot(after);
            yield return null;
            yield return null;
            Assert.AreEqual(0, _client.State.Players[0].Field.Length);
            Assert.AreEqual(1, _ui.OwnField.Widgets.Count, "Later destruction cannot overtake the first draw");
            Assert.IsFalse(_ui.Hand.Widgets.Any(w => w.Card?.Id == 3));
            float deadline = Time.realtimeSinceStartup + 3;
            while (_ui.Effects.DrawsPresented < 2)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline);
                yield return null;
            }
            Assert.AreEqual(0, _ui.OwnField.Widgets.Count, "Destruction must precede the second draw");
            Assert.AreEqual(1, _ui.OwnGraveyard.Widgets.Count);
            yield return Settled();
            AssertSlots(_ui.Hand);
        }

        [UnityTest]
        public IEnumerator HiddenDrawAndInvalidationRestoreLatestSnapshot()
        {
            yield return Setup(State(1));
            var after = State(1, 2);
            after.Players[1].HandCount = 3;
            Events(Draw(0, 2), Draw(2));
            Snapshot(after);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.Greater(_ui.Effects.ActiveDraws, 0);
            Assert.IsTrue(_ui.EnemyHand.Widgets.All(w => w.Card == null && w.Hidden));
            _client.DisconnectNetwork();
            yield return null;
            yield return null;
            Assert.AreEqual(0, _ui.Effects.ActiveDraws);
            Assert.AreEqual(2, _ui.Hand.Widgets.Count);
            Assert.AreEqual(3, _ui.EnemyHand.Widgets.Count);
            AssertSlots(_ui.Hand);
            AssertSlots(_ui.EnemyHand);
            Assert.IsFalse(_ui.PresentationBusy);
            Events(Draw(3));
            Snapshot(State(1, 2, 3));
            yield return null;
            yield return null;
            _instance.SetActive(false);
            Assert.AreEqual(0, _ui.Effects.ActiveDraws);
            _instance.SetActive(true);
            yield return null;
            yield return null;
            AssertSlots(_ui.Hand);
            Assert.AreEqual(3, _ui.Hand.Widgets.Count);
        }

        [UnityTest]
        public IEnumerator SplitEventAndSnapshotDeliveryDoesNotDuplicateCard()
        {
            yield return Setup(State(1));
            Events(Draw(2));
            yield return null;
            yield return null;
            var flight = _ui.Hand.Widgets.Single(w => w.Card?.Id == 2);
            Assert.IsTrue(flight.IsInFlight);
            Snapshot(State(1, 2));
            yield return Settled();
            Assert.AreSame(flight, _ui.Hand.Widgets.Single(w => w.Card?.Id == 2));
            Assert.AreEqual("Draw 2", flight.Card.Name);
            Assert.AreEqual(1, _ui.Effects.DrawsPresented);
            AssertSlots(_ui.Hand);
        }

        [UnityTest]
        public IEnumerator DrawVisualCaptureAtTwoCanvasSizes()
        {
            yield return Setup(State(1, 2, 3, 4, 5));
            // Avoid capturing an unrelated initial-turn dim over the draw.
            _ui.Effects.ResetFeedback();
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1280, 720, "Draw-before.png");
            Events(new EventView { Type = 13, SourceCardId = 100 }, Draw(6), Draw(7));
            Snapshot(State(1, 2, 3, 4, 5, 6, 7));
            yield return new WaitForSecondsRealtime(.12f);
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1280, 720, "Draw-departure.png");
            yield return new WaitForSecondsRealtime(.1f);
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1920, 1080, "Draw-flight.png");
            yield return Settled();
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1280, 720, "Draw-landed.png");
            AssertSlots(_ui.Hand);
        }

        [UnityTest]
        public IEnumerator DestroyDuringDrawDoesNotLeaveRunningPresentation()
        {
            yield return Setup(State(1));
            Events(Draw(2), Draw(3));
            Snapshot(State(1, 2, 3));
            yield return null;
            yield return null;
            Assert.Greater(_ui.Effects.ActiveDraws, 0);
            UnityEngine.Object.DestroyImmediate(_instance);
            _instance = null;
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ProjectionDoesNotMutateAuthorityOrRetainOpponentIdentity()
        {
            var original = State(1);
            var projection = new BattlePresentationState();
            projection.Reset(original);
            projection.Apply(new EventView { Type = 1, TargetCardId = 100, HpAfter = 1 }, original, 1);
            Assert.AreEqual(5, original.Players[0].Field[0].Hp);
            Assert.AreEqual(1, projection.Player(1).Field[0].Hp);
            original.Players[1].Field = new[] { Card(200) };
            projection.Reset(original);
            projection.Apply(new EventView { Type = 4, TargetPlayerId = 2, TargetCardId = 200,
                FromZone = 2, ToZone = 1 }, original, 1);
            Assert.IsEmpty(projection.Player(2).Hand);
            Assert.AreEqual(3, projection.Player(2).HandCount);
        }
    }
}
#endif
