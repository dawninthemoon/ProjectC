#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattleFieldClarityPlayModeTests
    {
        [UnityTest]
        public IEnumerator CropAndServerDrivenStatesRemainInteractive()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"field-preview\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "field-preview", GameNumber = 1, SequenceNumber = 1,
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
            var field = ui.OwnField.Widgets[0];
            Assert.IsFalse(field.Title.gameObject.activeSelf);
            Assert.IsFalse(field.transform.GetComponentsInChildren<Transform>(true).Single(item => item.name == "FieldStatusFrame").gameObject.activeSelf);
            Assert.Greater(new SerializedObject((AspectFillImage)field.ArtworkImage).FindProperty("_cropZoom").floatValue, 1f);
            Assert.AreEqual(1f, new SerializedObject((AspectFillImage)ui.Hand.Widgets[0].ArtworkImage).FindProperty("_cropZoom").floatValue);
            Assert.AreEqual(0f, Glow(ui.EnemyField.Widgets[0]).color.a);
            Assert.AreEqual(0f, Glow(field).color.a);
            yield return Capture("basic");
            field.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return null;
            Assert.IsTrue(ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(CardPresentation.Name(field.Card), ui.Detail.Title.text);
            yield return Capture("field-hover");
            field.OnPointerExit(new PointerEventData(EventSystem.current));
            var handClick = ui.Hand.Widgets[0].Clicked;
            var handHover = ui.Hand.Widgets[0].Hovered;
            var handLeave = ui.Hand.Widgets[0].HoverLeft;
            foreach (int count in new[] { 1, 3, 5 })
            {
                ui.OwnField.Render(Cards(count, 40), count, false, 0, false, null, null, null);
                ui.EnemyField.Render(Cards(count, 60), count, false, 0, false, null, null, null);
                yield return new WaitForSecondsRealtime(.4f);
                AssertHit(ui, ui.OwnField.Widgets[count / 2].Face);
                AssertHit(ui, ui.EndTurn.GetComponent<RectTransform>());
                yield return Capture("field-" + count);
            }
            var active = Cards(3, 40);
            active[0].CanAttack = true;
            ui.OwnField.Render(active, 5, false, 0, false, null, null, null);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.Greater(Glow(ui.OwnField.Widgets[0]).color.a, 0);
            Assert.AreEqual(0f, Glow(ui.OwnField.Widgets[1]).color.a);
            yield return Capture("attackable");
            ui.OwnField.Widgets[0].SetSelected(true);
            yield return null;
            Assert.Greater(Glow(ui.OwnField.Widgets[0]).color.r, Glow(ui.OwnField.Widgets[0]).color.g);
            yield return Capture("selected");
            ui.OwnField.Widgets[0].SetSelected(false);
            ui.EnemyField.Render(Cards(3, 60), 5, false, 0, true, null, null, null);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.Greater(Glow(ui.EnemyField.Widgets[0]).color.a, 0);
            Assert.Greater(Glow(ui.EnemyField.Widgets[0]).color.b, Glow(ui.EnemyField.Widgets[0]).color.r);
            yield return Capture("targetable");
            ui.EnemyField.Render(Cards(3, 60), 5, false, 0, false, null, null, null);
            foreach (int count in new[] { 5, 10 })
            {
                ui.Hand.Render(Cards(count, 1), 0, false, 0, false, handClick, handHover, handLeave);
                yield return new WaitForSecondsRealtime(.5f);
                AssertHit(ui, ui.Hand.Widgets[count - 1].Face);
                yield return Capture("hand-" + count);
            }
            var hovered = ui.Hand.Widgets[4];
            hovered.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsTrue(hovered.IsHovered);
            Assert.IsTrue(ui.Detail.gameObject.activeSelf);
            yield return Capture("hand-hover");
            var empty = SceneManager.CreateScene("FieldClarityCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static Image Glow(CardWidget card)
        {
            return card.GetComponentsInChildren<Image>(true).Single(image => image.name == "FrameGlow");
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] definitions = { 1001, 3001, 4002, 3004, 4005, 1003, 4001, 3002, 1006, 1002 };
            return Enumerable.Range(0, count).Select(i => new CardView
            {
                Id = first + i, DefinitionId = definitions[i % definitions.Length], Attack = 1 + i * 2,
                Hp = 2 + i, MaxHp = 2 + i,
            }).ToArray();
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
                Directory.CreateDirectory("Artifacts/BattleFieldClarity");
                File.WriteAllBytes("Artifacts/BattleFieldClarity/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
    }
}
#endif
