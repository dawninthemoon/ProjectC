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
        internal const float ExpandedHandFocusScale = 1.1f;
        private const float FieldHoverScale = 1.035f;
        public RectTransform Face;
        [FormerlySerializedAs("Frame")]
        public Image FrameImage;
        [FormerlySerializedAs("Art")]
        public Image ArtworkImage;
        public Image Accent;
        [SerializeField] private FieldCardCropProfile _fieldCropProfile;
        private TMP_Text _attack;
        private TMP_Text _health;
        private TMP_Text _cost;
        private Outline _highlight;
        private Image _frameGlow;
        private Color _glowColor;
        private bool _attackAllowed;
        private bool _activationAllowed;
        private Vector2 _facePosition;
        private Vector3 _faceScale;
        public TMP_Text Title,
            Stats,
            Status,
            Symbol;
        public CardView Card { get; private set; }
        public bool Hidden { get; private set; }
        public bool InHand { get; private set; }
        public bool OwnedByLocalPlayer { get; private set; }
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
            Face.Bind("FrameGlow", out _frameGlow);
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
            BattleText.StyleBody(Title);
            BattleText.StyleValue(_attack);
            BattleText.StyleValue(_health);
            BattleText.StyleValue(_cost);
            if (ArtworkImage is not AspectFillImage)
            {
                throw new InvalidOperationException("Card prefab ArtworkImage requires AspectFillImage.");
            }
            _facePosition = Face.anchoredPosition;
            _faceScale = Face.localScale;
            opacity = GetComponent<CanvasGroup>();
            bound = true;
        }

        public void Bind(CardView card, bool hidden, bool chosen, bool target, bool hand, bool ownedByLocalPlayer = false)
        {
            BindReferences();
            Card = hidden ? null : card;
            Hidden = hidden;
            InHand = hand;
            OwnedByLocalPlayer = ownedByLocalPlayer || hand;
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
            if (ArtworkImage is AspectFillImage aspectFill)
            {
                if (visible && !hand && _fieldCropProfile != null)
                {
                    _fieldCropProfile.Resolve(card.DefinitionId, out float zoom, out Vector2 offset);
                    aspectFill.SetCrop(zoom, offset);
                }
                else
                {
                    aspectFill.SetCrop(1f, Vector2.zero);
                }
            }
            ArtworkImage.gameObject.SetActive(!hidden);
            Title.gameObject.SetActive(!hidden && (hand || _fieldCropProfile == null));
            Symbol.text =
                hidden ? "C"
                : card != null && card.EffectCount > 0 ? "W"
                : "S";
            RenderStatus();
        }

        public void SetActionPermissions(bool attackAllowed, bool activationAllowed)
        {
            if (_attackAllowed == attackAllowed && _activationAllowed == activationAllowed)
            {
                return;
            }
            _attackAllowed = attackAllowed;
            _activationAllowed = activationAllowed;
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
            // Server availability and current client input permissions must both allow an action.
            bool hasActivation = Card?.Activations != null
                && Array.Exists(Card.Activations, action => action.CanActivate);
            bool usable = !Hidden && OwnedByLocalPlayer && Card != null
                && (InHand ? Card.CanSummon || hasActivation
                    : _attackAllowed && Card.CanAttack || _activationAllowed && hasActivation);
            Accent.color =
                hint == TargetHint.Focused ? new Color(1, .8f, .35f)
                : IsSelected ? new Color(.98f, .76f, .33f)
                : hint == TargetHint.Available ? new Color(.34f, .75f, 1f)
                : hint == TargetHint.Unavailable ? new Color(.22f, .25f, .29f)
                : usable ? new Color(.15f, .91f, .86f)
                : new Color(.28f, .36f, .43f);
            if (_highlight != null)
            {
                // The rectangular uGUI outline obscures the authored metal silhouette.
                _highlight.enabled = false;
                _highlight.effectColor = Accent.color;
            }
            if (_frameGlow != null)
            {
                _glowColor = Accent.color;
                _glowColor.a = !Hidden && hint != TargetHint.Unavailable
                    && (IsSelected || hint == TargetHint.Available || hint == TargetHint.Focused || usable) ? .65f : 0f;
                ApplyFrameGlow();
            }
            Status.text = "";
            Status.gameObject.SetActive(false);
        }

        private void ApplyFrameGlow()
        {
            if (_frameGlow == null)
            {
                return;
            }
            var color = _glowColor;
            if (pulse > color.a)
            {
                color = new Color(1f, .82f, .38f, pulse);
            }
            _frameGlow.color = color;
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
            if (pulse > 0)
            {
                pulse = Mathf.MoveTowards(pulse, 0, Time.unscaledDeltaTime * 3);
                ApplyFrameGlow();
            }
            // Scale the face relative to its layout slot. An absolute display scale
            // would shrink a focused card whenever the expanded hand grows larger.
            float focusScale = InHand ? (ExpandedHand ? ExpandedHandFocusScale : 1) : FieldHoverScale;
            bool focused = !dragging && (IsHovered || (InHand && ExpandedHand && IsSelected));
            float scale = (focused ? focusScale : 1) + pulse * .08f;
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
