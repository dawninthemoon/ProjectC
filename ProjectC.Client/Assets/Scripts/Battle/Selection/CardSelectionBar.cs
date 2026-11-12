using System;
using System.Collections.Generic;
using System.Linq;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class CardSelectionBar : MonoBehaviour
    {
        private const float CardWidth = 155;
        private const float CardHeight = 215;
        private const float CardPitch = 167;
        private const float ScrollPadding = 12;
        private RectTransform _content;
        private RectTransform _viewport;
        private GameObject _panel;
        private GameObject _guide;
        private TMP_Text _title;
        private TMP_Text _count;
        private Button _confirm;
        private Button _cancel;
        private Button _eye;
        private ScrollRect _scroll;
        private readonly List<CardWidget> _cards = new List<CardWidget>();
        private readonly List<GameObject> _checks = new List<GameObject>();
        private readonly List<TMP_Text> _sources = new List<TMP_Text>();
        private readonly CardSelectionDraft _draft = new CardSelectionDraft();
        private CardSelectionRequest _request;
        private CardWidget _cardPrefab;
        private Action<string> _pick;
        private Action<CardView> _inspect;
        private bool _submitting;
        private bool _bound;
        private BattlePrimaryActionOverride _passAction;
        private ModalBlocker.Lease _passModal;
        private bool _hidden;
        private int _selectedCount;
        private string _language;
        private float _lastViewportWidth;
        public CardSelectionDraft Draft => _draft;
        public bool Hidden => _hidden;
        public ScrollRect Scroll => _scroll;
        public Button ConfirmButton => _confirm;
        public event Action<bool> VisibilityChanged;

        private void BindReferences()
        {
            if (_bound)
            {
                return;
            }
            if (!this.Bind("SelectionContent", out _content) || !this.Bind("SelectionViewport", out _viewport)
                || !this.Bind("SelectionTitle", out _title) || !this.Bind("SelectionCount", out _count)
                || !this.Bind("SelectionConfirm", out _confirm) || !this.Bind("SelectionCancel", out _cancel)
                || !this.Bind("SelectionEye", out _eye) || !this.Bind("SelectionScroll", out _scroll))
            {
                throw new InvalidOperationException("CardSelectionBar is missing an authored UI binding.");
            }
            _panel = _scroll.transform.parent.gameObject;
            _guide = _title.transform.parent.gameObject;
            _eye.onClick.AddListener(ToggleVisibility);
            _bound = true;
        }

        public void Show(CardSelectionRequest request, CardWidget cardPrefab, Action<string> pick,
            Action confirm, Action cancel, Action<CardView> inspect, Action pass = null, BattlePresenter presenter = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            BindReferences();
            bool sameRequest = _request != null && (ReferenceEquals(_request, request)
                || !string.IsNullOrEmpty(request.RequestId) && _request.RequestId == request.RequestId);
            float scrollPosition = sameRequest ? _scroll.horizontalNormalizedPosition : 0;
            _draft.Refresh(request);
            if (!sameRequest)
            {
                _draft.Restore(Array.Empty<string>());
                _hidden = false;
            }
            _request = request;
            _selectedCount = _draft.Selected.Count;
            _cardPrefab = cardPrefab != null ? cardPrefab : throw new ArgumentNullException(nameof(cardPrefab));
            _pick = pick;
            _inspect = inspect;
            _submitting = false;
            _passAction?.Dispose();
            _passModal?.Dispose();
            _passModal = null;
            _passAction = request.AllowPass && presenter != null && pass != null
                ? new BattlePrimaryActionOverride(presenter.EndTurn, "ui.selection.pass", () =>
                {
                    if (presenter.Client.Input.CanExecute(GameplayInputAction.ConfirmSelection))
                    {
                        pass();
                    }
                }) : null;
            if (_passAction != null)
            {
                _passModal = presenter.Modal.Acquire(this, null, (RectTransform)transform,
                    (RectTransform)presenter.EndTurn.transform, (RectTransform)presenter.Detail.transform);
                _passModal.BackdropTint = Color.clear;
            }
            _title.text = string.IsNullOrEmpty(request.TitleKey) ? request.Title : BattleText.Get(request.TitleKey, request.TitleArguments);
            _language = CardPresentation.Library.Localization.Language;
            _confirm.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.confirm");
            _cancel.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.cancel");
            _confirm.onClick.RemoveAllListeners();
            _confirm.onClick.AddListener(() =>
            {
                if (!_submitting && CanConfirm())
                {
                    confirm?.Invoke();
                }
            });
            _cancel.onClick.RemoveAllListeners();
            _cancel.onClick.AddListener(() =>
            {
                if (!_submitting && request.AllowCancel)
                {
                    cancel?.Invoke();
                }
            });
            _cancel.gameObject.SetActive(request.AllowCancel);
            _eye.gameObject.SetActive(request.AllowEye);
            for (int i = 0; i < request.Items.Length; ++i)
            {
                AddItem(request.Items[i], i);
            }
            for (int i = request.Items.Length; i < _cards.Count; ++i)
            {
                _cards[i].transform.parent.gameObject.SetActive(false);
            }
            gameObject.SetActive(true);
            SetVisibility();
            Canvas.ForceUpdateCanvases();
            LayoutCards();
            _scroll.horizontalNormalizedPosition = scrollPosition;
            RefreshSelection();
        }

        private void AddItem(CardSelectionItem item, int index)
        {
            if (index == _cards.Count)
            {
                var slot = new GameObject("SelectionSlot", typeof(RectTransform));
                var rect = (RectTransform)slot.transform;
                rect.SetParent(_content, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, .5f);
                rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                var widget = Instantiate(_cardPrefab, rect);
                var face = (RectTransform)widget.transform;
                face.anchorMin = face.anchorMax = face.pivot = new Vector2(.5f, .5f);
                face.anchoredPosition = Vector2.zero;
                face.localRotation = Quaternion.identity;
                float scale = Mathf.Min(CardWidth / face.rect.width, CardHeight / face.rect.height);
                face.localScale = Vector3.one * scale;
                _cards.Add(widget);
                var badge = new GameObject("SelectionCheck", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionFrameGraphic));
                var badgeRect = (RectTransform)badge.transform;
                badgeRect.SetParent(rect, false);
                badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1, 1);
                badgeRect.anchoredPosition = new Vector2(-23, -52);
                badgeRect.sizeDelta = new Vector2(38, 38);
                var graphic = badge.GetComponent<SelectionFrameGraphic>();
                graphic.Shape = SelectionFrameGraphic.FrameShape.Check;
                graphic.color = new Color(.68f, .16f, .16f);
                graphic.Border = new Color(1, .86f, .63f);
                graphic.raycastTarget = false;
                _checks.Add(badge);
                var plate = new GameObject("SelectionSourcePlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionFrameGraphic));
                var plateRect = (RectTransform)plate.transform;
                plateRect.SetParent(rect, false);
                plateRect.anchorMin = plateRect.anchorMax = plateRect.pivot = new Vector2(0, 1);
                plateRect.anchoredPosition = new Vector2(10, -40);
                plateRect.sizeDelta = new Vector2(90, 22);
                var plateGraphic = plate.GetComponent<SelectionFrameGraphic>();
                plateGraphic.color = new Color(.15f, .13f, .16f, .94f);
                plateGraphic.BorderWidth = 1;
                plateGraphic.raycastTarget = false;
                var source = new GameObject("SelectionSource", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var label = source.GetComponent<TMP_Text>();
                label.rectTransform.SetParent(plateRect, false);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                label.fontSize = 16;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                BattleText.StyleHud(label);
                _sources.Add(label);
            }
            CardWidget card = _cards[index];
            card.transform.parent.gameObject.SetActive(true);
            card.Bind(item.Card, false, false, false, true);
            card.SetActionPermissions(false, false, false);
            card.Clicked = selected =>
            {
                if (!_submitting && item.Enabled)
                {
                    _pick?.Invoke(item.Key);
                }
            };
            card.Hovered = selected => _inspect?.Invoke(item.Card);
            card.HoverLeft = null;
            card.DragStarted = (selected, pointer) => _scroll.OnBeginDrag(pointer);
            card.DragMoved = (selected, pointer) => _scroll.OnDrag(pointer);
            card.DragEnded = (selected, pointer) => _scroll.OnEndDrag(pointer);
            var opacity = card.GetComponent<CanvasGroup>();
            opacity.alpha = item.Enabled ? 1 : .45f;
            _sources[index].text = string.IsNullOrEmpty(item.SourceKey) ? item.SourceLabel ?? "" : BattleText.Get(item.SourceKey);
            _sources[index].transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(_sources[index].text));
        }

        public void RenderPrimaryAction() => _passAction?.Render(_submitting);

        private void OnDisable()
        {
            _passAction?.Dispose();
            _passAction = null;
            _passModal?.Dispose();
            _passModal = null;
            _request = null;
        }

        private void LateUpdate()
        {
            RenderPrimaryAction();
            if (_bound && _request != null && _language != CardPresentation.Library.Localization.Language)
            {
                _language = CardPresentation.Library.Localization.Language;
                _title.text = string.IsNullOrEmpty(_request.TitleKey) ? _request.Title : BattleText.Get(_request.TitleKey, _request.TitleArguments);
                _confirm.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.confirm");
                _cancel.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.choice.cancel");
                for (int i = 0; i < _request.Items.Length; ++i)
                {
                    var item = _request.Items[i];
                    _sources[i].text = string.IsNullOrEmpty(item.SourceKey) ? item.SourceLabel ?? "" : BattleText.Get(item.SourceKey);
                }
                RefreshSelection();
            }
            if (_bound && _request != null && Mathf.Abs(_lastViewportWidth - _viewport.rect.width) > .5f)
            {
                LayoutCards();
            }
        }

        private void LayoutCards()
        {
            _lastViewportWidth = _viewport.rect.width;
            float width = _request.Items.Length == 0 ? 0 : CardWidth + (_request.Items.Length - 1) * CardPitch;
            _content.sizeDelta = new Vector2(Mathf.Max(_viewport.rect.width, width + ScrollPadding * 2), 0);
            float left = width + ScrollPadding * 2 <= _viewport.rect.width
                ? (_viewport.rect.width - width) * .5f : ScrollPadding;
            for (int i = 0; i < _request.Items.Length; ++i)
            {
                ((RectTransform)_cards[i].transform.parent).anchoredPosition = new Vector2(left + i * CardPitch, -2);
            }
            _scroll.horizontal = width + ScrollPadding * 2 > _viewport.rect.width;
            if (!_scroll.horizontal)
            {
                _content.anchoredPosition = Vector2.zero;
            }
        }

        public void SetSelected(IEnumerable<string> selected)
        {
            var keys = selected.ToArray();
            _draft.Restore(keys);
            _selectedCount = keys.Length;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _request.Items.Length; ++i)
            {
                bool selected = _draft.Selected.Contains(_request.Items[i].Key);
                _cards[i].SetSelected(selected);
                _checks[i].SetActive(selected);
            }
            _count.text = _request.MinCount == _request.MaxCount
                ? BattleText.Get("ui.selection.count", _selectedCount, _request.MaxCount)
                : BattleText.Get("ui.selection.range", _selectedCount, _request.MinCount, _request.MaxCount);
            _confirm.interactable = !_submitting && CanConfirm();
            _confirm.GetComponent<SelectionFrameGraphic>().color = _confirm.interactable
                ? new Color(.725f, .176f, .192f) : new Color(.329f, .322f, .325f);
            _confirm.GetComponentInChildren<TMP_Text>().color = _confirm.interactable
                ? new Color(1, 1, .97f) : new Color(.773f, .761f, .753f);
            _cancel.interactable = !_submitting && _request.AllowCancel;
        }

        private bool CanConfirm() => _selectedCount >= _request.MinCount && _selectedCount <= _request.MaxCount;

        public void SetSubmitting(bool submitting)
        {
            _submitting = submitting;
            if (_request != null)
            {
                RefreshSelection();
            }
        }

        public void ToggleVisibility()
        {
            _hidden = !_hidden;
            SetVisibility();
        }

        private void SetVisibility()
        {
            _panel.SetActive(!_hidden);
            _guide.SetActive(!_hidden);
            if (!_hidden && _request != null)
            {
                RefreshSelection();
            }
            _eye.GetComponent<SelectionFrameGraphic>().EyeClosed = _hidden;
            _eye.GetComponent<Graphic>().color = _hidden ? new Color(.7f, .32f, .25f) : new Color(.15f, .13f, .16f);
            VisibilityChanged?.Invoke(_hidden);
        }
    }
}
