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
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Application.temporaryCachePath,"FrontEnd-"+Guid.NewGuid().ToString("N"));ClientApp.StorageOverride=directory;
            yield return SceneManager.LoadSceneAsync("LobbyScene");yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(opponentObject!=null) UnityEngine.Object.Destroy(opponentObject);
            if(server!=null) {if(!server.HasExited) {server.Kill();server.WaitForExit(5000);}server.Dispose();server=null;}
            if(ClientApp.Instance!=null) UnityEngine.Object.Destroy(ClientApp.Instance.gameObject);
            var current=SceneManager.GetActiveScene();var empty=SceneManager.CreateScene("TestEmpty-"+Guid.NewGuid().ToString("N"));SceneManager.SetActiveScene(empty);yield return SceneManager.UnloadSceneAsync(current);
            ClientApp.StorageOverride=null;
            if(Directory.Exists(directory)) {foreach(var file in Directory.GetFiles(directory)) File.Delete(file);Directory.Delete(directory);}
        }
        static Button Button(MonoBehaviour view,string name) {Assert.IsTrue(view.Bind(name,out Button button));return button;}
        static IEnumerator Until(Func<bool> condition) {float end=Time.realtimeSinceStartup+15;while(!condition()) {Assert.Less(Time.realtimeSinceStartup,end,"Scene condition timed out");yield return null;}}
        [UnityTest] public IEnumerator ActualScenesEditSaveWarningDeleteAndRestore()
        {
            var app=ClientApp.Instance;Assert.IsNotNull(app);var lobby=UnityEngine.Object.FindFirstObjectByType<LobbyScenePresenter>();
            yield return Capture(lobby.GetComponent<Canvas>(),"LobbyScene.png");
            Button(lobby,"DeckManager").onClick.Invoke();yield return Until(()=>SceneManager.GetActiveScene().name=="DeckEditorScene");yield return null;
            var ui=UnityEngine.Object.FindFirstObjectByType<DeckEditorPresenter>();Assert.AreSame(app,ClientApp.Instance);
            Button(ui,"ImportStarter").onClick.Invoke();Assert.AreEqual(30,ui.Session.Draft.Cards.Count);Assert.IsFalse(ui.Session.Dirty);
            yield return Capture(ui.GetComponent<Canvas>(),"DeckEditorScene.png");
            ui.Remove(1001);Assert.IsTrue(ui.Session.Dirty);ui.Add(1007);Assert.AreEqual(29,ui.Session.Draft.Cards.Count);
            ui.Add(1001);Assert.AreEqual(30,ui.Session.Draft.Cards.Count);ui.Add(1001);Assert.AreEqual(30,ui.Session.Draft.Cards.Count);
            ui.NameInput.text="나의 백귀야행";Button(ui,"Back").onClick.Invoke();Assert.IsTrue(ui.Dialog.gameObject.activeSelf);
            Button(ui.Dialog,"Dismiss").onClick.Invoke();Assert.AreEqual("DeckEditorScene",SceneManager.GetActiveScene().name);Assert.IsTrue(ui.Session.Dirty);
            Button(ui,"Save").onClick.Invoke();Assert.IsFalse(ui.Session.Dirty);Assert.AreEqual("나의 백귀야행",app.Decks.Selected.Name);
            Button(ui,"CopyDeck").onClick.Invoke();Assert.AreEqual(2,app.Decks.Decks.Count);string copy=app.Decks.SelectedId;
            Button(ui,"DeleteDeck").onClick.Invoke();Assert.IsTrue(ui.Dialog.gameObject.activeSelf);Button(ui.Dialog,"Dismiss").onClick.Invoke();Assert.AreEqual(2,app.Decks.Decks.Count);
            Button(ui,"DeleteDeck").onClick.Invoke();Button(ui.Dialog,"Confirm").onClick.Invoke();Assert.AreEqual(1,app.Decks.Decks.Count);Assert.IsNull(app.Decks.Find(copy));
            ui.Search.text="등불";Assert.AreEqual(1,ui.LibraryContent.GetComponentsInChildren<DeckTileView>().Length);ui.Search.text="";
            ui.Remove(1001);Button(ui,"CancelEdits").onClick.Invoke();Button(ui.Dialog,"Alternative").onClick.Invoke();Assert.AreEqual(30,ui.Session.Draft.Cards.Count);Assert.IsFalse(ui.Session.Dirty);
            Button(ui,"NewDeck").onClick.Invoke();Assert.AreEqual(0,ui.Session.Draft.Cards.Count);Button(ui,"Save").onClick.Invoke();Assert.IsNotNull(app.Decks.Selected);
            ui.DeckList.value=0;Assert.AreEqual("나의 백귀야행",ui.Session.Draft.Name);
            Button(ui,"Back").onClick.Invoke();yield return Until(()=>SceneManager.GetActiveScene().name=="LobbyScene");yield return null;
            string selected=app.Decks.SelectedId;UnityEngine.Object.Destroy(app.gameObject);yield return null;
            yield return SceneManager.LoadSceneAsync("LobbyScene");yield return null;Assert.AreEqual(selected,ClientApp.Instance.Decks.SelectedId);Assert.AreEqual(30,ClientApp.Instance.Decks.Selected.Cards.Count);
        }
        [UnityTest] public IEnumerator LobbyReadyBattleVictoryAndReturnKeepConnection()
        {
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,"../../build/Debug/ProjectCNetworkServer.exe"));
            var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;++i) if(args[i]=="-projectcServerPath") path=args[i+1];
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);listener.Start();int port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
            server=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path,"--port "+port+" --data \""+Path.Combine(Path.GetDirectoryName(path),"TestData")+"\"") {UseShellExecute=false,RedirectStandardOutput=true,CreateNoWindow=true});
            var started=server.StandardOutput.ReadLineAsync();yield return Until(()=>started.IsCompleted);StringAssert.Contains("listening",started.Result);
            var app=ClientApp.Instance;var a=app.Network;
            opponentObject=new GameObject("Opponent",typeof(MultiplayerDebugScreen));UnityEngine.Object.DontDestroyOnLoad(opponentObject);var b=opponentObject.GetComponent<MultiplayerDebugScreen>();
            var lobby=UnityEngine.Object.FindFirstObjectByType<LobbyScenePresenter>();Button(lobby,"BattleMenu").onClick.Invoke();
            lobby.Bind("MatchPanel",out LobbyPanelView panel);panel.Address.text="ws://127.0.0.1:"+port;panel.Connect.onClick.Invoke();b.ConnectTo(panel.Address.text);
            yield return Until(()=>a.Connected && b.Connected && a.Decks.Length>0);yield return null;panel.Create.onClick.Invoke();yield return Until(()=>a.State!=null);
            b.JoinMatch(a.CurrentMatch);yield return Until(()=>b.State!=null);yield return null;panel.Ready.onClick.Invoke();b.SetReady("Strikers");
            yield return Until(()=>SceneManager.GetActiveScene().name=="BattleScene");yield return null;
            var battle=UnityEngine.Object.FindFirstObjectByType<BattlePresenter>();Assert.AreSame(a,battle.Client);Assert.AreSame(app,ClientApp.Instance);Assert.IsTrue(a.Connected);
            int card=a.State.Players[0].Hand[0].Id;
            yield return Command(a,b,"PlayCard",card);yield return Command(a,b,"EndTurn");yield return Command(b,a,"EndTurn");yield return Command(a,b,"AdvancePhase");yield return Command(a,b,"DirectAttack",card);
            Assert.IsTrue(a.State.Finished);yield return null;Assert.IsTrue(battle.ResultPanel.activeSelf);battle.ResultLeave.onClick.Invoke();
            yield return Until(()=>SceneManager.GetActiveScene().name=="LobbyScene");yield return null;Assert.AreSame(a,ClientApp.Instance.Network);Assert.IsTrue(a.Connected);Assert.IsEmpty(a.CurrentMatch);
        }
        static IEnumerator Command(MultiplayerDebugScreen actor,MultiplayerDebugScreen other,string type,int card=0)
        {long sequence=actor.Sequence;Assert.IsTrue(actor.SendGameCommand(type,card));yield return Until(()=>actor.Sequence==sequence+1 && other.Sequence==sequence+1);yield return null;}
        static IEnumerator Capture(Canvas canvas,string name)
        {
            var cameraObject=new GameObject("Capture",typeof(Camera));var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            var texture=new RenderTexture(1920,1080,24);camera.targetTexture=texture;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases();yield return null;camera.Render();var previous=RenderTexture.active;RenderTexture.active=texture;
            var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts"));Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());
            RenderTexture.active=previous;canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;camera.targetTexture=null;
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
#endif
