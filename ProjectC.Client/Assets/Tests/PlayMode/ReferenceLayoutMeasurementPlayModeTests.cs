#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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
    public sealed class ReferenceLayoutMeasurementPlayModeTests
    {
        [UnityTest]
        public IEnumerator MeasureReferenceLayout()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            // Keep the host mouse position from changing the measured card through Hover.
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"reference-measure\",\"PlayerId\":1,\"GameNumber\":1}");
            ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "reference-measure", GameNumber = 1, SequenceNumber = 1,
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
            Assert.AreEqual("2/10", Find(ui.transform, "YokaiValue").GetComponent<TMP_Text>().text);
            Assert.AreEqual("0/10", Find(ui.transform, "EnemyYokaiValue").GetComponent<TMP_Text>().text);
            Canvas.ForceUpdateCanvases();
            Directory.CreateDirectory("Artifacts/ReferenceLayout");
            var lines = new StringBuilder();
            foreach (var rect in ui.GetComponentsInChildren<RectTransform>(true))
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var points = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(null, corner)).ToArray();
                var min = new Vector2(points.Min(point => point.x), points.Min(point => point.y));
                var max = new Vector2(points.Max(point => point.x), points.Max(point => point.y));
                var image = rect.GetComponent<Image>();
                lines.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}\t{1}\t{2:F1}\t{3:F1}\t{4:F1}\t{5:F1}\t{6}\n",
                    PathOf(rect), rect.gameObject.activeInHierarchy, min.x, Screen.height - max.y,
                    max.x - min.x, max.y - min.y, image != null && image.sprite != null ? image.sprite.name : "");
            }
            File.WriteAllText("Artifacts/ReferenceLayout/measurement.tsv", lines.ToString());
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/ReferenceLayout/screen.png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
            var reference = JObject.Parse(File.ReadAllText("Assets/Tests/Fixtures/ReferenceLayout1920.json"));
            var elements = reference["elements"].ToDictionary(item => (string)item["id"]);
            var mapped = new Dictionary<string, RectTransform>
            {
                ["turn_button"] = (RectTransform)ui.EndTurn.transform,
                ["turn_phase_pill"] = Find(ui.EndTurn.transform, "TurnPhasePlate"),
                ["enemy_portrait"] = (RectTransform)ui.EnemyPlayer.transform,
                ["enemy_hp_badge"] = Find(ui.EnemyPlayer.transform, "LifeBadge"),
                ["hero_portrait"] = (RectTransform)ui.OwnPlayer.transform,
                ["hero_hp_badge"] = Find(ui.OwnPlayer.transform, "LifeBadge"),
                ["enemy_yokai_pill"] = Find(ui.transform, "EnemyYokaiPanel"),
                ["yokai_tray_row_1"] = Find(ui.transform, "YokaiRow"),
            };
            for (int i = 0; i < 3; i++)
            {
                mapped["field_enemy_c" + (i + 1)] = ui.EnemyField.Widgets[i].Face;
                mapped["field_ally_c" + (i + 1)] = ui.OwnField.Widgets[i].Face;
                mapped["resource_top_" + (i + 1)] = Find(Find(ui.transform, "EnemyResources"), "RenewalZone" + i);
                mapped["resource_bottom_" + (i + 1)] = Find(Find(ui.transform, "OwnResources"), "RenewalZone" + i);
            }
            var comparison = new StringBuilder("id\ttarget x,y,w,h\tactual x,y,w,h\tdelta x,y,w,h\n");
            foreach (var pair in mapped)
            {
                var expected = elements[pair.Key]["rect"];
                var actual = ScreenRect(pair.Value);
                CheckRect(pair.Key, actual, (float)expected["x"], (float)expected["y"], (float)expected["w"], (float)expected["h"], comparison);
            }
            var backs = Union(ui.EnemyHand.Widgets.Select(card => card.Face));
            var backsTarget = elements["enemy_deck_backs"]["rect"];
            CheckRect("enemy_deck_backs", backs, (float)backsTarget["x"], (float)backsTarget["y"], (float)backsTarget["w"], (float)backsTarget["h"], comparison);
            var middleHand = ScreenRect(ui.Hand.Widgets[2].Face);
            Assert.That(middleHand.width, Is.EqualTo(205).Within(1), "hand width");
            Assert.That(middleHand.height, Is.EqualTo(262).Within(1), "hand height");
            var handBounds = Union(ui.Hand.Widgets.Select(card => card.Face));
            Assert.That(handBounds.center.x, Is.EqualTo(1037).Within(6), "hand center");
            Assert.That(ui.Hand.Layout.Spacing, Is.EqualTo(185).Within(1), "hand pitch");
            var handTarget = elements["hand_bbox_5cards"]["rect"];
            CheckRect("hand_bbox_5cards", handBounds, (float)handTarget["x"], (float)handTarget["y"],
                (float)handTarget["w"], (float)handTarget["h"], comparison);
            var label = ui.EndTurn.GetComponentsInChildren<TMP_Text>(true).First(item => item.name == "Label");
            CheckPoint("turn_main_text_center", ScreenRect(label.rectTransform).center, elements, comparison);
            CheckPoint("turn_arrow_center", ScreenRect(Find(ui.EndTurn.transform, "TurnArrow")).center, elements, comparison);
            var frameRect = ScreenRect(ui.OwnField.Widgets[1].Face);
            var badges = elements["field_card_badges_relative"];
            foreach (var pair in new[] { (name: "attack_badge", node: "AttackBadgeFrame"), (name: "health_badge", node: "HealthBadgeFrame"), (name: "keyword_slot", node: "FieldStatusFrame") })
            {
                var target = badges[pair.name];
                var actual = ScreenRect(Find(ui.OwnField.Widgets[1].Face, pair.node));
                CheckRect(pair.name, actual, frameRect.x + (float)target["cx"] - (float)target["w"] / 2,
                    frameRect.y + (float)target["cy"] - (float)target["h"] / 2, (float)target["w"], (float)target["h"], comparison);
            }
            File.WriteAllText("Artifacts/ReferenceLayout/comparison.tsv", comparison.ToString());
            AssertHit(ui, (RectTransform)ui.OwnPlayer.transform, Vector2.zero);
            AssertHit(ui, (RectTransform)ui.EnemyPlayer.transform, Vector2.zero);
            AssertHit(ui, ui.OwnField.Widgets[0].Face, new Vector2(-53, -106));
            var fieldClick = ui.OwnField.Widgets[0].Clicked;
            var fieldHover = ui.OwnField.Widgets[0].Hovered;
            var fieldLeave = ui.OwnField.Widgets[0].HoverLeft;
            foreach (int count in new[] { 1, 5, 10 })
            {
                ui.OwnField.Render(Cards(count, 40), count, false, 0, false, fieldClick, fieldHover, fieldLeave);
                yield return new WaitForSecondsRealtime(.4f);
                foreach (var card in ui.OwnField.Widgets)
                {
                    AssertInside(card.Face);
                }
                AssertHit(ui, ui.OwnField.Widgets[count / 2].Face, Vector2.zero);
                yield return Capture("field-" + count);
            }
            ui.OwnField.Render(Cards(3, 40), 3, false, 0, false, fieldClick, fieldHover, fieldLeave);
            yield return new WaitForSecondsRealtime(.4f);

            var click = ui.Hand.Widgets[0].Clicked;
            var hover = ui.Hand.Widgets[0].Hovered;
            var leave = ui.Hand.Widgets[0].HoverLeft;
            foreach (int count in new[] { 1, 5, 10 })
            {
                ui.Hand.Render(Cards(count, 1), 0, false, 0, false, click, hover, leave);
                yield return new WaitForSecondsRealtime(.5f);
                foreach (var card in ui.Hand.Widgets)
                {
                    AssertInside(card.Face);
                }
                AssertHit(ui, ui.Hand.Widgets[count - 1].Face, Vector2.zero);
                yield return Capture("hand-" + count);
            }
            var focused = ui.Hand.Widgets[4];
            focused.OnPointerEnter(new PointerEventData(EventSystem.current));
            yield return new WaitForSecondsRealtime(.3f);
            Assert.IsTrue(focused.IsHovered);
            Assert.IsTrue(ui.Detail.gameObject.activeInHierarchy);
            AssertInside(focused.Face);
            yield return Capture("hand-hover-detail");
            var viewport = Find(ui.Detail.transform, "DetailViewport");
            var content = Find(ui.Detail.transform, "DetailContent");
            ui.Detail.Rules.text = string.Join(" ", Enumerable.Repeat("긴 카드 효과 설명", 100));
            typeof(CardDetailView).GetMethod("RefreshHeight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(ui.Detail, null);
            Canvas.ForceUpdateCanvases();
            Assert.LessOrEqual(((RectTransform)ui.Detail.transform).rect.height, 480.1f);
            Assert.Greater(content.rect.height, viewport.rect.height);
            Assert.IsTrue(viewport.GetComponent<ScrollRect>().vertical);
            focused.OnPointerExit(new PointerEventData(EventSystem.current));

            ui.enabled = false;
            var locale = CardPresentation.Library.Localization;
            string previousLanguage = locale.Language;
            foreach (string language in new[] { "korean", "english", "japanese" })
            {
                locale.Language = language;
                label.text = BattleText.Get("ui.phase.to_main2");
                ui.PhaseLabel.text = BattleText.Get("ui.phase.main1");
                foreach (bool enabled in new[] { true, false })
                {
                    ui.EndTurn.interactable = enabled;
                    yield return new WaitForSecondsRealtime(.15f);
                    label.ForceMeshUpdate();
                    ui.PhaseLabel.ForceMeshUpdate();
                    Assert.IsFalse(label.isTextOverflowing, language + " action text overflows");
                    Assert.IsFalse(ui.PhaseLabel.isTextOverflowing, language + " phase text overflows");
                    yield return Capture("turn-" + language + (enabled ? "-active" : "-disabled"));
                }
            }
            locale.Language = previousLanguage;
            foreach (int hp in new[] { 9, 18, 20, 123 })
            {
                ui.OwnPlayer.Render(new PlayerView { PlayerId = 1, Life = hp }, true, false);
                ui.EnemyPlayer.Render(new PlayerView { PlayerId = 2, Life = hp }, false, false);
                yield return null;
                foreach (var player in new[] { ui.OwnPlayer, ui.EnemyPlayer })
                {
                    player.Life.ForceMeshUpdate();
                    Assert.AreEqual(hp.ToString(), player.Life.text);
                    Assert.IsFalse(player.Life.isTextOverflowing, "HP " + hp);
                }
            }
            yield return Capture("hp-123");
            var hud = ui.GetComponentInChildren<BattleHudView>();
            hud.Render(new PlayerView(), new PlayerView(), 0);
            Assert.IsFalse(Find(ui.transform, "YokaiPanel").gameObject.activeSelf);
            Assert.IsFalse(Find(ui.transform, "EnemyYokaiPanel").gameObject.activeSelf);
            var empty = SceneManager.CreateScene("ReferenceLayoutCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static RectTransform Find(Transform root, string name)
        {
            return (RectTransform)root.GetComponentsInChildren<Transform>(true).First(item => item.name == name);
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var points = corners.Select(corner => RectTransformUtility.WorldToScreenPoint(null, corner)).ToArray();
            float left = points.Min(point => point.x);
            float right = points.Max(point => point.x);
            float top = Screen.height - points.Max(point => point.y);
            float bottom = Screen.height - points.Min(point => point.y);
            return Rect.MinMaxRect(left, top, right, bottom);
        }

        private static Rect Union(IEnumerable<RectTransform> transforms)
        {
            var rects = transforms.Select(ScreenRect).ToArray();
            return Rect.MinMaxRect(rects.Min(rect => rect.xMin), rects.Min(rect => rect.yMin),
                rects.Max(rect => rect.xMax), rects.Max(rect => rect.yMax));
        }

        private static void CheckRect(string id, Rect actual, float x, float y, float width, float height, StringBuilder output)
        {
            output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0}\t{1:F1},{2:F1},{3:F1},{4:F1}\t{5:F1},{6:F1},{7:F1},{8:F1}\t{9:F1},{10:F1},{11:F1},{12:F1}\n",
                id, x, y, width, height, actual.x, actual.y, actual.width, actual.height,
                actual.x - x, actual.y - y, actual.width - width, actual.height - height);
            Assert.That(actual.x, Is.EqualTo(x).Within(6), id + " x");
            Assert.That(actual.y, Is.EqualTo(y).Within(6), id + " y");
            Assert.That(actual.width, Is.EqualTo(width).Within(6), id + " width");
            Assert.That(actual.height, Is.EqualTo(height).Within(6), id + " height");
        }

        private static void CheckPoint(string id, Vector2 actual, Dictionary<string, JToken> elements, StringBuilder output)
        {
            var point = elements[id]["point"];
            float x = (float)point["x"];
            float y = (float)point["y"];
            output.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                "{0}\t{1:F1},{2:F1}\t{3:F1},{4:F1}\t{5:F1},{6:F1}\n", id, x, y, actual.x, actual.y, actual.x - x, actual.y - y);
            Assert.That(actual.x, Is.EqualTo(x).Within(6), id + " x");
            Assert.That(actual.y, Is.EqualTo(y).Within(6), id + " y");
        }

        private static void AssertInside(RectTransform rect)
        {
            var bounds = ScreenRect(rect);
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(-1));
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(-1));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(Screen.width + 1));
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(Screen.height + 1));
        }

        private static void AssertHit(BattlePresenter ui, RectTransform target, Vector2 offset)
        {
            var position = target.TransformPoint((Vector3)offset);
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, position),
            };
            var hits = new List<RaycastResult>();
            ui.GetComponent<GraphicRaycaster>().Raycast(pointer, hits);
            Assert.IsTrue(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(target), target.name + " is blocked");
        }

        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                var texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/ReferenceLayout/" + name + ".png", texture.EncodeToPNG());
                Object.Destroy(texture);
            }
        }

        private static string PathOf(Transform item)
        {
            string path = item.name;
            while (item.parent != null)
            {
                item = item.parent;
                path = item.name + "/" + path;
            }
            return path;
        }

        private static CardView[] Cards(int count, int first)
        {
            int[] definitions = { 1001, 3001, 4002, 3004, 4005 };
            return Enumerable.Range(0, count).Select(i => new CardView
            {
                Id = first + i, DefinitionId = definitions[i % definitions.Length], Attack = i + 1, Hp = i + 2, MaxHp = i + 2,
            }).ToArray();
        }
    }
}
#endif
