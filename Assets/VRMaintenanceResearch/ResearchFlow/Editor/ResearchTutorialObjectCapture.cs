#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    /// <summary>Renders the actual Training practice objects from the canonical scene into transparent images for the
    /// Training demo strip. Copies render in an isolated preview scene, so the open scene is never modified.</summary>
    public static class ResearchTutorialObjectCapture
    {
        public const string Folder = "Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchFlowTutorial/Objects";
        static readonly (string file, string source, Vector3 view)[] Targets =
        {
            ("mug", "GripHandle", new Vector3(-.55f, .55f, -1f)),
            ("stone", "PinchStone", new Vector3(-.55f, .55f, -1f)),
            ("module", "TRAINER_FX1_MODULE", new Vector3(-.35f, .8f, -1f)),
        };

        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stopped Editor required.");
            var sources = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)).ToArray();
            Directory.CreateDirectory(Folder);
            var preview = EditorSceneManager.NewPreviewScene();
            var written = new System.Collections.Generic.List<string>();
            try
            {
                var lightObject = new GameObject("Key"); SceneManager.MoveGameObjectToScene(lightObject, preview);
                var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f; light.shadows = LightShadows.None;
                lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
                var cameraObject = new GameObject("Capture"); SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.AddComponent<Camera>(); camera.scene = preview; camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.fieldOfView = 24; camera.allowHDR = false;
                foreach (var (file, name, view) in Targets)
                {
                    var source = sources.FirstOrDefault(t => t.name == name) ?? throw new InvalidOperationException("Practice object missing: " + name);
                    var copy = UnityEngine.Object.Instantiate(source.gameObject); SceneManager.MoveGameObjectToScene(copy, preview);
                    copy.transform.SetPositionAndRotation(Vector3.zero, source.rotation);
                    foreach (var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                    var renderers = copy.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
                    var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    float distance = bounds.extents.magnitude / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.08f;
                    camera.transform.position = bounds.center + view.normalized * distance;
                    camera.transform.LookAt(bounds.center); camera.nearClipPlane = distance / 20; camera.farClipPlane = distance * 4;
                    var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                    camera.targetTexture = target; for (int i = 0; i < 3; i++) camera.Render();
                    var image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                    RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); image.Apply(); RenderTexture.active = null;
                    camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target);
                    string path = Folder + "/" + file + ".png";
                    File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(copy);
                    written.Add(path);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            AssetDatabase.Refresh();
            foreach (string path in written)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Default; importer.alphaIsTransparency = true; importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear; importer.SaveAndReimport();
            }
            return string.Join(", ", written);
        }
    }
}
#endif
