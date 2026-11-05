using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class CardTableLayout : MonoBehaviour
    {
        private const float FieldSpacing = 71f;
        private const float HandTransitionDuration = .18f;
        internal const float FocusLift = 42f;
        private const float EdgePadding = 16f;
        private const float MinimumDuration = .01f;
        private const float DefaultHandRotation = 7f;
        private const float FullFanCardCount = 5f;
        public bool Hand, Compact;
        public bool OpponentHand;
        public BattleVisualSettings Settings;
        [SerializeField, Min(0)] private float _fieldSpacing = FieldSpacing;
        private List<CardWidget> _cards;
        private readonly Dictionary<CardWidget, Motion> _motions = new Dictionary<CardWidget, Motion>();
        private readonly List<CardWidget> _retired = new List<CardWidget>();
        public float Spacing { get; private set; }
        public Vector2 CardSize => ((RectTransform)GetComponent<CardZoneView>().CardPrefab.transform).rect.size;

        // Keep the authored aspect ratio and all badges visible as a hand grows.
        // Flights and hover use the same uniform scale as the settled layout.
        public float CardScale
        {
            get
            {
                int count = _cards?.Count ?? 0;
                if (OpponentHand)
                {
                    return 1;
                }
                if (!Hand)
                {
                    if (count < 2)
                    {
                        return 1;
                    }
                    float availableFieldWidth = Mathf.Max(0, ((RectTransform)transform).rect.width - EdgePadding);
                    float desiredWidth = count * CardSize.x + (count - 1) * _fieldSpacing;
                    return Mathf.Min(1, availableFieldWidth / desiredWidth);
                }
                float maximum = Compact
                    ? Settings?.CompactHandScale ?? BattleVisualSettings.DefaultCompactHandScale
                    : Settings?.ExpandedHandScale ?? BattleVisualSettings.DefaultExpandedHandScale;
                if (count < 2)
                {
                    return maximum;
                }
                float available = Mathf.Max(0, ((RectTransform)transform).rect.width - EdgePadding);
                // Let spacing absorb additional cards before reducing their readable size.
                return Mathf.Min(maximum, available / CardSize.x);
            }
        }

        private sealed class Motion
        {
            public Vector2 From, Target;
            public Quaternion FromRotation, Rotation;
            public float Started, FromScale, Scale;
        }

        public void Arrange(List<CardWidget> widgets)
        {
            _cards = widgets;
            Apply(false);
        }

        public void Snap()
        {
            Apply(true);
        }

        public void GetSlot(int index, int count, out Vector2 position, out Quaternion rotation)
        {
            float angle = Hand
                ? (Settings?.HandRotation ?? DefaultHandRotation) * Mathf.Clamp01((count - 1) / (FullFanCardCount - 1))
                : 0;
            float radians = angle * Mathf.Deg2Rad;
            float width = (CardSize.x * Mathf.Cos(radians) + CardSize.y * Mathf.Sin(radians)) * CardScale;
            float preferred = OpponentHand ? CardSize.x * 1.02f : !Hand ? width + _fieldSpacing * CardScale : Compact
                ? Settings?.CompactHandSpacing ?? BattleVisualSettings.DefaultCompactHandSpacing
                : Settings?.ExpandedHandSpacing ?? BattleVisualSettings.DefaultExpandedHandSpacing;
            // Dense/narrow hands overlap progressively instead of crossing the available edges.
            float available = Mathf.Max(0, ((RectTransform)transform).rect.width - width - EdgePadding);
            Spacing = count < 2 ? 0 : Mathf.Min(preferred, available / (count - 1));
            float offset = index - (count - 1) * .5f;
            float ratio = count > 1 ? offset / ((count - 1) * .5f) : 0;
            position = new Vector2(offset * Spacing, Hand ? -ratio * ratio * (Settings?.HandCurve ?? 24f) : 0);
            rotation = Quaternion.Euler(0, 0, -ratio * angle);
        }

        private float FocusPositionX(float x)
        {
            float limit = Mathf.Max(0, (((RectTransform)transform).rect.width
                - CardSize.x * CardScale * CardWidget.ExpandedHandFocusScale - EdgePadding) * .5f);
            return Mathf.Clamp(x, -limit, limit);
        }

        public void Place(CardWidget card, int index, int count)
        {
            var rect = (RectTransform)card.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = CardSize;
            GetSlot(index, count, out var position, out var rotation);
            if (Hand && (card.IsHovered || (!Compact && card.IsSelected)))
            {
                position.y = FocusLift;
                position.x = FocusPositionX(position.x);
                rotation = Quaternion.identity;
            }
            rect.anchoredPosition = position;
            rect.localRotation = rotation;
            rect.localScale = Vector3.one * CardScale;
            _motions.Remove(card);
        }

        public void SetExpanded(bool expanded)
        {
            if (!Hand)
            {
                return;
            }
            Compact = !expanded;
        }

        private void LateUpdate()
        {
            Apply(false);
        }

        private void Apply(bool snap)
        {
            if (_cards == null)
            {
                return;
            }
            int count = _cards.Count;
            for (int i = 0; i < count; ++i)
            {
                var card = _cards[i];
                if (card == null || !card.gameObject.activeSelf || card.IsInFlight)
                {
                    continue;
                }
                card.ExpandedHand = Hand;
                var rect = (RectTransform)card.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = CardSize;
                GetSlot(i, count, out var target, out var rotation);
                if (Hand && (card.IsHovered || (!Compact && card.IsSelected)))
                {
                    target.y = FocusLift;
                    target.x = FocusPositionX(target.x);
                    rotation = Quaternion.identity;
                }
                if (snap)
                {
                    Place(card, i, count);
                    continue;
                }
                if (!_motions.TryGetValue(card, out var motion))
                {
                    motion = new Motion { Target = target, Rotation = rotation,
                        From = rect.anchoredPosition, FromRotation = rect.localRotation,
                        FromScale = rect.localScale.x, Scale = CardScale, Started = Time.unscaledTime };
                    _motions.Add(card, motion);
                }
                if (motion.Target != target || motion.Rotation != rotation || motion.Scale != CardScale)
                {
                    motion.From = rect.anchoredPosition;
                    motion.FromRotation = rect.localRotation;
                    motion.FromScale = rect.localScale.x;
                    motion.Scale = CardScale;
                    motion.Target = target;
                    motion.Rotation = rotation;
                    motion.Started = Time.unscaledTime;
                }
                float duration = Hand ? HandTransitionDuration : Settings?.LayoutMove ?? .22f;
                float progress = Mathf.Clamp01((Time.unscaledTime - motion.Started) / Mathf.Max(MinimumDuration, duration));
                float ease = Settings?.Ease(progress) ?? Mathf.SmoothStep(0, 1, progress);
                rect.anchoredPosition = Vector2.LerpUnclamped(motion.From, target, ease);
                rect.localRotation = Quaternion.SlerpUnclamped(motion.FromRotation, rotation, ease);
                rect.localScale = Vector3.one * Mathf.LerpUnclamped(motion.FromScale, motion.Scale, ease);
            }
            // Recycled widgets must not retain a previous card's motion.
            _retired.Clear();
            foreach (var card in _motions.Keys)
            {
                if (!_cards.Contains(card))
                {
                    _retired.Add(card);
                }
            }
            foreach (var card in _retired)
            {
                _motions.Remove(card);
            }
        }
    }
}
