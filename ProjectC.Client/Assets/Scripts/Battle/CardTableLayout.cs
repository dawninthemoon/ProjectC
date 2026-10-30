using System.Collections.Generic;
using UnityEngine;
namespace ProjectC.Client
{
    public sealed class CardTableLayout : MonoBehaviour
    {
        public bool Hand, Compact;
        public BattleVisualSettings Settings;
        List<CardWidget> cards;
        public float Spacing { get; private set; }
        public void Arrange(List<CardWidget> widgets) { cards=widgets; Apply(1); }
        public void Snap() { Apply(1); }
        void LateUpdate() { Apply(1-Mathf.Exp(-Time.unscaledDeltaTime*18)); }
        void Apply(float factor)
        {
            if(cards==null) return;
            var area=(RectTransform)transform; int count=0; foreach(var c in cards) if(c.gameObject.activeSelf) ++count;
            float width=Compact?68:180, height=Compact?88:218;
            Spacing=count<2?0:Mathf.Min(width+(Hand?22:35),Mathf.Max(12,(area.rect.width-width-16)/(count-1)));
            int i=0;
            foreach(var card in cards)
            {
                if(!card.gameObject.activeSelf) continue;
                var rect=(RectTransform)card.transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.sizeDelta=new Vector2(width,height);
                float offset=i++-(count-1)*.5f, ratio=count>1?offset/((count-1)*.5f):0;
                rect.anchoredPosition=Vector2.Lerp(rect.anchoredPosition,new Vector2(offset*Spacing,Hand?-Mathf.Abs(ratio)*15:0),factor);
                rect.localRotation=Quaternion.Slerp(rect.localRotation,Quaternion.Euler(0,0,Hand?-ratio*5:0),factor);
            }
        }
    }
}
