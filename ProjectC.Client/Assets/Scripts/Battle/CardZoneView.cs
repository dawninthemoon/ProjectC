using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectC.Client
{
    public sealed class CardZoneView : MonoBehaviour
    {
        public CardWidget CardPrefab;
        public RectTransform Content;
        public bool Hand;
        public bool OwnedByLocalPlayer;
        public readonly List<CardWidget> Widgets = new List<CardWidget>();
        private readonly List<CardWidget> _pool = new List<CardWidget>();
        public CardTableLayout Layout => GetComponent<CardTableLayout>();

        public void Render(
            CardView[] cards, int slots, bool hidden, int selected, bool targets,
            Action<CardWidget> click, Action<CardWidget> hover, Action leave)
        {
            if (CardPrefab == null || Content == null)
            {
                throw new InvalidOperationException("CardZoneView requires a card prefab and Content: " + name);
            }
            int count = hidden ? slots : cards?.Length ?? 0;
            var previous = new List<CardWidget>(Widgets);
            Widgets.Clear();
            for (int i = 0; i < count; ++i)
            {
                var card = hidden ? null : cards[i];
                var widget = hidden
                    ? (i < previous.Count ? previous[i] : null)
                    : previous.Find(w => w.Card?.Id == card.Id);
                if (widget == null)
                {
                    widget = Acquire();
                }
                Widgets.Add(widget);
                if (!widget.IsInFlight && widget.transform.parent != Content)
                {
                    widget.transform.SetParent(Content, false);
                }
                widget.gameObject.SetActive(true);
                widget.Clicked = click;
                widget.Hovered = hover;
                widget.HoverLeft = leave;
                widget.SortRequested = RestoreSorting;
                widget.Bind(card, hidden, card != null && card.Id == selected, targets && card != null, Hand, OwnedByLocalPlayer);
            }
            foreach (var widget in previous)
            {
                if (!Widgets.Contains(widget))
                {
                    widget.SetInFlight(false);
                    widget.transform.SetParent(Content, false);
                    widget.gameObject.SetActive(false);
                    _pool.Add(widget);
                }
            }
            Layout?.Arrange(Widgets);
            RestoreSorting();
        }

        private CardWidget Acquire()
        {
            if (_pool.Count == 0)
            {
                return Instantiate(CardPrefab, Content);
            }
            int index = _pool.Count - 1;
            var widget = _pool[index];
            _pool.RemoveAt(index);
            widget.ResetPointer();
            return widget;
        }

        public void RestoreSorting()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            foreach (var widget in Widgets)
            {
                if (!widget.IsInFlight)
                {
                    widget.transform.SetAsLastSibling();
                }
            }
            foreach (var widget in Widgets)
            {
                if ((widget.IsHovered || widget.IsSelected) && !widget.IsInFlight)
                {
                    widget.transform.SetAsLastSibling();
                }
            }
        }

        public void CancelFlights()
        {
            foreach (var widget in Widgets)
            {
                if (widget.IsInFlight)
                {
                    if (isActiveAndEnabled)
                    {
                        widget.transform.SetParent(Content, false);
                    }
                    widget.SetInFlight(false);
                }
            }
            if (isActiveAndEnabled)
            {
                Layout?.Snap();
            }
            RestoreSorting();
        }
    }
}
