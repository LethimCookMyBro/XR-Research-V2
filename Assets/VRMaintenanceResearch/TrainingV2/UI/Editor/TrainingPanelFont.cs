#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace VRMaintenanceResearch.TrainingV2.Editor
{
    public static class TrainingPanelFont
    {
        public const string FontPath = "Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset";
        public static TMP_FontAsset EnsureFont()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai.ttf");
            if (source == null) throw new InvalidOperationException("Import bundled OFL NotoSansThai.ttf first.");
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "NotoSansThai SDF";
            string characters = string.Concat(TrainingStrings.Thai.Values) + "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZกขฃคฅฆงจฉชซฌญฎฏฐฑฒณดตถทธนบปผฝพฟภมยรลวศษสหฬอฮะัาำิีึืฺุูเแโใไๆ็่้๊๋์ํ๎๐๑๒๓๔๕๖๗๘๙<>+x?iII.%-:(),/";
            characters = new string(characters.Where(c => !char.IsControl(c)).Distinct().ToArray());
            if (!font.TryAddCharacters(characters, out string missing)) throw new InvalidOperationException("Training Thai glyph coverage failed: " + missing);
            AssetDatabase.CreateAsset(font, FontPath);
            font.material.name = "NotoSansThai SDF Material"; AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) { texture.name = "NotoSansThai SDF Atlas"; AssetDatabase.AddObjectToAsset(texture, font); }
            EditorUtility.SetDirty(font); AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_FONT_GLYPHS_PASS count=" + characters.Length + "; Thai shaping and wrapping require visual review.");
            return font;
        }
    }
}
#endif
