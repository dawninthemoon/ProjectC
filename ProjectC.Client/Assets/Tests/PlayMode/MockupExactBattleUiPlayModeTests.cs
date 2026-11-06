#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class MockupExactBattleUiPlayModeTests
    {
        private BattlePresenter _ui;
        private int _sequence;

        [UnityTest]
        public IEnumerator OriginalMockupLiveUiAndRequiredCaptures()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            _ui = Object.FindFirstObjectByType<BattlePresenter>();
            _ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"turnlog\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(1, 3, 120000);
            yield return new WaitForSecondsRealtime(2.5f);
            var hud = Find(_ui.transform, "TurnHudImage");
            var status = Find(hud, "TurnOwnerText").GetComponent<TMP_Text>();
            var ring = Find(hud, "TimerTicksOnImage").GetComponent<Image>();
            Assert.AreEqual("TurnHUD_Exact", hud.GetComponent<Image>().sprite.name);
            Assert.AreEqual("3", _ui.TurnLabel.text);
            Assert.AreEqual(BattleText.Get("ui.turn.yours"), status.text);
            Assert.AreEqual(Image.Type.Filled, ring.type);
            Assert.AreEqual(Image.FillMethod.Radial360, ring.fillMethod);
            Assert.IsFalse(ring.raycastTarget);
            var ownTint = status.color;
            float progress = ring.fillAmount;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.Less(ring.fillAmount, progress);
            yield return ValidateCardsAndCounters();
            yield return Capture("B-hand-five");
            yield return ValidateCosts();
            _ui.LogToggle.onClick.Invoke();
            Assert.IsTrue(_ui.Log.IsOpen);
            Assert.IsTrue(Find(_ui.Log.History.transform, "LogEmptyText").gameObject.activeInHierarchy);
            Assert.AreEqual(1, _ui.Modal.ActiveCount);
            Assert.AreEqual("ProjectC_BattleLog_PopupFrame", _ui.Log.History.GetComponent<Image>().sprite.name);
            Assert.Less(_ui.Modal.Tint.r, .1f);
            Assert.Greater(_ui.Modal.Tint.a, .5f);
            yield return Capture("log-empty");
            Find(_ui.Log.History.transform, "CloseLogButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            var events = new[]
            {
                new EventView { Type = 5, TargetPlayerId = 1, TurnNumber = 3 },
                new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = 11, FromZone = 1, ToZone = 2 },
                new EventView { Type = 11, TargetPlayerId = 1, TargetCardId = 11 },
                new EventView { Type = 0, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21 },
                new EventView { Type = 1, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21, Damage = 2, HpAfter = 0 },
                new EventView { Type = 13, SourcePlayerId = 1, SourceCardId = 11 },
                new EventView { Type = 2, TargetPlayerId = 2, TargetCardId = 21 },
                // Deliberately supply a definition as bait: a redacted instance must never use it.
                new EventView { Type = 4, TargetPlayerId = 2, TargetCardId = 0, DefinitionId = 4005, FromZone = 0, ToZone = 1 },
                // Also ignore any accidental opponent hand DTOs when building the visible-card cache.
                new EventView { Type = 13, SourcePlayerId = 2, SourceCardId = 999, DefinitionId = 4005 },
            };
            Events(events);
            Snapshot(1, 3, 60000);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(ring.fillAmount, Is.InRange(.45f, .55f));
            Assert.AreEqual(Color.white, ring.color);
            yield return Capture("F-timer-half");
            _ui.SkipPresentation();
            yield return null;
            Assert.AreEqual(9, _ui.Log.Entries.Count);
            var hidden = _ui.Log.Entries[7];
            Assert.IsNull(hidden.Card);
            Assert.IsNull(_ui.Log.Entries[8].Card);
            StringAssert.Contains(BattleText.Get("ui.log.hidden_card"), hidden.Text(1));
            Assert.IsFalse(hidden.Text(1).Contains(CardPresentation.Name(Card(999, 4005))));
            Assert.IsTrue(_ui.Log.Entries.Any(entry => entry.Text(1).Contains(CardPresentation.Name(Card(11, 1001)))));
            _ui.LogToggle.onClick.Invoke();
            yield return null;
            Assert.IsFalse(_ui.Client.Input.CanExecute(GameplayInputAction.Attack));
            Assert.IsFalse(_ui.Client.Input.CanExecute(GameplayInputAction.EndTurn));
            Assert.IsTrue(_ui.Client.Input.CanExecute(GameplayInputAction.OpenCardDetails));
            Assert.IsFalse(_ui.EndTurn.interactable);
            Canvas.ForceUpdateCanvases();
            var hit = Raycast(_ui.Hand.Widgets[0].Face);
            Assert.IsFalse(hit.transform.IsChildOf(_ui.Hand.transform), "Modal must block hand raycasts.");
            Assert.IsFalse(_ui.Log.GetComponent<Image>().enabled);
            var row = _ui.Log.History.GetComponentsInChildren<BattleLogItemView>().First(value => value.Entry.Card != null && value.GetComponent<Button>().interactable);
            row.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(CardPresentation.Name(row.Entry.Card), _ui.Detail.Title.text);
            Assert.AreSame(CardArtProvider.Load(row.Entry.Card.DefinitionId), Find(_ui.Detail.transform, "ArtworkImage").GetComponent<Image>().sprite);
            Assert.AreEqual(1001, _ui.Client.State.Players[0].Field[0].DefinitionId, "Inspecting history cannot mutate match data.");
            yield return Capture("G-log-card-detail");
            CloseWithEscape();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.IsFalse(_ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsTrue(_ui.Client.Input.CanExecute(GameplayInputAction.Attack));
            Snapshot(2, 4, 90000);
            yield return null;
            yield return null;
            Assert.AreEqual(BattleText.Get("ui.turn.theirs"), status.text);
            Assert.AreEqual(status.text, Find(_ui.transform, "CenterTurnOwnerText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("17", _ui.OwnPlayer.Life.text);
            Assert.AreEqual("16", _ui.EnemyPlayer.Life.text);
            Assert.AreEqual("24", Find(_ui.transform, "OwnDeckCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("22", Find(_ui.transform, "EnemyDeckCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("1", Find(_ui.transform, "OwnExileCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("1", Find(_ui.transform, "EnemyExileCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("1", Find(_ui.transform, "EnemyGraveCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("12", Find(_ui.OwnField.Widgets[0].Face, "AttackBadgeText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("9", Find(_ui.OwnField.Widgets[0].Face, "HealthBadgeText").GetComponent<TMP_Text>().text);
            Assert.AreNotEqual(ownTint, status.color);
            Assert.IsFalse(_ui.EndTurn.interactable);
            yield return Capture("opponent-turn");
            Events(Enumerable.Range(0, 30).Select(index => new EventView { Type = index % 2 == 0 ? 13 : 1, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21, Damage = 1, HpAfter = 1, TurnNumber = 4 }).ToArray());
            Snapshot(2, 4, 80000);
            _ui.Log.Toggle();
            yield return null;
            Assert.AreEqual(2, _ui.Log.History.GetComponentsInChildren<BattleLogItemView>().Count(value => !value.GetComponent<Button>().interactable && value.Entry.Turn > 0 && value.transform.Find("LogActorText").gameObject.activeSelf == false));
            Assert.Greater(_ui.Log.Scroll.content.rect.height, _ui.Log.Scroll.viewport.rect.height);
            _ui.Log.Scroll.verticalNormalizedPosition = 1;
            yield return null;
            var oldPosition = _ui.Log.Scroll.content.anchoredPosition;
            Events(new[] { new EventView { Type = 2, TargetPlayerId = 2, TargetCardId = 21, TurnNumber = 4 } });
            Snapshot(2, 4, 70000);
            Assert.AreEqual(oldPosition, _ui.Log.Scroll.content.anchoredPosition, "New events must not pull a reader away from older actions.");
            yield return Capture("log-scrolled");
            var locale = CardPresentation.Library.Localization;
            string original = locale.Language;
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                locale.Language = language;
                yield return null;
                yield return null;
                status.ForceMeshUpdate();
                Assert.IsFalse(status.isTextOverflowing, language + " turn status");
                AssertInside(hud);
                AssertInside((RectTransform)_ui.Log.History.transform);
                yield return Capture("locale-" + language);
            }
            locale.Language = original;
            foreach (var resolution in new[] { new Vector2Int(1920, 1200), new Vector2Int(2560, 1080), new Vector2Int(1280, 720) })
            {
                SetResolution(resolution.x, resolution.y);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.AreEqual(resolution.x, Screen.width);
                AssertInside(hud);
                AssertInside((RectTransform)_ui.Log.History.transform);
                yield return Capture("ratio-" + resolution.x + "x" + resolution.y);
            }
            int count = _ui.Log.Entries.Count;
            _ui.SkipPresentation();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.AreEqual(count, _ui.Log.Entries.Count, "Restoring the same match snapshot preserves visible history.");
            _ui.Client.ApplyMessage("{\"Type\":\"SessionUpdated\",\"ProtocolVersion\":2,\"MatchId\":\"turnlog\",\"PlayerId\":1,\"GameNumber\":2}");
            _sequence = 0;
            Snapshot(1, 1, 60000, 2);
            Assert.AreEqual(0, _ui.Log.Entries.Count, "A rematch must not mix the preceding game's history.");
            BattlefieldScenePlayModeTests.SetResolution();
            var emptyScene = SceneManager.CreateScene("SunsetCelCleanup");
            SceneManager.SetActiveScene(emptyScene);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }


        private IEnumerator ValidateCardsAndCounters()
        {
            var ornament = Find(_ui.transform, "TimerRingFrame").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Simple, ornament.type);
            Assert.AreEqual(Quaternion.identity, ornament.rectTransform.localRotation);
            Assert.IsFalse(ornament.enabled);
            var housing = Find(_ui.transform, "TimerHousingImage").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Simple, housing.type);
            Assert.AreEqual(Quaternion.identity, housing.transform.localRotation);
            Assert.IsFalse(housing.raycastTarget);
            Assert.IsNotNull(Raycast((RectTransform)_ui.EnemyPlayer.transform).GetComponentInParent<BattleHitArea>());
            Assert.IsFalse(_ui.Effects.PopupGroup.GetComponentsInChildren<Graphic>(true).Any(graphic => graphic.enabled));
            foreach (var zone in new[] { _ui.Hand, _ui.OwnField })
            {
                foreach (var widget in zone.Widgets)
                {
                    Assert.IsTrue(Raycast(widget.Face).transform.IsChildOf(widget.transform));
                    var health = Find(widget.Face, "HealthValueContainer");
                    var attack = Find(widget.Face, "AttackValueContainer");
                    Assert.Less(attack.position.x, health.position.x);
                    if (zone == _ui.Hand)
                    {
                        Assert.AreEqual("CardDefenseShield_Exact", health.GetComponent<Image>().sprite.name);
                        Assert.AreEqual("CardAttackFlower_Exact", attack.GetComponent<Image>().sprite.name);
                    }
                    else
                    {
                        Assert.IsFalse(health.GetComponent<Image>().enabled);
                        Assert.IsFalse(attack.GetComponent<Image>().enabled);
                    }
                    Assert.IsFalse(Find(widget.Face, "Inset").GetComponent<Image>().enabled);
                    Assert.IsFalse(Find(widget.Face, "ArtworkMask").GetComponent<Mask>().showMaskGraphic);
                }
            }
            var hand = _ui.Hand.Widgets[0];
            Assert.AreEqual("HandCardFrame_Exact", hand.FrameImage.sprite.name);
            Assert.AreEqual(TextAlignmentOptions.Center, hand.Title.alignment);
            Assert.Less(hand.Title.color.r, .3f, "Parchment title must retain dark ink after runtime styling.");
            Assert.That(hand.Title.transform.parent.GetComponent<RectTransform>().anchoredPosition.x, Is.EqualTo(0));
            hand.Card.CanSummon = true;
            hand.SetActionPermissions(false, false, true);
            Assert.Greater(Find(hand.Face, "FrameGlowImage").GetComponent<Image>().color.a, 0);
            hand.SetSelected(true);
            yield return Capture("hand-selected");
            hand.SetSelected(false);
            var own = _ui.OwnField.Widgets[0];
            Assert.AreEqual("FieldMonsterFrame_Exact", own.FrameImage.sprite.name);
            Assert.That(own.Face.rect.width / own.Face.rect.height, Is.EqualTo(139f / 156f).Within(.001f));
            Assert.AreEqual(Quaternion.identity, own.transform.localRotation);
            own.Card.CanAttack = true;
            own.SetActionPermissions(true, false);
            Assert.Greater(Find(own.Face, "FrameGlowImage").GetComponent<Image>().color.a, 0);
            own.SetSelected(true);
            _ui.EnemyField.Render(new[] { Card(31, 3001), Card(32, 4002) }, 5, false, 0, false, null, null, null);
            _ui.EnemyField.Layout.Snap();
            _ui.OwnField.Render(new[] { Card(11, 1001), Card(17, 1002), Card(18, 3004) }, 5, false, 11, false, null, null, null);
            _ui.OwnField.Layout.Snap();
            yield return Capture("C-both-fields");
            var fullField = Enumerable.Range(0, 5).Select(index => Card(60 + index, 1001)).ToArray();
            _ui.OwnField.Render(fullField, 5, false, 0, false, null, null, null);
            _ui.EnemyField.Render(fullField, 5, false, 0, false, null, null, null);
            _ui.OwnField.Layout.Snap();
            _ui.EnemyField.Layout.Snap();
            AssertInside(_ui.OwnField.Widgets[0].Face);
            AssertInside(_ui.EnemyField.Widgets[4].Face);
            Assert.Greater(_ui.EnemyField.Widgets[0].Face.position.y, _ui.OwnField.Widgets[0].Face.position.y);
            yield return Capture("both-fields-full");
            _ui.OwnField.Render(new[] { Card(11, 1001) }, 5, false, 0, false, null, null, null);
            own = _ui.OwnField.Widgets[0];
            var changed = Card(11, 1001);
            changed.Attack = 12;
            changed.Hp = 9;
            own.Bind(changed, false, false, false, false, true);
            Assert.AreEqual("12", Find(own.Face, "AttackBadgeText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("9", Find(own.Face, "HealthBadgeText").GetComponent<TMP_Text>().text);
            var previous = _ui.Hand.Widgets.Select(widget => widget.Card).ToArray();
            _ui.Hand.Render(previous.Take(2).ToArray(), 0, false, 0, false, null, null, null);
            Assert.AreEqual(2, _ui.Hand.Widgets.Count);
            _ui.Hand.Render(previous, 0, false, 0, false, null, null, null);
            Assert.AreEqual(5, _ui.Hand.Widgets.Count);
            _ui.EnemyField.Render(Array.Empty<CardView>(), 5, false, 0, false, null, null, null);
            Assert.IsFalse(_ui.EnemyField.Content.GetComponentsInChildren<CardWidget>().Any());
            foreach (var back in _ui.EnemyHand.Widgets)
            {
                Assert.AreEqual("CardBack_Exact", back.FrameImage.sprite.name);
                Assert.IsNull(back.Card);
                Assert.IsFalse(back.ArtworkImage.gameObject.activeSelf);
            }
            foreach (string prefix in new[] { "Own", "Enemy" })
            {
                foreach (string zoneName in new[] { "Deck", "Grave", "Exile" })
                {
                    var icon = Find(_ui.transform, prefix + zoneName + "Icon").GetComponent<Image>();
                    Assert.IsFalse(icon.enabled);
                    Assert.IsFalse(icon.raycastTarget);
                }
            }
            Assert.AreEqual("PhaseButton_Exact", _ui.EndTurn.GetComponent<Image>().sprite.name);
            Assert.AreEqual("PhaseArrow_Exact", Find(_ui.EndTurn.transform, "TurnArrowImage").GetComponent<Image>().sprite.name);
            Assert.Less(Find(_ui.transform, "OwnDeckCountText").GetComponent<TMP_Text>().color.r, .3f);
            Assert.IsTrue(Raycast((RectTransform)_ui.EndTurn.transform).transform.IsChildOf(_ui.EndTurn.transform));
            string[] phaseKeys = { "ui_battle_main_phase_1", "ui_battle_battle_phase", "ui_battle_main_phase_2", "ui_battle_end_phase" };
            string[] actionKeys = { "ui_battle_battle_phase", "ui_battle_main_phase_2", "ui_battle_end_turn", "ui_battle_end_turn" };
            BattlePhaseKind[] phases = { BattlePhaseKind.Main1, BattlePhaseKind.Battle, BattlePhaseKind.Main2, BattlePhaseKind.End };
            for (int phase = 0; phase < phaseKeys.Length; ++phase)
            {
                Snapshot(1, 3, 119000, phase: (int)phases[phase]);
                yield return null;
                Assert.AreEqual(BattleText.Get(phaseKeys[phase]), _ui.PhaseLabel.text);
                Assert.AreEqual(BattleText.Get(actionKeys[phase]), Find(_ui.EndTurn.transform, "LabelText").GetComponent<TMP_Text>().text);
            }
            Snapshot(1, 3, 119000);
            yield return null;
            Snapshot(1, 3, 0);
            yield return null;
            Assert.AreEqual(0, Find(_ui.transform, "TimerTicksOnImage").GetComponent<Image>().fillAmount);
            Assert.IsTrue(Find(_ui.transform, "TimerTicksOffImage").GetComponent<Image>().enabled);
            yield return Capture("timer-zero");
            Snapshot(1, 3, 119000);
            yield return null;
        }


        private IEnumerator ValidateCosts()
        {
            var costs = _ui.GetComponentsInChildren<BattleSpecialCostsView>(true).Single();
            var definition = AssetDatabase.LoadAssetAtPath<BattleCostDefinition>("Assets/Art/UI/BattleCompact/YokaiCost.asset");
            var moon = Object.Instantiate(definition);
            var star = Object.Instantiate(definition);
            var moonData = new SerializedObject(moon);
            moonData.FindProperty("_icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Battle/MockupExact/Slices/CostMoon.asset");
            moonData.ApplyModifiedPropertiesWithoutUndo();
            var starData = new SerializedObject(star);
            starData.FindProperty("_icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Battle/MockupExact/Slices/CostStar.asset");
            starData.ApplyModifiedPropertiesWithoutUndo();
            _ui.enabled = false;
            costs.Render(new[] { new BattleCostDisplay(definition, 2, 10), new BattleCostDisplay(moon, 0), new BattleCostDisplay(star, 1) });
            Assert.AreEqual(3, costs.Count);
            Assert.AreEqual("2", costs.Items[0].Amount);
            _ui.OwnField.Render(new[] { Card(11, 1001), Card(17, 4002), Card(18, 3004) }, 5, false, 0, false, null, null, null);
            _ui.EnemyField.Render(new[] { Card(31, 3001), Card(32, 4002) }, 5, false, 0, false, null, null, null);
            _ui.OwnField.Layout.Snap();
            _ui.EnemyField.Layout.Snap();
            yield return Capture("D-cost-expanded");
            yield return Capture("C-both-fields");
            var toggle = Find(costs.transform, "CostToggleButton").GetComponent<Button>();
            toggle.onClick.Invoke();
            Assert.IsTrue(costs.IsCollapsed);
            Assert.IsFalse(Find(costs.transform, "CostBodyImage").gameObject.activeSelf);
            Assert.IsFalse(Find(costs.transform, "CostViewportImage").gameObject.activeSelf);
            Assert.IsTrue(toggle.gameObject.activeInHierarchy);
            Assert.That(Find(costs.transform, "CostChevronImage").localEulerAngles.z, Is.EqualTo(0).Within(.01f));
            Assert.IsTrue(Raycast((RectTransform)toggle.transform).transform.IsChildOf(toggle.transform));
            yield return Capture("E-cost-collapsed");
            toggle.onClick.Invoke();
            Assert.IsFalse(costs.IsCollapsed);
            Assert.That(Find(costs.transform, "CostChevronImage").localEulerAngles.z, Is.EqualTo(180).Within(.01f));
            costs.Render(Enumerable.Range(0, 6).Select(index => new BattleCostDisplay(definition, index + 1)).ToArray());
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(6, costs.Count);
            Assert.Greater(costs.Scroll.content.rect.height, costs.Scroll.viewport.rect.height);
            var oldPosition = costs.Scroll.content.anchoredPosition;
            costs.Scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -4) });
            Assert.Less(costs.Scroll.verticalNormalizedPosition, 1);
            costs.Scroll.verticalNormalizedPosition = 0;
            yield return null;
            Assert.AreNotEqual(oldPosition, costs.Scroll.content.anchoredPosition);
            yield return Capture("cost-six-scrolled");
            costs.Render(Array.Empty<BattleCostDisplay>());
            Assert.IsFalse(costs.gameObject.activeSelf);
            costs.Render(new[] { new BattleCostDisplay(definition, 7, 10) });
            Assert.AreEqual("7", costs.Items[0].Amount);
            Object.Destroy(moon);
            Object.Destroy(star);
            _ui.enabled = true;
            Snapshot(1, 3, 119000);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanFailedScene()
        {
            var scene = SceneManager.GetSceneByName("BattleScene");
            if (scene.IsValid() && scene.isLoaded)
            {
                var empty = SceneManager.CreateScene("MockupFailedCleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        private void CloseWithEscape()
        {
            var keyboard = Keyboard.current;
            bool added = keyboard == null;
            keyboard = keyboard ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            _ui.Interaction.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            if (added)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private void Events(EventView[] values)
        {
            _ui.Client.ApplyMessage(JsonUtility.ToJson(new EventsMessage { Type = "GameEvents", ProtocolVersion = 2, MatchId = "turnlog", GameNumber = 1, SequenceNumber = ++_sequence, Payload = values }));
        }

        private void Snapshot(int active, int turn, long remaining, int game = 1, int phase = 0)
        {
            _ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "turnlog", GameNumber = game, SequenceNumber = _sequence, ServerTimeUnixMs = 100000,
                Payload = new StateView { Started = true, TurnStarted = true, ActivePlayerId = active, TurnNumber = turn, Phase = phase, TurnTimerRunning = true, TurnEndsAtUnixMs = 100000 + remaining,
                    FieldLimit = 10, Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = turn == 4 ? 17 : 20, DeckCount = turn == 3 ? 25 : 24, Hand = new[] { Card(12, 3001), Card(13, 4002), Card(14, 3004), Card(15, 4005), Card(16, 1001) }, Field = new[] { new CardView { Id = 11, DefinitionId = 1001, Attack = turn == 4 ? 12 : 2, Hp = turn == 4 ? 9 : 3, MaxHp = 9 } }, Graveyard = Array.Empty<CardView>(), Exile = turn == 3 ? Array.Empty<CardView>() : new[] { Card(51, 3001) } },
                        new PlayerView { PlayerId = 2, Life = turn == 4 ? 16 : 18, DeckCount = turn == 3 ? 23 : 22, HandCount = 5, Hand = new[] { Card(999, 4005) }, Field = Array.Empty<CardView>(), Graveyard = new[] { Card(21, 3001) }, Exile = turn == 3 ? Array.Empty<CardView>() : new[] { Card(51, 3001) } },
                    } },
            }));
        }

        private static CardView Card(int id, int definition) => new CardView { Id = id, DefinitionId = definition, Attack = 2, Hp = 3, MaxHp = 3 };
        private static RectTransform Find(Transform root, string name) => (RectTransform)root.GetComponentsInChildren<Transform>(true).First(value => value.name == name);
        private static void AssertInside(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(point.x, Is.InRange(-1, Screen.width + 1), rect.name + " x");
                Assert.That(point.y, Is.InRange(-1, Screen.height + 1), rect.name + " y");
            }
        }
        private static GameObject Raycast(RectTransform target)
        {
            var hits = new List<RaycastResult>();
            Canvas.ForceUpdateCanvases();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center)) }, hits);
            Assert.IsNotEmpty(hits);
            return hits[0].gameObject;
        }
        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                Directory.CreateDirectory("Artifacts/MockupExact");
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/MockupExact/" + name + ".png", image.EncodeToPNG());
                Object.Destroy(image);
            }
        }
        private static void SetResolution(int width, int height)
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "TurnLog " + width + "x" + height }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null) + (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null) - 1;
            var type = assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(type);
            type.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, index);
            window.Show();
        }
    }
}
#endif
