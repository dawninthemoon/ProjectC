using System;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class ContextActionView : MonoBehaviour
    {
        public Button ActionPrefab;
        readonly List<Button> buttons=new List<Button>();
        public void Show(CardWidget widget,Action<ActivationView> activate)
        {
            this.Bind("ActionContent",out RectTransform content);
            foreach(var b in buttons) { b.gameObject.SetActive(false); Destroy(b.gameObject); } buttons.Clear();
            if(widget?.Card?.Activations!=null) foreach(var action in widget.Card.Activations)
            {
                if(!action.CanActivate) continue;
                var button=Instantiate(ActionPrefab,content); buttons.Add(button);
                button.GetComponentInChildren<Text>().text=action.IsManifest?"현현":widget.Card.CardType==1?"ACTIVATE":"EFFECT #"+action.EffectId;
                if(action.YokaiCost>0) button.GetComponentInChildren<Text>().text+=" / YOKAI "+action.YokaiCost;
                button.onClick.AddListener(()=>activate(action));
            }
            gameObject.SetActive(buttons.Count>0);
            if(buttons.Count>0)
            {
                ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,buttons.Count*48+20);
                PopupPosition.Place((RectTransform)transform,widget.WorldCenter+Vector3.up*125);
            }
        }
    }
}
