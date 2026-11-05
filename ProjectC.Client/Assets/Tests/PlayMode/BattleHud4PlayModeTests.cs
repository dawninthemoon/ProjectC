#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleHud4PlayModeTests
    {
        [UnityTest]
        public IEnumerator HudSupportsLanguagesHpAndInteraction()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            var localize = CardPresentation.Library.Localization;
            string originalLanguage = localize.Language;
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"hud4\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "hud4", GameNumber = 1, SequenceNumber = 1, ServerTimeUnixMs = 100000,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, Phase = 0, TurnNumber = 3, FieldLimit = 5, MaxYokaiPower = 10,
                    TurnTimerRunning = true, TurnEndsAtUnixMs = 280000,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(1.5f);
            var action = ui.EndTurn.GetComponentInChildren<TMP_Text>();
            foreach (var language in new[] { "korean", "japanese", "english" })
            {
                localize.Language = language;
                yield return null;
                yield return null;
                Assert.AreEqual(BattleText.Get("ui.hud.action.battle"), action.text);
                Assert.AreEqual(BattleText.Get("ui_battle_menu"), ui.LobbyToggle.GetComponentInChildren<TMP_Text>().text);
                Assert.AreEqual(BattleText.Get("ui_battle_battle_log"), ui.LogToggle.GetComponentInChildren<TMP_Text>().text);
                action.ForceMeshUpdate();
                ui.PhaseLabel.ForceMeshUpdate();
                Assert.IsFalse(action.isTextOverflowing, language + " action overflow");
                Assert.IsFalse(ui.PhaseLabel.isTextOverflowing, language + " phase overflow");
                yield return Capture("hud-" + language);
            }
            localize.Language = "korean";
            yield return null;
            yield return null;
            ui.enabled = false;
            foreach (var language in new[] { "korean", "japanese", "english" })
            {
                localize.Language = language;
                foreach (var key in new[] { "ui.hud.action.battle", "ui.hud.action.main2", "ui.hud.action.end" })
                {
                    action.text = BattleText.Get(key);
                    action.ForceMeshUpdate();
                    Assert.IsFalse(action.isTextOverflowing, language + " " + key);
                }
                foreach (var key in new[] { "ui.hud.phase.main1", "ui.hud.phase.battle", "ui.hud.phase.main2", "ui.hud.phase.end" })
                {
                    ui.PhaseLabel.text = BattleText.Get(key);
                    ui.PhaseLabel.ForceMeshUpdate();
                    Assert.IsFalse(ui.PhaseLabel.isTextOverflowing, language + " " + key);
                }
            }
            localize.Language = "korean";
            ui.PhaseLabel.text = BattleText.Get("ui.hud.phase.main1");
            action.text = BattleText.Get("ui.hud.action.battle");
            ui.OwnPlayer.Render(new PlayerView { PlayerId = 1, Life = 9 }, true, true);
            ui.EnemyPlayer.Render(new PlayerView { PlayerId = 2, Life = 123 }, false, false);
            yield return null;
            Assert.AreEqual("9", ui.OwnPlayer.Life.text);
            Assert.AreEqual("123", ui.EnemyPlayer.Life.text);
            ui.EnemyPlayer.Life.ForceMeshUpdate();
            Assert.IsFalse(ui.EnemyPlayer.Life.isTextOverflowing);
            yield return Capture("hp-9-123");
            var turn = ui.EndTurn.GetComponent<TurnFrameGlow>();
            ui.EndTurn.interactable = true;
            turn.SetOpponentTurn(false);
            ui.PhaseLabel.text = BattleText.Get("ui.hud.phase.battle");
            action.text = BattleText.Get("ui.hud.action.main2");
            yield return Capture("battle-ready");
            ui.EndTurn.interactable = false;
            yield return Capture("battle-locked");
            turn.SetOpponentTurn(true);
            yield return Capture("opponent");
            AssertHit(ui, ui.LogToggle);
            AssertHit(ui, ui.LobbyToggle);
            AssertHit(ui, ui.EndTurn);
            var hoverEvent = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var image = ui.LogToggle.GetComponent<Image>();
            var normal = image.canvasRenderer.GetColor();
            ui.LogToggle.OnPointerEnter(hoverEvent);
            yield return new WaitForSecondsRealtime(.2f);
            var highlighted = image.canvasRenderer.GetColor();
            Assert.AreNotEqual(normal, highlighted, "Hover should change the button tint.");
            yield return Capture("menu-hover");
            ui.LogToggle.OnPointerDown(hoverEvent);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreNotEqual(highlighted, image.canvasRenderer.GetColor(), "Pressed should change the button tint.");
            ui.LogToggle.OnPointerUp(hoverEvent);
            ui.LogToggle.OnPointerExit(hoverEvent);
            using (ui.Client.Input.PushScope("Hud4Blocked", Array.Empty<GameplayInputAction>()))
            {
                ui.LogToggle.onClick.Invoke();
                Assert.IsFalse(ui.Log.IsOpen, "Input scope must block the log.");
            }
            ui.LogToggle.onClick.Invoke();
            Assert.IsTrue(ui.Log.IsOpen);
            ui.LogToggle.onClick.Invoke();
            Assert.IsFalse(ui.Log.IsOpen);
            ui.OwnField.Render(Cards(5, 40), 5, false, 0, false, null, null, null);
            ui.EnemyField.Render(Cards(5, 60), 5, false, 0, false, null, null, null);
            ui.Hand.Render(Cards(10, 1), 0, false, 0, false, null, null, null);
            yield return new WaitForSecondsRealtime(.5f);
            yield return Capture("field5-hand10");
            localize.Language = originalLanguage;
            var empty = SceneManager.CreateScene("Hud4Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static void AssertHit(BattlePresenter ui, Button button)
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)) };
            var hits = new List<RaycastResult>();
            ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(button.transform), button.name);
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] defs = { 1001, 3001, 4002, 3004, 4005, 1003, 4001, 3002, 1006, 1002 };
            return Enumerable.Range(0, count).Select(i => new CardView { Id = first + i, DefinitionId = defs[i % defs.Length], Attack = 1 + i * 2, Hp = 2 + i, MaxHp = 2 + i }).ToArray();
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.25f);
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/BattleHud4Synthetic");
                File.WriteAllBytes("Artifacts/BattleHud4Synthetic/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
