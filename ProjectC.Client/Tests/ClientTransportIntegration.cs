using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using ProjectC.Client;

sealed class Peer : IDisposable
{
    public WebSocketTransport Transport = new WebSocketTransport();
    public long Sequence;
    public int Game, Player;
    public string Match="", Token="";
    public JsonElement State;
    public Peer(int port) { _ = Transport.ConnectAsync("ws://127.0.0.1:" + port); }
    public async Task Ready()
    {
        var timeout = Stopwatch.StartNew();
        while (!Transport.Connected)
        {
            if (timeout.Elapsed.TotalSeconds > 5) throw new Exception("Connect timeout");
            await Task.Delay(10);
        }
    }
    public Task Send(object value) { return Transport.SendAsync(JsonSerializer.Serialize(value)); }
    public async Task<JsonElement> Receive(string type)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed.TotalSeconds < 5)
        {
            if (!Transport.Messages.TryDequeue(out var text)) { await Task.Delay(5); continue; }
            using var document = JsonDocument.Parse(text);
            var message = document.RootElement.Clone();
            var kind = message.GetProperty("Type").GetString();
            if (kind == "ServerError" || kind == "CommandRejected") throw new Exception(text);
            if(kind=="MatchCreated" || kind=="MatchJoined")
            {
                Match=message.GetProperty("MatchId").GetString();Player=message.GetProperty("PlayerId").GetInt32();Token=message.GetProperty("SessionToken").GetString();
            }
            if(kind=="SessionUpdated") Token=message.GetProperty("SessionToken").GetString();
            if(kind=="StateSnapshot" || kind=="GameEvents" || kind=="SessionUpdated" || kind=="LobbyState")
            {
                if(message.GetProperty("MatchId").GetString()!=Match) throw new Exception("Cross-match message");
                int game=message.GetProperty("GameNumber").GetInt32();
                if(game<Game) throw new Exception("Old game message");
                if(game>Game){Game=game;Sequence=0;}
            }
            if (kind == "StateSnapshot")
            {
                var sequence = message.GetProperty("SequenceNumber").GetInt64();
                if (sequence < Sequence) throw new Exception("Snapshot order");
                Sequence = sequence; State = message.GetProperty("Payload");
            }
            if(kind=="MatchEnded") Token="";
            if(kind=="MatchClosed" || kind=="MatchLeft") {Match="";Game=0;Sequence=0;Token="";}
            if (kind == type) return message;
        }
        throw new Exception("Receive timeout: " + type);
    }
    public void Dispose() { Transport.Dispose(); }
}
static class Program
{
    static void Check(bool result) { if (!result) throw new Exception("Client transport assertion failed"); }
    static async Task Main(string[] args)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var server = Process.Start(new ProcessStartInfo(args[0], "--port " + port) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
        try
        {
            Check((await server.StandardOutput.ReadLineAsync()).Contains("listening"));
            using var a = new Peer(port); using var b = new Peer(port); using var c=new Peer(port);using var d=new Peer(port);
            await a.Ready();await b.Ready();await c.Ready();await d.Ready();
            async Task Create(Peer host, Peer guest, string deckHost, string deckGuest)
            {
                await host.Send(new {Type="CreateMatch",ProtocolVersion=2,RequestId=Guid.NewGuid().ToString("N"),DeckId=deckHost});
                await host.Receive("MatchCreated");await host.Receive("StateSnapshot");
                await guest.Send(new {Type="JoinMatch",ProtocolVersion=2,RequestId="join",MatchId=host.Match,DeckId=deckGuest});
                await guest.Receive("MatchJoined");await guest.Receive("StateSnapshot");
                foreach(var p in new[]{host,guest})
                {
                    await p.Send(new {Type="SetReady",ProtocolVersion=2,RequestId="ready",MatchId=p.Match,GameNumber=0,Ready=true});
                    await p.Receive("LobbyAccepted");
                }
                await host.Receive("StateSnapshot");await guest.Receive("StateSnapshot");
            }
            await Create(a,b,"Strikers","Wardens");await Create(c,d,"Wardens","Strikers");
            Check(a.Match!=c.Match && a.Player!=c.Player && a.Sequence==c.Sequence);
            foreach(var pair in new[]{new[]{a,b},new[]{c,d}})
            {
                Check(pair[0].State.GetProperty("Players")[1].GetProperty("Hand").GetArrayLength()==0);
                Check(pair[1].State.GetProperty("Players")[0].GetProperty("Hand").GetArrayLength()==0);
            }
            async Task Command(Peer peer, Peer other, string type, int card=0,int target=0)
            {
                object command=type=="Attack"?new {Type=type,CardId=card,TargetCardId=target}:
                    type=="PlayCard" || type=="DirectAttack"?new {Type=type,CardId=card}:(object)new {Type=type};
                long before=peer.Sequence;
                await peer.Send(new {Type="SubmitCommand",ProtocolVersion=2,RequestId=Guid.NewGuid().ToString("N"),MatchId=peer.Match,GameNumber=peer.Game,SequenceNumber=before,Command=command});
                Check((await peer.Receive("CommandAccepted")).GetProperty("SequenceNumber").GetInt64()==before+1);
                await peer.Receive("StateSnapshot");await other.Receive("StateSnapshot");
                Check(peer.Sequence==other.Sequence && peer.Sequence==before+1);
                for(int i=0;i<2;++i) Check(peer.State.GetProperty("Players")[i].GetProperty("Life").GetInt32()==other.State.GetProperty("Players")[i].GetProperty("Life").GetInt32());
            }
            int aCard=a.State.GetProperty("Players")[0].GetProperty("Hand")[0].GetProperty("Id").GetInt32();
            int bCard=b.State.GetProperty("Players")[1].GetProperty("Hand")[0].GetProperty("Id").GetInt32();
            int cCard=c.State.GetProperty("Players")[0].GetProperty("Hand")[0].GetProperty("Id").GetInt32();
            await Command(c,d,"PlayCard",cCard);Check(c.State.GetProperty("Players")[0].GetProperty("Field")[0].GetProperty("Attack").GetInt32()==3);
            long independent=c.Sequence;string independentState=c.State.GetRawText();
            await Command(a,b,"PlayCard",aCard);await Command(a,b,"EndTurn");
            Check(a.State.GetProperty("TurnStarted").GetBoolean());
            await Command(b,a,"PlayCard",bCard);await Command(b,a,"EndTurn");
            await Command(a,b,"AdvancePhase");await Command(a,b,"Attack",aCard,bCard);
            await Command(a,b,"EndTurn");await Command(b,a,"EndTurn");await Command(a,b,"AdvancePhase");await Command(a,b,"DirectAttack",aCard);
            Check((await a.Receive("MatchEnded")).GetProperty("WinnerPlayerId").GetInt32()==a.Player);
            Check((await b.Receive("MatchEnded")).GetProperty("WinnerPlayerId").GetInt32()==a.Player);
            Check(c.Sequence==independent && c.State.GetRawText()==independentState);
            foreach(var p in new[]{a,b})
            {
                await p.Send(new {Type="Rematch",ProtocolVersion=2,RequestId="rematch",MatchId=p.Match,GameNumber=1});await p.Receive("LobbyAccepted");
            }
            await a.Receive("StateSnapshot");await b.Receive("StateSnapshot");
            Check(a.Game==2 && a.Sequence==1 && a.State.GetProperty("Players")[0].GetProperty("Field").GetArrayLength()==0);
            string token=c.Token;string match=c.Match;var finalState=c.State.GetRawText();c.Dispose();await Task.Delay(100);
            using var resumed=new Peer(port);await resumed.Ready();
            await resumed.Send(new {Type="JoinMatch",ProtocolVersion=2,RequestId="resume",MatchId=match,SessionToken=token});
            await resumed.Receive("MatchJoined");await resumed.Receive("StateSnapshot");Check(resumed.State.GetRawText()==finalState);
            await a.Send(new {Type="CloseMatch",ProtocolVersion=2,RequestId="close",MatchId=a.Match,GameNumber=2});
            await a.Receive("MatchClosed");await b.Receive("MatchClosed");
            await a.Send(new {Type="CreateMatch",ProtocolVersion=2,RequestId="new"});await a.Receive("MatchCreated");Check(a.Game==0);
            Console.WriteLine("Unity transport source: four C# clients, two isolated matches, decks/effects, automatic turns, victory, rematch, reconnect and new lobby passed");
        }
        finally { if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); } }
    }
}
