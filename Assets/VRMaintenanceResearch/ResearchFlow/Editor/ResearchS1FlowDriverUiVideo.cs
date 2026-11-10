#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Video v8 (owner 8 Nov): the Examine / Inspect / result chapters show the running app, recorded from the participant's eye while
    // the driver presses the real controls. A second camera follows the head and renders to 1280x720 at up to 24 fps; frames carry
    // their capture time so the encoder keeps real timing. Editor, simulated controllers; the app UI shown is the real UI.
    public static partial class ResearchS1FlowDriver
    {
        static Camera uiRecCamera; static string uiRecDir; static double uiRecStart, uiRecNext, uiRecEnd; static readonly List<object> uiRecFrames = new List<object>();
        public static readonly List<object> UiVideoRows = new List<object>();
        public static string UiVideoReport() => JsonConvert.SerializeObject(UiVideoRows);

        static void UiRecordStart(string dir, double seconds)
        {
            Directory.CreateDirectory(dir); foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
            uiRecFrames.Clear(); uiRecDir = dir;
            var head = controller.view.Head.GetComponent<Camera>() ?? Camera.main;
            if (!uiRecCamera) { uiRecCamera = new GameObject("S1UiVideoCamera") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>(); uiRecCamera.enabled = false; }
            uiRecCamera.CopyFrom(head); uiRecCamera.stereoTargetEye = StereoTargetEyeMask.None; uiRecCamera.fieldOfView = 60; uiRecCamera.aspect = 16f / 9;
            uiRecStart = uiRecNext = EditorApplication.timeSinceStartup; uiRecEnd = uiRecStart + seconds;
            EditorApplication.update -= UiRecordTick; EditorApplication.update += UiRecordTick;
        }

        static void UiRecordTick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (!EditorApplication.isPlaying || now > uiRecEnd || !controller) { UiRecordStop(); return; }
            if (now < uiRecNext) return;
            uiRecNext += 1.0 / 24; if (uiRecNext < now) uiRecNext = now;
            var head = controller.view.Head;
            uiRecCamera.transform.SetPositionAndRotation(head.position, head.rotation);
            string file = uiRecFrames.Count.ToString("0000") + ".png";
            File.WriteAllBytes(Path.Combine(uiRecDir, file), ResearchS1ReaderMediaBuilder.Render(uiRecCamera, null));
            uiRecFrames.Add(new { file, t = System.Math.Round(now - uiRecStart, 4) });
        }

        static void UiRecordStop()
        {
            EditorApplication.update -= UiRecordTick;
            if (uiRecDir != null) File.WriteAllText(Path.Combine(uiRecDir, "frames.json"), JsonConvert.SerializeObject(new { seconds = uiRecEnd - uiRecStart, frames = uiRecFrames }));
            uiRecDir = null;
        }

        static IEnumerator UiRecordWhile(string dir, double seconds, IEnumerator action)
        {
            UiRecordStart(dir, seconds);
            if (action != null) yield return action;
            while (uiRecDir != null) yield return null;
            UiVideoRows.Add(new { dir, seconds, frames = Directory.GetFiles(dir, "*.png").Length, state = controller.Flow?.State.ToString(), detail = Detail });
        }

        static IEnumerator FaceMain()
        {
            var main = (RectTransform)controller.view.mainPanel.transform;
            ResearchFlowVisualInput.SetHeadFacing(main.TransformPoint(main.rect.center)); yield return Settle(); yield return Wait(.4);
        }

        // Inspect from the Main panel; the result stays on the panel while recording continues.
        static IEnumerator UiInspect() { yield return FaceMain(); yield return RayClick("inspect"); }

        public static string UiVideoInspect(string dir, double seconds) => Start(UiInspectRoutine(dir, seconds), seconds + 60);
        static IEnumerator UiInspectRoutine(string dir, double seconds)
        {
            Require(controller.Flow.IsTaskActive, "UI video needs an active task.");
            yield return UiRecordWhile(dir, seconds, UiInspect());
        }

        // Result chapter: hold on the result already shown on the Main panel (call right after UiVideoInspect or a real Inspect).
        public static string UiVideoHoldMain(string dir, double seconds) => Start(UiHoldRoutine(dir, seconds), seconds + 30);
        static IEnumerator UiHoldRoutine(string dir, double seconds) { yield return FaceMain(); yield return UiRecordWhile(dir, seconds, null); }

        // Examine chapter: point at a real part, press its chip, then look at the Main panel where the values appear.
        public static string UiVideoExamine(string dir, string stableTarget, double seconds) => Start(UiExamineRoutine(dir, stableTarget, seconds), seconds + 60);
        static IEnumerator UiExamineRoutine(string dir, string stableTarget, double seconds)
        {
            Require(controller.Flow.IsTaskActive, "UI video needs an active task.");
            IEnumerator Act() { yield return BatchExaminePartRoutine(stableTarget); yield return Wait(.6); yield return FaceMain(); }
            yield return UiRecordWhile(dir, seconds, Act());
        }
    }
}
#endif
