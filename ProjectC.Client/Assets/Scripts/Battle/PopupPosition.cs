using UnityEngine;
namespace ProjectC.Client
{
    public static class PopupPosition
    {
        public static void Place(RectTransform panel,Vector3 world)
        {
            var parent=(RectTransform)panel.parent; var point=parent.InverseTransformPoint(world); var bounds=parent.rect;
            // All popup roots are top-left pivoted, inside the authored 1920x1080 safe table.
            point.x=Mathf.Clamp(point.x,bounds.xMin+12,bounds.xMax-panel.rect.width-12);
            point.y=Mathf.Clamp(point.y,bounds.yMin+panel.rect.height+12,bounds.yMax-12);
            panel.localPosition=new Vector3(point.x,point.y,0);
        }
    }
}
