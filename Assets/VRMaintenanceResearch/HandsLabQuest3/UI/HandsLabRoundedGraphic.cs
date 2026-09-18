using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.HandsLabQuest3
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HandsLabRoundedGraphic : MaskableGraphic
    {
        [SerializeField] float radius = 20;
        public float Radius { get => radius; set { radius = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            float r = Mathf.Clamp(radius, 0, Mathf.Min(rect.width, rect.height) * .5f);
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = new Vector2(corner < 2 ? rect.xMax - r : rect.xMin + r, corner == 0 || corner == 3 ? rect.yMax - r : rect.yMin + r);
                for (int step = 0; step <= 8; step++)
                {
                    float angle = (90 - corner * 90 - step * 90f / 8) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r, color, Vector2.zero);
                }
            }
            for (int i = 1; i <= 36; i++) mesh.AddTriangle(0, i, i == 36 ? 1 : i + 1);
        }
    }
}
