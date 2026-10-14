using System;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Explicit import for the two owned tutorial cutouts; never changes unrelated textures.
    public static class ResearchTutorialArtImporter
    {
        public static string Import()
        {
            foreach (string name in new[] { "Grasp", "Release" })
            {
                string path = "Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchFlowTutorial/" + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!importer) throw new InvalidOperationException("Tutorial art texture missing: " + path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                var spriteSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(spriteSettings);
                spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(spriteSettings);
                importer.maxTextureSize = 512;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
                if (!AssetDatabase.LoadAssetAtPath<Sprite>(path)) throw new InvalidOperationException("Sprite import failed: " + path);
            }
            return "PASS: both owned transparent tutorial sprites imported at 512 maximum size";
        }
    }
}
