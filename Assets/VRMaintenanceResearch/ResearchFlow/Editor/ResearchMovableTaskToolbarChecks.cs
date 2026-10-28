#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Checks actual Refresh/layout objects in stopped Editor preview scenes, not Quest input.</summary>
    public static class ResearchMovableTaskToolbarChecks
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        public static string Run(string freshDirectory)
        {
            if (EditorApplication.isPlaying || !ResearchFloorPolicy.EditorFixtureAllowed)
                throw new InvalidOperationException("Use a stopped Editor without a running XR subsystem.");
            string directory = Path.GetFullPath(freshDirectory);
            if (Directory.Exists(directory)) throw new IOException("Fresh evidence directory required.");
            var donor = Resources.FindObjectsOfTypeAll<ResearchFlowView>().FirstOrDefault(v => v.thaiFont && v.japaneseFont && v.englishFont);
            if (!donor) throw new InvalidOperationException("Load the canonical view and fonts first.");
            string[] scenesBefore = SceneState();
            var activeBefore = SceneManager.GetActiveScene();
            string priorPolicy = ResearchUIPolicy.Active;
            var apply = typeof(ResearchUIPolicy).GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic);
            var fixtureType = typeof(ResearchVisualClarityChecks).GetNestedType("Fixture", BindingFlags.NonPublic);
            var checks = new List<object>();
            var failures = new List<string>();
            void Check(bool ok, string name)
            { checks.Add(new { name, pass = ok }); if (!ok) failures.Add(name); }
            Directory.CreateDirectory(directory);
            try
            {
                foreach (var policy in new[] { ResearchUIPolicy.DevMovable, ResearchUIPolicy.LegacyMovable, ResearchUIPolicy.FixedDemo })
                foreach (var language in new[] { ParticipantLanguage.Thai, ParticipantLanguage.Japanese })
                {
                    IDisposable owner = null;
                    try
                    {
                        apply.Invoke(null, new object[] { policy });
                        var cell = Path.Combine(directory, policy + "_" + language);
                        owner = (IDisposable)Activator.CreateInstance(fixtureType, Instance, null, new object[] { cell, donor, language }, null);
                        var view = (ResearchFlowView)fixtureType.GetField("View", Instance).GetValue(owner);
                        foreach (var state in new[] { ResearchFlowState.Task1Active, ResearchFlowState.Task2Active })
                        {
                            fixtureType.GetMethod("ChangeFlow", Instance).Invoke(owner, new object[] { state, 5 });
                            var toolbar = (GameObject)typeof(ResearchFlowView).GetField("mainToolbar", Instance).GetValue(view);
                            var handle = toolbar.transform.Find("DragHandle");
                            string label = policy + "/" + language + "/" + state;
                            bool expected = policy != ResearchUIPolicy.FixedDemo;
                            Check(handle && handle.gameObject.activeInHierarchy == expected, label + " drag handle follows policy, including its parent");
                            if (expected)
                            {
                                Check(view.mainPanel.movable && handle.GetComponent<ResearchDragHandle>() && handle.GetComponent<Graphic>().raycastTarget,
                                    label + " existing drag handler and raycast hit area retained");
                                Check(!toolbar.transform.Find("Minimize").gameObject.activeInHierarchy && !toolbar.transform.Find("Recenter").gameObject.activeInHierarchy,
                                    label + " task drag grip does not add minimize/recenter buttons");
                            }
                        }
                    }
                    catch (Exception error)
                    {
                        var cause = error is TargetInvocationException wrapper && wrapper.InnerException != null ? wrapper.InnerException : error;
                        Check(false, policy + "/" + language + " fixture exception: " + cause);
                    }
                    finally { owner?.Dispose(); }
                }
            }
            finally { apply.Invoke(null, new object[] { priorPolicy }); }
            Check(ResearchUIPolicy.Active == priorPolicy, "caller UI policy restored");
            Check(activeBefore == SceneManager.GetActiveScene() && scenesBefore.SequenceEqual(SceneState()), "caller scene paths/dirty/active preserved");
            string path = Path.Combine(directory, "RESULT.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(new {
                scope = "Stopped Editor preview fixture: real ResearchFlowView.Refresh/layout hierarchy. Seeded flow, no physical input, stereo placement or wearer approval.",
                checks, failures, pass = failures.Count == 0
            }, Formatting.Indented));
            return path;
        }
        static string[] SceneState() => Enumerable.Range(0, SceneManager.sceneCount)
            .Select(i => SceneManager.GetSceneAt(i)).Select(s => s.path + ":" + s.isDirty).ToArray();
    }
}
#endif
