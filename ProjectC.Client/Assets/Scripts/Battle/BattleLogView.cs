using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleLogView : MonoBehaviour
    {
        private const int MaximumEntries = 200;
        private const float FollowThreshold = .035f;
        // Retained serialized fields for existing prefab/debug tooling compatibility.
        public TMP_Text Feedback;
        public GameObject History;
        public BattleVisualSettings Settings;
        [SerializeField] private BattleLogItemView _itemPrefab;
        public bool IsOpen { get; private set; }
        public ScrollRect Scroll { get; private set; }
        public IReadOnlyList<BattleLogEntry> Entries => _entries;
        private readonly List<BattleLogEntry> _entries = new List<BattleLogEntry>();
        private readonly List<BattleLogItemView> _rows = new List<BattleLogItemView>();
        private readonly List<EventView> _pending = new List<EventView>();
        private readonly Dictionary<int, CardView> _visibleCards = new Dictionary<int, CardView>();
        private BattlePresenter _presenter;
        private CanvasGroup _group;
        private RectTransform _panel;
        private TMP_Text _unread, _title, _empty;
        private int _unreadCount, _turn, _game = -1;
        private string _match, _language;
        private float _feedbackUntil;
        private IDisposable _scope;
        private ModalBlocker.Lease _modal;
        private Color _previousTint;
        private bool _bound;

        public void Initialize(BattlePresenter presenter)
        {
            _presenter = presenter;
            BindReferences();
        }

        private void BindReferences()
        {
            if (_bound)
            {
                return;
            }
            _presenter = _presenter != null ? _presenter : GetComponentInParent<BattlePresenter>();
            if (_presenter == null || _itemPrefab == null || !transform.parent.Bind("FullHistoryImage", out _panel)
                || !_panel.Bind("CloseLogButton", out Button close) || !_panel.Bind("LogPopupTitleText", out _title)
                || !_panel.Bind("LogEmptyText", out _empty) || !transform.parent.Bind("RejectionReasonText", out Feedback)
                || !transform.parent.Bind("UnreadText", out _unread))
            {
                throw new InvalidOperationException("Battle log requires its popup, row prefab and HUD bindings.");
            }
            History = _panel.gameObject;
            _group = _panel.GetComponent<CanvasGroup>();
            Scroll = _panel.GetComponent<ScrollRect>();
            if (_group == null || Scroll == null || Scroll.content == null)
            {
                throw new InvalidOperationException("Battle log popup requires CanvasGroup and configured ScrollRect.");
            }
            close.onClick.AddListener(Close);
            _bound = true;
            _group.alpha = 0;
            _group.blocksRaycasts = _group.interactable = false;
            History.SetActive(false);
        }

        private void EnsureEpoch()
        {
            if (_match == _presenter.Client.CurrentMatch && _game == _presenter.Client.GameNumber)
            {
                return;
            }
            Close();
            _match = _presenter.Client.CurrentMatch;
            _game = _presenter.Client.GameNumber;
            _entries.Clear();
            _visibleCards.Clear();
            _pending.Clear();
            _turn = _unreadCount = 0;
            _unread.text = "";
            Rebuild();
        }

        public void QueueEvent(EventView value)
        {
            BindReferences();
            EnsureEpoch();
            if (_pending.Count < MaximumEntries)
            {
                _pending.Add(value);
            }
        }

        public void AcceptSnapshot(StateView snapshot)
        {
            BindReferences();
            EnsureEpoch();
            // Only our hand and public zones enter this cache. In particular, never inspect opponent hand DTOs.
            foreach (var player in snapshot.Players ?? Array.Empty<PlayerView>())
            {
                Remember(player.Field);
                Remember(player.Graveyard);
                Remember(player.Exile);
                if (player.PlayerId == _presenter.Client.PlayerId)
                {
                    Remember(player.Hand);
                }
            }
            if (_turn == 0)
            {
                _turn = snapshot.TurnNumber;
            }
            bool follow = !IsOpen || Scroll.verticalNormalizedPosition <= FollowThreshold;
            var position = Scroll.content.anchoredPosition;
            int added = _pending.Count;
            foreach (var value in _pending)
            {
                if (value.TurnNumber > 0)
                {
                    _turn = value.TurnNumber;
                }
                _entries.Add(new BattleLogEntry(_turn, value, id => _visibleCards.TryGetValue(id, out var card) ? card : null));
            }
            _pending.Clear();
            _turn = snapshot.TurnNumber;
            Trim();
            if (added > 0)
            {
                Rebuild();
                RestoreScroll(follow, position);
                MarkUnread(added);
            }
            if (IsOpen && (snapshot.Finished || _presenter.Client.Input.HasSelection))
            {
                Close();
            }
        }

        private void Remember(CardView[] cards)
        {
            foreach (var card in cards ?? Array.Empty<CardView>())
            {
                if (card.Id > 0 && card.DefinitionId > 0)
                {
                    _visibleCards[card.Id] = card;
                }
            }
        }

        // Notices remain diagnostics/feedback; event lines are supplied by typed events above, avoiding duplicates.
        public void Notice(string value)
        {
            if (value == "Connected")
            {
                ClearFeedback();
            }
            else if (value.StartsWith("Rejected:", StringComparison.Ordinal)
                || value.StartsWith("Protocol error", StringComparison.Ordinal)
                || value.StartsWith("Disconnected", StringComparison.Ordinal))
            {
                ShowFeedback(value);
            }
        }

        public void Add(string line)
        {
            Notice(line);
            BindReferences();
            bool follow = !IsOpen || Scroll.verticalNormalizedPosition <= FollowThreshold;
            var position = Scroll.content.anchoredPosition;
            _entries.Add(new BattleLogEntry(_turn, line));
            Trim();
            Rebuild();
            RestoreScroll(follow, position);
            MarkUnread(1);
        }

        private void Trim()
        {
            if (_entries.Count > MaximumEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaximumEntries);
            }
        }

        private void MarkUnread(int count)
        {
            if (!IsOpen)
            {
                _unreadCount += count;
                _unread.text = _unreadCount > 99 ? "99+" : _unreadCount.ToString();
            }
        }

        private void RestoreScroll(bool follow, Vector2 position)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
            Scroll.StopMovement();
            if (follow)
            {
                Scroll.verticalNormalizedPosition = 0;
            }
            else
            {
                Scroll.content.anchoredPosition = position;
            }
        }

        private void Rebuild()
        {
            _language = CardPresentation.Library.Localization.Language;
            _title.text = BattleText.Get("ui_battle_battle_log");
            _empty.text = BattleText.Get("ui.log.empty");
            _empty.gameObject.SetActive(_entries.Count == 0);
            int index = 0, previousTurn = -1;
            foreach (var entry in _entries)
            {
                if (entry.Turn != previousTurn)
                {
                    Row(index++).Render(this, entry, _presenter.Client.PlayerId, true);
                    previousTurn = entry.Turn;
                }
                Row(index++).Render(this, entry, _presenter.Client.PlayerId);
            }
            for (int i = index; i < _rows.Count; i++)
            {
                _rows[i].gameObject.SetActive(false);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
        }

        private BattleLogItemView Row(int index)
        {
            if (index == _rows.Count)
            {
                _rows.Add(Instantiate(_itemPrefab, Scroll.content));
            }
            _rows[index].gameObject.SetActive(true);
            return _rows[index];
        }

        public void Inspect(BattleLogEntry entry)
        {
            if (IsOpen && entry?.Card != null)
            {
                _presenter.InspectCard(entry.Card);
            }
        }

        public void ShowFeedback(string value)
        {
            BindReferences();
            Feedback.text = value;
            _feedbackUntil = Time.unscaledTime + 3;
        }

        public void ClearFeedback()
        {
            BindReferences();
            Feedback.text = "";
        }

        public void Toggle()
        {
            BindReferences();
            if (IsOpen)
            {
                Close();
                return;
            }
            if (!_presenter.Client.Input.CanExecute(GameplayInputAction.ToggleBattleLog))
            {
                return;
            }
            _presenter.Interaction.ResetInput();
            _presenter.HideCardInfo();
            _presenter.SetHandExpanded(false);
            IsOpen = true;
            History.SetActive(true);
            _group.alpha = 1;
            _group.blocksRaycasts = _group.interactable = true;
            _scope = _presenter.Client.Input.PushScope("BattleLog", new[] { GameplayInputAction.OpenCardDetails, GameplayInputAction.ToggleBattleLog });
            _previousTint = _presenter.Modal.Tint;
            _presenter.Modal.Tint = new Color(0, .025f, .035f, .65f);
            _modal = _presenter.Modal.Acquire(this, null, _panel, (RectTransform)_presenter.Detail.transform);
            _unreadCount = 0;
            _unread.text = "";
            Rebuild();
            RestoreScroll(true, Vector2.zero);
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            _modal?.Dispose();
            _modal = null;
            _scope?.Dispose();
            _scope = null;
            _presenter.Modal.Tint = _previousTint;
            _group.alpha = 0;
            _group.blocksRaycasts = _group.interactable = false;
            History.SetActive(false);
            _presenter.HideCardInfo();
        }

        public void Invalidate()
        {
            _pending.Clear();
            Close();
        }

        private void Update()
        {
            BindReferences();
            if (_language != CardPresentation.Library.Localization.Language)
            {
                var position = Scroll.content.anchoredPosition;
                Rebuild();
                RestoreScroll(false, position);
            }
            if (IsOpen && _presenter.Client.Input.HasSelection)
            {
                Close();
            }
            if (Time.unscaledTime > _feedbackUntil)
            {
                Feedback.text = "";
            }
        }

        private void OnDisable()
        {
            Close();
        }
    }
}
