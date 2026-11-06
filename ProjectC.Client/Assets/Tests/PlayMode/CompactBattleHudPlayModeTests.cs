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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class CompactBattleHudPlayModeTests
    {
        [UnityTest]
        public IEnumerator TimersCostsTooltipsNamesAndRaycastsFollowLivePresentation()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"compact\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(ui, 1, 3, true, 60000);
            yield return new WaitForSecondsRealtime(1.5f);
            var turnHud = ui.GetComponentInChildren<BattleTurnHudView>();
            var ring = Find(turnHud.transform, "TimerTicksOnImage").GetComponent<Image>();
            Assert.AreEqual(Image.Type.Filled, ring.type);
            Assert.AreEqual(Image.FillMethod.Radial360, ring.fillMethod);
            Assert.AreEqual("3", ui.TurnLabel.text);
            Assert.That(int.Parse(Find(ui.transform, "TimerText").GetComponent<TMP_Text>().text), Is.InRange(57, 60));
            float before = ring.fillAmount;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.Less(ring.fillAmount, before);
            Snapshot(ui, 2, 3, true, 30000);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(ring.fillAmount, Is.InRange(.45f, .55f));
            Snapshot(ui, 3, 4, true, 45000);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual("4", ui.TurnLabel.text);
            Assert.Greater(ring.fillAmount, .95f);
            Assert.IsFalse(ui.LogToggle.GetComponentsInChildren<TMP_Text>(true).Any(text => text.gameObject.activeInHierarchy));
            Assert.IsFalse(ui.LobbyToggle.GetComponentsInChildren<TMP_Text>(true).Any(text => text.gameObject.activeInHierarchy));
            AssertHit(ui, ui.LogToggle.GetComponent<RectTransform>());
            AssertHit(ui, ui.LobbyToggle.GetComponent<RectTransform>());
            ui.LogToggle.onClick.Invoke();
            Assert.IsTrue(ui.Log.IsOpen);
            ui.LogToggle.onClick.Invoke();
            Assert.IsFalse(ui.Log.IsOpen);
            Assert.AreEqual("25", Find(ui.transform, "OwnDeckCountText").GetComponent<TMP_Text>().text);
            Assert.AreEqual("ExileIcon", Find(ui.transform, "OwnExileIcon").GetComponent<Image>().sprite.name);
            var list = ui.GetComponentsInChildren<BattleSpecialCostsView>(true).Single();
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("2/10", list.Items[0].Amount);
            Assert.IsFalse(Find(ui.transform, "YokaiPanel").gameObject.activeSelf);
            yield return new WaitForSecondsRealtime(2f);
            yield return Capture("battle-1920");
            var definition = list.Items[0].Data.Definition;
            var inactiveParent = new GameObject("Inactive cost pool");
            inactiveParent.SetActive(false);
            var pooled = Object.Instantiate(AssetDatabase.LoadAssetAtPath<BattleCostItemView>("Assets/Prefabs/Battle/BattleCostItem.prefab"), inactiveParent.transform);
            pooled.Bind(list, new BattleCostDisplay(definition, 7, 10));
            Assert.AreEqual("7/10", pooled.Amount, "The first resource can arrive after an initially empty/hidden list.");
            Object.DestroyImmediate(inactiveParent);
            ui.enabled = false;
            var pointer = new PointerEventData(EventSystem.current);
            foreach (int count in new[] { 0, 1, 2, 6 })
            {
                var rows = Enumerable.Range(0, count).Select(i => new BattleCostDisplay(definition, i + 2, i == 0 ? 10 : (int?)null)).ToArray();
                list.Render(rows);
                yield return null;
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(count > 0, list.gameObject.activeSelf);
                Assert.AreEqual(count, list.Count);
                if (count > 0)
                {
                    Assert.AreEqual("2/10", list.Items[0].Amount);
                    AssertHit(ui, list.Items[0].GetComponent<RectTransform>());
                    list.Items[0].OnPointerEnter(pointer);
                    Assert.IsTrue(list.TooltipVisible);
                    Assert.AreEqual(definition.Name, Find(list.transform, "CostTooltipTitleText").GetComponent<TMP_Text>().text);
                    AssertInside(Find(list.transform, "CostTooltipImage"));
                    yield return Capture("costs-" + count);
                    list.Items[0].OnPointerExit(pointer);
                    Assert.IsFalse(list.TooltipVisible);
                    list.Toggle();
                    Assert.IsTrue(list.IsCollapsed);
                    Assert.AreEqual(32, ((RectTransform)list.transform).rect.height);
                    Assert.IsFalse(list.Scroll.gameObject.activeSelf);
                    yield return Capture("costs-collapsed");
                    list.Toggle();
                    Assert.IsFalse(list.IsCollapsed);
                    if (count > 4)
                    {
                        Assert.Greater(list.Scroll.content.rect.height, list.Scroll.viewport.rect.height);
                        list.Scroll.OnScroll(new PointerEventData(EventSystem.current) { scrollDelta = new Vector2(0, -4) });
                        Assert.Less(list.Scroll.verticalNormalizedPosition, 1f);
                        list.Scroll.verticalNormalizedPosition = 0;
                        yield return null;
                        Assert.That(list.Scroll.verticalNormalizedPosition, Is.LessThan(.01f));
                        yield return Capture("costs-scrolled");
                    }
                }
            }
            var testResource = ScriptableObject.CreateInstance<BattleCostDefinition>();
            var definitionData = new SerializedObject(testResource);
            definitionData.FindProperty("_id").stringValue = "test-rp-only";
            definitionData.FindProperty("_nameKey").stringValue = "ui_battle_rp";
            definitionData.FindProperty("_descriptionKey").stringValue = "ui_battle_special_resource";
            definitionData.FindProperty("_icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/BattleReferenceMatch/ExileIcon.png");
            definitionData.ApplyModifiedPropertiesWithoutUndo();
            list.Render(new[] { new BattleCostDisplay(definition, 2, 10), new BattleCostDisplay(testResource, 0) });
            Assert.AreEqual("0", list.Items[1].Amount, "A maximum is optional; rows must not invent one.");
            Assert.AreEqual("test-rp-only", list.Items[1].Data.Definition.Id);
            list.Items[1].OnPointerEnter(pointer);
            yield return Capture("two-types-preview-only");
            list.Items[1].OnPointerExit(pointer);
            var locale = CardPresentation.Library.Localization;
            string previousLanguage = locale.Language;
            string[] names = { "백귀야행－달빛 아래 피어난 구미호", "月光の下に咲く九尾の幻想物語", "Moonlit Chronicle - Frostbound Archivist" };
            int languageIndex = 0;
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                locale.Language = language;
                turnHud.Render(4, 1, true, 43);
                var card = ui.Hand.Widgets[0];
                card.Title.text = names[languageIndex++];
                ui.PhaseLabel.text = BattleText.Get("ui_battle_main_phase_2");
                var action = ui.EndTurn.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Label");
                action.text = BattleText.Get("ui_battle_end_turn");
                list.Items[0].OnPointerEnter(pointer);
                yield return null;
                yield return null;
                card.Title.ForceMeshUpdate();
                action.ForceMeshUpdate();
                ui.PhaseLabel.ForceMeshUpdate();
                Assert.IsFalse(card.Title.isTextOverflowing, language + ": " + card.Title.text);
                Assert.IsFalse(action.isTextOverflowing, language + " turn action");
                Assert.IsFalse(ui.PhaseLabel.isTextOverflowing, language + " phase");
                var plate = Find(card.Face, "RenewalTitle");
                var corners = new Vector3[4];
                card.Title.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Assert.IsTrue(plate.rect.Contains(plate.InverseTransformPoint(corner)), "Title rect escapes the actual name plate.");
                }
                Assert.Greater(card.Title.transform.GetSiblingIndex(), Find(card.Face, "CardFrameImage").GetSiblingIndex());
                AssertInside(Find(list.transform, "CostTooltipImage"));
                foreach (string name in new[] { "CostTooltipTitle", "CostTooltipAmount", "CostTooltipDescription" })
                {
                    var text = Find(list.transform, name).GetComponent<TMP_Text>();
                    text.ForceMeshUpdate();
                    Assert.IsFalse(text.isTextOverflowing, language + " tooltip: " + name);
                }
                yield return Capture("language-" + language);
                list.Items[0].OnPointerExit(pointer);
            }
            locale.Language = previousLanguage;
            var clicked = ui.Hand.Widgets[0].Clicked;
            var hovered = ui.Hand.Widgets[0].Hovered;
            var leave = ui.Hand.Widgets[0].HoverLeft;
            ui.Hand.Render(Cards(10, 1), 0, false, 0, false, clicked, hovered, leave);
            yield return new WaitForSecondsRealtime(.5f);
            foreach (var card in ui.Hand.Widgets)
            {
                AssertInside(card.Face);
            }
            ui.Hand.Widgets[4].OnPointerEnter(pointer);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsTrue(ui.Detail.gameObject.activeSelf);
            AssertInside(ui.Hand.Widgets[4].Face);
            AssertInside((RectTransform)ui.Detail.transform);
            yield return Capture("hand10-hover");
            ui.Hand.Widgets[4].OnPointerExit(pointer);
            ui.Hand.Render(Cards(5, 1), 0, false, 0, false, clicked, hovered, leave);
            foreach (var resolution in new[] { new Vector2Int(1920, 1200), new Vector2Int(2560, 1080), new Vector2Int(2560, 1440) })
            {
                SetResolution(resolution.x, resolution.y);
                yield return new WaitForSecondsRealtime(.5f);
                Assert.AreEqual(resolution.x, Screen.width);
                AssertInside((RectTransform)turnHud.transform);
                AssertInside(ui.EndTurn.GetComponent<RectTransform>());
                AssertInside(ui.LogToggle.GetComponent<RectTransform>());
                list.Items[0].OnPointerEnter(pointer);
                AssertInside(Find(list.transform, "CostTooltipImage"));
                yield return Capture("ratio-" + resolution.x + "x" + resolution.y);
                list.Items[0].OnPointerExit(pointer);
            }
            BattlefieldScenePlayModeTests.SetResolution();
            yield return null;
            ui.enabled = true;
            Snapshot(ui, 4, 4, false, 0, 0);
            yield return null;
            Assert.AreEqual("--", Find(ui.transform, "TimerText").GetComponent<TMP_Text>().text);
            Assert.AreEqual(0, ring.fillAmount);
            Assert.AreEqual(0, list.Count);
            Assert.IsFalse(list.gameObject.activeSelf);
            var empty = SceneManager.CreateScene("CompactCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
            Object.DestroyImmediate(testResource);
        }

        private static void Snapshot(BattlePresenter ui, int sequence, int turn, bool timer, long duration, int maximum = 10)
        {
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "compact", GameNumber = 1, SequenceNumber = sequence, ServerTimeUnixMs = 100000,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = turn, Phase = (int)BattlePhaseKind.Main2,
                    FieldLimit = 10, MaxYokaiPower = maximum, TurnTimerRunning = timer, TurnEndsAtUnixMs = timer ? 100000 + duration : 0,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 18, DeckCount = 25, YokaiPower = 2, Hand = Cards(5, 1), Field = Cards(3, 40), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 20, DeckCount = 25, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 60), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    },
                },
            }));
        }
        private static CardView[] Cards(int count, int start)
        {
            int[] ids = { 1001, 3001, 4002, 3004, 4005 };
            return Enumerable.Range(0, count).Select(i => new CardView { Id = start + i, DefinitionId = ids[i % ids.Length], Attack = 1 + i, Hp = 2 + i, MaxHp = 2 + i }).ToArray();
        }
        private static RectTransform Find(Transform root, string name) => (RectTransform)root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
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
        private static void AssertHit(BattlePresenter ui, RectTransform target)
        {
            var hits = new List<RaycastResult>();
            ui.GetComponent<GraphicRaycaster>().Raycast(new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center)),
            }, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(target), target.name + " is blocked");
        }
        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                Directory.CreateDirectory("Artifacts/CompactBattleHud");
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/CompactBattleHud/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }
        private static void SetResolution(int width, int height)
        {
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Compact HUD " + width + "x" + height }, null);
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
