using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class BattleLogView : MonoBehaviour
    {
        public Text Recent, Full, Feedback;
        public GameObject History;
        public BattleVisualSettings Settings;
        public bool IsOpen { get; private set; }
        public ScrollRect Scroll { get; private set; }
        readonly Queue<string> lines=new Queue<string>();
        CanvasGroup group; RectTransform panel; Text unread;
        int unreadCount;float feedbackUntil;
        bool bound;
        void BindReferences()
        {
            if(bound) return;bound=true;
            transform.parent.Bind("FullHistory",out panel);History=panel.gameObject;
            panel.Bind("Lines",out Full);panel.Bind("CloseLog",out Button close);close.onClick.AddListener(Close);
            transform.parent.Bind("RejectionReason",out Feedback);transform.parent.Bind("Unread",out unread);
            group=panel.GetComponent<CanvasGroup>();Scroll=panel.GetComponent<ScrollRect>();
        }
        public void Add(string line)
        {
            if(line=="Connected") ClearFeedback();
            BindReferences();bool follow=Scroll.verticalNormalizedPosition<=.035f || Full.preferredHeight<Scroll.viewport.rect.height;
            Vector2 old=Scroll.content.anchoredPosition;float oldHeight=Full.preferredHeight;
            lines.Enqueue(line);while(lines.Count>200) lines.Dequeue();Full.text=string.Join("\n",lines);
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
            if(follow) Scroll.verticalNormalizedPosition=0;else Scroll.content.anchoredPosition=old;
            if(!IsOpen) { ++unreadCount;unread.text=unreadCount>99?"99+":unreadCount.ToString(); }
            if(line.StartsWith("Rejected:") || line.StartsWith("Protocol error") || line.StartsWith("Disconnected")) ShowFeedback(line);
        }
        public void ShowFeedback(string value) { BindReferences();Feedback.text=value;feedbackUntil=Time.unscaledTime+3; }
        public void ClearFeedback() { BindReferences();Feedback.text=""; }
        public void Toggle() { BindReferences();IsOpen=!IsOpen;if(IsOpen) { unreadCount=0;unread.text=""; } group.blocksRaycasts=group.interactable=IsOpen; }
        public void Close() { if(IsOpen) Toggle(); }
        void Update()
        {
            BindReferences();float duration=Settings!=null?Settings.DrawerDuration:.25f;
            panel.anchoredPosition=new Vector2(Mathf.MoveTowards(panel.anchoredPosition.x,IsOpen?20:-500,520*Time.unscaledDeltaTime/duration),-150);
            group.alpha=Mathf.InverseLerp(-500,20,panel.anchoredPosition.x);
            if(Time.unscaledTime>feedbackUntil) Feedback.text="";
        }
    }
}
