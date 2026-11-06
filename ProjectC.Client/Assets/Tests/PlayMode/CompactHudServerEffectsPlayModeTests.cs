using System.Linq;
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ProjectC.Client;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public partial class MultiplayerPlayModeTests
    {
        [UnityTest]
        public IEnumerator CompactHudEffectsWaitForPresentationBeforeTargetingAndManifest()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../build/Debug/ProjectCNetworkServer.exe")
            );
            string data = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../ProjectC.Server/Tests/InteractionData")
            );
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if (args[i] == "-projectcServerPath")
                {
                    path = args[i + 1];
                }
                if (args[i] == "-projectcInteractionData")
                {
                    data = args[i + 1];
                }
            }
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            server = Process.Start(
                new ProcessStartInfo(path, "--port " + port + " --data \"" + data + "\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                }
            );
            var startup = server.StandardOutput.ReadLineAsync();
            yield return Until(() => startup.IsCompleted);
            StringAssert.Contains("listening", startup.Result);
            var a = Screen(true);
            var b = Screen();
            a.ConnectTo("ws://127.0.0.1:" + port);
            b.ConnectTo("ws://127.0.0.1:" + port);
            yield return Until(() => a.Connected && b.Connected);
            yield return null;
            a.SelectedDeck = "Interaction";
            a.CreateMatch();
            yield return Until(() => a.State != null);
            b.SelectedDeck = "Interaction";
            b.JoinMatch(a.CurrentMatch);
            yield return Until(() => b.State != null);
            a.SetReady("Interaction");
            b.SetReady("Interaction");
            yield return Until(() =>
                a.State != null && a.State.Started && b.State != null && b.State.Started
            );
            yield return null;
            int source = a.State.Players[0].Hand[0].Id,
                target = b.State.Players[1].Hand[0].Id;
            yield return Command(a, "PlayCard", source);
            yield return Command(a, "EndTurn");
            yield return Until(() => b.Sequence == a.Sequence);
            yield return Command(b, "PlayCard", target);
            yield return Command(b, "EndTurn");
            yield return Until(() => a.Sequence == b.Sequence);
            yield return null;
            var ui = a.GetComponent<BattlePresenter>();
            // Snapshot arrival precedes the presentation queue; input must remain locked until it settles.
            yield return Until(() => !ui.PresentationBusy && a.Input.CanExecute(GameplayInputAction.ActivateEffect));
            yield return null;
            var own = ui.OwnField.Widgets[0];
            own.OnPointerDown(null);
            own.OnPointerClick(null);
            var context = ui.GetComponentInChildren<ContextActionView>();
            Assert.IsNotNull(context);
            var actions = context.GetComponentsInChildren<UnityEngine.UI.Button>();
            Assert.AreEqual(3, actions.Length);
            actions[0].onClick.Invoke();
            Assert.AreEqual(BattleInputState.SelectingEffectTarget, ui.Interaction.State);
            var enemy = ui.EnemyField.Widgets[0];
            Assert.IsFalse(enemy.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Status"));
            enemy.OnPointerClick(null);
            Assert.IsFalse(enemy.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Status"));
            var picker = ui.GetComponentInChildren<EffectTargetPicker>();
            var confirm = picker.transform.Find("ConfirmButton").GetComponent<UnityEngine.UI.Button>();
            Assert.IsTrue(confirm.interactable);
            long seq = a.Sequence;
            confirm.onClick.Invoke();
            Assert.AreEqual(BattleInputState.AwaitingServerResponse, ui.Interaction.State);
            yield return Until(() => a.Sequence == seq + 1);
            yield return null;
            Assert.AreEqual(4, a.State.Players[1].Field[0].Hp);
            yield return Until(() => !ui.PresentationBusy && a.Input.CanExecute(GameplayInputAction.ActivateEffect));
            yield return null;
            own = ui.OwnField.Widgets[0];
            own.OnPointerDown(null);
            own.OnPointerClick(null);
            context = ui.GetComponentInChildren<ContextActionView>();
            actions = context.GetComponentsInChildren<UnityEngine.UI.Button>();
            var manifest = Array.Find(
                actions,
                x => x.GetComponentInChildren<TMPro.TMP_Text>().text == "\uD604\uD604"
            );
            Assert.IsNotNull(manifest);
            seq = a.Sequence;
            manifest.onClick.Invoke();
            Assert.AreEqual(
                100,
                own.Card.DefinitionId,
                "Only the server snapshot can change the card form"
            );
            yield return Until(() => a.Sequence == seq + 1);
            yield return null;
            Assert.AreEqual(101, ui.OwnField.Widgets[0].Card.DefinitionId);
            Assert.IsFalse(ui.Interaction.Arrow.gameObject.activeSelf);
            Assert.IsNull(ui.Interaction.Ghost);
            ui.OwnField.Widgets[0].OnPointerClick(null);
            Assert.IsNull(
                ui.GetComponentInChildren<ContextActionView>(),
                "Form without an activated effect exposes no action"
            );
        }

    }
}
