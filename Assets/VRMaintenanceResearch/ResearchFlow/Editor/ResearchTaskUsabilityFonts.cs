#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchTaskUsabilityFonts
    {
        public static string Bake(string evidenceDirectory, string stepsTextPath = "Logs/TaskUsability20261030/v4-steps-font-text.json")
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Editor required.");
            if (!Path.IsPathRooted(evidenceDirectory) || Directory.Exists(evidenceDirectory)) throw new IOException("Fresh absolute evidence directory required.");
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>(FindObjectsInactive.Include);
            var fonts = new[] { view.englishFont, view.thaiFont, view.japaneseFont };
            var languages = new[] { "English", "Thai", "Japanese" };
            var codes = new[] { "en", "th", "ja" };
            foreach (var font in fonts) if (EditorUtility.IsDirty(font)) throw new InvalidOperationException("Preserve existing unsaved font: " + font.name);
            Directory.CreateDirectory(evidenceDirectory);
            var evidence = new List<object>();
            for (int i = 0; i < fonts.Length; i++)
            {
                var font = fonts[i];
                string path = AssetDatabase.GetAssetPath(font);
                string backup = Path.Combine(evidenceDirectory, Path.GetFileName(path));
                File.Copy(path, backup);
                string text = string.Concat(ResearchStrings.All(languages[i]).Values);
                // Only Steps are TMP text. PDF/illustrated/video glyphs are rendered by the media pipeline.
                var steps = JObject.Parse(File.ReadAllText(stepsTextPath));
                text += (string)steps[codes[i]];
                string chars = new string(text.Where(c => !char.IsControl(c)).Distinct().ToArray());
                uint[] before;
                font.HasCharacters(chars, out before, true, false);
                string missingNative;
                font.TryAddCharacters(chars, out missingNative);
                uint[] after;
                bool covered = font.HasCharacters(chars, out after, true, false);
                if (!covered) throw new InvalidOperationException(languages[i] + " missing glyphs: " + string.Join(",", after.Select(c => c.ToString("X"))));
                typeof(ResearchFlowSceneBuilder).GetMethod("PersistFontTextures", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { font });
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssetIfDirty(font);
                foreach (var atlas in font.atlasTextures) if (atlas) AssetDatabase.SaveAssetIfDirty(atlas);
                if (font.material) AssetDatabase.SaveAssetIfDirty(font.material);
                evidence.Add(new { language = languages[i], path, backup, missingBefore = before ?? Array.Empty<uint>(), covered, missingAfter = after ?? Array.Empty<uint>(), dirtyAfter = EditorUtility.IsDirty(font) });
            }
            File.WriteAllText(Path.Combine(evidenceDirectory, "glyphs.json"), JsonConvert.SerializeObject(evidence, Formatting.Indented));
            return "UI_AND_STEPS_GLYPHS_PASS languages=3";
        }
    }
}
#endif
