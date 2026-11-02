#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public partial class MultiplayerPlayModeTests
    {
        [UnityTest]
        public IEnumerator DrawPresentationPreservesServerDragAttackAndRematch()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../build/Debug/ProjectCNetworkServer.exe"));
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if (args[i] == "-projectcServerPath")
                {
                    path = args[i + 1];
                }
            }
            string source = Path.Combine(Path.GetDirectoryName(path), "TestData");
            string data = Path.Combine(Application.temporaryCachePath, "ProjectC-DrawCombat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            File.Copy(Path.Combine(source, "Cards.json"), Path.Combine(data, "Cards.json"));
            var decks = JObject.Parse(File.ReadAllText(Path.Combine(source, "Decks.json")));
            decks["Config"]["DrawPerTurn"] = 1;
            foreach (var deck in decks["Decks"])
            {
                var cards = (JArray)deck["Cards"];
                var additionalCards = (JArray)cards.DeepClone();
                foreach (var card in additionalCards)
                {
                    cards.Add(card.DeepClone());
                }
            }
            File.WriteAllText(Path.Combine(data, "Decks.json"), decks.ToString());
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                server = Process.Start(new ProcessStartInfo(path, "--port " + port + " --data \"" + data + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                var startup = server.StandardOutput.ReadLineAsync();
                yield return Until(() => startup.IsCompleted);
                StringAssert.Contains("listening", startup.Result);
                var a = Screen(true);
                var b = Screen();
                string address = "ws://127.0.0.1:" + port;
                a.ConnectTo(address);
                b.ConnectTo(address);
                yield return Until(() => a.Connected && b.Connected && a.Decks.Length > 0 && b.Decks.Length > 0);
                a.CreateMatch();
                yield return Until(() => a.State != null);
                b.JoinMatch(a.CurrentMatch);
                yield return Until(() => b.State != null);
                a.SetReady("Strikers");
                b.SetReady("Wardens");
                yield return Until(() => a.State?.Started == true && b.State?.Started == true);
                var ui = a.GetComponent<BattlePresenter>();
                yield return null;
                yield return Until(() => !ui.PresentationBusy);
                int attacker = a.State.Players[0].Hand[0].Id;
                int defender = b.State.Players[1].Hand[0].Id;
                yield return Command(a, "PlayCard", attacker);
                yield return Command(a, "EndTurn");
                yield return Until(() => b.Sequence == a.Sequence && !ui.PresentationBusy);
                yield return Command(b, "PlayCard", defender);
                yield return Command(b, "EndTurn");
                yield return Until(() => a.Sequence == b.Sequence && !ui.PresentationBusy);
                var attackerWidget = ui.OwnField.Widgets.Find(w => w.Card?.Id == attacker);
                yield return Command(a, "AdvancePhase");
                yield return Command(a, "Attack", attacker, defender);
                yield return Command(a, "EndTurn");
                yield return Until(() => b.Sequence == a.Sequence && !ui.PresentationBusy);
                yield return Command(b, "EndTurn");
                yield return Until(() => a.Sequence == b.Sequence && !ui.PresentationBusy);
                yield return Command(a, "AdvancePhase");
                Assert.AreSame(attackerWidget, ui.OwnField.Widgets.Find(w => w.Card?.Id == attacker));
                yield return Command(a, "DirectAttack", attacker);
                yield return Until(() => a.State.Finished && b.State.Finished);
                Assert.Greater(ui.Effects.DrawsPresented, 0);
                Assert.IsNull(ui.Interaction.Ghost);
                a.RequestRematch();
                b.RequestRematch();
                yield return Until(() => a.GameNumber == 2 && b.GameNumber == 2 && a.State != null);
                yield return null;
                Assert.AreEqual(0, ui.Effects.ActiveDraws);
                Assert.AreEqual(a.State.Players[0].Hand.Length, ui.Hand.Widgets.Count);
            }
            finally
            {
                // The task-specific fixture directory is the only generated data removed.
                if (Directory.Exists(data))
                {
                    Assert.AreEqual(
                        Path.GetFullPath(Application.temporaryCachePath).TrimEnd(Path.DirectorySeparatorChar),
                        Directory.GetParent(Path.GetFullPath(data)).FullName);
                    Directory.Delete(data, true);
                }
            }
        }
    }
}
#endif
