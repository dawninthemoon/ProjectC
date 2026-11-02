using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    // Explicit authoring command. All scene layout is serialized; player code only binds data and list items.
    public static class AuthorFrontEndAssets
    {
        const string Folder = "Assets/Prefabs/FrontEnd";
        static TMP_FontAsset font;
        static readonly Color Ink = new Color(.035f, .043f, .055f),
            Panel = new Color(.075f, .09f, .105f),
            Paper = new Color(.91f, .86f, .74f),
            Gold = new Color(.75f, .58f, .32f),
            Red = new Color(.65f, .18f, .12f);

        static void Rect(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
        }

        static GameObject Box(
            Transform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            Color color,
            bool hit = false
        )
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Rect((RectTransform)go.transform, x, y, w, h);
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = hit;
            return go;
        }

        static TMP_Text Label(
            Transform parent,
            string name,
            string value,
            float x,
            float y,
            float w,
            float h,
            int size = 20,
            Color? color = null
        )
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            Rect((RectTransform)go.transform, x, y, w, h);
            var t = go.GetComponent<TMP_Text>();
            t.font = font;
            t.text = value;
            t.fontSize = size;
            t.color = color ?? Paper;
            t.raycastTarget = false;
            t.richText = false;
            t.alignment = TextAlignmentOptions.Left;
            t.overflowMode = TextOverflowModes.Truncate;
            return t;
        }

        static Button Button(
            Transform parent,
            string name,
            string value,
            float x,
            float y,
            float w,
            float h,
            bool primary = false
        )
        {
            var go = Box(
                parent,
                name,
                x,
                y,
                w,
                h,
                primary ? Red : new Color(.15f, .19f, .20f),
                true
            );
            var button = go.AddComponent<Button>();
            button.targetGraphic = go.GetComponent<Image>();
            var label = Label(go.transform, "Label", value, 12, 0, w - 24, h, 20);
            label.alignment = TextAlignmentOptions.Center;
            var colors = button.colors;
            colors.disabledColor = new Color(.5f, .5f, .5f, .65f);
            button.colors = colors;
            return button;
        }

        static TMP_InputField Input(
            Transform parent,
            string name,
            string value,
            float x,
            float y,
            float w,
            int limit = 128
        )
        {
            var go = Box(parent, name, x, y, w, 48, new Color(.10f, .13f, .14f), true);
            var field = go.AddComponent<TMP_InputField>();
            field.targetGraphic = go.GetComponent<Image>();
            field.characterLimit = limit;
            field.textComponent = Label(go.transform, "Text", value, 12, 0, w - 24, 48, 20);
            field.textViewport = (RectTransform)go.transform;
            field.fontAsset = font;
            field.pointSize = field.textComponent.fontSize;
            field.richText = false;
            field.text = value;
            field.placeholder = Label(
                go.transform,
                "Placeholder",
                name == "Search" ? "카드명 검색"
                    : name == "DeckName" ? "덱 이름"
                    : name == "MatchId" ? "참가할 Match ID"
                    : "서버 주소",
                12,
                0,
                w - 24,
                48,
                18,
                new Color(.55f, .56f, .50f)
            );
            return field;
        }

        static RectTransform Scroll(
            Transform parent,
            string name,
            float x,
            float y,
            float w,
            float h,
            int columns = 0,
            Vector2 cell = default
        )
        {
            var viewport = Box(parent, name + "Viewport", x, y, w, h, Ink, true);
            viewport.AddComponent<RectMask2D>();
            var content = Box(viewport.transform, name, 0, 0, w, h, Color.clear);
            var r = (RectTransform)content.transform;
            r.anchorMax = new Vector2(1, 1);
            r.sizeDelta = new Vector2(0, h);
            if (columns > 0)
            {
                var grid = content.AddComponent<GridLayoutGroup>();
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                grid.cellSize = cell;
                grid.spacing = new Vector2(12, 12);
                grid.padding = new RectOffset(10, 10, 10, 10);
            }
            else
            {
                var layout = content.AddComponent<VerticalLayoutGroup>();
                layout.childControlHeight = true;
                layout.childForceExpandHeight = false;
                layout.padding = new RectOffset(8, 8, 8, 8);
            }
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter
                .FitMode
                .PreferredSize;
            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = r;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30;
            return r;
        }

        static TMP_Dropdown Dropdown(Transform parent, string name, float x, float y, float w)
        {
            var go = Box(parent, name, x, y, w, 48, Panel, true);
            var dropdown = go.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = go.GetComponent<Image>();
            dropdown.captionText = Label(go.transform, "Caption", "저장된 덱", 12, 0, w - 45, 48);
            Label(go.transform, "Arrow", "▼", w - 32, 0, 25, 48, 16, Gold);
            var template = Box(go.transform, "Template", 0, 48, w, 250, Panel, true);
            var viewport = Box(template.transform, "Viewport", 0, 0, w, 250, Panel, true);
            viewport.AddComponent<RectMask2D>();
            var content = Box(viewport.transform, "Content", 0, 0, w, 44, Color.clear);
            var item = Box(content.transform, "Item", 0, 0, w, 44, Panel, true);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = item.GetComponent<Image>();
            toggle.graphic = Box(item.transform, "Checkmark", 8, 16, 12, 12, Gold)
                .GetComponent<Image>();
            dropdown.itemText = Label(item.transform, "ItemLabel", "Deck", 28, 0, w - 40, 44, 18);
            var scroll = template.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = (RectTransform)content.transform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            dropdown.template = (RectTransform)template.transform;
            dropdown.options.Add(new TMP_Dropdown.OptionData("저장된 덱 없음"));
            template.SetActive(false);
            return dropdown;
        }

        static GameObject Canvas(string name, out Transform table)
        {
            var root = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster)
            );
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scale = root.GetComponent<CanvasScaler>();
            scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scale.referenceResolution = new Vector2(1920, 1080);
            scale.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var bg = Box(root.transform, "Background", 0, 0, 1920, 1080, Ink);
            var br = (RectTransform)bg.transform;
            br.anchorMin = Vector2.zero;
            br.anchorMax = Vector2.one;
            br.offsetMin = br.offsetMax = Vector2.zero;
            table = Box(root.transform, "Table", 0, 0, 1920, 1080, Ink).transform;
            var tr = (RectTransform)table;
            tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(.5f, .5f);
            tr.anchoredPosition = Vector2.zero;
            Box(table, "TopRule", 40, 86, 1840, 2, Gold);
            Box(table, "BottomRule", 40, 1025, 1840, 2, Gold);
            Label(table, "Edition", "PROJECT C  /  HYAKKI YAGYO", 40, 1034, 1100, 30, 14, Gold);
            return root;
        }

        static DeckTileView Tile()
        {
            var root = Box(null, "DeckTile", 0, 0, 150, 184, Panel, true);
            root.AddComponent<Button>();
            root.AddComponent<LayoutElement>();
            var view = root.AddComponent<DeckTileView>();
            Box(root.transform, "Border", 0, 0, 3, 184, Gold);
            var name = Label(root.transform, "CardName", "百鬼夜行", 10, 8, 130, 40, 17);
            name.enableAutoSizing = true;
            name.fontSizeMin = 12;
            name.fontSizeMax = 17;
            var art = Box(
                root.transform,
                "Placeholder",
                10,
                52,
                130,
                68,
                new Color(.09f, .12f, .14f)
            );
            Label(art.transform, "Crest", "妖", 0, 0, 130, 68, 44, Gold).alignment =
                TextAlignmentOptions.Center;
            Label(root.transform, "CardKind", "MONSTER", 10, 122, 130, 18, 11, Gold);
            Label(root.transform, "CardStats", "ATK 3  HP 4", 10, 142, 130, 18, 12);
            Label(root.transform, "CardCount", "× 0", 10, 161, 130, 20, 13);
            UpgradeCardPresentationAssets.ConfigureTile(root, 150);
            var prefab = PrefabUtility
                .SaveAsPrefabAsset(root, Folder + "/DeckTile.prefab")
                .GetComponent<DeckTileView>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void Dialog(Transform table)
        {
            var overlay = Box(
                table,
                "ConfirmOverlay",
                0,
                0,
                1920,
                1080,
                new Color(0, 0, 0, .8f),
                true
            );
            overlay.AddComponent<ConfirmationDialog>();
            var panel = Box(overlay.transform, "Dialog", 550, 340, 820, 360, Panel, true);
            Box(panel.transform, "Seal", 0, 0, 6, 360, Red);
            Label(panel.transform, "DialogTitle", "確認", 30, 24, 760, 55, 30, Gold);
            Label(panel.transform, "DialogMessage", "", 30, 90, 760, 130, 22);
            Button(panel.transform, "Confirm", "확인", 30, 250, 240, 65, true);
            Button(panel.transform, "Alternative", "버리고 이동", 290, 250, 240, 65);
            Button(panel.transform, "Dismiss", "계속 편집", 550, 250, 240, 65);
            overlay.SetActive(false);
        }

        static void MakeEditor(DeckTileView tile)
        {
            var root = Canvas("DeckEditorScreen", out var table);
            var p = root.AddComponent<DeckEditorPresenter>();
            p.TilePrefab = tile;
            Label(table, "Title", "百鬼の書庫  /  덱 편집", 40, 16, 1100, 60, 32, Gold);
            Dropdown(table, "DeckList", 40, 110, 440);
            Input(table, "DeckName", "", 510, 110, 540, 60);
            Button(table, "NewDeck", "새 덱", 1080, 110, 170, 48);
            Button(table, "CopyDeck", "복제", 1270, 110, 170, 48);
            Button(table, "DeleteDeck", "삭제", 1460, 110, 170, 48);
            Button(table, "ImportStarter", "백귀야행 복사", 1650, 110, 230, 48);
            Label(table, "DetailCaption", "카드 상세", 40, 180, 290, 40, 23, Gold);
            Box(table, "DetailPanel", 40, 230, 290, 680, Panel);
            Label(table, "DetailCrest", "妖", 60, 250, 250, 160, 96, Gold).alignment =
                TextAlignmentOptions.Center;
            Label(table, "DetailName", "카드에 마우스를 올려보세요", 60, 425, 250, 80, 23);
            Label(
                table,
                "DetailStats",
                "라이브러리 클릭: 추가\n내 덱 클릭: 제거",
                60,
                510,
                250,
                100,
                17,
                Gold
            );
            var rules = Scroll(table, "DetailContent", 60, 620, 250, 270);
            var rule = Label(rules, "DetailRules", "", 0, 0, 230, 270, 18);
            rule.alignment = TextAlignmentOptions.TopLeft;
            Label(table, "DeckCaption", "현재 덱  /  클릭하여 제거", 350, 180, 750, 40, 23, Gold);
            Label(table, "DeckCount", "", 350, 225, 770, 75, 18);
            Scroll(table, "DeckCards", 350, 310, 780, 600, 5, new Vector2(140, 184));
            Label(table, "LibraryCaption", "카드 라이브러리", 1150, 175, 730, 42, 23, Gold);
            Input(table, "Search", "", 1150, 222, 730);
            Button(table, "TypeFilter", "종류: 전체", 1150, 282, 225, 44);
            Button(table, "ArchetypeFilter", "카드군: 전체", 1390, 282, 250, 44);
            Button(table, "RarityFilter", "희귀도: 전체", 1655, 282, 225, 44);
            Scroll(table, "LibraryCards", 1150, 340, 730, 570, 4, new Vector2(164, 184));
            Label(table, "Feedback", "", 350, 922, 1000, 90, 18);
            Button(table, "Back", "로비로", 40, 950, 270, 60);
            Button(table, "CancelEdits", "변경 취소", 1390, 950, 230, 60);
            Button(table, "Save", "덱 저장", 1650, 950, 230, 60, true);
            Dialog(table);
            UpgradeCardPresentationAssets.ConfigureEditor(root);
            UpgradeSampleDeckAssets.Configure(root);
            SaveScene(root, "DeckEditorScene");
        }

        static void MatchPanel(Transform parent)
        {
            var panel = Box(parent, "MatchPanel", 650, 220, 1230, 680, Panel, true);
            panel.AddComponent<LobbyPanelView>();
            Label(
                panel.transform,
                "Heading",
                "対戦の間  /  온라인 대전",
                30,
                22,
                1150,
                48,
                28,
                Gold
            );
            Input(panel.transform, "ServerAddress", "ws://127.0.0.1:7777", 30, 90, 680);
            Button(panel.transform, "Connect", "접속 / 재접속", 740, 90, 220, 48, true);
            Button(panel.transform, "Disconnect", "연결 해제", 980, 90, 220, 48);
            Label(panel.transform, "Status", "OFFLINE", 30, 150, 1150, 80, 19);
            Input(panel.transform, "MatchId", "", 30, 255, 660);
            Label(panel.transform, "MatchHint", "참가할 Match ID", 40, 230, 600, 24, 14, Gold);
            Button(panel.transform, "Create", "매치 생성", 730, 255, 220, 48);
            Button(panel.transform, "Join", "매치 참가", 975, 255, 220, 48);
            Button(panel.transform, "Deck", "서버 덱 선택", 30, 330, 660, 48);
            Button(panel.transform, "Ready", "준비", 730, 330, 465, 48, true);
            Label(panel.transform, "Seats", "", 30, 405, 1150, 90, 22);
            Button(panel.transform, "Rematch", "재대전", 30, 535, 260, 48);
            Button(panel.transform, "Leave", "방 나가기", 315, 535, 260, 48);
            Button(panel.transform, "Close", "방 닫기", 600, 535, 260, 48);
            Label(panel.transform, "ServerDeckSummary", "", 30, 600, 1150, 66, 16, Gold);
            AddMatchmakingControls(panel.transform);
            panel.SetActive(false);
        }

        static void AddMatchmakingControls(Transform panel)
        {
            if (panel.Find("QueueMatch") == null)
            {
                Button(panel, "QueueMatch", "자동 매칭 시작", 730, 22, 220, 48, true);
            }
            if (panel.Find("CancelQueue") == null)
            {
                Button(panel, "CancelQueue", "매칭 취소", 975, 22, 220, 48);
            }
            var heading = panel.Find("Heading") as RectTransform;
            if (heading != null)
            {
                heading.sizeDelta = new Vector2(660, heading.sizeDelta.y);
            }
        }

        [MenuItem("Project C/Upgrade lobby matchmaking UI")]
        public static void UpgradeMatchmaking()
        {
            const string PrefabPath = "Assets/Prefabs/FrontEnd/LobbyScreen.prefab";
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var panel = root.GetComponentInChildren<LobbyPanelView>(true);
                if (panel == null)
                {
                    throw new InvalidOperationException("LobbyScreen is missing LobbyPanelView.");
                }
                font = panel.GetComponentInChildren<TMP_Text>(true).font;
                AddMatchmakingControls(panel.transform);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        public static void ValidateMatchmakingScene()
        {
            UpgradeMatchmaking();
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
            var panel = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<LobbyPanelView>(true))
                .Single();
            foreach (string name in new[] { "QueueMatch", "CancelQueue" })
            {
                var button = panel.GetComponentsInChildren<Button>(true)
                    .SingleOrDefault(candidate => candidate.name == name);
                if (button == null || button.GetComponentInChildren<TMP_Text>(true) == null)
                {
                    throw new InvalidOperationException("LobbyScene is missing matchmaking button: " + name);
                }
                Debug.Log("LobbyScene matchmaking UI verified: " + name);
            }
        }

        static void Settings(Transform parent)
        {
            var panel = Box(parent, "SettingsPanel", 650, 220, 1230, 680, Panel, true);
            Label(panel.transform, "Heading", "設定  /  설정", 30, 30, 1100, 55, 28, Gold);
            Label(panel.transform, "VolumeLabel", "음량", 30, 140, 240, 50, 22);
            var track = Box(panel.transform, "Volume", 300, 145, 650, 40, Ink, true);
            var slider = track.AddComponent<Slider>();
            var fill = Box(track.transform, "Fill", 0, 0, 650, 40, Red);
            slider.fillRect = (RectTransform)fill.transform;
            slider.minValue = 0;
            slider.maxValue = 1;
            slider.value = 1;
            var toggle = Box(panel.transform, "Fullscreen", 300, 240, 44, 44, Ink, true);
            var t = toggle.AddComponent<Toggle>();
            t.targetGraphic = toggle.GetComponent<Image>();
            t.graphic = Box(toggle.transform, "Check", 8, 8, 28, 28, Gold).GetComponent<Image>();
            Label(panel.transform, "FullLabel", "전체 화면", 30, 235, 240, 55, 22);
            Button(panel.transform, "CloseSettings", "닫기", 900, 570, 270, 60);
            panel.SetActive(false);
        }

        static void MakeLobby()
        {
            var root = Canvas("LobbyScreen", out var table);
            root.AddComponent<LobbyScenePresenter>();
            Label(table, "Title", "百鬼夜行", 40, 10, 800, 65, 46, Gold);
            Label(
                table,
                "Subtitle",
                "안개 너머, 당신의 행렬을 준비하세요.",
                650,
                100,
                1200,
                70,
                25
            );
            // Original shrine silhouettes and paper seals, saved as ordinary editable Images/Text.
            Box(table, "ShrineLintel", 900, 310, 690, 28, new Color(.22f, .10f, .085f));
            Box(table, "ShrineBeam", 940, 370, 610, 18, new Color(.25f, .13f, .1f));
            Box(table, "ShrineLeft", 1000, 325, 24, 470, new Color(.18f, .10f, .09f));
            Box(table, "ShrineRight", 1490, 325, 24, 470, new Color(.18f, .10f, .09f));
            Label(
                table,
                "Seal",
                "妖",
                1140,
                400,
                250,
                250,
                150,
                new Color(.29f, .23f, .16f)
            ).alignment = TextAlignmentOptions.Center;
            Label(
                table,
                "Welcome",
                "YOUR PROCESSION AWAITS",
                900,
                800,
                720,
                55,
                23,
                Gold
            ).alignment = TextAlignmentOptions.Center;
            Button(table, "BattleMenu", "一  대전 시작", 40, 230, 520, 100, true);
            Button(table, "DeckManager", "二  덱 관리", 40, 350, 520, 100);
            Button(table, "SettingsMenu", "三  설정", 40, 470, 520, 100);
            Box(table, "DeckSummaryPanel", 40, 620, 520, 235, Panel);
            Label(table, "DeckSummaryHeading", "선택한 개인 덱", 64, 640, 470, 40, 23, Gold);
            Label(table, "SelectedPersonalDeck", "", 64, 690, 470, 145, 21);
            Button(table, "ResumeBattle", "진행 중인 대전으로", 40, 880, 520, 65, true);
            Label(table, "Notice", "", 650, 925, 1230, 80, 18);
            MatchPanel(table);
            Settings(table);
            SaveScene(root, "LobbyScene");
        }

        static void SaveScene(GameObject screen, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(
                screen,
                Folder + "/" + screen.name + ".prefab"
            );
            Object.DestroyImmediate(screen);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/ClientServices.prefab")
            );
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10);
            camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camera.GetComponent<Camera>().backgroundColor = Ink;
            var es = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule)
            );
            var input = es.GetComponent<InputSystemUIInputModule>();
            const string actionsPath = "Assets/InputSystem_Actions.inputactions";
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(actionsPath);
            var references = AssetDatabase
                .LoadAllAssetsAtPath(actionsPath)
                .OfType<InputActionReference>()
                .ToArray();
            InputActionReference Reference(string name) =>
                references.First(r => r.action.id == actions.FindAction("UI/" + name, true).id);
            input.actionsAsset = actions;
            input.point = Reference("Point");
            input.leftClick = Reference("Click");
            input.rightClick = Reference("RightClick");
            input.middleClick = Reference("MiddleClick");
            input.scrollWheel = Reference("ScrollWheel");
            input.move = Reference("Navigate");
            input.submit = Reference("Submit");
            input.cancel = Reference("Cancel");
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + name + ".unity");
        }

        [MenuItem("Project C/Author lobby and deck editor (explicit)")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory("Assets/Resources/CardData");
            string data = Path.GetFullPath("../ProjectC.Server/Data/HyakkiYagyo");
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; ++i)
            {
                if (args[i] == "-projectcServerData")
                {
                    data = args[i + 1];
                }
            }
            File.Copy(
                Path.Combine(data, "Cards.json"),
                "Assets/Resources/CardData/HyakkiCards.json",
                true
            );
            File.Copy(
                Path.Combine(data, "Decks.json"),
                "Assets/Resources/CardData/HyakkiDecks.json",
                true
            );
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/Harmonic SDF.asset");
            if (font == null)
            {
                throw new InvalidOperationException("Harmonic SDF font is required");
            }
            var services = new GameObject(
                "ClientServices",
                typeof(MultiplayerDebugScreen),
                typeof(ClientApp)
            );
            PrefabUtility.SaveAsPrefabAsset(services, Folder + "/ClientServices.prefab");
            Object.DestroyImmediate(services);
            var tile = Tile();
            MakeEditor(tile);
            MakeLobby();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/Scenes/LobbyScene.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/DeckEditorScene.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/BattleScene.unity", true),
            };
            AssetDatabase.SaveAssets();
        }
    }
}
