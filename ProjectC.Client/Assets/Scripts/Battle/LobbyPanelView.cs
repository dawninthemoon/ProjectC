using System.Text;
using UnityEngine;
using UnityEngine.UI;
namespace ProjectC.Client
{
    public sealed class LobbyPanelView : MonoBehaviour
    {
        public InputField Address, MatchId;
        public Text Connection, Seats, DeckLabel, ReadyLabel;
        public Button Connect, Disconnect, Create, Join, Ready, Deck, Rematch, Leave, Close;
        MultiplayerDebugScreen client;
        public void Bind(MultiplayerDebugScreen value)
        {
            client = value;
            Connect.onClick.AddListener(() => client.ConnectTo(Address.text.Trim()));
            Disconnect.onClick.AddListener(client.DisconnectNetwork);
            Create.onClick.AddListener(client.CreateMatch);
            Join.onClick.AddListener(() => client.JoinMatch(MatchId.text.Trim()));
            Ready.onClick.AddListener(() => client.SetReady(client.SelectedDeck, !IsReady()));
            Deck.onClick.AddListener(() => {
                var decks = client.Decks; if (decks.Length == 0) return;
                int index = System.Array.FindIndex(decks, d => d.Id == client.SelectedDeck);
                client.SelectedDeck = decks[(index + 1) % decks.Length].Id;
            });
            Rematch.onClick.AddListener(client.RequestRematch); Leave.onClick.AddListener(client.LeaveMatch); Close.onClick.AddListener(client.CloseMatch);
        }
        bool IsReady()
        {
            if (client.Lobby?.Players != null) foreach (var p in client.Lobby.Players) if (p.PlayerId == client.PlayerId) return p.Ready;
            return false;
        }
        public void Render()
        {
            if (client == null) return;
            bool available = client.Connected && !client.Busy;
            bool joined = !string.IsNullOrEmpty(client.CurrentMatch);
            string status = client.Lobby?.Status ?? "Lobby";
            Connection.text = (client.Connected ? "CONNECTED" : "OFFLINE / reconnect to restore") + "\n" + (joined ? client.CurrentMatch + "  /  P" + client.PlayerId + "  /  " + status : "Create a room or enter a Match ID");
            var seats = new StringBuilder();
            if (client.Lobby?.Players != null) foreach (var p in client.Lobby.Players)
                seats.AppendLine("P" + p.PlayerId + (p.PlayerId == client.PlayerId ? " (you)" : "") + " : " + (!p.Occupied ? "empty" : !p.Connected ? "disconnected" : p.Rematch ? "rematch requested" : p.Ready ? "READY" : "not ready"));
            Seats.text = seats.ToString(); DeckLabel.text = "DECK / " + client.SelectedDeck;
            ReadyLabel.text = IsReady() ? "CANCEL READY" : "READY TO PLAY";
            Connect.interactable = !client.Connected && !client.Busy; Disconnect.interactable = client.Connected || client.Busy;
            Create.interactable = available && !joined; Join.interactable = available && !joined && !string.IsNullOrWhiteSpace(MatchId.text);
            Ready.interactable = available && joined && (status == "WaitingForPlayers" || status == "Ready");
            Deck.interactable = available && !IsReady() && status != "Playing";
            Rematch.interactable = available && status == "Finished";
            Leave.interactable = available && joined; Close.interactable = available && joined && client.Lobby?.HostPlayerId == client.PlayerId;
        }
    }
}
