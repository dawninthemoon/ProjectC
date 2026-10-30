using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    // One-time editor authoring tool. Saved prefabs are the source of truth at runtime.
    // This is deliberately never invoked automatically on import or in a player build.
    public static class AuthorBattleAssets
    {
        const string Folder = "Assets/Prefabs/Battle";
        static readonly Color Ink = new Color(.035f,.06f,.09f), Panel = new Color(.065f,.105f,.145f), Gold = new Color(.85f,.68f,.37f), Teal = new Color(.24f,.74f,.65f), Paper = new Color(.88f,.92f,.91f);
        static Font font;
        static GameObject Box(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent,false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0,1); r.pivot = new Vector2(0,1); r.anchoredPosition = new Vector2(x,-y); r.sizeDelta = new Vector2(w,h);
            var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return go;
        }
        static void Stretch(RectTransform r, float left = 0, float top = 0, float right = 0, float bottom = 0)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left,bottom); r.offsetMax = new Vector2(-right,-top); }
        static Text Label(Transform parent, string name, string text, float x, float y, float w, float h, int size = 20, Color? color = null)
        {
            var go = Box(parent,name,x,y,w,h,Color.clear); Object.DestroyImmediate(go.GetComponent<Image>());
            var t = go.AddComponent<Text>(); t.font = font; t.text = text; t.fontSize = size; t.color = color ?? Paper; t.supportRichText = false; t.raycastTarget = false;
            t.alignment = TextAnchor.MiddleLeft; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t;
        }
        static Button Button(Transform parent, string name, string text, float x, float y, float w, float h, bool primary = false)
        {
            var go = Box(parent,name,x,y,w,h,primary ? new Color(.12f,.38f,.36f) : new Color(.12f,.19f,.25f));
            go.GetComponent<Image>().raycastTarget = true;
            var b = go.AddComponent<Button>(); b.targetGraphic = go.GetComponent<Image>();
            var colors = b.colors; colors.highlightedColor = new Color(.75f,1,.92f); colors.disabledColor = new Color(.4f,.4f,.4f,.7f); b.colors = colors;
            var t = Label(go.transform,"Label",text,10,0,w-20,h,19); t.alignment = TextAnchor.MiddleCenter;
            Stretch((RectTransform)t.transform,8,2,8,2); return b;
        }
        static InputField Input(Transform parent, string name, string value, float x, float y, float w)
        {
            var go = Box(parent,name,x,y,w,48,Ink); go.GetComponent<Image>().raycastTarget = true;
            var input = go.AddComponent<InputField>(); input.targetGraphic = go.GetComponent<Image>(); input.characterLimit = 128;
            input.textComponent = Label(go.transform,"Text",value,14,2,w-28,44); input.text = value; return input;
        }
        static CardWidget MakeCard(bool back)
        {
            var root = new GameObject(back ? "CardBack" : "BattleCard", typeof(RectTransform),typeof(LayoutElement));
            ((RectTransform)root.transform).sizeDelta = new Vector2(180,215);
            var layout = root.GetComponent<LayoutElement>(); layout.minWidth = back ? 24 : 80; layout.preferredWidth = back ? 80 : 180; layout.flexibleWidth = 0;
            var c = root.AddComponent<CardWidget>();
            var face = Box(root.transform,"Face",0,0,180,215,new Color(.22f,.32f,.4f)); Stretch((RectTransform)face.transform); face.GetComponent<Image>().raycastTarget = true;
            c.Face = (RectTransform)face.transform; c.Frame = face.GetComponent<Image>();
            var inner = Box(face.transform,"Inset",4,4,172,207,Panel); Stretch((RectTransform)inner.transform,4,4,4,4);
            c.Art = Box(inner.transform,"PlaceholderArt",0,0,172,207,new Color(.12f,.29f,.3f)).GetComponent<Image>(); Stretch((RectTransform)c.Art.transform,5,38,5,52);
            c.Symbol = Label(c.Art.transform,"Monogram",back ? "?" : "S",0,0,160,110,56,Gold); Stretch((RectTransform)c.Symbol.transform); c.Symbol.alignment = TextAnchor.MiddleCenter;
            c.Title = Label(inner.transform,"Name",back ? "HIDDEN CARD" : "CARD NAME",8,0,156,36,19); var tr = (RectTransform)c.Title.transform; tr.anchorMin=new Vector2(0,1); tr.anchorMax=Vector2.one; tr.sizeDelta = new Vector2(-16,36); c.Title.resizeTextForBestFit = true; c.Title.resizeTextMinSize = 12; c.Title.resizeTextMaxSize = 19;
            c.Stats = Label(inner.transform,"Stats","3 ATK     5/5 HP",8,155,156,26,18); var sr=(RectTransform)c.Stats.transform; sr.anchorMin=Vector2.zero; sr.anchorMax=new Vector2(1,0); sr.pivot=new Vector2(0,0); sr.anchoredPosition=new Vector2(8,27); sr.sizeDelta=new Vector2(-16,26); c.Stats.resizeTextForBestFit=true; c.Stats.resizeTextMinSize=11; c.Stats.resizeTextMaxSize=18;
            c.Status = Label(inner.transform,"Status","READY",8,183,156,22,12,Gold); sr=(RectTransform)c.Status.transform; sr.anchorMin=Vector2.zero; sr.anchorMax=new Vector2(1,0); sr.pivot=Vector2.zero; sr.anchoredPosition=new Vector2(8,4); sr.sizeDelta=new Vector2(-16,22); c.Status.resizeTextForBestFit=true; c.Status.resizeTextMinSize=8; c.Status.resizeTextMaxSize=12;
            c.Accent = Box(inner.transform,"Accent",0,0,3,200,Teal).GetComponent<Image>(); var ar=(RectTransform)c.Accent.transform; ar.anchorMin=Vector2.zero; ar.anchorMax=new Vector2(0,1); ar.offsetMin=Vector2.zero; ar.offsetMax=new Vector2(3,0);
            // Compact backs show only a crest; never a face, name or definition.
            if(back) { c.Title.gameObject.SetActive(false); c.Stats.gameObject.SetActive(false); c.Status.gameObject.SetActive(false); Stretch((RectTransform)c.Art.transform,6,6,6,6); c.Symbol.fontSize=26; }
            var asset=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/"+root.name+".prefab").GetComponent<CardWidget>(); Object.DestroyImmediate(root); return asset;
        }
        static PlayerPanelView MakePlayer()
        {
            var go=Box(null,"PlayerPanel",0,0,450,100,Panel); var p=go.AddComponent<PlayerPanelView>();
            p.TurnMark=Box(go.transform,"TurnMark",0,0,5,100,Teal).GetComponent<Image>();
            p.Life=Label(go.transform,"Life","3",20,10,65,65,48,Gold);
            Label(go.transform,"LifeCaption","LIFE",25,73,65,20,13,Gold);
            p.Name=Label(go.transform,"Name","PLAYER",105,13,325,35,25);
            p.Counts=Label(go.transform,"Counts","DECK / HAND / GRAVE",105,55,325,34,16);
            var asset=PrefabUtility.SaveAsPrefabAsset(go,Folder+"/PlayerPanel.prefab").GetComponent<PlayerPanelView>(); Object.DestroyImmediate(go); return asset;
        }
        static CardZoneView Zone(Transform parent,string name,float x,float y,float w,float h,CardWidget card,bool hand=false)
        {
            var go=Box(parent,name,x,y,w,h,Color.clear); var zone=go.AddComponent<CardZoneView>(); zone.CardPrefab=card; zone.Content=(RectTransform)go.transform; zone.Hand=hand;
            var layout=go.AddComponent<HorizontalLayoutGroup>(); layout.spacing=12; layout.childControlWidth=true; layout.childControlHeight=true; layout.childForceExpandWidth=!hand && card.name != "CardBack"; layout.childAlignment=TextAnchor.MiddleCenter; layout.childForceExpandHeight=true; layout.padding=new RectOffset(4,4,16,4); return zone;
        }
        static CardZoneView ScrollZone(Transform parent,string name,float x,float y,float w,float h,CardWidget card)
        {
            var viewport=Box(parent,name+"Viewport",x,y,w,h,Color.clear);
            viewport.GetComponent<Image>().raycastTarget=true; viewport.AddComponent<RectMask2D>();
            var zone=Zone(viewport.transform,name,0,0,w,h,card,true);
            var fitter=zone.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
            var scroll=viewport.AddComponent<ScrollRect>(); scroll.viewport=(RectTransform)viewport.transform; scroll.content=zone.Content;
            scroll.horizontal=true; scroll.vertical=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            zone.Hand=false; return zone;
        }
        static CanvasGroup Group(Transform parent,string name,float x,float y,float w,float h)
        { var go=Box(parent,name,x,y,w,h,new Color(.04f,.1f,.16f,.96f)); var group=go.AddComponent<CanvasGroup>(); group.alpha=0; group.blocksRaycasts=false; group.interactable=false; return group; }
        [MenuItem("Project C/Author battle prefabs (replaces authored assets)")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder); font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var card=MakeCard(false); var back=MakeCard(true); var player=MakePlayer();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            card=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/BattleCard.prefab").GetComponent<CardWidget>(); back=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/CardBack.prefab").GetComponent<CardWidget>();
            var root=new GameObject("BattleScreen",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1920,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var bg=Box(root.transform,"Background",0,0,1920,1080,Ink); Stretch((RectTransform)bg.transform);
            var table=Box(root.transform,"Table",0,0,1920,1080,Ink); var tableRect=(RectTransform)table.transform; tableRect.anchorMin=tableRect.anchorMax=tableRect.pivot=new Vector2(.5f,.5f); tableRect.anchoredPosition=Vector2.zero;
            var p=root.AddComponent<BattlePresenter>(); p.Client=root.AddComponent<MultiplayerDebugScreen>();
            Label(table.transform,"Brand","PROJECT C",28,20,275,40,30,Gold); Label(table.transform,"Edition","LOCAL MULTIPLAYER / PLAYTEST",30,62,280,24,13);
            Box(table.transform,"Battlefield",334,155,1250,575,new Color(.055f,.115f,.145f));
            Box(table.transform,"Midline",360,443,1198,2,new Color(.23f,.36f,.38f));
            Label(table.transform,"EnemyFieldCaption","OPPONENT FIELD",355,174,600,24,15,Gold);
            Label(table.transform,"OwnFieldCaption","YOUR FIELD",355,475,600,24,15,Teal);
            p.EnemyHand=Zone(table.transform,"OpponentHand",355,12,600,110,back);
            p.EnemyPlayer=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PlayerPanel.prefab"),table.transform)).GetComponent<PlayerPanelView>(); var pr=(RectTransform)p.EnemyPlayer.transform; pr.anchoredPosition=new Vector2(1100,-30);
            p.OwnPlayer=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PlayerPanel.prefab"),table.transform)).GetComponent<PlayerPanelView>(); pr=(RectTransform)p.OwnPlayer.transform; pr.anchoredPosition=new Vector2(355,-735);
            p.EnemyField=Zone(table.transform,"OpponentField",355,205,1205,225,card);
            p.OwnField=Zone(table.transform,"OwnField",355,503,1205,225,card);
            p.Hand=Zone(table.transform,"YourHand",355,845,1205,222,card,true);
            Label(table.transform,"HandCaption","YOUR HAND / select to summon",855,780,670,32,18,Teal);
            var details=Box(table.transform,"CardInspector",25,155,285,392,Panel); p.Detail=details.AddComponent<CardDetailView>();
            p.Detail.Title=Label(details.transform,"Name","CARD INSPECTOR",18,20,249,65,26,Gold);
            p.Detail.Stats=Label(details.transform,"Stats","Hover a visible card",18,88,249,45,20);
            p.Detail.Rules=Label(details.transform,"Rules","",18,143,249,130,18); p.Detail.Rules.alignment=TextAnchor.UpperLeft;
            p.Detail.Forms=Label(details.transform,"Forms","",18,285,249,95,16,Teal); p.Detail.Forms.alignment=TextAnchor.UpperLeft;
            var log=Box(table.transform,"RecentEvents",25,570,285,345,Panel); p.Log=log.AddComponent<BattleLogView>();
            Label(log.transform,"Header","BATTLE RECORD",18,15,249,35,20,Gold); p.Log.Recent=Label(log.transform,"Recent","Connect to begin.",18,60,249,270,16); p.Log.Recent.alignment=TextAnchor.UpperLeft;
            p.Log.Feedback=Label(table.transform,"RejectionReason","",25,925,285,125,18,new Color(1,.54f,.45f));
            p.TurnLabel=Label(table.transform,"Turn","WAITING",1620,170,270,80,28,Gold); p.PhaseLabel=Label(table.transform,"Phase","",1620,250,270,45,21);
            p.EndTurn=Button(table.transform,"EndTurn","END TURN",1620,315,270,85,true);
            Label(table.transform,"AutoStartHint","Opponent starts automatically",1620,405,270,42,15);
            p.BattlePhase=Button(table.transform,"EnterBattle","ENTER BATTLE PHASE",1620,465,270,52);
            p.Summon=Button(table.transform,"Summon","SUMMON SELECTED",1620,530,270,52,true);
            p.Attack=Button(table.transform,"AttackTarget","ATTACK TARGET",1620,595,270,52);
            p.DirectAttack=Button(table.transform,"DirectAttack","DIRECT ATTACK",1620,660,270,52);
            p.Cancel=Button(table.transform,"Deselect","CLEAR SELECTION",1620,725,270,44);
            p.SelectionLabel=Label(table.transform,"Selection","Select a card",1620,780,270,70,18,Teal);
            p.GraveyardToggle=Button(table.transform,"Graveyards","GRAVEYARDS",1620,875,270,42);
            p.LogToggle=Button(table.transform,"History","BATTLE LOG",1620,929,270,42);
            p.Resync=Button(table.transform,"Resync","RESYNC",1620,983,270,42);
            p.LobbyToggle=Button(table.transform,"LobbyToggle","LOBBY / CONNECTION",1620,30,270,50);
            p.ConnectionLabel=Label(table.transform,"Connection","OFFLINE",1620,90,270,40,16,Teal);
            p.DebugLabel=Label(table.transform,"ProtocolDebug","",1620,1035,270,24,12);
            // Modal panels are ordinary scene/prefab UI, hidden until requested.
            p.GraveyardPanel=Box(table.transform,"GraveyardPanel",335,160,1250,570,Panel); p.GraveyardPanel.GetComponent<Image>().raycastTarget=true;
            Label(p.GraveyardPanel.transform,"OwnCaption","YOUR GRAVEYARD",20,10,1000,30,20,Gold);
            p.OwnGraveyard=ScrollZone(p.GraveyardPanel.transform,"YourGraveyard",20,45,1210,225,card);
            Label(p.GraveyardPanel.transform,"EnemyCaption","OPPONENT GRAVEYARD",20,285,1000,30,20,Gold);
            p.EnemyGraveyard=ScrollZone(p.GraveyardPanel.transform,"OpponentGraveyard",20,320,1210,225,card); p.GraveyardPanel.SetActive(false);
            var history=Box(table.transform,"FullHistory",335,160,1250,570,Panel); history.GetComponent<Image>().raycastTarget=true; p.Log.History=history;
            Label(history.transform,"Header","BATTLE LOG / scroll to inspect",20,10,1200,40,24,Gold);
            var viewport=Box(history.transform,"Viewport",20,65,1210,480,Ink); viewport.AddComponent<RectMask2D>(); viewport.GetComponent<Image>().raycastTarget=true;
            var scroll=history.AddComponent<ScrollRect>(); scroll.viewport=(RectTransform)viewport.transform; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            p.Log.Full=Label(viewport.transform,"Lines","",10,0,1180,480,20); p.Log.Full.alignment=TextAnchor.UpperLeft;
            var fitter=p.Log.Full.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize; scroll.content=(RectTransform)p.Log.Full.transform; history.SetActive(false);
            p.Effects=root.AddComponent<BattleEffectsView>(); var fx=p.Effects;
            fx.TurnGroup=Group(table.transform,"TurnBanner",510,390,900,145); fx.TurnTitle=Label(fx.TurnGroup.transform,"Title","YOUR TURN",25,15,850,65,48,Gold); fx.TurnTitle.alignment=TextAnchor.MiddleCenter;
            fx.TurnSubtitle=Label(fx.TurnGroup.transform,"Subtitle","TURN 1",25,83,850,40,26); fx.TurnSubtitle.alignment=TextAnchor.MiddleCenter;
            fx.PopupGroup=Group(table.transform,"CardEventPopup",570,115,780,120); fx.PopupTitle=Label(fx.PopupGroup.transform,"Title","",22,10,736,35,25,Gold); fx.PopupBody=Label(fx.PopupGroup.transform,"Description","",22,45,736,68,18);
            fx.DrawGroup=Group(table.transform,"DrawCard",0,0,90,125); fx.DrawCard=(RectTransform)fx.DrawGroup.transform; fx.DrawLabel=Label(fx.DrawGroup.transform,"Back","DRAW",8,0,74,125,18,Gold); fx.DrawLabel.alignment=TextAnchor.MiddleCenter;
            var ownDeck=Box(table.transform,"YourDeck",1475,750,85,85,new Color(.13f,.23f,.28f));
            Label(ownDeck.transform,"Label","DECK",8,0,69,85,18,Gold).alignment=TextAnchor.MiddleCenter;
            var opponentDeck=Box(table.transform,"OpponentDeck",980,30,85,100,new Color(.13f,.23f,.28f));
            Label(opponentDeck.transform,"Label","DECK",8,0,69,100,18,Gold).alignment=TextAnchor.MiddleCenter;
            // Put the deck widgets behind cosmetic overlays and modals.
            ownDeck.transform.SetSiblingIndex(5); opponentDeck.transform.SetSiblingIndex(5);
            fx.OwnDeck=(RectTransform)ownDeck.transform; fx.OwnHand=p.Hand.Content; fx.OpponentDeck=(RectTransform)opponentDeck.transform; fx.OpponentHand=p.EnemyHand.Content;
            p.ResultPanel=Box(table.transform,"MatchResult",510,330,900,380,Panel); p.ResultPanel.GetComponent<Image>().raycastTarget=true;
            p.ResultLabel=Label(p.ResultPanel.transform,"Result","VICTORY",40,35,820,90,56,Gold); p.ResultLabel.alignment=TextAnchor.MiddleCenter;
            Label(p.ResultPanel.transform,"Hint","Both players must request a rematch.",60,145,780,45,23);
            p.ResultRematch=Button(p.ResultPanel.transform,"Rematch","REQUEST REMATCH",65,240,365,70,true); p.ResultLeave=Button(p.ResultPanel.transform,"Leave","RETURN TO LOBBY",470,240,365,70); p.ResultPanel.SetActive(false);
            p.LobbyOverlay=Box(table.transform,"LobbyPanel",475,155,1000,750,Panel); p.LobbyOverlay.GetComponent<Image>().raycastTarget=true; var lobby=p.LobbyOverlay.AddComponent<LobbyPanelView>(); p.Lobby=lobby;
            Label(lobby.transform,"Title","PROJECT C / MATCH LOBBY",35,20,930,60,34,Gold);
            lobby.Address=Input(lobby.transform,"ServerAddress","ws://127.0.0.1:7777",35,100,610);
            lobby.Connect=Button(lobby.transform,"Connect","CONNECT",665,100,140,48,true); lobby.Disconnect=Button(lobby.transform,"Disconnect","DISCONNECT",820,100,145,48);
            lobby.Connection=Label(lobby.transform,"Status","",35,165,930,65,20,Teal);
            lobby.Deck=Button(lobby.transform,"Deck","DECK / Strikers",35,245,450,52); lobby.DeckLabel=lobby.Deck.GetComponentInChildren<Text>();
            lobby.Create=Button(lobby.transform,"Create","CREATE MATCH",515,245,450,52,true);
            lobby.MatchId=Input(lobby.transform,"MatchId","",35,320,610); lobby.Join=Button(lobby.transform,"Join","JOIN MATCH ID",665,320,300,48,true);
            lobby.Seats=Label(lobby.transform,"Seats","",35,390,930,95,22);
            lobby.Ready=Button(lobby.transform,"Ready","READY TO PLAY",35,505,450,60,true); lobby.ReadyLabel=lobby.Ready.GetComponentInChildren<Text>();
            lobby.Rematch=Button(lobby.transform,"Rematch","REQUEST REMATCH",515,505,450,60);
            lobby.Leave=Button(lobby.transform,"Leave","LEAVE MATCH",35,590,450,50); lobby.Close=Button(lobby.transform,"Close","CLOSE MATCH (HOST)",515,590,450,50);
            Label(lobby.transform,"Help","Both players ready: start automatically. Use LOBBY / CONNECTION to return to the table.",35,660,930,65,18);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/BattleScreen.prefab"); Object.DestroyImmediate(root);
            PrefabUtility.InstantiatePrefab(prefab);
            var camera=new GameObject("Main Camera",typeof(Camera)); camera.tag="MainCamera"; camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor; camera.GetComponent<Camera>().backgroundColor=Ink;
            var es=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)); es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            EditorSceneManager.SaveScene(scene,"Assets/Scenes/BattleScene.unity"); EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/BattleScene.unity",true)};
            AssetDatabase.SaveAssets();
        }
    }
}
