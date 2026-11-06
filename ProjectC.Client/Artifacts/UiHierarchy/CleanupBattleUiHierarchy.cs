using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectC.EditorTools
{
    // One-time structural migration. Never calls a visual authoring/Apply tool.
    public static class CleanupBattleUiHierarchy
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        private static readonly List<string> Changes = new List<string>();

        public static void Run()
        {
            foreach (string path in Directory.GetFiles("Assets/Prefabs/Battle", "*.prefab"))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var rootRect = (RectTransform)root.transform;
                    var rootCanvas = root.GetComponent<Canvas>();
                    Vector3 savedScale = rootRect.localScale;
                    Vector2 savedSize = rootRect.sizeDelta;
                    bool canvasEnabled = rootCanvas != null && rootCanvas.enabled;
                    if (rootCanvas != null)
                    {
                        rootCanvas.enabled = false;
                        rootRect.localScale = Vector3.one;
                        rootRect.sizeDelta = new Vector2(1920, 1080);
                    }
                    rootRect.ForceUpdateRectTransforms();
                    var before = root.GetComponentsInChildren<RectTransform>(true)
                        .ToDictionary(rect => rect, Corners);
                    var alternate = new Dictionary<Vector2, Dictionary<RectTransform, Vector3[]>>();
                    if (rootCanvas != null)
                    {
                        foreach (var size in new[] { new Vector2(1280, 720), new Vector2(2560, 1080), new Vector2(1920, 1200) })
                        {
                            rootRect.sizeDelta = size;
                            rootRect.ForceUpdateRectTransforms();
                            alternate[size] = before.Keys.ToDictionary(rect => rect, Corners);
                        }
                        rootRect.sizeDelta = new Vector2(1920, 1080);
                        rootRect.ForceUpdateRectTransforms();
                    }
                    bool screen = Path.GetFileNameWithoutExtension(path) == "BattleScreen";
                    if (screen)
                    {
                        Delete(root.transform, "Brand", "Edition", "ArenaGuides", "Midline", "EnemyFieldCaption", "OwnFieldCaption", "HandCaption", "AutoStartHint", "EnterBattle", "Graveyards", "LegacyDebugControls", "YokaiPanel", "TurnSocket", "TimeSocket", "TurnUnit", "TimeUnit", "TurnOwnerAccent", "TimerRingFrame", "PhaseInside", "TurnPhasePlate", "OwnResourceLabel");
                        foreach (var name in new[] { "EnemyPlayer", "OwnPlayer" })
                        {
                            Delete(Find(root.transform, name), "RenewalPortraitFrame", "LifeCaption", "Name", "Counts", "TurnMark", "LifeIcon");
                        }
                        Delete(Find(root.transform, "CostBody"), "BodyFill", "LeftRail", "RightRail", "Bottom");
                        Delete(Find(root.transform, "CostToggle"), "ChevronWell");
                        Delete(Find(root.transform, "RecentEvents"), "Header", "Recent");
                        Delete(Find(root.transform, "FullHistory"), "Header", "Lines");
                        foreach (string name in new[] { "OpponentDeck", "YourDeck", "History", "LobbyToggle" })
                        {
                            Delete(Find(root.transform, name), "Label");
                        }
                        var viewport = (RectTransform)Find(root.transform, "CostViewport");
                        var viewportPosition = viewport.anchoredPosition;
                        viewport.SetParent(Find(root.transform, "CostBody"), false);
                        viewport.anchoredPosition = viewportPosition;
                        Move(root.transform, "TurnOwner", "TurnBannerArt");
                        Move(root.transform, "Turn", "TurnBannerArt");
                        foreach (string prefix in new[] { "Own", "Enemy" })
                        {
                            var resources = Find(root.transform, prefix + "Resources");
                            Find(resources, "RenewalZone0").name = prefix + "DeckTagImage";
                            Find(resources, "RenewalZone1").name = prefix + "GraveTagImage";
                            var exile = Find(resources, "RenewalZone2");
                            exile.name = prefix + "ExileTagImage";
                            Move(root.transform, prefix + "ExileBrowse", exile);
                        }
                    }
                    if (root.GetComponent<ProjectC.Client.CardWidget>() != null)
                    {
                        Delete(root.transform, "Stats", "Status", "Accent", "RenewalTitle", "FieldStatusFrame");
                        foreach (string badge in new[] { "AttackBadgeFrame", "HealthBadgeFrame" })
                        {
                            var holder = Find(root.transform, badge);
                            // The baked frame contains both badge backgrounds; the container still controls visibility.
                            if (holder != null && holder.TryGetComponent<Image>(out var image))
                            {
                                Object.DestroyImmediate(image);
                            }
                        }
                        var inset = Find(root.transform, "Inset");
                        if (inset != null)
                        {
                            int artworkIndex = inset.GetSiblingIndex();
                            Move(root.transform, "ArtworkMask", inset.parent);
                            Find(root.transform, "ArtworkMask").SetSiblingIndex(artworkIndex);
                            Delete(root.transform, "Inset");
                        }
                        foreach (string name in new[] { "Name", "AttackBadgeFrame", "HealthBadgeFrame", "CostBadgeFrame" })
                        {
                            Move(root.transform, name, "RenewalFrame");
                        }
                    }
                    if (root.GetComponent<ProjectC.Client.PlayerPanelView>() != null)
                    {
                        Delete(root.transform, "LifeCaption", "Name", "Counts", "TurnMark");
                    }
                    foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        if (rect == root.transform)
                        {
                            continue;
                        }
                        string old = rect.name;
                        string next = old;
                        if (rect.GetComponent<TMP_Text>() != null && !old.EndsWith("Text", StringComparison.Ordinal))
                        {
                            next = old + "Text";
                        }
                        else if (rect.GetComponent<Button>() != null && !old.EndsWith("Button", StringComparison.Ordinal))
                        {
                            next = old + "Button";
                        }
                        else if (rect.GetComponent<Image>() != null && !old.EndsWith("Image", StringComparison.Ordinal)
                            && !old.EndsWith("Icon", StringComparison.Ordinal) && !old.EndsWith("Mask", StringComparison.Ordinal))
                        {
                            next = old + "Image";
                        }
                        if (old == "TurnBanner") { next = "TurnTransitionImage"; }
                        if (old == "TurnOwner") { next = "TurnOwnerText"; }
                        if (old == "TurnTimer") { next = "TimerText"; }
                        if (old == "TimeTrack") { next = "TimerTicksOffImage"; }
                        if (old == "TimeRing") { next = "TimerTicksOnImage"; }
                        if (old == "TurnBannerArt") { next = "TurnBannerImage"; }
                        if (old == "RenewalFrame") { next = "CardFrameImage"; }
                        if (old == "AttackBadgeFrame") { next = "AttackValueContainer"; }
                        if (old == "HealthBadgeFrame") { next = "HealthValueContainer"; }
                        if (old == "CostBadgeFrame") { next = "CostBadgeImage"; }
                        Rename(rect, next);
                    }
                    foreach (var pair in before)
                    {
                        if (pair.Key == null) { continue; }
                        var after = Corners(pair.Key);
                        for (int i = 0; i < after.Length; i++)
                        {
                            if (Vector3.Distance(pair.Value[i], after[i]) > .05f)
                            {
                                throw new InvalidOperationException(path + ": layout moved: " + pair.Key.name);
                            }
                        }
                    }
                    foreach (var sample in alternate)
                    {
                        rootRect.sizeDelta = sample.Key;
                        rootRect.ForceUpdateRectTransforms();
                        foreach (var pair in sample.Value)
                        {
                            if (pair.Key == null) { continue; }
                            var after = Corners(pair.Key);
                            for (int i = 0; i < after.Length; i++)
                            {
                                if (Vector3.Distance(pair.Value[i], after[i]) > .05f)
                                {
                                    throw new InvalidOperationException(path + ": responsive layout moved at " + sample.Key + ": " + pair.Key.name);
                                }
                            }
                        }
                    }
                    if (rootCanvas != null)
                    {
                        rootRect.localScale = savedScale;
                        rootRect.sizeDelta = savedSize;
                        rootCanvas.enabled = canvasEnabled;
                    }
                    Changes.Add("Verified " + path + " at reference and alternate sizes");
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Directory.CreateDirectory("Artifacts/UiHierarchy");
            File.WriteAllLines("Artifacts/UiHierarchy/names.tsv", Names.Select(pair => pair.Key + "\t" + pair.Value));
            File.WriteAllLines("Artifacts/UiHierarchy/changes.txt", Changes);
            AssetDatabase.SaveAssets();
        }

        private static Vector3[] Corners(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners;
        }

        private static Transform Find(Transform root, string name)
        {
            return root == null ? null : root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
        }

        private static void Delete(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                var target = Find(root, name);
                if (target != null)
                {
                    Changes.Add("Remove " + name);
                    Object.DestroyImmediate(target.gameObject);
                }
            }
        }

        private static void Rename(Transform target, string name)
        {
            if (target == null || target.name == name) { return; }
            if (Names.TryGetValue(target.name, out string previous) && previous != name)
            {
                throw new InvalidOperationException("Ambiguous rename " + target.name);
            }
            Names[target.name] = name;
            Changes.Add("Rename " + target.name + " -> " + name);
            target.name = name;
        }

        private static void Move(Transform root, string child, string parent)
        {
            Move(root, child, Find(root, parent));
        }

        private static void Move(Transform root, string child, Transform parent)
        {
            var target = Find(root, child) as RectTransform;
            if (target == null || parent == null || target.parent == parent) { return; }
            var corners = Corners(target);
            target.SetParent(parent, true);
            // Fixed decorative parents use fixed center anchors, retaining the authored pixel rectangle.
            target.anchorMin = target.anchorMax = new Vector2(.5f, .5f);
            var localMin = parent.InverseTransformPoint(corners[0]);
            var localMax = parent.InverseTransformPoint(corners[2]);
            target.sizeDelta = new Vector2(localMax.x - localMin.x, localMax.y - localMin.y);
            target.localPosition = new Vector3(Mathf.Lerp(localMin.x, localMax.x, target.pivot.x),
                Mathf.Lerp(localMin.y, localMax.y, target.pivot.y), target.localPosition.z);
            Changes.Add("Parent " + child + " -> " + parent.name);
        }
    }
}

