using System;
using System.Collections.Generic;
using System.Linq;
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
        private CardSelectionBar _selectionBar;
        [SerializeField] private bool _showFieldMirrors = true;
        private Outline _graveOutline;
        private Color _graveOutlineColor;
        private Vector2 _graveOutlineDistance;
        private bool _graveOutlineEnabled;
        private TMP_Text _graveLabel;
        private readonly Dictionary<GameObject, bool> _browseVisibility = new Dictionary<GameObject, bool>();
        private Vector2 _browseAnchorMin, _browseAnchorMax, _browsePivot, _browsePosition, _browseSize;
        private bool _compactChoice;
        private bool _detailWasVisible;
        public CardSelectionBar SelectionBar => _selectionBar;
        private Button _close;
        private readonly List<CardWidget> _cards = new List<CardWidget>();
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
            if (!this.Bind("CardSelectionBar", out _selectionBar))
            {
                throw new InvalidOperationException("CardGallery requires the shared CardSelectionBar prefab.");
            }
            _selectionBar.VisibilityChanged += HideChoiceDetails;
            _close.onClick.AddListener(Close);
            presenter.Client.OwnDeckContentsReceived += ShowRemainingDeck;
            presenter.Client.OwnDeckReadFailed += DeckReadFailed;
            presenter.Client.SnapshotReceived += RefreshRemainingDeck;
            presenter.Client.ViewInvalidated += InvalidateBrowse;
        }

        public void Close()
        {
            ReleaseBrowse();
            SetGraveyardFocus(false);
            RestoreBrowseLayout();
            gameObject.SetActive(false);
            Choosing = false;
        }

        private void Begin(string title, bool choosing)
        {
            ReleaseBrowse();
            if (choosing)
            {
                UseChoiceLayout();
            }
            else
            {
                RestoreBrowseLayout();
            }
            Choosing = choosing;
            _title.text = title;
            _title.gameObject.SetActive(!choosing);
            _close.gameObject.SetActive(!choosing);
            foreach (var card in _cards)
            {
                card.transform.parent.gameObject.SetActive(false);
            }
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
            if (_graveLabel != null && _graveLabel.gameObject.activeSelf)
            {
                _graveLabel.text = BattleText.Get("ui.selection.own_grave");
            }
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
            var items = (choice.Candidates ?? Array.Empty<ActionChoiceCandidateView>())
                .Where(option => option.Card != null || option.Zone == (int)PresentationZone.Field && option.CardId != 0)
                .Where(option => _showFieldMirrors || option.Zone != (int)PresentationZone.Field)
                .Select(option => new CardSelectionItem
                {
                    Key = option.Token,
                    Card = option.Card ?? _presenter.FindCard(option.CardId),
                }).Where(item => item.Card != null).ToArray();
            // The current DTO does not identify discard/devour/necromancy. Do not infer a rule from its zone.
            string key = choice.IsCost
                ? choice.MinCount == choice.MaxCount ? "ui.selection.cost_exact" : "ui.selection.cost"
                : choice.MinCount == choice.MaxCount ? "ui.selection.targets_exact" : "ui.selection.targets";
            ShowSelection(new CardSelectionRequest { RequestId = choice.ChoiceId,
                TitleKey = key, TitleArguments = new object[] { choice.MinCount, choice.MaxCount }, Items = items,
                MinCount = choice.MinCount, MaxCount = choice.MaxCount, AllowCancel = choice.CanCancel }, pick);
            SetGraveyardFocus(choice.Candidates.Length > 0 && choice.Candidates.All(option =>
                option.Zone == (int)PresentationZone.Graveyard && option.PlayerId == _presenter.Client.PlayerId));
        }

        private void ShowSelection(CardSelectionRequest request, Action<string> pick)
        {
            var picker = _presenter.GetComponentInChildren<EffectTargetPicker>(true);
            _selectionBar.Show(request, _presenter.Hand.CardPrefab, pick, picker.ConfirmSelection,
                picker.CancelSelection, card => _presenter.InspectCard(card, true));
        }

        private void UseChoiceLayout()
        {
            if (_compactChoice)
            {
                return;
            }
            _compactChoice = true;
            var rect = (RectTransform)transform;
            _browseAnchorMin = rect.anchorMin;
            _browseAnchorMax = rect.anchorMax;
            _browsePivot = rect.pivot;
            _browsePosition = rect.anchoredPosition;
            _browseSize = rect.sizeDelta;
            _browseVisibility.Clear();
            foreach (Transform child in transform)
            {
                if (child != _selectionBar.transform)
                {
                    _browseVisibility[child.gameObject] = child.gameObject.activeSelf;
                    child.gameObject.SetActive(false);
                }
            }
            GetComponent<Image>().enabled = false;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void RestoreBrowseLayout()
        {
            if (!_compactChoice)
            {
                return;
            }
            if (_selectionBar.Hidden)
            {
                HideChoiceDetails(false);
            }
            _selectionBar.gameObject.SetActive(false);
            var rect = (RectTransform)transform;
            rect.anchorMin = _browseAnchorMin;
            rect.anchorMax = _browseAnchorMax;
            rect.pivot = _browsePivot;
            rect.anchoredPosition = _browsePosition;
            rect.sizeDelta = _browseSize;
            GetComponent<Image>().enabled = true;
            foreach (var entry in _browseVisibility)
            {
                entry.Key.SetActive(entry.Value);
            }
            _compactChoice = false;
        }

        private void HideChoiceDetails(bool hidden)
        {
            if (hidden)
            {
                _detailWasVisible = _presenter.Detail.gameObject.activeSelf;
                _presenter.Detail.gameObject.SetActive(false);
            }
            else if (_detailWasVisible)
            {
                _presenter.Detail.gameObject.SetActive(true);
                _detailWasVisible = false;
            }
        }

        public static string TargetKey(EffectTargetView target)
        {
            return target.PlayerId + ":" + target.Zone + ":" + target.CardId;
        }

        public void ShowTargets(EffectSelectionView selection, Action<EffectTargetView> pick)
        {
            Begin(BattleText.Get("ui.choice.targets", selection.MinCount, selection.MaxCount), true);
            SetGraveyardFocus(selection.Candidates.Length > 0 && selection.Candidates.All(target =>
                target.Zone == (int)PresentationZone.Graveyard && target.PlayerId == _presenter.Client.PlayerId));
            var targets = selection.Candidates.Where(target => target.CardId != 0)
                .Where(target => _showFieldMirrors || target.Zone != (int)PresentationZone.Field)
                .Where(target => _presenter.FindCard(target.CardId) != null).ToArray();
            ShowSelection(new CardSelectionRequest
            {
                RequestId = "local-" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(selection),
                TitleKey = selection.MinCount == selection.MaxCount ? "ui.selection.targets_exact" : "ui.selection.targets", TitleArguments = new object[] { selection.MinCount, selection.MaxCount },
                MinCount = selection.MinCount, MaxCount = selection.MaxCount,
                Items = targets.Select(target => new CardSelectionItem
                {
                    Key = TargetKey(target), Card = _presenter.FindCard(target.CardId),
                }).ToArray(),
            }, key => pick(targets.First(target => TargetKey(target) == key)));
        }

        public void SetSelected(List<string> selected)
        {
            if (Choosing)
            {
                _selectionBar.SetSelected(selected);
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

        private void SetGraveyardFocus(bool focused)
        {
            if (focused && _graveOutline == null)
            {
                if (!_presenter.Bind("OwnGraveTagImage", out Image tag))
                {
                    Debug.LogError("Graveyard selection requires OwnGraveTagImage.", this);
                    return;
                }
                _graveOutline = tag.GetComponent<Outline>();
                if (_graveOutline == null)
                {
                    _graveOutline = tag.gameObject.AddComponent<Outline>();
                    _graveOutline.enabled = false;
                }
                _graveOutlineColor = _graveOutline.effectColor;
                _graveOutlineDistance = _graveOutline.effectDistance;
                _graveOutlineEnabled = _graveOutline.enabled;
                var root = new GameObject("ChoiceGraveLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                _graveLabel = root.GetComponent<TMP_Text>();
                var rect = _graveLabel.rectTransform;
                rect.SetParent(tag.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = new Vector2(.5f, 1);
                rect.anchoredPosition = new Vector2(0, -6);
                rect.sizeDelta = new Vector2(130, 28);
                _graveLabel.fontSize = _graveLabel.fontSizeMax = 22;
                _graveLabel.fontSizeMin = 16;
                _graveLabel.enableAutoSizing = true;
                _graveLabel.alignment = TextAlignmentOptions.Center;
                _graveLabel.raycastTarget = false;
                BattleText.StyleHud(_graveLabel);
            }
            if (_graveOutline != null)
            {
                _graveOutline.effectColor = focused ? new Color(1, .65f, .2f, .9f) : _graveOutlineColor;
                _graveOutline.effectDistance = focused ? new Vector2(3, -3) : _graveOutlineDistance;
                _graveOutline.enabled = focused || _graveOutlineEnabled;
                if (_graveLabel != null)
                {
                    if (focused)
                    {
                        _graveLabel.text = BattleText.Get("ui.selection.own_grave");
                    }
                    _graveLabel.gameObject.SetActive(focused);
                }
            }
        }

        private void OnDisable()
        {
            ReleaseBrowse();
            SetGraveyardFocus(false);
        }
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
