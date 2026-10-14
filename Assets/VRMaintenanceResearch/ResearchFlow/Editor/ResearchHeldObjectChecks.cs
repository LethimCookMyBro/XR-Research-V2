#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Deterministic Edit Mode fixture: copies of the real training-object convex colliders, driven through the transformer
    // with synthetic grab points against the saved scene's actual static colliders. Not SDK input, not Quest evidence.
    public static class ResearchHeldObjectChecks
    {
        sealed class FixtureGrab : IGrabbable
        {
            public List<Pose> GrabPoints { get; } = new List<Pose>();
            public Transform Transform { get; set; }
        }

        const float Tolerance = .0025f; // transformer tolerance 2 mm + ComputePenetration numerical slack
        static float[] V(Vector3 v) => new[] { v.x, v.y, v.z };

        // Independent measurement from the colliders' actual transforms, not from the transformer's own bookkeeping.
        static float Depth(Collider[] own, Collider[] solids)
        {
            Physics.SyncTransforms(); float deepest = 0;
            var bounds = own[0].bounds; foreach (var c in own) bounds.Encapsulate(c.bounds); bounds.Expand(.01f);
            foreach (var o in own)
                foreach (var s in solids)
                    if (s && s.enabled && s.gameObject.activeInHierarchy && s.bounds.Intersects(bounds)
                        && Physics.ComputePenetration(o, o.transform.position, o.transform.rotation, s, s.transform.position, s.transform.rotation, out _, out float d))
                        deepest = Mathf.Max(deepest, d);
            return deepest;
        }

        static JObject[] Events(ResearchSessionFlowController controller)
        {
            var rows = new List<JObject>();
            using (var reader = new StreamReader(new FileStream(controller.Logger.EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                for (string line; (line = reader.ReadLine()) != null;) rows.Add(JObject.Parse(line));
            return rows.ToArray();
        }

        static void Drive(int side, Pose pose, float grip = 0) => ResearchFlowVisualInput.SetControllerWorldPose(side, pose, grip);

        // Native SDK path: the object is already gripped/lifted through the real HandGrabInteractor by the caller.
        public static IEnumerable<double> NativeContact(ResearchSessionFlowController controller, HandsLabObject item, int side, Action<bool, string, object> check)
        {
            var hand = side == 0 ? Handedness.Left : Handedness.Right;
            var constraint = item.GetComponent<ResearchHeldObjectConstraint>();
            check(constraint && constraint.Holding && item.IsHeld && item.body.isKinematic, item.stableId + " native SDK Grabbable drives the held-object transformer", item.CaptureSnapshot());
            var solids = ResearchHandContactProxy.StaticEnvironment(controller);
            var own = item.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger && c.attachedRigidbody == item.body).ToArray();
            var support = Physics.RaycastAll(item.transform.position + Vector3.up * .5f, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance).First(h => solids.Contains(h.collider));
            float surface = support.point.y;
            float Bottom() { Physics.SyncTransforms(); return own.Min(c => c.bounds.min.y); }
            var input = controller.guards.Single(g => g.side == hand).controllerReference as IController;
            var proxy = controller.origin.GetComponentsInChildren<ResearchHandContactProxy>(true).Single(p => p.handVisual.Hand.Handedness == hand);
            float VisualGap() => Vector3.Distance(proxy.handVisual.Root.position, item.transform.position);
            float freeGap = VisualGap(), maxDepth = 0, maxVisualChange = 0;
            var lifted = ResearchFlowVisualInput.ControllerWorldPose(side); var pose = lifted;
            int corrections = constraint.ReleaseCorrections;
            void Sample() { maxDepth = Mathf.Max(maxDepth, Depth(own, solids)); maxVisualChange = Mathf.Max(maxVisualChange, Mathf.Abs(VisualGap() - freeGap)); }

            // Vertical push: the commanded controller travels 60 cm down, far through the actual tabletop.
            for (int i = 0; i < 24; i++) { pose.position += Vector3.down * .025f; Drive(side, pose, 1); yield return .03d; Sample(); }
            yield return .1d; Sample();
            float gap = Bottom() - surface;
            input.TryGetPose(out Pose native);
            check(maxDepth <= Tolerance + .0005f && gap > -Tolerance && gap < .01f, item.stableId + " native held object stops at actual tabletop",
                new { max_penetration_m = maxDepth, gap, surface_collider = support.collider.name, desired_separation_m = constraint.DesiredSeparationMetres });
            check(Vector3.Distance(native.position, pose.position) < .01f && constraint.DesiredSeparationMetres > .1f && item.IsHeld,
                item.stableId + " raw native controller continues inside table; only the object pose is constrained",
                new { native = V(native.position), commanded = V(pose.position), desired_separation_m = constraint.DesiredSeparationMetres });
            check(maxVisualChange < .05f, item.stableId + " rendered hand stays with the constrained held object",
                new { free_hand_to_object_m = freeGap, max_change_m = maxVisualChange, proxy_separation_m = proxy.SeparationMetres, ghost_visible = proxy.GhostVisible });

            // Pull back, then one-frame fast downward move.
            for (int i = 0; i < 8; i++) { Drive(side, lifted, 1); yield return .03d; }
            float pulled = Bottom() - surface;
            pose = lifted; pose.position += Vector3.down * .5f; Drive(side, pose, 1); yield return .1d; Sample();
            gap = Bottom() - surface;
            check(pulled > .05f && maxDepth <= Tolerance + .0005f && gap > -Tolerance && gap < .01f, item.stableId + " native pull-back frees object and fast downward move does not tunnel",
                new { pulled_gap_m = pulled, gap, max_penetration_m = maxDepth });

            // Slide along the tabletop while the commanded target stays inside it.
            var along = Vector3.ProjectOnPlane(support.collider.bounds.center - item.transform.position, Vector3.up).normalized;
            var slideStart = item.transform.position;
            for (int i = 0; i < 10; i++) { pose.position += along * .015f; Drive(side, pose, 1); yield return .03d; Sample(); }
            float slid = Vector3.Dot(item.transform.position - slideStart, along); gap = Bottom() - surface;
            check(slid > .10f && gap > -Tolerance && gap < .01f && maxDepth <= Tolerance + .0005f, item.stableId + " native pressed object slides along tabletop",
                new { slid_m = slid, requested_m = .15f, gap, max_penetration_m = maxDepth });

            // Release while pressed deep into the tabletop.
            long before = Events(controller).Max(e => (long)e["seq"]);
            bool captured = false; Vector3 releaseVelocity = Vector3.positiveInfinity;
            // Subscribed after HandsLabObject, so this observes the velocity it actually applied.
            Action<PointerEvent> capture = e => { if (e.Type == PointerEventType.Unselect) { captured = true; releaseVelocity = item.body.linearVelocity; } };
            item.grabbable.WhenPointerEventRaised += capture;
            try { for (int i = 0; i < 12; i++) { Drive(side, pose); yield return .03d; } }
            finally { item.grabbable.WhenPointerEventRaised -= capture; }
            check(captured && !item.IsHeld && !constraint.Holding && !item.body.isKinematic && item.body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic
                && releaseVelocity.magnitude < .5f, item.stableId + " release while pressed restores dynamic ContinuousDynamic body without into-table impulse",
                new { captured, velocity = V(releaseVelocity), mode = item.body.collisionDetectionMode.ToString(), release_corrections = constraint.ReleaseCorrections - corrections });
            yield return .8d;
            gap = Bottom() - surface; float settled = Depth(own, solids);
            check(gap > -Tolerance && gap < .02f && settled <= Tolerance && item.body.linearVelocity.magnitude < .1f, item.stableId + " released object settles on actual tabletop",
                new { gap, penetration_m = settled, velocity = V(item.body.linearVelocity) });
            yield return 1d;
            gap = Bottom() - surface;
            check(gap > -Tolerance, item.stableId + " released object does not fall through tabletop", new { gap });
            var logged = Events(controller).Where(e => (long)e["seq"] > before && (string)e["target"] == item.stableId).ToArray();
            check(logged.Count(e => (string)e["event"] == "ACTION" && (string)e["result"] == "Release" && (string)e["source"] == "User") == 1
                && !logged.Any(e => (string)e["event"] == "SYSTEM_RELEASE"), item.stableId + " pressed release logs one participant ACTION Release", logged);
            check(constraint.SaturatedQueries == 0 && constraint.UnresolvedPoses == 0, item.stableId + " constraint queries neither saturated nor unresolved",
                new { constraint.Updates, constraint.ConstrainedUpdates, constraint.RejectedSubsteps, constraint.ReleaseCorrections,
                    constraint.MaxDesiredSeparationMetres, constraint.LastUpdateMilliseconds, constraint.MaxUpdateMilliseconds });
            for (int i = 0; i < 6; i++) { Drive(side, lifted); yield return .03d; }
        }

        public static IEnumerable<double> NativeThrow(ResearchSessionFlowController controller, HandsLabObject item, int side, Action<bool, string, object> check)
        {
            var direction = Vector3.ProjectOnPlane(controller.view.Head.position - item.transform.position, Vector3.up).normalized;
            var pose = ResearchFlowVisualInput.ControllerWorldPose(side);
            long before = Events(controller).Max(e => (long)e["seq"]);
            bool captured = false; Vector3 releaseVelocity = Vector3.zero;
            Action<PointerEvent> capture = e => { if (e.Type == PointerEventType.Unselect) { captured = true; releaseVelocity = item.body.linearVelocity; } };
            item.grabbable.WhenPointerEventRaised += capture;
            try
            {
                for (int i = 0; i < 6; i++) { pose.position += direction * .05f; Drive(side, pose, 1); yield return .03d; }
                for (int i = 0; i < 8; i++) { pose.position += direction * .05f; Drive(side, pose); yield return .03d; }
            }
            finally { item.grabbable.WhenPointerEventRaised -= capture; }
            var logged = Events(controller).Where(e => (long)e["seq"] > before && (string)e["target"] == item.stableId
                && (string)e["event"] == "ACTION" && (string)e["result"] == "Release").ToArray();
            var loggedVelocity = logged.Length == 1 ? logged[0]["after"]?["velocity"]?.ToObject<float[]>() : null;
            check(captured && !item.body.isKinematic && releaseVelocity.magnitude > .5f && Vector3.Dot(releaseVelocity.normalized, direction) > .7f
                && loggedVelocity != null && Vector3.Distance(new Vector3(loggedVelocity[0], loggedVelocity[1], loggedVelocity[2]), releaseVelocity) < .001f,
                item.stableId + " native throw applies and logs motion-sampled release velocity", new { velocity = V(releaseVelocity), direction = V(direction), logged });
            yield return .8d;
        }

        public static string Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ResearchFlowSceneBuilder.ScenePath)
                throw new InvalidOperationException("Held-object fixture requires the saved integration scene in Edit Mode.");
            var env = scene.GetRootGameObjects().Single(r => r.name == "Environment_V2");
            var solids = ResearchHandContactProxy.StaticEnvironment(env.transform);
            var items = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<HandsLabObject>(true))
                .Where(o => o.stableId == "handslab.grip_mug" || o.stableId == "handslab.pinch_stone").ToArray();
            if (items.Length != 2) throw new InvalidOperationException("Expected the two saved training objects.");
            var cases = new List<object>(); int checks = 0;
            void Check(bool ok, string name, object detail = null) { if (!ok) throw new InvalidOperationException(name + " " + JsonConvert.SerializeObject(detail)); checks++; }
            var created = new List<GameObject>();
            try
            {
                foreach (var real in items)
                {
                    foreach (var name in new[] { "Training_Practice_A", "Computer_TaskCase", "Fan_TaskFan" })
                        Surfaces(real, solids, solids.Single(c => c.name == name), cases, Check, created);
                    ThinAndCarry(real, created, cases, Check);
                }
            }
            finally { foreach (var go in created) if (go) UnityEngine.Object.DestroyImmediate(go); Physics.SyncTransforms(); }
            Directory.CreateDirectory("Logs/QuestFinalIntegration/held-object");
            File.WriteAllText("Logs/QuestFinalIntegration/held-object/editor-fixture.json", JsonConvert.SerializeObject(new
            { hardware_verified = false, input = "Direct ITransformer calls with synthetic grab points; real object collider meshes; saved scene static colliders",
                tolerance_metres = Tolerance, checks, cases }, Formatting.Indented));
            return "HELD_OBJECT_EDITOR_PASS checks=" + checks + " cases=" + cases.Count + "; Editor geometry only, not SDK input or Quest physics";
        }

        sealed class Fixture
        {
            public GameObject root; public ResearchHeldObjectConstraint constraint; public FixtureGrab grab; public Collider[] colliders;
            public Collider[] solids; public Pose grabOffset;
            public float Bottom { get { Physics.SyncTransforms(); return colliders.Min(c => c.bounds.min.y); } }
            public float Depth() => ResearchHeldObjectChecks.Depth(colliders, solids);
            public void Hold(Vector3 position, Quaternion rotation)
            {
                root.transform.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
                grab.GrabPoints.Clear(); grab.GrabPoints.Add(new Pose(position + rotation * grabOffset.position, rotation * grabOffset.rotation));
                constraint.BeginTransform(); Physics.SyncTransforms();
            }
            public Pose Grab => grab.GrabPoints[0];
            public void Move(Pose target) { grab.GrabPoints[0] = target; constraint.UpdateTransform(); Physics.SyncTransforms(); }
        }

        static Fixture Create(HandsLabObject real, Collider[] environment, List<GameObject> created)
        {
            var root = new GameObject("HeldObject_Fixture_" + real.stableId) { hideFlags = HideFlags.HideAndDontSave };
            created.Add(root);
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            foreach (var source in real.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && c.GetComponentInParent<Rigidbody>() == real.body))
            {
                var mesh = source as MeshCollider ?? throw new InvalidOperationException("Fixture copies only the real convex mesh colliders.");
                var child = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }.transform;
                child.SetParent(root.transform, false);
                child.localPosition = real.transform.InverseTransformPoint(source.transform.position);
                child.localRotation = Quaternion.Inverse(real.transform.rotation) * source.transform.rotation;
                child.localScale = source.transform.lossyScale;
                var copy = child.gameObject.AddComponent<MeshCollider>(); copy.sharedMesh = mesh.sharedMesh; copy.convex = true;
            }
            var grab = new FixtureGrab { Transform = root.transform };
            var constraint = root.AddComponent<ResearchHeldObjectConstraint>();
            constraint.ConfigureEnvironment(environment); constraint.Initialize(grab);
            // A handle-like offset: the grab point sits beside and above the object origin, as HandGrab poses do.
            return new Fixture { root = root, constraint = constraint, grab = grab, solids = environment,
                colliders = root.GetComponentsInChildren<Collider>(), grabOffset = new Pose(new Vector3(.07f, .03f, 0), Quaternion.Euler(0, 0, 20)) };
        }

        static RaycastHit FirstSolid(Ray ray, float distance, Collider[] solids)
        {
            foreach (var hit in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (solids.Contains(hit.collider)) return hit;
            throw new InvalidOperationException("No actual static surface along " + ray);
        }

        static void Surfaces(HandsLabObject real, Collider[] solids, Collider target, List<object> cases, Action<bool, string, object> Check, List<GameObject> created)
        {
            var f = Create(real, solids, created);
            string id = real.stableId + "/" + target.name;
            var b = target.bounds; var rotation = real.transform.rotation;
            // Measure the object's own bottom offset once, then work only from actual raycast surfaces.
            f.root.transform.SetPositionAndRotation(new Vector3(0, 500, 0), rotation);
            float bottomOffset = 500 - f.Bottom;
            var metrics = new List<object>();
            void Record(string name, float maxDepth, int constrainedBefore, int rejectedBefore, object extra = null) => metrics.Add(new
            { name, max_penetration_m = maxDepth, constrained_updates = f.constraint.ConstrainedUpdates - constrainedBefore,
                rejected_substeps = f.constraint.RejectedSubsteps - rejectedBefore, max_desired_separation_m = f.constraint.MaxDesiredSeparationMetres,
                max_update_ms = f.constraint.MaxUpdateMilliseconds, extra });

            if (target.name == "Training_Practice_A")
            {
                var along = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
                // Pick an uncluttered patch whose whole slide path is the actual table surface at one height.
                RaycastHit top = default; bool found = false;
                for (int i = 1; i < 9 && !found; i++)
                    for (int j = 1; j < 9 && !found; j++)
                    {
                        var start = new Vector3(Mathf.Lerp(b.min.x, b.max.x, i / 9f), b.max.y + 1, Mathf.Lerp(b.min.z, b.max.z, j / 9f));
                        top = FirstSolid(new Ray(start, Vector3.down), 3, solids);
                        var end = FirstSolid(new Ray(start + along * .3f, Vector3.down), 3, solids);
                        found = top.collider == target && end.collider == target && Mathf.Abs(top.point.y - end.point.y) < .001f
                            && Physics.OverlapSphere(top.point + Vector3.up * .15f + along * .15f, .14f, ~0, QueryTriggerInteraction.Ignore).All(c => !solids.Contains(c));
                    }
                if (!found) throw new InvalidOperationException("No uncluttered actual tabletop patch on " + target.name);
                float surface = top.point.y;
                Vector3 above = new Vector3(top.point.x, surface + bottomOffset + .10f, top.point.z);

                // 1. grab above table: no motion requested, no motion applied.
                f.Hold(above, rotation); f.Move(f.Grab);
                Check(Vector3.Distance(f.root.transform.position, above) < .0001f && f.Depth() == 0, id + " grab above table keeps pose", null);

                // 2. push vertically into tabletop: requested target ends 24 cm below the object's contact pose.
                int c0 = f.constraint.ConstrainedUpdates, r0 = f.constraint.RejectedSubsteps; float worst = 0;
                var g = f.Grab;
                for (int i = 0; i < 34; i++) { g.position += Vector3.down * .01f; f.Move(g); worst = Mathf.Max(worst, f.Depth()); }
                float gap = f.Bottom - surface;
                Check(worst <= Tolerance && gap > -Tolerance && gap < .006f, id + " vertical push stops at tabletop contact", new { worst, gap });
                Check(f.constraint.DesiredSeparationMetres > .2f, id + " requested target continues through table while object stays", f.constraint.DesiredSeparationMetres);
                Record("vertical_push", worst, c0, r0, new { final_gap_m = gap, requested_below_contact_m = f.constraint.DesiredSeparationMetres });

                // 3. fast downward: one frame from 10 cm above to 60 cm below.
                f.constraint.EndTransform(); f.Hold(above, rotation); c0 = f.constraint.ConstrainedUpdates; r0 = f.constraint.RejectedSubsteps;
                g = f.Grab; g.position += Vector3.down * .70f; f.Move(g);
                gap = f.Bottom - surface; worst = f.Depth();
                Check(worst <= Tolerance && gap > -Tolerance && gap < .006f, id + " single-frame fast downward move does not tunnel", new { worst, gap });
                Record("fast_downward_single_frame_0.70m", worst, c0, r0, new { final_gap_m = gap });

                // 4. slide along tabletop while the requested target stays ~60 cm below the surface.
                c0 = f.constraint.ConstrainedUpdates; r0 = f.constraint.RejectedSubsteps; worst = 0;
                var startSlide = f.root.transform.position;
                for (int i = 0; i < 20; i++) { g.position += along * .01f; f.Move(g); worst = Mathf.Max(worst, f.Depth()); }
                float slid = Vector3.Dot(f.root.transform.position - startSlide, along); gap = f.Bottom - surface;
                Check(worst <= Tolerance && slid > .18f && gap > -Tolerance && gap < .006f, id + " pressed object slides along tabletop", new { worst, slid, gap });
                Record("slide_while_pressed", worst, c0, r0, new { slid_m = slid, requested_m = .20f, final_gap_m = gap });

                // 6. rotate into tabletop while pressed: 90 degrees about a horizontal axis.
                var axis = Vector3.Cross(Vector3.up, along).normalized; var startRotation = f.root.transform.rotation;
                c0 = f.constraint.ConstrainedUpdates; r0 = f.constraint.RejectedSubsteps; worst = 0;
                for (int i = 0; i < 18; i++) { g.rotation = Quaternion.AngleAxis(5, axis) * g.rotation; f.Move(g); worst = Mathf.Max(worst, f.Depth()); }
                float turned = Quaternion.Angle(startRotation, f.root.transform.rotation); gap = f.Bottom - surface;
                Check(worst <= Tolerance && turned > 80 && gap > -Tolerance, id + " rotation into tabletop lifts instead of penetrating", new { worst, turned, gap });
                Record("rotate_into_tabletop_90deg", worst, c0, r0, new { turned_deg = turned, final_gap_m = gap });

                // 9. release while touching tabletop.
                int corrections = f.constraint.ReleaseCorrections;
                f.constraint.EndTransform();
                Check(!f.constraint.Holding && f.Depth() <= Tolerance && f.constraint.ReleaseCorrections == corrections,
                    id + " release while touching tabletop is already depenetrated", f.Depth());

                // 10. release after a deep attempt: 30 cm through in 10 frames, then release.
                f.Hold(above, rotation); g = f.Grab;
                for (int i = 0; i < 10; i++) { g.position += Vector3.down * .04f; f.Move(g); }
                f.constraint.EndTransform(); gap = f.Bottom - surface;
                Check(f.Depth() <= Tolerance && gap > -Tolerance, id + " release after deep attempt leaves a valid pose", new { depth = f.Depth(), gap });
                // Release guard itself: an externally embedded pose is pushed outward before the SDK unlocks physics.
                // Embed less than the object's height: a prop wholly inside a surface-only mesh touches no triangles.
                f.Hold(above, rotation);
                float height = f.colliders.Max(c => c.bounds.max.y) - f.Bottom, embed = Mathf.Min(.03f, height * .4f);
                f.root.transform.position = new Vector3(above.x, surface + bottomOffset - embed, above.z); Physics.SyncTransforms();
                float embedded = f.Depth(); corrections = f.constraint.ReleaseCorrections;
                f.constraint.EndTransform(); gap = f.Bottom - surface;
                Check(embedded > embed * .5f && f.constraint.ReleaseCorrections == corrections + 1 && f.Depth() <= Tolerance && gap > -Tolerance,
                    id + " release guard resolves an embedded pose outward", new { embedded, after = f.Depth(), gap });
                Record("release_guard_embedded", embedded, 0, 0, new { embed_m = embed, object_height_m = height, after_m = f.Depth(), final_gap_m = gap });

                // 5. push horizontally against the actual tabletop edge.
                var inward = b.size.x > b.size.z ? Vector3.back : Vector3.left;
                if (!target.Raycast(new Ray(new Vector3(top.point.x, surface - .01f, top.point.z) - inward * 2, inward), out RaycastHit edge, 3))
                    throw new InvalidOperationException(target.name + " has no sampled front edge");
                var outside = edge.point - inward * (bottomOffset + .12f); outside.y = surface - .005f;
                f.Hold(outside, rotation); g = f.Grab; c0 = f.constraint.ConstrainedUpdates; r0 = f.constraint.RejectedSubsteps; worst = 0;
                for (int i = 0; i < 40; i++) { g.position += inward * .01f; f.Move(g); worst = Mathf.Max(worst, f.Depth()); }
                var p = f.root.transform.position;
                bool outsideEdge = Vector3.Dot(p - edge.point, -inward) > 0, onTop = f.Bottom > surface - Tolerance;
                Check(worst <= Tolerance && (outsideEdge || onTop), id + " push against tabletop edge stays outside the edge", new { worst, outsideEdge, onTop });
                Record("push_table_edge", worst, c0, r0, new { outside_edge = outsideEdge, climbed_on_top = onTop });
                f.constraint.EndTransform();

                // 7. rotate around the table corner/edge while pressing down and inward.
                var corner = new Vector3(edge.point.x, surface + bottomOffset + .01f, edge.point.z) + inward * .01f;
                f.Hold(corner, rotation); g = f.Grab; c0 = f.constraint.ConstrainedUpdates; r0 = f.constraint.RejectedSubsteps; worst = 0;
                var diagonal = (inward + along).normalized;
                for (int i = 0; i < 24; i++)
                {
                    g.rotation = Quaternion.AngleAxis(-5, Vector3.Cross(Vector3.up, inward)) * Quaternion.AngleAxis(3, diagonal) * g.rotation;
                    g.position += (Vector3.down + inward * .3f) * .01f; f.Move(g); worst = Mathf.Max(worst, f.Depth());
                }
                p = f.root.transform.position;
                bool overTable = Vector3.Dot(p - edge.point, inward) > .01f;
                Check(worst <= Tolerance && (!overTable || f.Bottom > surface - Tolerance), id + " rotation around tabletop edge does not enter the table", new { worst, overTable });
                Record("rotate_around_edge", worst, c0, r0, new { over_table = overTable, final_gap_m = f.Bottom - surface });
                f.constraint.EndTransform();
            }
            else
            {
                // 8. push against actual PC case / fan casing side.
                RaycastHit side = default; float reach = b.extents.magnitude + 1;
                if (!new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left }
                    .Any(d => target.Raycast(new Ray(b.center - d * reach, d), out side, reach * 2)))
                    throw new InvalidOperationException(target.name + " has no sampled side surface");
                var normal = side.normal;
                f.Hold(side.point + normal * (bottomOffset + .15f), rotation);
                var g = f.Grab; int c0 = f.constraint.ConstrainedUpdates, r0 = f.constraint.RejectedSubsteps; float worst = 0;
                for (int i = 0; i < 40; i++) { g.position -= normal * .01f; f.Move(g); worst = Mathf.Max(worst, f.Depth()); }
                float front = Vector3.Dot(f.root.transform.position - side.point, normal);
                Check(worst <= Tolerance && front > 0, id + " push against casing stays outside the actual casing surface", new { worst, front, hit = side.collider.name });
                Record("push_casing", worst, c0, r0, new { surface_collider = side.collider.name, centre_in_front_m = front });
                f.constraint.EndTransform();
            }
            cases.Add(new { target = id, metrics });
            f.root.SetActive(false);
        }

        // Thin/zero-thickness surfaces and rig teleport, isolated from the scene at y=100.
        static void ThinAndCarry(HandsLabObject real, List<GameObject> created, List<object> cases, Action<bool, string, object> Check)
        {
            var surfaceRoot = new GameObject("HeldObject_ThinSurfaces") { hideFlags = HideFlags.HideAndDontSave }; created.Add(surfaceRoot);
            surfaceRoot.transform.position = new Vector3(0, 100, 0);
            var box = surfaceRoot.AddComponent<BoxCollider>(); box.size = new Vector3(2, .001f, 2);
            var quadMesh = new Mesh { vertices = new[] { new Vector3(-1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 0, -1) }, triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            var quadGo = new GameObject("ZeroThicknessQuad") { hideFlags = HideFlags.HideAndDontSave }; created.Add(quadGo);
            quadGo.transform.position = new Vector3(5, 100, 0);
            var quad = quadGo.AddComponent<MeshCollider>(); quad.sharedMesh = quadMesh; quad.convex = false;
            var rows = new List<object>();
            foreach (var (surface, x) in new[] { ((Collider)box, 0f), (quad, 5f) })
            {
                var f = Create(real, new[] { surface }, created);
                f.root.transform.SetPositionAndRotation(new Vector3(x, 500, 0), real.transform.rotation);
                float bottomOffset = 500 - f.Bottom;
                f.Hold(new Vector3(x, 100 + bottomOffset + .1f, 0), real.transform.rotation);
                var g = f.Grab; g.position += Vector3.down * .8f; f.Move(g);
                float gap = f.Bottom - 100;
                Check(gap > -Tolerance && gap < .006f, real.stableId + " single-frame 0.8 m move does not tunnel " + surface.name, new { gap });
                rows.Add(new { surface = surface is BoxCollider ? "1mm box" : "zero-thickness mesh quad", final_gap_m = gap, max_update_ms = f.constraint.MaxUpdateMilliseconds });
                f.constraint.EndTransform(); f.root.SetActive(false);
            }
            // Teleport/snap: the held pose rides with the rig rather than being swept across the room.
            var carry = Create(real, new Collider[] { box }, created);
            var origin = new GameObject("HeldObject_FixtureOrigin") { hideFlags = HideFlags.HideAndDontSave }; created.Add(origin);
            origin.transform.position = new Vector3(0, 100, 0); carry.constraint.trackingOrigin = origin.transform;
            carry.Hold(new Vector3(0, 101, 0), real.transform.rotation);
            var jump = new Pose(new Vector3(3, 0, 1), Quaternion.Euler(0, 45, 0));
            var before = carry.Grab;
            origin.transform.SetPositionAndRotation(origin.transform.position + jump.position, jump.rotation);
            var moved = new Pose(origin.transform.position + jump.rotation * (before.position - new Vector3(0, 100, 0)), jump.rotation * before.rotation);
            carry.Move(moved);
            var expected = moved.position + moved.rotation * Quaternion.Inverse(before.rotation) * (new Vector3(0, 101, 0) - before.position);
            float error = Vector3.Distance(carry.root.transform.position, expected);
            Check(error < .001f, real.stableId + " rig teleport and snap turn carry the held object", new { error });
            rows.Add(new { surface = "rig teleport 3.16 m + 45 deg snap", carried_error_m = error });
            carry.constraint.EndTransform();
            cases.Add(new { target = real.stableId + "/synthetic_thin_and_carry", metrics = rows });
        }
    }
}
#endif
