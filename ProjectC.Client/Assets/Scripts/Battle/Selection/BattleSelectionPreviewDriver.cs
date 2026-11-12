#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ProjectC.Client
{
    // Editor-only isolated scene fixture. Never added to BattleScene or connected to a server.
    public sealed class BattleSelectionPreviewDriver : MonoBehaviour
    {
        public enum PreviewMode { Discard, Graveyard, FieldCost, Response, Defense }
        [SerializeField] private GameObject _screenPrefab;
        private GameObject _screen;
        private BattlePresenter _presenter;
        private bool _switching;
        private static readonly int[] Definitions = { 1001, 4002, 3004, 1003, 4001, 3001, 1002, 4003 };

        public void Configure(GameObject screenPrefab) => _screenPrefab = screenPrefab;

        private void Start()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("PreviewEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(transform, false);
            }
            ShowMode(PreviewMode.Defense);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                ShowMode(PreviewMode.Discard);
            }
            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                ShowMode(PreviewMode.Graveyard);
            }
            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                ShowMode(PreviewMode.FieldCost);
            }
            if (keyboard.digit4Key.wasPressedThisFrame)
            {
                ShowMode(PreviewMode.Response);
            }
            if (keyboard.digit5Key.wasPressedThisFrame)
            {
                ShowMode(PreviewMode.Defense);
            }
        }

        public void ShowMode(PreviewMode mode)
        {
            if (!_switching)
            {
                StartCoroutine(Show(mode));
            }
        }

        private IEnumerator Show(PreviewMode mode)
        {
            _switching = true;
            if (_screen != null)
            {
                Destroy(_screen);
                yield return null;
            }
            _screen = Instantiate(_screenPrefab, transform);
            _presenter = _screen.GetComponent<BattlePresenter>();
            var client = _presenter.Client;
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"ui-preview-only\",\"PlayerId\":1,\"GameNumber\":1}");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage
            {
                Type = "StateSnapshot", MatchId = "ui-preview-only", ProtocolVersion = 2,
                GameNumber = 1, SequenceNumber = 1, ServerTimeUnixMs = now,
                Payload = new StateView
                {
                    Started = true, TurnStarted = true, ActivePlayerId = 1, TurnNumber = 3,
                    TurnTimerRunning = true, TurnEndsAtUnixMs = now + 120000, FieldLimit = 10,
                    Players = new[]
                    {
                        new PlayerView { PlayerId = 1, Life = 20, HandCount = 5, DeckCount = 25,
                            Hand = Enumerable.Range(0, 5).Select(i => Card(i + 1, i)).ToArray(),
                            Field = Enumerable.Range(0, 3).Select(i => Card(i + 20, i)).ToArray(),
                            Graveyard = Enumerable.Range(0, 8).Select(i => Card(i + 40, i)).ToArray(), Exile = Array.Empty<CardView>() },
                        new PlayerView { PlayerId = 2, Life = 18, HandCount = 5, DeckCount = 23,
                            Hand = Array.Empty<CardView>(), Field = Enumerable.Range(0, 2).Select(i => Card(i + 60, i + 2)).ToArray(),
                            Graveyard = new[] { Card(70, 0) }, Exile = Array.Empty<CardView>() },
                    },
                },
            }));
            yield return null;
            yield return null;
            _presenter.Effects.ResetFeedback();
            foreach (var zone in _presenter.Zones)
            {
                zone.Layout?.Snap();
            }
            if (mode == PreviewMode.Defense)
            {
                var request = new DefenseAssignmentRequest
                {
                    AllowedPairs = new[] { Pair(60, 20), Pair(60, 21), Pair(60, 22), Pair(61, 20), Pair(61, 21), Pair(61, 22) },
                    Assignments = new[] { Pair(60, 21), Pair(61, 22) },
                    AttackCounters = new Dictionary<int, int> { { 60, 2 }, { 61, 3 }, { 20, 1 }, { 21, 2 }, { 22, 0 } },
                };
                _presenter.Defense.Show(_presenter, request);
                _presenter.Defense.Confirmed += pairs => Debug.Log("UI PREVIEW: defense draft confirmed locally; no command sent.");
            }
            else
            {
                ShowSelection(mode);
            }
            _switching = false;
        }

        private void ShowSelection(PreviewMode mode)
        {
            var mine = _presenter.Client.State.Players[0];
            CardView[] cards = mode == PreviewMode.Graveyard ? mine.Graveyard
                : mode == PreviewMode.FieldCost ? mine.Field : mine.Hand;
            int count = mode == PreviewMode.Graveyard ? 3 : mode == PreviewMode.Response ? 1 : 2;
            int zone = mode == PreviewMode.Graveyard ? 3 : mode == PreviewMode.FieldCost ? 2 : 1;
            // Authorized DTO fixture enters the same selection lock as the existing live choice UI.
            var choice = new ActionChoiceView
            {
                ChoiceId = "preview-" + mode, IsCost = mode != PreviewMode.Response, CanCancel = mode != PreviewMode.Response,
                MinCount = count, MaxCount = count, Candidates = cards.Select(card => new ActionChoiceCandidateView
                { Token = card.Id.ToString(), CardId = card.Id, Card = card, PlayerId = 1, Zone = zone }).ToArray(),
            };
            _presenter.Client.ApplyMessage(JsonUtility.ToJson(new ActionChoiceMessage
            {
                Type = "ActionChoiceRequired", MatchId = "ui-preview-only", GameNumber = 1,
                ProtocolVersion = 2, SequenceNumber = 1, Payload = choice,
            }));
            _presenter.Interaction.RefreshServerChoice();
            var gallery = _screen.GetComponentInChildren<CardGalleryView>(true);
            var bar = gallery.SelectionBar;
            var request = new CardSelectionRequest
            {
                TitleKey = mode == PreviewMode.Graveyard ? "ui.selection.grave"
                    : mode == PreviewMode.FieldCost ? "ui.selection.field"
                    : mode == PreviewMode.Response ? "ui.selection.response" : "ui.selection.discard",
                TitleArguments = new object[] { count },
                MinCount = count, MaxCount = count, AllowCancel = mode != PreviewMode.Response,
                AllowPass = mode == PreviewMode.Response,
                Items = cards.Select(card => new CardSelectionItem { Key = card.Id.ToString(), Card = card }).ToArray(),
            };
            if (mode == PreviewMode.Response)
            {
                request.Items = new[]
                {
                    new CardSelectionItem { Key = "hand", Card = mine.Hand[0], SourceKey = "ui.selection.hand_source" },
                    new CardSelectionItem { Key = "field", Card = mine.Field[1], SourceKey = "ui.selection.field_source" },
                };
            }
            Action<string> toggle = key =>
            {
                bar.Draft.Toggle(key);
                bar.SetSelected(bar.Draft.Selected);
                if (mode == PreviewMode.FieldCost)
                {
                    foreach (var widget in _presenter.OwnField.Widgets)
                    {
                        bool selected = bar.Draft.Selected.Contains(widget.Card.Id.ToString());
                        CardSelectionMark.Set(widget, selected);
                        widget.SetHint(selected ? TargetHint.Focused : TargetHint.Available);
                    }
                }
            };
            bar.Show(request, _presenter.Hand.CardPrefab, toggle,
                () => Debug.Log("UI PREVIEW: selection confirmed locally; no command sent."),
                () => ShowMode(mode), card => _presenter.InspectCard(card, true),
                () => Debug.Log("UI PREVIEW: pass clicked; no command sent."), _presenter);
            if (mode == PreviewMode.FieldCost)
            {
                foreach (var widget in _presenter.OwnField.Widgets)
                {
                    widget.Clicked = selected => toggle(selected.Card.Id.ToString());
                }
            }
        }

        private static CardView Card(int id, int index)
        {
            var definition = CardPresentation.Library.Find(Definitions[index % Definitions.Length]);
            return new CardView { Id = id, DefinitionId = definition.Id,
                Attack = definition.Attack, Hp = definition.MaxHp, MaxHp = definition.MaxHp,
                CardType = definition.Type == "Spell" ? 1 : 0 };
        }

        private static DefenseAssignmentPair Pair(int attacker, int defender) => new DefenseAssignmentPair
        { AttackerId = attacker, DefenderId = defender };
    }
}
#endif
