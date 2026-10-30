using Game.Utils;
using UnityEngine;
namespace ProjectC.Client
{
    public sealed class AttackArrowView : MonoBehaviour
    {
        RectTransform shaft, tip;
        public void Show(Vector3 from,Vector3 to)
        {
            if(shaft==null) { this.Bind("Shaft",out shaft); this.Bind("Tip",out tip); }
            gameObject.SetActive(true);
            var parent=(RectTransform)transform.parent; Vector3 a=parent.InverseTransformPoint(from), b=parent.InverseTransformPoint(to), delta=b-a;
            transform.localPosition=a; transform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
            shaft.sizeDelta=new Vector2(Mathf.Max(0,delta.magnitude-12),7); tip.anchoredPosition=new Vector2(delta.magnitude,0);
        }
    }
}
