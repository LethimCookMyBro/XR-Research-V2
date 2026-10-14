using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    // One flat, opaque surface mesh; the retained component name preserves serialized scenes.
    [DisallowMultipleComponent]
    public sealed class ResearchGlassEffect : BaseMeshEffect
    {
        public float radius = 24;
        public bool panel;
        public bool outlineOnly;
        public float edgeAlpha = .42f;
        const int Segments = 40;

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            var rect = graphic.rectTransform.rect;
            if (rect.width <= 0 || rect.height <= 0) return;
            mesh.Clear();
            Color tint = graphic.color;
            if (outlineOnly)
            {
                Ring(mesh, rect, radius, 0, -4, tint, Vector2.zero);
                return;
            }
            if (panel)
                for (int i = 3; i >= 1; i--)
                    Ring(mesh, rect, radius, i * 7, (i - 1) * 7, new Color(.015f, .035f, .06f, .055f * tint.a), new Vector2(0, -7));
            mesh.AddVert(rect.center, tint, Vector2.zero);
            for (int i = 0; i < Segments; i++)
            {
                Vector2 p = Point(rect, radius, i);
                mesh.AddVert(p, tint, Vector2.zero);
            }
            int start = mesh.currentVertCount - Segments - 1;
            for (int i = 0; i < Segments; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % Segments);
            Ring(mesh, rect, radius, 0, -1.5f, new Color(.35f, .55f, .76f, edgeAlpha * tint.a), Vector2.zero);
        }

        static Vector2 Point(Rect rect, float r, int i)
        {
            r = Mathf.Clamp(r, 0, Mathf.Min(rect.width, rect.height) * .5f);
            int corner = i / 10;
            float angle = (90 - corner * 90 - (i % 10) * 90f / 9) * Mathf.Deg2Rad;
            var center = new Vector2(corner < 2 ? rect.xMax - r : rect.xMin + r, corner == 0 || corner == 3 ? rect.yMax - r : rect.yMin + r);
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
        }

        static void Ring(VertexHelper mesh, Rect rect, float r, float outer, float inner, Color color, Vector2 offset)
        {
            int start = mesh.currentVertCount;
            var a = new Rect(rect.xMin - outer, rect.yMin - outer, rect.width + outer * 2, rect.height + outer * 2);
            var b = new Rect(rect.xMin - inner, rect.yMin - inner, rect.width + inner * 2, rect.height + inner * 2);
            for (int i = 0; i < Segments; i++)
            {
                var p = Point(a, r + outer, i); var q = Point(b, r + inner, i);
                var shaded = color; shaded.a *= Mathf.Lerp(.3f, 1, Mathf.InverseLerp(rect.yMin, rect.yMax, p.y));
                mesh.AddVert(p + offset, shaded, Vector2.zero); mesh.AddVert(q + offset, shaded, Vector2.zero);
            }
            for (int i = 0; i < Segments; i++)
            {
                int next = (i + 1) % Segments;
                mesh.AddTriangle(start + i * 2, start + next * 2, start + i * 2 + 1);
                mesh.AddTriangle(start + i * 2 + 1, start + next * 2, start + next * 2 + 1);
            }
        }
    }
}
