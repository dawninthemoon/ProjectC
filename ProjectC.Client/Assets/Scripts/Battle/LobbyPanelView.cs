using System.Text;
using Game.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client
{
    public sealed class LobbyPanelView : MonoBehaviour
    {
        public TMP_InputField Address,
            MatchId;
        public TMP_Text Connection,
            Seats,
            DeckLabel,
            ReadyLabel;
        public Button Connect,
            Disconnect,
            Create,
            Join,
            Ready,
            Deck,
            Rematch,
            Leave,
            Close;
        MultiplayerDebugScreen client;
        Button _queueMatch;
        Button _cancelQueue;
        private bool _streamlined;

        public void Bind(MultiplayerDebugScreen value)
        {
            this.Bind("ServerAddress", out Address);
            this.Bind("MatchId", out MatchId);
            this.Bind("Status", out Connection);
            this.Bind("Seats", out Seats);
            this.Bind("Connect", out Connect);
            this.Bind("Disconnect", out Disconnect);
            this.Bind("Create", out Create);
            this.Bind("Join", out Join);
            this.Bind("Ready", out Ready);
            this.Bind("Deck", out Deck);
            this.Bind("Rematch", out Rematch);
            this.Bind("Leave", out Leave);
            this.Bind("Close", out Close);
            Deck.transform.Bind("Label", out DeckLabel);
            Ready.transform.Bind("Label", out ReadyLabel);
            client = value;
            // BattleScene also uses this panel and does not require queue controls.
            bool queueBound = this.Bind("QueueMatch", out _queueMatch);
            bool cancelBound = this.Bind("CancelQueue", out _cancelQueue);
            if (queueBound && cancelBound)
            {
                _queueMatch.onClick.AddListener(client.QueueMatch);
                _cancelQueue.onClick.AddListener(client.CancelQueue);
            }
            else if (queueBound || cancelBound || GetComponentInParent<LobbyScenePresenter>() != null)
            {
                Debug.LogError("LobbyPanelView requires QueueMatch and CancelQueue Button children. Run Project C/Upgrade lobby matchmaking UI.", this);
            }
            _streamlined = GetComponentInParent<LobbyScenePresenter>() != null;
            if (_streamlined)
            {
                foreach (string name in new[] { "ServerAddress", "Connect", "Disconnect", "MatchId", "MatchHint", "Create", "Join", "Ready", "Seats", "Rematch", "Leave", "Close" })
                {
                    var child = transform.Find(name);
                    if (child != null)
                    {
                        child.gameObject.SetActive(false);
                    }
                }
                Deck.onClick.AddListener(client.CycleDeck);
                return;
            }
            Connect.onClick.AddListener(() => client.ConnectTo(Address.text.Trim()));
            Disconnect.onClick.AddListener(client.DisconnectNetwork);
            Create.onClick.AddListener(client.CreateMatch);
            Join.onClick.AddListener(() => client.JoinMatch(MatchId.text.Trim()));
            Ready.onClick.AddListener(() => client.SetReady(client.SelectedDeck, !IsReady()));
            Deck.onClick.AddListener(client.CycleDeck);
            Rematch.onClick.AddListener(client.RequestRematch);
            Leave.onClick.AddListener(client.LeaveMatch);
            Close.onClick.AddListener(client.CloseMatch);
        }

        bool IsReady()
        {
            if (client.Lobby?.Players != null)
            {
                foreach (var p in client.Lobby.Players)
                {
                    if (p.PlayerId == client.PlayerId)
                    {
                        return p.Ready;
                    }
                }
            }
            return false;
        }

        private void RenderMatchmaking()
        {
            bool searching = client.Matchmaking == MatchmakingState.Queuing;
            bool joined = !string.IsNullOrEmpty(client.CurrentMatch);
            DeckLabel.text = client.HasDeckSelection ? client.DeckSummary : "덱 선택";
            Deck.interactable = !client.Busy && !joined;
            _queueMatch.gameObject.SetActive(!searching);
            _queueMatch.interactable = client.ConnectionReady && !client.Busy && !joined && client.CanQueueSelectedDeck;
            _cancelQueue.gameObject.SetActive(searching);
            _cancelQueue.interactable = client.Connected && !client.CancellingQueue;
            if (searching)
            {
                const int SecondsPerMinute = 60;
                int seconds = Mathf.FloorToInt(client.QueueSeconds);
                Connection.text = (client.CancellingQueue ? "검색 취소 중" : "대전 상대 검색 중")
                    + $" · {seconds / SecondsPerMinute:00}:{seconds % SecondsPerMinute:00}";
            }
            else if (client.Matchmaking == MatchmakingState.Matched)
            {
                Connection.text = "대전 준비 중";
            }
            else if (!string.IsNullOrEmpty(client.MatchmakingError))
            {
                Connection.text = client.MatchmakingError;
            }
            else
            {
                Connection.text = !string.IsNullOrEmpty(client.CatalogProblem) ? client.CatalogProblem
                    : !client.HasDeckSelection ? "대전에 사용할 덱을 선택하세요."
                    : !client.CanQueueSelectedDeck ? client.DeckProblem : "대전 상대를 검색할 수 있습니다.";
            }
        }

        public void Render()
        {
            if (client == null)
            {
                return;
            }
            if (_streamlined)
            {
                RenderMatchmaking();
                return;
            }
            bool available = client.Connected && !client.Busy;
            bool joined = !string.IsNullOrEmpty(client.CurrentMatch);
            string status = client.Lobby?.Status ?? "Lobby";
            Connection.text =
                (client.Connected ? "CONNECTED" : "OFFLINE / reconnect to restore")
                + "\n"
                + (
                    joined
                        ? client.CurrentMatch + "  /  P" + client.PlayerId + "  /  " + status
                        : "Create a room or enter a Match ID"
                );
            if (client.Matchmaking == MatchmakingState.Queuing)
            {
                const int SecondsPerMinute = 60;
                int seconds = Mathf.FloorToInt(client.QueueSeconds);
                Connection.text = (client.CancellingQueue ? "매칭 취소 요청 중" : "매칭 중")
                    + $" · {seconds / SecondsPerMinute:00}:{seconds % SecondsPerMinute:00}"
                    + (client.QueuePosition > 0 ? " · 대기 순서 " + client.QueuePosition : "")
                    + "\n" + client.MatchmakingError;
            }
            else if (!string.IsNullOrEmpty(client.MatchmakingError))
            {
                Connection.text += "\n" + client.MatchmakingError;
            }
            if (_queueMatch != null && _cancelQueue != null)
            {
                _queueMatch.interactable = available && !joined && string.IsNullOrEmpty(client.CatalogProblem);
                _cancelQueue.gameObject.SetActive(client.Matchmaking == MatchmakingState.Queuing);
                _cancelQueue.interactable = client.Connected && !client.CancellingQueue;
            }
            Address.interactable = !client.Connected && !client.Busy;
            MatchId.interactable = !joined && client.Matchmaking != MatchmakingState.Queuing;
            var seats = new StringBuilder();
            if (client.Lobby?.Players != null)
            {
                foreach (var p in client.Lobby.Players)
                {
                    seats.AppendLine(
                        "P"
                            + p.PlayerId
                            + (p.PlayerId == client.PlayerId ? " (you)" : "")
                            + " : "
                            + (
                                !p.Occupied ? "empty"
                                : !p.Connected ? "disconnected"
                                : p.Rematch ? "rematch requested"
                                : p.Ready ? "READY / 덱 확정"
                                : p.DeckSubmitted ? "덱 제출 완료 / 준비 전"
                                : "덱 제출 대기"
                            )
                    );
                }
            }
            Seats.text = seats.ToString();
            DeckLabel.text = client.DeckSummary;
            ReadyLabel.text = IsReady() ? "CANCEL READY" : "READY TO PLAY";
            if (!IsReady() && !string.IsNullOrEmpty(client.DeckProblem))
            {
                Seats.text += "\n" + client.DeckProblem;
            }
            if (!string.IsNullOrEmpty(client.ConfirmedDeckName))
            {
                Seats.text += "\n확정 덱: " + client.ConfirmedDeckName;
            }
            Connect.interactable = !client.Connected && !client.Busy;
            Disconnect.interactable = client.Connected || client.Busy;
            Create.interactable = available && !joined;
            Join.interactable = available && !joined && !string.IsNullOrWhiteSpace(MatchId.text);
            Ready.interactable =
                available
                && joined
                && (status == "WaitingForPlayers" || status == "Ready")
                && (IsReady() || string.IsNullOrEmpty(client.DeckProblem));
            Deck.interactable = available && !IsReady() && status != "Playing";
            Rematch.interactable = available && status == "Finished";
            Leave.interactable = available && joined;
            Close.interactable =
                available && joined && client.Lobby?.HostPlayerId == client.PlayerId;
        }
    }
}
