using System.Collections;
using System.Collections.Generic;
using Game.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public enum BattleInputState
    {
        Idle,
        DraggingHandCard,
        DraggingAttackTarget,
        SelectingCardAction,
        SelectingEffectTarget,
        AwaitingServerResponse,
    }

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
        CardWidget source,
            ghost;
        Vector3 attackOrigin;
        int sourceId,
            selectionIndex;
        ActivationView activation;
        ActionChoiceView serverChoice;
        readonly List<string> serverTargets = new List<string>();
        readonly List<List<EffectTargetView>> targets = new List<List<EffectTargetView>>();
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        Coroutine rollback;
        private BattleHitArea[] _areas;
        private BattleHitArea _dropArea;
        private const float MinimumDropOverlap = .30f;

        public void Initialize(BattlePresenter value)
        {
            presenter = value;
            this.Bind("DragVisualRoot", out visualRoot);
            this.Bind("AttackArrow", out arrow);
            this.Bind("ContextAction", out context);
            this.Bind("EffectTargetPicker", out picker);
            _areas = GetComponentsInChildren<BattleHitArea>(true);
            foreach (var area in _areas)
            {
                area.Controller = this;
            }
            presenter.Client.CommandFinished += Finished;
            presenter.Client.ViewInvalidated += ResetInput;
            ResetInput();
        }

        void OnDestroy()
        {
            if (presenter != null)
            {
                presenter.Client.CommandFinished -= Finished;
                presenter.Client.ViewInvalidated -= ResetInput;
            }
        }

        void Update()
        {
            if (
                Keyboard.current?.escapeKey.wasPressedThisFrame == true
                && State != BattleInputState.AwaitingServerResponse
            )
            {
                CancelSelection();
            }
        }

        public void Attach(CardWidget widget)
        {
            widget.DragStarted = Begin;
            widget.DragMoved = Drag;
            widget.DragEnded = End;
        }

        public void SnapshotChanged()
        {
            if (State != BattleInputState.AwaitingServerResponse)
            {
                ResetInput();
            }
        }

        public void ResetInput()
        {
            if (rollback != null)
            {
                StopCoroutine(rollback);
            }
            rollback = null;
            if (source != null)
            {
                source.ResetPointer();
            }
            _dropArea = null;
            source = null;
            sourceId = 0;
            if (ghost != null)
            {
                Destroy(ghost.gameObject);
            }
            ghost = null;
            if (arrow != null)
            {
                arrow.gameObject.SetActive(false);
            }
            if (context != null)
            {
                context.gameObject.SetActive(false);
            }
            if (picker != null)
            {
                picker.gameObject.SetActive(false);
            }
            targets.Clear();
            activation = null;
            serverChoice = null;
            serverTargets.Clear();
            State = BattleInputState.Idle;
            if (presenter != null)
            {
                presenter.ClearSelection();
                presenter.SetHandExpanded(false);
                Hints();
            }
        }

        public void Click(CardWidget widget)
        {
            presenter.InspectCard(widget.Card);
            if (State == BattleInputState.AwaitingServerResponse || widget.Card == null)
            {
                return;
            }
            if (State == BattleInputState.SelectingEffectTarget)
            {
                if (serverChoice != null)
                {
                    foreach (var option in serverChoice.Candidates)
                    {
                        if (option.CardId == widget.Card.Id && option.Zone != 0)
                        {
                            PickServer(option.Token);
                            return;
                        }
                    }
                    presenter.Log.ShowFeedback(BattleText.Get("ui.choice.invalid"));
                    return;
                }
                foreach (var target in activation.Selections[selectionIndex].Candidates)
                {
                    if (target.CardId == widget.Card.Id)
                    {
                        Pick(target);
                        return;
                    }
                }
                presenter.Log.ShowFeedback(BattleText.Get("ui.choice.invalid"));
                return;
            }
            ResetInput();
            presenter.SelectCard(widget);
            presenter.SetHandExpanded(widget.InHand);
            if ((presenter.IsOwnField(widget.Card.Id) || widget.InHand) && presenter.CanAct)
            {
                source = widget;
                sourceId = widget.Card.Id;
                State = BattleInputState.SelectingCardAction;
                context.Show(widget, SelectAction);
            }
        }

        public void FocusHand(CardWidget widget)
        {
            if (!widget.InHand || presenter.Hand.Layout.Compact
                || (State != BattleInputState.Idle && State != BattleInputState.SelectingCardAction))
            {
                return;
            }
            // Browsing the expanded hand also moves selection on the opponent's turn.
            // Only action submission and its context controls depend on CanAct.
            presenter.SelectCard(widget);
            if (!presenter.CanAct)
            {
                context.gameObject.SetActive(false);
                return;
            }
            source = widget;
            sourceId = widget.Card.Id;
            State = BattleInputState.SelectingCardAction;
            context.Show(widget, SelectAction);
        }

        public void ClickArea(BattleHitArea area)
        {
            if (area.Kind == BattleHitArea.AreaKind.Background || area.Kind == BattleHitArea.AreaKind.OwnField)
            {
                if (State == BattleInputState.SelectingEffectTarget || State == BattleInputState.AwaitingServerResponse)
                {
                    return;
                }
                ResetInput();
                presenter.HideCardInfo();
                return;
            }
            if (State == BattleInputState.AwaitingServerResponse)
            {
                return;
            }
            if (serverChoice != null && State == BattleInputState.SelectingEffectTarget)
            {
                int player =
                    area.Kind == BattleHitArea.AreaKind.OwnHero ? presenter.Client.PlayerId
                    : area.Kind == BattleHitArea.AreaKind.EnemyHero ? presenter.EnemyId
                    : 0;
                foreach (var option in serverChoice.Candidates)
                {
                    if (option.CardId == 0 && option.Zone == 2 && option.PlayerId == player)
                    {
                        PickServer(option.Token);
                        return;
                    }
                }
                return;
            }
            if (
                State == BattleInputState.SelectingEffectTarget
                && (
                    area.Kind == BattleHitArea.AreaKind.OwnHero
                    || area.Kind == BattleHitArea.AreaKind.EnemyHero
                )
            )
            {
                int id =
                    area.Kind == BattleHitArea.AreaKind.OwnHero
                        ? presenter.Client.PlayerId
                        : presenter.EnemyId;
                foreach (var target in activation.Selections[selectionIndex].Candidates)
                {
                    if (target.CardId == 0 && target.PlayerId == id)
                    {
                        Pick(target);
                        return;
                    }
                }
                presenter.Log.ShowFeedback(BattleText.Get("ui.choice.invalid"));
                return;
            }
            ResetInput();
        }

        void Begin(CardWidget widget, PointerEventData pointer)
        {
            if (State == BattleInputState.AwaitingServerResponse || State == BattleInputState.SelectingEffectTarget || !presenter.CanAct)
            {
                return;
            }
            bool summon =
                widget.InHand
                && (
                    widget.Card.CanSummon
                    || widget.Card.Activations != null
                        && System.Array.Exists(widget.Card.Activations, a => a.CanActivate)
                );
            bool attack = presenter.IsOwnField(widget.Card.Id) && widget.Card.CanAttack;
            if (!summon && !attack)
            {
                presenter.Log.ShowFeedback(
                    BattleText.Get("ui.card.cannot_act")
                );
                return;
            }
            ResetInput();
            source = widget;
            sourceId = widget.Card.Id;
            State = summon
                ? BattleInputState.DraggingHandCard
                : BattleInputState.DraggingAttackTarget;
            if (summon)
            {
                ghost = Instantiate(GhostPrefab, visualRoot);
                ghost.name = "Dragged card";
                ghost.GetComponent<CanvasGroup>().blocksRaycasts = false;
                foreach (var g in ghost.GetComponentsInChildren<Graphic>(true))
                {
                    g.raycastTarget = false;
                }
                ghost.Bind(widget.Card, false, false, false, false);
                source.Dim(true);
            }
            attackOrigin = widget.WorldCenter;
            Hints();
            Drag(widget, pointer);
        }

        Vector3 World(PointerEventData pointer)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                visualRoot,
                pointer.position,
                pointer.pressEventCamera,
                out var point
            );
            return point;
        }

        GameObject Hit(PointerEventData pointer)
        {
            hits.Clear();
            EventSystem.current?.RaycastAll(pointer, hits);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }

        void Drag(CardWidget widget, PointerEventData pointer)
        {
            if (State == BattleInputState.DraggingHandCard && ghost != null)
            {
                ghost.transform.position = World(pointer);
                UpdateDropArea();
            }
            if (State == BattleInputState.DraggingAttackTarget)
            {
                arrow.Show(attackOrigin, World(pointer));
            }
            if (
                State == BattleInputState.DraggingAttackTarget
                || State == BattleInputState.DraggingHandCard
            )
            {
                Hints();
                var target = Hit(pointer)?.GetComponentInParent<CardWidget>();
                if (
                    target != null
                    && presenter.IsEnemyField(target.Card?.Id ?? 0)
                    && State == BattleInputState.DraggingAttackTarget
                )
                {
                    target.SetHint(TargetHint.Focused);
                }
            }
        }

        void End(CardWidget widget, PointerEventData pointer)
        {
            if (
                State != BattleInputState.DraggingHandCard
                && State != BattleInputState.DraggingAttackTarget
            )
            {
                return;
            }
            if (State == BattleInputState.DraggingHandCard)
            {
                ghost.transform.position = World(pointer);
                UpdateDropArea();
            }
            var hit = Hit(pointer);
            var card = hit?.GetComponentInParent<CardWidget>();
            var area = hit?.GetComponentInParent<BattleHitArea>();
            string command = null;
            int target = 0;
            if (
                presenter.CanAct
                && State == BattleInputState.DraggingHandCard
                && _dropArea != null
            )
            {
                command = "PlayCard";
            }
            if (presenter.CanAct && State == BattleInputState.DraggingAttackTarget)
            {
                if (presenter.IsEnemyField(card?.Card?.Id ?? 0))
                {
                    command = "Attack";
                    target = card.Card.Id;
                }
                else if (
                    area?.Kind == BattleHitArea.AreaKind.EnemyHero
                    && presenter.EnemyFieldEmpty
                    && source.Card.CanDirectAttack
                )
                {
                    command = "DirectAttack";
                }
            }
            if (command == "PlayCard" && source != null && !source.Card.CanSummon)
            {
                var action = System.Array.Find(source.Card.Activations, a => a.CanActivate);
                if (action != null)
                {
                    SelectAction(action);
                    return;
                }
            }
            if (command != null && presenter.Client.SendGameCommand(command, sourceId, target))
            {
                State = BattleInputState.AwaitingServerResponse;
                arrow.gameObject.SetActive(false);
                Hints();
            }
            else
            {
                presenter.Log.ShowFeedback(BattleText.Get("ui.drop.cancelled"));
                ReturnCard();
            }
        }

        void Finished(bool accepted, string reason)
        {
            if (State != BattleInputState.AwaitingServerResponse)
            {
                return;
            }
            if (accepted)
            {
                ResetInput();
            }
            else
            {
                presenter.Log.ShowFeedback(reason);
                ReturnCard();
            }
        }

        void ReturnCard()
        {
            if (ghost != null && source != null && source.gameObject.activeInHierarchy)
            {
                State = BattleInputState.Idle;
                rollback = StartCoroutine(ReturnAnimation());
            }
            else
            {
                ResetInput();
            }
        }

        IEnumerator ReturnAnimation()
        {
            var from = ghost.transform.position;
            var to = source.WorldCenter;
            float duration = Settings != null ? Settings.Rollback : .22f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                if (ghost == null)
                {
                    yield break;
                }
                ghost.transform.position = Vector3.Lerp(
                    from,
                    to,
                    Mathf.SmoothStep(0, 1, t / duration)
                );
                yield return null;
            }
            rollback = null;
            ResetInput();
        }

        void SelectAction(ActivationView value)
        {
            if (!presenter.CanAct || !value.CanActivate)
            {
                return;
            }
            if (value.Interactive)
            {
                context.gameObject.SetActive(false);
                if (presenter.Client.SendPlayerAction(sourceId, value.EffectId))
                {
                    State = BattleInputState.AwaitingServerResponse;
                }
                else
                {
                    ResetInput();
                }
                return;
            }
            activation = value;
            selectionIndex = 0;
            targets.Clear();
            context.gameObject.SetActive(false);
            foreach (var selection in value.Selections)
            {
                targets.Add(new List<EffectTargetView>());
            }
            // Only server-enumerated, single-source choices may be implicit for a manifestation.
            bool self = value.IsManifest;
            foreach (var selection in value.Selections)
            {
                self &=
                    selection.MinCount == 1
                    && selection.MaxCount == 1
                    && selection.Candidates.Length == 1
                    && selection.Candidates[0].CardId == sourceId;
            }
            if (self)
            {
                for (int i = 0; i < targets.Count; ++i)
                {
                    targets[i].Add(value.Selections[i].Candidates[0]);
                }
                SendEffect();
                return;
            }
            if (targets.Count == 0)
            {
                SendEffect();
                return;
            }
            State = BattleInputState.SelectingEffectTarget;
            ShowTargets();
        }

        void ShowTargets()
        {
            picker.Show(
                activation.Selections[selectionIndex],
                selectionIndex,
                TargetName,
                Pick,
                Confirm,
                ResetInput
            );
            picker.SetSelected(targets[selectionIndex]);
            Hints();
        }

        string TargetName(EffectTargetView target) => target.CardId == 0
            ? BattleText.Get("ui.choice.player", target.PlayerId)
            : CardPresentation.Name(presenter.FindCard(target.CardId));

        void Pick(EffectTargetView target)
        {
            presenter.InspectCard(presenter.FindCard(target.CardId));
            var chosen = targets[selectionIndex];
            int index = chosen.FindIndex(t =>
                t.CardId == target.CardId && t.PlayerId == target.PlayerId && t.Zone == target.Zone
            );
            if (index >= 0)
            {
                chosen.RemoveAt(index);
            }
            else if (activation.Selections[selectionIndex].MaxCount == 1)
            {
                chosen.Clear();
                chosen.Add(target);
            }
            else if (chosen.Count < activation.Selections[selectionIndex].MaxCount)
            {
                chosen.Add(target);
            }
            else
            {
                presenter.Log.ShowFeedback(
                    BattleText.Get("ui.choice.maximum")
                );
            }
            picker.SetSelected(chosen);
            Hints();
        }

        void Confirm()
        {
            var rule = activation.Selections[selectionIndex];
            int count = targets[selectionIndex].Count;
            if (count < rule.MinCount || count > rule.MaxCount)
            {
                return;
            }
            if (++selectionIndex < targets.Count)
            {
                ShowTargets();
            }
            else
            {
                SendEffect();
            }
        }

        void SendEffect()
        {
            picker.gameObject.SetActive(false);
            if (presenter.Client.SendEffect(sourceId, activation.EffectId, targets))
            {
                State = BattleInputState.AwaitingServerResponse;
                Hints();
            }
            else
            {
                ResetInput();
            }
        }

        public void RefreshServerChoice()
        {
            var pending = presenter.Client.PendingChoice;
            if (pending == null)
            {
                if (serverChoice != null && State != BattleInputState.AwaitingServerResponse)
                {
                    ResetInput();
                }
                return;
            }
            if (presenter.Client.Busy)
            {
                return;
            }
            if (
                serverChoice?.ChoiceId == pending.ChoiceId
                && State == BattleInputState.SelectingEffectTarget
            )
            {
                return;
            }
            ResetInput();
            serverChoice = pending;
            sourceId = pending.SourceCardId;
            State = BattleInputState.SelectingEffectTarget;
            picker.ShowServer(pending, PickServer, ConfirmServer, CancelSelection);
            picker.SetServerSelected(serverTargets);
            Hints();
        }

        void CancelSelection()
        {
            if (serverChoice == null)
            {
                ResetInput();
                return;
            }
            if (!serverChoice.CanCancel)
            {
                presenter.Log.ShowFeedback(
                    BattleText.Get("ui.choice.private")
                );
                return;
            }
            if (presenter.Client.SendCancelChoice())
            {
                State = BattleInputState.AwaitingServerResponse;
                picker.gameObject.SetActive(false);
            }
        }

        void PickServer(string token)
        {
            var option = System.Array.Find(serverChoice.Candidates, candidate => candidate.Token == token);
            if (option == null)
            {
                return;
            }
            // Preview only the card DTO authorized in this choice; never resolve private IDs locally.
            presenter.InspectCard(option.Card, true);
            if (serverTargets.Contains(token))
            {
                serverTargets.Remove(token);
            }
            else if (serverChoice.MaxCount == 1)
            {
                serverTargets.Clear();
                serverTargets.Add(token);
            }
            else if (serverTargets.Count < serverChoice.MaxCount)
            {
                serverTargets.Add(token);
            }
            picker.SetServerSelected(serverTargets);
            Hints();
        }

        void ConfirmServer()
        {
            if (
                serverTargets.Count < serverChoice.MinCount
                || serverTargets.Count > serverChoice.MaxCount
            )
            {
                return;
            }
            if (presenter.Client.SendChoice(serverTargets))
            {
                State = BattleInputState.AwaitingServerResponse;
                picker.gameObject.SetActive(false);
            }
        }

        private void UpdateDropArea()
        {
            _dropArea = null;
            if (ghost == null || source?.Card == null || !presenter.CanAct)
            {
                return;
            }
            bool canDrop = source.Card.CanSummon || source.Card.Activations != null
                && System.Array.Exists(source.Card.Activations, action => action.CanActivate);
            if (!canDrop)
            {
                return;
            }
            float best = MinimumDropOverlap;
            foreach (var area in _areas)
            {
                if (area.Kind != BattleHitArea.AreaKind.OwnField || !area.gameObject.activeInHierarchy)
                {
                    continue;
                }
                float overlap = ScreenRectOverlap.Fraction(ghost.Face, (RectTransform)area.transform);
                if (overlap >= best)
                {
                    best = overlap;
                    _dropArea = area;
                }
            }
        }

        void Hints()
        {
            foreach (var zone in presenter.Zones)
            {
                foreach (var widget in zone.Widgets)
                {
                    var hint = TargetHint.None;
                    if (
                        State == BattleInputState.DraggingAttackTarget
                        && widget.Card?.Id != sourceId
                    )
                    {
                        hint = presenter.IsEnemyField(widget.Card?.Id ?? 0)
                            ? TargetHint.Available
                            : TargetHint.Unavailable;
                    }
                    if (
                        State == BattleInputState.SelectingEffectTarget
                        && serverChoice == null
                        && widget.Card != null
                    )
                    {
                        hint = TargetHint.Unavailable;
                        foreach (var candidate in activation.Selections[selectionIndex].Candidates)
                        {
                            if (candidate.CardId == widget.Card.Id)
                            {
                                hint = targets[selectionIndex]
                                    .Exists(t => t.CardId == candidate.CardId)
                                    ? TargetHint.Focused
                                    : TargetHint.Available;
                            }
                        }
                    }
                    if (
                        serverChoice != null
                        && State == BattleInputState.SelectingEffectTarget
                        && widget.Card != null
                    )
                    {
                        hint = TargetHint.Unavailable;
                        foreach (var option in serverChoice.Candidates)
                        {
                            if (option.CardId == widget.Card.Id && option.Zone != 0)
                            {
                                hint = serverTargets.Contains(option.Token)
                                    ? TargetHint.Focused
                                    : TargetHint.Available;
                            }
                        }
                    }
                    widget.SetHint(hint);
                }
            }
            foreach (var area in _areas)
            {
                if (area.Kind == BattleHitArea.AreaKind.Background)
                {
                    continue;
                }
                bool valid =
                    State == BattleInputState.DraggingHandCard
                        && area == _dropArea
                    || State == BattleInputState.DraggingAttackTarget
                        && area.Kind == BattleHitArea.AreaKind.EnemyHero
                        && presenter.EnemyFieldEmpty
                        && source.Card.CanDirectAttack;
                if (State == BattleInputState.SelectingEffectTarget && serverChoice == null)
                {
                    foreach (var t in activation.Selections[selectionIndex].Candidates)
                    {
                        if (
                            t.CardId == 0
                            && (
                                area.Kind == BattleHitArea.AreaKind.OwnHero
                                    && t.PlayerId == presenter.Client.PlayerId
                                || area.Kind == BattleHitArea.AreaKind.EnemyHero
                                    && t.PlayerId == presenter.EnemyId
                            )
                        )
                        {
                            valid = true;
                        }
                    }
                }
                area.GetComponent<Image>().color = valid
                    ? new Color(.12f, .38f, .32f)
                    : new Color(.055f, .115f, .145f);
            }
        }
    }
}
