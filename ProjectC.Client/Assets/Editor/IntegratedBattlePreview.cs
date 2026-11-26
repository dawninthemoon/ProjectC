using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    // Static authoring preview, with synthetic visible cards; does not connect to a match.
    public static class IntegratedBattlePreview
    {
        public static void UpgradeAndCapture()
        {
            UpgradeIntegratedBattleAssets.Upgrade();
            Capture();
        }

        [MenuItem("ProjectC/Capture Integrated Battle Preview")]
        public static void Capture()
        {
            Capture(false);
        }

        [MenuItem("ProjectC/Capture Resource Crest Preview")]
        public static void CaptureResourceCrests()
        {
            Capture(true);
        }

        [MenuItem("ProjectC/Capture Response Flow Preview")]
        public static void CaptureResponseFlow()
        {
            Capture(false, true);
        }

        private static void Capture(bool resourceCrests, bool responseFlow = false)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Battle/BattleScreen.prefab"));
            var cameraObject = new GameObject("BattlePreviewCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(root, scene);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.GetComponent<Camera>().scene = scene;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active;
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 540;
                camera.transform.position = new Vector3(0, 0, -100);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.02f, .04f, .06f);
                camera.targetTexture = target;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10;
                var p = root.GetComponent<BattlePresenter>();
                foreach (var behavior in root.GetComponents<MonoBehaviour>())
                {
                    behavior.enabled = false;
                }
                foreach (var name in new[] { "GraveyardPanel", "MatchResult", "EffectTargetPicker", "FullHistory", "ContextAction", "CardGallery" })
                {
                    var node = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                    node?.gameObject.SetActive(false);
                }
                foreach (var group in new[] { p.Effects.TurnGroup, p.Effects.PopupGroup, p.Effects.DrawGroup })
                {
                    group.alpha = 0;
                }
                var dim = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TurnDim");
                if (dim != null)
                {
                    dim.GetComponent<CanvasGroup>().alpha = 0;
                }
                var hand = new[] { Card(1, 3006), Card(2, 4005), Card(3, 1001), Card(4, 1002), Card(5, 1003) };
                p.Hand.Render(hand, 0, false, 0, false, null, null, null);
                p.OwnField.Render(new[] { Card(6, 3001), Card(7, 3004), Card(8, 4002) }, 5, false, 0, false, null, null, null);
                p.EnemyField.Render(new[] { Card(9, 4001) }, 5, false, 0, false, null, null, null);
                p.EnemyHand.Render(null, 5, true, 0, false, null, null, null);
                p.OwnPlayer.Render(new PlayerView { Life = 18, PlayerId = 1 }, true, true);
                p.EnemyPlayer.Render(new PlayerView { Life = 20, PlayerId = 2 }, false, false);
                if (resourceCrests || responseFlow)
                {
                    p.OwnPlayer.RenderResources(JObject.Parse("{\"Ki\":{\"Current\":0,\"Max\":5},\"Spirit\":{\"Current\":12,\"Max\":20}}"));
                    p.EnemyPlayer.RenderResources(JObject.Parse("{\"Blood\":{\"Current\":3,\"Max\":10},\"Spirit\":{\"Current\":9,\"Max\":20}}"));
                }
                p.TurnLabel.text = "내 턴 · 3";
                p.PhaseLabel.text = "메인 1";
                p.EndTurn.GetComponentInChildren<TMP_Text>().text = "다음 페이즈";
                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.name == "TurnTimer")
                    {
                        label.text = "02:46";
                    }
                    if (label.name.EndsWith("DeckCount"))
                    {
                        label.text = "25";
                    }
                    if (label.name == "YokaiValue")
                    {
                        label.text = "요력 2 / 10";
                    }
                }
                p.Detail.Show(null);
                foreach (var zone in p.Zones)
                {
                    zone.Layout?.Snap();
                }
                if (responseFlow)
                {
                    CaptureResponseStates(root, p, camera, target);
                    return;
                }
                if (resourceCrests)
                {
                    Save(root, camera, target, "ResourceCrests");
                    return;
                }
                Save(root, camera, target, "Compact");
                p.Hand.Layout.SetExpanded(true);
                ((RectTransform)p.Hand.transform).anchoredPosition = new Vector2(510, -840);
                ((RectTransform)p.Hand.transform).sizeDelta = new Vector2(1040, 244);
                p.Hand.Widgets[2].SetSelected(true);
                p.Hand.Layout.Snap();
                p.Detail.Show(hand[2]);
                Save(root, camera, target, "Expanded");
                var gallery = root.GetComponentInChildren<CardGalleryView>(true);
                gallery.Initialize(p);
                p.Hand.gameObject.SetActive(false);
                var picker = root.GetComponentInChildren<EffectTargetPicker>(true);
                var choice = new ActionChoiceView { MinCount = 1, MaxCount = 1, CanCancel = false,
                    Candidates = hand.Select((card, index) => new ActionChoiceCandidateView
                    { Token = "preview-" + index, PlayerId = 1, CardId = card.Id, Card = card, Zone = 0 }).ToArray() };
                picker.ShowServer(choice, token => { }, () => { }, () => { });
                picker.SetServerSelected(new System.Collections.Generic.List<string> { "preview-2" });
                Save(root, camera, target, "Gallery");
            }
            finally
            {
                RenderTexture.active = previous;
                cameraObject.GetComponent<Camera>().targetTexture = null;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static CardView YakouCard(int id, int definitionId)
        {
            var definition = CardLibrary.LoadAlpha02Resources().Find(definitionId);
            return new CardView
            {
                Yakou = true, Id = id, DefinitionId = definitionId,
                CardType = definition.Type == "Spell" ? 1 : 0,
                Attack = definition.Attack, Hp = definition.MaxHp, MaxHp = definition.MaxHp,
            };
        }

        private static void CaptureResponseStates(GameObject root, BattlePresenter presenter,
            Camera camera, RenderTexture target)
        {
            var detail = presenter.Detail;
            var gallery = root.GetComponentInChildren<CardGalleryView>(true);
            gallery.InitializeYakou(presenter.Hand.CardPrefab, detail, card => detail.Show(card), null, null);
            detail.Show(YakouCard(20, 2009));
            presenter.EndTurn.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.selection.pass");
            var first = YakouCard(21, 3020);
            var second = YakouCard(22, 4020);
            var request = new CardSelectionRequest
            {
                RequestId = "response-preview",
                TitleKey = "ui.selection.response",
                Items = new[]
                {
                    new CardSelectionItem { Key = "21:RespondWithNegation", Card = first,
                        SourceKey = "ui.selection.hand_source" },
                    new CardSelectionItem { Key = "22:RespondWithNegation", Card = second,
                        SourceKey = "ui.selection.hand_source" },
                },
                MinCount = 1, MaxCount = 1, AllowCancel = false,
            };
            gallery.ShowYakouSelection(request, null, null, null);
            presenter.EndTurn.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.selection.pass");
            presenter.Hand.gameObject.SetActive(false);
            gallery.SelectionBar.SetSelected(new[] { "21:RespondWithNegation" });
            Save(root, camera, target, "ResponseSelection");
            gallery.SelectionBar.ToggleVisibility();
            presenter.Hand.gameObject.SetActive(true);
            Save(root, camera, target, "ResponseHidden");
            presenter.Hand.gameObject.SetActive(false);
            gallery.ShowYakouPublicZones(BattleText.Get("ui.gallery.grave"),
                new[] { YakouCard(30, 2001), YakouCard(31, 3004) });
            Save(root, camera, target, "ResponseBrowse");
            gallery.Close();
            gallery.ShowYakouSelection(request, null, null, null);
            presenter.Hand.gameObject.SetActive(false);
            gallery.SelectionBar.SetSelected(new[] { "21:RespondWithNegation" });
            Save(root, camera, target, "ResponseReturn");
            gallery.Close();
            presenter.Hand.gameObject.SetActive(true);
            var chain = root.GetComponentInChildren<YakouChainResolutionView>(true);
            chain.gameObject.SetActive(true);
            var chainCards = new[] { YakouCard(20, 2009), first, second };
            for (int index = 0; index < chainCards.Length; ++index)
            {
                var slot = chain.transform.Find("ChainSlot" + (index + 1));
                slot.gameObject.SetActive(true);
                ((RectTransform)slot).anchoredPosition = new Vector2((index - 1) * 174, 0);
                slot.GetComponentInChildren<CardWidget>(true).Bind(chainCards[index], false, false, false, false);
                slot.GetComponentsInChildren<TMP_Text>(true)
                    .First(text => text.name == "ChainNumber" + (index + 1)).text = (index + 1).ToString();
            }
            Save(root, camera, target, "ChainResolution");
        }

        private static CardView Card(int id, int definition)
        {
            var card = CardPresentation.Library.Find(definition);
            return new CardView { Id = id, DefinitionId = definition, Attack = card.Attack,
                Hp = card.MaxHp, MaxHp = card.MaxHp, CardType = card.Type == "Spell" ? 1 : 0 };
        }

        private static void Save(GameObject root, Camera camera, RenderTexture target, string state)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            System.IO.Directory.CreateDirectory("Artifacts/IntegratedBattle");
            System.IO.File.WriteAllBytes("Artifacts/IntegratedBattle/" + state + ".png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
