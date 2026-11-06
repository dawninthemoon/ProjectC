using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    // This gallery only receives public cards or the requesting player's authorized choice DTOs.
    public sealed class CardGalleryView : MonoBehaviour
    {
        private const float CardScale = .77f;
        private BattlePresenter _presenter;
        private RectTransform _content;
        private TMP_Text _title;
        private Button _close;
        private readonly List<CardWidget> _cards = new List<CardWidget>();
        private readonly List<string> _tokens = new List<string>();
        public bool Choosing { get; private set; }
        private IDisposable _browseScope;
        private ModalBlocker.Lease _browseModal;
        private const float DeckReadTimeout = 5f;
        private bool _browsingDeck, _refreshDeck;
        private float _deckReadDeadline;

        public void Initialize(BattlePresenter presenter)
        {
            _presenter = presenter;
            if (!this.Bind("GalleryContent", out _content)
                || !this.Bind("GalleryTitleText", out _title)
                || !this.Bind("GalleryCloseButton", out _close))
            {
                throw new InvalidOperationException("CardGallery requires content, title and close bindings.");
            }
            _close.onClick.AddListener(Close);
            presenter.Client.OwnDeckContentsReceived += ShowRemainingDeck;
            presenter.Client.OwnDeckReadFailed += DeckReadFailed;
            presenter.Client.SnapshotReceived += RefreshRemainingDeck;
            presenter.Client.ViewInvalidated += InvalidateBrowse;
        }

        public void Close()
        {
            ReleaseBrowse();
            gameObject.SetActive(false);
            Choosing = false;
        }

        private void Begin(string title, bool choosing)
        {
            ReleaseBrowse();
            Choosing = choosing;
            _title.text = title;
            _title.gameObject.SetActive(!choosing);
            _close.gameObject.SetActive(!choosing);
            foreach (var card in _cards)
            {
                card.transform.parent.gameObject.SetActive(false);
            }
            _tokens.Clear();
            gameObject.SetActive(true);
        }

        private CardWidget Add(CardView value, int index)
        {
            if (index == _cards.Count)
            {
                var slot = new GameObject("GallerySlot", typeof(RectTransform), typeof(LayoutElement));
                slot.transform.SetParent(_content, false);
                var size = slot.GetComponent<LayoutElement>();
                size.preferredWidth = 140;
                size.preferredHeight = 190;
                var card = Instantiate(_presenter.Hand.CardPrefab, slot.transform);
                var rect = (RectTransform)card.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one * CardScale;
                _cards.Add(card);
            }
            var widget = _cards[index];
            widget.gameObject.SetActive(true);
            widget.transform.parent.gameObject.SetActive(true);
            widget.Bind(value, false, false, false, false);
            widget.Hovered = card => _presenter.InspectCard(card.Card, Choosing);
            widget.HoverLeft = null;
            widget.DragStarted = null;
            widget.DragMoved = null;
            widget.DragEnded = null;
            return widget;
        }

        public void ShowPublicZones(bool exile = false)
        {
            if (!_presenter.Client.Input.CanExecute(GameplayInputAction.OpenPublicZone))
            {
                return;
            }
            BeginBrowse(exile ? BattleText.Get("ui.gallery.exile") : BattleText.Get("ui.gallery.grave"));
            int index = 0;
            foreach (var player in _presenter.Client.State?.Players ?? Array.Empty<PlayerView>())
            {
                foreach (var value in (exile ? player.Exile : player.Graveyard) ?? Array.Empty<CardView>())
                {
                    var widget = Add(value, index++);
                    widget.Title.text = BattleText.Get(player.PlayerId == _presenter.Client.PlayerId
                        ? "ui.gallery.own_card" : "ui.gallery.enemy_card", CardPresentation.Name(value));
                    widget.Clicked = card =>
                    {
                        ClientAudio.Play(SoundCue.CardSelect);
                        _presenter.InspectCard(card.Card);
                    };
                }
            }
        }

        private void BeginBrowse(string title)
        {
            _presenter.Interaction.ResetInput();
            _presenter.SetHandExpanded(false);
            Begin(title, false);
            _browseScope = _presenter.Client.Input.PushScope("CardBrowse", new[] { GameplayInputAction.OpenCardDetails });
            _browseModal = _presenter.Modal.Acquire(this, null, (RectTransform)transform, (RectTransform)_presenter.Detail.transform);
            _browseModal.OutsideClicked = Close;
        }

        public void ShowOwnDeck()
        {
            if (!_presenter.Client.Input.CanExecute(GameplayInputAction.OpenPublicZone)
                || _presenter.Client.Busy || _presenter.Client.PendingChoice != null)
            {
                return;
            }
            BeginBrowse(BattleText.Get("ui.gallery.deck_loading"));
            _browsingDeck = true;
            RefreshRemainingDeck(null);
        }

        private void RefreshRemainingDeck(StateView state)
        {
            if (_browsingDeck)
            {
                _refreshDeck = true;
                _deckReadDeadline = Time.unscaledTime + DeckReadTimeout;
            }
        }

        private void Update()
        {
            if (!_browsingDeck)
            {
                return;
            }
            if (_refreshDeck && _presenter.Client.ReadOwnDeck())
            {
                _refreshDeck = false;
            }
            if (_deckReadDeadline > 0 && Time.unscaledTime >= _deckReadDeadline)
            {
                _presenter.Client.CancelOwnDeckRead();
                DeckReadFailed("Timeout");
            }
        }

        private void ShowRemainingDeck(RemainingDeckEntry[] entries)
        {
            if (!_browsingDeck || !gameObject.activeInHierarchy)
            {
                return;
            }
            _deckReadDeadline = 0;
            foreach (var widget in _cards)
            {
                widget.transform.parent.gameObject.SetActive(false);
            }
            int index = 0;
            foreach (var entry in entries)
            {
                var definition = CardPresentation.Library.Find(entry.DefinitionId);
                var card = new CardView
                {
                    DefinitionId = entry.DefinitionId, Name = definition?.Name ?? "#" + entry.DefinitionId,
                    Attack = definition?.Attack ?? 0, Hp = definition?.MaxHp ?? 0, MaxHp = definition?.MaxHp ?? 0,
                    CardType = definition?.Type == "Spell" ? 1 : 0,
                };
                var widget = Add(card, index++);
                widget.Title.text = BattleText.Get("ui.gallery.deck_quantity", CardPresentation.Name(card), entry.Quantity);
                widget.Clicked = selected => _presenter.InspectCard(selected.Card);
            }
            _title.text = BattleText.Get("ui.gallery.deck");
        }

        private void DeckReadFailed(string reason)
        {
            if (!_browsingDeck)
            {
                return;
            }
            if (reason == "StaleSequence")
            {
                RefreshRemainingDeck(null);
                return;
            }
            _deckReadDeadline = 0;
            _refreshDeck = false;
            // Never leave an old deck composition looking current after a failed refresh.
            foreach (var widget in _cards)
            {
                widget.transform.parent.gameObject.SetActive(false);
            }
            _title.text = BattleText.Get("ui.gallery.deck_unavailable");
        }

        private void InvalidateBrowse()
        {
            if (gameObject.activeSelf && !Choosing)
            {
                Close();
            }
        }

        public void ShowChoices(ActionChoiceView choice, Action<string> pick)
        {
            Begin(BattleText.Get("ui.choice.targets", choice.MinCount, choice.MaxCount), true);
            int index = 0;
            foreach (var option in choice.Candidates)
            {
                if (option.Card == null || option.Zone == (int)PresentationZone.Field)
                {
                    continue;
                }
                string token = option.Token;
                _tokens.Add(token);
                Add(option.Card, index++).Clicked = card => pick(token);
            }
        }

        public static string TargetKey(EffectTargetView target)
        {
            return target.PlayerId + ":" + target.Zone + ":" + target.CardId;
        }

        public void ShowTargets(EffectSelectionView selection, Action<EffectTargetView> pick)
        {
            Begin(BattleText.Get("ui.choice.targets", selection.MinCount, selection.MaxCount), true);
            int index = 0;
            foreach (var target in selection.Candidates)
            {
                var value = _presenter.FindCard(target.CardId);
                if (value == null || target.Zone == (int)PresentationZone.Field)
                {
                    continue;
                }
                _tokens.Add(TargetKey(target));
                Add(value, index++).Clicked = card => pick(target);
            }
        }

        public void SetSelected(List<string> selected)
        {
            for (int i = 0; i < _tokens.Count; ++i)
            {
                _cards[i].SetSelected(selected.Contains(_tokens[i]));
            }
        }

        private void ReleaseBrowse()
        {
            if (_browsingDeck)
            {
                _presenter.Client.CancelOwnDeckRead();
            }
            _browsingDeck = _refreshDeck = false;
            _deckReadDeadline = 0;
            _browseScope?.Dispose();
            _browseScope = null;
            _browseModal?.Dispose();
            _browseModal = null;
        }

        private void OnDisable() => ReleaseBrowse();
        private void OnDestroy()
        {
            ReleaseBrowse();
            if (_presenter != null && _presenter.Client != null)
            {
                _presenter.Client.OwnDeckContentsReceived -= ShowRemainingDeck;
                _presenter.Client.OwnDeckReadFailed -= DeckReadFailed;
                _presenter.Client.SnapshotReceived -= RefreshRemainingDeck;
                _presenter.Client.ViewInvalidated -= InvalidateBrowse;
            }
        }
    }
}
