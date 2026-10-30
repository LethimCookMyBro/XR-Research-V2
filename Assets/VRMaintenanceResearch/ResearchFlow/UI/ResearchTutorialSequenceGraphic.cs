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
            fact = value; hands = useHands; SetVerticesDirty(); PlaceObjects();
        }

        // Rendered images of the real practice mug and Trainer_FX1 module (ResearchTutorialObjectCapture) stand where the
        // strip used to draw an abstract box, so each row shows the object the participant actually handles.
        RawImage[] objectImages;
        static Texture2D mug, module;
        static bool ShowsMug(string action) => action == "grab" || action == "move" || action == "rotate" || action == "release";
        static bool ShowsModule(string action) => action == "insert" || action == "insert_wrong" || action == "lock" || action == "unlock";
        protected override void OnRectTransformDimensionsChange() { base.OnRectTransformDimensionsChange(); PlaceObjects(); }
        protected override void OnEnable() { base.OnEnable(); PlaceObjects(); }
        void PlaceObjects()
        {
            if (objectImages == null)
            {
                mug = mug ? mug : Resources.Load<Texture2D>("ResearchFlowTutorial/Objects/mug");
                module = module ? module : Resources.Load<Texture2D>("ResearchFlowTutorial/Objects/module");
                if (!mug || !module) return;
                objectImages = new RawImage[3];
                for (int i = 0; i < 3; i++)
                {
                    var child = new GameObject("RealObject_" + i, typeof(RectTransform), typeof(RawImage));
                    child.transform.SetParent(transform, false);
                    objectImages[i] = child.GetComponent<RawImage>(); objectImages[i].raycastTarget = false;
                }
                SetVerticesDirty();
            }
            string action = fact ?? "select";
            var r = rectTransform.rect; var scale = new Vector2(r.width / 280, r.height / 270);
            for (int stage = 0; stage < 3; stage++)
            {
                var image = objectImages[stage]; float y = 225 - stage * 90;
                bool showMug = !hands && ShowsMug(action), showModule = !hands && ShowsModule(action);
                image.gameObject.SetActive(showMug || showModule);
                if (!showMug && !showModule) continue;
                Vector2 center, size; float angle = 0; bool mirrored = false;
                if (showMug)
                {
                    float lift = action == "move" && stage > 0 ? 26 : action == "release" && stage < 2 ? 26 : 0;
                    center = new Vector2(stage == 2 && action == "release" ? 220 : 190, y - 2 + lift); size = new Vector2(74, 74);
                    if (action == "rotate" && stage > 0) angle = 35;
                }
                else
                {
                    center = ModuleCenter(action, stage, y); size = new Vector2(80, 80);
                    mirrored = action == "insert_wrong";
                }
                image.texture = showMug ? mug : module;
                var rt = image.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = r.min + Vector2.Scale(center, scale) - r.center;
                rt.sizeDelta = Vector2.Scale(size, scale); rt.localEulerAngles = new Vector3(0, 0, angle);
                rt.localScale = new Vector3(mirrored ? -1 : 1, 1, 1);
            }
        }
        static Vector2 ModuleCenter(string action, int stage, float y)
        {
            if (action == "examine") return new Vector2(196, y - 4);
            if (action == "lock" || action == "unlock") return new Vector2(196, !(action == "lock") && stage == 2 ? y + 12 : y - 14);
            return new Vector2(200, stage == 0 ? y + 14 : stage == 1 ? y + 6 : action == "insert_wrong" ? y - 4 : y - 14);
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
                // Practice-station mechanisms show their own object and motion (capture C05: Examine previously reused the menu picture).
                if (TrainerStep(mesh, action, stage, y)) continue;
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

        public static bool HasTrainerDrawing(string action) => action == "examine" || action == "fasten" || action == "loosen"
            || action == "cable_connect" || action == "cable_disconnect" || action == "insert" || action == "insert_wrong"
            || action == "lock" || action == "unlock" || action == "rotate_access";

        // Three rows read top to bottom: approach, act (button held), result. Grip for module/latch/turntable, Trigger (pinch) for
        // screw and plug heads, Trigger ray for Examine - matching ResearchTrainingStation and the instruction text.
        bool TrainerStep(VertexHelper mesh, string action, int stage, float y)
        {
            if (!HasTrainerDrawing(action)) return false;
            bool trigger = action == "examine" || action == "fasten" || action == "loosen" || action == "cable_connect" || action == "cable_disconnect";
            bool held = action == "examine" ? stage == 1 : action == "insert" ? stage == 1 : stage >= 1;
            Controller(mesh, new Vector2(50, y), trigger ? "trigger" : "grip", held);
            switch (action)
            {
                case "examine":
                {
                    Box(mesh, new Vector2(196, y + 3), new Vector2(90, 56), Pale);
                    Line(mesh, new Vector2(166, y + 20), new Vector2(220, y + 20), Ink, 3);
                    Line(mesh, new Vector2(178, y - 14), new Vector2(214, y - 14), Blue, 5);
                    Line(mesh, new Vector2(80, y + 2), new Vector2(178, y - 14), stage == 1 ? Blue : Ink, 2);
                    if (stage == 2)
                    {
                        // Information only: an "i" marker beside the unchanged module.
                        var info = new Vector2(196, y + 2); Ring(mesh, info, 13, Blue);
                        Box(mesh, info + new Vector2(0, -3), new Vector2(4, 11), Blue); Box(mesh, info + new Vector2(0, 6), new Vector2(4, 4), Blue);
                    }
                    break;
                }
                case "fasten": case "loosen":
                {
                    var center = new Vector2(196, y - 4); bool clockwise = action == "fasten";
                    Ring(mesh, center, 20, Ink);
                    float slot = stage == 2 ? (clockwise ? -50 : 50) : 0;
                    var dir = new Vector2(Mathf.Cos(slot * Mathf.Deg2Rad), Mathf.Sin(slot * Mathf.Deg2Rad));
                    Line(mesh, center - dir * 13, center + dir * 13, Ink, 4);
                    Line(mesh, new Vector2(80, y + 2), center + new Vector2(-22, 6), stage >= 1 ? Blue : Ink, 2);
                    if (stage >= 1) CircleArrow(mesh, center, 32, clockwise ? 120 : 60, clockwise ? 10 : 170);
                    break;
                }
                case "cable_connect": case "cable_disconnect":
                {
                    bool connect = action == "cable_connect";
                    bool seated = connect ? stage == 2 : stage == 0;
                    var socket = new Vector2(244, y - 4);
                    Line(mesh, socket + new Vector2(-14, 16), socket + new Vector2(14, 16), Ink, 3);
                    Line(mesh, socket + new Vector2(-14, -16), socket + new Vector2(14, -16), Ink, 3);
                    Line(mesh, socket + new Vector2(14, 16), socket + new Vector2(14, -16), Ink, 3);
                    float plugX = seated ? 238 : 182;
                    Box(mesh, new Vector2(plugX, y - 4), new Vector2(24, 18), Pale);
                    Line(mesh, new Vector2(plugX - 12, y - 4), new Vector2(118, y - 26), Ink, 3); // cable stays attached at its far end
                    Line(mesh, new Vector2(80, y + 2), new Vector2(plugX - 10, y + 4), stage >= 1 ? Blue : Ink, 2);
                    if (stage == 1) Arrow(mesh, new Vector2(connect ? 200 : 226, y + 20), new Vector2(connect ? 232 : 192, y + 20));
                    break;
                }
                case "insert": case "insert_wrong":
                {
                    bool wrong = action == "insert_wrong";
                    Slot(mesh, new Vector2(200, y - 22));
                    float moduleY = stage == 0 ? y + 14 : stage == 1 ? y + 6 : wrong ? y - 4 : y - 14;
                    Module(mesh, new Vector2(200, moduleY), wrong, false);
                    // Neutral FX1 practice only: the actual photographed module preview hides
                    // the abstract mesh notch, so show TWO matching key markers explicitly.
                    // Mirroring the photo for the blocked direction moves only the module mark.
                    Ring(mesh, new Vector2(171, y - 13), 6, Blue); // slot notch
                    Ring(mesh, new Vector2(wrong ? 230 : 170, moduleY - 4), 6, Blue); // module notch
                    if (stage == 1) Arrow(mesh, new Vector2(246, y + 22), new Vector2(246, y - 8));
                    if (wrong && stage == 2)
                    {
                        // Stops part-way: an X beside the blocked module.
                        Line(mesh, new Vector2(240, y + 6), new Vector2(258, y - 12), Blue, 3);
                        Line(mesh, new Vector2(240, y - 12), new Vector2(258, y + 6), Blue, 3);
                    }
                    if (stage < 2) Line(mesh, new Vector2(82, y - 1), new Vector2(170, moduleY), stage == 1 ? Blue : Ink, 2);
                    break;
                }
                case "lock": case "unlock":
                {
                    bool closing = action == "lock";
                    Slot(mesh, new Vector2(196, y - 22));
                    bool pulled = !closing && stage == 2;
                    Module(mesh, new Vector2(196, pulled ? y + 12 : y - 14), false, false);
                    bool closed = closing ? stage == 2 : stage == 0;
                    var hinge = new Vector2(232, y - 6);
                    var tip = closed ? hinge + new Vector2(-30, 0) : hinge + new Vector2(-10, 28);
                    Line(mesh, hinge, tip, Ink, 5); Ring(mesh, hinge, 4, Ink);
                    if (stage == 1) CircleArrow(mesh, hinge, 22, closing ? 110 : 170, closing ? 170 : 110);
                    if (pulled) Arrow(mesh, new Vector2(150, y - 10), new Vector2(150, y + 22));
                    Line(mesh, new Vector2(82, y - 1), stage == 2 && pulled ? new Vector2(170, y + 12) : tip, stage >= 1 ? Blue : Ink, 2);
                    break;
                }
                case "rotate_access":
                {
                    var center = new Vector2(196, y - 4);
                    Ring(mesh, center, 30, Ink);
                    float angle = stage == 2 ? 200 : 290;
                    var handle = center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * 30;
                    Ring(mesh, handle, 7, Blue);
                    // Rear screw becomes reachable once turned.
                    var screw = center + (stage == 2 ? new Vector2(-14, -14) : new Vector2(14, 14));
                    Ring(mesh, screw, 6, stage == 2 ? Blue : Pale);
                    Line(mesh, new Vector2(82, y - 1), handle, stage >= 1 ? Blue : Ink, 2);
                    if (stage == 1) CircleArrow(mesh, center, 40, 300, 210);
                    break;
                }
            }
            return true;
        }
        void Module(VertexHelper mesh, Vector2 center, bool reversed, bool highlighted)
        {
            if (!hands && objectImages != null) return;
            Box(mesh, center, new Vector2(56, 30), Pale);
            Line(mesh, center + new Vector2(-28, 15), center + new Vector2(28, 15), highlighted ? Blue : Ink, highlighted ? 4 : 3);
            // Keyed cut corner shows orientation; reversed for the wrong-way practice.
            var corner = center + new Vector2(reversed ? 28 : -28, -15);
            Line(mesh, corner + new Vector2(reversed ? -10 : 10, 0), corner + new Vector2(0, 10), Ink, 3);
        }
        void Slot(VertexHelper mesh, Vector2 bottom)
        {
            Line(mesh, bottom + new Vector2(-34, 0), bottom + new Vector2(34, 0), Ink, 3);
            Line(mesh, bottom + new Vector2(-34, 0), bottom + new Vector2(-34, 26), Ink, 3);
            Line(mesh, bottom + new Vector2(34, 0), bottom + new Vector2(34, 26), Ink, 3);
        }
        void CircleArrow(VertexHelper mesh, Vector2 center, float radius, float fromDegrees, float toDegrees)
        {
            Vector2 Point(float degrees) => center + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;
            Vector2 last = Point(fromDegrees), before = last;
            for (int i = 1; i <= 12; i++) { var next = Point(Mathf.Lerp(fromDegrees, toDegrees, i / 12f)); Line(mesh, last, next, Blue, 3); before = last; last = next; }
            var direction = (last - before).normalized; var side = new Vector2(-direction.y, direction.x);
            Line(mesh, last, last - direction * 9 + side * 5, Blue, 3);
            Line(mesh, last, last - direction * 9 - side * 5, Blue, 3);
        }

        // Side view of a Touch controller: handle, head with thumbstick, trigger in front, grip on the handle.
        // The control the step uses is always outlined blue so it is identifiable before the press; it fills blue while held.
        void Controller(VertexHelper mesh, Vector2 p, string control, bool held)
        {
            if (hands)
            {
                Ring(mesh, p + new Vector2(0, 8), 12, Ink);
                Ring(mesh, p + new Vector2(held ? 15 : 34, 15), 10, held ? Blue : Ink);
                return;
            }
            Outline(mesh, p + new Vector2(-2, -14), new Vector2(22, 44), Ink);
            Outline(mesh, p + new Vector2(2, 14), new Vector2(40, 16), Ink);
            Part(mesh, p + new Vector2(-4, 26), new Vector2(12, 8), control == "stick", held);
            Part(mesh, p + new Vector2(25, 6), new Vector2(9, 14), control == "trigger", held);
            Part(mesh, p + new Vector2(13, -16), new Vector2(7, 22), control == "grip", held);
        }
        void Part(VertexHelper mesh, Vector2 center, Vector2 size, bool active, bool held)
        {
            if (active && held) Box(mesh, center, size, Blue);
            else if (active) { Box(mesh, center, size, Pale); Outline(mesh, center, size, Blue); }
            else Outline(mesh, center, size, Ink);
        }
        void Outline(VertexHelper mesh, Vector2 center, Vector2 size, Color tint)
        {
            var r = new Rect(center - size / 2, size);
            Line(mesh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin), tint, 2);
            Line(mesh, new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMax), tint, 2);
            Line(mesh, new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax), tint, 2);
            Line(mesh, new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMin), tint, 2);
        }
        void Head(VertexHelper mesh, Vector2 p)
        {
            Ring(mesh, p + new Vector2(0, 7), 12, Ink);
            Box(mesh, p + new Vector2(0, 7), new Vector2(22, 8), Blue);
            Line(mesh, p + new Vector2(-17, -16), p + new Vector2(17, -16), Ink, 5);
        }
        void Cup(VertexHelper mesh, Vector2 p, bool rotated)
        {
            if (!hands && objectImages != null) return;
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
