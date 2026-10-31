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
        ActionChoiceView serverChoice;
        public void ShowServer(ActionChoiceView value,Action<string> pick,Action confirm,Action cancel)
        {
            serverChoice=value;this.Bind("Title",out title);this.Bind("Candidates",out RectTransform content);
            this.Bind("Confirm",out Button ok);this.Bind("Cancel",out Button back);
            title.text=(value.IsCost?"COST TARGETS / will be paid":"EFFECT TARGETS")+$"\nChoose {value.MinCount}..{value.MaxCount}";
            title.color=value.IsCost?new Color(1,.7f,.4f):new Color(.4f,.95f,.83f);
            foreach(var button in buttons) { button.gameObject.SetActive(false);Destroy(button.gameObject); }buttons.Clear();
            foreach(var option in value.Candidates)
            {
                var button=Instantiate(CandidatePrefab,content);buttons.Add(button);
                button.GetComponentInChildren<Text>().text=option.Card?.Name??"Player "+option.PlayerId;
                button.onClick.AddListener(()=>pick(option.Token));
            }
            ok.onClick.RemoveAllListeners();ok.onClick.AddListener(()=>confirm());
            back.onClick.RemoveAllListeners();back.interactable=value.CanCancel;back.onClick.AddListener(()=>cancel());back.GetComponentInChildren<Text>().text=value.CanCancel?"CANCEL":"RESOLVE TO CONTINUE";
            gameObject.SetActive(true);
        }
        public void SetServerSelected(List<string> selected)
        {
            this.Bind("Confirm",out Button ok);ok.interactable=selected.Count>=serverChoice.MinCount && selected.Count<=serverChoice.MaxCount;
            for(int i=0;i<buttons.Count;++i)
            {
                bool chosen=selected.Contains(serverChoice.Candidates[i].Token);var label=buttons[i].GetComponentInChildren<Text>();
                label.text=(chosen?"✓ ":"")+(serverChoice.Candidates[i].Card?.Name??"Player");
                buttons[i].GetComponent<Image>().color=chosen?new Color(.23f,.46f,.36f):new Color(.1f,.18f,.24f);
            }
        }
        public void Show(EffectSelectionView value,int index,Func<EffectTargetView,string> name,Action<EffectTargetView> pick,Action confirm,Action cancel)
        {
            selection=value; serverChoice=null;this.Bind("Title",out title); this.Bind("Candidates",out RectTransform content);
            this.Bind("Confirm",out Button ok); this.Bind("Cancel",out Button back);
            back.interactable=true;back.GetComponentInChildren<Text>().text="CANCEL";title.color=Color.white;
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
