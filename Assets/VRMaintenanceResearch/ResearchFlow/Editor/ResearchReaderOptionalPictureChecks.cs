#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Uses Unity's actual JSON serializer, not a desktop JSON surrogate.</summary>
    public static class ResearchReaderOptionalPictureChecks
    {
        const string Root = "Assets/VRMaintenanceResearch/ResearchFlow/Resources/";
        const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
        { var scene = SceneManager.GetSceneAt(i); return scene.handle + ":" + scene.path + ":" + scene.isDirty; }).ToArray();

        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Path.IsPathRooted(evidenceDirectory)
                || Directory.Exists(evidenceDirectory))
                throw new InvalidOperationException("Stopped Editor and fresh absolute evidence directory required.");
            var rows = new List<object>();
            var failures = new List<string>();
            var beforeScenes = Scenes();
            var packageFiles = AssetDatabase.FindAssets("package t:TextAsset", new[] { Root.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith(Root + "ResearchReaderS1/", StringComparison.Ordinal)
                    || p.StartsWith(Root + "ResearchReaderDemo/", StringComparison.Ordinal))
                .Where(p => p.EndsWith("/package.json", StringComparison.Ordinal)).OrderBy(p => p).ToArray();
            var hashes = packageFiles.ToDictionary(p => p, p => File.ReadAllBytes(p));
            var cache = (IDictionary)typeof(ResearchReferenceViewer).GetField("registries", HiddenStatic).GetValue(null);
            var savedCache = new List<DictionaryEntry>();
            foreach (DictionaryEntry item in cache) savedCache.Add(item);
            void Check(string id, bool pass, object detail = null)
            { rows.Add(new { id, pass, detail }); if (!pass) failures.Add(id); }
            void Case(string id, Action action)
            { try { action(); } catch (Exception error) { Check(id + ":exception", false, error.ToString()); } }

            try
            {
                cache.Clear();
                Check("installed_packages_found", packageFiles.Length > 0, packageFiles.Length);
                foreach (var path in packageFiles)
                {
                    string resource = path.Substring(Root.Length, path.Length - Root.Length - ".json".Length);
                    Case(resource, () =>
                    {
                        var asset = Resources.Load<TextAsset>(resource);
                        Check(resource + ":resource_loaded", asset, path);
                        if (!asset) return;
                        var package = JsonUtility.FromJson<ResearchReferenceViewer.Package>(asset.text);
                        string error = ResearchReferenceViewer.ValidatePackage(package);
                        Check(resource + ":unity_deserialization_valid", error == null, new
                        {
                            error,
                            first_picture_materialized = package?.steps?.FirstOrDefault()?.picture != null,
                            first_picture_resource = package?.steps?.FirstOrDefault()?.picture?.resource
                        });
                        foreach (string format in ResearchReferenceScheme.Formats)
                            Check(resource + ":available:" + format, ResearchReferenceViewer.Available(format, resource));
                        Resources.UnloadAsset(asset);
                    });
                }

                string trainingPath = Root + "ResearchReaderS1/T6_th/package.json";
                string illustratedPath = Root + "ResearchReaderS1/A6_th/package.json";
                var training = JObject.Parse(File.ReadAllText(trainingPath));
                var illustrated = JObject.Parse(File.ReadAllText(illustratedPath));
                // V6 Steps are text-only since 31 Oct; a real installed Reader image supplies the valid resource/hash pair.
                var image = illustrated["images"][0];
                var validPicture = new JObject { ["resource"] = image["resource"].DeepClone(), ["sha256"] = image["sha256"].DeepClone() };
                void PictureCase(string id, JToken picture, bool omit, bool valid)
                {
                    Case(id, () =>
                    {
                        var input = (JObject)training.DeepClone();
                        var step = (JObject)input["steps"][0];
                        step.Remove("picture");
                        if (!omit) step["picture"] = picture?.DeepClone() ?? JValue.CreateNull();
                        var actual = JsonUtility.FromJson<ResearchReferenceViewer.Package>(input.ToString(Formatting.None));
                        string error = ResearchReferenceViewer.ValidatePackage(actual);
                        Check(id, valid ? error == null : error != null, new
                        {
                            expected_valid = valid, error,
                            picture_materialized = actual.steps[0].picture != null,
                            resource = actual.steps[0].picture?.resource,
                            sha256 = actual.steps[0].picture?.sha256
                        });
                    });
                }
                PictureCase("optional_picture:omitted", null, true, true);
                PictureCase("optional_picture:null", JValue.CreateNull(), false, true);
                PictureCase("optional_picture:empty_object", new JObject(), false, true);
                PictureCase("optional_picture:empty_fields", new JObject { ["resource"] = "", ["sha256"] = "" }, false, true);
                PictureCase("optional_picture:valid_asset", validPicture, false, true);
                PictureCase("optional_picture:resource_only", new JObject { ["resource"] = validPicture["resource"].ToString() }, false, false);
                PictureCase("optional_picture:hash_only", new JObject { ["sha256"] = validPicture["sha256"].ToString() }, false, false);
                PictureCase("optional_picture:unsafe_resource", new JObject
                { ["resource"] = "ResearchReaderS1/../outside", ["sha256"] = validPicture["sha256"].ToString() }, false, false);
                PictureCase("optional_picture:foreign_root", new JObject
                { ["resource"] = "OtherResource/image", ["sha256"] = validPicture["sha256"].ToString() }, false, false);
                PictureCase("optional_picture:malformed_hash", new JObject
                { ["resource"] = validPicture["resource"].ToString(), ["sha256"] = "not-a-sha256" }, false, false);

                Case("step_picture_integrity", () =>
                {
                    string resource = validPicture["resource"].ToString(), expected = validPicture["sha256"].ToString();
                    var asset = Resources.Load<TextAsset>(resource);
                    Check("step_picture:real_asset_loaded", asset, resource);
                    if (!asset) return;
                    var bytes = asset.bytes;
                    Check("step_picture:original_hash_valid", ResearchReferenceViewer.HashMatches(bytes, expected));
                    var changed = (byte[])bytes.Clone(); changed[changed.Length / 2] ^= 1;
                    Check("step_picture:tampered_bytes_rejected", !ResearchReferenceViewer.HashMatches(changed, expected));
                    Check("step_picture:wrong_hash_rejected", !ResearchReferenceViewer.HashMatches(bytes, new string('0', 64)));
                    Resources.UnloadAsset(asset);
                });
            }
            catch (Exception error) { Check("test_setup_exception", false, error.ToString()); }
            finally
            {
                cache.Clear();
                foreach (var item in savedCache) cache.Add(item.Key, item.Value);
                Check("registry_cache_restored", cache.Count == savedCache.Count
                    && savedCache.All(item => ReferenceEquals(cache[item.Key], item.Value)));
                Check("scenes_preserved", beforeScenes.SequenceEqual(Scenes()));
                Check("package_files_preserved", hashes.All(item => File.ReadAllBytes(item.Key).SequenceEqual(item.Value)));
            }
            Directory.CreateDirectory(evidenceDirectory);
            string status = failures.Count == 0 ? "PASS" : "ISSUES";
            File.WriteAllText(Path.Combine(evidenceDirectory, "report.json"), JsonConvert.SerializeObject(new
            {
                status, evidence = "ACTUAL_UNITY_JSON_DESERIALIZATION_AND_RESOURCES_NOT_NATIVE_WEARER",
                package_count = packageFiles.Length, checks = rows.Count, failures, rows
            }, Formatting.Indented), new UTF8Encoding(false));
            return "READER_OPTIONAL_PICTURE " + status + " packages=" + packageFiles.Length + " checks=" + rows.Count + " failures=" + failures.Count;
        }
    }
}
#endif
