using System;
using System.Collections;
using System.Collections.Generic;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattlePresenter : MonoBehaviour
    {
        private const float DefaultSummonDuration = .66f;
        private const float MaximumSummonSpeed = 2f;
        private const float DefaultHandAngle = 7f;
        private const float SummonSpeedPerPendingBatch = .2f;
        private const double MillisecondsPerSecond = 1000;
        public MultiplayerDebugScreen Client;
        public LobbyPanelView Lobby;
        public GameObject LobbyOverlay,
            GraveyardPanel,
            ResultPanel;
        public CardZoneView Hand,
            OwnField,
            EnemyField,
            EnemyHand,
            OwnGraveyard,
            EnemyGraveyard;
        public PlayerPanelView OwnPlayer,
            EnemyPlayer;
        public CardDetailView Detail;
        private CardView _inspected;
        private bool _inspectionFromChoice;
        private bool _compactHandHoverInspection;
        private bool _fieldHoverInspection;
        private TMP_Text _turnTimer;
        private double _turnDeadline;
        private bool _turnTimerRunning;
        private RectTransform _handRect;
        private Vector2 _handPosition;
        private CardGalleryView _gallery;
        private bool _resultWasVisible;
        private string _hudLanguage;
        private BattleTurnHudView _turnHud;

        public void SetHandExpanded(bool expanded)
        {
            Hand.Layout.SetExpanded(expanded);
        }

        public void InspectCard(CardView card, bool fromChoice = false)
        {
            if (card == null || !Client.Input.CanExecute(GameplayInputAction.OpenCardDetails))
            {
                return;
            }
            _inspected = card;
            _inspectionFromChoice = fromChoice;
            Detail.Show(card);
        }

        public void HideCardInfo()
        {
            _inspected = null;
            _compactHandHoverInspection = false;
            _fieldHoverInspection = false;
            Detail.Show(null);
        }

        public BattleLogView Log;
        public BattleEffectsView Effects;
        public TMP_Text TurnLabel,
            PhaseLabel,
            ResultLabel;
        public Button EndTurn,
            LobbyToggle,
            LogToggle,
            Resync,
            ResultRematch,
            ResultLeave;
        public int SelectedCardId { get; private set; }
        public int TargetCardId { get; private set; }
        public int RenderedTurn { get; private set; }
        public MulliganPanelView Mulligan { get; private set; }
        public BattleInteractionController Interaction { get; private set; }
        public ModalBlocker Modal { get; private set; }
        public CardZoneView[] Zones =>
            new[] { Hand, OwnField, EnemyField, EnemyHand, OwnGraveyard, EnemyGraveyard };
        public bool CanAct =>
            Client.Connected
            && !Client.Busy
            && Client.State != null
            && Client.State.Started
            && !Client.State.Finished
            && !Client.State.MulliganActive
            && !Client.Input.HasSelection
            && !(Mulligan?.Reviewing ?? false)
            && Client.State.ResolvingPlayerId == 0
            && Client.State.ActivePlayerId == Client.PlayerId
            && (LobbyOverlay == null || !LobbyOverlay.activeSelf)
            && !GraveyardPanel.activeSelf
            && (_gallery == null || !_gallery.gameObject.activeSelf);
        public bool PresentationBusy => _playing || _batches.Count > 0 || events.Count > 0;
        public int EnemyId => enemy?.PlayerId ?? 0;
        public bool EnemyFieldEmpty => enemy?.Field?.Length == 0;

        public bool IsOwnField(int id) =>
            id != 0 && mine?.Field != null && System.Array.Exists(mine.Field, c => c.Id == id);

        public bool IsEnemyField(int id) =>
            id != 0 && enemy?.Field != null && System.Array.Exists(enemy.Field, c => c.Id == id);

        public CardView FindCard(int id) => knownCards.TryGetValue(id, out var card) ? card : null;

        public void SelectCard(CardWidget widget)
        {
            if (SelectedCardId != widget.Card.Id)
            {
                ClientAudio.Play(SoundCue.CardSelect);
            }
            SelectedCardId = widget.Card.Id;
            selected = widget.Card;
            foreach (var zone in Zones)
            {
                foreach (var w in zone.Widgets)
                {
                    w.SetSelected(w.Card?.Id == SelectedCardId);
                }
            }
            InspectCard(selected);
        }

        StateView rendered;
        string match;
        int epoch;
        bool dirty = true,
            showLobby = true;
        CardView selected;
        PlayerView mine,
            enemy;
        private const int MaximumPendingEvents = 256;
        private const int MaximumPendingBatches = 64;
        private readonly BattlePresentationState _presentation = new BattlePresentationState();
        private readonly Queue<PresentationBatch> _batches = new Queue<PresentationBatch>();
        private bool _playing;
        private bool _snapPresentation = true;
        private bool _restorePresentation = true;

        private const float CompactHandWidth = 1050f;
        private const float ExpandedHandWidth = 1100f;
        private const float ExpandedHandCenterX = 1037f;
        private const float ExpandedHandTop = 803f;
        private const float HandViewportPadding = 16f;
        private Vector2 _authoredHandSize;
        [SerializeField] private bool _useAuthoredHandLayout;

        private sealed class PresentationBatch
        {
            public EventView[] Events;
            public StateView Snapshot;
        }

        readonly Queue<EventView> events = new Queue<EventView>();
        readonly Dictionary<int, CardView> knownCards = new Dictionary<int, CardView>();

        void Awake()
        {
            BattleText.StyleValue(TurnLabel);
            BattleText.StyleHud(PhaseLabel);
            BattleText.StyleHeading(EndTurn.GetComponentInChildren<TMP_Text>(), true);
            // Resolve hierarchy references using the project's scoped Bind contract.
            Client =
                ClientApp.Instance != null
                    ? ClientApp.Instance.Network
                    : GetComponent<MultiplayerDebugScreen>();
            // The scene-based front end no longer embeds the legacy lobby/debug panel.
            this.Bind("LobbyPanel", out Lobby);
            LobbyOverlay = Lobby != null ? Lobby.gameObject : null;
            this.Bind("YourHandImage", out Hand);
            this.Bind("OwnFieldImage", out OwnField);
            this.Bind("OpponentFieldImage", out EnemyField);
            this.Bind("OpponentHandImage", out EnemyHand);
            this.Bind("YourGraveyardImage", out OwnGraveyard);
            this.Bind("OpponentGraveyardImage", out EnemyGraveyard);
            this.Bind("OwnPlayerImage", out OwnPlayer);
            this.Bind("EnemyPlayerImage", out EnemyPlayer);
            this.Bind("CardInspectorImage", out Detail);
            this.Bind("RecentEventsImage", out Log);
            Modal = ModalBlocker.Create((RectTransform)transform);
            Interaction = GetComponent<BattleInteractionController>();
            Interaction.Initialize(this);
            _handRect = (RectTransform)Hand.transform;
            _handPosition = _handRect.anchoredPosition;
            _authoredHandSize = _handRect.sizeDelta;
            if (!this.Bind("CardGalleryImage", out _gallery))
            {
                throw new InvalidOperationException("Battle screen requires CardGallery.");
            }
            _gallery.Initialize(this);
            var deckButton = Effects.OwnDeck.GetComponent<Button>();
            if (deckButton == null)
            {
                deckButton = Effects.OwnDeck.gameObject.AddComponent<Button>();
            }
            deckButton.onClick.AddListener(_gallery.ShowOwnDeck);
            if (Effects.OwnDeck.TryGetComponent<Image>(out var deckImage))
            {
                deckImage.raycastTarget = true;
                deckButton.targetGraphic = deckImage;
            }
            Lobby?.Bind(Client);
            EndTurn.onClick.AddListener(() =>
            {
                if (Interaction.State == BattleInputState.Idle || Interaction.State == BattleInputState.SelectingCardAction)
                {
                    Command(Client.State?.Phase == (int)BattlePhaseKind.Main2 || Client.State?.Phase == (int)BattlePhaseKind.End ? "EndTurn" : "AdvancePhase");
                }
            });
            if (!this.Bind("TimerText", out _turnTimer))
            {
                throw new InvalidOperationException("Battle screen requires TimerText.");
            }
            if (!this.Bind("TurnHudImage", out _turnHud))
            {
                throw new InvalidOperationException("Battle screen requires TurnHudImage.");
            }
            BattleText.StyleValue(_turnTimer);
            LobbyToggle.onClick.AddListener(() =>
            {
                if (!Client.Input.CanExecute(GameplayInputAction.OpenBattleMenu))
                {
                    return;
                }
                if (ClientApp.Instance != null)
                {
                    ClientApp.Instance.Navigate("LobbyScene");
                }
                else
                {
                    showLobby = !showLobby;
                }
            });
            LogToggle.onClick.AddListener(() =>
            {
                if (Client.Input.CanExecute(GameplayInputAction.ToggleBattleLog))
                {
                    Log.Toggle();
                }
            });
            Resync?.onClick.AddListener(Client.RequestSnapshot);
            ResultRematch.onClick.AddListener(Client.RequestRematch);
            ResultLeave.onClick.AddListener(Client.LeaveMatch);
            Log.Initialize(this);
            Client.NoticeReceived += Log.Notice;
            Client.EventReceived += ReceiveEvent;
            Client.SnapshotReceived += ReceiveSnapshot;
            Client.ViewInvalidated += Invalidate;
            Effects.ResetFeedback();
            HideCardInfo();
            Mulligan = MulliganPanelView.Create(this);
            if (Client.State != null)
            {
                ReceiveSnapshot(Client.State);
            }
        }

        void OnDestroy()
        {
            if (Client != null)
            {
                Client.NoticeReceived -= Log.Notice;
                Client.EventReceived -= ReceiveEvent;
                Client.SnapshotReceived -= ReceiveSnapshot;
                Client.ViewInvalidated -= Invalidate;
            }
        }

        void Invalidate()
        {
            ClientAudio.StopBattleIfRunning();
            _turnTimerRunning = false;
            _turnHud?.ResetTimer();
            Log.Invalidate();
            StopAllCoroutines();
            _playing = false;
            _batches.Clear();
            _restorePresentation = true;
            _snapPresentation = true;
            dirty = true;
            events.Clear();
            Effects.ResetFeedback();
            _gallery?.Close();
            HideCardInfo();
        }

        public void SkipPresentation()
        {
            // Reuse the same cleanup as reconnect, then commit the authoritative snapshot.
            Invalidate();
            Interaction.ResetInput();
            ClearSelection();
            rendered = Client.State;
            _presentation.Reset(Client.State);
            _restorePresentation = false;
            _turnTimerRunning = Client.State?.TurnTimerRunning ?? false;
            RenderBoard();
            dirty = false;
        }

        private void OnDisable()
        {
            if (Effects != null && Detail != null)
            {
                Invalidate();
            }
        }

        void ReceiveEvent(EventView e)
        {
            Log.QueueEvent(e);
            if (events.Count >= MaximumPendingEvents)
            {
                Debug.LogWarning("Battle presentation event backlog exceeded; restoring the latest snapshot.", this);
                Invalidate();
            }
            events.Enqueue(e);
        }

        private void ReceiveSnapshot(StateView snapshot)
        {
            Log.AcceptSnapshot(snapshot);
            _turnTimerRunning = snapshot.TurnTimerRunning;
            // Correct against the server's timestamp pair, then advance with a monotonic local clock.
            _turnDeadline = Time.realtimeSinceStartupAsDouble + Math.Max(0,
                (snapshot.TurnEndsAtUnixMs - Client.SnapshotServerTimeUnixMs) / MillisecondsPerSecond);
            if (_restorePresentation || _presentation.State == null
                || (snapshot.Started && !_presentation.State.Started)
                || snapshot.MulliganActive != _presentation.State.MulliganActive)
            {
                _presentation.Reset(snapshot);
                _restorePresentation = false;
                events.Clear();
                dirty = true;
                return;
            }
            if (_batches.Count >= MaximumPendingBatches)
            {
                Debug.LogWarning("Battle presentation snapshot backlog exceeded; restoring the latest snapshot.", this);
                Invalidate();
                _presentation.Reset(snapshot);
                _restorePresentation = false;
                return;
            }
            // Pair each event batch with the snapshot that supplies its visible cards,
            // including when several server messages arrive in one Unity frame.
            _batches.Enqueue(new PresentationBatch { Events = events.ToArray(), Snapshot = snapshot });
            events.Clear();
        }

        private IEnumerator PlayPresentation()
        {
            _playing = true;
            var playback = PlayBatches();
            while (true)
            {
                object next;
                try
                {
                    if (!playback.MoveNext())
                    {
                        break;
                    }
                    next = playback.Current;
                }
                catch (Exception error)
                {
                    Debug.LogException(error, this);
                    Effects.ResetFeedback();
                    _batches.Clear();
                    events.Clear();
                    _presentation.Reset(Client.State);
                    _snapPresentation = true;
                    dirty = true;
                    break;
                }
                yield return next;
            }
            _playing = false;
        }

        private IEnumerator PlayBatches()
        {
            while (_batches.Count > 0)
            {
                var batch = _batches.Dequeue();
                bool drawing = false;
                foreach (var e in batch.Events)
                {
                    bool draw = BattlePresentationState.IsDraw(e);
                    bool fieldEntry = BattlePresentationState.IsFieldEntry(e);
                    bool stateChange = BattlePresentationState.IsStateChange(e);
                    // Summon metadata follows its field-entry movement immediately so its
                    // sound stays synchronized. Later mutations wait for the reveal.
                    if (stateChange && !fieldEntry && e.Type != (int)PresentationEvent.Summon)
                    {
                        while (Effects.ActiveSummons > 0)
                        {
                            yield return null;
                        }
                    }
                    // Information can start while cards fly. The next actual state
                    // transition waits for preceding draws to land, preserving causality.
                    if (!draw && stateChange && drawing)
                    {
                        while (Effects.ActiveDraws > 0)
                        {
                            yield return null;
                        }
                        drawing = false;
                    }
                    if (draw && drawing)
                    {
                        yield return new WaitForSecondsRealtime(Effects.Settings?.DrawInterval ?? .16f);
                    }
                    BattleSoundEvents.Present(e, _presentation.State);
                    Effects.PresentNumber(e, this, _presentation.State);
                    _presentation.Apply(e, batch.Snapshot, Client.PlayerId);
                    if (stateChange)
                    {
                        RenderBoard();
                    }
                    Present(e);
                    if (fieldEntry)
                    {
                        var zone = e.TargetPlayerId == Client.PlayerId ? OwnField : EnemyField;
                        var widget = zone.Widgets.Find(w => w.Card?.Id == e.TargetCardId);
                        float speed = Mathf.Min(MaximumSummonSpeed, 1 + _batches.Count * SummonSpeedPerPendingBatch);
                        Effects.Summon(zone, widget, (Effects.Settings?.SummonDuration ?? DefaultSummonDuration) / speed);
                    }
                    if (draw)
                    {
                        bool own = e.TargetPlayerId == Client.PlayerId;
                        var zone = own ? Hand : EnemyHand;
                        var widget = own ? zone.Widgets.Find(w => w.Card?.Id == e.TargetCardId)
                            : zone.Widgets.Count > 0 ? zone.Widgets[zone.Widgets.Count - 1] : null;
                        if (widget != null)
                        {
                            Effects.Draw(zone, widget, own);
                            drawing = true;
                        }
                    }
                }
                while (Effects.ActiveDraws > 0 || Effects.ActiveSummons > 0)
                {
                    yield return null;
                }
                if (batch.Snapshot != null)
                {
                    _presentation.Reset(batch.Snapshot);
                    RenderBoard();
                }
            }
        }

        void Command(string type)
        {
            if (!CanAct)
            {
                return;
            }
            Interaction.ResetInput();
            Log.ClearFeedback();
            Client.SendGameCommand(type, SelectedCardId, TargetCardId);
        }

        public void ClearSelection()
        {
            SelectedCardId = TargetCardId = 0;
            selected = null;
            foreach (var zone in Zones)
            {
                if (zone != null)
                {
                    foreach (var widget in zone.Widgets)
                    {
                        widget.SetSelected(false);
                    }
                }
            }
        }

        void Choose(CardWidget widget)
        {
            Interaction.Click(widget);
        }

        void ChooseTarget(CardWidget widget)
        {
            Interaction.Click(widget);
        }

        private void UpdateHandBounds()
        {
            var parent = (RectTransform)_handRect.parent;
            var canvas = (RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
            Vector3 topLeft = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(canvas.rect.xMin, canvas.rect.yMax)));
            Vector3 bottomRight = parent.InverseTransformPoint(canvas.TransformPoint(new Vector3(canvas.rect.xMax, canvas.rect.yMin)));
            float left = topLeft.x - parent.rect.xMin;
            float top = parent.rect.yMax - topLeft.y;
            float viewportWidth = bottomRight.x - topLeft.x;
            float viewportHeight = topLeft.y - bottomRight.y;
            bool compact = Hand.Layout.Compact;
            float desiredWidth = _useAuthoredHandLayout ? _authoredHandSize.x
                : compact ? CompactHandWidth : ExpandedHandWidth;
            float width = Mathf.Min(desiredWidth,
                Mathf.Max(0, viewportWidth - HandViewportPadding * 2));
            _handRect.sizeDelta = new Vector2(width, _authoredHandSize.y);
            // Keep the compact hand's authored right edge and expanded center.
            // Authored skins retain their measured position; bounds/focus/drag behavior still applies.
            var destination = _useAuthoredHandLayout
                ? _handPosition + Vector2.right * (_authoredHandSize.x - width) * .5f
                : compact
                ? _handPosition + Vector2.left * (width - _authoredHandSize.x)
                : new Vector2(ExpandedHandCenterX - width * .5f, -ExpandedHandTop);
            destination.x = Mathf.Clamp(destination.x, left + HandViewportPadding,
                Mathf.Max(left + HandViewportPadding, left + viewportWidth - width - HandViewportPadding));
            float halfHeight = _handRect.rect.height * .5f;
            float rotation = (Hand.Layout.Settings?.HandRotation ?? DefaultHandAngle) * Mathf.Deg2Rad;
            float rotatedHeight = Hand.Layout.CardSize.y * Mathf.Cos(rotation)
                + Hand.Layout.CardSize.x * Mathf.Sin(rotation);
            float faceHalfHeight = rotatedHeight * Hand.Layout.CardScale * .5f
                * CardWidget.ExpandedHandFocusScale;
            float minimumTop = Mathf.Max(HandViewportPadding,
                faceHalfHeight + CardTableLayout.FocusLift - halfHeight + HandViewportPadding);
            // A focused card is lifted and unrotated; the lowest idle card is rotated and curved.
            // These are separate states, so reserve the larger extent rather than adding both.
            float idleBottom = rotatedHeight * Hand.Layout.CardScale * .5f
                + (Hand.Layout.Settings?.HandCurve ?? 24f);
            float focusedBottom = Hand.Layout.CardSize.y * Hand.Layout.CardScale * .5f
                * CardWidget.ExpandedHandFocusScale - CardTableLayout.FocusLift;
            float maximumTop = Mathf.Max(minimumTop, viewportHeight - halfHeight
                - Mathf.Max(idleBottom, focusedBottom) - HandViewportPadding);
            destination.y = -Mathf.Clamp(-destination.y, top + minimumTop, top + maximumTop);
            _handRect.anchoredPosition = Vector2.Lerp(_handRect.anchoredPosition, destination,
                1 - Mathf.Exp(-Time.unscaledDeltaTime * 22));
            // A viewport shrink must not wait for the transition before fitting horizontally.
            var position = _handRect.anchoredPosition;
            position.x = Mathf.Clamp(position.x, left + HandViewportPadding,
                Mathf.Max(left + HandViewportPadding, left + viewportWidth - width - HandViewportPadding));
            _handRect.anchoredPosition = position;
        }

        void Hover(CardWidget widget)
        {
            if (widget.Card != null && (widget.InHand || widget.OwnedByLocalPlayer
                || IsEnemyField(widget.Card.Id)
                || Interaction.State == BattleInputState.SelectingEffectTarget))
            {
                _compactHandHoverInspection = widget.InHand && Hand.Layout.Compact && SelectedCardId == 0;
                _fieldHoverInspection = !widget.InHand && SelectedCardId == 0;
                InspectCard(widget.Card);
                Interaction.FocusHand(widget);
            }
        }

        void Unhover()
        {
            if ((_compactHandHoverInspection || _fieldHoverInspection) && SelectedCardId == 0)
            {
                HideCardInfo();
            }
        }

        void LateUpdate()
        {
            if (ClientApp.Instance != null && string.IsNullOrEmpty(Client.CurrentMatch))
            {
                ClientApp.Instance.Navigate("LobbyScene");
                return;
            }
            bool changedEpoch = epoch != Client.GameNumber || match != Client.CurrentMatch;
            if (changedEpoch)
            {
                epoch = Client.GameNumber;
                match = Client.CurrentMatch;
                RenderedTurn = 0;
                knownCards.Clear();
                ClearSelection();
                Effects.ResetFeedback();
                showLobby = !(Client.State?.Started ?? false);
            }
            if (rendered != Client.State)
            {
                bool enteringGame =
                    Client.State != null
                    && Client.State.Started
                    && (rendered == null || !rendered.Started || changedEpoch);
                Interaction.SnapshotChanged();
                rendered = Client.State;
                dirty = true;
                if (enteringGame)
                {
                    showLobby = false;
                }
            }
            if (_restorePresentation)
            {
                _presentation.Reset(Client.State);
                _restorePresentation = false;
                dirty = true;
            }
            if (dirty)
            {
                RenderBoard();
                dirty = false;
            }
            if (events.Count > 0)
            {
                // A separate network frame may carry events before its snapshot. Public
                // events can still be shown; the later snapshot reconciles the projection.
                _batches.Enqueue(new PresentationBatch { Events = events.ToArray() });
                events.Clear();
            }
            if (!_playing && _batches.Count > 0)
            {
                StartCoroutine(PlayPresentation());
            }
            if (!PresentationBusy)
            {
                Interaction.RefreshServerChoice();
            }
            bool browsing = _gallery.gameObject.activeSelf;
            Hand.gameObject.SetActive(!browsing);
            UpdateHandBounds();
            Lobby?.Render();
            Mulligan.Render();
            bool started = rendered != null && rendered.Started;
            LobbyOverlay?.SetActive((showLobby || !started) && !Client.AutomaticMatchInProgress);
            bool resultVisible = started && rendered.Finished && !showLobby;
            ResultPanel.SetActive(resultVisible);
            if (resultVisible && !_resultWasVisible)
            {
                ClientAudio.Shared.PlayResult(Client.CurrentMatch, Client.GameNumber, rendered.WinnerPlayerId, Client.PlayerId);
            }
            _resultWasVisible = resultVisible;
            if (started && rendered.Finished)
            {
                ResultLabel.text =
                    rendered.WinnerPlayerId == 0 ? "DRAW / ABORTED"
                    : rendered.WinnerPlayerId == Client.PlayerId ? "VICTORY" : "DEFEAT";
                if (Client.WebsiteMode && ClientApp.Instance.Website != null)
                {
                    ResultLabel.text += "\n" + ClientApp.Instance.Website.ResultStatus
                        + "\n" + ClientApp.Instance.Website.ResultRankSummary;
                }
            }
            bool action = CanAct;
            EndTurn.interactable = action && (Interaction.State == BattleInputState.Idle
                || Interaction.State == BattleInputState.SelectingCardAction)
                && Client.Input.CanExecute(rendered?.Phase == (int)BattlePhaseKind.Main2 || rendered?.Phase == (int)BattlePhaseKind.End
                    ? GameplayInputAction.EndTurn : GameplayInputAction.AdvancePhase);
            bool cardActionsAllowed = action && (Interaction.State == BattleInputState.Idle
                || Interaction.State == BattleInputState.SelectingCardAction);
            bool attackAllowed = cardActionsAllowed && Client.Input.CanExecute(GameplayInputAction.Attack);
            bool activationAllowed = cardActionsAllowed && Client.Input.CanExecute(GameplayInputAction.ActivateEffect);
            bool playAllowed = cardActionsAllowed && Client.Input.CanExecute(GameplayInputAction.PlayCard);
            foreach (var widget in Hand.Widgets)
            {
                widget.SetActionPermissions(attackAllowed, activationAllowed, playAllowed);
            }
            foreach (var widget in OwnField.Widgets)
            {
                widget.SetActionPermissions(attackAllowed, activationAllowed);
            }
            var turnFrame = EndTurn.GetComponent<TurnFrameGlow>();
            if (turnFrame != null)
            {
                turnFrame.SetOpponentTurn(rendered != null && rendered.ActivePlayerId != Client.PlayerId);
            }
            EndTurn.GetComponentInChildren<TMP_Text>().text = BattleText.Get(
                rendered?.Phase == (int)BattlePhaseKind.Main1 ? "ui_battle_battle_phase"
                : rendered?.Phase == (int)BattlePhaseKind.Battle ? "ui_battle_main_phase_2" : "ui_battle_end_turn");
            if (rendered != null)
            {
                PhaseLabel.text = BattleText.Get(rendered.Phase == (int)BattlePhaseKind.Main1 ? "ui_battle_main_phase_1"
                    : rendered.Phase == (int)BattlePhaseKind.Battle ? "ui_battle_battle_phase"
                    : rendered.Phase == (int)BattlePhaseKind.Main2 ? "ui_battle_main_phase_2" : "ui_battle_end_phase");
                TurnLabel.text = (Client.State ?? rendered).TurnNumber.ToString();
            }
            string language = CardPresentation.Library.Localization.Language;
            if (_hudLanguage != language && mine != null && enemy != null && rendered != null)
            {
                GetComponentInChildren<BattleHudView>().Render(mine, enemy, rendered.MaxYokaiPower);
                _hudLanguage = language;
            }
            double remaining = Math.Max(0, _turnDeadline - Time.realtimeSinceStartupAsDouble);
            int seconds = _turnTimerRunning ? (int)Math.Ceiling(remaining) : 0;
            _turnTimer.text = _turnTimerRunning ? seconds.ToString() : "--";
            _turnHud.Render(Client.State?.TurnNumber ?? 0, Client.State?.ActivePlayerId ?? 0, _turnTimerRunning, remaining,
                Client.PlayerId, Client.State?.Started ?? false, Client.State?.Finished ?? false, Client.Input.HasSelection);
            ResultRematch.interactable = !Client.WebsiteMode && Client.Connected && !Client.Busy && !RematchRequested();
            ResultLeave.interactable = Client.Connected && !Client.Busy;
        }

        bool RematchRequested()
        {
            if (Client.Lobby?.Players != null)
            {
                foreach (var p in Client.Lobby.Players)
                {
                    if (p.PlayerId == Client.PlayerId)
                    {
                        return p.Rematch;
                    }
                }
            }
            return false;
        }

        void RenderBoard()
        {
            // Command availability follows the latest server state even while visual
            // positions, HP and zone transitions are still catching up.
            _presentation.RefreshActions(Client.State);
            mine = enemy = null;
            if (rendered?.Players != null)
            {
                foreach (var p in rendered.Players)
                {
                    if (p.PlayerId == Client.PlayerId)
                    {
                        mine = p;
                    }
                    else
                    {
                        enemy = p;
                    }
                }
            }
            var visibleMine = _presentation.Player(Client.PlayerId) ?? mine;
            var visibleEnemy = _presentation.Player(enemy?.PlayerId ?? 0) ?? enemy;
            if (mine == null || enemy == null)
            {
                Hand.Render(null, 0, false, 0, false, Choose, Hover, Unhover);
                OwnField.Render(null, 5, false, 0, false, Choose, Hover, Unhover);
                EnemyField.Render(null, 5, false, 0, false, ChooseTarget, Hover, Unhover);
                EnemyHand.Render(null, 0, true, 0, false, null, Hover, Unhover);
                return;
            }
            selected = null;
            bool targetExists = false;
            foreach (var p in rendered.Players)
            {
                foreach (var zone in new[] { p.Hand, p.Field, p.Graveyard, p.Exile })
                {
                    if (zone != null)
                    {
                        foreach (var card in zone)
                        {
                            knownCards[card.Id] = card;
                            if (p == mine && card.Id == SelectedCardId)
                            {
                                selected = card;
                            }
                        }
                    }
                }
            }
            foreach (var card in enemy.Field)
            {
                if (card.Id == TargetCardId)
                {
                    targetExists = true;
                }
            }
            if (selected == null)
            {
                SelectedCardId = 0;
            }
            if (!targetExists || selected == null || !selected.CanAttack)
            {
                TargetCardId = 0;
            }
            int slots = Mathf.Max(1, rendered.FieldLimit);
            Hand.Render(visibleMine.Hand, 0, false, SelectedCardId, false, Choose, Hover, Unhover);
            OwnField.Render(
                visibleMine.Field,
                slots,
                false,
                SelectedCardId,
                false,
                Choose,
                Hover,
                Unhover
            );
            EnemyField.Render(
                visibleEnemy.Field,
                slots,
                false,
                TargetCardId,
                selected != null && selected.CanAttack,
                ChooseTarget,
                Hover,
                Unhover
            );
            EnemyHand.Render(null, visibleEnemy.HandCount, true, 0, false, null, Hover, Unhover);
            OwnGraveyard.Render(visibleMine.Graveyard, 0, false, 0, false, Choose, Hover, Unhover);
            EnemyGraveyard.Render(visibleEnemy.Graveyard, 0, false, 0, false, Choose, Hover, Unhover);
            OwnPlayer.Render(
                visibleMine,
                true,
                rendered.ActivePlayerId == mine.PlayerId,
                rendered.MaxYokaiPower
            );
            EnemyPlayer.Render(
                visibleEnemy,
                false,
                rendered.ActivePlayerId == enemy.PlayerId,
                rendered.MaxYokaiPower
            );
            GetComponentInChildren<BattleHudView>().Render(visibleMine, visibleEnemy, rendered.MaxYokaiPower);
            TurnLabel.text = rendered.TurnNumber.ToString();
            PhaseLabel.text = BattleText.Get(
                rendered.Phase == (int)BattlePhaseKind.Main1 ? "ui_battle_main_phase_1" : rendered.Phase == (int)BattlePhaseKind.Battle ? "ui_battle_battle_phase"
                : rendered.Phase == (int)BattlePhaseKind.Main2 ? "ui_battle_main_phase_2" : "ui_battle_end_phase");
            foreach (var zone in Zones)
            {
                foreach (var widget in zone.Widgets)
                {
                    Interaction.Attach(widget);
                }
            }
            if (_snapPresentation)
            {
                foreach (var zone in Zones)
                {
                    zone.Layout?.Snap();
                }
                _snapPresentation = false;
            }
            if (_inspected != null && !_inspectionFromChoice)
            {
                var current = FindCard(_inspected.Id);
                if (current != null)
                {
                    _inspected = current;
                    Detail.Show(current);
                }
            }
            if (rendered.TurnNumber != RenderedTurn && rendered.TurnStarted)
            {
                RenderedTurn = rendered.TurnNumber;
                ClientAudio.Shared.PlayTurn(Client.CurrentMatch, Client.GameNumber, RenderedTurn);
                Effects.Turn(rendered.ActivePlayerId == Client.PlayerId, RenderedTurn);
            }
        }

        void Present(EventView e)
        {
            int id = e.TargetCardId != 0 ? e.TargetCardId : e.SourceCardId;
            knownCards.TryGetValue(id, out var card);
            if (BattlePresentationState.IsStateChange(e) && !BattlePresentationState.IsDraw(e)
                && !BattlePresentationState.IsFieldEntry(e) && e.Type != (int)PresentationEvent.Summon)
            {
                foreach (var zone in Zones)
                {
                    foreach (var widget in zone.Widgets)
                    {
                        if (widget.Card?.Id == id)
                        {
                            widget.Pulse();
                        }
                    }
                }
            }
            if (e.Type == 19)
            {
                Effects.Popup(CardPresentation.Name(card, BattleText.Get("ui.card.spell")), BattleText.Get("ui.effect.activate"));
            }
            if (e.Type == 13)
            {
                Effects.Popup(
                    CardPresentation.Name(card, "Card effect"),
                    BattleText.Get("ui.effect.activate") + "\n" + CardPresentation.Rules(card)
                );
            }
            if (e.Type == 1)
            {
                Effects.Popup(
                    CardPresentation.Name(card, "Monster"),
                    "DAMAGE  -" + e.Damage + " / HP " + e.HpAfter
                );
            }
            if (e.Type == 15)
            {
                Effects.Popup(CardPresentation.Name(card, "Manifestation"), "FORM CHANGED / server confirmed");
            }
            if (e.Type == 10)
            {
                Effects.Popup("PLAYER DAMAGE", "-" + e.Damage + " / LIFE " + e.LifeAfter);
            }
            if (e.Type == 7 && e.Phase != (int)BattlePhaseKind.Main1)
            {
                Effects.Phase(BattleText.Get(e.Phase == (int)BattlePhaseKind.Battle ? "ui.phase.battle" : e.Phase == (int)BattlePhaseKind.Main2 ? "ui.phase.main2" : "ui.phase.end"));
            }
            if (e.Type == 2)
            {
                Effects.Popup(CardPresentation.Name(card, "Monster"), "DESTROYED / moved to graveyard");
            }
        }
    }
}
