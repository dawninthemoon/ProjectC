using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectC.Client
{
    public sealed class DefenseCardHandle : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerDownHandler
    {
        public DefenseAssignmentView View;
        public CardWidget Card;
        public bool Attacker;
        private bool _dragged;
        public void OnPointerDown(PointerEventData eventData) => _dragged = false;
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            _dragged = true;
            View.BeginDrag(this, eventData);
        }
        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                View.MoveDrag(eventData);
            }
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                View.EndDrag(eventData);
            }
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!_dragged && eventData.button == PointerEventData.InputButton.Left)
            {
                View.Select(this);
            }
            _dragged = false;
        }
        public void OnPointerEnter(PointerEventData eventData) => View.Inspect(Card.Card);
    }
}
