#if UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ProjectC.Tests
{
    public sealed class BattlefieldOnlinePlayModeTests
    {
        private Process _server;
        private GameObject _peer;
        private MultiplayerDebugScreen _client;

        [TearDown]
        public void Cleanup()
        {
            _client?.DisconnectNetwork();
            if (_peer != null)
            {
                Object.DestroyImmediate(_peer);
            }
            if (_server != null)
            {
                if (!_server.HasExited)
                {
                    _server.Kill();
                }
                _server.Dispose();
                _server = null;
            }
        }

        [UnityTest]
        public IEnumerator SceneAllowsInvalidDropSummonAttackPhaseAndReconnect()
        {
            BattlefieldScenePlayModeTests.SetResolution();
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
            string data = Path.Combine(Path.GetDirectoryName(executable), "TestData");
            _server = Process.Start(new ProcessStartInfo(executable, "--port " + port + " --mulligan off --data \"" + data + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
            var startup = _server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Single));
            var ui = Object.FindFirstObjectByType<BattlePresenter>();
            _client = ui.Client;
            _peer = new GameObject("Stage online opponent", typeof(MultiplayerDebugScreen));
            var other = _peer.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            _client.ConnectTo(address);
            other.ConnectTo(address);
            yield return Until(() => _client.Connected && other.Connected && _client.Decks.Length > 0 && other.Decks.Length > 0);
            _client.CreateMatch();
            yield return Until(() => _client.State != null);
            other.JoinMatch(_client.CurrentMatch);
            yield return Until(() => other.State != null);
            _client.SetReady("Strikers");
            other.SetReady("Wardens");
            yield return Until(() => _client.State?.Started == true && other.State?.Started == true && !ui.PresentationBusy);
            yield return null;
            int attacker = _client.State.Players[0].Hand[0].Id;
            var hand = ui.Hand.Widgets.Find(widget => widget.Card.Id == attacker);
            hand.OnPointerClick(null);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.IsFalse(ui.Hand.Layout.Compact);
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, hand.WorldCenter), button = PointerEventData.InputButton.Left };
            long sequence = _client.Sequence;
            hand.OnBeginDrag(pointer);
            pointer.position = new Vector2(Screen.width + 500, Screen.height + 500);
            hand.OnDrag(pointer);
            hand.OnEndDrag(pointer);
            yield return new WaitForSecondsRealtime(.5f);
            Assert.AreEqual(sequence, _client.Sequence);
            Assert.IsTrue(ui.Hand.Widgets.Any(widget => widget.Card.Id == attacker));
            yield return Drag(ui, attacker, false);
            Assert.IsTrue(_client.State.Players[0].Field.Any(card => card.Id == attacker));
            yield return Send(_client, "EndTurn");
            yield return Until(() => other.Sequence == _client.Sequence);
            int defender = other.State.Players[1].Hand[0].Id;
            yield return Send(other, "PlayCard", defender);
            yield return Send(other, "EndTurn");
            yield return Until(() => _client.State.ActivePlayerId == _client.PlayerId && !ui.PresentationBusy);
            long beforePhase = _client.Sequence;
            ui.EndTurn.onClick.Invoke();
            yield return Until(() => _client.Sequence > beforePhase && !ui.PresentationBusy);
            Assert.AreEqual((int)BattlePhaseKind.Battle, _client.State.Phase);
            int hp = _client.State.Players[1].Field.Sum(card => card.Hp);
            yield return Drag(ui, attacker, true);
            Assert.Less(_client.State.Players[1].Field.Sum(card => card.Hp), hp);
            yield return BattlefieldScenePlayModeTests.Save("YokaiShrine-Online-1920.png");
            var previous = _client.State;
            _client.DisconnectNetwork();
            yield return Until(() => !_client.Connected);
            _client.ReconnectNetwork();
            yield return Until(() => _client.Connected && _client.State != previous && !ui.PresentationBusy);
            Assert.IsNotNull(GameObject.Find("YokaiShrineStage"));
            Assert.IsNotNull(ui.OwnField.Widgets.Find(widget => widget.Card.Id == attacker));
            var empty = SceneManager.CreateScene("StageOnlineCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("BattleScene");
        }

        private IEnumerator Drag(BattlePresenter ui, int id, bool attack)
        {
            yield return Until(() => !ui.PresentationBusy && ui.Interaction.State == BattleInputState.Idle);
            var widget = (attack ? ui.OwnField : ui.Hand).Widgets.Find(card => card.Card.Id == id);
            var pointer = new PointerEventData(EventSystem.current)
            { position = RectTransformUtility.WorldToScreenPoint(null, widget.WorldCenter), button = PointerEventData.InputButton.Left };
            long sequence = _client.Sequence;
            widget.OnBeginDrag(pointer);
            Assert.IsNotNull(attack ? (object)ui.Interaction.Arrow : ui.Interaction.Ghost);
            Vector3 destination = attack ? ui.EnemyField.Widgets[0].WorldCenter
                : ui.OwnField.transform.TransformPoint(((RectTransform)ui.OwnField.transform).rect.center);
            pointer.position = RectTransformUtility.WorldToScreenPoint(null, destination);
            widget.OnDrag(pointer);
            widget.OnEndDrag(pointer);
            Assert.AreEqual(BattleInputState.AwaitingServerResponse, ui.Interaction.State);
            yield return Until(() => _client.Sequence > sequence && !_client.Busy && !ui.PresentationBusy);
            yield return null;
        }

        private static IEnumerator Send(MultiplayerDebugScreen client, string command, int card = 0)
        {
            long sequence = client.Sequence;
            Assert.IsTrue(client.SendGameCommand(command, card, 0));
            yield return Until(() => client.Sequence > sequence && !client.Busy);
        }

        private static IEnumerator Until(Func<bool> ready)
        {
            float deadline = Time.realtimeSinceStartup + 12;
            while (!ready())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "Stage network condition timed out");
                yield return null;
            }
        }
    }
}
#endif
