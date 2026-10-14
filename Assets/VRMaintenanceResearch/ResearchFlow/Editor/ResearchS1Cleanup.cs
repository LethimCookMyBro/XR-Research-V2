#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Archives project assets outside Assets/: copy (asset + .meta), verify SHA256, then Recycle Bin via Unity. Never permanent delete.
    public static class ResearchS1Cleanup
    {
        public static string Archive(string archiveRoot, string[] assetPaths)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var open = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(i => EditorSceneManager.GetSceneAt(i).path).ToArray();
            var log = new List<string>();
            foreach (var asset in assetPaths)
            {
                if (open.Contains(asset)) throw new InvalidOperationException("Asset is open: " + asset);
                if (!File.Exists(asset)) throw new FileNotFoundException(asset);
                string guid = AssetDatabase.AssetPathToGUID(asset);
                foreach (var file in new[] { asset, asset + ".meta" })
                {
                    string target = Path.Combine(archiveRoot, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, false);
                    if (Sha(file) != Sha(target)) throw new IOException("Archive copy mismatch: " + file);
                }
                bool trashed = AssetDatabase.MoveAssetToTrash(asset);
                log.Add(asset + "\t" + guid + "\t" + Sha(Path.Combine(archiveRoot, asset)) + "\t-> " + Path.Combine(archiveRoot, asset) + "\trecycle_bin=" + trashed);
            }
            File.AppendAllLines(Path.Combine(archiveRoot, "MAPPING.tsv"), log);
            return string.Join("\n", log);
        }

        static string Sha(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
