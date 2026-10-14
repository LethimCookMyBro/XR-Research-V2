#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchSecondPassCapture
    {
        public static void Environment()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Settled Edit Mode required");
            var view = UnityEngine.Object.FindFirstObjectByType<ResearchFlowView>();
            if (!view) throw new InvalidOperationException("ResearchFlow scene required");
            var cameraObject = new GameObject("SecondPass_EvidenceCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main); camera.enabled = false;
            camera.fieldOfView = 64;
            bool active = view.gameObject.activeSelf;
            try
            {
                view.gameObject.SetActive(false);
                Capture(camera, "environment_training", new Vector3(12.4f, 2.05f, 6.8f), new Vector3(9.7f, 1.3f, 3.7f));
                Capture(camera, "environment_computer", new Vector3(3.4f, 2.05f, 7.7f), new Vector3(-.5f, 1.25f, 4.1f));
                Capture(camera, "environment_fan", new Vector3(-6.8f, 2.05f, 6.8f), new Vector3(-10, 1.25f, 3.7f));
                Capture(camera, "environment_room_overview", new Vector3(11.7f, 2.2f, -2.7f), new Vector3(-5, 1.2f, -1.5f));
                Capture(camera, "environment_material_lighting", new Vector3(1.1f, 1.65f, 5.8f), new Vector3(0, .95f, 4.5f));
            }
            finally { view.gameObject.SetActive(active); UnityEngine.Object.DestroyImmediate(cameraObject); }
        }

        static void Capture(Camera camera, string name, Vector3 position, Vector3 target)
        {
            const string directory = "Logs/ResearchFlowEvidence/2026-09-22-UXSecondPass/final-screenshots";
            Directory.CreateDirectory(directory);
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            var old = camera.targetTexture; var active = RenderTexture.active;
            var render = new RenderTexture(1600, 1000, 24);
            var image = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render; camera.Render(); RenderTexture.active = render;
                image.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); image.Apply();
                File.WriteAllBytes(directory + "/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = old; RenderTexture.active = active;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(render);
            }
        }
    }
}
#endif
