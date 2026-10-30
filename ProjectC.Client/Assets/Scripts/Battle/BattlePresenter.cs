using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class BattlePresenter : MonoBehaviour
    {
        public MultiplayerDebugScreen Client;
        public LobbyPanelView Lobby;
        public GameObject LobbyOverlay, GraveyardPanel, ResultPanel;
        public CardZoneView Hand, OwnField, EnemyField, EnemyHand, OwnGraveyard, EnemyGraveyard;
        public PlayerPanelView OwnPlayer, EnemyPlayer;
        public CardDetailView Detail;
        public BattleLogView Log;
        public BattleEffectsView Effects;
        public Text TurnLabel, PhaseLabel, SelectionLabel, ResultLabel, ConnectionLabel, DebugLabel;
        public Button Summon, BattlePhase, Attack, DirectAttack, EndTurn, Cancel, LobbyToggle, GraveyardToggle, LogToggle, Resync, ResultRematch, ResultLeave;
        public int SelectedCardId { get; private set; }
        public int TargetCardId { get; private set; }
        public int RenderedTurn { get; private set; }
        StateView rendered;
        string match;
        int epoch;
        bool dirty = true, showLobby = true;
        CardView selected, hovered;
        PlayerView mine, enemy;
        readonly Queue<EventView> events = new Queue<EventView>();
        readonly Dictionary<int, CardView> knownCards = new Dictionary<int, CardView>();
        void Awake()
        {
            Lobby.Bind(Client);
            Summon.onClick.AddListener(() => Command("PlayCard")); BattlePhase.onClick.AddListener(() => Command("AdvancePhase"));
            Attack.onClick.AddListener(() => Command("Attack")); DirectAttack.onClick.AddListener(() => Command("DirectAttack"));
            EndTurn.onClick.AddListener(() => Command("EndTurn")); Cancel.onClick.AddListener(ClearSelection);
            LobbyToggle.onClick.AddListener(() => showLobby = !showLobby);
            GraveyardToggle.onClick.AddListener(() => GraveyardPanel.SetActive(!GraveyardPanel.activeSelf));
            LogToggle.onClick.AddListener(Log.Toggle); Resync.onClick.AddListener(Client.RequestSnapshot);
            ResultRematch.onClick.AddListener(Client.RequestRematch); ResultLeave.onClick.AddListener(Client.LeaveMatch);
            Client.NoticeReceived += Log.Add; Client.EventReceived += ReceiveEvent;
            Effects.ResetFeedback(); Detail.Show(null);
        }
        void OnDestroy() { if (Client != null) { Client.NoticeReceived -= Log.Add; Client.EventReceived -= ReceiveEvent; } }
        void ReceiveEvent(EventView e) { if (events.Count < 256) events.Enqueue(e); }
        void Command(string type) { Log.ClearFeedback(); Client.SendGameCommand(type, SelectedCardId, TargetCardId); }
        public void ClearSelection() { SelectedCardId = TargetCardId = 0; selected = hovered = null; dirty = true; Detail.Show(null); }
        void Choose(CardWidget widget)
        {
            SelectedCardId = widget.Card.Id; TargetCardId = 0; selected = widget.Card; dirty = true; Detail.Show(selected);
        }
        void ChooseTarget(CardWidget widget)
        {
            Detail.Show(widget.Card);
            if (selected != null && selected.CanAttack) { TargetCardId = widget.Card.Id; dirty = true; }
        }
        void Hover(CardWidget widget) { hovered = widget.Hidden ? null : widget.Card; Detail.Show(hovered ?? selected); }
        void Unhover() { hovered = null; Detail.Show(selected); }
        void LateUpdate()
        {
            bool changedEpoch = epoch != Client.GameNumber || match != Client.CurrentMatch;
            if (changedEpoch)
            {
                epoch = Client.GameNumber; match = Client.CurrentMatch; RenderedTurn = 0; knownCards.Clear();
                ClearSelection(); Effects.ResetFeedback(); showLobby = !(Client.State?.Started ?? false);
            }
            if (rendered != Client.State)
            {
                bool enteringGame = Client.State != null && Client.State.Started && (rendered == null || !rendered.Started || changedEpoch);
                rendered = Client.State; dirty = true; hovered = null;
                if (enteringGame) showLobby = false;
            }
            if (dirty) { RenderBoard(); dirty = false; }
            while (events.Count > 0) Present(events.Dequeue());
            Lobby.Render();
            bool started = rendered != null && rendered.Started;
            LobbyOverlay.SetActive(showLobby || !started);
            ResultPanel.SetActive(started && rendered.Finished && !showLobby);
            if (started && rendered.Finished) ResultLabel.text = rendered.WinnerPlayerId == Client.PlayerId ? "VICTORY" : "DEFEAT";
            bool action = Client.Connected && !Client.Busy && started && !rendered.Finished && rendered.ActivePlayerId == Client.PlayerId;
            Summon.interactable = action && selected != null && selected.CanSummon;
            BattlePhase.interactable = action && rendered.Phase == 0;
            Attack.interactable = action && selected != null && selected.CanAttack && TargetCardId != 0;
            DirectAttack.interactable = action && selected != null && selected.CanAttack && enemy?.Field?.Length == 0;
            EndTurn.interactable = action;
            ResultRematch.interactable = Client.Connected && !Client.Busy && !RematchRequested();
            ResultLeave.interactable = Client.Connected && !Client.Busy;
            ConnectionLabel.text = (Client.Connected ? "ONLINE" : "OFFLINE") + "  /  " + Client.CurrentMatch;
            DebugLabel.text = $"P{Client.PlayerId}  |  GAME {epoch}  |  SEQ {Client.Sequence}" + (Client.Busy ? "  /  WAITING" : "");
        }
        bool RematchRequested()
        {
            if (Client.Lobby?.Players != null) foreach (var p in Client.Lobby.Players) if (p.PlayerId == Client.PlayerId) return p.Rematch;
            return false;
        }
        void RenderBoard()
        {
            mine = enemy = null;
            if (rendered?.Players != null) foreach (var p in rendered.Players) { if (p.PlayerId == Client.PlayerId) mine = p; else enemy = p; }
            if (mine == null || enemy == null)
            {
                Hand.Render(null,0,false,0,false,Choose,Hover,Unhover); OwnField.Render(null,5,false,0,false,Choose,Hover,Unhover);
                EnemyField.Render(null,5,false,0,false,ChooseTarget,Hover,Unhover); EnemyHand.Render(null,0,true,0,false,null,Hover,Unhover);
                return;
            }
            selected = null; bool targetExists = false;
            foreach (var p in rendered.Players) foreach (var zone in new[] {p.Hand, p.Field, p.Graveyard})
                if (zone != null) foreach (var card in zone) { knownCards[card.Id] = card; if (p == mine && card.Id == SelectedCardId) selected = card; }
            foreach (var card in enemy.Field) if (card.Id == TargetCardId) targetExists = true;
            if (selected == null) SelectedCardId = 0;
            if (!targetExists || selected == null || !selected.CanAttack) TargetCardId = 0;
            int slots = Mathf.Max(1, rendered.FieldLimit);
            Hand.Render(mine.Hand,0,false,SelectedCardId,false,Choose,Hover,Unhover);
            OwnField.Render(mine.Field,slots,false,SelectedCardId,false,Choose,Hover,Unhover);
            EnemyField.Render(enemy.Field,slots,false,TargetCardId,selected != null && selected.CanAttack,ChooseTarget,Hover,Unhover);
            EnemyHand.Render(null,enemy.HandCount,true,0,false,null,Hover,Unhover);
            OwnGraveyard.Render(mine.Graveyard,0,false,0,false,w => Detail.Show(w.Card),Hover,Unhover);
            EnemyGraveyard.Render(enemy.Graveyard,0,false,0,false,w => Detail.Show(w.Card),Hover,Unhover);
            OwnPlayer.Render(mine,true,rendered.ActivePlayerId == mine.PlayerId); EnemyPlayer.Render(enemy,false,rendered.ActivePlayerId == enemy.PlayerId);
            TurnLabel.text = rendered.ActivePlayerId == Client.PlayerId ? "YOUR TURN" : "OPPONENT TURN";
            PhaseLabel.text = "TURN " + rendered.TurnNumber + "  /  " + (rendered.Phase == 0 ? "MAIN" : rendered.Phase == 1 ? "BATTLE" : "END");
            SelectionLabel.text = selected == null ? "Select a card" : selected.Name + (TargetCardId == 0 ? "" : "  >  target #" + TargetCardId);
            Detail.Show(hovered ?? selected);
            if (rendered.TurnNumber != RenderedTurn && rendered.TurnStarted)
            { RenderedTurn = rendered.TurnNumber; Effects.Turn(rendered.ActivePlayerId == Client.PlayerId, RenderedTurn); }
        }
        void Present(EventView e)
        {
            int id = e.TargetCardId != 0 ? e.TargetCardId : e.SourceCardId;
            knownCards.TryGetValue(id, out var card);
            if (e.Type == 4 && e.FromZone == 0 && e.ToZone == 1) Effects.Draw(e.TargetPlayerId == Client.PlayerId);
            if (e.Type == 11) Effects.Popup(card?.Name ?? "Monster summoned", "SUMMONED\n" + (card?.RulesText ?? ""));
            if (e.Type == 13) Effects.Popup(card?.Name ?? "Card effect", "EFFECT ACTIVATED\n" + (card?.RulesText ?? ""));
            if (e.Type == 1) Effects.Popup(card?.Name ?? "Monster", "DAMAGE  -" + e.Damage + " / HP " + e.HpAfter);
            if (e.Type == 2) Effects.Popup(card?.Name ?? "Monster", "DESTROYED / moved to graveyard");
        }
    }
}
