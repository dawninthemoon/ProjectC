#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleLayoutReadabilityPlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleCardsRemainVisibleAndInteractiveAt1920()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"readability\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "readability", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3, FieldLimit = 10, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual(1920, Screen.width);
            Assert.AreEqual(1080, Screen.height);
            Assert.AreEqual(5, ui.EnemyHand.Widgets.Count);
            foreach (var back in ui.EnemyHand.Widgets)
            {
                Assert.IsTrue(back.Hidden);
                Assert.IsNull(back.Card);
                Assert.AreEqual("FoxCardBack", back.FrameImage.sprite.name);
                Assert.IsFalse(back.ArtworkImage.gameObject.activeInHierarchy);
            }
            var click = ui.Hand.Widgets[0].Clicked;
            var hover = ui.Hand.Widgets[0].Hovered;
            var leave = ui.Hand.Widgets[0].HoverLeft;
            foreach (int count in new[] { 1, 5, 10 })
            {
                ui.Hand.Render(Cards(count, 1), 0, false, 0, false, click, hover, leave);
                yield return new WaitForSecondsRealtime(.6f);
                Assert.AreEqual(count, ui.Hand.Widgets.Count);
                Assert.That(ui.Hand.Layout.CardSize.x * ui.Hand.Layout.CardScale, Is.InRange(185, 195));
                foreach (var card in ui.Hand.Widgets)
                {
                    AssertWithinScreen(card.Face);
                }
                AssertHit(ui, ui.Hand.Widgets[count - 1].Face);
                yield return Capture("hand-" + count);
            }
            var focused = ui.Hand.Widgets[4];
            focused.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.5f);
            Assert.IsTrue(focused.IsHovered);
            Assert.IsTrue(ui.Detail.gameObject.activeInHierarchy);
            Assert.AreEqual(CardPresentation.Name(focused.Card), ui.Detail.Title.text);
            Assert.AreEqual(focused.transform, focused.transform.parent.GetChild(focused.transform.parent.childCount - 1));
            Assert.Greater(focused.Face.localScale.x, 1.05f);
            AssertWithinScreen(focused.Face);
            AssertHit(ui, focused.Face);
            yield return Capture("hand-10-hover");
            focused.OnPointerExit(new PointerEventData(EventSystem.current));
            Assert.IsFalse(ui.Detail.gameObject.activeSelf);
            ui.Hand.Render(Cards(5, 1), 0, false, 0, false, click, hover, leave);
            foreach (int count in new[] { 1, 3, 5, 10 })
            {
                ui.OwnField.Render(Cards(count, 40), count, false, 0, false, null, null, null);
                ui.EnemyField.Render(Cards(count, 60), count, false, 0, false, null, null, null);
                yield return new WaitForSecondsRealtime(.6f);
                Assert.AreEqual(count, ui.OwnField.Widgets.Count);
                foreach (var card in ui.OwnField.Widgets.Concat(ui.EnemyField.Widgets))
                {
                    AssertWithinScreen(card.Face);
                }
                AssertHit(ui, ui.OwnField.Widgets[count / 2].Face);
                AssertHit(ui, ui.EndTurn.GetComponent<RectTransform>());
                yield return Capture("field-" + count);
            }
            var empty = SceneManager.CreateScene("ReadabilityCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] definitions = { 1001, 3001, 1006, 4002, 3004, 1003, 4001, 3002, 4005, 1002 };
            return Enumerable.Range(0, count).Select(i => new CardView
            {
                Id = first + i, DefinitionId = definitions[i % definitions.Length], Attack = 1 + i * 2,
                Hp = 2 + i, MaxHp = 2 + i, CanSummon = true,
            }).ToArray();
        }

        private static void AssertWithinScreen(RectTransform rect)
        {
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(point.x, Is.InRange(-1, Screen.width + 1), rect.name + " x");
                Assert.That(point.y, Is.InRange(-1, Screen.height + 1), rect.name + " y");
            }
        }

        private static void AssertHit(BattlePresenter ui, RectTransform target)
        {
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center)),
            };
            var hits = new List<RaycastResult>();
            ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(target), target.name + " blocked");
        }

        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/BattleLayoutReadability");
                File.WriteAllBytes("Artifacts/BattleLayoutReadability/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
