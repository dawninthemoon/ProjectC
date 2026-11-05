using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ProjectC.Client.Editor
{
    public static class ApplyBattleFonts
    {
        private const string FontFolder = "Assets/Fonts/TMP";
        private const string PalettePath = "Assets/Resources/UI/BattleFontPalette.asset";

        [MenuItem("Project C/Apply localized TMP fonts")]
        public static void Apply()
        {
            Directory.CreateDirectory(FontFolder);
            Directory.CreateDirectory("Assets/Resources/UI");
            AssetDatabase.Refresh();
            var fonts = new Dictionary<string, TMP_FontAsset>();
            foreach (string name in new[] { "NotoSerifKR-Bold", "NotoSerifJP-Bold", "Pretendard-Medium", "Pretendard-SemiBold", "PretendardJP-Medium", "PretendardJP-SemiBold", "Cinzel-Bold" })
            {
                string path = FontFolder + "/" + name + ".asset";
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/" + name + ".ttf");
                    if (source == null)
                    {
                        throw new InvalidOperationException("Missing supplied font: " + name);
                    }
                    font = TMP_FontAsset.CreateFontAsset(source, 64, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                    font.name = name + " SDF";
                    font.material.name = name + " Material";
                    AssetDatabase.CreateAsset(font, path);
                    AssetDatabase.AddObjectToAsset(font.material, path);
                    foreach (var atlas in font.atlasTextures)
                    {
                        atlas.name = name + " Atlas";
                        AssetDatabase.AddObjectToAsset(atlas, path);
                    }
                }
                var serialized = new SerializedObject(font);
                serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                font.material.shader = Shader.Find("TextMeshPro/Distance Field");
                font.material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.015f, .035f, .04f, 1));
                font.material.SetFloat(ShaderUtilities.ID_OutlineWidth, name == "Cinzel-Bold" ? .1f : name.Contains("Medium") ? .035f : .065f);
                font.material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
                font.material.EnableKeyword("OUTLINE_ON");
                fonts.Add(name, font);
            }
            var lastFallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NotoSansKR-Regular.asset");
            foreach (var pair in fonts)
            {
                pair.Value.fallbackFontAssetTable = pair.Key == "Cinzel-Bold"
                    ? new List<TMP_FontAsset> { fonts["Pretendard-SemiBold"], fonts["PretendardJP-SemiBold"], lastFallback }
                    : new List<TMP_FontAsset> { lastFallback };
                string seed = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz /:+-.,()";
                if (pair.Key != "Cinzel-Bold")
                {
                    seed += pair.Key.Contains("JP") ? "メインバトルターン終了攻撃体力効果" : "메인배틀턴종료공격력체력효과요력";
                }
                pair.Value.TryAddCharacters(seed, out string missing);
                EditorUtility.SetDirty(pair.Value);
                EditorUtility.SetDirty(pair.Value.material);
            }
            var palette = AssetDatabase.LoadAssetAtPath<BattleFontPalette>(PalettePath);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<BattleFontPalette>();
                AssetDatabase.CreateAsset(palette, PalettePath);
            }
            var data = new SerializedObject(palette);
            Assign(data, "_koreanHeading", fonts["NotoSerifKR-Bold"]);
            Assign(data, "_japaneseHeading", fonts["NotoSerifJP-Bold"]);
            Assign(data, "_koreanBody", fonts["Pretendard-Medium"]);
            Assign(data, "_japaneseBody", fonts["PretendardJP-Medium"]);
            Assign(data, "_koreanHud", fonts["Pretendard-SemiBold"]);
            Assign(data, "_japaneseHud", fonts["PretendardJP-SemiBold"]);
            Assign(data, "_numbers", fonts["Cinzel-Bold"]);
            data.ApplyModifiedPropertiesWithoutUndo();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Battle", "Assets/Prefabs/FrontEnd" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    {
                        var role = Role(label);
                        var view = label.GetComponent<LocalizedBattleFont>() ?? label.gameObject.AddComponent<LocalizedBattleFont>();
                        view.Configure(role, palette);
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
        }

        private static BattleFontRole Role(TMP_Text label)
        {
            string name = label.name;
            if (name == "AttackBadge" || name == "HealthBadge" || name == "CostBadge" || name == "Life" || name == "Number"
                || name.EndsWith("Count") || name == "YokaiValue" || name == "EnemyYokaiValue" || name == "TurnTimer" || name == "Stats" || name == "DetailStats")
            {
                return BattleFontRole.Number;
            }
            if (name == "Rules" || name == "Forms" || name == "Description" || name == "DetailRules")
            {
                return BattleFontRole.Body;
            }
            if (name == "Name" || name.EndsWith("Title") || name == "CardName" || name == "DetailName" || name == "ResultLabel"
                || name == "Label" && label.transform.parent.name == "EndTurn")
            {
                return BattleFontRole.Heading;
            }
            return BattleFontRole.Hud;
        }

        private static void Assign(SerializedObject data, string field, TMP_FontAsset font)
        {
            data.FindProperty(field).objectReferenceValue = font;
        }
    }
}
