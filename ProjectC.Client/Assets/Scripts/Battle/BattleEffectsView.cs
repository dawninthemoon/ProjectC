using System.Collections;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class BattleEffectsView : MonoBehaviour
    {
        public CanvasGroup TurnGroup, PopupGroup, DrawGroup;
        public Text TurnTitle, TurnSubtitle, PopupTitle, PopupBody, DrawLabel;
        public RectTransform DrawCard, OwnDeck, OwnHand, OpponentDeck, OpponentHand;
        public BattleVisualSettings Settings;
        readonly Queue<IEnumerator> queue=new Queue<IEnumerator>();
        int turnGeneration;
        Coroutine routine;CanvasGroup dim;CardWidget drawFace;bool bound;
        public int DrawsPresented { get; private set; }
        public int PopupsPresented { get; private set; }
        void BindReferences()
        {
            if(bound) return;bound=true;
            this.Bind("TurnBanner",out TurnGroup);this.Bind("CardEventPopup",out PopupGroup);this.Bind("DrawCard",out DrawGroup);
            TurnGroup.transform.Bind("Title",out TurnTitle);TurnGroup.transform.Bind("Subtitle",out TurnSubtitle);
            PopupGroup.transform.Bind("Title",out PopupTitle);PopupGroup.transform.Bind("Description",out PopupBody);
            DrawGroup.transform.Bind("Back",out DrawLabel);DrawGroup.transform.Bind("DrawFace",out drawFace);DrawCard=(RectTransform)DrawGroup.transform;
            this.Bind("YourDeck",out OwnDeck);this.Bind("YourHand",out OwnHand);this.Bind("OpponentDeck",out OpponentDeck);this.Bind("OpponentHand",out OpponentHand);this.Bind("TurnDim",out dim);
            foreach(var group in new[]{TurnGroup,PopupGroup,DrawGroup,dim}) group.blocksRaycasts=group.interactable=false;
        }
        public void ResetFeedback()
        {
            BindReferences();++turnGeneration;StopAllCoroutines();queue.Clear();routine=null;
            TurnGroup.alpha=PopupGroup.alpha=DrawGroup.alpha=dim.alpha=0;
        }
        void Enqueue(IEnumerator effect) { if(queue.Count<128) queue.Enqueue(effect);if(routine==null) routine=StartCoroutine(Play()); }
        IEnumerator Play() { while(queue.Count>0) yield return queue.Dequeue();routine=null; }
        public void Turn(bool own,int number) { Enqueue(TurnAnimation(own,number,++turnGeneration)); }
        public void Phase(string name) { Enqueue(PopupAnimation("PHASE",name)); }
        public void Popup(string title,string body) { Enqueue(PopupAnimation(title,body)); }
        public void Draw(bool own,CardView card=null) { Enqueue(DrawAnimation(own,card)); }
        IEnumerator TurnAnimation(bool own,int number,int generation)
        {
            if(generation!=turnGeneration) yield break;
            TurnTitle.text=own?"YOUR TURN":"OPPONENT TURN";TurnSubtitle.text="TURN "+number;
            yield return Flash(TurnGroup,(Settings?.TurnFadeIn??.2f)+(Settings?.TurnHold??.7f)+(Settings?.TurnFadeOut??.3f),dim,generation);
        }
        IEnumerator PopupAnimation(string title,string body)
        { PopupTitle.text=title;PopupBody.text=body;++PopupsPresented;yield return Flash(PopupGroup,Settings?.PopupDuration??1.35f); }
        IEnumerator DrawAnimation(bool own,CardView card)
        {
            ++DrawsPresented;DrawLabel.text=own?"DRAW":"HIDDEN";
            drawFace.gameObject.SetActive(own && card!=null);if(own && card!=null) drawFace.Bind(card,false,false,false,false);
            var from=own?OwnDeck:OpponentDeck;var to=own?OwnHand:OpponentHand;
            Vector3 a=from.TransformPoint(from.rect.center), b=to.TransformPoint(to.rect.center);
            float duration=Settings?.DrawDuration??.45f;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime)
            {
                float u=Mathf.SmoothStep(0,1,t/duration);DrawGroup.alpha=1;
                DrawCard.position=Vector3.Lerp(a,b,u)+Vector3.up*(Mathf.Sin(u*Mathf.PI)*60);
                DrawCard.localRotation=Quaternion.Euler(0,0,Mathf.Lerp(-18,0,u));DrawCard.localScale=Vector3.one*(.65f+.35f*Mathf.Sin(u*Mathf.PI));yield return null;
            }
            DrawGroup.alpha=0;DrawCard.localRotation=Quaternion.identity;DrawCard.localScale=Vector3.one;
        }
        IEnumerator Flash(CanvasGroup group,float duration,CanvasGroup shade=null,int generation=-1)
        {
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime)
            { if(generation>=0 && generation!=turnGeneration) break; group.alpha=Mathf.Min(t/(Settings?.TurnFadeIn??.2f),1,(duration-t)/(Settings?.TurnFadeOut??.3f));if(shade!=null) shade.alpha=group.alpha;yield return null; }
            group.alpha=0;if(shade!=null) shade.alpha=0;
        }
    }
}
