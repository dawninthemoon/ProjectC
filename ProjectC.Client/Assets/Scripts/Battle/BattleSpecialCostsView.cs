using System;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattleSpecialCostsView : MonoBehaviour
    {
        private const float RowHeight = 46f;
        private const float RowSpacing = 6f;
        private const int VisibleRows = 4;
        private const float HeaderHeight = 32f;
        [SerializeField] private BattleCostItemView _itemPrefab;
        [SerializeField, Min(0)] private float _headerHeight = HeaderHeight;
        [SerializeField, Min(0)] private float _bottomPadding = 8f;
        [SerializeField] private float _expandedChevronAngle;
        [SerializeField] private float _collapsedChevronAngle = 180f;
        private RectTransform _body;
        private readonly List<BattleCostItemView> _items = new List<BattleCostItemView>();
        private RectTransform _viewport, _content, _tooltip, _chevron;
        private TMP_Text _title, _amount, _description;
        private ScrollRect _scroll;
        private bool _collapsed;
        private bool _bound;
        private BattleCostItemView _hovered;
        private string _tooltipLanguage;
        private Vector2Int _tooltipResolution;
        public IReadOnlyList<BattleCostItemView> Items => _items;
        public bool IsCollapsed => _collapsed;
        public bool TooltipVisible => _tooltip.gameObject.activeSelf;
        public ScrollRect Scroll => _scroll;

        private void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_bound)
            {
                return;
            }
            if (_itemPrefab == null || !this.Bind("CostViewportImage", out _viewport) || !this.Bind("CostContent", out _content)
                || !this.Bind("CostTooltipImage", out _tooltip) || !this.Bind("CostTooltipTitleText", out _title)
                || !this.Bind("CostTooltipAmountText", out _amount) || !this.Bind("CostTooltipDescriptionText", out _description)
                || !this.Bind("CostChevronImage", out _chevron) || !this.Bind("CostToggleButton", out Button toggle))
            {
                throw new InvalidOperationException("Special cost list is missing its prefab or hierarchy bindings.");
            }
            _scroll = _viewport.GetComponent<ScrollRect>();
            if (_scroll == null)
            {
                throw new InvalidOperationException("Special cost viewport requires ScrollRect.");
            }
            toggle.onClick.AddListener(Toggle);
            this.Bind("CostBodyImage", out _body);
            _bound = true;
            HideTooltip();
        }

        public void Render(IReadOnlyList<BattleCostDisplay> costs)
        {
            Initialize();
            if (costs == null)
            {
                throw new ArgumentNullException(nameof(costs));
            }
            while (_items.Count < costs.Count)
            {
                _items.Add(Instantiate(_itemPrefab, _content));
            }
            for (int i = 0; i < _items.Count; i++)
            {
                bool visible = i < costs.Count;
                _items[i].gameObject.SetActive(visible);
                if (visible)
                {
                    if (costs[i].Definition == null)
                    {
                        throw new ArgumentException("A displayed cost requires its presentation definition.", nameof(costs));
                    }
                    _items[i].Bind(this, costs[i]);
                }
            }
            bool countChanged = Count != costs.Count;
            Count = costs.Count;
            gameObject.SetActive(Count > 0);
            ApplyLayout();
            if (countChanged)
            {
                _scroll.verticalNormalizedPosition = 1;
            }
            if (_hovered != null && _hovered.gameObject.activeInHierarchy && !_collapsed)
            {
                ShowTooltip(_hovered);
            }
            else
            {
                HideTooltip();
            }
        }

        public int Count { get; private set; }

        public void Toggle()
        {
            _collapsed = !_collapsed;
            HideTooltip();
            ApplyLayout();
            _scroll.verticalNormalizedPosition = 1;
        }

        private void ApplyLayout()
        {
            float height = Mathf.Min(Count, VisibleRows) * RowHeight + Mathf.Max(0, Mathf.Min(Count, VisibleRows) - 1) * RowSpacing;
            _viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            _viewport.gameObject.SetActive(!_collapsed && Count > 0);
            ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                _headerHeight + (_collapsed || Count == 0 ? 0 : height + _bottomPadding));
            if (_body != null)
            {
                _body.gameObject.SetActive(!_collapsed && Count > 0);
            }
            _chevron.localRotation = Quaternion.Euler(0, 0, _collapsed ? _collapsedChevronAngle : _expandedChevronAngle);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }

        public void ShowTooltip(BattleCostItemView item)
        {
            _hovered = item;
            _tooltipLanguage = CardPresentation.Library.Localization.Language;
            _tooltipResolution = new Vector2Int(Screen.width, Screen.height);
            _title.text = item.Data.Definition.Name;
            _amount.text = item.Data.Amount;
            _description.text = item.Data.Definition.Description;
            _tooltip.gameObject.SetActive(true);
            float height = Mathf.Clamp(_description.GetPreferredValues(_description.text, 260, 0).y + 110, 150, 280);
            _tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            _description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height - 110);
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, item.transform);
            _tooltip.anchoredPosition = new Vector2(144, bounds.max.y + 16);
            Canvas.ForceUpdateCanvases();
            var canvas = GetComponentInParent<Canvas>().rootCanvas;
            var corners = new Vector3[4];
            _tooltip.GetWorldCorners(corners);
            var bottomLeft = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[0]);
            var topRight = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, corners[2]);
            const float padding = 12;
            float dx = Mathf.Max(0, padding - bottomLeft.x) - Mathf.Max(0, topRight.x - Screen.width + padding);
            float dy = Mathf.Max(0, padding - bottomLeft.y) - Mathf.Max(0, topRight.y - Screen.height + padding);
            _tooltip.anchoredPosition += new Vector2(dx, dy) / canvas.scaleFactor;
        }

        private void LateUpdate()
        {
            if (_hovered != null && (_tooltipLanguage != CardPresentation.Library.Localization.Language
                || _tooltipResolution != new Vector2Int(Screen.width, Screen.height)))
            {
                ShowTooltip(_hovered);
            }
        }

        public void HideTooltip()
        {
            _hovered = null;
            if (_tooltip != null)
            {
                _tooltip.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            HideTooltip();
        }
    }
}
