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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class TurnLogPlayModeTests
    {
        private BattlePresenter _ui;
        private int _sequence;

        [UnityTest]
        public IEnumerator SuppliedArtworkLiveTurnModalLogPrivacyAndCardDetails()
        {
            BattlefieldScenePlayModeTests.SetResolution();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            foreach (var module in EventSystem.current.GetComponents<BaseInputModule>())
            {
                module.enabled = false;
            }
            _ui = Object.FindFirstObjectByType<BattlePresenter>();
            _ui.Client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"turnlog\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot(1, 3, 120000);
            yield return new WaitForSecondsRealtime(2.5f);
            var hud = Find(_ui.transform, "TurnHud");
            var status = Find(hud, "TurnOwner").GetComponent<TMP_Text>();
            var ring = Find(hud, "TimeRing").GetComponent<Image>();
            Assert.AreEqual("ProjectC_TurnHUD_Textless", hud.GetComponent<Image>().sprite.name);
            Assert.AreEqual("3", _ui.TurnLabel.text);
            Assert.AreEqual(BattleText.Get("ui.turn.yours"), status.text);
            Assert.AreEqual(Image.Type.Filled, ring.type);
            Assert.AreEqual(Image.FillMethod.Radial360, ring.fillMethod);
            Assert.IsFalse(ring.raycastTarget);
            var ownTint = status.color;
            float progress = ring.fillAmount;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.Less(ring.fillAmount, progress);
            yield return Capture("own-turn");
            _ui.LogToggle.onClick.Invoke();
            Assert.IsTrue(_ui.Log.IsOpen);
            Assert.IsTrue(Find(_ui.Log.History.transform, "LogEmpty").gameObject.activeInHierarchy);
            Assert.AreEqual(1, _ui.Modal.ActiveCount);
            Assert.AreEqual("ProjectC_BattleLog_PopupFrame", _ui.Log.History.GetComponent<Image>().sprite.name);
            Assert.Less(_ui.Modal.Tint.r, .1f);
            Assert.Greater(_ui.Modal.Tint.a, .5f);
            yield return Capture("log-empty");
            Find(_ui.Log.History.transform, "CloseLog").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            var events = new[]
            {
                new EventView { Type = 5, TargetPlayerId = 1, TurnNumber = 3 },
                new EventView { Type = 4, TargetPlayerId = 1, TargetCardId = 11, FromZone = 1, ToZone = 2 },
                new EventView { Type = 11, TargetPlayerId = 1, TargetCardId = 11 },
                new EventView { Type = 0, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21 },
                new EventView { Type = 1, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21, Damage = 2, HpAfter = 0 },
                new EventView { Type = 13, SourcePlayerId = 1, SourceCardId = 11 },
                new EventView { Type = 2, TargetPlayerId = 2, TargetCardId = 21 },
                // Deliberately supply a definition as bait: a redacted instance must never use it.
                new EventView { Type = 4, TargetPlayerId = 2, TargetCardId = 0, DefinitionId = 4005, FromZone = 0, ToZone = 1 },
                // Also ignore any accidental opponent hand DTOs when building the visible-card cache.
                new EventView { Type = 13, SourcePlayerId = 2, SourceCardId = 999, DefinitionId = 4005 },
            };
            Events(events);
            Snapshot(1, 3, 60000);
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(ring.fillAmount, Is.InRange(.45f, .55f));
            _ui.SkipPresentation();
            yield return null;
            Assert.AreEqual(9, _ui.Log.Entries.Count);
            var hidden = _ui.Log.Entries[7];
            Assert.IsNull(hidden.Card);
            Assert.IsNull(_ui.Log.Entries[8].Card);
            StringAssert.Contains(BattleText.Get("ui.log.hidden_card"), hidden.Text(1));
            Assert.IsFalse(hidden.Text(1).Contains(CardPresentation.Name(Card(999, 4005))));
            Assert.IsTrue(_ui.Log.Entries.Any(entry => entry.Text(1).Contains(CardPresentation.Name(Card(11, 1001)))));
            _ui.LogToggle.onClick.Invoke();
            yield return null;
            Assert.IsFalse(_ui.Client.Input.CanExecute(GameplayInputAction.Attack));
            Assert.IsFalse(_ui.Client.Input.CanExecute(GameplayInputAction.EndTurn));
            Assert.IsTrue(_ui.Client.Input.CanExecute(GameplayInputAction.OpenCardDetails));
            Assert.IsFalse(_ui.EndTurn.interactable);
            Canvas.ForceUpdateCanvases();
            var hit = Raycast(_ui.Hand.Widgets[0].Face);
            Assert.IsFalse(hit.transform.IsChildOf(_ui.Hand.transform), "Modal must block hand raycasts.");
            Assert.IsFalse(_ui.Log.GetComponent<Image>().enabled);
            var row = _ui.Log.History.GetComponentsInChildren<BattleLogItemView>().First(value => value.Entry.Card != null && value.GetComponent<Button>().interactable);
            row.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(CardPresentation.Name(row.Entry.Card), _ui.Detail.Title.text);
            Assert.AreEqual(1001, _ui.Client.State.Players[0].Field[0].DefinitionId, "Inspecting history cannot mutate match data.");
            yield return Capture("log-card-detail");
            CloseWithEscape();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.IsFalse(_ui.Detail.gameObject.activeSelf);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsTrue(_ui.Client.Input.CanExecute(GameplayInputAction.Attack));
            Snapshot(2, 4, 90000);
            yield return null;
            yield return null;
            Assert.AreEqual(BattleText.Get("ui.turn.theirs"), status.text);
            Assert.AreNotEqual(ownTint, status.color);
            Assert.IsFalse(_ui.EndTurn.interactable);
            yield return Capture("opponent-turn");
            Events(Enumerable.Range(0, 30).Select(index => new EventView { Type = index % 2 == 0 ? 13 : 1, SourcePlayerId = 1, SourceCardId = 11, TargetPlayerId = 2, TargetCardId = 21, Damage = 1, HpAfter = 1, TurnNumber = 4 }).ToArray());
            Snapshot(2, 4, 80000);
            _ui.Log.Toggle();
            yield return null;
            Assert.AreEqual(2, _ui.Log.History.GetComponentsInChildren<BattleLogItemView>().Count(value => !value.GetComponent<Button>().interactable && value.Entry.Turn > 0 && value.transform.Find("LogActor").gameObject.activeSelf == false));
            Assert.Greater(_ui.Log.Scroll.content.rect.height, _ui.Log.Scroll.viewport.rect.height);
            _ui.Log.Scroll.verticalNormalizedPosition = 1;
            yield return null;
            var oldPosition = _ui.Log.Scroll.content.anchoredPosition;
            Events(new[] { new EventView { Type = 2, TargetPlayerId = 2, TargetCardId = 21, TurnNumber = 4 } });
            Snapshot(2, 4, 70000);
            Assert.AreEqual(oldPosition, _ui.Log.Scroll.content.anchoredPosition, "New events must not pull a reader away from older actions.");
            yield return Capture("log-scrolled");
            var locale = CardPresentation.Library.Localization;
            string original = locale.Language;
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                locale.Language = language;
                yield return null;
                yield return null;
                status.ForceMeshUpdate();
                Assert.IsFalse(status.isTextOverflowing, language + " turn status");
                AssertInside(hud);
                AssertInside((RectTransform)_ui.Log.History.transform);
                yield return Capture("locale-" + language);
            }
            locale.Language = original;
            foreach (var resolution in new[] { new Vector2Int(1920, 1200), new Vector2Int(2560, 1080), new Vector2Int(1280, 720) })
            {
                SetResolution(resolution.x, resolution.y);
                yield return new WaitForSecondsRealtime(.3f);
                Assert.AreEqual(resolution.x, Screen.width);
                AssertInside(hud);
                AssertInside((RectTransform)_ui.Log.History.transform);
                yield return Capture("ratio-" + resolution.x + "x" + resolution.y);
            }
            int count = _ui.Log.Entries.Count;
            _ui.SkipPresentation();
            Assert.IsFalse(_ui.Log.IsOpen);
            Assert.AreEqual(count, _ui.Log.Entries.Count, "Restoring the same match snapshot preserves visible history.");
            _ui.Client.ApplyMessage("{\"Type\":\"SessionUpdated\",\"ProtocolVersion\":2,\"MatchId\":\"turnlog\",\"PlayerId\":1,\"GameNumber\":2}");
            _sequence = 0;
            Snapshot(1, 1, 60000, 2);
            Assert.AreEqual(0, _ui.Log.Entries.Count, "A rematch must not mix the preceding game's history.");
            BattlefieldScenePlayModeTests.SetResolution();
            var emptyScene = SceneManager.CreateScene("TurnLogCleanup");
            SceneManager.SetActiveScene(emptyScene);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private void CloseWithEscape()
        {
            var keyboard = Keyboard.current;
            bool added = keyboard == null;
            keyboard = keyboard ?? InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            InputSystem.Update();
            _ui.Interaction.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            if (added)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        private void Events(EventView[] values)
        {
            _ui.Client.ApplyMessage(JsonUtility.ToJson(new EventsMessage { Type = "GameEvents", ProtocolVersion = 2, MatchId = "turnlog", GameNumber = 1, SequenceNumber = ++_sequence, Payload = values }));
        }

        private void Snapshot(int active, int turn, long remaining, int game = 1)
        {
            _ui.Client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "turnlog", GameNumber = game, SequenceNumber = _sequence, ServerTimeUnixMs = 100000,
                Payload = new StateView { Started = true, TurnStarted = true, ActivePlayerId = active, TurnNumber = turn, Phase = 0, TurnTimerRunning = true, TurnEndsAtUnixMs = 100000 + remaining,
                    FieldLimit = 10, Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, Hand = new[] { Card(12, 3001), Card(13, 4002), Card(14, 3004), Card(15, 4005), Card(16, 1001) }, Field = new[] { Card(11, 1001) }, Graveyard = Array.Empty<CardView>(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 18, HandCount = 5, Hand = new[] { Card(999, 4005) }, Field = Array.Empty<CardView>(), Graveyard = new[] { Card(21, 3001) }, Exile = Array.Empty<CardView>() },
                    } },
            }));
        }

        private static CardView Card(int id, int definition) => new CardView { Id = id, DefinitionId = definition, Attack = 2, Hp = 3, MaxHp = 3 };
        private static RectTransform Find(Transform root, string name) => (RectTransform)root.GetComponentsInChildren<Transform>(true).First(value => value.name == name);
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
        private static GameObject Raycast(RectTransform target)
        {
            var hits = new List<RaycastResult>();
            Canvas.ForceUpdateCanvases();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center)) }, hits);
            Assert.IsNotEmpty(hits);
            return hits[0].gameObject;
        }
        private static IEnumerator Capture(string name)
        {
            if (!Application.isBatchMode)
            {
                yield return new WaitForEndOfFrame();
                Directory.CreateDirectory("Artifacts/TurnLogHud");
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes("Artifacts/TurnLogHud/" + name + ".png", image.EncodeToPNG());
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
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "TurnLog " + width + "x" + height }, null);
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
