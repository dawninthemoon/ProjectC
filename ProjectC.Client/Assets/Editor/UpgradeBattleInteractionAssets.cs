using System;
using Game.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    // Explicit editor migration: modifies the existing prefab and preserves the user's scene/camera.
    // Nothing here is called by player builds or during asset import.
    public static class UpgradeBattleInteractionAssets
    {
        const string Folder="Assets/Prefabs/Battle/";
        static Font font;
        static readonly Color Panel=new Color(.055f,.1f,.14f), Ink=new Color(.025f,.045f,.075f), Gold=new Color(.88f,.71f,.4f);
        static T Find<T>(GameObject root,string name) where T:Component
        { if(!root.Bind(name,out T result)) throw new InvalidOperationException("Missing "+name+" in "+root.name); return result; }
        static T Ensure<T>(GameObject go) where T:Component { var component=go.GetComponent<T>();return component!=null?component:go.AddComponent<T>(); }
        static void Rect(RectTransform rect,float x,float y,float w,float h)
        { rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1); rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(w,h); }
        static GameObject Box(Transform parent,string name,float x,float y,float w,float h,Color color,bool raycast=false)
        {
            var child=parent!=null?parent.Find(name):null; var go=child!=null?child.gameObject:new GameObject(name,typeof(RectTransform),typeof(Image));
            go.transform.SetParent(parent,false); Rect((RectTransform)go.transform,x,y,w,h); var image=Ensure<Image>(go); image.color=color; image.raycastTarget=raycast; return go;
        }
        static Text Label(Transform parent,string name,string text,float x,float y,float w,float h,int size=20)
        {
            var existing=parent.Find(name); var go=existing!=null?existing.gameObject:new GameObject(name,typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent,false); Rect((RectTransform)go.transform,x,y,w,h);
            var t=go.GetComponent<Text>(); t.text=text;t.font=font;t.fontSize=size;t.color=new Color(.88f,.93f,.94f);t.raycastTarget=false;t.supportRichText=false;t.verticalOverflow=VerticalWrapMode.Overflow;t.alignment=TextAnchor.MiddleLeft; return t;
        }
        static Button Button(Transform parent,string name,string text,float x,float y,float w,float h)
        {
            var go=Box(parent,name,x,y,w,h,new Color(.1f,.22f,.27f),true); var b=Ensure<Button>(go);b.targetGraphic=go.GetComponent<Image>();
            var t=Label(go.transform,"Label",text,8,0,w-16,h,19);t.alignment=TextAnchor.MiddleCenter; return b;
        }
        static void Move(Component c,float x,float y,float w,float h) { Rect((RectTransform)c.transform,x,y,w,h); }
        static CanvasGroup Group(GameObject go,bool visible=false)
        { var g=Ensure<CanvasGroup>(go);g.alpha=visible?1:0;g.interactable=g.blocksRaycasts=visible;return g; }
        static void Layer(Transform parent,string name)
        { var go=Box(parent,name,0,0,1920,1080,Color.clear);go.GetComponent<Image>().enabled=false; }
        static void SetButton(Button b,string text) { b.GetComponentInChildren<Text>(true).text=text; }
        static void SaveCard(string name)
        {
            var root=PrefabUtility.LoadPrefabContents(Folder+name+".prefab");
            Ensure<CanvasGroup>(root);
            foreach(var t in root.GetComponentsInChildren<Text>(true)) { t.font=font;t.verticalOverflow=VerticalWrapMode.Overflow; }
            PrefabUtility.SaveAsPrefabAsset(root,Folder+name+".prefab"); PrefabUtility.UnloadPrefabContents(root);
        }
        [MenuItem("Project C/Upgrade card interaction prefabs")]
        public static void Build()
        {
            font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansKR-Regular.otf");
            if(font==null) throw new InvalidOperationException("Import the licensed NotoSansKR font before upgrading");
            var settings=AssetDatabase.LoadAssetAtPath<BattleVisualSettings>(Folder+"BattleVisualSettings.asset");
            if(settings==null) { settings=ScriptableObject.CreateInstance<BattleVisualSettings>();AssetDatabase.CreateAsset(settings,Folder+"BattleVisualSettings.asset"); }
            SaveCard("BattleCard");SaveCard("CardBack");
            var action=Button(null,"CardAction","ACTION",0,0,230,44); var element=action.gameObject.AddComponent<LayoutElement>();element.preferredHeight=44;element.minHeight=44;
            var actionAsset=PrefabUtility.SaveAsPrefabAsset(action.gameObject,Folder+"CardAction.prefab").GetComponent<Button>();Object.DestroyImmediate(action.gameObject);
            var root=PrefabUtility.LoadPrefabContents(Folder+"BattleScreen.prefab");
            var p=root.GetComponent<BattlePresenter>(); var table=Find<RectTransform>(root,"Table");
            foreach(var t in root.GetComponentsInChildren<Text>(true)) { t.font=font;t.verticalOverflow=VerticalWrapMode.Overflow; }
            p.OwnPlayer.name="OwnPlayer";p.EnemyPlayer.name="EnemyPlayer";
            Move(p.EnemyPlayer,735,20,450,100);Move(p.OwnPlayer,40,860,450,100);
            Move(p.EnemyHand,470,125,980,100);Move(p.EnemyField,240,248,1440,220);Move(p.OwnField,240,510,1440,230);Move(p.Hand,460,805,1000,260);
            foreach(var zone in new[]{p.Hand,p.OwnField,p.EnemyField,p.EnemyHand})
            {
                var old=zone.GetComponent<HorizontalLayoutGroup>();if(old!=null) Object.DestroyImmediate(old);
                var layout=Ensure<CardTableLayout>(zone.gameObject);layout.Hand=zone==p.Hand;layout.Compact=zone==p.EnemyHand;layout.Settings=settings;
            }
            Move(Find<RectTransform>(root,"Battlefield"),215,225,1490,535);
            Move(Find<RectTransform>(root,"Midline"),240,485,1440,2);
            Move(Find<Text>(root,"EnemyFieldCaption"),250,218,550,28);Move(Find<Text>(root,"OwnFieldCaption"),250,480,550,28);
            Move(Find<Text>(root,"HandCaption"),660,770,700,28);Find<Text>(root,"HandCaption").text="YOUR HAND  /  drag a card onto your field";
            Find<Text>(root,"Edition").text="CARD BATTLE / LOCAL MULTIPLAYER";
            Move(p.TurnLabel,1720,330,180,55);p.TurnLabel.fontSize=23;
            Move(p.PhaseLabel,1720,390,180,50);p.PhaseLabel.fontSize=18;
            Move(p.EndTurn,1720,465,180,74);SetButton(p.EndTurn,"END TURN");
            Move(p.BattlePhase,1720,552,180,50);SetButton(p.BattlePhase,"BATTLE PHASE");
            Move(Find<Text>(root,"AutoStartHint"),1720,610,175,65);Find<Text>(root,"AutoStartHint").fontSize=14;
            Move(p.LobbyToggle,1710,24,190,44);SetButton(p.LobbyToggle,"MENU / LOBBY");
            Move(p.LogToggle,26,470,160,48);SetButton(p.LogToggle,"BATTLE LOG");
            Move(p.GraveyardToggle,26,530,160,48);SetButton(p.GraveyardToggle,"GRAVEYARD");
            var unread=table.Find("Unread");if(unread==null) Label(table,"Unread","",168,470,35,48,17).color=Gold;
            var legacy=Box(table,"LegacyDebugControls",0,0,1,1,Color.clear);legacy.SetActive(false);
            foreach(var c in new Component[]{p.Summon,p.Attack,p.DirectAttack,p.Cancel,p.SelectionLabel}) c.transform.SetParent(legacy.transform,false);
            // Preserve the existing lobby; tuck protocol/resync inside it instead of the play surface.
            p.ConnectionLabel.transform.SetParent(p.LobbyOverlay.transform,false);Move(p.ConnectionLabel,35,705,520,30);p.ConnectionLabel.fontSize=14;
            p.DebugLabel.transform.SetParent(p.LobbyOverlay.transform,false);Move(p.DebugLabel,570,705,250,30);
            p.Resync.transform.SetParent(p.LobbyOverlay.transform,false);Move(p.Resync,825,700,140,36);
            var help=Find<Text>(p.LobbyOverlay,"Help");help.text="Ready on both clients to begin. MENU / LOBBY closes this panel during a game.";
            // Background and leader hit areas are authored Images, not runtime graphics.
            var background=Find<Image>(root,"Table");background.raycastTarget=true;Ensure<BattleHitArea>(background.gameObject).Kind=BattleHitArea.AreaKind.Background;
            var field=Box(table,"OwnFieldDropZone",230,505,1460,245,new Color(.055f,.115f,.145f),true);
            if(field.transform.GetSiblingIndex()>p.OwnField.transform.GetSiblingIndex()) field.transform.SetSiblingIndex(p.OwnField.transform.GetSiblingIndex());Ensure<BattleHitArea>(field).Kind=BattleHitArea.AreaKind.OwnField;
            foreach(var entry in new[]{p.OwnPlayer,p.EnemyPlayer}) { entry.GetComponent<Image>().raycastTarget=true;Ensure<BattleHitArea>(entry.gameObject).Kind=entry==p.OwnPlayer?BattleHitArea.AreaKind.OwnHero:BattleHitArea.AreaKind.EnemyHero; }
            Move(Find<RectTransform>(root,"YourDeck"),1715,845,100,125);Move(Find<RectTransform>(root,"OpponentDeck"),1720,125,100,125);
            // Card details are contextual and scroll when a definition has long text.
            Move(p.Detail,280,240,335,440);p.Detail.GetComponent<Image>().raycastTarget=true;
            Move(p.Detail.Title,18,15,299,70);Move(p.Detail.Stats,18,88,299,64);
            var detailViewport=Box(p.Detail.transform,"DetailViewport",18,160,299,260,Ink,true);Ensure<RectMask2D>(detailViewport);
            var detailContent=Box(detailViewport.transform,"DetailContent",0,0,299,260,Color.clear);
            var detailLayout=Ensure<VerticalLayoutGroup>(detailContent);detailLayout.spacing=18;detailLayout.childControlHeight=true;detailLayout.childForceExpandHeight=false;
            Ensure<ContentSizeFitter>(detailContent).verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            p.Detail.Rules.transform.SetParent(detailContent.transform,false);p.Detail.Forms.transform.SetParent(detailContent.transform,false);
            foreach(var t in new[]{p.Detail.Rules,p.Detail.Forms}) { t.fontSize=18;t.alignment=TextAnchor.UpperLeft;t.verticalOverflow=VerticalWrapMode.Overflow; }
            var ds=Ensure<ScrollRect>(detailViewport);ds.viewport=(RectTransform)detailViewport.transform;ds.content=(RectTransform)detailContent.transform;ds.horizontal=false;ds.movementType=ScrollRect.MovementType.Clamped;
            p.Detail.gameObject.SetActive(false);
            // Keep the log model alive while removing its old fixed display.
            p.Log.GetComponent<Image>().enabled=false;
            foreach(Transform child in p.Log.transform) child.gameObject.SetActive(false);
            Move(p.Log,0,0,1,1);Move(p.Log.Feedback,560,716,800,70);p.Log.Feedback.fontSize=19;
            var history=p.Log.History;history.SetActive(true);Move(history.GetComponent<RectTransform>(),-500,150,450,700);Group(history);
            Move(Find<Text>(history,"Header"),20,12,310,48);Find<Text>(history,"Header").text="BATTLE LOG";
            Button(history.transform,"CloseLog","X",370,18,50,38);
            var vp=Find<RectTransform>(history,"Viewport");Move(vp,18,80,414,600);Move(p.Log.Full,8,0,395,600);p.Log.Full.fontSize=19;p.Log.Full.raycastTarget=false;
            p.Log.Full.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            // Overlay layers contain real prefab objects. They never intercept gameplay rays.
            Layer(table,"DragVisualRoot");var dragRoot=table.Find("DragVisualRoot");Group(dragRoot.gameObject,true).blocksRaycasts=false;
            var arrow=Box(table,"AttackArrow",0,0,1,1,Color.clear);arrow.GetComponent<Image>().enabled=false;Group(arrow,true).blocksRaycasts=false;
            var shaft=Box(arrow.transform,"Shaft",0,0,100,7,new Color(.4f,.95f,.83f));var sr=(RectTransform)shaft.transform;sr.anchorMin=sr.anchorMax=sr.pivot=new Vector2(0,.5f);sr.anchoredPosition=Vector2.zero;
            var tip=Label(arrow.transform,"Tip",">",100,-24,50,50,48);tip.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");tip.fontSize=36;tip.horizontalOverflow=HorizontalWrapMode.Overflow;tip.color=Gold;tip.alignment=TextAnchor.MiddleCenter;((RectTransform)tip.transform).anchorMin=((RectTransform)tip.transform).anchorMax=new Vector2(0,.5f);((RectTransform)tip.transform).pivot=new Vector2(.5f,.5f);((RectTransform)tip.transform).anchoredPosition=new Vector2(100,0);
            // Serialized chevron Images stay legible over card art; no runtime line drawing.
            tip.enabled=false;
            foreach(int sign in new[]{-1,1})
            {
                var wing=Box(tip.transform,sign<0?"LowerWing":"UpperWing",0,0,25,6,new Color(.4f,.95f,.83f));
                var wr=(RectTransform)wing.transform;wr.anchorMin=wr.anchorMax=new Vector2(.5f,.5f);wr.pivot=new Vector2(1,.5f);wr.anchoredPosition=Vector2.zero;wr.localRotation=Quaternion.Euler(0,0,sign*35);
            }
            Ensure<AttackArrowView>(arrow);arrow.SetActive(false);
            var context=Box(table,"ContextAction",0,0,250,70,Panel,true);var actionContent=Box(context.transform,"ActionContent",10,10,230,50,Color.clear);
            var vl=Ensure<VerticalLayoutGroup>(actionContent);vl.spacing=4;vl.childControlHeight=true;vl.childForceExpandHeight=false;
            var cr=(RectTransform)actionContent.transform;cr.anchorMin=Vector2.zero;cr.anchorMax=Vector2.one;cr.offsetMin=new Vector2(10,10);cr.offsetMax=new Vector2(-10,-10);
            Ensure<ContextActionView>(context).ActionPrefab=actionAsset;context.SetActive(false);
            var picker=Box(table,"EffectTargetPicker",20,640,410,410,Panel,true);Ensure<EffectTargetPicker>(picker).CandidatePrefab=actionAsset;
            Label(picker.transform,"Title","EFFECT TARGETS",18,10,374,65,18);
            var pv=Box(picker.transform,"Viewport",18,85,374,250,Ink,true);Ensure<RectMask2D>(pv);
            var pc=Box(pv.transform,"Candidates",0,0,374,250,Color.clear);var pl=Ensure<VerticalLayoutGroup>(pc);pl.spacing=5;pl.childControlHeight=true;pl.childForceExpandHeight=false;
            Ensure<ContentSizeFitter>(pc).verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            var ps=Ensure<ScrollRect>(pv);ps.viewport=(RectTransform)pv.transform;ps.content=(RectTransform)pc.transform;ps.horizontal=false;
            Button(picker.transform,"Confirm","CONFIRM",18,350,180,44);Button(picker.transform,"Cancel","CANCEL",212,350,180,44);picker.SetActive(false);
            var dim=Box(table,"TurnDim",0,0,1920,1080,new Color(0,0,0,.28f));Group(dim);dim.transform.SetSiblingIndex(p.Effects.TurnGroup.transform.GetSiblingIndex());
            // Explicit front/back draw visual, resolved from visible DTOs only.
            var drawFace=p.Effects.DrawGroup.transform.Find("DrawFace")?.gameObject ?? (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"BattleCard.prefab"),p.Effects.DrawGroup.transform);
            drawFace.name="DrawFace";Move(drawFace.GetComponent<RectTransform>(),0,0,180,218);drawFace.GetComponent<CanvasGroup>().blocksRaycasts=false;drawFace.SetActive(false);
            Ensure<BattleInteractionController>(root).GhostPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"BattleCard.prefab").GetComponent<CardWidget>();
            root.GetComponent<BattleInteractionController>().Settings=settings;p.Effects.Settings=settings;p.Log.Settings=settings;
            p.Detail.transform.SetAsLastSibling();context.transform.SetAsLastSibling();picker.transform.SetAsLastSibling();p.GraveyardPanel.transform.SetAsLastSibling();history.transform.SetAsLastSibling();p.ResultPanel.transform.SetAsLastSibling();p.LobbyOverlay.transform.SetAsLastSibling();
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"BattleScreen.prefab");PrefabUtility.UnloadPrefabContents(root);
            // Existing scene is opened, not recreated: URP camera and input module are retained.
            var scene=EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity",OpenSceneMode.Single);
            foreach(var go in scene.GetRootGameObjects()) if(go.GetComponent<BattlePresenter>()!=null) go.name="Project C - Card Battle";
            EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        }
    }
}
