using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    // Small mesh icons share one stroke family and never depend on font glyph coverage.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ResearchUtilityIcon : MaskableGraphic
    {
        public string kind;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            void Line(Vector2 a, Vector2 b)
            {
                var n = new Vector2(-(b - a).y, (b - a).x).normalized * 2f;
                int i = mesh.currentVertCount;
                mesh.AddVert(a - n, color, Vector2.zero); mesh.AddVert(a + n, color, Vector2.zero);
                mesh.AddVert(b + n, color, Vector2.zero); mesh.AddVert(b - n, color, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
            }
            void Ring(Vector2 center, float radius)
            {
                const int segments = 18;
                for (int i = 0; i < segments; i++)
                {
                    float a = 2 * Mathf.PI * i / segments, b = 2 * Mathf.PI * (i + 1) / segments;
                    Line(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                         center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius);
                }
            }
            if (kind == "researcher_group")
            {
                Ring(new Vector2(-8, 6), 5); Ring(new Vector2(8, 6), 5);
                Line(new Vector2(-17, -13), new Vector2(-17, -5));
                Line(new Vector2(-17, -5), new Vector2(-10, 0));
                Line(new Vector2(-6, -13), new Vector2(6, -13));
                Line(new Vector2(6, -13), new Vector2(17, -13));
                Line(new Vector2(17, -13), new Vector2(17, -5));
                Line(new Vector2(17, -5), new Vector2(10, 0));
            }
            else if (kind == "researcher_person")
            {
                Ring(new Vector2(0, 7), 7);
                Line(new Vector2(-12, -14), new Vector2(-12, -6));
                Line(new Vector2(-12, -6), new Vector2(-5, -1));
                Line(new Vector2(-5, -1), new Vector2(5, -1));
                Line(new Vector2(5, -1), new Vector2(12, -6));
                Line(new Vector2(12, -6), new Vector2(12, -14));
                Line(new Vector2(-12, -14), new Vector2(12, -14));
            }
            else if (kind == "researcher_globe")
            {
                Ring(Vector2.zero, 15);
                Ring(Vector2.zero, 7);
                Line(new Vector2(-15, 0), new Vector2(15, 0));
                Line(new Vector2(0, -15), new Vector2(0, 15));
            }
            else if (kind == "researcher_order")
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = 9 - i * 9;
                    Line(new Vector2(-15, y), new Vector2(-9, y));
                    Line(new Vector2(-4, y), new Vector2(15, y));
                }
            }
            else if (kind == "pause") { Line(new Vector2(-6, -11), new Vector2(-6, 11)); Line(new Vector2(6, -11), new Vector2(6, 11)); }
            else if (kind == "stop")
            {
                mesh.AddVert(new Vector2(-10, -10), color, Vector2.zero); mesh.AddVert(new Vector2(-10, 10), color, Vector2.zero);
                mesh.AddVert(new Vector2(10, 10), color, Vector2.zero); mesh.AddVert(new Vector2(10, -10), color, Vector2.zero);
                mesh.AddTriangle(0, 1, 2); mesh.AddTriangle(0, 2, 3);
            }
            else if (kind == "play" || kind == "replay")
            {
                Line(new Vector2(-7, -11), new Vector2(-7, 11)); Line(new Vector2(-7, 11), new Vector2(11, 0)); Line(new Vector2(11, 0), new Vector2(-7, -11));
            }
            else if (kind == "previous" || kind == "next" || kind == "seek_back" || kind == "seek_forward")
            {
                float d = kind == "previous" || kind == "seek_back" ? -1 : 1;
                Line(new Vector2(-5 * d, -10), new Vector2(5 * d, 0)); Line(new Vector2(5 * d, 0), new Vector2(-5 * d, 10));
                if (kind.StartsWith("seek_")) { Line(new Vector2(-12 * d, -10), new Vector2(-2 * d, 0)); Line(new Vector2(-2 * d, 0), new Vector2(-12 * d, 10)); }
            }
            else if (kind == "chevron_down") { Line(new Vector2(-9, 5), new Vector2(0, -5)); Line(new Vector2(0, -5), new Vector2(9, 5)); }
            else if (kind == "minimize") Line(new Vector2(-11, -8), new Vector2(11, -8));
            else if (kind == "close") { Line(new Vector2(-9, -9), new Vector2(9, 9)); Line(new Vector2(-9, 9), new Vector2(9, -9)); }
            else
            {
                // Frame corners: recenter / Reader closer (plus) / Reader back to default distance (minus).
                Line(new Vector2(-11, -5), new Vector2(-11, -11)); Line(new Vector2(-11, -11), new Vector2(-5, -11));
                Line(new Vector2(11, -5), new Vector2(11, -11)); Line(new Vector2(11, -11), new Vector2(5, -11));
                Line(new Vector2(-11, 5), new Vector2(-11, 11)); Line(new Vector2(-11, 11), new Vector2(-5, 11));
                Line(new Vector2(11, 5), new Vector2(11, 11)); Line(new Vector2(11, 11), new Vector2(5, 11));
                Line(new Vector2(-5, 0), new Vector2(5, 0));
                if (kind != "farther") Line(new Vector2(0, -5), new Vector2(0, 5));
            }
        }
    }
}
