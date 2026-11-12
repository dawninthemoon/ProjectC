using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class SelectionHorizontalScrollRect : ScrollRect
    {
        public override void OnScroll(PointerEventData eventData)
        {
            Vector2 original = eventData.scrollDelta;
            eventData.scrollDelta = new Vector2(Mathf.Abs(original.x) > Mathf.Abs(original.y) ? original.x : original.y, 0);
            base.OnScroll(eventData);
            eventData.scrollDelta = original;
        }
    }
}
