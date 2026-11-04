#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectC.Tests
{
    public sealed class HandBrowsingPlayModeTests
    {
        private GameObject _root, _events;
        private BattlePresenter _ui;
        private MultiplayerDebugScreen _client;

        private static CardView Card(int id) => new CardView
        {
            Id = id, DefinitionId = 1001, Attack = 2, Hp = 3, MaxHp = 3,
        };

        private IEnumerator Setup(int count = 5)
        {
            _root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
            _ui = _root.GetComponent<BattlePresenter>();
            _client = _ui.Client;
            if (EventSystem.current == null)
            {
                _events = new GameObject("Hand browse test events", typeof(EventSystem));
            }
            _client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"hand-browse\",\"PlayerId\":1,\"GameNumber\":1}");
            _client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", ProtocolVersion = 2, MatchId = "hand-browse", GameNumber = 1, SequenceNumber = 1,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, TurnNumber = 1, ActivePlayerId = 1, FieldLimit = 5,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, HandCount = count, Hand = Enumerable.Range(1, count).Select(Card).ToArray(),
                            Field = Array.Empty<CardView>(), Graveyard = new[] { Card(50) } },
                        new PlayerView { PlayerId = 2, Life = 20, Hand = Array.Empty<CardView>(), Field = Array.Empty<CardView>(),
                            Graveyard = new[] { Card(51) } },
                    },
                },
            }));
            yield return null;
            yield return null;
            _ui.Effects.ResetFeedback();
            yield return new WaitForSecondsRealtime(.4f);
        }

        private static PointerEventData ClickAt(Vector2 position, out GameObject hit)
        {
            Canvas.ForceUpdateCanvases();
            var data = new PointerEventData(EventSystem.current)
            {
                position = position, button = PointerEventData.InputButton.Left,
            };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, results);
            Assert.IsNotEmpty(results);
            hit = results[0].gameObject;
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, data, ExecuteEvents.pointerClickHandler);
            return data;
        }

        private Vector2 EndTurnPosition()
        {
            var rect = (RectTransform)_ui.EndTurn.transform;
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private void AssertHandInsideCanvas()
        {
            Canvas.ForceUpdateCanvases();
            var canvas = (RectTransform)_ui.GetComponent<Canvas>().transform;
            foreach (var widget in _ui.Hand.Widgets)
            {
                var corners = new Vector3[4];
                widget.Face.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Vector3 point = canvas.InverseTransformPoint(corner);
                    Assert.That(point.x, Is.InRange(canvas.rect.xMin - 1, canvas.rect.xMax + 1));
                    Assert.That(point.y, Is.InRange(canvas.rect.yMin - 1, canvas.rect.yMax + 1));
                }
                Assert.That(Quaternion.Angle(widget.transform.localRotation, Quaternion.identity), Is.LessThan(.1f));
            }
        }

        [UnityTest]
        public IEnumerator FiveAndNineCardsFitBothModesAndKeepRelativeFocus()
        {
            yield return Setup();
            var settings = _ui.Hand.Layout.Settings;
            Assert.That(settings.CompactHandScale, Is.EqualTo(.57f * .9f).Within(.0001f));
            Assert.That(settings.ExpandedHandScale, Is.EqualTo(1.29f * .9f).Within(.0001f));
            Assert.That(settings.CompactHandSpacing, Is.EqualTo((180 + 8) * .57f * 1.25f).Within(.001f));
            Assert.That(settings.ExpandedHandSpacing, Is.EqualTo((180 - 25) * 1.29f * 1.25f).Within(.001f));
            foreach (int count in new[] { 5, 9 })
            {
                _ui.Hand.Render(Enumerable.Range(1, count).Select(Card).ToArray(), 0, false, 0, false, null, null, null);
                foreach (bool expanded in new[] { false, true })
                {
                    _ui.SetHandExpanded(expanded);
                    yield return new WaitForSecondsRealtime(.45f);
                    AssertHandInsideCanvas();
                    Assert.That(_ui.Hand.Widgets[0].transform.localScale.x,
                        Is.EqualTo(expanded ? settings.ExpandedHandScale : settings.CompactHandScale).Within(.001f));
                    float expectedMaximum = expanded ? settings.ExpandedHandSpacing : settings.CompactHandSpacing;
                    Assert.LessOrEqual(_ui.Hand.Layout.Spacing, expectedMaximum + .001f);
                    if (count == 5)
                    {
                        Assert.That(_ui.Hand.Layout.Spacing, Is.EqualTo(expectedMaximum).Within(.1f));
                    }
                }
            }
            var card = _ui.Hand.Widgets.Last();
            Vector3 faceScale = card.Face.localScale;
            card.OnPointerEnter(null);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(card.Face.localScale.x / faceScale.x, Is.EqualTo(1.2f).Within(.01f));
            AssertHandInsideCanvas();
            card.OnPointerExit(null);
            card.SetSelected(true);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(card.Face.localScale.x / faceScale.x, Is.EqualTo(1.2f).Within(.01f));
            AssertHandInsideCanvas();
        }

        [UnityTest]
        public IEnumerator ViewportResizeCompressesDenseHandWithoutLosingSlots()
        {
            yield return Setup(9);
            var canvas = _ui.GetComponent<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvas.transform;
            foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(960, 720) })
            {
                rect.sizeDelta = size;
                foreach (bool expanded in new[] { false, true })
                {
                    _ui.SetHandExpanded(expanded);
                    yield return new WaitForSecondsRealtime(.6f);
                    AssertHandInsideCanvas();
                    for (int index = 0; index < _ui.Hand.Widgets.Count; ++index)
                    {
                        _ui.Hand.Layout.GetSlot(index, _ui.Hand.Widgets.Count, out var slot, out _);
                        var widget = (RectTransform)_ui.Hand.Widgets[index].transform;
                        Assert.Less(Vector2.Distance(slot, widget.anchoredPosition), .1f);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator BrowseDismissesOutsideConsumesClickAndRestoresInput()
        {
            yield return Setup();
            int behindClicks = 0;
            _ui.EndTurn.onClick.AddListener(() => ++behindClicks);
            var gallery = _ui.GetComponentInChildren<CardGalleryView>(true);
            gallery.ShowPublicZones();
            yield return null;
            Assert.IsFalse(gallery.Choosing);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            var card = gallery.GetComponentsInChildren<CardWidget>().First();
            card.OnPointerEnter(null);
            Assert.IsTrue(_ui.Detail.gameObject.activeSelf);
            var data = ClickAt(EndTurnPosition(), out var hit);
            Assert.AreSame(_ui.Modal.gameObject, hit);
            Assert.IsTrue(data.used);
            Assert.IsFalse(gallery.gameObject.activeSelf);
            Assert.AreEqual(0, behindClicks);
            Assert.AreEqual(0, _ui.Modal.ActiveCount);
            Assert.IsTrue(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            yield return null;
            ClickAt(EndTurnPosition(), out _);
            Assert.AreEqual(1, behindClicks, "Only a separate subsequent click may reach the exposed button.");
        }

        [UnityTest]
        public IEnumerator RequiredDeckChoiceDoesNotDismissBeforeServerConfirmation()
        {
            yield return Setup();
            _client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", ProtocolVersion = 2, MatchId = "hand-browse", GameNumber = 1, SequenceNumber = 1,
                Payload = new ActionChoiceView
                {
                    ChoiceId = "required-deck", MinCount = 1, MaxCount = 1, CanCancel = false,
                    Candidates = new[] { new ActionChoiceCandidateView { Token = "first", Zone = 0, PlayerId = 1, Card = Card(70) } },
                },
            }));
            _ui.Interaction.RefreshServerChoice();
            yield return null;
            var gallery = _ui.GetComponentInChildren<CardGalleryView>(true);
            Assert.IsTrue(gallery.Choosing);
            ClickAt(EndTurnPosition(), out var hit);
            Assert.AreSame(_ui.Modal.gameObject, hit);
            Assert.IsTrue(gallery.gameObject.activeSelf);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
            _client.Input.CommandSent(true);
            ClickAt(EndTurnPosition(), out _);
            Assert.IsTrue(gallery.gameObject.activeSelf);
            Assert.IsTrue(_client.Input.HasSelection);
            Assert.IsFalse(_client.Input.CanExecute(GameplayInputAction.EndTurn));
        }

        [TearDown]
        public void Cleanup()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }
            if (_events != null)
            {
                UnityEngine.Object.DestroyImmediate(_events);
            }
        }
    }
}
#endif
