using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ResearchTutorialSequenceGraphic : MaskableGraphic
    {
        // Trainer parts held with the grip button (palm grab); screw and plug heads use the index-finger pinch (Trigger).
        public static bool GripFact(string fact) => fact == "insert_wrong" || fact == "insert" || fact == "lock" || fact == "unlock" || fact == "rotate_access";

        public string fact;
        public bool hands;
        static readonly Color Ink = new Color(.10f, .23f, .34f);
        static readonly Color Pale = new Color(.83f, .93f, 1f);
        static readonly Color Blue = new Color(.04f, .43f, .80f);
        public void SetStep(string value, bool useHands)
        {
            if (fact == value && hands == useHands) return;
            fact = value; hands = useHands; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            string action = fact ?? "select";
            Line(mesh, new Vector2(8, 90), new Vector2(272, 90), Pale, 2);
            Line(mesh, new Vector2(8, 180), new Vector2(272, 180), Pale, 2);
            for (int stage = 0; stage < 3; stage++)
            {
                float y = 225 - stage * 90;
                bool held = action == "grab" ? stage > 0 : action == "release" ? stage == 0
                    : stage < 2 || action == "move" || action == "rotate";
                if (action == "roomscale")
                {
                    Head(mesh, new Vector2(stage == 0 ? 62 : 88, y));
                    Line(mesh, new Vector2(42, y - 27), new Vector2(204, y - 27), Ink, 2);
                    if (stage > 0) { Ring(mesh, new Vector2(62, y + 3), 16, Pale); Arrow(mesh, new Vector2(112, y + 5), new Vector2(180, y + 5)); }
                    continue;
                }
                bool grip = action == "grab" || action == "move" || action == "rotate" || action == "release" || GripFact(action);
                Controller(mesh, new Vector2(50, y), action == "teleport" || action == "snap" ? "stick" : grip ? "grip" : "trigger", held);
                if (action == "teleport")
                {
                    Ring(mesh, new Vector2(148, y - 18), 14, stage == 2 ? Pale : Ink);
                    Ring(mesh, new Vector2(242, y - 18), 14, Blue);
                    Head(mesh, new Vector2(stage == 2 ? 242 : 148, y + 7));
                    if (stage == 1) Arc(mesh, new Vector2(158, y - 2), new Vector2(237, y - 2));
                    if (stage == 0) Arrow(mesh, new Vector2(78, y - 8), new Vector2(78, y + 23));
                }
                else if (action == "snap")
                {
                    var center = new Vector2(203, y - 6); Ring(mesh, center, 24, Pale);
                    Vector2 direction = stage == 0 ? Vector2.up : new Vector2(.71f, .71f);
                    Arrow(mesh, center, center + direction * 33);
                    if (stage == 0) Arrow(mesh, new Vector2(82, y + 4), new Vector2(117, y + 4));
                }
                else if (grip)
                {
                    Line(mesh, new Vector2(130, y - 25), new Vector2(262, y - 25), Ink, 3);
                    float lift = action == "move" && stage > 0 ? 26 : action == "release" && stage < 2 ? 26 : 0;
                    Cup(mesh, new Vector2(stage == 2 && action == "release" ? 220 : 190, y - 5 + lift), action == "rotate" && stage > 0);
                    if (action == "move" && stage > 0) Arrow(mesh, new Vector2(232, y - 12), new Vector2(232, y + 23));
                    if (action == "rotate" && stage > 0) Arc(mesh, new Vector2(159, y + 16), new Vector2(216, y + 16));
                    if (action == "grab" || action == "move" || action == "rotate" || stage < 2)
                        Line(mesh, new Vector2(82, y - 1), new Vector2(170, y - 1 + lift), Blue, 3);
                }
                else
                {
                    bool pill = action == "minimize" && stage == 2 || action == "restore" && stage == 0;
                    float x = action == "reposition" && stage > 0 ? 227 : 193;
                    var size = pill ? new Vector2(60, 20) : new Vector2(90, 48);
                    Box(mesh, new Vector2(x, y + 3), size, Pale);
                    Line(mesh, new Vector2(x - 30, y + 14), new Vector2(x + 22, y + 14), Ink, 3);
                    if (!pill) Line(mesh, new Vector2(x - 19, y - 12), new Vector2(x + 19, y - 12), Blue, 5);
                    Line(mesh, new Vector2(80, y + 2), new Vector2(x - 26, y - 12), stage == 1 ? Blue : Ink, 2);
                    if (action == "reposition" && stage == 1) Arrow(mesh, new Vector2(153, y + 34), new Vector2(241, y + 34));
                }
            }
        }

        void Controller(VertexHelper mesh, Vector2 p, string control, bool held)
        {
            if (hands)
            {
                Ring(mesh, p + new Vector2(0, 8), 12, Ink);
                Ring(mesh, p + new Vector2(held ? 15 : 34, 15), 10, held ? Blue : Ink);
                return;
            }
            Box(mesh, p + new Vector2(0, -15), new Vector2(29, 46), Pale);
            Ring(mesh, p + new Vector2(0, 12), 22, Ink);
            Ring(mesh, p + new Vector2(-6, 17), 6, control == "stick" && held ? Blue : Pale);
            Box(mesh, p + new Vector2(22, 4), new Vector2(8, 13), control == "trigger" && held ? Blue : Pale);
            Box(mesh, p + new Vector2(16, -17), new Vector2(8, 22), control == "grip" && held ? Blue : Pale);
        }
        void Head(VertexHelper mesh, Vector2 p)
        {
            Ring(mesh, p + new Vector2(0, 7), 12, Ink);
            Box(mesh, p + new Vector2(0, 7), new Vector2(22, 8), Blue);
            Line(mesh, p + new Vector2(-17, -16), p + new Vector2(17, -16), Ink, 5);
        }
        void Cup(VertexHelper mesh, Vector2 p, bool rotated)
        {
            Box(mesh, p, new Vector2(28, 34), Pale);
            Line(mesh, p + new Vector2(-14, 17), p + new Vector2(14, 17), Ink, 3);
            Ring(mesh, p + new Vector2(rotated ? -20 : 20, 0), 9, Blue);
        }
        void Arc(VertexHelper mesh, Vector2 start, Vector2 end)
        {
            Vector2 last = start;
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f; var next = Vector2.Lerp(start, end, t) + Vector2.up * (4 * t * (1 - t) * 26);
                Line(mesh, last, next, Blue, 3); last = next;
            }
            Arrow(mesh, last + new Vector2(-9, 10), last);
        }
        void Arrow(VertexHelper mesh, Vector2 from, Vector2 to)
        {
            Line(mesh, from, to, Blue, 3);
            var direction = (to - from).normalized; var side = new Vector2(-direction.y, direction.x);
            Line(mesh, to, to - direction * 9 + side * 5, Blue, 3);
            Line(mesh, to, to - direction * 9 - side * 5, Blue, 3);
        }
        void Ring(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI / 12, b = (i + 1) * Mathf.PI / 12;
                Line(mesh, center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                    center + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, tint, 3);
            }
        }
        void Box(VertexHelper mesh, Vector2 center, Vector2 size, Color tint)
        {
            var r = new Rect(center - size / 2, size);
            Quad(mesh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMin, r.yMax), new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, r.yMin), tint);
        }
        void Line(VertexHelper mesh, Vector2 from, Vector2 to, Color tint, float width)
        {
            var direction = (to - from).normalized; var side = new Vector2(-direction.y, direction.x) * width / 2;
            Quad(mesh, from - side, from + side, to + side, to - side, tint);
        }
        void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            var r = rectTransform.rect; Vector2 Map(Vector2 p) => r.min + Vector2.Scale(p, new Vector2(r.width / 280, r.height / 270));
            int i = mesh.currentVertCount; mesh.AddVert(Map(a), tint, Vector2.zero); mesh.AddVert(Map(b), tint, Vector2.zero);
            mesh.AddVert(Map(c), tint, Vector2.zero); mesh.AddVert(Map(d), tint, Vector2.zero);
            mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
