using UnityEngine;
using UnityEngine.UI;
namespace VRMaintenanceResearch.TrainingV2
{
    // A single lightweight UI mesh avoids texture assets, materials and per-panel stencil masks.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrainingRoundedGraphic : MaskableGraphic
    {
        [SerializeField] float radius = 20;
        public float Radius { get => radius; set { radius = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect; float corner = Mathf.Min(radius, Mathf.Min(r.width, r.height) * .5f);
            vh.AddVert(r.center, color, Vector2.zero);
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                Vector2 center = new Vector2(quadrant < 2 ? r.xMax - corner : r.xMin + corner, quadrant == 0 || quadrant == 3 ? r.yMax - corner : r.yMin + corner);
                for (int step = 0; step <= 8; step++)
                {
                    float angle = (90 - quadrant * 90 - step * 90f / 8) * Mathf.Deg2Rad;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * corner, color, Vector2.zero);
                }
            }
            const int count = 36;
            for (int i = 1; i <= count; i++) vh.AddTriangle(0, i, i == count ? 1 : i + 1);
        }
    }
}
