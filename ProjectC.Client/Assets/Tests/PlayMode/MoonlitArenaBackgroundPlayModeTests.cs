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
    public sealed class MoonlitArenaBackgroundPlayModeTests
    {
        [UnityTest]
        public IEnumerator BackdropRendersBehindExistingBattleUiAt1920x1080()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            Assert.IsNotNull(ui);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, ui.GetComponent<Canvas>().renderMode);
            var backdrop = ui.GetComponentsInChildren<AspectFillImage>(true).Single(image => image.name == "MoonlitArenaBackdropImage");
            Assert.IsTrue(backdrop.gameObject.activeInHierarchy);
            Assert.IsNotNull(backdrop.sprite);
            Assert.AreEqual(1672, backdrop.sprite.rect.width);
            Assert.AreEqual(941, backdrop.sprite.rect.height);
            Assert.IsFalse(backdrop.raycastTarget);
            Assert.IsFalse(ui.GetComponentsInChildren<Transform>(true).Single(item => item.name == "ArenaGuides").gameObject.activeSelf);
            var stage = SceneManager.GetActiveScene().GetRootGameObjects().Single(item => item.name == "YokaiShrineStage");
            Assert.IsFalse(stage.activeSelf);
            Assert.IsTrue(Camera.main.orthographic);

            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"moonlit-preview\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "moonlit-preview", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3, FieldLimit = 5, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(5, 30), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(5, 50), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return new WaitForSecondsRealtime(2f);
            Assert.AreEqual(1920, Screen.width);
            Assert.AreEqual(1080, Screen.height);
            Assert.AreEqual(5, ui.Hand.Widgets.Count);
            Assert.AreEqual(5, ui.OwnField.Widgets.Count);
            Assert.AreEqual(5, ui.EnemyField.Widgets.Count);
            Assert.IsTrue(ui.EndTurn.gameObject.activeInHierarchy);
            Canvas.ForceUpdateCanvases();
            foreach (var target in new[] { ui.Hand.Widgets[0].transform, ui.OwnField.Widgets[0].transform, ui.EndTurn.transform })
            {
                var rect = (RectTransform)target;
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                };
                var hits = new List<RaycastResult>();
                ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(target), target.name + " is blocked");
            }
            var corners = new Vector3[4];
            ((RectTransform)backdrop.transform).GetWorldCorners(corners);
            Assert.That(corners[0].x, Is.EqualTo(0).Within(1));
            Assert.That(corners[0].y, Is.EqualTo(0).Within(1));
            Assert.That(corners[2].x, Is.EqualTo(1920).Within(1));
            Assert.That(corners[2].y, Is.EqualTo(1080).Within(1));

            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory("Artifacts/MoonlitArena");
                File.WriteAllBytes("Artifacts/MoonlitArena/1920x1080.png", screenshot.EncodeToPNG());
                Object.Destroy(screenshot);
            }

            var empty = SceneManager.CreateScene("MoonlitArenaCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static CardView[] Cards(int count, int first)
        {
            return Enumerable.Range(first, count).Select(id => new CardView
            {
                Id = id, DefinitionId = 3001, Attack = 3, Hp = 3, MaxHp = 3,
            }).ToArray();
        }
    }
}
#endif
