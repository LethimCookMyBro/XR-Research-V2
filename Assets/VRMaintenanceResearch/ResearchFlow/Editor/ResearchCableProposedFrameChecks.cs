#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Candidate diagnostic. Root may compile this in Editor; it never saves or reopens the scene.
    public static class ResearchCableProposedFrameChecks
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo[] SpanFields = typeof(S1CableSpan).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        sealed class Query
        {
            public string name;
            public Transform moving;
            public Matrix4x4 pose;
        }

        static bool Moves(Transform target, Transform moving) => moving && (target == moving || target.IsChildOf(moving));
        static Matrix4x4 Pose(Transform target, Query query) => Moves(target, query.moving)
            ? query.pose * query.moving.worldToLocalMatrix * target.localToWorldMatrix : target.localToWorldMatrix;

        static S1CableSpan Cold(S1CableSpan source)
        {
            var copy = new S1CableSpan();
            foreach (var field in SpanFields) field.SetValue(copy, field.GetValue(source));
            copy.cached = false;
            copy.cachedPath = null;
            copy.cachedSide = Vector3.zero;
            copy.cachedPreferred = Vector3.zero;
            return copy;
        }

        static Vector3[] Solve(ResearchS1Cable cable, S1CableSpan source, Query query)
        {
            var span = Cold(source);
            var a = Pose(source.from, query);
            var b = Pose(source.to, query);
            var values = new List<object> { span, a.MultiplyPoint3x4(source.fromExit), b.MultiplyPoint3x4(source.toExit),
                a.MultiplyVector(source.fromDirection), b.MultiplyVector(source.toDirection) };
            var method = typeof(ResearchS1Cable).GetMethods(PrivateInstance)
                .Single(m => m.Name == "Path" && m.GetParameters().Length >= 5);
            if (method.GetParameters().Length == 6) values.Add(Pose(cable.transform, query));
            return (Vector3[])method.Invoke(cable, values.ToArray());
        }

        static float Difference(Vector3[] left, Vector3[] right)
        {
            if (left == null || right == null) return left == right ? 0 : float.PositiveInfinity;
            if (left.Length != right.Length) return float.PositiveInfinity;
            return left.Select((p, i) => Vector3.Distance(p, right[i])).DefaultIfEmpty(0).Max();
        }

        static double Radius(Vector3[] points)
        {
            double radius = double.PositiveInfinity;
            for (int i = 1; i < points.Length - 1; i++)
            {
                var a = points[i] - points[i - 1];
                var b = points[i + 1] - points[i];
                double cross = Vector3.Cross(a, b).magnitude;
                if (cross > 1e-12) radius = Math.Min(radius, a.magnitude * b.magnitude * (a + b).magnitude / (2 * cross));
            }
            return radius;
        }

        public static string Run(string reportPath)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty)
                throw new InvalidOperationException("A clean edit-mode scene is required; no save or reopen is performed.");
            var task = UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include);
            if (!task || !task.wiring || !task.servicePivot) throw new InvalidOperationException("Authored Task A required.");
            var cables = task.wiring.cables;
            var spans = cables.SelectMany(c => c.spans).ToArray();
            var savedFields = spans.ToDictionary(s => s, s => SpanFields.Select(f => f.GetValue(s)).ToArray());
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var savedPoses = transforms.ToDictionary(t => t, t => t.localToWorldMatrix);
            var report = new List<string>();
            int failures = 0;
            GameObject shadowObject = null;
            var previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var pivot = task.servicePivot;
                var queries = new List<Query> { new Query { name = "CURRENT", pose = Matrix4x4.identity } };
                foreach (float angle in new[] { -90f, 90f, 180f })
                    queries.Add(new Query { name = "SERVICE_YAW_" + angle, moving = pivot,
                        pose = Matrix4x4.TRS(pivot.position, Quaternion.AngleAxis(angle, Vector3.up) * pivot.rotation, pivot.lossyScale) });
                queries.Add(new Query { name = "SERVICE_TRANSLATION", moving = pivot,
                    pose = Matrix4x4.TRS(pivot.position + new Vector3(.013f, 0, .017f), pivot.rotation, pivot.lossyScale) });
                foreach (var end in task.wiring.ends)
                    foreach (var offset in new[] { new Vector3(.01f, 0, 0), new Vector3(0, .01f, 0) })
                        queries.Add(new Query { name = "END_ONLY_" + end.plug.stableId + "_" + offset,
                            moving = end.plug.transform,
                            pose = Matrix4x4.TRS(end.plug.transform.position + offset, end.plug.transform.rotation, end.plug.transform.lossyScale) });

                shadowObject = new GameObject("CableFrameProbeShadow") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(shadowObject, previewScene);
                var shadow = shadowObject.AddComponent<ResearchS1Cable>();
                foreach (var query in queries)
                {
                    report.Add(query.name + " wiringAllows=" + task.wiring.Allows(query.moving, query.pose));
                    foreach (var cable in cables)
                    {
                        report.Add(query.name + " " + cable.assetId + " allows=" + cable.Allows(query.moving, query.pose));
                        var route = Pose(cable.transform, query);
                        shadow.transform.SetPositionAndRotation(route.GetColumn(3), route.rotation);
                        shadow.transform.localScale = route.lossyScale;
                        shadow.preferredBow = cable.preferredBow;
                        shadow.sideways = cable.sideways;
                        foreach (var span in cable.spans)
                        {
                            // Query uses the original cable component. Oracle uses a disposable component already in the proposed frame.
                            bool carried = cable.spans.Any(s => s.from == span.to.parent || s.to == span.to.parent);
                            var rigidMethod = typeof(ResearchS1Cable).GetMethod("Rigid", BindingFlags.Static | BindingFlags.NonPublic,
                                null, new[] { typeof(S1CableSpan), typeof(Transform), typeof(Matrix4x4) }, null);
                            bool rigid = (bool)rigidMethod.Invoke(null, new object[] { span, query.moving, query.pose });
                            var actual = Solve(cable, span, query);
                            if (!carried && !rigid && actual == null) failures++;
                            var expected = Solve(shadow, span, query);
                            float error = Difference(actual, expected);
                            bool parity = error <= .0002f;
                            if (!parity) failures++;
                            var a = Pose(span.from, query).MultiplyPoint3x4(span.fromExit);
                            var b = Pose(span.to, query).MultiplyPoint3x4(span.toExit);
                            float endpointError = actual == null ? float.NaN : Mathf.Max(Vector3.Distance(actual[0], a), Vector3.Distance(actual[actual.Length - 1], b));
                            if (actual != null && endpointError > .0001f) failures++;
                            bool guided = span.routeGuides != null && span.routeGuides.Length > 0;
                            if (query.name == "CURRENT" && guided && actual == null) failures++;
                            double lengthError = actual == null ? double.NaN : Math.Abs(ResearchS1CablePath.Length(actual) - span.length);
                            double radius = actual == null ? double.NaN : Radius(actual);
                            if (actual != null && (lengthError > .0002 || radius < span.minimumRadius - .0001)) failures++;
                            var baseline = Solve(cable, span, queries[0]);
                            float invariantError = float.NaN;
                            if (query.moving == pivot && Moves(cable.transform, pivot) && Moves(span.from, pivot) && Moves(span.to, pivot) && baseline != null)
                            {
                                var delta = query.pose * pivot.worldToLocalMatrix;
                                invariantError = Difference(actual, baseline.Select(delta.MultiplyPoint3x4).ToArray());
                            }
                            report.Add(query.name + " " + cable.assetId + "/" + span.from.name + " carried=" + carried + " rigid=" + rigid + " directPathRequired=" + (!carried && !rigid) + " configuredGuides=" + guided +
                                " currentFeasible=" + (baseline != null) + " proposedFeasible=" + (actual != null) +
                                " sameFrameOracleFeasible=" + (expected != null) + " parity=" + parity +
                                " maxPathError=" + error.ToString("G9") + " endpointError=" + endpointError.ToString("G9") +
                                " rigidInvariantError=" + invariantError.ToString("G9") +
                                " lengthError=" + lengthError.ToString("G9") + " radius=" + radius.ToString("G9") +
                                " length=" + (actual == null ? "null" : ResearchS1CablePath.Length(actual).ToString("G9")));
                        }
                    }
                }
                var faultCable = cables.First(c => c.spans.Any(s => s.routeGuides != null && s.routeGuides.Length > 0));
                var faultSpan = faultCable.spans.First(s => s.routeGuides != null && s.routeGuides.Length > 0);
                var unrelated = task.ram[0].transform;
                if (Moves(faultCable.transform, unrelated) || Moves(faultSpan.from, unrelated) || Moves(faultSpan.to, unrelated))
                    throw new InvalidOperationException("The selected fault span is related to the RAM fixture.");
                faultSpan.length = .00001f;
                foreach (var span in spans) span.cached = false;
                bool fullValidationRejects = !faultCable.Allows(null, Matrix4x4.identity);
                bool unrelatedAllowed = task.wiring.Allows(unrelated, unrelated.localToWorldMatrix);
                report.Add("UNRELATED_FAULT fullValidationRejects=" + fullValidationRejects + " unrelatedAllowed=" + unrelatedAllowed);
                if (!fullValidationRejects || !unrelatedAllowed) failures++;
            }
            finally
            {
                if (shadowObject) UnityEngine.Object.DestroyImmediate(shadowObject);
                EditorSceneManager.ClosePreviewScene(previewScene);
                foreach (var item in savedFields)
                    for (int i = 0; i < SpanFields.Length; i++) SpanFields[i].SetValue(item.Key, item.Value[i]);
                bool unchanged = !scene.isDirty && savedPoses.All(p => p.Key && p.Key.localToWorldMatrix == p.Value);
                report.Add("sceneUnchanged=" + unchanged);
                if (!unchanged) failures++;
                report.Add("failures=" + failures + " tier=EDITOR_SYNTHETIC_NOT_NATIVE");
                File.WriteAllLines(reportPath, report);
            }
            return "CABLE_PROPOSED_FRAME_" + (failures == 0 ? "PASS" : "FAIL") + " failures=" + failures;
        }
    }
}
#endif
