using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectC.Client.Editor
{
    public static class ApplyCardAura
    {
        private const string Art = "Assets/Art/UI/BattleCardAura/";
        private const float AuraPadding = 18;

        [MenuItem("Project C/Apply silhouette card aura")]
        public static void Apply()
        {
            foreach (var file in Directory.GetFiles(Art, "*.png"))
            {
                AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(file);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 200;
                importer.spriteBorder = Vector4.zero;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
            var shader = Shader.Find("ProjectC/UI/CardFrameAura");
            if (shader == null)
            {
                throw new InvalidOperationException("Card aura shader must be imported before authoring prefabs.");
            }
            string path = Art + "CardFrameAura.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            foreach (var pair in new[] { ("BattleCard", "HandAura"), ("BattleFieldCard", "FieldAura") })
            {
                string prefab = "Assets/Prefabs/Battle/" + pair.Item1 + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(prefab);
                try
                {
                    var widget = root.GetComponent<CardWidget>();
                    var glow = root.GetComponentsInChildren<Image>(true).Single(image => image.name == "FrameGlow");
                    widget.Accent.enabled = false;
                    glow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + pair.Item2 + ".png");
                    glow.material = material;
                    glow.type = Image.Type.Simple;
                    glow.preserveAspect = false;
                    glow.useSpriteMesh = false;
                    glow.color = Color.clear;
                    glow.raycastTarget = false;
                    glow.rectTransform.SetParent(widget.Face, false);
                    glow.rectTransform.anchorMin = Vector2.zero;
                    glow.rectTransform.anchorMax = Vector2.one;
                    glow.rectTransform.pivot = new Vector2(.5f, .5f);
                    glow.rectTransform.offsetMin = new Vector2(-AuraPadding, -AuraPadding);
                    glow.rectTransform.offsetMax = new Vector2(AuraPadding, AuraPadding);
                    glow.rectTransform.localScale = Vector3.one;
                    glow.rectTransform.localRotation = Quaternion.identity;
                    glow.rectTransform.SetAsFirstSibling();
                    PrefabUtility.SaveAsPrefabAsset(root, prefab);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
