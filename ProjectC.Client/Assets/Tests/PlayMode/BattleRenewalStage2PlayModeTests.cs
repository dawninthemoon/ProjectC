#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    public sealed class BattleRenewalStage2PlayModeTests
    {
        [UnityTest]
        public IEnumerator SceneFitsHandsAndKeepsDecorationsOutOfRaycasts()
        {
            SetResolution(1920, 1080);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            Assert.IsNotNull(ui);
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"renewal-preview\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "renewal-preview", GameNumber = 1, SequenceNumber = 1,
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
            yield return new WaitForSecondsRealtime(.6f);
            foreach (string name in new[] { "YourDeck", "OwnGraveBrowse", "OwnExileBrowse", "EnemyGraveBrowse", "EnemyExileBrowse" })
            {
                var target = ui.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
                var rect = (RectTransform)target;
                var pointer = new PointerEventData(EventSystem.current)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
                };
                var hits = new List<RaycastResult>();
                ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(target), $"{name} is blocked");
            }
            var guide = ui.GetComponentsInChildren<Transform>(true).First(item => item.name == "ArenaGuides").GetComponent<Image>();
            Assert.IsFalse(guide.raycastTarget);
            Assert.IsNotNull(guide.sprite);
            var enemyBadge = ui.EnemyPlayer.GetComponentsInChildren<Transform>(true).First(item => item.name == "LifeBadge") as RectTransform;
            foreach (var fieldCard in ui.EnemyField.Widgets)
            {
                Assert.IsFalse(ScreenBounds(fieldCard.Face).Overlaps(ScreenBounds(enemyBadge)), "Opponent HP must remain visible.");
            }
            foreach (var dimensions in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(1920, 1200), new Vector2Int(2520, 1080) })
            {
                SetResolution(dimensions.x, dimensions.y);
                yield return new WaitForSecondsRealtime(.25f);
                Assert.AreEqual(dimensions.x, Screen.width);
                Assert.AreEqual(dimensions.y, Screen.height);
                foreach (int count in new[] { 0, 3, 5, 8, 10 })
                {
                    ui.Hand.Render(Cards(count, 1), 0, false, 0, false, null, null, null);
                    ui.SetHandExpanded(true);
                    yield return new WaitForSecondsRealtime(.4f);
                    if (count > 0)
                    {
                        ui.Hand.Widgets[count - 1].SetSelected(true);
                        yield return new WaitForSecondsRealtime(.3f);
                    }
                    Canvas.ForceUpdateCanvases();
                    var blockers = new[] { (RectTransform)ui.OwnPlayer.transform, (RectTransform)ui.EndTurn.transform,
                        (RectTransform)ui.GetComponentsInChildren<Transform>(true).First(item => item.name == "OwnResources") };
                    var fieldBounds = ui.OwnField.Widgets.Select(card => ScreenBounds(card.Face)).ToArray();
                    foreach (var widget in ui.Hand.Widgets)
                    {
                        var bounds = ScreenBounds(widget.Face);
                        Assert.GreaterOrEqual(bounds.xMin, -1, $"{dimensions} {count} left");
                        Assert.GreaterOrEqual(bounds.yMin, -1, $"{dimensions} {count} bottom");
                        Assert.LessOrEqual(bounds.xMax, Screen.width + 1, $"{dimensions} {count} right");
                        Assert.LessOrEqual(bounds.yMax, Screen.height + 1, $"{dimensions} {count} top");
                        foreach (var fieldCard in fieldBounds)
                        {
                            Assert.IsFalse(bounds.Overlaps(fieldCard), $"{dimensions} {count} overlaps own field");
                        }
                        foreach (var blocker in blockers)
                        {
                            Assert.IsFalse(bounds.Overlaps(ScreenBounds(blocker)), $"{dimensions} {count} overlaps {blocker.name}");
                        }
                    }
                    if (!Application.isBatchMode && (count == 5 || count == 10))
                    {
                        yield return new WaitForEndOfFrame();
                        var texture = ScreenCapture.CaptureScreenshotAsTexture();
                        Directory.CreateDirectory("Artifacts/BattleRenewalStage2");
                        File.WriteAllBytes($"Artifacts/BattleRenewalStage2/{dimensions.x}x{dimensions.y}-hand{count}.png", texture.EncodeToPNG());
                        Object.Destroy(texture);
                    }
                }
            }
            var empty = SceneManager.CreateScene("RenewalStage2Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static Rect ScreenBounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static CardView[] Cards(int count, int first)
        {
            return Enumerable.Range(first, count).Select(id => new CardView
            {
                Id = id, DefinitionId = 3001, Attack = 3, Hp = 3, MaxHp = 3,
            }).ToArray();
        }

        private static void SetResolution(int width, int height)
        {
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kind = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(kind, 1), width, height, $"Renewal {width}x{height}" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null)
                + (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null) - 1;
            var viewType = assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, index);
            window.Show();
        }
    }
}
#endif
