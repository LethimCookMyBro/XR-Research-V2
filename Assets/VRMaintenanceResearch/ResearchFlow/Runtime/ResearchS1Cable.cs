using System;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow
{
    [Serializable]
    public sealed class S1CableSpan
    {
        public Transform from, to;
        public Vector3[] routeGuides;
        public Vector3 routeSlack;
        [NonSerialized] public bool cachedGuided;
        [NonSerialized] public Vector3 cachedPreferred;
        [NonSerialized] public Matrix4x4 cachedRoutePose;
        public Vector3 fromExit, toExit, fromDirection, toDirection;
        [NonSerialized] public Vector3[] cachedPath;
        [NonSerialized] public Vector3 cachedA, cachedB, cachedDa, cachedDb, cachedSide;
        [NonSerialized] public bool cached;
        // From the skin's bind pose, once: each bone's modelled segment in bone space (toward the next node) and its arc fraction.
        [NonSerialized] public Vector3[] bindAxes;
        [NonSerialized] public float[] bindFractions;
        [NonSerialized] public bool carried;
        public Transform[] bones;
        public Quaternion[] restRotations;
        public Vector3[] restPositions;
        public Vector3 restToPosition;
        public Quaternion restToRotation, restFromRotation;
        public Vector3[] restDirections;
        public float length, minimumRadius;
    }

    [Serializable]
    public sealed class S1CableSlot
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }

    [Serializable]
    public sealed class S1CablePark
    {
        public Transform head;
        public VRMaintenanceResearch.AssetLibraryQA.QAPlug plug;
        public S1CableSlot[] slots;
        // Corner slot tried first while the other end sits in a port; 0 = the corner on that port's side of the board tray.
        // The SATA data lead (0.5 m) cannot reach the front corner from a board port without cutting through the PSU.
        public int waitSlot;
    }

    // A knot of an authored lead route: a point of the case (anchor 0, cable space) or an offset from where the lead leaves the
    // from head (1) or the to head (2), so the same route fits every port of a family.
    [Serializable]
    public sealed class S1CableKnot
    {
        public int anchor;
        public Vector3 offset;
    }

    // Used while each lead exit lies in its box (cable space), e.g. the PSU end in a PSU port and the other end at the SSD.
    [Serializable]
    public sealed class S1CableRoute
    {
        public Bounds from, to;
        public S1CableKnot[] knots;
        public Vector3 slack;
        // Share of the spare length each segment takes (knots + 1 entries); the short runs into the plugs take none.
        public float[] weights;
    }

    // The folded cable as it lies in its start slots, in the space of spans[0].from.
    [Serializable]
    public sealed class S1CableFold
    {
        public Vector3 toPosition;
        public Quaternion toRotation = Quaternion.identity;
        public Vector3[] bonePositions;
        public Quaternion[] boneRotations;
    }

    [DefaultExecutionOrder(14000)]
    public sealed class ResearchS1Cable : MonoBehaviour
    {
        public string assetId;
        public S1CableSpan[] spans;
        public SkinnedMeshRenderer skin;
        public Vector3 preferredBow;
        // Plane tried first when the shape is (re)seeded, e.g. the loose SATA lead at task start. Only the order changes:
        // preferredBow and every fallback plane are still tried, so Allows (CableLimit) accepts exactly the same poses.
        public Vector3 restBow;
        public bool sideways; // bow across the chord first, as a short lead lying over; the default arches up
        // S1_LAYOUT_CABLE_TRAY_20261110: both heads of a task cable are loose and lie in tray slots given in cable space. In
        // slot 0 the cable is folded along parkGuides. With its ends on opposite sides of the motherboard tray (the plane
        // z = passPlane) the lead runs through the board-edge channel (passGuides, listed from the open side to the rear).
        public S1CablePark[] parks;
        // While both ends are loose the cable stays folded and rigid: picked up by one head, the whole fold comes along, and
        // it never limits the hand. Bowing a cable between two free heads a few centimetres apart had no solution in some
        // wrist poses, which froze the held plug on its tray (CableLimit).
        public S1CableFold fold;
        public bool Loose => parks != null && parks.Length == 2 && parks[0].plug && parks[1].plug && !parks[0].plug.Socket && !parks[1].plug.Socket;
        public Pose FoldedPose(Transform head)
        {
            var span = spans[0];
            if (head == span.to) return new Pose(span.from.TransformPoint(fold.toPosition), span.from.rotation * fold.toRotation);
            var rotation = span.to.rotation * Quaternion.Inverse(fold.toRotation);
            return new Pose(span.to.position - rotation * fold.toPosition, rotation);
        }
        bool Folded(S1CableSpan span) => fold != null && fold.bonePositions != null && span == spans[0] && Loose &&
            Vector3.Distance(span.from.InverseTransformPoint(span.to.position), fold.toPosition) < .0005f &&
            Quaternion.Angle(Quaternion.Inverse(span.from.rotation) * span.to.rotation, fold.toRotation) < .1f;
        // Authored leads for the plugged states (10 Nov): the free bow between two ports cut through the motherboard tray and
        // the drive cage. The first route whose boxes hold both exits is tried before passGuides.
        public S1CableRoute[] routes;
        public Vector3[] parkGuides, passGuides;
        public Vector3 parkSlack, passSlack;
        public float passPlane;
        public S1CablePark ParkOf(Transform head) => parks == null ? null : Array.Find(parks, p => p != null && p.head == head);
        public Pose SlotPose(S1CableSlot slot) => new Pose(transform.TransformPoint(slot.position), transform.rotation * slot.rotation);
        // Drawn instead of the skinned body when the modelled lead cannot straighten (Training FX1: its coiled body sits up to
        // 16 mm off the bone chords, so posed straight it waved and dipped 9 mm into the base). Zero radius keeps the skin.
        public Material tubeMaterial;
        public float tubeRadius;
        public const int TubeSides = 8;
        MeshFilter tube; Vector3[] tubeVertices, tubeNormals, tubeSource;

        // Work surfaces the drawn cable rests on (benches and the trainer base), found by name as ResearchFlowPhysicalProbe does.
        static readonly string[] SurfaceNames = { "Computer_Main", "Fan_Main", "Training_Practice_A", "COL_TRAINER_FX1_BASE_Body" };
        static Collider[] surfaces;
        // Bones sit ~4 cm apart, so a cable dropping off an edge would cut the corner unless it clears the edge by about that much.
        const float Clearance = .005f, EdgeMargin = .03f, RigidCarriedLength = .1f;

        // The SATA power plug's side wire stubs are skinned rigid to its head but stay outside the solid collider (SATA_GAP), so laid
        // on its side a stub dipped 3.2 mm into the bench. Mate-space boxes of that geometry from VIS_CABLE_MX_SATA3_LOD0: the exit lip
        // and the wire stub on each side. The FBX is not Read/Write, so Quest cannot re-weight the stubs; the plug visual rests on them.
        const string StubbedHead = "B_CABLE_MX_SATA3_B", StubbedMate = "MATE_CABLE_MX_SATA3_EndB";
        const float StubClearance = .0005f, StubReach = .01f;
        static readonly Bounds[] Stubs = {
            new Bounds(new Vector3(-.0114f, -.0009f, -.007f), new Vector3(.0012f, .0059f, .014f)), new Bounds(new Vector3(-.01581f, -.0009f, -.0096f), new Vector3(.00762f, .0024f, .008f)),
            new Bounds(new Vector3(.0114f, -.0009f, -.007f), new Vector3(.0012f, .0059f, .014f)), new Bounds(new Vector3(.014015f, -.0009f, -.0096f), new Vector3(.00403f, .0024f, .008f)) };
        Transform stubbedVisual, stubbedMate;

        // Re-checking every bone name each frame allocated ~25 KB/frame across the 8 cables on Quest (GC every ~4 s).
        // shortcut: a pass is cached per spans array + assetId; bones destroyed in place are not re-detected (authoring never does that).
        S1CableSpan[] validatedSpans; string validatedId;
        public bool AuthoredValid()
        {
            if (validatedSpans != null && ReferenceEquals(validatedSpans, spans) && validatedId == assetId && skin && skin.sharedMesh) return true;
            bool valid = !string.IsNullOrEmpty(assetId) && skin && skin.sharedMesh && spans != null && spans.Length > 0 &&
                spans.All(s => s != null && s.from && s.to && s.length > 0 && s.bones != null && s.bones.Length >= 2 &&
                    s.restPositions != null && s.restPositions.Length == s.bones.Length && s.restRotations != null && s.restRotations.Length == s.bones.Length && s.restDirections != null && s.restDirections.Length == s.bones.Length &&
                    s.bones.All(b => b && b.name.StartsWith("B_" + assetId + "_", StringComparison.Ordinal)));
            validatedSpans = valid ? spans : null; validatedId = valid ? assetId : null;
            return valid;
        }

        // A span whose reference pose is the skin's bind pose, for a lead built at runtime without authored rest data (Training FX1).
        public static S1CableSpan BindSpan(Transform root, SkinnedMeshRenderer skin, Transform from, Transform[] bones, Transform to)
        {
            var binds = skin.sharedMesh.bindposes; var all = skin.bones;
            Matrix4x4 Relative(Transform t) => binds[Array.IndexOf(all, from)] * binds[Array.IndexOf(all, t)].inverse;
            var end = Relative(to); float length = 0;
            for (int i = 0; i < bones.Length; i++) length += Vector3.Distance(Relative(bones[i]).GetColumn(3), (i + 1 < bones.Length ? Relative(bones[i + 1]) : end).GetColumn(3));
            Vector3 exit = Relative(bones[0]).GetColumn(3), last = (end.inverse * Relative(bones[bones.Length - 1])).GetColumn(3);
            var fromRotation = Quaternion.Inverse(root.rotation) * from.rotation;
            return new S1CableSpan { from = from, to = to, bones = bones, length = length, fromExit = exit, fromDirection = exit.normalized, toDirection = last.normalized,
                restToPosition = end.GetColumn(3), restToRotation = end.rotation, restFromRotation = fromRotation,
                restPositions = bones.Select(b => (Vector3)Relative(b).GetColumn(3)).ToArray(),
                restRotations = bones.Select(b => fromRotation * Relative(b).rotation).ToArray(), restDirections = new Vector3[bones.Length] };
        }

        public bool Allows(Transform moving, Matrix4x4 proposed)
        {
            if (!AuthoredValid()) return false;
            // A task cable limits a move only through an end that sits in a port and is not the one being moved: with the
            // other end free, the cable simply comes along.
            if (parks != null && parks.Length == 2 && !parks.Any(p => p.plug && p.plug.Socket && !Moves(p.head, moving))) return true;
            var routePose = ProposedPose(transform, moving, proposed);
            foreach (var span in spans)
            {
                if (moving && !Moves(span.from, moving) && !Moves(span.to, moving) && !Moves(transform, moving)) continue;
                Vector3 a = Exit(span.from, span.fromExit, moving, proposed), b = Exit(span.to, span.toExit, moving, proposed);
                Bind(span);
                if (span.carried || Rigid(span,moving,proposed)) continue;
                Vector3 da = Direction(span.from, span.fromDirection, moving, proposed), db = Direction(span.to, span.toDirection, moving, proposed);
                if (Path(span,a,b,da,db,routePose) == null) return false;
            }
            return true;
        }

        private static bool Moves(Transform target, Transform moving) => moving && (target == moving || target.IsChildOf(moving));

        private static Matrix4x4 ProposedPose(Transform target, Transform moving, Matrix4x4 proposed) =>
            Moves(target, moving) ? proposed * moving.worldToLocalMatrix * target.localToWorldMatrix : target.localToWorldMatrix;

        private static Vector3 Exit(Transform endpoint, Vector3 offset, Transform moving, Matrix4x4 proposed)
        {
            var pose = endpoint.localToWorldMatrix;
            if (moving && (endpoint == moving || endpoint.IsChildOf(moving))) pose = proposed * moving.worldToLocalMatrix * pose;
            return pose.MultiplyPoint3x4(offset);
        }

        private static Vector3 Direction(Transform endpoint, Vector3 offset, Transform moving, Matrix4x4 proposed)
        {
            var pose = endpoint.localToWorldMatrix;
            if (moving && (endpoint == moving || endpoint.IsChildOf(moving))) pose = proposed * moving.worldToLocalMatrix * pose;
            return pose.MultiplyVector(offset);
        }
        private static bool Rigid(S1CableSpan span) => Rigid(span,null,Matrix4x4.identity);
        private static bool Rigid(S1CableSpan span,Transform moving,Matrix4x4 proposed)
        {
            var a=span.from.localToWorldMatrix;var b=span.to.localToWorldMatrix;
            if(moving && (span.from==moving || span.from.IsChildOf(moving)))a=proposed*moving.worldToLocalMatrix*a;
            if(moving && (span.to==moving || span.to.IsChildOf(moving)))b=proposed*moving.worldToLocalMatrix*b;
            var relative=a.inverse*b;
            return Vector3.Distance(relative.GetColumn(3),span.restToPosition)<.00001f && Quaternion.Angle(relative.rotation,span.restToRotation)<.01f;
        }
        private Vector3[] Path(S1CableSpan span,Vector3 a,Vector3 b,Vector3 da,Vector3 db,Matrix4x4 routePose)
        {
            var orientation = routePose.rotation;
            Vector3 up = orientation * Vector3.up, right = orientation * Vector3.right, forward = orientation * Vector3.forward;
            Vector3 preferred = preferredBow.sqrMagnitude > .0001f ? orientation * preferredBow : sideways ? Vector3.Cross(up,b-a) : up;
            if(span.cached && span.cachedRoutePose == routePose && span.cachedA==a && span.cachedB==b && span.cachedDa==da && span.cachedDb==db && span.cachedPreferred==preferred)return span.cachedPath;
            if(span.cachedRoutePose != routePose || span.cachedPreferred!=preferred || span.cachedSide.sqrMagnitude<.0001f) span.cachedSide=restBow.sqrMagnitude > .0001f ? orientation * restBow : preferred;
            span.cachedRoutePose=routePose;
            span.cachedPreferred=preferred;
            span.cachedGuided=false;
            span.cached=true;span.cachedA=a;span.cachedB=b;span.cachedDa=da;span.cachedDb=db;span.cachedPath=null;
            var routeGuides=span.routeGuides;
            var routeSlack=span.routeSlack;
            float[] routeWeights=null;
            // An authored route turns into a port within a centimetre or two; the 4 cm bone chords cannot show a tighter bend anyway.
            double guidedRadius=Math.Max(.008,span.minimumRadius);
            if(parks!=null && parks.Length>0 && span==spans[0])
            {
                var toLocal=routePose.inverse;
                Vector3 la=toLocal.MultiplyPoint3x4(a),lb=toLocal.MultiplyPoint3x4(b);
                var fromSlot=SlotAt(span.from,la,span.fromExit);var toSlot=SlotAt(span.to,lb,span.toExit);
                var route=routes==null?null:Array.Find(routes,r=>r!=null && r.knots!=null && r.from.Contains(la) && r.to.Contains(lb));
                if(fromSlot!=null && toSlot!=null && fromSlot==ParkOf(span.from).slots[0] && toSlot==ParkOf(span.to).slots[0]){routeGuides=parkGuides;routeSlack=parkSlack;}
                else if(route!=null){routeGuides=Array.ConvertAll(route.knots,k=>k.anchor==1?la+k.offset:k.anchor==2?lb+k.offset:k.offset);routeSlack=route.slack;routeWeights=route.weights!=null && route.weights.Length==route.knots.Length+1?route.weights:null;guidedRadius=RouteRadius;}
                else if(passGuides!=null && passGuides.Length>0 && (la.z-passPlane)*(lb.z-passPlane)<0)
                {routeGuides=la.z<passPlane?passGuides:passGuides.Reverse().ToArray();routeSlack=passSlack;}
            }
            if(routeGuides != null && routeGuides.Length > 0)
            {
                var guides=Array.ConvertAll(routeGuides,routePose.MultiplyPoint3x4);
                span.cachedPath=ResearchS1CablePath.SolveGuided(a,b,da,db,span.length,guides,orientation * routeSlack,guidedRadius,routeWeights);
                if(span.cachedPath!=null && KeepAbove(span.cachedPath,span.length,guidedRadius,.0001))
                {span.cachedGuided=true;return span.cachedPath;}
                span.cachedPath=null;
            }
            // A real cable may bow in any plane. Keep the last valid plane first so the drawn cable does not flip between frames.
            // A task cable takes the first plane whose lead touches no solid; with none clear it keeps the first valid plane, so
            // the poses Allows accepts are unchanged.
            Vector3[] fallback=null;Vector3 fallbackBow=default;
            foreach(var bow in new[]{span.cachedSide,preferred,sideways?Vector3.Cross(up,b-a):up,-up,right,-right,forward,-forward})
            {
                Vector3 side=Vector3.ProjectOnPlane(bow,b-a).normalized;
                if(side.sqrMagnitude<.5f)continue;
                var path=ResearchS1CablePath.Solve(a,b,da,db,span.length,side,span.minimumRadius);
                if(path==null || !KeepAbove(path,span.length,span.minimumRadius,.00001))continue;
                if(parks==null || Clear(path,span)){span.cachedSide=bow;return span.cachedPath=path;}
                if(fallback==null){fallback=path;fallbackBow=bow;}
            }
            if(fallback!=null)span.cachedSide=fallbackBow;
            return span.cachedPath=fallback;
        }

        static readonly Collider[] touched=new Collider[8];
        const float LeadRadius=.003f;
        public const double RouteRadius=.004;
        // Sweeps the lead between its heads in short chords, so a 1 mm panel is not stepped over (the first and last 3 cm
        // belong to the plugs and their ports).
        private static bool Clear(Vector3[] path,S1CableSpan span)
        {
            for(int i=0;i+4<path.Length;i+=4)
            {
                if(Vector3.Distance(path[i],path[0])<.03f || Vector3.Distance(path[i+4],path[path.Length-1])<.03f)continue;
                int count=Physics.OverlapCapsuleNonAlloc(path[i],path[i+4],LeadRadius,touched,~0,QueryTriggerInteraction.Ignore);
                for(int c=0;c<count;c++)
                    if(!touched[c].transform.IsChildOf(span.from) && !touched[c].transform.IsChildOf(span.to))return false;
            }
            return true;
        }

        // The slot a head lies in, judged by where its lead leaves it (exit in cable space), so proposed poses are judged too.
        private S1CableSlot SlotAt(Transform head,Vector3 exit,Vector3 exitInHead)
        {
            var park=ParkOf(head);
            return park==null?null:Array.Find(park.slots,s=>Vector3.Distance(exit,s.position+s.rotation*exitInHead)<.002f);
        }

        // The bow solve knows nothing of the bench: laid on it, the SATA data path ran 10 mm under the top. Visual only.
        private static bool KeepAbove(Vector3[] path, float length, double minimumRadius, double lengthTolerance)
        {
            Vector3 start=path[0],end=path[path.Length-1];
            for (int i = 0; i < path.Length; i++) path[i].y = Mathf.Max(path[i].y, Floor(path[i]) + Clearance);
            if(path[0]!=start || path[path.Length-1]!=end || Math.Abs(ResearchS1CablePath.Length(path)-length)>lengthTolerance) return false;
            for(int i=1;i<path.Length-1;i++)
            {
                double x=Vector3.Distance(path[i-1],path[i]),y=Vector3.Distance(path[i],path[i+1]),z=Vector3.Distance(path[i-1],path[i+1]);
                double semi=(x+y+z)/2,areaSquared=semi*(semi-x)*(semi-y)*(semi-z);
                if(areaSquared>1e-20 && x*y*z/(4*Math.Sqrt(areaSquared))<minimumRadius) return false;
            }
            return true;
        }

        private static float Floor(Vector3 p, float margin = EdgeMargin)
        {
            if (surfaces == null || Array.Exists(surfaces, s => !s))
                surfaces = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => !c.isTrigger && Array.IndexOf(SurfaceNames, c.name) >= 0).ToArray();
            float floor = float.NegativeInfinity;
            foreach (var surface in surfaces)
            {
                var bounds = surface.bounds;
                if (bounds.size.x > .05f && bounds.size.z > .05f && p.x >= bounds.min.x - margin && p.x <= bounds.max.x + margin &&
                    p.z >= bounds.min.z - margin && p.z <= bounds.max.z + margin)
                    floor = Mathf.Max(floor, bounds.max.y);
            }
            return floor;
        }

        private void Bind(S1CableSpan span)
        {
            if (span.bindAxes != null) return;
            var binds = skin.sharedMesh.bindposes; var all = skin.bones;
            if (stubbedVisual) all[Array.IndexOf(all, stubbedVisual)] = stubbedVisual.parent;
            Matrix4x4 Pose(Transform t) => binds[Array.IndexOf(all, t)];
            int n = span.bones.Length; var lengths = new float[n]; float total = 0;
            span.bindAxes = new Vector3[n]; span.bindFractions = new float[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 next = (i + 1 < n ? Pose(span.bones[i + 1]) : Pose(span.to)).inverse.GetColumn(3);
                span.bindAxes[i] = Pose(span.bones[i]).MultiplyPoint3x4(next);
                lengths[i] = Vector3.Distance(Pose(span.bones[i]).inverse.GetColumn(3), next); total += lengths[i];
            }
            for (int i = 1; i < n; i++) span.bindFractions[i] = span.bindFractions[i - 1] + lengths[i - 1] / total;
            span.carried = spans.Any(s => s.from == span.to.parent || s.to == span.to.parent);
        }

        // A connector carried by another head (SATA daisy-chain C/D, the second PCIe plug) hangs from it instead of turning
        // rigidly with the held plug: rigid, the 30 cm SATA tail swung through the bench whenever the plug was laid flat.
        private void Hang(S1CableSpan span, bool rigid)
        {
            var from = rigid || span.length < RigidCarriedLength ? span.from.localToWorldMatrix
                : Matrix4x4.TRS(span.from.position, transform.rotation * span.restFromRotation, Vector3.one);
            var pose = from * Matrix4x4.TRS(span.restToPosition, span.restToRotation, Vector3.one);
            Vector3 position = pose.GetColumn(3);
            if (!rigid) position.y = Mathf.Max(position.y, Floor(position) + 2 * Clearance);
            span.to.SetPositionAndRotation(position, pose.rotation);
        }

        private Vector3[] Path(S1CableSpan span) => Path(span, span.from.TransformPoint(span.fromExit), span.to.TransformPoint(span.toExit),
            span.from.TransformDirection(span.fromDirection), span.to.TransformDirection(span.toDirection), transform.localToWorldMatrix);

        public void RefreshShape()
        {
            if (!AuthoredValid()) return;
            foreach (var span in spans)
            {
                Bind(span);
                if (span.carried)
                {
                    Hang(span, false);
                    // Too taut to bow from the hung pose: ride on the head as authored rather than freeze in an old shape.
                    if (!Rigid(span) && Path(span) == null) Hang(span, true);
                }
                if (Folded(span))
                {
                    for (int i = 0; i < span.bones.Length; i++)
                        span.bones[i].SetPositionAndRotation(span.from.TransformPoint(fold.bonePositions[i]), span.from.rotation * fold.boneRotations[i]);
                    continue;
                }
                if (Rigid(span))
                {
                    for (int i = 0; i < span.bones.Length; i++) span.bones[i].SetPositionAndRotation(span.from.TransformPoint(span.restPositions[i]),
                        span.from.rotation * Quaternion.Inverse(span.restFromRotation) * span.restRotations[i]);
                    continue;
                }
                var path = Path(span);
                if (path == null) continue;
                // Each bone aims its modelled segment at the next node. Aimed along the path tangent with the scene default-pose
                // direction, segments ended up to 30 mm off the next bone and dipped the mesh 5-9 mm into the bench.
                ResearchS1CablePath.At(path, 0, out var position, out _);
                for (int i = 0; i < span.bones.Length; i++)
                {
                    Vector3 next = span.to.position;
                    if (i + 1 < span.bones.Length) ResearchS1CablePath.At(path, span.bindFractions[i + 1], out next, out _);
                    var rest = transform.rotation * span.restRotations[i];
                    span.bones[i].SetPositionAndRotation(position, Quaternion.FromToRotation(rest * span.bindAxes[i], next - position) * rest);
                    position = next;
                }
                if (tubeRadius > 0) DrawTube(span, path);
            }
            RestOnStubs();
        }

        // The housing is skinned to a child of the head that rises only while a stub would sit below a work surface top, so rest,
        // held and seated poses render exactly as before. Colliders and the grabbed head never move.
        private void RestOnStubs()
        {
            if (!stubbedMate)
            {
                if (!Application.isPlaying || assetId != "CABLE_MX_SATA3") return;
                var bones = skin.bones; int head = Array.FindIndex(bones, b => b.name == StubbedHead);
                stubbedMate = bones[head].Find(StubbedMate);
                stubbedVisual = new GameObject("VIS_" + StubbedHead).transform;
                stubbedVisual.SetParent(bones[head], false);
                bones[head] = stubbedVisual; skin.bones = bones;
            }
            float lift = 0;
            foreach (var box in Stubs)
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = stubbedMate.TransformPoint(box.center + Vector3.Scale(box.extents, new Vector3((corner & 1) * 2 - 1, (corner & 2) - 1, (corner & 4) / 2 - 1)));
                    float top = Floor(p, 0);
                    if (p.y < top && p.y > top - StubReach) lift = Mathf.Max(lift, top + StubClearance - p.y);
                }
            stubbedVisual.position = stubbedVisual.parent.position + Vector3.up * lift;
        }

        private void DrawTube(S1CableSpan span, Vector3[] path)
        {
            if (ReferenceEquals(tubeSource, path)) return;
            tubeSource = path;
            if (!tube)
            {
                // The coiled body folds into the bone line inside the tube; the plug housings stay skinned.
                foreach (var bone in span.bones) bone.localScale = Vector3.zero;
                tube = new GameObject("VIS_" + assetId + "_Tube").AddComponent<MeshFilter>();
                tube.transform.SetParent(transform, false);
                tube.gameObject.AddComponent<MeshRenderer>().sharedMaterial = tubeMaterial;
                tubeVertices = new Vector3[path.Length * TubeSides]; tubeNormals = new Vector3[tubeVertices.Length];
                var triangles = new int[(path.Length - 1) * TubeSides * 6];
                for (int i = 0, t = 0; i < path.Length - 1; i++)
                    for (int s = 0; s < TubeSides; s++)
                    {
                        int a = i * TubeSides + s, b = i * TubeSides + (s + 1) % TubeSides;
                        triangles[t++] = a; triangles[t++] = b; triangles[t++] = a + TubeSides;
                        triangles[t++] = b; triangles[t++] = b + TubeSides; triangles[t++] = a + TubeSides;
                    }
                var mesh = new Mesh { name = tube.name };
                mesh.MarkDynamic(); mesh.vertices = tubeVertices; mesh.triangles = triangles; tube.sharedMesh = mesh;
            }
            var toLocal = tube.transform.worldToLocalMatrix;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < path.Length; i++)
            {
                var tangent = (path[Mathf.Min(i + 1, path.Length - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                // Carried from ring to ring (parallel transport) so the tube never twists.
                normal = Vector3.ProjectOnPlane(i == 0 ? Vector3.up : normal, tangent);
                if (normal.sqrMagnitude < 1e-8f) normal = Vector3.ProjectOnPlane(Vector3.right, tangent);
                normal.Normalize();
                var binormal = Vector3.Cross(tangent, normal);
                for (int s = 0; s < TubeSides; s++)
                {
                    float angle = s * 2 * Mathf.PI / TubeSides;
                    var radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
                    tubeVertices[i * TubeSides + s] = toLocal.MultiplyPoint3x4(path[i] + radial * tubeRadius);
                    tubeNormals[i * TubeSides + s] = toLocal.MultiplyVector(radial);
                }
            }
            tube.sharedMesh.SetVertices(tubeVertices); tube.sharedMesh.SetNormals(tubeNormals); tube.sharedMesh.RecalculateBounds();
        }
        private void OnValidate()
        {
            if(spans==null)return;
            foreach(var span in spans)if(span!=null){span.cached=false;span.cachedSide=Vector3.zero;}
        }

        private void LateUpdate() => RefreshShape();
    }
}
