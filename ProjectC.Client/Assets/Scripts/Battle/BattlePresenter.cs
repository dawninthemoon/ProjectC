using System.Collections.Generic;
using Game.Utils;
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
        public BattleInteractionController Interaction { get; private set; }
        public CardZoneView[] Zones => new[]{Hand,OwnField,EnemyField,EnemyHand,OwnGraveyard,EnemyGraveyard};
        public bool CanAct => Client.Connected && !Client.Busy && Client.State != null && Client.State.Started && !Client.State.Finished && Client.State.ActivePlayerId==Client.PlayerId && !LobbyOverlay.activeSelf && !GraveyardPanel.activeSelf;
        public int EnemyId => enemy?.PlayerId ?? 0;
        public bool EnemyFieldEmpty => enemy?.Field?.Length==0;
        public bool IsOwnField(int id) => id!=0 && mine?.Field!=null && System.Array.Exists(mine.Field,c=>c.Id==id);
        public bool IsEnemyField(int id) => id!=0 && enemy?.Field!=null && System.Array.Exists(enemy.Field,c=>c.Id==id);
        public CardView FindCard(int id) => knownCards.TryGetValue(id,out var card)?card:null;
        public void SelectCard(CardWidget widget) { SelectedCardId=widget.Card.Id; selected=widget.Card; foreach(var zone in Zones) foreach(var w in zone.Widgets) w.SetSelected(w.Card?.Id==SelectedCardId); Detail.Show(selected); Detail.Place(widget.WorldCenter); }
        float hoverAt; CardWidget hoverWidget;
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
            // Resolve hierarchy references using the project's scoped Bind contract.
            Client=GetComponent<MultiplayerDebugScreen>();
            this.Bind("LobbyPanel",out Lobby); LobbyOverlay=Lobby.gameObject;
            this.Bind("YourHand",out Hand);this.Bind("OwnField",out OwnField);this.Bind("OpponentField",out EnemyField);this.Bind("OpponentHand",out EnemyHand);
            this.Bind("YourGraveyard",out OwnGraveyard);this.Bind("OpponentGraveyard",out EnemyGraveyard);
            this.Bind("OwnPlayer",out OwnPlayer);this.Bind("EnemyPlayer",out EnemyPlayer);
            this.Bind("CardInspector",out Detail);this.Bind("RecentEvents",out Log);
            Interaction=GetComponent<BattleInteractionController>();Interaction.Initialize(this);
            Lobby.Bind(Client);
            Summon.onClick.AddListener(() => Command("PlayCard")); BattlePhase.onClick.AddListener(() => Command("AdvancePhase"));
            Attack.onClick.AddListener(() => Command("Attack")); DirectAttack.onClick.AddListener(() => Command("DirectAttack"));
            EndTurn.onClick.AddListener(() => Command("EndTurn")); Cancel.onClick.AddListener(ClearSelection);
            LobbyToggle.onClick.AddListener(() => showLobby = !showLobby);
            GraveyardToggle.onClick.AddListener(() => GraveyardPanel.SetActive(!GraveyardPanel.activeSelf));
            LogToggle.onClick.AddListener(Log.Toggle); Resync.onClick.AddListener(Client.RequestSnapshot);
            ResultRematch.onClick.AddListener(Client.RequestRematch); ResultLeave.onClick.AddListener(Client.LeaveMatch);
            Client.NoticeReceived += Log.Add; Client.EventReceived += ReceiveEvent; Client.ViewInvalidated+=Invalidate;
            Effects.ResetFeedback(); Detail.Show(null);
        }
        void OnDestroy() { if (Client != null) { Client.NoticeReceived -= Log.Add; Client.EventReceived -= ReceiveEvent;Client.ViewInvalidated-=Invalidate; } }
        void Invalidate() { events.Clear();Effects.ResetFeedback();Detail.Show(null); }
        void ReceiveEvent(EventView e) { if (events.Count < 256) events.Enqueue(e); }
        void Command(string type) { if(!CanAct) return; Interaction.ResetInput(); Log.ClearFeedback(); Client.SendGameCommand(type, SelectedCardId, TargetCardId); }
        public void ClearSelection() { SelectedCardId = TargetCardId = 0; selected = hovered = null; hoverWidget=null; Detail.Show(null); foreach(var zone in Zones) if(zone!=null) foreach(var widget in zone.Widgets) widget.SetSelected(false); }
        void Choose(CardWidget widget) { Interaction.Click(widget); }
        void ChooseTarget(CardWidget widget) { Interaction.Click(widget); }
        void Hover(CardWidget widget) { if(Interaction.State!=BattleInputState.Idle && Interaction.State!=BattleInputState.SelectingCardAction) return; hoverWidget=widget;hoverAt=Time.unscaledTime; }
        void Unhover() { hoverWidget=null;hovered=null; Detail.Show(null); }
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
                Interaction.SnapshotChanged(); rendered = Client.State; dirty = true; hovered = null;
                if (enteringGame) showLobby = false;
            }
            if (dirty) { RenderBoard(); dirty = false; }
            while (events.Count > 0) Present(events.Dequeue());
            if(hoverWidget!=null && Time.unscaledTime-hoverAt>=(Interaction.Settings?.HoverDelay??.2f)) { hovered=hoverWidget.Card;Detail.Show(hovered);Detail.Place(hoverWidget.WorldCenter); }
            Lobby.Render();
            bool started = rendered != null && rendered.Started;
            LobbyOverlay.SetActive(showLobby || !started);
            ResultPanel.SetActive(started && rendered.Finished && !showLobby);
            if (started && rendered.Finished) ResultLabel.text = rendered.WinnerPlayerId == Client.PlayerId ? "VICTORY" : "DEFEAT";
            bool action = CanAct;
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
            OwnGraveyard.Render(mine.Graveyard,0,false,0,false,Choose,Hover,Unhover);
            EnemyGraveyard.Render(enemy.Graveyard,0,false,0,false,Choose,Hover,Unhover);
            OwnPlayer.Render(mine,true,rendered.ActivePlayerId == mine.PlayerId); EnemyPlayer.Render(enemy,false,rendered.ActivePlayerId == enemy.PlayerId);
            TurnLabel.text = rendered.ActivePlayerId == Client.PlayerId ? "YOUR TURN" : "OPPONENT TURN";
            PhaseLabel.text = "TURN " + rendered.TurnNumber + "  /  " + (rendered.Phase == 0 ? "MAIN" : rendered.Phase == 1 ? "BATTLE" : "END");
            SelectionLabel.text = selected == null ? "Select a card" : selected.Name + (TargetCardId == 0 ? "" : "  >  target #" + TargetCardId);
            foreach(var zone in Zones) foreach(var widget in zone.Widgets) Interaction.Attach(widget);
            if (rendered.TurnNumber != RenderedTurn && rendered.TurnStarted)
            { RenderedTurn = rendered.TurnNumber; Effects.Turn(rendered.ActivePlayerId == Client.PlayerId, RenderedTurn); }
        }
        void Present(EventView e)
        {
            int id = e.TargetCardId != 0 ? e.TargetCardId : e.SourceCardId;
            knownCards.TryGetValue(id, out var card);
            if (e.Type == 4 && e.FromZone == 0 && e.ToZone == 1) Effects.Draw(e.TargetPlayerId == Client.PlayerId,card);
            if (e.Type == 11) { foreach(var zone in Zones) foreach(var widget in zone.Widgets) if(widget.Card?.Id==id) widget.Pulse(); }
            if (e.Type == 11) Effects.Popup(card?.Name ?? "Monster summoned", "SUMMONED\n" + (card?.RulesText ?? ""));
            if (e.Type == 13) Effects.Popup(card?.Name ?? "Card effect", "EFFECT ACTIVATED\n" + (card?.RulesText ?? ""));
            if (e.Type == 1) Effects.Popup(card?.Name ?? "Monster", "DAMAGE  -" + e.Damage + " / HP " + e.HpAfter);
            if (e.Type == 15) Effects.Popup(card?.Name??"Manifestation", "FORM CHANGED / server confirmed");
            if (e.Type == 10) Effects.Popup("PLAYER DAMAGE", "-"+e.Damage+" / LIFE "+e.LifeAfter);
            if (e.Type == 7 && e.Phase!=0) Effects.Phase(e.Phase==1?"BATTLE":"END");
            if (e.Type == 2) Effects.Popup(card?.Name ?? "Monster", "DESTROYED / moved to graveyard");
        }
    }
}
