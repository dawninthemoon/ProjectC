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
        private const int SecondsPerMinute = 60;
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
        private TMP_Text _turnTimer;
        private double _turnDeadline;
        private bool _turnTimerRunning;
        public void InspectCard(CardView card, bool fromChoice = false)
        {
            if (card == null)
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
            Detail.Show(null);
        }

        public BattleLogView Log;
        public BattleEffectsView Effects;
        public TMP_Text TurnLabel,
            PhaseLabel,
            SelectionLabel,
            ResultLabel,
            ConnectionLabel,
            DebugLabel;
        public Button Summon,
            BattlePhase,
            Attack,
            DirectAttack,
            EndTurn,
            Cancel,
            LobbyToggle,
            GraveyardToggle,
            LogToggle,
            Resync,
            ResultRematch,
            ResultLeave;
        public int SelectedCardId { get; private set; }
        public int TargetCardId { get; private set; }
        public int RenderedTurn { get; private set; }
        public MulliganPanelView Mulligan { get; private set; }
        public BattleInteractionController Interaction { get; private set; }
        public CardZoneView[] Zones =>
            new[] { Hand, OwnField, EnemyField, EnemyHand, OwnGraveyard, EnemyGraveyard };
        public bool CanAct =>
            Client.Connected
            && !Client.Busy
            && Client.State != null
            && Client.State.Started
            && !Client.State.Finished
            && !Client.State.MulliganActive
            && Client.State.ResolvingPlayerId == 0
            && Client.State.ActivePlayerId == Client.PlayerId
            && (LobbyOverlay == null || !LobbyOverlay.activeSelf)
            && !GraveyardPanel.activeSelf;
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

        private sealed class PresentationBatch
        {
            public EventView[] Events;
            public StateView Snapshot;
        }

        readonly Queue<EventView> events = new Queue<EventView>();
        readonly Dictionary<int, CardView> knownCards = new Dictionary<int, CardView>();

        void Awake()
        {
            // Resolve hierarchy references using the project's scoped Bind contract.
            Client =
                ClientApp.Instance != null
                    ? ClientApp.Instance.Network
                    : GetComponent<MultiplayerDebugScreen>();
            // The scene-based front end no longer embeds the legacy lobby/debug panel.
            this.Bind("LobbyPanel", out Lobby);
            LobbyOverlay = Lobby != null ? Lobby.gameObject : null;
            this.Bind("YourHand", out Hand);
            this.Bind("OwnField", out OwnField);
            this.Bind("OpponentField", out EnemyField);
            this.Bind("OpponentHand", out EnemyHand);
            this.Bind("YourGraveyard", out OwnGraveyard);
            this.Bind("OpponentGraveyard", out EnemyGraveyard);
            this.Bind("OwnPlayer", out OwnPlayer);
            this.Bind("EnemyPlayer", out EnemyPlayer);
            this.Bind("CardInspector", out Detail);
            this.Bind("RecentEvents", out Log);
            Interaction = GetComponent<BattleInteractionController>();
            Interaction.Initialize(this);
            Lobby?.Bind(Client);
            Summon.onClick.AddListener(() => Command("PlayCard"));
            BattlePhase.gameObject.SetActive(false);
            Attack.onClick.AddListener(() => Command("Attack"));
            DirectAttack.onClick.AddListener(() => Command("DirectAttack"));
            EndTurn.onClick.AddListener(() =>
            {
                if (Interaction.State == BattleInputState.Idle || Interaction.State == BattleInputState.SelectingCardAction)
                {
                    Command(Client.State?.Phase == (int)BattlePhaseKind.Main2 || Client.State?.Phase == (int)BattlePhaseKind.End ? "EndTurn" : "AdvancePhase");
                }
            });
            if (!this.Bind("TurnTimer", out _turnTimer))
            {
                throw new InvalidOperationException("Battle screen requires TurnTimer.");
            }
            Cancel.onClick.AddListener(ClearSelection);
            LobbyToggle.onClick.AddListener(() =>
            {
                if (ClientApp.Instance != null)
                {
                    ClientApp.Instance.Navigate("LobbyScene");
                }
                else
                {
                    showLobby = !showLobby;
                }
            });
            GraveyardToggle.onClick.AddListener(() =>
                GraveyardPanel.SetActive(!GraveyardPanel.activeSelf)
            );
            LogToggle.onClick.AddListener(Log.Toggle);
            Resync?.onClick.AddListener(Client.RequestSnapshot);
            ResultRematch.onClick.AddListener(Client.RequestRematch);
            ResultLeave.onClick.AddListener(Client.LeaveMatch);
            Client.NoticeReceived += Log.Add;
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
                Client.NoticeReceived -= Log.Add;
                Client.EventReceived -= ReceiveEvent;
                Client.SnapshotReceived -= ReceiveSnapshot;
                Client.ViewInvalidated -= Invalidate;
            }
        }

        void Invalidate()
        {
            _turnTimerRunning = false;
            StopAllCoroutines();
            _playing = false;
            _batches.Clear();
            _restorePresentation = true;
            _snapPresentation = true;
            dirty = true;
            events.Clear();
            Effects.ResetFeedback();
            HideCardInfo();
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
            if (events.Count >= MaximumPendingEvents)
            {
                Debug.LogWarning("Battle presentation event backlog exceeded; restoring the latest snapshot.", this);
                Invalidate();
            }
            events.Enqueue(e);
        }

        private void ReceiveSnapshot(StateView snapshot)
        {
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
                    bool stateChange = BattlePresentationState.IsStateChange(e);
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
                    _presentation.Apply(e, batch.Snapshot, Client.PlayerId);
                    if (stateChange)
                    {
                        RenderBoard();
                    }
                    Present(e);
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
                while (Effects.ActiveDraws > 0)
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

        void Hover(CardWidget widget)
        {
            // Hover animation and sorting belong to CardWidget; inspection is click-only.
        }

        void Unhover()
        {
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
            Lobby?.Render();
            Mulligan.Render();
            bool started = rendered != null && rendered.Started;
            LobbyOverlay?.SetActive(showLobby || !started);
            ResultPanel.SetActive(started && rendered.Finished && !showLobby);
            if (started && rendered.Finished)
            {
                ResultLabel.text =
                    rendered.WinnerPlayerId == Client.PlayerId ? "VICTORY" : "DEFEAT";
            }
            bool action = CanAct;
            Summon.interactable = action && selected != null && selected.CanSummon;
            BattlePhase.interactable = false;
            Attack.interactable =
                action && selected != null && selected.CanAttack && TargetCardId != 0;
            DirectAttack.interactable =
                action && selected != null && selected.CanDirectAttack && enemy?.Field?.Length == 0;
            EndTurn.interactable = action && (Interaction.State == BattleInputState.Idle
                || Interaction.State == BattleInputState.SelectingCardAction);
            EndTurn.GetComponentInChildren<TMP_Text>().text = BattleText.Get(
                rendered?.Phase == (int)BattlePhaseKind.Main1 ? "ui.phase.to_battle"
                : rendered?.Phase == (int)BattlePhaseKind.Battle ? "ui.phase.to_main2" : "ui.phase.end_turn");
            int seconds = _turnTimerRunning ? Mathf.Max(0, (int)Math.Ceiling(_turnDeadline - Time.realtimeSinceStartupAsDouble)) : 0;
            _turnTimer.text = _turnTimerRunning
                ? BattleText.Get("ui.turn.remaining", $"{seconds / SecondsPerMinute:00}:{seconds % SecondsPerMinute:00}") : "";
            ResultRematch.interactable = Client.Connected && !Client.Busy && !RematchRequested();
            ResultLeave.interactable = Client.Connected && !Client.Busy;
            if (ConnectionLabel != null)
            {
                ConnectionLabel.text =
                    (Client.Connected ? "ONLINE" : "OFFLINE") + "  /  " + Client.CurrentMatch;
            }
            if (DebugLabel != null)
            {
                DebugLabel.text =
                    $"P{Client.PlayerId}  |  GAME {epoch}  |  SEQ {Client.Sequence}"
                    + (Client.Busy ? "  /  WAITING" : "");
            }
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
            TurnLabel.text =
                rendered.ActivePlayerId == Client.PlayerId ? "YOUR TURN" : "OPPONENT TURN";
            if (rendered.ResolvingPlayerId != 0)
            {
                TurnLabel.text = "RESOLVING / P" + rendered.ResolvingPlayerId;
            }
            PhaseLabel.text = BattleText.Get("ui.turn.phase", rendered.TurnNumber, BattleText.Get(
                rendered.Phase == (int)BattlePhaseKind.Main1 ? "ui.phase.main1" : rendered.Phase == (int)BattlePhaseKind.Battle ? "ui.phase.battle"
                : rendered.Phase == (int)BattlePhaseKind.Main2 ? "ui.phase.main2" : "ui.phase.end"));
            SelectionLabel.text =
                selected == null
                    ? "Select a card"
                    : CardPresentation.Name(selected) + (TargetCardId == 0 ? "" : "  >  target #" + TargetCardId);
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
                Effects.Turn(rendered.ActivePlayerId == Client.PlayerId, RenderedTurn);
            }
        }

        void Present(EventView e)
        {
            int id = e.TargetCardId != 0 ? e.TargetCardId : e.SourceCardId;
            knownCards.TryGetValue(id, out var card);
            if (BattlePresentationState.IsStateChange(e) && !BattlePresentationState.IsDraw(e))
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
            if (e.Type == 11)
            {
                Effects.Popup(
                    CardPresentation.Name(card, "Monster summoned"),
                    "SUMMONED\n" + CardPresentation.Rules(card)
                );
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
