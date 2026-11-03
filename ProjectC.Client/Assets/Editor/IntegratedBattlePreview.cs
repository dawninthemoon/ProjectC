using System;
using System.Linq;
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
                var dim = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "TurnDim");
                dim.GetComponent<CanvasGroup>().alpha = 0;
                var hand = new[] { Card(1, 3006), Card(2, 4005), Card(3, 1001), Card(4, 1002), Card(5, 1003) };
                p.Hand.Render(hand, 0, false, 0, false, null, null, null);
                p.OwnField.Render(new[] { Card(6, 3001), Card(7, 3004), Card(8, 4002) }, 5, false, 0, false, null, null, null);
                p.EnemyField.Render(new[] { Card(9, 4001) }, 5, false, 0, false, null, null, null);
                p.EnemyHand.Render(null, 5, true, 0, false, null, null, null);
                p.OwnPlayer.Render(new PlayerView { Life = 18, PlayerId = 1 }, true, true);
                p.EnemyPlayer.Render(new PlayerView { Life = 20, PlayerId = 2 }, false, false);
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
