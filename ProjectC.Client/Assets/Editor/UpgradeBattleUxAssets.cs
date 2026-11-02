using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    public static class UpgradeBattleUxAssets
    {
        private const string Path = "Assets/Prefabs/Battle/BattleScreen.prefab";

        private static RectTransform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == name);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static RectTransform Rect(Transform parent, string name)
        {
            var existing = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
            if (existing != null)
            {
                return (RectTransform)existing;
            }
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TMP_Text Label(Transform parent, string name, TMP_Text template)
        {
            var existing = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
            if (existing != null)
            {
                return existing.GetComponent<TMP_Text>();
            }
            var label = Object.Instantiate(template, parent);
            label.name = name;
            label.raycastTarget = false;
            label.gameObject.SetActive(true);
            return label;
        }

        [MenuItem("ProjectC/Upgrade Battle UX")]
        public static void Upgrade()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                Configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("Battle UX prefab upgraded.");
        }

        public static void Configure(GameObject root)
        {
            var presenter = root.GetComponent<BattlePresenter>();
            var table = Find(root, "Table");
            var space = Rect(table, "CardInfoSpace");
            Place(space, 20, 150, 300, 650);
            space.SetSiblingIndex(Find(root, "Battlefield").GetSiblingIndex());
            var background = space.GetComponent<Image>() ?? space.gameObject.AddComponent<Image>();
            background.color = new Color(.04f, .075f, .095f);
            background.raycastTarget = false;
            // The reservation always stays present; only inspector content is hidden.
            var inspector = presenter.Detail;
            inspector.transform.SetParent(space, false);
            Place((RectTransform)inspector.transform, 0, 0, 300, 650);
            Place(inspector.Title.rectTransform, 12, 10, 276, 54);
            inspector.Title.fontSizeMin = 16;
            inspector.Title.fontSizeMax = 23;
            inspector.Title.enableAutoSizing = true;
            var mask = Rect(inspector.transform, "ArtworkMask");
            Place(mask, 12, 68, 276, 250);
            if (mask.GetComponent<RectMask2D>() == null)
            {
                mask.gameObject.AddComponent<RectMask2D>();
            }
            var artwork = Rect(mask, "ArtworkImage");
            artwork.anchorMin = Vector2.zero;
            artwork.anchorMax = Vector2.one;
            artwork.offsetMin = artwork.offsetMax = Vector2.zero;
            var image = artwork.GetComponent<AspectFillImage>() ?? artwork.gameObject.AddComponent<AspectFillImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            var placeholder = Label(mask, "ArtPlaceholder", inspector.Title);
            Place(placeholder.rectTransform, 0, 0, 276, 250);
            placeholder.text = "?";
            placeholder.alignment = TextAlignmentOptions.Center;
            var type = Label(inspector.transform, "CardType", inspector.Stats);
            Place(type.rectTransform, 12, 324, 276, 26);
            Place(inspector.Stats.rectTransform, 12, 352, 276, 34);
            inspector.Stats.fontSize = 17;
            var viewport = Find(inspector.gameObject, "DetailViewport");
            Place(viewport, 12, 392, 276, 242);
            var content = Find(inspector.gameObject, "DetailContent");
            content.sizeDelta = new Vector2(276, content.sizeDelta.y);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            foreach (var text in new[] { inspector.Rules, inspector.Forms })
            {
                text.rectTransform.sizeDelta = new Vector2(276, text.rectTransform.sizeDelta.y);
                text.textWrappingMode = TextWrappingModes.Normal;
            }
            inspector.gameObject.SetActive(false);
            // Keep the existing field, hit area, layouts, and zone references.
            Place(Find(root, "Battlefield"), 350, 225, 1355, 535);
            Place((RectTransform)presenter.EnemyField.transform, 370, 248, 1310, 220);
            Place((RectTransform)presenter.OwnField.transform, 370, 510, 1310, 230);
            Place(Find(root, "OwnFieldDropZone"), 360, 505, 1330, 245);
            Place(Find(root, "Midline"), 370, 485, 1310, 2);
            Place(Find(root, "EnemyFieldCaption"), 380, 218, 550, 28);
            Place(Find(root, "OwnFieldCaption"), 380, 480, 550, 28);
            Place((RectTransform)presenter.LogToggle.transform, 20, 810, 145, 40);
            Place((RectTransform)presenter.GraveyardToggle.transform, 175, 810, 145, 40);
            var unread = Find(root, "Unread");
            if (unread != null)
            {
                Place(unread, 140, 810, 25, 40);
            }
            presenter.BattlePhase.gameObject.SetActive(false);
            Place((RectTransform)presenter.EndTurn.transform, 1720, 465, 180, 74);
            presenter.EndTurn.GetComponentInChildren<TMP_Text>().text = BattleText.Get("ui.phase.to_battle");
            var timer = Label(table, "TurnTimer", presenter.PhaseLabel);
            Place(timer.rectTransform, 1720, 425, 180, 34);
            timer.fontSize = 18;
            timer.text = "";
            // Candidate selection is non-modal so the fixed inspector remains visible.
            var picker = Find(root, "EffectTargetPicker");
            Place(picker, 700, 275, 620, 440);
            var context = Find(root, "ContextAction");
            context.SetAsLastSibling();
        }
    }
}
