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

namespace ProjectC.Tests
{
    public sealed class BattlefieldScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator SceneBackdropPreservesOverlayRaycastsAndHandSelection()
        {
            SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var ui = UnityEngine.Object.FindFirstObjectByType<BattlePresenter>();
            Assert.IsNotNull(ui);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, ui.GetComponent<Canvas>().renderMode);
            var stage = GameObject.Find("YokaiShrineStage");
            Assert.IsNotNull(stage);
            Assert.IsEmpty(stage.GetComponentsInChildren<Collider>());
            Assert.IsEmpty(stage.GetComponentsInChildren<Rigidbody>());
            var renderer = stage.GetComponentInChildren<MeshRenderer>();
            Assert.AreEqual("Universal Render Pipeline/Lit", renderer.sharedMaterial.shader.name);
            Assert.IsTrue(renderer.sharedMaterial.shader.isSupported);
            foreach (string map in new[] { "_BaseMap", "_BumpMap", "_EmissionMap", "_MetallicGlossMap" })
            {
                Assert.IsNotNull(renderer.sharedMaterial.GetTexture(map), map);
            }
            Assert.IsTrue(Camera.main.orthographic);
            Assert.That(Vector3.Dot(Camera.main.transform.forward, Vector3.down), Is.GreaterThan(.9999f));
            Assert.That(Vector3.Dot(Camera.main.transform.up, Vector3.forward), Is.GreaterThan(.9999f));
            string modelPath = AssetDatabase.GetAssetPath(stage.GetComponentInChildren<MeshFilter>().sharedMesh);
            StringAssert.Contains("/YokaiShrine/", modelPath);
            Assert.IsFalse(stage.GetComponentsInChildren<MeshFilter>().Any(mesh =>
                AssetDatabase.GetAssetPath(mesh.sharedMesh).Contains("Battlefield_YokaiShrine_Floor")));
            Assert.Greater(renderer.bounds.size.x, renderer.bounds.size.y);
            var client = ui.Client;
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"stage-preview\",\"PlayerId\":1,\"GameNumber\":1}");
            var hand = new[] { Card(1, 3006), Card(2, 4005), Card(3, 1001), Card(4, 1002), Card(5, 1003) };
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "stage-preview", GameNumber = 1,
                SequenceNumber = 1, ServerTimeUnixMs = 100000,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, Phase = 0, TurnNumber = 3,
                    FieldLimit = 5, MaxYokaiPower = 10, TurnTimerRunning = true, TurnEndsAtUnixMs = 280000,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, HandCount = 5, Hand = hand,
                            Field = new[] { Card(6, 3001), Card(7, 3004), Card(8, 4002), Card(10, 3002), Card(11, 1001) }, Graveyard = Array.Empty<CardView>(), YokaiPower = 2 },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(),
                            Field = new[] { Card(9, 4001), Card(12, 3001), Card(13, 4002), Card(14, 3002), Card(15, 1002) }, Graveyard = Array.Empty<CardView>(), YokaiPower = 2 }
                    }
                }
            }));
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.AreEqual(1920, Screen.width);
            Assert.AreEqual(1080, Screen.height);
            Assert.AreEqual(5, ui.OwnField.Widgets.Count);
            Assert.AreEqual(5, ui.EnemyField.Widgets.Count);
            foreach (var zone in new[] { ui.Hand, ui.OwnField, ui.EnemyField })
            {
                foreach (var widget in zone.Widgets)
                {
                    var point = RectTransformUtility.WorldToScreenPoint(null, widget.WorldCenter);
                    var pointer = new PointerEventData(EventSystem.current) { position = point };
                    var hits = new List<RaycastResult>();
                    ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
                    Assert.IsNotEmpty(hits);
                    Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(widget.transform), widget.Card.Id.ToString());
                }
            }
            yield return Save("YokaiShrine-Compact-1920.png");
            ui.Hand.Widgets[0].OnPointerClick(null);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.IsFalse(ui.Hand.Layout.Compact);
            ui.Hand.Widgets[2].OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.4f);
            Assert.IsFalse(ui.Hand.Widgets[0].IsSelected);
            Assert.IsTrue(ui.Hand.Widgets[2].IsSelected);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(ui.Hand.Widgets[2].transform.eulerAngles.z, 0)), Is.LessThan(1));
            yield return Save("YokaiShrine-Expanded-1920.png");
            var area = ui.GetComponentsInChildren<BattleHitArea>().First(a => a.Kind == BattleHitArea.AreaKind.Background);
            ui.Interaction.ClickArea(area);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.IsTrue(ui.Hand.Layout.Compact);
            Assert.IsFalse(ui.Detail.gameObject.activeSelf);
            ui.gameObject.SetActive(false);
            yield return Save("YokaiShrine-Model-1920.png");
            var empty = SceneManager.CreateScene("StageValidationCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static CardView Card(int id, int definition)
        {
            var card = CardPresentation.Library.Find(definition);
            return new CardView { Id = id, DefinitionId = definition, Attack = card.Attack,
                Hp = card.MaxHp, MaxHp = card.MaxHp, CardType = card.Type == "Spell" ? 1 : 0 };
        }

        internal static IEnumerator Save(string name)
        {
            yield return new WaitForEndOfFrame();
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory("Artifacts/YokaiShrineTopDown");
            File.WriteAllBytes("Artifacts/YokaiShrineTopDown/" + name, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
        }

        internal static void SetResolution()
        {
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kind = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(kind, 1), 1920, 1080, "Stage 1920x1080" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null)
                + (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null) - 1;
            var gameViewType = assembly.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(gameViewType);
            gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(window, index);
            window.Show();
        }
    }
}
#endif
