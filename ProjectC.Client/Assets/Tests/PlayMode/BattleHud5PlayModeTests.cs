#if UNITY_EDITOR
using System;
using System.Collections;
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
    public sealed class BattleHud5PlayModeTests
    {
        [UnityTest]
        public IEnumerator ResourceAndTypographyRenderAtBattleResolution()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            var localization = CardPresentation.Library.Localization;
            var oldLanguage = localization.Language;
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"hud5\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "hud5", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, Phase = 0, TurnNumber = 3, FieldLimit = 5, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(1.5f);
            var hud = ui.GetComponentInChildren<BattleHudView>();
            var current = Find<TMP_Text>(hud, "YokaiCurrent");
            var maximum = Find<TMP_Text>(hud, "YokaiMaximum");
            var fill = Find<Image>(hud, "YokaiFill");
            var panel = Find<Image>(hud, "YokaiPanel");
            Assert.AreEqual(Image.Type.Sliced, panel.type);
            Assert.AreEqual(Image.Type.Sliced, ui.LogToggle.GetComponent<Image>().type);
            Assert.Greater(ui.LogToggle.GetComponent<Image>().sprite.border.x, 0);
            var currentPosition = current.rectTransform.anchoredPosition;
            var maximumPosition = maximum.rectTransform.anchoredPosition;
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                localization.Language = language;
                yield return null;
                yield return null;
                var text = Find<TMP_Text>(hud, "YokaiValue");
                Assert.AreEqual(BattleText.Get("ui_battle_yokai_power"), text.text);
                text.ForceMeshUpdate();
                Assert.IsFalse(text.isTextOverflowing, language);
                yield return Capture("language-" + language);
            }
            localization.Language = "korean";
            yield return null;
            yield return null;
            ui.enabled = false;
            foreach (int value in new[] { 0, 2, 10 })
            {
                hud.Render(new PlayerView { YokaiPower = value, DeckCount = 25 }, new PlayerView { DeckCount = 25 }, 10);
                Assert.AreEqual(value.ToString(), current.text);
                Assert.AreEqual("10", maximum.text);
                Assert.AreEqual(value / 10f, fill.fillAmount, .001f);
                Assert.AreEqual(currentPosition, current.rectTransform.anchoredPosition);
                Assert.AreEqual(maximumPosition, maximum.rectTransform.anchoredPosition);
                yield return Capture("resource-" + value);
            }
            hud.Render(new PlayerView(), new PlayerView(), 0);
            Assert.IsFalse(panel.gameObject.activeSelf);
            hud.Render(new PlayerView { YokaiPower = 2, DeckCount = 25 }, new PlayerView { DeckCount = 25 }, 10);
            var action = ui.EndTurn.GetComponentInChildren<TMP_Text>();
            ui.EndTurn.interactable = true;
            action.text = BattleText.Get("ui.hud.action.battle");
            yield return Capture("turn-enabled");
            ui.EndTurn.interactable = false;
            yield return Capture("turn-disabled");
            var pointer = new PointerEventData(EventSystem.current);
            ui.LogToggle.OnPointerEnter(pointer);
            yield return new WaitForSecondsRealtime(.2f);
            yield return Capture("menu-hover");
            ui.LogToggle.OnPointerExit(pointer);
            Assert.AreEqual(5, ui.Hand.Widgets.Count);
            Assert.AreEqual(3, ui.OwnField.Widgets.Count);
            Assert.IsTrue(ui.Hand.Widgets.Any(card => card.Card.DefinitionId == 1008));
            yield return Capture("hand5-long-name");
            ui.OwnField.Render(Cards(5, 40), 5, false, 0, false, null, null, null);
            ui.EnemyField.Render(Cards(5, 60), 5, false, 0, false, null, null, null);
            ui.Hand.Render(Cards(10, 1), 0, false, 0, false, null, null, null);
            yield return new WaitForSecondsRealtime(.5f);
            Assert.AreEqual(10, ui.Hand.Widgets.Count);
            Assert.AreEqual(5, ui.OwnField.Widgets.Count);
            yield return Capture("field5-hand10");
            localization.Language = oldLanguage;
            var empty = SceneManager.CreateScene("Hud5Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static T Find<T>(Component root, string name) where T : Component
        {
            return root.GetComponentsInChildren<T>(true).First(item => item.name == name);
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] defs = { 1008, 1003, 4002, 3004, 4005, 1002, 4001, 3002, 1006, 1001 };
            return Enumerable.Range(0, count).Select(i => new CardView { Id = first + i, DefinitionId = defs[i % defs.Length], Attack = 1 + i * 2, Hp = 2 + i, MaxHp = 2 + i }).ToArray();
        }

        private static IEnumerator Capture(string name)
        {
            yield return new WaitForSecondsRealtime(.2f);
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/BattleHud5Synthetic");
                File.WriteAllBytes("Artifacts/BattleHud5Synthetic/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
