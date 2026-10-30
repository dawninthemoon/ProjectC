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
        public readonly List<CardWidget> Widgets = new List<CardWidget>();
        public void Render(CardView[] cards, int slots, bool hidden, int selected, bool targets,
            Action<CardWidget> click, Action<CardWidget> hover, Action leave)
        {
            int count = hidden ? slots : (cards?.Length ?? 0);
            while (Widgets.Count < count) Widgets.Add(Instantiate(CardPrefab, Content));
            for (int i = 0; i < Widgets.Count; ++i)
            {
                var widget = Widgets[i]; widget.gameObject.SetActive(i < count);
                if (i >= count) continue;
                var card = !hidden && cards != null && i < cards.Length ? cards[i] : null;
                widget.Clicked = click; widget.Hovered = hover; widget.HoverLeft = leave;
                widget.Bind(card, hidden, card != null && card.Id == selected, targets && card != null, Hand);
            }
            GetComponent<CardTableLayout>()?.Arrange(Widgets);
        }
    }
}
