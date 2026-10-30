using System;
using Game.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public enum TargetHint { None, Unavailable, Available, Focused }
    public sealed class CardWidget : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Face;
        public Image Frame, Art, Accent;
        public Text Title, Stats, Status, Symbol;
        public CardView Card { get; private set; }
        public bool Hidden { get; private set; }
        public bool InHand { get; private set; }
        public bool IsSelected { get; private set; }
        public bool IsHovered { get; private set; }
        public Vector3 WorldCenter => Face.TransformPoint(Face.rect.center);
        public Action<CardWidget> Clicked, Hovered;
        public Action HoverLeft;
        public Action<CardWidget,PointerEventData> DragStarted, DragMoved, DragEnded;
        CanvasGroup opacity;
        bool bound, dragging, pointerDragged;
        int originalSibling;
        float pulse;
        TargetHint hint;
        void Awake() { BindReferences(); }
        void BindReferences()
        {
            if(bound) return;
            if(!this.Bind("Face",out Face)) throw new InvalidOperationException("Card prefab requires Face");
            Frame=Face.GetComponent<Image>(); Face.Bind("PlaceholderArt",out Art); Face.Bind("Accent",out Accent);
            Face.Bind("Name",out Title); Face.Bind("Stats",out Stats); Face.Bind("Status",out Status); Face.Bind("Monogram",out Symbol);
            opacity=GetComponent<CanvasGroup>(); bound=true;
        }
        public void Bind(CardView card,bool hidden,bool chosen,bool target,bool hand)
        {
            BindReferences(); Card=hidden?null:card; Hidden=hidden; InHand=hand; IsSelected=chosen;
            hint=target?TargetHint.Available:TargetHint.None;
            Title.text=hidden?"HIDDEN CARD":card?.Name??"";
            Stats.text=card==null || hidden?"":$"{card.Attack} ATK   {card.Hp}/{card.MaxHp} HP";
            Art.color=hidden?new Color(.07f,.13f,.2f):card!=null && card.EffectCount>0?new Color(.22f,.2f,.33f):new Color(.1f,.29f,.31f);
            Symbol.text=hidden?"C":card!=null && card.EffectCount>0?"W":"S";
            RenderStatus();
        }
        public void SetHint(TargetHint value) { hint=value; RenderStatus(); }
        public void SetSelected(bool value) { IsSelected=value; RenderStatus(); }
        public void Dim(bool value) { if(opacity!=null) opacity.alpha=value ? .28f : 1f; }
        public void Pulse() { pulse=1; }
        void RenderStatus()
        {
            if(!bound) return;
            Frame.color=hint==TargetHint.Focused?new Color(1,.8f,.35f):hint==TargetHint.Available?new Color(.38f,.9f,.71f):hint==TargetHint.Unavailable?new Color(.22f,.25f,.29f):IsSelected?new Color(.93f,.73f,.36f):Card!=null && (Card.CanSummon || Card.CanAttack)?new Color(.24f,.64f,.58f):new Color(.28f,.36f,.43f);
            Accent.color=Frame.color;
            Status.text=Hidden?"HIDDEN":Card==null?"":hint==TargetHint.Focused?"TARGET SELECTED":hint==TargetHint.Available?"VALID TARGET":hint==TargetHint.Unavailable?"NOT A TARGET":IsSelected?"SELECTED":InHand?(Card.CanSummon?"DRAG TO SUMMON":"IN HAND"):Card.HasAttacked?"ATTACK USED":Card.SummoningSick?"SUMMONING SICKNESS":Card.CanAttack?"DRAG TO ATTACK":"ON FIELD";
        }
        void Update()
        {
            if(!bound || (dragging && !InHand)) return;
            float lift=IsHovered && !dragging?(InHand?40:8):IsSelected?12:0;
            Face.anchoredPosition=Vector2.Lerp(Face.anchoredPosition,new Vector2(0,lift),1-Mathf.Exp(-Time.unscaledDeltaTime*22));
            pulse=Mathf.MoveTowards(pulse,0,Time.unscaledDeltaTime*3);
            float scale=(IsHovered && !dragging?(InHand?1.1f:1.035f):1)+pulse*.08f;
            Face.localScale=Vector3.Lerp(Face.localScale,Vector3.one*scale,1-Mathf.Exp(-Time.unscaledDeltaTime*22));
        }
        public void OnPointerDown(PointerEventData e) { pointerDragged=false; }
        public void OnPointerEnter(PointerEventData e)
        {
            if(Hidden || Card==null) return;
            if(!IsHovered) { originalSibling=transform.GetSiblingIndex(); if(InHand) transform.SetAsLastSibling(); }
            IsHovered=true; Hovered?.Invoke(this);
        }
        public void OnPointerExit(PointerEventData e) { IsHovered=false; if(InHand && !dragging) transform.SetSiblingIndex(originalSibling); HoverLeft?.Invoke(); }
        public void OnPointerClick(PointerEventData e) { if(!Hidden && Card!=null && !pointerDragged && (e==null || e.button==PointerEventData.InputButton.Left)) Clicked?.Invoke(this); pointerDragged=false; }
        public void OnBeginDrag(PointerEventData e)
        {
            if(Hidden || Card==null || e.button!=PointerEventData.InputButton.Left) return;
            pointerDragged=dragging=true; IsHovered=false; HoverLeft?.Invoke(); DragStarted?.Invoke(this,e); dragging=pointerDragged=true;
        }
        public void OnDrag(PointerEventData e) { if(dragging) DragMoved?.Invoke(this,e); }
        public void OnEndDrag(PointerEventData e) { if(dragging) { dragging=false; DragEnded?.Invoke(this,e); } }
        public void ResetPointer() { IsHovered=dragging=pointerDragged=false; Dim(false); }
        void OnDisable() { ResetPointer(); }
    }
}
