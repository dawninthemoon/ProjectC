#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Game.Utils;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectC.Tests
{
    public sealed class BattleUxPlayModeTests
    {
        private GameObject _root;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;

        private static T Find<T>(Component root, string name) where T : Component
        {
            Assert.IsTrue(root.transform.Bind(name, out T component), name);
            return component;
        }

        private IEnumerator Setup()
        {
            _root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Battle/BattleScreen.prefab"));
            _ui = _root.GetComponent<BattlePresenter>();
            _client = _ui.Client;
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"ux\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(0, 1);
            yield return null;
            yield return null;
        }

        private void Snapshot(int phase, int sequence)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", MatchId = "ux", GameNumber = 1, ProtocolVersion = 2,
                SequenceNumber = sequence, ServerTimeUnixMs = 1000000,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 1,
                    Phase = phase, FieldLimit = 5, TurnTimerRunning = true, TurnEndsAtUnixMs = 1002500,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, HandCount = 2, DeckCount = 20,
                            Hand = new[] { Card(1, 1001), Card(2, 1002) }, Field = Array.Empty<CardView>(),
                            Graveyard = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, HandCount = 5, DeckCount = 20,
                            Hand = Array.Empty<CardView>(), Field = Array.Empty<CardView>(),
                            Graveyard = Array.Empty<CardView>() }
                    }
                }
            }));
        }

        private static CardView Card(int id, int definition)
        {
            return new CardView { Id = id, DefinitionId = definition, Attack = 3, Hp = 2, MaxHp = 5 };
        }

        [TearDown]
        public void Cleanup()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
        }

        [UnityTest]
        public IEnumerator InspectorIsClickPinnedAndTimerUsesServerCorrection()
        {
            yield return Setup();
            var a = _ui.Hand.Widgets[0];
            var b = _ui.Hand.Widgets[1];
            var fieldPosition = _ui.OwnField.transform.position;
            var panelPosition = _ui.Detail.transform.position;
            a.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsFalse(_ui.Detail.gameObject.activeSelf, "Hover must not inspect");
            _ui.Interaction.Click(a);
            Assert.AreEqual(CardPresentation.Name(a.Card), _ui.Detail.Title.text);
            a.OnPointerExit(null);
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf);
            _ui.Interaction.Click(a);
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf, "Repeated click retains info");
            _ui.Interaction.Click(b);
            Assert.AreEqual(CardPresentation.Name(b.Card), _ui.Detail.Title.text);
            Assert.AreSame(CardArtProvider.Load(1002), Find<AspectFillImage>(_ui.Detail, "ArtworkImage").sprite);
            _ui.LogToggle.onClick.Invoke();
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf, "Ordinary UI buttons retain info");
            _ui.Interaction.ResetInput();
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf, "Input cleanup and drag setup retain info");
            var background = _root.GetComponentsInChildren<BattleHitArea>().First(area => area.Kind == BattleHitArea.AreaKind.Background);
            _ui.Interaction.ClickArea(background);
            Assert.IsFalse(_ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(fieldPosition, _ui.OwnField.transform.position);
            Assert.AreEqual(panelPosition, _ui.Detail.transform.position);
            Assert.IsTrue(Find<RectTransform>(_ui, "CardInfoSpace").gameObject.activeSelf);
            Assert.AreEqual(BattleText.Get("ui.phase.to_battle"), _ui.EndTurn.GetComponentInChildren<TMP_Text>().text);
            Snapshot(1, 2);
            yield return null;
            Assert.AreEqual(BattleText.Get("ui.phase.to_main2"), _ui.EndTurn.GetComponentInChildren<TMP_Text>().text);
            Snapshot(3, 3);
            yield return null;
            Assert.AreEqual(BattleText.Get("ui.phase.end_turn"), _ui.EndTurn.GetComponentInChildren<TMP_Text>().text);
            var timer = Find<TMP_Text>(_ui, "TurnTimer");
            Assert.IsTrue(timer.text.Contains("00:03") || timer.text.Contains("00:02"));
            yield return new WaitForSecondsRealtime(2.7f);
            Assert.IsTrue(timer.text.Contains("00:00"));
            Assert.AreEqual(1, _client.State.TurnNumber, "The local countdown must never end a turn");
            _ui.LogToggle.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.3f);
            _ui.Interaction.Click(a);
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1920, 1080, "BattleUX-Inspector.png");
        }

        private void Choice(int maximum, string id)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", ProtocolVersion = 2, GameNumber = 1, MatchId = "ux", SequenceNumber = 1,
                Payload = new ActionChoiceView { ChoiceId = id, SourceCardId = 1, MinCount = 1, MaxCount = maximum,
                    CanCancel = true, Candidates = new[]
                    {
                        new ActionChoiceCandidateView { Token = "a", CardId = 0, Zone = 0, PlayerId = 1, Card = Card(0, 1003) },
                        new ActionChoiceCandidateView { Token = "b", CardId = 0, Zone = 0, PlayerId = 1, Card = Card(0, 1004) }
                    } }
            }));
            _ui.Interaction.RefreshServerChoice();
        }

        [UnityTest]
        public IEnumerator PinnedInspectorRefreshesFromUpdatedServerSnapshot()
        {
            yield return Setup();
            var widget = _ui.Hand.Widgets[0];
            _ui.Interaction.Click(widget);
            var state = JsonUtility.FromJson<StateView>(JsonUtility.ToJson(_client.State));
            var updated = state.Players[0].Hand[0];
            updated.Attack = 13;
            updated.Hp = 1;
            updated.DefinitionId = 1007;
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "ux", GameNumber = 1,
                SequenceNumber = 2, ServerTimeUnixMs = 1000000, Payload = state
            }));
            yield return null;
            yield return null;
            StringAssert.Contains("13", _ui.Detail.Stats.text);
            Assert.AreSame(CardArtProvider.Load(1007), Find<AspectFillImage>(_ui.Detail, "ArtworkImage").sprite);
            Assert.AreEqual(CardPresentation.Name(updated), _ui.Detail.Title.text);
            Assert.AreEqual(13, _client.State.Players[0].Hand[0].Attack);
        }

        [Test]
        public void ProjectionRefreshesIndependentDirectAttackPermission()
        {
            var card = Card(1, 1001);
            var state = new StateView { Players = new[] { new PlayerView
            {
                PlayerId = 1, Hand = Array.Empty<CardView>(), Field = new[] { card },
                Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>()
            } } };
            var projection = new BattlePresentationState();
            projection.Reset(state);
            card.CanAttack = card.CanDirectAttack = true;
            projection.RefreshActions(state);
            var displayed = BattlePresentationState.Find(projection.State, 1);
            Assert.AreNotSame(card, displayed);
            Assert.IsTrue(displayed.CanAttack && displayed.CanDirectAttack);
            card.CanDirectAttack = false;
            projection.RefreshActions(state);
            Assert.IsTrue(displayed.CanAttack, "A direct-only restriction must retain monster attack permission");
            Assert.IsFalse(displayed.CanDirectAttack);
        }

        [UnityTest]
        public IEnumerator ChoicePreviewSingleReplacementAndMultipleSelectionRemainTemporary()
        {
            yield return Setup();
            var before = JsonUtility.ToJson(_client.State);
            Choice(1, "single");
            var picker = Find<EffectTargetPicker>(_ui, "EffectTargetPicker");
            var gallery = _ui.GetComponentInChildren<CardGalleryView>(true);
            var candidates = gallery.GetComponentsInChildren<CardWidget>();
            candidates[0].Clicked(candidates[0]);
            Assert.AreEqual(CardPresentation.Name(Card(0, 1003)), _ui.Detail.Title.text);
            candidates[1].Clicked(candidates[1]);
            Assert.AreEqual(CardPresentation.Name(Card(0, 1004)), _ui.Detail.Title.text);
            Assert.IsFalse(candidates[0].IsSelected);
            Assert.IsTrue(candidates[1].IsSelected);
            Assert.IsTrue(Find<Button>(picker, "Confirm").interactable);
            var area = _root.GetComponentsInChildren<BattleHitArea>().First(a => a.Kind == BattleHitArea.AreaKind.OwnField);
            _ui.Interaction.ClickArea(area);
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf, "Blocked background input must retain the choice preview");
            Assert.AreEqual(BattleInputState.SelectingEffectTarget, _ui.Interaction.State);
            Assert.IsTrue(picker.gameObject.activeSelf);
            Assert.AreEqual(before, JsonUtility.ToJson(_client.State));
            Choice(2, "multiple");
            candidates = gallery.GetComponentsInChildren<CardWidget>();
            candidates[0].Clicked(candidates[0]);
            candidates[1].Clicked(candidates[1]);
            Assert.IsTrue(candidates[0].IsSelected);
            Assert.IsTrue(candidates[1].IsSelected);
            Assert.AreEqual(before, JsonUtility.ToJson(_client.State));
            Assert.AreEqual(BattleText.Get("ui.effect.activate"), BattleText.Effect(Card(1, 1001), new ActivationView { EffectId = 99 }));
            yield return BattlePrefabPlayModeTests.Capture(_ui, 1280, 720, "BattleUX-ChoicePreview.png");
        }

        [UnityTest]
        public IEnumerator OverlapUsesRotatedAndScaledCardAreaInEveryCanvasMode()
        {
            yield return Setup();
            var card = _ui.Hand.Widgets[0].Face;
            var slot = (RectTransform)_ui.OwnField.transform;
            var cameraObject = new GameObject("Overlap test camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -1000);
            camera.orthographic = true;
            camera.orthographicSize = 600;
            var canvas = _ui.GetComponent<Canvas>();
            try
            {
                foreach (var mode in new[] { RenderMode.ScreenSpaceOverlay, RenderMode.ScreenSpaceCamera, RenderMode.WorldSpace })
                {
                    canvas.renderMode = mode;
                    canvas.worldCamera = camera;
                    Canvas.ForceUpdateCanvases();
                    card.position = slot.TransformPoint(slot.rect.center);
                    card.localScale = Vector3.one;
                    card.rotation = slot.rotation;
                    Assert.Greater(ScreenRectOverlap.Fraction(card, slot), .9f, mode.ToString());
                    card.position = slot.TransformPoint(new Vector3(slot.rect.center.x, slot.rect.yMin - 30, 0));
                    Assert.Greater(ScreenRectOverlap.Fraction(card, slot), .30f, "Pointer is outside but more than 30% of the card overlaps");
                    card.Rotate(0, 0, 20);
                    card.localScale = Vector3.one * 1.1f;
                    Assert.Greater(ScreenRectOverlap.Fraction(card, slot), .30f);
                    card.position = slot.TransformPoint(new Vector3(slot.rect.xMax + 1000, slot.rect.yMax + 1000));
                    Assert.AreEqual(0, ScreenRectOverlap.Fraction(card, slot));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
            yield return null;
        }
    }
}
#endif
