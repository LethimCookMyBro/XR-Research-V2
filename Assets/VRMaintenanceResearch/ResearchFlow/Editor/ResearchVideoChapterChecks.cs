#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchVideoChapterChecks
    {
        public static string Run()
        {
            int checks = 0;
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
            ResearchReferenceViewer.Chapter At(double start) => new ResearchReferenceViewer.Chapter { start_seconds = start };
            var scenes = new[] { At(0), At(17.35), At(28.92), At(42.25) };
            double Skip(double time, int direction) => ResearchReferenceViewer.SkipTarget(time, scenes, direction);
            Check(Skip(5, 1) == 17.35, "Next jumps to the next scene");
            Check(Skip(17.35, 1) == 28.92, "Next from a scene start goes to the following scene");
            Check(Skip(20, -1) == 17.35, "Back restarts the current scene");
            Check(Skip(18, -1) == 0, "Back near a scene start goes to the previous scene");
            Check(Skip(50, 1) == 60, "Next in the last scene keeps the 10 s step; Seek clamps it to the end");
            Check(ResearchReferenceViewer.SkipTarget(30, null, -1) == 20, "a video without chapters keeps 10 s steps");
            foreach (var name in new[] { "A_th", "A_ja", "B_th", "B_ja" })
            {
                var asset = Resources.Load<TextAsset>("ResearchReaderS1/" + name + "/package");
                var package = JsonUtility.FromJson<ResearchReferenceViewer.Package>(asset.text);
                Check(package.video.chapters != null && package.video.chapters.Length >= 10
                    && package.video.chapters.Zip(package.video.chapters.Skip(1), (a, b) => b.start_seconds > a.start_seconds).All(x => x),
                    name + " package ships ordered scene chapters");
            }
            return "VIDEO_CHAPTER_PASS checks=" + checks;
        }
    }
}
#endif
