using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class CardWidget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform Face;
        public Image Frame, Art, Accent;
        public Text Title, Stats, Status, Symbol;
        public CardView Card { get; private set; }
        public bool Hidden { get; private set; }
        public Action<CardWidget> Clicked, Hovered;
        public Action HoverLeft;
        bool selected, hover;
        public void Bind(CardView card, bool hidden, bool chosen, bool target, bool hand)
        {
            Card = card; Hidden = hidden; selected = chosen;
            Title.text = hidden ? "HIDDEN CARD" : card?.Name ?? "EMPTY SLOT";
            Stats.text = card == null || hidden ? "" : $"{card.Attack} ATK     {card.Hp}/{card.MaxHp} HP";
            Status.text = hidden ? "OPPONENT HAND" : card == null ? "FIELD" : chosen ? "SELECTED" : target ? "ATTACK TARGET" : hand ? (card.CanSummon ? "SUMMON AVAILABLE" : "IN HAND") : card.HasAttacked ? "ATTACK USED" : card.SummoningSick ? "SUMMONING SICKNESS" : card.CanAttack ? "READY TO ATTACK" : "ON FIELD";
            Frame.color = chosen ? new Color(.94f,.73f,.3f) : target ? new Color(.88f,.35f,.34f) : card != null && (card.CanSummon || card.CanAttack) ? new Color(.23f,.75f,.67f) : new Color(.22f,.32f,.4f);
            Art.color = hidden ? new Color(.09f,.17f,.25f) : card == null ? new Color(.055f,.09f,.12f) : card.EffectCount > 0 ? new Color(.18f,.26f,.34f) : new Color(.12f,.29f,.3f);
            Symbol.text = hidden ? "?" : card == null ? "+" : card.EffectCount > 0 ? "W" : "S";
            Accent.color = Frame.color;
            UpdatePose();
        }
        void UpdatePose() { Face.anchoredPosition = new Vector2(0, selected ? 14 : hover ? 9 : 0); Face.localScale = Vector3.one * (hover ? 1.04f : 1); }
        public void OnPointerEnter(PointerEventData e) { hover = true; UpdatePose(); Hovered?.Invoke(this); }
        public void OnPointerExit(PointerEventData e) { hover = false; UpdatePose(); HoverLeft?.Invoke(); }
        public void OnPointerClick(PointerEventData e) { if (!Hidden && Card != null) Clicked?.Invoke(this); }
        void OnDisable() { hover = false; }
    }
}
