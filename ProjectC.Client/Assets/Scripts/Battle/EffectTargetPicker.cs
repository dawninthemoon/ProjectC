using System;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class EffectTargetPicker : MonoBehaviour
    {
        public Button CandidatePrefab;
        readonly List<Button> buttons=new List<Button>();
        EffectSelectionView selection;
        Text title;
        public void Show(EffectSelectionView value,int index,Func<EffectTargetView,string> name,Action<EffectTargetView> pick,Action confirm,Action cancel)
        {
            selection=value; this.Bind("Title",out title); this.Bind("Candidates",out RectTransform content);
            this.Bind("Confirm",out Button ok); this.Bind("Cancel",out Button back);
            title.text=$"EFFECT TARGETS / {index+1}\nChoose {value.MinCount}..{value.MaxCount}";
            foreach(var b in buttons) { b.gameObject.SetActive(false); Destroy(b.gameObject); } buttons.Clear();
            foreach(var target in value.Candidates)
            {
                var button=Instantiate(CandidatePrefab,content); buttons.Add(button); button.GetComponentInChildren<Text>().text=name(target);
                button.onClick.AddListener(()=>pick(target));
            }
            ok.onClick.RemoveAllListeners(); ok.onClick.AddListener(()=>confirm()); back.onClick.RemoveAllListeners(); back.onClick.AddListener(()=>cancel());
            gameObject.SetActive(true);
        }
        public void SetSelected(List<EffectTargetView> selected)
        {
            this.Bind("Confirm",out Button ok); ok.interactable=selected.Count>=selection.MinCount && selected.Count<=selection.MaxCount;
            for(int i=0;i<buttons.Count;++i)
            {
                var target=selection.Candidates[i]; bool chosen=selected.Exists(t=>t.PlayerId==target.PlayerId && t.CardId==target.CardId && t.Zone==target.Zone);
                var label=buttons[i].GetComponentInChildren<Text>(); string text=label.text.TrimStart('✓',' '); label.text=(chosen?"✓ ":"")+text;
                buttons[i].GetComponent<Image>().color=chosen?new Color(.23f,.46f,.36f):new Color(.1f,.18f,.24f);
            }
        }
    }
}
