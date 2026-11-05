#if UNITY_EDITOR
using System;
using System.Collections;
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
    public sealed class CardAuraPlayModeTests
    {
        private BattlePresenter _ui;
        private bool _before;

        [UnityTest]
        public IEnumerator CardSilhouetteAvailabilityHoverSelectionTargetsAndRestoration()
        {
            _before = Environment.GetCommandLineArgs().Contains("-cardAuraBefore");
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            _ui = Object.FindFirstObjectByType<BattlePresenter>();
            _ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"aura\",\"PlayerId\":1,\"GameNumber\":1}");
            _ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "aura", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView { Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3, FieldLimit = 10,
                    Players = new[] {
                        new PlayerView { PlayerId = 1, Life = 20, Hand = Cards(5, 1), Field = Cards(3, 30), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 18, HandCount = 5, Hand = Array.Empty<CardView>(), Field = Cards(3, 50), Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                    } },
            }));
            yield return new WaitForSecondsRealtime(2.3f);
            _ui.enabled = false;
            _ui.SkipPresentation();
            var click = _ui.Hand.Widgets[0].Clicked;
            var hover = _ui.Hand.Widgets[0].Hovered;
            var leave = _ui.Hand.Widgets[0].HoverLeft;
            var pointer = new PointerEventData(EventSystem.current);
            foreach (int count in new[] { 1, 3, 5, 10 })
            {
                _ui.Hand.Render(Cards(count, 1), 0, false, 0, false, click, hover, leave);
                _ui.Hand.Layout.SetExpanded(false);
                foreach (var card in _ui.Hand.Widgets.Concat(_ui.OwnField.Widgets))
                {
                    Permissions(card, true);
                }
                _ui.Hand.Layout.Snap();
                yield return new WaitForSecondsRealtime(.35f);
                yield return Capture("hand-" + count);
                if (!_before)
                {
                    foreach (var card in _ui.Hand.Widgets)
                    {
                        Assert.IsFalse(card.Accent.enabled, "The obsolete full-height status strip must be replaced, not clipped.");
                        Assert.AreEqual("HandAura", Glow(card).sprite.name);
                        Assert.IsFalse(Glow(card).raycastTarget);
                        Assert.IsFalse(Glow(card).GetComponentInParent<Mask>() != null);
                        Assert.That(Glow(card).rectTransform.localScale, Is.EqualTo(Vector3.one));
                        if (card.Card.CanSummon)
                        {
                            Assert.Greater(Glow(card).color.a, .8f);
                        }
                        else
                        {
                            Assert.AreEqual(0, Glow(card).color.a);
                        }
                        AssertInside(card.Face);
                    }
                }
                if (count != 5)
                {
                    continue;
                }
                var chosen = _ui.Hand.Widgets[2];
                Vector2 rest = chosen.Face.anchoredPosition;
                Vector3 scale = chosen.Face.localScale;
                var usable = Glow(chosen).color;
                Permissions(chosen, false);
                if (!_before)
                {
                    Assert.AreEqual(0, Glow(chosen).color.a, "Input lock suppresses server availability.");
                }
                yield return Capture("input-locked");
                Permissions(chosen, true);
                var model = chosen.Card;
                model.CanSummon = false;
                chosen.Bind(model, false, false, false, true);
                if (!_before)
                {
                    Assert.AreEqual(0, Glow(chosen).color.a, "A server-disabled card must stop glowing immediately.");
                }
                yield return Capture("server-unavailable");
                chosen.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(.3f);
                yield return Capture("hover-unavailable");
                chosen.OnPointerExit(pointer);
                model.CanSummon = true;
                chosen.Bind(model, false, false, false, true);
                chosen.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(.4f);
                if (!_before)
                {
                    Assert.Greater(Glow(chosen).color.a, usable.a);
                    Assert.AreEqual(chosen.transform.parent.childCount - 1, chosen.transform.GetSiblingIndex());
                }
                yield return Capture("hover");
                chosen.OnPointerExit(pointer);
                chosen.SetSelected(true);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.Greater(Glow(chosen).color.r, Glow(chosen).color.g);
                yield return Capture("selected");
                chosen.SetHint(TargetHint.Unavailable);
                _ui.EnemyField.Widgets[0].SetHint(TargetHint.Available);
                _ui.EnemyField.Widgets[1].SetHint(TargetHint.Focused);
                _ui.EnemyField.Widgets[2].SetHint(TargetHint.Unavailable);
                if (!_before)
                {
                    Assert.Greater(Glow(chosen).color.a, 0, "The selected source remains marked while choosing its target.");
                    Assert.Greater(Glow(_ui.EnemyField.Widgets[0]).color.b, Glow(_ui.EnemyField.Widgets[0]).color.r);
                    Assert.AreEqual(0, Glow(_ui.EnemyField.Widgets[2]).color.a);
                    Assert.AreEqual("FieldAura", Glow(_ui.OwnField.Widgets[0]).sprite.name);
                }
                yield return Capture("target-selection");
                foreach (var card in _ui.EnemyField.Widgets)
                {
                    card.SetHint(TargetHint.None);
                }
                chosen.SetHint(TargetHint.None);
                chosen.SetSelected(false);
                chosen.ResetPointer();
                yield return new WaitForSecondsRealtime(.4f);
                Assert.Less(Vector2.Distance(rest, chosen.Face.anchoredPosition), .1f);
                Assert.Less(Vector3.Distance(scale, chosen.Face.localScale), .01f);
                yield return Capture("restored");
                var field = _ui.OwnField.Widgets[0];
                var fieldRest = field.Face.anchoredPosition;
                var fieldScale = field.Face.localScale;
                field.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.Greater(field.Face.localScale.x, fieldScale.x);
                yield return Capture("field-hover");
                field.OnPointerExit(pointer);
                field.SetSelected(true);
                yield return new WaitForSecondsRealtime(.3f);
                yield return Capture("field-selected");
                field.SetSelected(false);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.Less(Vector2.Distance(fieldRest, field.Face.anchoredPosition), .1f);
                Assert.Less(Vector3.Distance(fieldScale, field.Face.localScale), .01f);
                if (!_before)
                {
                    chosen.Bind(model, true, true, true, true);
                    chosen.Pulse();
                    chosen.SetHint(TargetHint.Focused);
                    Assert.AreEqual(0, Glow(chosen).color.a, "A hidden card cannot leak a selected/pulsed aura.");
                    chosen.Bind(model, false, false, false, true);
                    chosen.SetInFlight(true);
                    Assert.AreEqual(0, Glow(chosen).color.a, "A draw flight must not inherit a stale selected aura.");
                    chosen.SetInFlight(false);
                }
            }
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(2560, 1080) })
            {
                SetResolution(resolution.x, resolution.y);
                _ui.Hand.Render(Cards(5, 1), 0, false, 0, false, click, hover, leave);
                foreach (var card in _ui.Hand.Widgets)
                {
                    Permissions(card, true);
                }
                yield return new WaitForSecondsRealtime(.4f);
                _ui.Hand.Widgets[2].OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(.35f);
                foreach (var card in _ui.Hand.Widgets)
                {
                    AssertInside(card.Face);
                }
                yield return Capture("ratio-" + resolution.x + "x" + resolution.y);
                _ui.Hand.Widgets[2].OnPointerExit(pointer);
            }
            BattlefieldScenePlayModeTests.SetResolution();
            _ui.enabled = true;
            yield return null;
            yield return null;
            if (!_before)
            {
                Assert.IsTrue(_ui.Hand.Widgets.All(card => Glow(card).color.a == 0), "Disconnected live UI must use coordinator permissions rather than preview availability.");
            }
            var empty = SceneManager.CreateScene("AuraCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private static void Permissions(CardWidget card, bool allowed)
        {
            var method = typeof(CardWidget).GetMethod("SetActionPermissions");
            method.Invoke(card, method.GetParameters().Length == 3 ? new object[] { allowed, allowed, allowed } : new object[] { allowed, allowed });
        }
        private static CardView[] Cards(int count, int start)
        {
            int[] definitions = { 1001, 3001, 4002, 3004, 4005 };
            return Enumerable.Range(0, count).Select(i => new CardView { Id = start + i, DefinitionId = definitions[i % definitions.Length], Attack = 1 + i, Hp = 2 + i, MaxHp = 2 + i, CanSummon = i % 2 == 0, CanAttack = i % 2 == 0 }).ToArray();
        }
        private static Image Glow(CardWidget card) => card.GetComponentsInChildren<Image>(true).Single(image => image.name == "FrameGlow");
        private static void AssertInside(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(point.x, Is.InRange(-1, Screen.width + 1));
                Assert.That(point.y, Is.InRange(-1, Screen.height + 1));
            }
        }
        private IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                string directory = "Artifacts/CardAura/" + (_before ? "Before" : "After");
                Directory.CreateDirectory(directory);
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(directory + "/" + name + ".png", image.EncodeToPNG());
                Object.Destroy(image);
            }
        }
        private static void SetResolution(int width, int height)
        {
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Aura " + width + "x" + height }, null);
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
