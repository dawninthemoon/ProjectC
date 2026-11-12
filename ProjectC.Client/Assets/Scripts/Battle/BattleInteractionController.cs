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
        private readonly Vector3[] _heroCorners = new Vector3[4];
        private const float MinimumDropOverlap = .30f;
        private ModalBlocker.Lease _selectionModal;
        private bool _listeningInput;

        public void Initialize(BattlePresenter value)
        {
            presenter = value;
            this.Bind("DragVisualRootImage", out visualRoot);
            this.Bind("AttackArrowImage", out arrow);
            this.Bind("ContextActionImage", out context);
            this.Bind("EffectTargetPickerImage", out picker);
            _areas = GetComponentsInChildren<BattleHitArea>(true);
            foreach (var area in _areas)
            {
                area.Controller = this;
            }
            presenter.Client.CommandFinished += Finished;
            presenter.Client.ViewInvalidated += ResetInput;
            ListenInput();
            ResetInput();
        }

        private void ListenInput()
        {
            if (!_listeningInput && presenter != null)
            {
                presenter.Client.Input.Changed += PermissionsChanged;
                _listeningInput = true;
                PermissionsChanged();
            }
        }

        private void PermissionsChanged()
        {
            if (presenter.Client.Input.HasSelection)
            {
                if (_selectionModal == null)
                {
                    var gallery = presenter.GetComponentInChildren<CardGalleryView>(true);
                    _selectionModal = presenter.Modal.Acquire(this, PassThroughSelection,
                        picker != null ? (RectTransform)picker.transform : null,
                        gallery != null ? (RectTransform)gallery.transform : null,
                        presenter.Detail != null ? (RectTransform)presenter.Detail.transform : null);
                    _selectionModal.BackdropTint = Color.clear;
                }
                picker?.SetSubmitting(presenter.Client.Input.SelectionPhase != SelectionInputPhase.Selecting);
            }
            else
            {
                _selectionModal?.Dispose();
                _selectionModal = null;
                if (serverChoice != null && (presenter.Client.State?.Finished == true || !presenter.Client.Connected))
                {
                    ResetInput();
                }
            }
        }

        private bool PassThroughSelection(Vector2 point, Camera camera)
        {
            foreach (var zone in presenter.Zones)
            {
                foreach (var widget in zone.Widgets)
                {
                    int kind = widget.InHand ? (int)PresentationZone.Hand : (int)PresentationZone.Field;
                    if (widget.Card != null && widget.gameObject.activeInHierarchy
                        && presenter.Client.Input.CanSelectCard(kind, widget.Card.Id)
                        && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)widget.transform, point, camera))
                    {
                        return true;
                    }
                }
            }
            foreach (var area in _areas)
            {
                int player = area.Kind == BattleHitArea.AreaKind.OwnHero ? presenter.Client.PlayerId
                    : area.Kind == BattleHitArea.AreaKind.EnemyHero ? presenter.EnemyId : 0;
                if (player != 0 && presenter.Client.Input.CanSelectPlayer(player)
                    && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)area.transform, point, camera))
                {
                    return true;
                }
            }
            return false;
        }

        private void OnEnable() => ListenInput();

        private void OnDisable()
        {
            if (_listeningInput)
            {
                presenter.Client.Input.Changed -= PermissionsChanged;
                _listeningInput = false;
            }
            ResetInput();
            _selectionModal?.Dispose();
            _selectionModal = null;
        }

        void OnDestroy()
        {
            _selectionModal?.Dispose();
            if (presenter != null)
            {
                presenter.Client.CommandFinished -= Finished;
                presenter.Client.ViewInvalidated -= ResetInput;
                presenter.Client.Input.Changed -= PermissionsChanged;
            }
        }

        void Update()
        {
            if (presenter.Log.IsOpen)
            {
                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                {
                    presenter.Log.Close();
                }
                return;
            }
            if (Keyboard.current?.spaceKey.wasPressedThisFrame == true && presenter.PresentationBusy)
            {
                presenter.SkipPresentation();
            }
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
            presenter?.Client.Input.CancelLocalSelection();
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
            int zone = widget.InHand ? (int)PresentationZone.Hand : (int)PresentationZone.Field;
            if (!presenter.Client.Input.CanSelectCard(zone, widget.Card.Id)
                || (presenter.Client.Input.HasSelection && State != BattleInputState.SelectingEffectTarget))
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
            if (!presenter.Client.Input.CanExecute(GameplayInputAction.SelectHandCard) || presenter.Client.Input.HasSelection)
            {
                return;
            }
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
            if (presenter.Client.Input.HasSelection && area.Kind != BattleHitArea.AreaKind.OwnHero
                && area.Kind != BattleHitArea.AreaKind.EnemyHero)
            {
                return;
            }
            if (area.Kind == BattleHitArea.AreaKind.Background || area.Kind == BattleHitArea.AreaKind.OwnField)
            {
                if (State == BattleInputState.SelectingEffectTarget || State == BattleInputState.AwaitingServerResponse)
                {
                    return;
                }
                if (presenter.SelectedCardId != 0)
                {
                    ClientAudio.Play(SoundCue.CardDeselect);
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
            if (!presenter.Client.Input.CanExecute(widget.InHand ? GameplayInputAction.PlayCard : GameplayInputAction.Attack))
            {
                return;
            }
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
                var destination = CanDirectAttackAt(pointer.position)
                    ? presenter.EnemyPlayer.transform.TransformPoint(((RectTransform)presenter.EnemyPlayer.transform).rect.center)
                    : World(pointer);
                arrow.Show(attackOrigin, destination);
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
                else if (CanDirectAttackAt(pointer.position))
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
                // A destroyed/hidden local picker has no draft to retry after rejection.
                if (!accepted && activation == null)
                {
                    presenter.Client.Input.CancelLocalSelection();
                }
                return;
            }
            if (accepted)
            {
                ResetInput();
            }
            else
            {
                presenter.Log.ShowFeedback(reason);
                if (presenter.Client.Input.HasSelection)
                {
                    State = BattleInputState.SelectingEffectTarget;
                    if (serverChoice != null)
                    {
                        picker.ShowServer(serverChoice, PickServer, ConfirmServer, CancelSelection);
                        picker.SetServerSelected(serverTargets);
                    }
                    else if (activation != null)
                    {
                        selectionIndex = Mathf.Min(selectionIndex, activation.Selections.Length - 1);
                        ShowTargets();
                    }
                    return;
                }
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
            if (!presenter.CanAct || !value.CanActivate || !presenter.Client.Input.CanExecute(GameplayInputAction.ActivateEffect))
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
            presenter.Client.Input.SetLocalChoice(activation.Selections[selectionIndex]);
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
            var action = target.CardId == 0 && target.Zone == (int)PresentationZone.Field
                ? GameplayInputAction.SelectPlayer : GameplayInputCoordinator.CardSelectionAction(target.Zone);
            if (!presenter.Client.Input.CanExecute(action))
            {
                return;
            }
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
            bool selectedNow = chosen.Exists(t => t.CardId == target.CardId
                && t.PlayerId == target.PlayerId && t.Zone == target.Zone);
            if ((index >= 0) != selectedNow)
            {
                ClientAudio.Play(selectedNow ? SoundCue.CardSelect : SoundCue.CardDeselect);
            }
            picker.SetSelected(chosen);
            Hints();
        }

        void Confirm()
        {
            if (!presenter.Client.Input.CanExecute(GameplayInputAction.ConfirmSelection))
            {
                return;
            }
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
            if (presenter.Client.SendEffect(sourceId, activation.EffectId, targets))
            {
                State = BattleInputState.AwaitingServerResponse;
                picker.SetSubmitting(true);
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
                if (!ReferenceEquals(serverChoice, pending))
                {
                    serverChoice = pending;
                    serverTargets.RemoveAll(token => !System.Array.Exists(pending.Candidates, option => option.Token == token));
                    picker.ShowServer(pending, PickServer, ConfirmServer, CancelSelection);
                    picker.SetServerSelected(serverTargets);
                    Hints();
                }
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
            if (presenter.Client.Input.HasSelection && !presenter.Client.Input.CanExecute(GameplayInputAction.CancelSelection))
            {
                return;
            }
            if (serverChoice == null)
            {
                if (presenter.SelectedCardId != 0 || State != BattleInputState.Idle)
                {
                    ClientAudio.Play(SoundCue.CardDeselect);
                }
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
                ClientAudio.Play(SoundCue.CardDeselect);
                State = BattleInputState.AwaitingServerResponse;
                picker.SetSubmitting(true);
            }
        }

        void PickServer(string token)
        {
            if (serverChoice == null || presenter.Client.Input.SelectionPhase != SelectionInputPhase.Selecting)
            {
                return;
            }
            var option = System.Array.Find(serverChoice.Candidates, candidate => candidate.Token == token);
            if (option == null)
            {
                return;
            }
            var action = option.CardId == 0 && option.Zone == (int)PresentationZone.Field
                ? GameplayInputAction.SelectPlayer : GameplayInputCoordinator.CardSelectionAction(option.Zone);
            if (!presenter.Client.Input.CanExecute(action))
            {
                return;
            }
            // Preview only the card DTO authorized in this choice; never resolve private IDs locally.
            presenter.InspectCard(option.Card, true);
            bool selectedBefore = serverTargets.Contains(token);
            if (selectedBefore)
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
            bool selectedNow = serverTargets.Contains(token);
            if (selectedBefore != selectedNow)
            {
                ClientAudio.Play(selectedNow ? SoundCue.CardSelect : SoundCue.CardDeselect);
            }
            picker.SetServerSelected(serverTargets);
            Hints();
        }

        void ConfirmServer()
        {
            if (serverChoice == null || !presenter.Client.Input.CanExecute(GameplayInputAction.ConfirmSelection))
            {
                return;
            }
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
                picker.SetSubmitting(true);
            }
        }

        private bool CanDirectAttackAt(Vector2 screenPoint)
        {
            if (!presenter.CanAct || !presenter.EnemyFieldEmpty || source?.Card?.CanDirectAttack != true)
            {
                return false;
            }
            var canvas = presenter.GetComponent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null
                : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)canvas.transform, screenPoint, camera))
            {
                return false;
            }
            // Use the rendered portrait's bottom edge at any canvas scale/resolution.
            // This is a drag threshold, not a raycast surface that can cover other UI.
            ((RectTransform)presenter.EnemyPlayer.transform).GetWorldCorners(_heroCorners);
            float bottom = float.PositiveInfinity;
            foreach (var corner in _heroCorners)
            {
                bottom = Mathf.Min(bottom, RectTransformUtility.WorldToScreenPoint(camera, corner).y);
            }
            return screenPoint.y >= bottom;
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
                    if (!widget.InHand)
                    {
                        CardSelectionMark.Set(widget, State == BattleInputState.SelectingEffectTarget && hint == TargetHint.Focused);
                    }
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
