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
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class YakouOnlinePlayModeTests
    {
        private const float TimeoutSeconds = 15f;
        private Process _server;
        private GameObject _first;
        private GameObject _second;

        [TearDown]
        public void Cleanup()
        {
            if (_first != null)
            {
                Object.DestroyImmediate(_first);
            }
            if (_second != null)
            {
                Object.DestroyImmediate(_second);
            }
            if (_server != null)
            {
                if (!_server.HasExited)
                {
                    _server.Kill();
                }
                _server.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator TwoUnityTransportsPlayReconnectAndKeepHandsPrivate()
        {
            string executable = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if (args[i] == "-projectcServerPath")
                {
                    executable = args[i + 1];
                }
            }
            Assert.IsTrue(File.Exists(executable), "Pass -projectcServerPath");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            _server = Process.Start(new ProcessStartInfo(executable, "--port " + port + " --yakou-prototype on")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            });
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            _first = new GameObject("Yakou client one", typeof(MultiplayerDebugScreen));
            _second = new GameObject("Yakou client two", typeof(MultiplayerDebugScreen));
            var a = _first.GetComponent<MultiplayerDebugScreen>();
            var b = _second.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            a.ConnectTo(address);
            b.ConnectTo(address);
            yield return Until(() => a.ConnectionReady && b.ConnectionReady);
            a.Yakou.Create();
            yield return Until(() => a.Yakou.State != null);
            b.Yakou.Join(a.Yakou.MatchId);
            yield return Until(() => b.Yakou.State != null);
            Assert.AreEqual(5, ((JArray)a.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)a.Yakou.State["Players"][1]["Hand"]).Count);
            Assert.AreEqual(0, ((JArray)b.Yakou.State["Players"][0]["Hand"]).Count);
            Assert.AreEqual(5, ((JArray)b.Yakou.State["Players"][1]["Hand"]).Count);
            b.Yakou.Command(new JObject { ["Type"] = "StartTurn" });
            yield return Until(() => !b.Yakou.Busy);
            Assert.AreEqual("Rejected", b.Yakou.Error);
            Assert.AreEqual(0, b.Yakou.Sequence);
            yield return Send(a, new JObject { ["Type"] = "StartTurn" });
            var choices = (JArray)a.Yakou.State["Players"][0]["SummonChoices"];
            JToken normal = null;
            foreach (var choice in choices)
            {
                if ((string)choice["Method"] == "Normal")
                {
                    normal = choice;
                    break;
                }
            }
            Assert.IsNotNull(normal);
            int card = (int)normal["CardId"];
            yield return Send(a, new JObject { ["Type"] = "Summon", ["CardId"] = card,
                ["Method"] = "Normal", ["Materials"] = new JArray(), ["Slot"] = 0 });
            Assert.AreEqual(card, (int)a.Yakou.State["Players"][0]["FieldSlots"][0]["Id"]);
            a.DisconnectNetwork();
            Assert.IsFalse(a.Yakou.Bound);
            a.ReconnectNetwork();
            yield return Until(() => a.Yakou.Bound && a.Yakou.State != null);
            Assert.AreEqual(card, (int)a.Yakou.State["Players"][0]["FieldSlots"][0]["Id"]);
            Assert.AreEqual(0, ((JArray)a.Yakou.State["Players"][1]["Hand"]).Count);
            yield return Send(a, new JObject { ["Type"] = "BeginRaid" });
            yield return Until(() => b.Yakou.Sequence == a.Yakou.Sequence);
            for (int turns = 0; (string)a.Yakou.State["Phase"] != "AssignBlockers" && turns < 8; ++turns)
            {
                var active = (int)a.Yakou.State["ActivePlayerId"] == 1 ? a : b;
                var other = active == a ? b : a;
                yield return Send(active, new JObject { ["Type"] = "StartTurn" });
                yield return Send(active, new JObject { ["Type"] = "BeginRaid" });
                yield return Until(() => other.Yakou.Sequence == active.Yakou.Sequence);
            }
            Assert.AreEqual("AssignBlockers", (string)a.Yakou.State["Phase"]);
            yield return Send(b, new JObject { ["Type"] = "AssignBlockers", ["Blockers"] = new JArray() });
            yield return Send(b, new JObject { ["Type"] = "PassRaidResponse" });
            yield return Send(a, new JObject { ["Type"] = "PassRaidResponse" });
            yield return Send(b, new JObject { ["Type"] = "ResolveRaid" });
            Assert.AreEqual("TurnStart", (string)b.Yakou.State["Phase"]);
            Assert.AreEqual(2, (int)b.Yakou.State["ActivePlayerId"]);
            b.Yakou.Leave();
            yield return Until(() => a.Yakou.MatchId == null && b.Yakou.MatchId == null);
        }

        private static IEnumerator Send(MultiplayerDebugScreen client, JObject command)
        {
            long sequence = client.Yakou.Sequence;
            client.Yakou.Command(command);
            yield return Until(() => !client.Yakou.Busy);
            Assert.AreEqual(sequence + 1, client.Yakou.Sequence, client.Yakou.Error);
        }

        private static IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Yakou network condition timed out");
                yield return null;
            }
        }
    }
}
#endif
