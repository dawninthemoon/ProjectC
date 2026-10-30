using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ProjectC.Client;

namespace ProjectC.Tests
{
    public class MultiplayerPlayModeTests
    {
        Process server;
        readonly System.Collections.Generic.List<GameObject> objects = new System.Collections.Generic.List<GameObject>();
        MultiplayerDebugScreen Screen(bool withUi = false)
        {
#if UNITY_EDITOR
            if (withUi)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab");
                Assert.IsNotNull(prefab, "Authored BattleScreen prefab is required");
                var ui = UnityEngine.Object.Instantiate(prefab); objects.Add(ui); return ui.GetComponent<MultiplayerDebugScreen>();
            }
#endif
            var obj = new GameObject("Multiplayer test client"); objects.Add(obj); return obj.AddComponent<MultiplayerDebugScreen>();
        }
        static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Multiplayer condition timed out");
                yield return null;
            }
        }
        static IEnumerator Command(MultiplayerDebugScreen client, string type, int card = 0, int target = 0)
        {
            long sequence = client.Sequence;
            var ui = client.GetComponent<BattlePresenter>();
            if (ui == null) client.SendGameCommand(type, card, target);
            else
            {
                yield return null; // snapshot bindings are applied by LateUpdate
                if (card != 0)
                {
                    var zone = type == "PlayCard" ? ui.Hand : ui.OwnField;
                    var widget = zone.Widgets.Find(w => w.Card != null && w.Card.Id == card);
                    Assert.IsNotNull(widget); widget.OnPointerClick(null); yield return null;
                }
                if (target != 0) { ui.EnemyField.Widgets.Find(w => w.Card != null && w.Card.Id == target).OnPointerClick(null); yield return null; }
                var button = type == "PlayCard" ? ui.Summon : type == "AdvancePhase" ? ui.BattlePhase : type == "EndTurn" ? ui.EndTurn : type == "Attack" ? ui.Attack : ui.DirectAttack;
                Assert.IsTrue(button.interactable, type + " button must be usable"); button.onClick.Invoke();
            }
            yield return Until(() => client.Sequence == sequence + 1);
        }
        [TearDown] public void Cleanup()
        {
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
            if (server != null) { if (!server.HasExited) { server.Kill(); server.WaitForExit(5000); } server.Dispose(); server = null; }
        }
        [UnityTest] public IEnumerator FourClientsTwoMatchesPlayRematchAndReconnect()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/Debug/ProjectCNetworkServer.exe"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i) if (args[i] == "-projectcServerPath") path = args[i + 1];
            Assert.IsTrue(File.Exists(path), "Build the C++ server or pass -projectcServerPath");
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            server = Process.Start(new ProcessStartInfo(path, "--port " + port) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var startup = server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            var a = Screen(true); var b = Screen(); var c = Screen(); var d = Screen();
            string address = "ws://127.0.0.1:" + port;
            foreach (var client in new[] { a, b, c, d }) client.ConnectTo(address);
            yield return Until(() => a.Connected && b.Connected && c.Connected && d.Connected);
            // Let Update consume the connection notices and deck catalog before issuing UI actions.
            yield return null;
            a.CreateMatch(); c.CreateMatch();
            yield return Until(() => a.State != null && c.State != null);
            b.JoinMatch(a.CurrentMatch); d.JoinMatch(c.CurrentMatch);
            yield return Until(() => b.State != null && d.State != null);
            Assert.AreNotEqual(a.CurrentMatch, c.CurrentMatch);
            a.SetReady("Strikers"); b.SetReady("Wardens"); c.SetReady("Wardens"); d.SetReady("Strikers");
            yield return Until(() => a.State != null && a.State.Started && b.State != null && b.State.Started && c.State != null && c.State.Started && d.State != null && d.State.Started);
            yield return null;
            var ui = a.GetComponent<BattlePresenter>();
            Assert.AreEqual(5, ui.OwnField.Widgets.Count);
            Assert.IsFalse(ui.LobbyOverlay.activeSelf);
            Assert.AreEqual("YOUR TURN", ui.TurnLabel.text);
            foreach (var back in ui.EnemyHand.Widgets) { Assert.IsTrue(back.Hidden); Assert.IsNull(back.Card); }
            var handCard = ui.Hand.Widgets[0]; handCard.OnPointerEnter(null);
            Assert.AreEqual(handCard.Card.Name, ui.Detail.Title.text);
            handCard.OnPointerExit(null);
            Assert.AreEqual(0, a.State.Players[1].Hand.Length); Assert.AreEqual(0, c.State.Players[1].Hand.Length);
            int aCard = a.State.Players[0].Hand[0].Id, bCard = b.State.Players[1].Hand[0].Id, cCard = c.State.Players[0].Hand[0].Id;
            yield return Command(c, "PlayCard", cCard);
            Assert.AreEqual(3, c.State.Players[0].Field[0].Attack);
            Assert.AreEqual(1, a.Sequence);
            yield return Command(a, "PlayCard", aCard); yield return Command(a, "EndTurn");
            yield return Until(() => b.Sequence == a.Sequence);
            Assert.IsTrue(b.State.TurnStarted);
            yield return Command(b, "PlayCard", bCard); yield return Command(b, "EndTurn");
            yield return Until(() => a.Sequence == b.Sequence);
            yield return Command(a, "AdvancePhase"); yield return Command(a, "Attack", aCard, bCard);
            yield return Command(a, "EndTurn"); yield return Until(() => b.Sequence == a.Sequence);
            yield return Command(b, "EndTurn"); yield return Until(() => a.Sequence == b.Sequence);
            yield return Command(a, "AdvancePhase"); yield return Command(a, "DirectAttack", aCard);
            yield return Until(() => b.State.Finished);
            Assert.IsTrue(a.State.Finished); Assert.AreEqual(2, c.Sequence);
            yield return null;
            Assert.IsTrue(ui.ResultPanel.activeSelf); Assert.AreEqual("VICTORY", ui.ResultLabel.text);
            ui.ResultRematch.onClick.Invoke(); b.RequestRematch();
            yield return Until(() => a.GameNumber == 2 && b.GameNumber == 2 && a.State != null && b.State != null);
            Assert.AreEqual(1, a.Sequence); Assert.AreEqual(0, a.State.Players[0].Field.Length); Assert.AreEqual(3, a.State.Players[0].Life);
            var old = c.State; c.DisconnectNetwork(); yield return new WaitForSecondsRealtime(.2f); c.ConnectTo(address);
            yield return Until(() => c.Connected && c.State != null && c.State != old);
            Assert.AreEqual(2, c.Sequence); Assert.AreEqual(cCard, c.State.Players[0].Field[0].Id);
            UnityEngine.Debug.Log("Four Unity PlayMode clients: independent matches, effects, automatic turns, victory, rematch and reconnect passed");
        }
        [UnityTest] public IEnumerator RealServerDrawUsesOwnHandAndOpponentBacks()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/Debug/ProjectCNetworkServer.exe"));
            var args = Environment.GetCommandLineArgs();
            for (int i=0;i+1<args.Length;++i) if(args[i]=="-projectcServerPath") path=args[i+1];
            string source=Path.Combine(Path.GetDirectoryName(path),"Data");
            string data=Path.Combine(Application.temporaryCachePath,"ProjectC-DrawUi-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            File.Copy(Path.Combine(source,"Cards.json"),Path.Combine(data,"Cards.json"));
            string decks=File.ReadAllText(Path.Combine(source,"Decks.json"));
            decks=System.Text.RegularExpressions.Regex.Replace(decks,@"(""DrawPerTurn""\s*:\s*)0","${1}1");
            File.WriteAllText(Path.Combine(data,"Decks.json"),decks);
            var listener=new TcpListener(IPAddress.Loopback,0); listener.Start(); int port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            server=Process.Start(new ProcessStartInfo(path,"--port "+port+" --data \""+data+"\"") {UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});
            var startup=server.StandardOutput.ReadLineAsync(); yield return Until(()=>startup.IsCompleted);
            StringAssert.Contains("listening",startup.Result);
            var a=Screen(true); var b=Screen(); string address="ws://127.0.0.1:"+port;
            a.ConnectTo(address); b.ConnectTo(address); yield return Until(()=>a.Connected && b.Connected); yield return null;
            a.CreateMatch(); yield return Until(()=>a.State!=null); b.JoinMatch(a.CurrentMatch); yield return Until(()=>b.State!=null);
            a.SetReady("Wardens"); b.SetReady("Strikers"); yield return Until(()=>a.State!=null && a.State.Started && b.State!=null && b.State.Started);
            var ui=a.GetComponent<BattlePresenter>(); yield return null;
            Assert.AreEqual(3,a.State.Players[0].HandCount); Assert.AreEqual(3,ui.Hand.Widgets.Count);
            yield return Command(a,"PlayCard",a.State.Players[0].Hand[0].Id);
            yield return Command(a,"EndTurn"); yield return Until(()=>b.Sequence==a.Sequence); yield return null;
            Assert.AreEqual(3,a.State.Players[1].HandCount); Assert.AreEqual(0,a.State.Players[1].Hand.Length);
            Assert.AreEqual(3,ui.EnemyHand.Widgets.Count); foreach(var back in ui.EnemyHand.Widgets) Assert.IsNull(back.Card);
            yield return Command(b,"EndTurn"); yield return Until(()=>a.Sequence==b.Sequence); yield return null;
            Assert.AreEqual(3,a.State.Players[0].HandCount);
            yield return new WaitForSecondsRealtime(2);
            Assert.GreaterOrEqual(ui.Effects.DrawsPresented,2); Assert.Greater(ui.Effects.PopupsPresented,0);
            Assert.AreEqual(3,ui.OwnField.Widgets[0].Card.Attack);
            // Only generated fixture files are removed; no recursive directory operations.
            File.Delete(Path.Combine(data,"Cards.json")); File.Delete(Path.Combine(data,"Decks.json")); Directory.Delete(data);
        }
        [UnityTest] public IEnumerator OldEpochAndForeignMatchSnapshotsAreIgnored()
        {
            var client = Screen();
            Assert.IsTrue(Application.runInBackground);
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"PlayerId\":1,\"GameNumber\":1}");
            client.ApplyMessage("{\"Type\":\"StateSnapshot\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"GameNumber\":1,\"SequenceNumber\":10,\"Payload\":{\"TurnNumber\":5,\"Players\":[]}}");
            Assert.AreEqual(10, client.Sequence);
            client.ApplyMessage("{\"Type\":\"SessionUpdated\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"GameNumber\":2}");
            Assert.IsNull(client.State); Assert.AreEqual(0, client.Sequence);
            client.ApplyMessage("{\"Type\":\"StateSnapshot\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"GameNumber\":2,\"SequenceNumber\":1,\"Payload\":{\"TurnNumber\":1,\"Players\":[]}}");
            var current = client.State;
            client.ApplyMessage("{\"Type\":\"StateSnapshot\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"GameNumber\":1,\"SequenceNumber\":100,\"Payload\":{}}");
            client.ApplyMessage("{\"Type\":\"StateSnapshot\",\"ProtocolVersion\":2,\"MatchId\":\"B\",\"GameNumber\":3,\"SequenceNumber\":100,\"Payload\":{}}");
            Assert.AreSame(current, client.State); Assert.AreEqual(1, client.Sequence);
            client.ApplyMessage("{\"Type\":\"ServerError\",\"ProtocolVersion\":2,\"MatchId\":\"A\",\"Reason\":\"MatchNotFound\"}");
            Assert.AreEqual("", client.CurrentMatch); Assert.IsNull(client.State);
            yield return null;
        }
    }
}

