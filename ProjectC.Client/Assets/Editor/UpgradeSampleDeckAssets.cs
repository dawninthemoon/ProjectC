using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.Client.Editor
{
    public static class UpgradeSampleDeckAssets
    {
        private const string PrefabPath = "Assets/Prefabs/FrontEnd/DeckEditorScreen.prefab";

        private static RectTransform Find(GameObject root, string name)
        {
            return root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == name);
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static void Configure(GameObject root)
        {
            Place(Find(root, "DetailStats"), 60, 590, 250, 70);
            Place((RectTransform)Find(root, "DetailContent").GetComponentInParent<ScrollRect>().transform,
                60, 670, 250, 220);
            var original = Find(root, "DeckList").GetComponent<TMP_Dropdown>();
            var sample = root.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault(d => d.name == "SampleDeckList");
            if (sample == null)
            {
                sample = Object.Instantiate(original, original.transform.parent);
                sample.name = "SampleDeckList";
                float difference = 300 - ((RectTransform)sample.transform).rect.width;
                foreach (var child in sample.GetComponentsInChildren<RectTransform>(true))
                {
                    if (child != sample.transform && child.rect.width > 100)
                    {
                        child.sizeDelta += new Vector2(difference, 0);
                    }
                }
                var arrow = sample.transform.Find("Arrow") as RectTransform;
                arrow.anchoredPosition = new Vector2(268, 0);
            }
            Place((RectTransform)sample.transform, 1410, 110, 300, 48);
            var library = CardLibrary.LoadResources();
            sample.ClearOptions();
            sample.AddOptions(CardLibrary.Samples(Resources.Load<TextAsset>("CardData/HyakkiDecks").text, library)
                .Select(deck => deck.Name).ToList());
            sample.captionText.enableAutoSizing = true;
            sample.captionText.fontSizeMin = 12;
            sample.captionText.fontSizeMax = 18;
            foreach (var item in new[] { ("NewDeck", 1080f, 100f), ("CopyDeck", 1190f, 100f),
                ("DeleteDeck", 1300f, 100f), ("ImportStarter", 1720f, 160f) })
            {
                var button = Find(root, item.Item1);
                Place(button, item.Item2, 110, item.Item3, 48);
                var text = button.GetComponentInChildren<TMP_Text>(true);
                Place(text.rectTransform, 8, 0, item.Item3 - 16, 48);
                text.enableAutoSizing = true;
                text.fontSizeMin = 12;
                text.fontSizeMax = 20;
                if (item.Item1 == "ImportStarter")
                {
                    text.text = library.Localization.GetText("ui.deck.import_sample");
                }
            }
        }

        [MenuItem("ProjectC/Upgrade Sample Deck Selection")]
        public static void Upgrade()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("Sample deck selector saved.");
        }
    }
}
