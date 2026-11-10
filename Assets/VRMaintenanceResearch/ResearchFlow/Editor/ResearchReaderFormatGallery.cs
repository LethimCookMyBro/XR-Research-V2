#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Owner 31 Oct: the four formats looked alike in the headset. Render the wearer's view of each format in the active task.
    public static partial class ResearchS1FlowDriver
    {
        public static string ReaderFormatGallery(string directory) => Start(ReaderFormatGalleryRoutine(directory), 120);

        static IEnumerator ReaderFormatGalleryRoutine(string directory)
        {
            Require(controller.Flow.IsTaskActive, "Reader format gallery requires an active task.");
            System.IO.Directory.CreateDirectory(directory);
            var station = controller.stations[controller.CurrentStationIndex];
            ResearchFlowVisualInput.SetHeadMotion(Vector3.zero, 0);
            ResearchFlowVisualInput.SetHeadFacing(station.position + station.forward * 2f + Vector3.up * (float)controller.StationEyeHeight);
            yield return Wait(.3);
            if (!controller.ReferenceOpen) controller.OpenReference("None");
            yield return Wait(1);
            ResearchFlowVisualInput.SetHeadFacing(controller.view.referencePanel.transform.position);
            yield return Wait(.3);
            var written = new List<string>();
            foreach (var format in new[] { "user_manual", "step_by_step", "illustrated", "video" })
            {
                controller.Command("reference_tab." + format, "None");
                yield return Wait(format == "video" ? 4 : 1.5);
                var panel = (RectTransform)controller.view.referencePanel.transform;
                var head = controller.view.Head;
                var camera = head.GetComponent<Camera>();
                var texture = new RenderTexture(1600, 1200, 24);
                var previous = camera.targetTexture;
                camera.targetTexture = texture;
                camera.Render();
                camera.targetTexture = previous;
                RenderTexture.active = texture;
                var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                RenderTexture.active = null;
                string path = System.IO.Path.Combine(directory, controller.Flow.CurrentTask + "_" + format + ".png");
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image); texture.Release(); Object.DestroyImmediate(texture);
                written.Add(format + "=" + controller.ReferenceSection);
            }
            Detail = "READER_FORMAT_GALLERY " + string.Join(" ", written);
        }
    }
}
#endif
