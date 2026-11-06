#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleRenewalStage3PlayModeTests
    {
        [UnityTest]
        public IEnumerator DistinctCardsRenderCountsHoverSelectionAndTarget()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            Assert.IsNotNull(ui);
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"renewal3\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "renewal3", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3, FieldLimit = 10, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, Hand = Cards(5, 1), Field = Cards(5, 30), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 50), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(.6f);
            Assert.AreNotSame(ui.Hand.CardPrefab, ui.OwnField.CardPrefab);
            var handSprite = ui.Hand.CardPrefab.FrameImage.sprite;
            var fieldSprite = ui.OwnField.CardPrefab.FrameImage.sprite;
            Assert.AreNotSame(handSprite, fieldSprite);
            Assert.AreEqual("HandCardFrame", handSprite.name);
            Assert.AreEqual("FieldCardFrame", fieldSprite.name);
            var shader = Shader.Find("ProjectC/UI/BattleFrameGlow");
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported);
            ui.SetHandExpanded(true);
            foreach (int count in new[] { 1, 5, 10 })
            {
                ui.Hand.Render(Cards(count, 1), 0, false, 0, false, null, null, null);
                yield return new WaitForSecondsRealtime(.5f);
                Assert.AreEqual(count, ui.Hand.Widgets.Count);
                var card = ui.Hand.Widgets[0];
                Assert.IsTrue(card.InHand);
                Assert.IsTrue(card.transform.Find("FrameImage/CostBadgeFrame").gameObject.activeSelf, "The catalog's existing activation cost should be shown.");
                Assert.AreEqual("HandCardGlowMask", Find(card.transform, "FrameGlowImage").GetComponent<Image>().sprite.name);
                yield return Capture($"hand-{count}");
            }
            var hovered = ui.Hand.Widgets[0];
            hovered.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.IsTrue(hovered.IsHovered);
            Assert.Greater(hovered.Face.localScale.x, 1f);
            yield return Capture("hand-hover");
            hovered.OnPointerExit(null);
            foreach (int count in new[] { 1, 5, 10 })
            {
                ui.OwnField.Render(Cards(count, 30), count, false, 0, false, null, null, null);
                yield return new WaitForSecondsRealtime(.4f);
                Assert.AreEqual(count, ui.OwnField.Widgets.Count);
                var card = ui.OwnField.Widgets[0];
                Assert.IsFalse(card.InHand);
                Assert.IsFalse(Find(card.transform, "CostBadgeImage").gameObject.activeSelf);
                Assert.AreEqual("FieldCardGlowMask", Find(card.transform, "FrameGlowImage").GetComponent<Image>().sprite.name);
                Assert.AreEqual("3", Find(card.transform, "AttackBadgeText").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("4", Find(card.transform, "HealthBadgeText").GetComponent<TMPro.TMP_Text>().text);
                Assert.LessOrEqual(((RectTransform)card.transform).rect.width, ui.OwnField.GetComponent<RectTransform>().rect.width);
                if (count == 10)
                {
                    Assert.GreaterOrEqual(ui.OwnField.Layout.Spacing, ui.OwnField.Layout.CardSize.x * ui.OwnField.Layout.CardScale);
                }
                yield return Capture($"field-{count}");
            }
            var chosen = ui.OwnField.Widgets[0];
            chosen.SetSelected(true);
            yield return null;
            Assert.Greater(Find(chosen.transform, "FrameGlowImage").GetComponent<Image>().color.a, 0);
            yield return Capture("field-selected");
            chosen.SetSelected(false);
            chosen.SetHint(TargetHint.Available);
            yield return null;
            Assert.Greater(Find(chosen.transform, "FrameGlowImage").GetComponent<Image>().color.a, 0);
            yield return Capture("field-target");
            var empty = SceneManager.CreateScene("RenewalStage3Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static Transform Find(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true).First(node => node.name == name);
        }

        private static CardView[] Cards(int count, int first)
        {
            return Enumerable.Range(first, count).Select(id => new CardView
            {
                Id = id, DefinitionId = 1006, Attack = 3, Hp = 4, MaxHp = 4, CanSummon = true,
            }).ToArray();
        }

        private static IEnumerator Capture(string name)
        {
            if (Application.isBatchMode)
            {
                yield break;
            }
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory("Artifacts/BattleRenewalStage3");
            File.WriteAllBytes("Artifacts/BattleRenewalStage3/" + name + ".png", texture.EncodeToPNG());
            Object.Destroy(texture);
        }
    }
}
#endif
