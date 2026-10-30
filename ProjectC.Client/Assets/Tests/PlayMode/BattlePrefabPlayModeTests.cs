#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ProjectC.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectC.Tests
{
    public class BattlePrefabPlayModeTests
    {
        GameObject instance, input;
        [TearDown] public void Cleanup() { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); if (input != null) UnityEngine.Object.DestroyImmediate(input); }
        static CardView Card(int id, bool attack = false) => new CardView { Id=id, DefinitionId=10, Name="Test Striker", Attack=3, Hp=5, MaxHp=5, CanAttack=attack, CanSummon=!attack, RulesText="No card effects.", ManifestationText="Forms: Manifest Striker (4/8). Effect-driven only." };
        [UnityTest] public IEnumerator AuthoredPrefabHoverSelectionHiddenCardsAndFeedback()
        {
            instance=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
            var ui=instance.GetComponent<BattlePresenter>(); var client=ui.Client;
            client.ApplyMessage("{\"Type\":\"MatchCreated\",\"ProtocolVersion\":2,\"MatchId\":\"UI-test\",\"PlayerId\":1,\"GameNumber\":1}");
            var state=new StateView {Started=true,TurnStarted=true,ActivePlayerId=1,TurnNumber=3,Phase=1,FieldLimit=5,Players=new[]{
                new PlayerView {PlayerId=1,Life=3,DeckCount=20,HandCount=3,Hand=new[]{Card(1),Card(2),Card(3)},Field=new[]{Card(4,true),Card(5)},Graveyard=Array.Empty<CardView>()},
                new PlayerView {PlayerId=2,Life=3,DeckCount=19,HandCount=4,Hand=Array.Empty<CardView>(),Field=new[]{Card(6),Card(7)},Graveyard=Array.Empty<CardView>()}}};
            state.Players[0].Field[1].SummoningSick=true;
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage {Type="StateSnapshot",ProtocolVersion=2,MatchId="UI-test",GameNumber=1,SequenceNumber=1,Payload=state}));
            yield return null; yield return null;
            Assert.AreEqual(3,ui.RenderedTurn); Assert.IsFalse(ui.EndTurn.interactable,"Offline input must be disabled");
            Assert.AreEqual(4,ui.EnemyHand.Widgets.Count);
            foreach(var back in ui.EnemyHand.Widgets) { Assert.IsNull(back.Card); Assert.IsTrue(back.Hidden); }
            var own=ui.OwnField.Widgets[0];
            input=new GameObject("Test EventSystem",typeof(UnityEngine.EventSystems.EventSystem));
            Canvas.ForceUpdateCanvases();
            var pointer=new UnityEngine.EventSystems.PointerEventData(input.GetComponent<UnityEngine.EventSystems.EventSystem>());
            pointer.position=RectTransformUtility.WorldToScreenPoint(null,own.Face.TransformPoint(own.Face.rect.center));
            var hits=new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            instance.GetComponent<UnityEngine.UI.GraphicRaycaster>().Raycast(pointer,hits);
            Assert.IsNotEmpty(hits,"The authored card must receive actual UI raycasts");
            Assert.IsTrue(hits[0].gameObject.transform.IsChildOf(own.transform),"Cosmetic overlays must not intercept card input");
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            yield return null;
            Assert.AreEqual(4,ui.SelectedCardId); Assert.AreEqual("ATTACK TARGET",ui.EnemyField.Widgets[0].Status.text);
            ui.EnemyField.Widgets[0].OnPointerClick(null); yield return null; Assert.AreEqual(6,ui.TargetCardId);
            var hand=ui.Hand.Widgets[0]; hand.OnPointerEnter(null); Assert.AreEqual(hand.Card.Name,ui.Detail.Title.text);
            Assert.Greater(hand.Face.localScale.x,1); hand.OnPointerExit(null); Assert.AreEqual(own.Card.Name,ui.Detail.Title.text);
            Assert.AreEqual("SUMMONING SICKNESS",ui.OwnField.Widgets[1].Status.text);
            ui.ClearSelection(); yield return null; Assert.AreEqual(0,ui.TargetCardId);
            // Cosmetic queues must not block snapshot state, and hidden draws carry no card identity.
            client.ApplyMessage(JsonUtility.ToJson(new EventsMessage {Type="GameEvents",ProtocolVersion=2,MatchId="UI-test",GameNumber=1,SequenceNumber=2,Payload=new[]{
                new EventView {Type=4,FromZone=0,ToZone=1,TargetPlayerId=2},
                new EventView {Type=11,TargetCardId=4},new EventView {Type=13,SourceCardId=4}}}));
            yield return null; yield return null;
            Assert.Greater(ui.Effects.DrawsPresented,0); Assert.Greater(ui.Effects.PopupsPresented,0);
            Assert.AreEqual("HIDDEN",ui.Effects.DrawLabel.text); Assert.IsFalse(ui.Effects.TurnGroup.blocksRaycasts);
            yield return new WaitForSecondsRealtime(3);
            Assert.AreEqual(2,ui.Effects.PopupsPresented);
            ui.Effects.ResetFeedback();
            yield return Capture(ui,1920,1080,"Battle-1920.png");
            yield return Capture(ui,1280,720,"Battle-1280.png");
            // Maximum configured hand fits without hiding information outside its region.
            state.Players[0].Hand=new CardView[10]; for(int i=0;i<10;++i) state.Players[0].Hand[i]=Card(100+i);
            state.Players[0].HandCount=10;
            client.ApplyMessage(JsonUtility.ToJson(new SnapshotMessage {Type="StateSnapshot",ProtocolVersion=2,MatchId="UI-test",GameNumber=1,SequenceNumber=2,Payload=state}));
            yield return null; yield return null; Assert.AreEqual(10,ui.Hand.Widgets.Count);
            yield return Capture(ui,1280,720,"Battle-FullHand.png");
            client.ApplyMessage("{\"Type\":\"CommandRejected\",\"ProtocolVersion\":2,\"MatchId\":\"UI-test\",\"GameNumber\":1,\"Reason\":\"GameRule:WrongPhase\"}");
            StringAssert.Contains("current phase",ui.Log.Feedback.text);
        }
        static IEnumerator Capture(BattlePresenter ui,int width,int height,string name)
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            var cameraObject=new GameObject("UI capture camera"); var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false;
            var texture=new RenderTexture(width,height,24); texture.Create(); camera.targetTexture=texture;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.06f,.09f);
            var canvas=ui.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1;
            Canvas.ForceUpdateCanvases(); yield return null; camera.Render();
            var previous=RenderTexture.active; RenderTexture.active=texture;
            var pixels=new Texture2D(width,height,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Artifacts")); Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,name),pixels.EncodeToPNG());
            RenderTexture.active=previous; canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; camera.targetTexture=null;
            texture.Release(); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
#endif
