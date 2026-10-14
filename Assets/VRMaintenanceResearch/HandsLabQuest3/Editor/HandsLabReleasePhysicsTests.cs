#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.HandsLabQuest3.Editor
{
    public static partial class HandsLabEditor
    {
        public static void VerifyReleaseSampling()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required");
            var evidence = new List<object>();
            var go = new GameObject("HandsLabReleaseSamplingCheck") { hideFlags = HideFlags.HideAndDontSave };
            go.SetActive(false);
            try
            {
                var item = go.AddComponent<HandsLabObject>();
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(HandsLabObject);
                var sample = type.GetMethod("SampleReleaseMotion", flags, null, new[] { typeof(double) }, null);
                if (sample == null) throw new InvalidOperationException("SampleReleaseMotion(double now) required");
                var reset = type.GetMethod("ResetReleaseMotion", flags);
                var calculate = type.GetMethod("CalculateReleaseMotion", flags);
                double now = 100d;
                void Begin()
                {
                    now = 100d;
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    reset.Invoke(item, new object[] { now });
                }
                void Feed(Vector3 position, Quaternion rotation, double elapsed = .02d)
                {
                    now += elapsed;
                    go.transform.SetPositionAndRotation(position, rotation);
                    sample.Invoke(item, new object[] { now });
                }
                void Expect(string name, Vector3 expectedLinear, Vector3 expectedAngular, float tolerance = .01f)
                {
                    var values = new object[] { null, null };
                    calculate.Invoke(item, values);
                    var linear = (Vector3)values[0]; var angular = (Vector3)values[1];
                    bool ok = Vector3.Distance(linear, expectedLinear) < tolerance && Vector3.Distance(angular, expectedAngular) < tolerance;
                    evidence.Add(new { name, passed = ok, linear = linear.ToString("F6"), angular = angular.ToString("F6"), expectedLinear = expectedLinear.ToString("F6"), expectedAngular = expectedAngular.ToString("F6") });
                    if (!ok) throw new Exception(name);
                }

                Begin();
                for (int i = 1; i <= 12; i++) Feed(Vector3.right * (.01f * i), Quaternion.identity);
                Expect("recent movement retains direction and 0.5 m/s magnitude", Vector3.right * .5f, Vector3.zero);
                for (int i = 0; i < 12; i++) Feed(Vector3.right * .12f, Quaternion.identity);
                Expect("stopped movement expires stale momentum", Vector3.zero, Vector3.zero);

                Begin();
                for (int i = 1; i <= 12; i++) Feed(Vector3.zero, Quaternion.AngleAxis(i, Vector3.up));
                Expect("pure rotation with constant position retains angular velocity", Vector3.zero, Vector3.up * (50f * Mathf.Deg2Rad));

                Begin();
                for (int i = 1; i <= 12; i++) Feed(Vector3.right * (.01f * i), Quaternion.identity);
                Feed(Vector3.right * 10f, Quaternion.identity);
                Expect("impossible position spike clears release estimate", Vector3.zero, Vector3.zero);
                for (int i = 1; i <= 12; i++) Feed(Vector3.right * (10f + .01f * i), Quaternion.identity);
                Expect("valid history recovers after position spike", Vector3.right * .5f, Vector3.zero);
                Feed(go.transform.position, Quaternion.AngleAxis(180f, Vector3.up));
                Expect("impossible angular spike clears release estimate", Vector3.zero, Vector3.zero);
                for (int i = 1; i <= 12; i++) Feed(go.transform.position, Quaternion.AngleAxis(180f + i, Vector3.up));
                Expect("valid rotation recovers after angular spike", Vector3.zero, Vector3.up * (50f * Mathf.Deg2Rad));
                Feed(go.transform.position, go.transform.rotation, .25d);
                Expect("stale observation gap discards motion history", Vector3.zero, Vector3.zero);

                Begin();
                for (int i = 1; i <= 12; i++) Feed(Vector3.left * (.01f * i), Quaternion.identity);
                var savedPosition = go.transform.position;
                Feed(Vector3.right * 100f, Quaternion.identity, 0d);
                Expect("duplicate timestamp cannot contaminate motion estimate", Vector3.left * .5f, Vector3.zero);
                Feed(savedPosition - Vector3.right * .01f, Quaternion.identity);
                Expect("valid sample after duplicate preserves direction and magnitude", Vector3.left * .5f, Vector3.zero);
                Begin();
                for (int i = 1; i <= 287; i++) Feed(Vector3.right * (.012f * (i / 18)), Quaternion.identity, 1d / 600);
                Expect("600 FPS with 30 Hz pose updates retains motion window", Vector3.right * .4f, Vector3.zero, .12f);
                Begin();
                for (int i = 1; i <= 287; i++) Feed(Vector3.zero, Quaternion.AngleAxis(4f * (i / 18), Vector3.up), 1d / 600);
                Expect("repeated poses retain angular motion without random zero", Vector3.zero, Vector3.up * (120f * Mathf.Deg2Rad), .5f);
                Write("release_sampling_result", new { passed = true, hardwareVerified = false, clock = "controlled monotonic timestamps", evidence });
            }
            catch (Exception error)
            {
                Write("release_sampling_result", new { passed = false, hardwareVerified = false, error = error.ToString(), evidence });
                throw;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        public static void VerifyReleaseConfiguration()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required");
            var evidence = new List<object>();
            bool passed = true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRMaintenanceResearch/HandsLabQuest3/Prefabs/HandsLab_Quest3.prefab");
            var roots = new[] { Lab ? Lab.gameObject : null, prefab };
            for (int index = 0; index < roots.Length; index++)
            {
                var objects = roots[index] ? roots[index].GetComponentsInChildren<HandsLabObject>(true) : Array.Empty<HandsLabObject>();
                bool expectedObjects = objects.Length == 2 && objects.Any(o => o.stableId == "handslab.grip_mug") && objects.Any(o => o.stableId == "handslab.pinch_stone");
                passed &= expectedObjects;
                evidence.Add(new { scope = index == 0 ? "scene" : "prefab", expectedObjects });
                foreach (var item in objects)
                {
                    var serialized = item.grabbable ? new SerializedObject(item.grabbable) : null;
                    bool bodyWired = item.body && serialized != null && serialized.FindProperty("_rigidbody").objectReferenceValue == item.body;
                    bool throwEnabled = serialized != null && serialized.FindProperty("_throwWhenUnselected").boolValue;
                    bool dynamicGravity = item.body && !item.body.isKinematic && item.body.useGravity && item.body.constraints == RigidbodyConstraints.None;
                    bool noAutomaticRespawn = !item.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.enabled && c.GetType().Name == "RespawnOnDrop");
                    bool ok = bodyWired && throwEnabled && dynamicGravity && noAutomaticRespawn;
                    passed &= ok;
                    evidence.Add(new { scope = index == 0 ? "scene" : "prefab", target = item.stableId, bodyWired, throwEnabled, dynamicGravity, noAutomaticRespawn, passed = ok });
                }
            }
            Write("release_configuration_result", new { passed, hardwareVerified = false, evidence });
            if (!passed) throw new Exception("Release configuration failed; see release_configuration_result.json");
        }

        static List<JObject> ReleaseEvents()
        {
            var rows = new List<JObject>();
            using (var stream = new FileStream(Path.Combine(Lab.Logger.DirectoryPath, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                while (!reader.EndOfStream) rows.Add(JObject.Parse(reader.ReadLine()));
            return rows;
        }

        static void CheckSettled(HandsLabObject item,string context,string surface=null)
        {
            var solid=item.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).ToArray();
            var bounds=solid[0].bounds;
            foreach(var collider in solid.Skip(1))bounds.Encapsulate(collider.bounds);
            var supports=Physics.RaycastAll(bounds.center+Vector3.up*.1f,Vector3.down,4f,~0,QueryTriggerInteraction.Ignore)
                .Where(h=>!h.collider.attachedRigidbody&&h.normal.y>.9f&&h.point.y<=bounds.max.y).OrderBy(h=>h.distance).ToArray();
            Check(supports.Length>0&&Mathf.Abs(bounds.min.y-supports[0].point.y)<.04f
                &&(surface==null||supports[0].collider.name==surface),item.stableId+context+" rests on static surface without tunnelling or hover");
            Check(item.body.linearVelocity.magnitude<.05f&&item.body.angularVelocity.magnitude<.15f,item.stableId+context+" settles");
            inputEvidence.Add(new {kind="SETTLED_PHYSICS",target=item.stableId,context,surface=supports[0].collider.name,
                bottom=bounds.min.y,surfaceHeight=supports[0].point.y,velocity=item.body.linearVelocity.ToString("F6"),angular=item.body.angularVelocity.ToString("F6")});
        }
        static Vector3 ReleaseVector(JToken value) => new Vector3((float)value[0], (float)value[1], (float)value[2]);

        static IEnumerable<double> ReleaseGrip(HandsLabObject item)
        {
            rightGrip = rightTrigger = leftGrip = leftTrigger = 0;
            Lab.ResetObjects();
            yield return .7d;
            var pose = PickPose(item, 1);
            for (int i = 0; i < 24; i++) { AimWrist(1, pose); yield return .06d; }
            for (int i = 0; i < 12; i++) { ApproachPalm(1, item); yield return .08d; }
            rightGrip = 1;
            for (int i = 0; i < 12; i++) { rightPosition += Vector3.right * .0001f; yield return .03d; }
            Check(item.IsHeld && item.body.isKinematic, item.stableId + " grip selects through SDK and locks kinematic");
            float originalHeight = item.transform.position.y;
            for (int i = 0; i < 10; i++) { rightPosition += Vector3.up * .025f; yield return .03d; }
            Check(item.IsHeld && item.transform.position.y > originalHeight + .18f, item.stableId + " held lift follows controller");
        }

        static IEnumerator ReleasePhysicsSequence()
        {
            yield return 1d;
            Check(Lab.guards.All(g => g.TrackingValid && g.InteractionsAllowed), "release tests have valid simulated controller tracking");
            foreach (var item in Lab.objects)
            {
                Mark(item.stableId + " lifted stationary drop");
                foreach (double delay in ReleaseGrip(item)) yield return delay;
                // Hold after movement: stale lift velocity must not become a throw.
                yield return 1d;
                var releaseVelocity = Vector3.zero;
                var releaseAngular = Vector3.zero;
                var releasePosition = Vector3.zero;
                bool unselected = false, dynamicGravity = false;
                Action<PointerEvent> capture = evt =>
                {
                    if (evt.Type != PointerEventType.Unselect) return;
                    unselected = true;
                    releaseVelocity = item.body.linearVelocity;
                    releaseAngular = item.body.angularVelocity;
                    releasePosition = item.body.position;
                    dynamicGravity = !item.body.isKinematic && item.body.useGravity;
                };
                item.grabbable.WhenPointerEventRaised += capture;
                int start = ReleaseEvents().Count;
                try
                {
                    rightGrip = 0;
                    yield return .08d;
                    inputEvidence.Add(new { kind = "DROP_OBSERVATION", target = item.stableId, unselected, dynamicGravity, releaseVelocity = releaseVelocity.ToString("F6"), releaseAngular = releaseAngular.ToString("F6"), releasePosition = releasePosition.ToString("F6"), position = item.body.position.ToString("F6"), velocity = item.body.linearVelocity.ToString("F6") });
                    Check(unselected && !item.IsHeld && dynamicGravity, item.stableId + " normal release is dynamic with gravity");
                    Check(releaseVelocity.magnitude < .08f && releaseAngular.magnitude < .15f, item.stableId + " stopped lift releases near stationary");
                    Check(item.body.position.y < releasePosition.y - .005f && item.body.linearVelocity.y < -.05f, item.stableId + " gravity lowers object after release");
                    CheckReleaseLog(item, start, releaseVelocity, releaseAngular);
                    yield return 3d;
                    CheckSettled(item," stationary drop","Table");
                }
                finally { item.grabbable.WhenPointerEventRaised -= capture; }

                Mark(item.stableId + " moving and rotating release");
                foreach (double delay in ReleaseGrip(item)) yield return delay;
                yield return .4d;
                unselected = false;
                item.grabbable.WhenPointerEventRaised += capture;
                start = ReleaseEvents().Count;
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        rightPosition += Vector3.right * .012f;
                        rightRotation = Quaternion.AngleAxis(4f, Vector3.up) * rightRotation;
                        yield return .03d;
                    }
                    rightGrip = 0;
                    rightPosition += Vector3.right * .006f;
                    rightRotation = Quaternion.AngleAxis(2f, Vector3.up) * rightRotation;
                    yield return .08d;
                    inputEvidence.Add(new { kind = "MOVING_RELEASE_OBSERVATION", target = item.stableId, unselected, dynamicGravity, releaseVelocity = releaseVelocity.ToString("F6"), releaseAngular = releaseAngular.ToString("F6"), releasePosition = releasePosition.ToString("F6"), position = item.body.position.ToString("F6") });
                    Check(unselected && !item.IsHeld && dynamicGravity, item.stableId + " moving release clears SDK ownership");
                    Check(releaseVelocity.x > .05f, item.stableId + " movement history transfers forward linear momentum");
                    Check(releaseAngular.y > .1f, item.stableId + " wrist history transfers signed angular momentum");
                    Check(Vector3.Dot(item.body.position - releasePosition, releaseVelocity.normalized) > .002f, item.stableId + " continues in release direction after unselect");
                    CheckReleaseLog(item, start, releaseVelocity, releaseAngular);
                    var action = (string)ReleaseEvents().Last(e => (string)e["event"] == "RELEASE" && (string)e["target"] == item.stableId)["action_id"];
                    Check(ReleaseEvents().Any(e => (string)e["event"] == "ROTATE" && (string)e["action_id"] == action), item.stableId + " rotation still emits ROTATE in same grab episode");
                    yield return 3d;
                    CheckSettled(item," moving release");
                }
                finally { item.grabbable.WhenPointerEventRaised -= capture; }
                Mark(item.stableId + " stationary drop outside table onto floor");
                foreach(double delay in ReleaseGrip(item))yield return delay;
                for(int i=0;i<20;i++){rightPosition+=Vector3.right*.05f;yield return .03d;}
                yield return 1d;
                Check(item.IsHeld,item.stableId+" remains held beyond table edge");
                rightGrip=0;yield return 3d;
                Check(!item.IsHeld,item.stableId+" releases over floor");
                CheckSettled(item," floor drop","Floor");
            }

            var mug = Lab.objects.Single(o => o.stableId == "handslab.grip_mug");
            Mark("tracking loss cancels moving held object");
            foreach (double delay in ReleaseGrip(mug)) yield return delay;
            for (int i = 0; i < 8; i++)
            {
                rightPosition += Vector3.right * .012f;
                rightRotation = Quaternion.AngleAxis(4f, Vector3.up) * rightRotation;
                yield return .03d;
            }
            int cancelStart = ReleaseEvents().Count;
            simTracking = false;
            try
            {
                yield return .08d;
                Check(!mug.IsHeld && !Lab.guards[1].InteractionsAllowed, "tracking loss cancels selection and blocks input");
                Check(new Vector2(mug.body.linearVelocity.x, mug.body.linearVelocity.z).magnitude < .01f && mug.body.angularVelocity.magnitude < .01f, "tracking loss does not launch or spin object");
                var canceled = ReleaseEvents().Skip(cancelStart).Where(e => (string)e["target"] == mug.stableId).ToArray();
                Check(canceled.Any(e => (string)e["event"] == "SYSTEM_RELEASE" && (string)e["source"] == "System") && !canceled.Any(e => (string)e["event"] == "RELEASE"), "tracking loss stays SYSTEM_RELEASE");
            }
            finally { simTracking = true; rightGrip = 0; }
            yield return 1d;

            Mark("explicit held reset clears velocity");
            foreach (double delay in ReleaseGrip(mug)) yield return delay;
            rightPosition += Vector3.right * .04f;
            rightRotation = Quaternion.AngleAxis(12f, Vector3.up) * rightRotation;
            yield return .03d;
            int resetStart = ReleaseEvents().Count;
            Lab.ResetObjects();
            Check(Lab.objects.All(o => !o.IsHeld && o.body.linearVelocity == Vector3.zero && o.body.angularVelocity == Vector3.zero), "explicit reset clears selection and both velocities immediately");
            Check(!ReleaseEvents().Skip(resetStart).Any(e => (string)e["event"] == "RELEASE"), "explicit reset does not produce participant RELEASE");
            rightGrip = 0;
            yield return .7d;

            Mark("UI Trigger presses panel without selecting an object");
            rightGrip = rightTrigger = 0;
            rightPosition = new Vector3(.25f, 1.10f, .28f);
            rightRotation = Quaternion.identity;
            yield return .5d;
            int presses = PressCount(Lab.Logger.DirectoryPath);
            int grabs = ReleaseEvents().Count(e => (string)e["event"] == "GRAB");
            foreach (double delay in ClickInput(1, Lab.panel.TestButton)) yield return delay;
            Check(PressCount(Lab.Logger.DirectoryPath) == presses + 1 && Lab.objects.All(o => !o.IsHeld) && ReleaseEvents().Count(e => (string)e["event"] == "GRAB") == grabs, "UI PRESS remains available without accidental object grab");
            Lab.ResetObjects();
            Mark("release physics simulator checks completed; real Quest and Hand Tracking unverified");
            inputSequenceComplete = true;
        }

        static void CheckReleaseLog(HandsLabObject item, int start, Vector3 velocity, Vector3 angular)
        {
            var events = ReleaseEvents().Skip(start).Where(e => (string)e["target"] == item.stableId).ToArray();
            var releases = events.Where(e => (string)e["event"] == "RELEASE").ToArray();
            Check(releases.Length == 1 && !events.Any(e => (string)e["event"] == "SYSTEM_RELEASE"), item.stableId + " participant release is not system cleanup");
            var release = releases.Single();
            Check((string)release["source"] == "Tester" && (string)release["input_source"] == "SimulatedController" && (bool?)release["tracking_valid"] == true, item.stableId + " release keeps simulated Tester attribution");
            var after = release["after"];
            Check(after?["angular_velocity"] != null && Vector3.Distance(ReleaseVector(after["velocity"]), velocity) < .0001f && Vector3.Distance(ReleaseVector(after["angular_velocity"]), angular) < .0001f, item.stableId + " raw release snapshot contains SDK linear and angular velocity");
            inputEvidence.Add(new { kind = "RELEASE_PHYSICS", target = item.stableId, release });
        }
    }
}
#endif
