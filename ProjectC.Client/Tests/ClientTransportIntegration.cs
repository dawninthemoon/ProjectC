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
            if (kind == "StateSnapshot")
            {
                var sequence = message.GetProperty("SequenceNumber").GetInt64();
                if (sequence < Sequence) throw new Exception("Snapshot order");
                Sequence = sequence; State = message.GetProperty("Payload");
            }
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
            using var a = new Peer(port); using var b = new Peer(port);
            await a.Ready(); await b.Ready();
            await a.Send(new { Type="JoinMatch", ProtocolVersion=1, RequestId="a" });
            var joined = await a.Receive("MatchJoined"); string token = joined.GetProperty("SessionToken").GetString();
            await a.Receive("StateSnapshot");
            await b.Send(new { Type="JoinMatch", ProtocolVersion=1, RequestId="b" });
            await b.Receive("MatchJoined"); await a.Receive("StateSnapshot"); await b.Receive("StateSnapshot");
            Check(a.State.GetProperty("Players")[1].GetProperty("Hand").GetArrayLength() == 0);
            Check(b.State.GetProperty("Players")[0].GetProperty("Hand").GetArrayLength() == 0);
            async Task Command(Peer peer, string type, int card=0, int target=0)
            {
                object command = type == "Attack" ? new { Type=type, CardId=card, TargetCardId=target } :
                    type == "PlayCard" || type == "DirectAttack" ? new { Type=type, CardId=card } : (object)new { Type=type };
                long before = peer.Sequence;
                await peer.Send(new { Type="SubmitCommand", ProtocolVersion=1, RequestId=Guid.NewGuid().ToString("N"), MatchId="local-1", SequenceNumber=before, Command=command });
                var accepted = await peer.Receive("CommandAccepted");
                Check(accepted.GetProperty("SequenceNumber").GetInt64() == before+1);
                await a.Receive("StateSnapshot"); await b.Receive("StateSnapshot");
                Check(a.Sequence == b.Sequence && a.Sequence == before+1);
                for (int i=0; i<2; ++i)
                {
                    var pa=a.State.GetProperty("Players")[i]; var pb=b.State.GetProperty("Players")[i];
                    foreach(var key in new[]{"Life","HandCount","DeckCount","Field","Graveyard"})
                        Check(pa.GetProperty(key).GetRawText() == pb.GetProperty(key).GetRawText());
                }
            }
            async Task End(Peer peer) { await Command(peer,"AdvancePhase"); await Command(peer,"AdvancePhase"); await Command(peer,"EndTurn"); }
            await Command(a,"PlayCard",1); await End(a);
            await Command(b,"StartTurn"); await Command(b,"PlayCard",3); await End(b);
            await Command(a,"StartTurn"); await Command(a,"AdvancePhase"); await Command(a,"Attack",1,3);
            await Command(a,"AdvancePhase"); await Command(a,"EndTurn");
            await Command(b,"StartTurn"); await End(b);
            await Command(a,"StartTurn"); await Command(a,"AdvancePhase"); await Command(a,"DirectAttack",1);
            Check((await a.Receive("MatchEnded")).GetProperty("WinnerPlayerId").GetInt32() == 1);
            Check((await b.Receive("MatchEnded")).GetProperty("WinnerPlayerId").GetInt32() == 1);
            var finalState = a.State.GetRawText(); a.Dispose(); await Task.Delay(100);
            using var reconnected = new Peer(port); await reconnected.Ready();
            await reconnected.Send(new { Type="JoinMatch", ProtocolVersion=1, RequestId="reconnect", MatchId="local-1", SessionToken=token });
            Check((await reconnected.Receive("MatchJoined")).GetProperty("PlayerId").GetInt32() == 1);
            await reconnected.Receive("StateSnapshot"); Check(finalState == reconnected.State.GetRawText());
            Console.WriteLine("Unity transport source: two real C# WebSocket clients, full battle/victory, public state agreement and reconnect passed");
        }
        finally { if (!server.HasExited) { server.Kill(); await server.WaitForExitAsync(); } }
    }
}
