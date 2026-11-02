#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Game.Utils;

namespace ProjectC.Tests
{
    public sealed class FrontEndPlayModeTests
    {
        string directory;
        System.Diagnostics.Process server;
        GameObject opponentObject;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            directory = Path.Combine(
                Application.temporaryCachePath,
                "FrontEnd-" + Guid.NewGuid().ToString("N")
            );
            ClientApp.StorageOverride = directory;
            yield return SceneManager.LoadSceneAsync("LobbyScene");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (opponentObject != null)
                UnityEngine.Object.Destroy(opponentObject);
            if (server != null)
            {
                if (!server.HasExited)
                {
                    server.Kill();
                    server.WaitForExit(5000);
                }
                server.Dispose();
                server = null;
            }
            if (ClientApp.Instance != null)
                UnityEngine.Object.Destroy(ClientApp.Instance.gameObject);
            var current = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("TestEmpty-" + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(current);
            ClientApp.StorageOverride = null;
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.GetFiles(directory))
                    File.Delete(file);
                Directory.Delete(directory);
            }
        }

        static Button Button(MonoBehaviour view, string name)
        {
            Assert.IsTrue(view.Bind(name, out Button button));
            return button;
        }

        static IEnumerator Until(Func<bool> condition)
        {
            float end = Time.realtimeSinceStartup + 15;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, end, "Scene condition timed out");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator ActualScenesEditSaveWarningDeleteAndRestore()
        {
            var app = ClientApp.Instance;
            Assert.IsNotNull(app);
            var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyScenePresenter>();
            yield return Capture(lobby.GetComponent<Canvas>(), "LobbyScene.png");
            Button(lobby, "DeckManager").onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == "DeckEditorScene");
            yield return null;
            var ui = UnityEngine.Object.FindFirstObjectByType<DeckEditorPresenter>();
            Assert.AreSame(app, ClientApp.Instance);
            Button(ui, "ImportStarter").onClick.Invoke();
            Assert.AreEqual(30, ui.Session.Draft.Cards.Count);
            Assert.IsFalse(ui.Session.Dirty);
            yield return Capture(ui.GetComponent<Canvas>(), "DeckEditorScene.png");
            ui.Remove(1001);
            Assert.IsTrue(ui.Session.Dirty);
            ui.Add(1007);
            Assert.AreEqual(29, ui.Session.Draft.Cards.Count);
            ui.Add(1001);
            Assert.AreEqual(30, ui.Session.Draft.Cards.Count);
            ui.Add(1001);
            Assert.AreEqual(30, ui.Session.Draft.Cards.Count);
            ui.NameInput.text = "나의 백귀야행";
            Button(ui, "Back").onClick.Invoke();
            Assert.IsTrue(ui.Dialog.gameObject.activeSelf);
            Button(ui.Dialog, "Dismiss").onClick.Invoke();
            Assert.AreEqual("DeckEditorScene", SceneManager.GetActiveScene().name);
            Assert.IsTrue(ui.Session.Dirty);
            Button(ui, "Save").onClick.Invoke();
            Assert.IsFalse(ui.Session.Dirty);
            Assert.AreEqual("나의 백귀야행", app.Decks.Selected.Name);
            Button(ui, "CopyDeck").onClick.Invoke();
            Assert.AreEqual(2, app.Decks.Decks.Count);
            string copy = app.Decks.SelectedId;
            Button(ui, "DeleteDeck").onClick.Invoke();
            Assert.IsTrue(ui.Dialog.gameObject.activeSelf);
            Button(ui.Dialog, "Dismiss").onClick.Invoke();
            Assert.AreEqual(2, app.Decks.Decks.Count);
            Button(ui, "DeleteDeck").onClick.Invoke();
            Button(ui.Dialog, "Confirm").onClick.Invoke();
            Assert.AreEqual(1, app.Decks.Decks.Count);
            Assert.IsNull(app.Decks.Find(copy));
            ui.Search.text = "등불";
            Assert.AreEqual(1, ui.LibraryContent.GetComponentsInChildren<DeckTileView>().Length);
            ui.Search.text = "";
            ui.Remove(1001);
            Button(ui, "CancelEdits").onClick.Invoke();
            Button(ui.Dialog, "Alternative").onClick.Invoke();
            Assert.AreEqual(30, ui.Session.Draft.Cards.Count);
            Assert.IsFalse(ui.Session.Dirty);
            Button(ui, "NewDeck").onClick.Invoke();
            Assert.AreEqual(0, ui.Session.Draft.Cards.Count);
            Button(ui, "Save").onClick.Invoke();
            Assert.IsNotNull(app.Decks.Selected);
            ui.DeckList.value = 0;
            Assert.AreEqual("나의 백귀야행", ui.Session.Draft.Name);
            Button(ui, "Back").onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == "LobbyScene");
            yield return null;
            string selected = app.Decks.SelectedId;
            UnityEngine.Object.Destroy(app.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync("LobbyScene");
            yield return null;
            Assert.AreEqual(selected, ClientApp.Instance.Decks.SelectedId);
            Assert.AreEqual(30, ClientApp.Instance.Decks.Selected.Cards.Count);
        }

        [UnityTest]
        public IEnumerator LobbyReadyBattleVictoryAndReturnKeepConnection()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../build/Debug/ProjectCNetworkServer.exe")
            );
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-projectcServerPath")
                    path = args[i + 1];
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            server = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    path,
                    "--port "
                        + port
                        + " --data \""
                        + Path.Combine(Path.GetDirectoryName(path), "TestData")
                        + "\""
                )
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                }
            );
            var started = server.StandardOutput.ReadLineAsync();
            yield return Until(() => started.IsCompleted);
            StringAssert.Contains("listening", started.Result);
            var app = ClientApp.Instance;
            var a = app.Network;
            a.UsePersonalDeck = false;
            opponentObject = new GameObject("Opponent", typeof(MultiplayerDebugScreen));
            UnityEngine.Object.DontDestroyOnLoad(opponentObject);
            var b = opponentObject.GetComponent<MultiplayerDebugScreen>();
            var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyScenePresenter>();
            Button(lobby, "BattleMenu").onClick.Invoke();
            lobby.Bind("MatchPanel", out LobbyPanelView panel);
            panel.Address.text = "ws://127.0.0.1:" + port;
            panel.Connect.onClick.Invoke();
            b.ConnectTo(panel.Address.text);
            yield return Until(() => a.Connected && b.Connected && a.Decks.Length > 0);
            yield return null;
            panel.Create.onClick.Invoke();
            yield return Until(() => a.State != null);
            b.JoinMatch(a.CurrentMatch);
            yield return Until(() => b.State != null);
            yield return null;
            panel.Ready.onClick.Invoke();
            b.SetReady("Strikers");
            yield return Until(() => SceneManager.GetActiveScene().name == "BattleScene");
            yield return null;
            var battle = UnityEngine.Object.FindFirstObjectByType<BattlePresenter>();
            Assert.AreSame(a, battle.Client);
            Assert.AreSame(app, ClientApp.Instance);
            Assert.IsTrue(a.Connected);
            int card = a.State.Players[0].Hand[0].Id;
            yield return Command(a, b, "PlayCard", card);
            yield return Command(a, b, "EndTurn");
            yield return Command(b, a, "EndTurn");
            yield return Command(a, b, "AdvancePhase");
            yield return Command(a, b, "DirectAttack", card);
            Assert.IsTrue(a.State.Finished);
            yield return null;
            Assert.IsTrue(battle.ResultPanel.activeSelf);
            battle.ResultLeave.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == "LobbyScene");
            yield return null;
            Assert.AreSame(a, ClientApp.Instance.Network);
            Assert.IsTrue(a.Connected);
            Assert.IsEmpty(a.CurrentMatch);
        }

        [UnityTest]
        public IEnumerator PersonalDecksSubmitFreezeReconnectAndReject()
        {
            string path = "";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-projectcServerPath")
                    path = args[i + 1];
            Assert.IsTrue(File.Exists(path), "Pass -projectcServerPath");
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            server = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    path,
                    "--port "
                        + port
                        + " --data \""
                        + Path.Combine(Path.GetDirectoryName(path), "Data", "HyakkiYagyo")
                        + "\""
                )
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            );
            var started = server.StandardOutput.ReadLineAsync();
            yield return Until(() => started.IsCompleted);
            StringAssert.Contains("listening", started.Result);
            var app = ClientApp.Instance;
            var a = app.Network;
            opponentObject = new GameObject("Personal opponent", typeof(MultiplayerDebugScreen));
            UnityEngine.Object.DontDestroyOnLoad(opponentObject);
            var b = opponentObject.GetComponent<MultiplayerDebugScreen>();
            a.ConnectTo("ws://127.0.0.1:" + port);
            b.ConnectTo("ws://127.0.0.1:" + port);
            yield return Until(() =>
                a.Connected && b.Connected && a.Decks.Length > 0 && b.Decks.Length > 0
            );
            a.CreateMatch();
            yield return Until(() => a.State != null);
            b.JoinMatch(a.CurrentMatch);
            yield return Until(() => b.State != null);
            var panel = UnityEngine.Object.FindFirstObjectByType<LobbyPanelView>(
                FindObjectsInactive.Include
            );
            panel.Render();
            Assert.IsFalse(panel.Ready.interactable, "Empty personal deck blocks readiness");
            var deck = app.Starter.Copy();
            deck.Name = "개인 A";
            Assert.IsTrue(app.Decks.Save(deck, out _));
            panel.Render();
            Assert.IsTrue(panel.Ready.interactable);
            panel.Ready.onClick.Invoke();
            yield return Until(() => a.Lobby.Players.Any(p => p.PlayerId == a.PlayerId && p.Ready));
            var invalid = deck.Copy();
            invalid.Cards[0] = 999999;
            string notice = "";
            b.NoticeReceived += text => notice = text;
            Assert.IsTrue(b.SubmitPersonalDeck(invalid, app.Library.CatalogVersion));
            yield return Until(() => notice.Contains("DeckUnknownCard"));
            Assert.IsFalse(b.Busy);
            var second = deck.Copy();
            second.Name = "개인 B";
            second.Cards[0] = 2004;
            Assert.IsTrue(b.SubmitPersonalDeck(second, app.Library.CatalogVersion));
            second.Cards.Clear();
            yield return Until(() => a.State?.Started == true && b.State?.Started == true);
            yield return Until(() => SceneManager.GetActiveScene().name == "BattleScene");
            Assert.AreEqual("개인 A", a.ConfirmedDeckName);
            Assert.AreEqual("개인 B", b.ConfirmedDeckName);
            Assert.AreEqual(0, a.State.Players[1].Hand.Length);
            Assert.AreEqual(0, b.State.Players[0].Hand.Length);
            string before = JsonUtility.ToJson(a.State);
            deck.Cards.Clear();
            Assert.IsTrue(app.Decks.Save(deck, out _));
            Assert.AreEqual(before, JsonUtility.ToJson(a.State));
            a.DisconnectNetwork();
            yield return Until(() => b.Lobby.Players.Any(p => !p.Connected));
            a.ConnectTo("ws://127.0.0.1:" + port);
            yield return Until(() => a.Connected && a.Lobby.Players.All(p => p.Connected));
            Assert.AreEqual(before, JsonUtility.ToJson(a.State));
            yield return Command(a, b, "EndTurn");
        }

        [UnityTest]
        public IEnumerator MulliganSelectConfirmReconnectAndStart()
        {
            yield return MulliganFlow(false);
        }

        [UnityTest]
        public IEnumerator MulliganKeepClearsSelectionAndStarts()
        {
            yield return MulliganFlow(true);
        }

        IEnumerator MulliganFlow(bool keep)
        {
            string path = "";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
                if (args[i] == "-projectcServerPath")
                    path = args[i + 1];
            Assert.IsTrue(File.Exists(path), "Pass -projectcServerPath");
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            server = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    path,
                    "--port "
                        + port
                        + " --mulligan on --data \""
                        + Path.Combine(Path.GetDirectoryName(path), "Data", "HyakkiYagyo")
                        + "\""
                )
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                }
            );
            var startup = server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            var app = ClientApp.Instance;
            var a = app.Network;
            Assert.IsTrue(app.Decks.Save(app.Starter.Copy(), out _));
            opponentObject = new GameObject("Mulligan opponent", typeof(MultiplayerDebugScreen));
            UnityEngine.Object.DontDestroyOnLoad(opponentObject);
            var b = opponentObject.GetComponent<MultiplayerDebugScreen>();
            string address = "ws://127.0.0.1:" + port;
            a.ConnectTo(address);
            b.ConnectTo(address);
            yield return Until(() =>
                a.Connected && b.Connected && a.Decks.Length > 0 && b.Decks.Length > 0
            );
            a.CreateMatch();
            yield return Until(() => a.State != null);
            b.JoinMatch(a.CurrentMatch);
            yield return Until(() => b.State != null);
            a.SetReady(a.SelectedDeck);
            b.SetReady(b.SelectedDeck);
            yield return Until(() =>
                a.State?.MulliganActive == true && b.State?.MulliganActive == true
            );
            yield return Until(() => SceneManager.GetActiveScene().name == "BattleScene");
            yield return null;
            var battle = UnityEngine.Object.FindFirstObjectByType<BattlePresenter>();
            var ui = battle.Mulligan;
            Assert.AreEqual("교체 확정", ui.Confirm.GetComponentInChildren<Text>().text);
            Assert.AreEqual("그대로 시작", ui.Keep.GetComponentInChildren<Text>().text);
            Assert.IsTrue(ui.gameObject.activeSelf);
            Assert.IsFalse(battle.CanAct);
            Assert.IsFalse(battle.EndTurn.interactable);
            Assert.AreEqual(5, a.State.Players[0].Hand.Length);
            Assert.IsFalse(a.State.TurnStarted);
            var widget = ui.GetComponentsInChildren<CardWidget>()[0];
            int selected = widget.Card.Id;
            widget.Clicked(widget);
            Assert.Contains(selected, a.MulliganSelection.ToList());
            Assert.IsTrue(widget.IsSelected);
            widget.Clicked(widget);
            Assert.IsFalse(widget.IsSelected);
            widget.Clicked(widget);
            if (!keep)
                yield return Capture(battle.GetComponent<Canvas>(), "MulliganOverlay.png");
            var before = a.State;
            a.DisconnectNetwork();
            yield return Until(() => b.Lobby.Players.Any(p => !p.Connected));
            yield return null;
            Assert.IsTrue(ui.Reconnect.interactable);
            ui.Reconnect.onClick.Invoke();
            yield return Until(() => a.Connected && a.State != null && a.State != before);
            yield return null;
            Assert.Contains(selected, a.MulliganSelection.ToList());
            if (keep)
                ui.Keep.onClick.Invoke();
            else
                ui.Confirm.onClick.Invoke();
            yield return Until(() => a.State.Players[0].MulliganComplete);
            yield return null;
            Assert.IsTrue(ui.gameObject.activeSelf);
            Assert.IsFalse(ui.Confirm.interactable);
            Assert.IsFalse(a.State.TurnStarted);
            if (keep)
                Assert.AreEqual(0, a.State.Players[0].MulliganSelectedIds.Length);
            else
                Assert.IsFalse(a.State.Players[0].Hand.Any(c => c.Id == selected));
            before = a.State;
            a.DisconnectNetwork();
            yield return Until(() => b.Lobby.Players.Any(p => !p.Connected));
            a.ConnectTo(address);
            yield return Until(() => a.Connected && a.State != null && a.State != before);
            Assert.IsTrue(a.State.Players[0].MulliganComplete);
            var replaced = b.State.Players[1].Hand.Select(c => c.Id).ToArray();
            Assert.IsTrue(b.SubmitMulligan(replaced));
            yield return Until(() => !a.State.MulliganActive && !b.State.MulliganActive);
            yield return null;
            Assert.IsFalse(ui.gameObject.activeSelf);
            Assert.IsTrue(battle.CanAct);
            Assert.IsTrue(a.State.TurnStarted);
            Assert.AreEqual(6, a.State.Players[0].Hand.Length);
            Assert.AreEqual(2, a.State.Players[0].YokaiPower);
            Assert.IsFalse(b.State.Players[1].Hand.Any(c => replaced.Contains(c.Id)));
            Assert.IsEmpty(a.State.Players[1].Hand);
            Assert.IsEmpty(a.State.Players[1].MulliganSelectedIds);
            yield return Command(a, b, "EndTurn");
        }

        static IEnumerator Command(
            MultiplayerDebugScreen actor,
            MultiplayerDebugScreen other,
            string type,
            int card = 0
        )
        {
            long sequence = actor.Sequence;
            Assert.IsTrue(actor.SendGameCommand(type, card));
            yield return Until(() =>
                actor.Sequence == sequence + 1 && other.Sequence == sequence + 1
            );
            yield return null;
        }

        static IEnumerator Capture(Canvas canvas, string name)
        {
            var cameraObject = new GameObject("Capture", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var texture = new RenderTexture(1920, 1080, 24);
            camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            yield return null;
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            pixels.Apply();
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Artifacts"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name), pixels.EncodeToPNG());
            RenderTexture.active = previous;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.worldCamera = null;
            camera.targetTexture = null;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
#endif
