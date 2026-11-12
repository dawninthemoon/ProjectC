using UnityEngine;

namespace ProjectC.Client
{
    public static class CardSelectionMark
    {
        public static void Set(CardWidget card, bool selected)
        {
            var mark = card.Face.Find("FieldSelectionCheck");
            if (mark == null && selected)
            {
                var root = new GameObject("FieldSelectionCheck", typeof(RectTransform), typeof(CanvasRenderer), typeof(SelectionFrameGraphic));
                var rect = (RectTransform)root.transform;
                rect.SetParent(card.Face, false);
                rect.anchorMin = rect.anchorMax = Vector2.one;
                rect.anchoredPosition = new Vector2(-card.Face.rect.width * .12f, -card.Face.rect.height * .2f);
                rect.sizeDelta = Vector2.one * card.Face.rect.width * .24f;
                var graphic = root.GetComponent<SelectionFrameGraphic>();
                graphic.Shape = SelectionFrameGraphic.FrameShape.Check;
                graphic.color = new Color(.68f, .16f, .16f);
                graphic.Border = new Color(1, .86f, .63f);
                graphic.raycastTarget = false;
                mark = rect;
            }
            if (mark != null)
            {
                mark.gameObject.SetActive(selected);
            }
        }
    }
}
