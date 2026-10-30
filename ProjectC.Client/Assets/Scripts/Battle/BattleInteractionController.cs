using System.Collections;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public enum BattleInputState { Idle, DraggingHandCard, DraggingAttackTarget, SelectingCardAction, SelectingEffectTarget, AwaitingServerResponse }
    public sealed class BattleInteractionController : MonoBehaviour
    {
        public CardWidget GhostPrefab;
        public BattleVisualSettings Settings;
        public BattleInputState State { get; private set; }
        public CardWidget Ghost => ghost;
        public AttackArrowView Arrow => arrow;
        BattlePresenter presenter;
        RectTransform visualRoot;
        AttackArrowView arrow;
        ContextActionView context;
        EffectTargetPicker picker;
        CardWidget source, ghost;
        Vector3 attackOrigin;
        int sourceId, selectionIndex;
        ActivationView activation;
        readonly List<List<EffectTargetView>> targets = new List<List<EffectTargetView>>();
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        Coroutine rollback;
        public void Initialize(BattlePresenter value)
        {
            presenter=value;
            this.Bind("DragVisualRoot",out visualRoot); this.Bind("AttackArrow",out arrow);
            this.Bind("ContextAction",out context); this.Bind("EffectTargetPicker",out picker);
            foreach(var area in GetComponentsInChildren<BattleHitArea>(true)) area.Controller=this;
            presenter.Client.CommandFinished+=Finished; presenter.Client.ViewInvalidated+=ResetInput;
            ResetInput();
        }
        void OnDestroy()
        { if(presenter!=null) { presenter.Client.CommandFinished-=Finished; presenter.Client.ViewInvalidated-=ResetInput; } }
        void Update() { if(Keyboard.current?.escapeKey.wasPressedThisFrame==true && State!=BattleInputState.AwaitingServerResponse) ResetInput(); }
        public void Attach(CardWidget widget)
        { widget.DragStarted=Begin; widget.DragMoved=Drag; widget.DragEnded=End; }
        public void SnapshotChanged()
        { if(State!=BattleInputState.AwaitingServerResponse) ResetInput(); }
        public void ResetInput()
        {
            if(rollback!=null) StopCoroutine(rollback); rollback=null;
            if(source!=null) source.ResetPointer(); source=null;sourceId=0;
            if(ghost!=null) Destroy(ghost.gameObject);ghost=null;
            if(arrow!=null) arrow.gameObject.SetActive(false);
            if(context!=null) context.gameObject.SetActive(false);
            if(picker!=null) picker.gameObject.SetActive(false);
            targets.Clear();activation=null;State=BattleInputState.Idle;
            if(presenter!=null) { presenter.ClearSelection();Hints(); }
        }
        public void Click(CardWidget widget)
        {
            if(State==BattleInputState.AwaitingServerResponse || widget.Card==null) return;
            if(State==BattleInputState.SelectingEffectTarget)
            {
                foreach(var target in activation.Selections[selectionIndex].Candidates)
                    if(target.CardId==widget.Card.Id) { Pick(target);return; }
                presenter.Log.ShowFeedback("That card is not an eligible target.");return;
            }
            ResetInput();presenter.SelectCard(widget);
            if(presenter.IsOwnField(widget.Card.Id) && presenter.CanAct)
            { source=widget;sourceId=widget.Card.Id;State=BattleInputState.SelectingCardAction;context.Show(widget,SelectAction); }
        }
        public void ClickArea(BattleHitArea area)
        {
            if(State==BattleInputState.AwaitingServerResponse) return;
            if(State==BattleInputState.SelectingEffectTarget && (area.Kind==BattleHitArea.AreaKind.OwnHero || area.Kind==BattleHitArea.AreaKind.EnemyHero))
            {
                int id=area.Kind==BattleHitArea.AreaKind.OwnHero?presenter.Client.PlayerId:presenter.EnemyId;
                foreach(var target in activation.Selections[selectionIndex].Candidates) if(target.CardId==0 && target.PlayerId==id) { Pick(target);return; }
                presenter.Log.ShowFeedback("That player is not an eligible target.");return;
            }
            ResetInput();
        }
        void Begin(CardWidget widget,PointerEventData pointer)
        {
            if(State==BattleInputState.AwaitingServerResponse || !presenter.CanAct) return;
            bool summon=widget.InHand && widget.Card.CanSummon;
            bool attack=presenter.IsOwnField(widget.Card.Id) && widget.Card.CanAttack;
            if(!summon && !attack) { presenter.Log.ShowFeedback("This card cannot act now. Check turn, phase and summoning sickness.");return; }
            ResetInput();source=widget;sourceId=widget.Card.Id;presenter.Detail.Show(null);
            State=summon?BattleInputState.DraggingHandCard:BattleInputState.DraggingAttackTarget;
            if(summon)
            {
                ghost=Instantiate(GhostPrefab,visualRoot);ghost.name="Dragged card";
                ghost.GetComponent<CanvasGroup>().blocksRaycasts=false;
                foreach(var g in ghost.GetComponentsInChildren<Graphic>(true)) g.raycastTarget=false;
                ghost.Bind(widget.Card,false,false,false,false);((RectTransform)ghost.transform).sizeDelta=new Vector2(180,218);
                source.Dim(true);
            }
            attackOrigin=widget.WorldCenter;Hints();Drag(widget,pointer);
        }
        Vector3 World(PointerEventData pointer)
        { RectTransformUtility.ScreenPointToWorldPointInRectangle(visualRoot,pointer.position,pointer.pressEventCamera,out var point);return point; }
        GameObject Hit(PointerEventData pointer)
        { hits.Clear();EventSystem.current?.RaycastAll(pointer,hits);return hits.Count==0?null:hits[0].gameObject; }
        void Drag(CardWidget widget,PointerEventData pointer)
        {
            if(State==BattleInputState.DraggingHandCard && ghost!=null) ghost.transform.position=World(pointer);
            if(State==BattleInputState.DraggingAttackTarget) arrow.Show(attackOrigin,World(pointer));
            if(State==BattleInputState.DraggingAttackTarget || State==BattleInputState.DraggingHandCard)
            { Hints();var target=Hit(pointer)?.GetComponentInParent<CardWidget>();if(target!=null && presenter.IsEnemyField(target.Card?.Id??0) && State==BattleInputState.DraggingAttackTarget) target.SetHint(TargetHint.Focused); }
        }
        void End(CardWidget widget,PointerEventData pointer)
        {
            if(State!=BattleInputState.DraggingHandCard && State!=BattleInputState.DraggingAttackTarget) return;
            var hit=Hit(pointer);var card=hit?.GetComponentInParent<CardWidget>();var area=hit?.GetComponentInParent<BattleHitArea>();
            string command=null;int target=0;
            if(presenter.CanAct && State==BattleInputState.DraggingHandCard && (area?.Kind==BattleHitArea.AreaKind.OwnField || presenter.IsOwnField(card?.Card?.Id??0))) command="PlayCard";
            if(presenter.CanAct && State==BattleInputState.DraggingAttackTarget)
            {
                if(presenter.IsEnemyField(card?.Card?.Id??0)) { command="Attack";target=card.Card.Id; }
                else if(area?.Kind==BattleHitArea.AreaKind.EnemyHero && presenter.EnemyFieldEmpty) command="DirectAttack";
            }
            if(command!=null && presenter.Client.SendGameCommand(command,sourceId,target))
            { State=BattleInputState.AwaitingServerResponse;arrow.gameObject.SetActive(false);Hints(); }
            else { presenter.Log.ShowFeedback("Cancelled: drop on a highlighted target.");ReturnCard(); }
        }
        void Finished(bool accepted,string reason)
        {
            if(State!=BattleInputState.AwaitingServerResponse) return;
            if(accepted) ResetInput();else { presenter.Log.ShowFeedback(reason);ReturnCard(); }
        }
        void ReturnCard()
        { if(ghost!=null && source!=null && source.gameObject.activeInHierarchy) { State=BattleInputState.Idle;rollback=StartCoroutine(ReturnAnimation()); } else ResetInput(); }
        IEnumerator ReturnAnimation()
        {
            var from=ghost.transform.position;var to=source.WorldCenter;float duration=Settings!=null?Settings.Rollback:.22f;
            for(float t=0;t<duration;t+=Time.unscaledDeltaTime) { if(ghost==null) yield break;ghost.transform.position=Vector3.Lerp(from,to,Mathf.SmoothStep(0,1,t/duration));yield return null; }
            rollback=null;ResetInput();
        }
        void SelectAction(ActivationView value)
        {
            if(!presenter.CanAct || !value.CanActivate) return;
            activation=value;selectionIndex=0;targets.Clear();context.gameObject.SetActive(false);presenter.Detail.Show(null);
            foreach(var selection in value.Selections) targets.Add(new List<EffectTargetView>());
            // Only server-enumerated, single-source choices may be implicit for a manifestation.
            bool self=value.IsManifest;
            foreach(var selection in value.Selections) self &= selection.MinCount==1 && selection.MaxCount==1 && selection.Candidates.Length==1 && selection.Candidates[0].CardId==sourceId;
            if(self) { for(int i=0;i<targets.Count;++i) targets[i].Add(value.Selections[i].Candidates[0]);SendEffect();return; }
            if(targets.Count==0) { SendEffect();return; }
            State=BattleInputState.SelectingEffectTarget;ShowTargets();
        }
        void ShowTargets()
        {
            picker.Show(activation.Selections[selectionIndex],selectionIndex,TargetName,Pick,Confirm,ResetInput);
            picker.SetSelected(targets[selectionIndex]);Hints();
        }
        string TargetName(EffectTargetView target) => target.CardId==0?"Player "+target.PlayerId:presenter.FindCard(target.CardId)?.Name+" #"+target.CardId+" / "+(target.Zone==1?"Hand":target.Zone==2?"Field":"Graveyard");
        void Pick(EffectTargetView target)
        {
            var chosen=targets[selectionIndex];int index=chosen.FindIndex(t=>t.CardId==target.CardId && t.PlayerId==target.PlayerId && t.Zone==target.Zone);
            if(index>=0) chosen.RemoveAt(index);
            else if(chosen.Count<activation.Selections[selectionIndex].MaxCount) chosen.Add(target);
            else presenter.Log.ShowFeedback("Maximum targets selected. Click a selected target to remove it.");
            picker.SetSelected(chosen);Hints();
        }
        void Confirm()
        {
            var rule=activation.Selections[selectionIndex];int count=targets[selectionIndex].Count;
            if(count<rule.MinCount || count>rule.MaxCount) return;
            if(++selectionIndex<targets.Count) ShowTargets();else SendEffect();
        }
        void SendEffect()
        {
            picker.gameObject.SetActive(false);
            if(presenter.Client.SendEffect(sourceId,activation.EffectId,targets)) { State=BattleInputState.AwaitingServerResponse;Hints(); }
            else ResetInput();
        }
        void Hints()
        {
            foreach(var zone in presenter.Zones) foreach(var widget in zone.Widgets)
            {
                var hint=TargetHint.None;
                if(State==BattleInputState.DraggingAttackTarget && widget.Card?.Id!=sourceId) hint=presenter.IsEnemyField(widget.Card?.Id??0)?TargetHint.Available:TargetHint.Unavailable;
                if(State==BattleInputState.SelectingEffectTarget && widget.Card!=null)
                {
                    hint=TargetHint.Unavailable;
                    foreach(var candidate in activation.Selections[selectionIndex].Candidates) if(candidate.CardId==widget.Card.Id) hint=targets[selectionIndex].Exists(t=>t.CardId==candidate.CardId)?TargetHint.Focused:TargetHint.Available;
                }
                widget.SetHint(hint);
            }
            foreach(var area in GetComponentsInChildren<BattleHitArea>(true))
            {
                if(area.Kind==BattleHitArea.AreaKind.Background) continue;
                bool valid=State==BattleInputState.DraggingHandCard && area.Kind==BattleHitArea.AreaKind.OwnField || State==BattleInputState.DraggingAttackTarget && area.Kind==BattleHitArea.AreaKind.EnemyHero && presenter.EnemyFieldEmpty;
                if(State==BattleInputState.SelectingEffectTarget) foreach(var t in activation.Selections[selectionIndex].Candidates)
                    if(t.CardId==0 && (area.Kind==BattleHitArea.AreaKind.OwnHero && t.PlayerId==presenter.Client.PlayerId || area.Kind==BattleHitArea.AreaKind.EnemyHero && t.PlayerId==presenter.EnemyId)) valid=true;
                area.GetComponent<Image>().color=valid?new Color(.12f,.38f,.32f):new Color(.055f,.115f,.145f);
            }
        }
    }
}

