using System;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public enum TargetHint
    {
        None,
        Unavailable,
        Available,
        Focused,
    }

    public sealed class CardWidget
        : MonoBehaviour,
            IPointerDownHandler,
            IPointerEnterHandler,
            IPointerExitHandler,
            IPointerClickHandler,
            IBeginDragHandler,
            IDragHandler,
            IEndDragHandler
    {
        private const float MinimumDisplayScale = .01f;
        public RectTransform Face;
        [FormerlySerializedAs("Frame")]
        public Image FrameImage;
        [FormerlySerializedAs("Art")]
        public Image ArtworkImage;
        public Image Accent;
        private TMP_Text _attack;
        private TMP_Text _health;
        private TMP_Text _cost;
        private Outline _highlight;
        private Vector2 _facePosition;
        private Vector3 _faceScale;
        public TMP_Text Title,
            Stats,
            Status,
            Symbol;
        public CardView Card { get; private set; }
        public bool Hidden { get; private set; }
        public bool InHand { get; private set; }
        public bool IsSelected { get; private set; }
        public bool IsHovered { get; private set; }
        public bool IsInFlight { get; private set; }
        public bool ExpandedHand { get; set; }
        public Action SortRequested;
        public Vector3 WorldCenter => Face.TransformPoint(Face.rect.center);
        public Action<CardWidget> Clicked,
            Hovered;
        public Action HoverLeft;
        public Action<CardWidget, PointerEventData> DragStarted,
            DragMoved,
            DragEnded;
        CanvasGroup opacity;
        bool bound,
            dragging,
            pointerDragged;
        float pulse;
        TargetHint hint;

        void Awake()
        {
            BindReferences();
        }

        void BindReferences()
        {
            if (bound)
            {
                return;
            }
            if (!this.Bind("FrameImage", out Face))
            {
                throw new InvalidOperationException("Card prefab requires FrameImage");
            }
            FrameImage = Face.GetComponent<Image>();
            _highlight = Face.GetComponent<Outline>();
            if (FrameImage == null
                || !Face.Bind("ArtworkImage", out ArtworkImage)
                || !Face.Bind("Accent", out Accent)
                || !Face.Bind("Name", out Title)
                || !Face.Bind("Stats", out Stats)
                || !Face.Bind("Status", out Status)
                || !Face.Bind("Monogram", out Symbol))
            {
                throw new InvalidOperationException("Card prefab requires frame, artwork, accent, name, stats, status and monogram components.");
            }
            Face.Bind("AttackBadge", out _attack);
            Face.Bind("HealthBadge", out _health);
            Face.Bind("CostBadge", out _cost);
            if (ArtworkImage is not AspectFillImage)
            {
                throw new InvalidOperationException("Card prefab ArtworkImage requires AspectFillImage.");
            }
            _facePosition = Face.anchoredPosition;
            _faceScale = Face.localScale;
            opacity = GetComponent<CanvasGroup>();
            bound = true;
        }

        public void Bind(CardView card, bool hidden, bool chosen, bool target, bool hand)
        {
            BindReferences();
            Card = hidden ? null : card;
            Hidden = hidden;
            InHand = hand;
            IsSelected = chosen;
            hint = target ? TargetHint.Available : TargetHint.None;
            Title.text = hidden ? "HIDDEN CARD" : CardPresentation.Name(card);
            bool visible = card != null && !hidden;
            bool hasStats = visible && card.CardType != 1;
            if (hasStats && (_attack == null || _health == null))
            {
                throw new InvalidOperationException("Face card prefab requires AttackBadge and HealthBadge text components.");
            }
            Stats.gameObject.SetActive(false);
            Stats.text = "";
            Status.gameObject.SetActive(false);
            Status.text = "";
            if (_attack != null && _health != null)
            {
                _attack.transform.parent.gameObject.SetActive(hasStats);
                _health.transform.parent.gameObject.SetActive(hasStats);
                _attack.text = hasStats ? card.Attack.ToString() : "";
                _health.text = hasStats ? card.Hp.ToString() : "";
            }
            if (_cost != null)
            {
                var costs = visible ? CardPresentation.Library.Find(card.DefinitionId)?.ActivationYokaiCosts : null;
                bool catalogMatches = ClientApp.Instance == null || string.IsNullOrEmpty(ClientApp.Instance.Network.CatalogProblem);
                bool showCost = hand && catalogMatches && costs != null && costs.Length > 0;
                _cost.transform.parent.gameObject.SetActive(showCost);
                _cost.text = showCost ? string.Join("/", costs) : "";
            }
            // Hidden cards never resolve an artwork resource from a definition ID.
            CardArtProvider.Apply(ArtworkImage, Symbol, visible ? card.DefinitionId : 0);
            Symbol.text =
                hidden ? "C"
                : card != null && card.EffectCount > 0 ? "W"
                : "S";
            RenderStatus();
        }

        public void SetHint(TargetHint value)
        {
            hint = value;
            RenderStatus();
        }

        public void SetSelected(bool value)
        {
            IsSelected = value;
            RenderStatus();
        }

        public void Dim(bool value)
        {
            if (opacity != null)
            {
                opacity.alpha = value ? .28f : 1f;
            }
        }

        public void Pulse()
        {
            pulse = 1;
        }

        void RenderStatus()
        {
            if (!bound)
            {
                return;
            }
            // Availability is supplied by the server and remains visible without selection.
            bool usable = !Hidden && Card != null && (Card.CanSummon || Card.CanAttack
                || Card.Activations != null && Array.Exists(Card.Activations, action => action.CanActivate));
            Accent.color =
                hint == TargetHint.Focused ? new Color(1, .8f, .35f)
                : hint == TargetHint.Available ? new Color(.38f, .9f, .71f)
                : hint == TargetHint.Unavailable ? new Color(.22f, .25f, .29f)
                : IsSelected ? new Color(.93f, .73f, .36f)
                : usable ? new Color(.25f, .95f, .7f)
                : new Color(.28f, .36f, .43f);
            if (_highlight != null)
            {
                _highlight.enabled = !Hidden && hint != TargetHint.Unavailable
                    && (IsSelected || hint == TargetHint.Available || hint == TargetHint.Focused || usable);
                _highlight.effectColor = Accent.color;
            }
            Status.text = "";
            Status.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!bound || IsInFlight || (dragging && !InHand))
            {
                return;
            }
            float lift =
                IsHovered && !dragging ? (InHand ? (ExpandedHand ? 12 : 0) : 8)
                : IsSelected ? 12
                : 0;
            Face.anchoredPosition = Vector2.Lerp(
                Face.anchoredPosition,
                _facePosition + new Vector2(0, lift),
                1 - Mathf.Exp(-Time.unscaledDeltaTime * 22)
            );
            pulse = Mathf.MoveTowards(pulse, 0, Time.unscaledDeltaTime * 3);
            float hoverScale = InHand
                ? (ExpandedHand ? 1.04f : transform.localScale.x) / Mathf.Max(MinimumDisplayScale, transform.localScale.x)
                : 1.035f;
            float scale = (IsHovered && !dragging ? hoverScale : 1) + pulse * .08f;
            Face.localScale = Vector3.Lerp(
                Face.localScale,
                _faceScale * scale,
                1 - Mathf.Exp(-Time.unscaledDeltaTime * 22)
            );
        }

        public void OnPointerDown(PointerEventData e)
        {
            pointerDragged = false;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (IsInFlight || Hidden || Card == null)
            {
                return;
            }
            if (!IsHovered)
            {
                if (InHand)
                {
                    transform.SetAsLastSibling();
                }
            }
            IsHovered = true;
            Hovered?.Invoke(this);
        }

        public void OnPointerExit(PointerEventData e)
        {
            IsHovered = false;
            if (InHand && !dragging)
            {
                SortRequested?.Invoke();
            }
            HoverLeft?.Invoke();
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (
                !IsInFlight
                && !Hidden
                && Card != null
                && !pointerDragged
                && (e == null || e.button == PointerEventData.InputButton.Left)
            )
            {
                Clicked?.Invoke(this);
            }
            pointerDragged = false;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (IsInFlight || Hidden || Card == null || e.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            pointerDragged = dragging = true;
            IsHovered = false;
            HoverLeft?.Invoke();
            DragStarted?.Invoke(this, e);
            dragging = pointerDragged = true;
        }

        public void OnDrag(PointerEventData e)
        {
            if (dragging)
            {
                DragMoved?.Invoke(this, e);
            }
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (dragging)
            {
                dragging = false;
                DragEnded?.Invoke(this, e);
            }
        }

        public void ResetPointer()
        {
            IsHovered = dragging = pointerDragged = false;
            Dim(false);
            SortRequested?.Invoke();
        }

        public void SetInFlight(bool value)
        {
            IsInFlight = value;
            ResetPointer();
            Face.anchoredPosition = _facePosition;
            Face.localScale = _faceScale;
            if (opacity != null)
            {
                opacity.blocksRaycasts = opacity.interactable = !value;
            }
        }

        void OnDisable()
        {
            // Unity forbids sibling changes while a parent hierarchy is being disabled.
            IsHovered = dragging = pointerDragged = false;
            Dim(false);
        }
    }
}
