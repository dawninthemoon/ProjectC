using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using ProjectC.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace ProjectC.Tests
{
    public partial class MultiplayerPlayModeTests
    {
        [UnityTest] public IEnumerator SpellDragPrivateChoiceReconnectAndCommit()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../build/Debug/ProjectCNetworkServer.exe"));
            var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;++i) if(args[i]=="-projectcServerPath") path=args[i+1];
            string folder=Path.Combine(Application.temporaryCachePath,"ContentChoice-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(Path.GetDirectoryName(path),"Data/HyakkiYagyo/Cards.json"),Path.Combine(folder,"Cards.json"));
            File.WriteAllText(Path.Combine(folder,"Decks.json"),"{\"Config\":{\"InitialLife\":20,\"InitialHandSize\":2,\"DrawPerTurn\":0,\"FieldLimit\":5,\"HandLimit\":10,\"MinDeckSize\":10,\"MaxDeckSize\":10,\"MaxCardCopies\":10,\"InitialYokaiPower\":2,\"YokaiPowerPerTurn\":1,\"MaxYokaiPower\":10,\"SummoningSickness\":false},\"Decks\":[{\"Id\":\"Choice\",\"Name\":\"Choice test\",\"Cards\":[1102,1102,1102,1102,1102,1102,1102,1102,1102,1102]}]}");
            try
            {
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
                server=Process.Start(new ProcessStartInfo(path,"--port "+port+" --data \""+folder+"\""){UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});
                var startup=server.StandardOutput.ReadLineAsync();yield return Until(()=>startup.IsCompleted);StringAssert.Contains("listening",startup.Result);
                var a=Screen(true);var b=Screen();string address="ws://127.0.0.1:"+port;
                a.ConnectTo(address);b.ConnectTo(address);yield return Until(()=>a.Connected && b.Connected && a.Decks.Length>0 && b.Decks.Length>0);yield return null;
                a.CreateMatch();yield return Until(()=>a.State!=null);b.JoinMatch(a.CurrentMatch);yield return Until(()=>b.State!=null);
                a.SetReady("Choice");b.SetReady("Choice");yield return Until(()=>a.State!=null && a.State.Started && b.State!=null && b.State.Started);yield return null;
                var ui=a.GetComponent<BattlePresenter>();var widget=ui.Hand.Widgets[0];
                Assert.AreEqual(1,widget.Card.CardType);Assert.IsTrue(widget.Card.Activations[0].CanActivate);
                int source=widget.Card.Id,other=a.State.Players[0].Hand[1].Id;
                var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,widget.WorldCenter)};
                widget.OnBeginDrag(pointer);Assert.IsNotNull(ui.Interaction.Ghost);
                pointer.position=RectTransformUtility.WorldToScreenPoint(null,ui.OwnField.transform.TransformPoint(((RectTransform)ui.OwnField.transform).rect.center));
                widget.OnDrag(pointer);widget.OnEndDrag(pointer);
                yield return Until(()=>a.PendingChoice!=null && !a.Busy);yield return null;
                Assert.AreEqual(1,a.Sequence);Assert.AreEqual(2,a.State.Players[0].Hand.Length);Assert.AreEqual(8,a.State.Players[0].DeckCount);
                Assert.IsNull(b.PendingChoice);Assert.AreEqual(0,b.State.Players[0].Hand.Length);
                Assert.IsFalse(a.PendingChoice.CanCancel,"A revealed draw must not be cancelled to peek at the deck");
                foreach(var option in a.PendingChoice.Candidates) Assert.AreNotEqual(source,option.CardId);
                string choice=a.PendingChoice.ChoiceId;
                a.DisconnectNetwork();yield return new WaitForSecondsRealtime(.2f);a.ConnectTo(address);
                yield return Until(()=>a.PendingChoice!=null && a.PendingChoice.ChoiceId==choice && !a.Busy);yield return null;
                Assert.AreEqual(BattleInputState.SelectingEffectTarget,ui.Interaction.State);
                var picker=ui.GetComponentInChildren<EffectTargetPicker>();Assert.IsNotNull(picker);
                int index=Array.FindIndex(a.PendingChoice.Candidates,x=>x.CardId!=other);Assert.GreaterOrEqual(index,0);
                picker.transform.Find("Viewport/Candidates").GetChild(index).GetComponent<Button>().onClick.Invoke();
                var confirm=picker.transform.Find("Confirm").GetComponent<Button>();Assert.IsTrue(confirm.interactable);
#if UNITY_EDITOR
                yield return BattlePrefabPlayModeTests.Capture(ui,1920,1080,"Content-PrivateChoice.png");
#endif
                confirm.onClick.Invoke();yield return Until(()=>a.Sequence==2 && b.Sequence==2);yield return null;
                Assert.IsNull(a.PendingChoice);Assert.IsNull(ui.Interaction.Ghost);
                Assert.AreEqual(1,a.State.Players[0].Hand.Length);Assert.AreEqual(2,a.State.Players[0].Graveyard.Length);
                Assert.AreEqual(7,a.State.Players[0].DeckCount);Assert.AreEqual(2,a.State.Players[0].YokaiPower);
                StringAssert.Contains("YOKAI 2/10",ui.OwnPlayer.Counts.text);
                Assert.AreEqual(2,b.State.Players[0].Graveyard.Length);
            }
            finally
            {
                if(server!=null && !server.HasExited) {server.Kill();server.WaitForExit(5000);}
                File.Delete(Path.Combine(folder,"Cards.json"));File.Delete(Path.Combine(folder,"Decks.json"));Directory.Delete(folder);
            }
        }
    }
}
