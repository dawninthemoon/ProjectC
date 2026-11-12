#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Utils;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class CardSelectionFrameworkTests
    {
        private GameObject _root;
        private GameObject _eventSystem;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;
        private int _sequence;
        private string _language;
        private readonly List<string> _measurements = new List<string>();
        private static readonly int[] Definitions = { 1001, 4002, 3004, 2001, 1003, 4001, 3001, 1002 };

        private static CardView Card(int id, int index) => new CardView
        {
            Id = id, DefinitionId = Definitions[index % Definitions.Length], Attack = index + 1,
            Hp = index + 2, MaxHp = index + 2,
        };

        private IEnumerator Setup()
        {
            if (EventSystem.current == null)
            {
                _eventSystem = new GameObject("Selection test events", typeof(EventSystem));
            }
            _language = CardPresentation.Library.Localization.Language;
            CardPresentation.Library.Localization.Language = "korean";
            _root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
            _ui = _root.GetComponent<BattlePresenter>();
            _client = _ui.Client;
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"selection-ui\",\"PlayerId\":1,\"GameNumber\":1}");
            Snapshot();
            Directory.CreateDirectory("Artifacts/BattleSelection");
            yield return null;
            yield return null;
            foreach (var zone in _ui.Zones)
            {
                zone.Layout?.Snap();
            }
            yield return new WaitForSecondsRealtime(1.5f);
            _ui.Effects.ResetFeedback();
            yield return null;
        }

        private void Snapshot()
        {
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", MatchId = "selection-ui", GameNumber = 1, ProtocolVersion = 2,
                SequenceNumber = ++_sequence, Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3,
                    FieldLimit = 10, MaxYokaiPower = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, DeckCount = 25, HandCount = 8, YokaiPower = 2,
                            Hand = Enumerable.Range(0, 8).Select(i => Card(i + 1, i)).ToArray(),
                            Field = Enumerable.Range(0, 3).Select(i => Card(i + 20, i)).ToArray(),
                            Graveyard = Enumerable.Range(0, 8).Select(i => Card(i + 40, i + 2)).ToArray(),
                            Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 18, DeckCount = 23, HandCount = 5,
                            Hand = Array.Empty<CardView>(),
                            Field = Enumerable.Range(0, 2).Select(i => Card(i + 60, i + 2)).ToArray(),
                            Graveyard = new[] { Card(70, 3) }, Exile = Array.Empty<CardView>() },
                    },
                },
            }));
        }

        private void Choice(int zone, int count, CardView[] cards, string id)
        {
            _client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", ProtocolVersion = 2, GameNumber = 1,
                MatchId = "selection-ui", SequenceNumber = _sequence,
                Payload = new ActionChoiceView
                {
                    ChoiceId = id, SourceCardId = 1, IsCost = true,
                    MinCount = count, MaxCount = count, CanCancel = true,
                    Candidates = cards.Select(card => new ActionChoiceCandidateView
                    {
                        Token = "token-" + card.Id, PlayerId = 1, CardId = card.Id, Card = card, Zone = zone,
                    }).ToArray(),
                },
            }));
            _ui.Interaction.RefreshServerChoice();
        }

        private CardSelectionBar Bar => _root.GetComponentInChildren<CardGalleryView>(true).SelectionBar;
        private CardWidget[] Items => Bar.GetComponentsInChildren<CardWidget>();

        [TearDown]
        public void Cleanup()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
            if (_eventSystem != null)
            {
                Object.DestroyImmediate(_eventSystem);
            }
            if (_language != null)
            {
                CardPresentation.Library.Localization.Language = _language;
            }
        }

        [Test]
        public void SelectionDraftRejectsOverflowAndDisabledItemsAndReconcilesRefresh()
        {
            var draft = new CardSelectionDraft();
            var request = new CardSelectionRequest { MinCount = 2, MaxCount = 2, Items = new[]
            {
                new CardSelectionItem { Key = "a" }, new CardSelectionItem { Key = "b" },
                new CardSelectionItem { Key = "c" }, new CardSelectionItem { Key = "disabled", Enabled = false },
            } };
            draft.Refresh(request);
            Assert.IsFalse(draft.CanConfirm);
            Assert.IsTrue(draft.Toggle("a"));
            Assert.IsFalse(draft.CanConfirm);
            Assert.IsTrue(draft.Toggle("b"));
            Assert.IsTrue(draft.CanConfirm);
            Assert.IsFalse(draft.Toggle("c"));
            Assert.IsFalse(draft.Toggle("disabled"));
            var saved = draft.Selected.ToArray();
            draft.Restore(saved); // A rejected submission retains the draft, with no state mutation.
            CollectionAssert.AreEquivalent(saved, draft.Selected);
            Assert.IsTrue(draft.Toggle("b"));
            Assert.IsFalse(draft.CanConfirm);
            draft.Toggle("b");
            request.Items = request.Items.Where(item => item.Key != "a").ToArray();
            draft.Refresh(request);
            CollectionAssert.AreEquivalent(new[] { "b" }, draft.Selected);
            request.MinCount = 0;
            request.MaxCount = 3;
            draft.Refresh(request);
            Assert.IsTrue(draft.CanConfirm);
            Assert.Throws<ArgumentException>(() => draft.Refresh(new CardSelectionRequest { MinCount = 3, MaxCount = 1 }));
        }

        [Test]
        public void DefenseDraftReassignsTogglesAndUsesOnlySuppliedPairs()
        {
            var draft = new DefenseAssignmentDraft();
            var request = new DefenseAssignmentRequest { AllowedPairs = new[]
            {
                Pair(60, 20), Pair(60, 21), Pair(61, 20), Pair(61, 21),
            } };
            draft.Refresh(request);
            Assert.IsFalse(draft.Assign(60, 99));
            Assert.IsTrue(draft.Assign(60, 20));
            Assert.IsTrue(draft.Assign(60, 21));
            Assert.AreEqual(21, draft.Assignments[60]);
            draft.Assign(61, 21);
            Assert.IsFalse(draft.Assignments.ContainsKey(60), "A defender is moved to the new attacker");
            draft.Assign(61, 21);
            Assert.IsEmpty(draft.Assignments);
            request.AllowSharedDefender = true;
            draft.Refresh(request);
            draft.Assign(60, 20);
            draft.Assign(61, 20);
            Assert.AreEqual(2, draft.Assignments.Count);
            request.AllowedPairs = new[] { Pair(60, 20) };
            draft.Refresh(request);
            Assert.AreEqual(1, draft.Assignments.Count);
            Assert.Less(Vector2.Distance(DefenseBezierGraphic.Evaluate(Vector2.zero, new Vector2(100, .01f), .25f),
                DefenseBezierGraphic.Evaluate(Vector2.zero, new Vector2(100, -.01f), .25f)), .1f,
                "Crossing the source row during a drag must not flip the curve");
            Assert.AreEqual(new Vector2(3, 8), DefenseBezierGraphic.Evaluate(new Vector2(3, 8), new Vector2(20, -80), 0));
            Assert.AreEqual(new Vector2(20, -80), DefenseBezierGraphic.Evaluate(new Vector2(3, 8), new Vector2(20, -80), 1));
        }

        private static DefenseAssignmentPair Pair(int attacker, int defender) => new DefenseAssignmentPair
        { AttackerId = attacker, DefenderId = defender };

        [Test]
        public void SameChoiceRefreshUpdatesCandidatesWithoutUnlockingSubmission()
        {
            var input = new GameplayInputCoordinator();
            input.SetServerChoice(new ActionChoiceView { ChoiceId = "same", Candidates = new[]
            {
                new ActionChoiceCandidateView { Token = "old", CardId = 1, Zone = 1 },
            } });
            input.CommandSent(true);
            input.SetServerChoice(new ActionChoiceView { ChoiceId = "same", Candidates = new[]
            {
                new ActionChoiceCandidateView { Token = "new", CardId = 2, Zone = 3 },
            } });
            Assert.AreEqual(SelectionInputPhase.WaitingForServer, input.SelectionPhase);
            Assert.IsFalse(input.CanExecute(GameplayInputAction.ConfirmSelection));
            input.CommandRejected();
            Assert.IsTrue(input.CanSelectCard(3, 2));
            Assert.IsFalse(input.CanSelectCard(1, 1));
        }

        [UnityTest]
        public IEnumerator AuthoredPreviewSceneSupportsDefenseAndClickableResponsePass()
        {
            _language = CardPresentation.Library.Localization.Language;
            var scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Previews/BattleSelectionPreview.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            for (int i = 0; i < 60; ++i)
            {
                _ui = Object.FindFirstObjectByType<BattlePresenter>();
                if (_ui != null && _ui.Defense.IsOpen)
                {
                    break;
                }
                yield return null;
            }
            Assert.IsNotNull(_ui);
            Assert.IsTrue(_ui.Defense.IsOpen);
            Assert.AreEqual(2, _ui.Defense.Draft.Assignments.Count);
            var driver = Object.FindFirstObjectByType<BattleSelectionPreviewDriver>();
            driver.ShowMode(BattleSelectionPreviewDriver.PreviewMode.Response);
            for (int i = 0; i < 60; ++i)
            {
                yield return null;
                _ui = driver.GetComponentInChildren<BattlePresenter>();
                if (_ui != null && _ui.GetComponentInChildren<CardSelectionBar>() != null)
                {
                    break;
                }
            }
            Assert.IsNotNull(_ui);
            _client = _ui.Client;
            yield return null;
            var bar = _ui.GetComponentInChildren<CardSelectionBar>();
            Assert.IsNotNull(bar);
            Assert.AreEqual(0, bar.Draft.Selected.Count);
            var overlay = _ui.EndTurn.GetComponentsInChildren<Button>().First(button => button.name == "SelectionPrimaryAction");
            Assert.IsTrue(overlay.interactable, "Response pass remains available with zero selected cards");
            Assert.AreEqual(BattleText.Get("ui.selection.pass"), _ui.EndTurn.GetComponentInChildren<TMP_Text>().text);
            var point = RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)overlay.transform).TransformPoint(((RectTransform)overlay.transform).rect.center));
            var pointer = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.AreSame(overlay.gameObject, hits[0].gameObject, "The existing selection ModalBlocker must not intercept pass");
            LogAssert.Expect(LogType.Log, "UI PREVIEW: pass clicked; no command sent.");
            ExecuteEvents.Execute(overlay.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            Directory.CreateDirectory("Artifacts/BattleSelection");
            _root = _ui.gameObject;
            yield return Capture("Response-InteractivePrototype");
            _root = null;
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest]
        public IEnumerator ActualChoiceBarExactCountEyeScrollingAndNoStateMutation()
        {
            yield return Setup();
            string state = JsonUtility.ToJson(_client.State);
            yield return Capture("BasicHUD");
            Choice(1, 2, _client.State.Players[0].Hand.Skip(1).Take(5).ToArray(), "discard");
            yield return null;
            Assert.IsFalse(Bar.ConfirmButton.interactable);
            Assert.IsFalse(Bar.Scroll.horizontal);
            Items[0].Clicked(Items[0]);
            Assert.IsFalse(Bar.ConfirmButton.interactable);
            Items[1].Clicked(Items[1]);
            Assert.IsTrue(Bar.ConfirmButton.interactable);
            Items[2].Clicked(Items[2]);
            Assert.AreEqual(2, Bar.Draft.Selected.Count);
            Assert.IsFalse(_ui.EndTurn.interactable);
            Assert.IsFalse(_ui.Hand.gameObject.activeSelf);
            Bar.SetSubmitting(true);
            Assert.IsFalse(Bar.ConfirmButton.interactable);
            Bar.SetSubmitting(false);
            Assert.IsTrue(Bar.ConfirmButton.interactable);
            yield return Capture("Discard");
            Bar.ToggleVisibility();
            yield return null;
            Assert.IsTrue(Bar.Hidden);
            Assert.IsTrue(Bar.transform.Find("SelectionEye").gameObject.activeInHierarchy);
            Assert.AreEqual(2, Bar.Draft.Selected.Count);
            Bar.ToggleVisibility();
            Items[1].Clicked(Items[1]);
            Assert.IsFalse(Bar.ConfirmButton.interactable);
            Assert.AreEqual(state, JsonUtility.ToJson(_client.State));
            Choice(3, 3, _client.State.Players[0].Graveyard, "grave");
            yield return null;
            Assert.IsTrue(Bar.Scroll.horizontal);
            Items[0].Clicked(Items[0]);
            Items[1].Clicked(Items[1]);
            Items[2].Clicked(Items[2]);
            Assert.IsTrue(Bar.ConfirmButton.interactable);
            Bar.Scroll.horizontalNormalizedPosition = .6f;
            float scroll = Bar.Scroll.horizontalNormalizedPosition;
            Bar.ToggleVisibility();
            yield return null;
            Bar.ToggleVisibility();
            Assert.AreEqual(scroll, Bar.Scroll.horizontalNormalizedPosition, .02f);
            // Scrolling through a card uses the existing drag threshold and cannot become a click.
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            int selected = Bar.Draft.Selected.Count;
            Items[0].OnBeginDrag(pointer);
            Items[0].OnDrag(pointer);
            Items[0].OnEndDrag(pointer);
            Items[0].OnPointerClick(pointer);
            Assert.AreEqual(selected, Bar.Draft.Selected.Count);
            Bar.Scroll.horizontalNormalizedPosition = 0;
            yield return Capture("Graveyard");
            Assert.AreEqual(state, JsonUtility.ToJson(_client.State));
            Assert.IsTrue(_ui.Bind("OwnGraveTagImage", out Image graveTag));
            Assert.IsTrue(graveTag.GetComponent<Outline>().enabled);
            _ui.GetComponentInChildren<CardGalleryView>(true).Close();
            Assert.IsFalse(graveTag.GetComponent<Outline>().enabled);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn), "Closing presentation must not cancel an authoritative request");
        }

        [UnityTest]
        public IEnumerator FieldMirrorAndPrototypeResponseHaveLocalizedSafeLayout()
        {
            yield return Setup();
            Choice(2, 2, _client.State.Players[0].Field, "field");
            yield return null;
            _ui.Interaction.Click(_ui.OwnField.Widgets[0]);
            Assert.IsTrue(Items[0].IsSelected);
            Items[1].Clicked(Items[1]);
            Assert.IsTrue(_ui.OwnField.Widgets[1].Face.Find("FieldSelectionCheck").gameObject.activeSelf);
            Assert.IsTrue(Bar.ConfirmButton.interactable);
            yield return Capture("FieldCost");
            var rebound = _ui.OwnField.Widgets[0];
            rebound.Bind(rebound.Card, false, false, false, false);
            Assert.IsFalse(rebound.Face.Find("FieldSelectionCheck").gameObject.activeSelf, "Rebinding a pooled field card clears its old choice badge");
            Choice(1, 1, new[] { Card(90, 2), Card(91, 1), Card(92, 4) }, "response-preview");
            // These response candidates are explicitly synthetic. No response command is sent.
            var request = new CardSelectionRequest
            {
                TitleKey = "ui.selection.response", MinCount = 1, MaxCount = 1, AllowCancel = false, AllowPass = true,
                Items = new[]
                {
                    new CardSelectionItem { Key = "hand", Card = Card(90, 2), SourceKey = "ui.selection.hand_source" },
                    new CardSelectionItem { Key = "field", Card = Card(91, 1), SourceKey = "ui.selection.field_source" },
                    new CardSelectionItem { Key = "disabled", Card = Card(92, 4), Enabled = false },
                },
            };
            Bar.Show(request, _ui.Hand.CardPrefab, key =>
            {
                Bar.Draft.Toggle(key);
                Bar.SetSelected(Bar.Draft.Selected);
            }, () => { }, null, card => _ui.InspectCard(card, true), () => { }, _ui);
            Items[2].Clicked(Items[2]);
            Assert.AreEqual(0, Bar.Draft.Selected.Count);
            Items[0].Clicked(Items[0]);
            Assert.IsTrue(Bar.ConfirmButton.interactable);
            yield return Capture("Response-Prototype");
            foreach (string language in new[] { "korean", "japanese", "english" })
            {
                CardPresentation.Library.Localization.Language = language;
                request.Title = BattleText.Get("ui.selection.response");
                Bar.Show(request, _ui.Hand.CardPrefab, null, null, null, null, () => { }, _ui);
                yield return null;
                foreach (TMP_Text label in Bar.GetComponentsInChildren<TMP_Text>())
                {
                    if (label.name.StartsWith("Selection") && label.text.Length > 0)
                    {
                        label.ForceMeshUpdate();
                        Assert.IsFalse(label.isTextOverflowing, label.name + " " + language);
                    }
                }
                yield return Capture("Response-" + language);
            }
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(2560, 1440),
                new Vector2Int(2400, 1080), new Vector2Int(2560, 1080) })
            {
                yield return Capture("Selection-" + resolution.x + "x" + resolution.y, resolution.x, resolution.y);
            }
        }

        [UnityTest]
        public IEnumerator DefenseDragBothDirectionsReassignmentAndLiveCurves()
        {
            yield return Setup();
            var view = _ui.Defense;
            var request = new DefenseAssignmentRequest
            {
                AllowedPairs = new[] { Pair(60, 20), Pair(60, 21), Pair(60, 22), Pair(61, 20), Pair(61, 21), Pair(61, 22) },
                AttackCounters = new Dictionary<int, int> { { 60, 2 }, { 61, 3 }, { 20, 1 }, { 21, 2 }, { 22, 0 } },
            };
            string original = JsonUtility.ToJson(_client.State);
            view.Show(_ui, request);
            yield return null;
            var handles = view.GetComponentsInChildren<DefenseCardHandle>();
            var attacker = handles.First(handle => handle.Card.Card.Id == 60);
            var defender = handles.First(handle => handle.Card.Card.Id == 20);
            Drag(view, attacker, defender);
            Assert.AreEqual(20, view.Draft.Assignments[60]);
            var otherAttacker = handles.First(handle => handle.Card.Card.Id == 61);
            var otherDefender = handles.First(handle => handle.Card.Card.Id == 21);
            Drag(view, otherDefender, otherAttacker);
            Assert.AreEqual(21, view.Draft.Assignments[61]);
            yield return null;
            yield return Capture("Defense");
            foreach (string language in new[] { "japanese", "english" })
            {
                CardPresentation.Library.Localization.Language = language;
                yield return null;
                var primaryLabel = _ui.EndTurn.GetComponentInChildren<TMP_Text>();
                primaryLabel.ForceMeshUpdate();
                Assert.IsFalse(primaryLabel.isTextOverflowing, "Defense action text " + language);
                Assert.LessOrEqual(primaryLabel.textBounds.size.x, 202, "Use the visible button interior rather than its transparent art bounds");
                yield return Capture("Defense-" + language);
            }
            CardPresentation.Library.Localization.Language = "korean";
            var lines = view.GetComponentsInChildren<DefenseBezierGraphic>();
            Assert.AreEqual(2, lines.Count(line => line.name.StartsWith("DefenseLink")));
            Assert.IsTrue(lines.All(line => !line.raycastTarget));
            Drag(view, defender, otherAttacker);
            Assert.IsFalse(view.Draft.Assignments.ContainsKey(60));
            Assert.AreEqual(20, view.Draft.Assignments[61]);
            Drag(view, defender, otherAttacker);
            Assert.IsEmpty(view.Draft.Assignments);
            view.Select(attacker);
            view.Select(defender);
            Assert.AreEqual(20, view.Draft.Assignments[60]);
            view.SetSubmitting(true);
            Drag(view, otherDefender, attacker);
            Assert.AreEqual(20, view.Draft.Assignments[60]);
            view.SetSubmitting(false);
            bool confirmed = false;
            view.Confirmed += pairs => confirmed = pairs.Length == 1;
            view.transform.root.GetComponentsInChildren<Button>().First(button => button.name == "DefenseConfirmOverlay").onClick.Invoke();
            Assert.IsTrue(confirmed);
            Assert.AreEqual(original, JsonUtility.ToJson(_client.State));
            Assert.IsFalse(_ui.CanAct);
            foreach (var resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(2560, 1440), new Vector2Int(2400, 1080) })
            {
                yield return Capture("Defense-" + resolution.x + "x" + resolution.y, resolution.x, resolution.y);
            }
            view.Close();
            yield return null;
            Assert.IsFalse(view.IsOpen);
            Assert.IsFalse(_client.Input.HasSelection);
            Assert.AreEqual(0, _client.Input.ScopeCount);
        }

        private static void Drag(DefenseAssignmentView view, DefenseCardHandle from, DefenseCardHandle to)
        {
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, from.Card.WorldCenter),
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsTrue(hits.Count > 0);
            Assert.AreSame(from, hits[0].gameObject.GetComponent<DefenseCardHandle>(), "Defense handle must receive real UI raycasts");
            from.OnPointerDown(pointer);
            from.OnBeginDrag(pointer);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, to.Card.WorldCenter);
            from.OnDrag(pointer);
            from.OnEndDrag(pointer);
        }

        private IEnumerator Capture(string name, int width = 1920, int height = 1080)
        {
            var cameraObject = new GameObject("Selection capture camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            var target = new RenderTexture(width, height, 24);
            target.Create();
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .06f, .09f);
            var canvas = _ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes("Artifacts/BattleSelection/" + name + ".png", pixels.EncodeToPNG());
            _measurements.Add(name + ": " + width + "x" + height + " turnTransitionAlpha=" + _ui.Effects.TurnGroup.alpha);
            foreach (var rect in _root.GetComponentsInChildren<RectTransform>())
            {
                if (rect.name == "SelectionPanel" || rect.name == "SelectionGuide" || rect.name == "SelectionEye"
                    || rect.name == "SelectionViewport" || rect.name == "SelectionConfirm")
                {
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    var a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    var b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    _measurements.Add("  " + rect.name + ": x=" + a.x.ToString("F1") + " y(top)=" + (height - b.y).ToString("F1")
                        + " w=" + (b.x - a.x).ToString("F1") + " h=" + (b.y - a.y).ToString("F1"));
                    Assert.GreaterOrEqual(a.x, -.5f, name + " left edge");
                    Assert.LessOrEqual(b.x, width + .5f, name + " right edge");
                    Assert.GreaterOrEqual(a.y, -.5f, name + " bottom edge");
                    Assert.LessOrEqual(b.y, height + .5f, name + " top edge");
                    if (width == 1920 && height == 1080 && rect.name == "SelectionPanel")
                    {
                        Assert.AreEqual(1114, b.x - a.x, 1);
                        Assert.AreEqual(249, b.y - a.y, 1);
                    }
                }
            }
            File.WriteAllLines("Artifacts/BattleSelection/Captures.txt", _measurements);
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
#endif
