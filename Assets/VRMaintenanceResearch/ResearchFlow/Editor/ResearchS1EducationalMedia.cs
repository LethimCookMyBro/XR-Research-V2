#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRMaintenanceResearch.AssetLibraryQA;
using VRMaintenanceResearch.HandsLabQuest3.Logging;
using Plan = VRMaintenanceResearch.ResearchFlow.Editor.ResearchS1EducationalPlan;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchS1EducationalMedia
    {
        sealed class PoseStamp
        {
            internal Transform Source, Parent; internal Vector3 Position, Scale; internal Quaternion Rotation;
            internal bool Active;
            internal PoseStamp(Transform source) { Source = source; Parent = source.parent; Position = source.localPosition; Rotation = source.localRotation; Scale = source.localScale; Active = source.gameObject.activeSelf; }
            internal bool Unchanged => Source && Source.parent == Parent && Source.localPosition == Position && Source.localRotation == Rotation && Source.localScale == Scale && Source.gameObject.activeSelf == Active;
        }
        sealed class Fixture
        {
            internal ResearchSessionFlowController Controller; internal ResearchS1TaskAMechanics A; internal ResearchS1TaskBMechanics B;
            internal Camera Camera; internal Vector3 CameraPosition; internal Quaternion CameraRotation, PageRotation; internal float Fov; internal int Culling, OwnedCulling, OwnedLayer, Display, Width, Height;
            internal GameObject Owned; internal Transform Model; internal Coroutine Routine; internal Plan.Contract Contract;
            internal List<Plan.Page> Pages; internal Plan.Page Page; internal TMP_Text Header, Caption, Label, Badge;
            internal string Output, Session, Run, Task, Language, Status = "PREPARING", Error, TableSha, Mechanism;
            internal int PageIndex = -1, ObservedFrames, ObservedPages, SectionsEnded; internal double Began, PageStarted, Seconds;
            internal bool Restored, RecordingStarted, Stopping, CullingOwned; internal readonly Dictionary<Transform, Transform> Clones = new Dictionary<Transform, Transform>();
            internal readonly Dictionary<Renderer, bool> Renderers = new Dictionary<Renderer, bool>(); internal readonly Dictionary<Canvas, bool> Canvases = new Dictionary<Canvas, bool>();
            internal readonly List<PoseStamp> Sources = new List<PoseStamp>(), ClonePoses = new List<PoseStamp>();
            internal readonly List<Renderer> Visible = new List<Renderer>(); internal readonly Dictionary<Renderer, Renderer> Visuals = new Dictionary<Renderer, Renderer>();
            internal readonly List<ResearchS1Cable> Cables = new List<ResearchS1Cable>(); internal readonly List<UnityEngine.Object> GeneratedMeshes = new List<UnityEngine.Object>();
            internal readonly Dictionary<TMP_FontAsset, TMP_FontAsset> Fonts = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
            internal readonly Dictionary<string, object> Assets = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<TMP_Text> Markers = new List<TMP_Text>(); internal readonly List<Transform> MarkerAnchors = new List<Transform>();
            internal readonly List<Transform> MotionRoots = new List<Transform>();
        }
        static Fixture current;
        public static bool Running => current != null && !current.Stopping;

        public static string Begin(string evidenceDirectory, string expectedSessionId, string expectedRunId, string captionTablePath,
            string expectedTableSha256, string taskId, string language)
        {
            if (current != null && (!current.Stopping || !current.Restored)) throw new InvalidOperationException("Previous educational fixture still owns resources.");
            if (ResearchS1GameViewVideo.Running) throw new InvalidOperationException("Prepare the educational fixture before starting an independently owned Game View recording.");
            var fixture = new Fixture { Controller = UnityEngine.Object.FindObjectsByType<ResearchSessionFlowController>(FindObjectsSortMode.None).Single(),
                Session = expectedSessionId, Run = expectedRunId, Task = taskId, Language = language, TableSha = expectedTableSha256 };
            Context(fixture);
            if ((taskId != "A" && taskId != "B") || (language != "th" && language != "ja")) throw new ArgumentException("Use task A/B and language th/ja.");
            fixture.Contract = Plan.Load(captionTablePath, expectedTableSha256);
            fixture.Camera = fixture.Controller.origin ? fixture.Controller.origin.Camera : null;
            if (!fixture.Camera || !fixture.Camera.enabled || Screen.width < 1280 || Screen.height < 720 || Screen.width > 1920 || Screen.height > 1080)
                throw new InvalidOperationException("Use the existing enabled research camera and a real 1280..1920 by 720..1080 Game View. This fixture does not resize or create a camera.");
            fixture.CameraPosition = fixture.Camera.transform.position; fixture.CameraRotation = fixture.Camera.transform.rotation;
            fixture.Fov = fixture.Camera.fieldOfView; fixture.Culling = fixture.Camera.cullingMask; fixture.Display = fixture.Camera.targetDisplay; fixture.Width = Screen.width; fixture.Height = Screen.height;
            fixture.A = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            fixture.B = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            if (!fixture.A.AuthoredBindingsValid() || !fixture.B.Capture().systemIntegrityValid) throw new InvalidOperationException("The existing A/B authored model graph is incomplete. No substitute models.");
            BoundModels(fixture);
            fixture.Output = Fresh(evidenceDirectory); fixture.Began = EditorApplication.timeSinceStartup; current = fixture;
            try
            {
                fixture.OwnedLayer = IsolationLayer(); fixture.OwnedCulling = 1 << fixture.OwnedLayer;
                foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>()) fixture.Renderers.Add(renderer, renderer.enabled);
                foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => !EditorUtility.IsPersistent(c))) fixture.Canvases.Add(canvas, canvas.enabled);
                fixture.Owned = new GameObject("S1 Educational Model Acquisition") { hideFlags = HideFlags.HideAndDontSave }; fixture.Owned.SetActive(false);
                Transform source = taskId == "A" ? fixture.A.transform : fixture.B.transform;
                fixture.Model = Copy(fixture, source, fixture.Owned.transform);
                fixture.Model.SetPositionAndRotation(source.position, source.rotation); fixture.Model.localScale = source.lossyScale;
                CopySkins(fixture);
                if (taskId == "A") CopyCables(fixture);
                foreach (var original in fixture.Clones.Keys) fixture.Sources.Add(new PoseStamp(original));
                foreach (var clone in fixture.Clones.Values) fixture.ClonePoses.Add(new PoseStamp(clone));
                Captions(fixture);
                var lightRoot = new GameObject("Owned capture key") { hideFlags = HideFlags.HideAndDontSave }; lightRoot.transform.SetParent(fixture.Owned.transform, false);
                lightRoot.transform.rotation = fixture.CameraRotation * Quaternion.Euler(20, -20, 0);
                var captureLight = lightRoot.AddComponent<Light>(); captureLight.type = LightType.Directional; captureLight.intensity = 1.5f; captureLight.cullingMask = fixture.OwnedCulling; captureLight.renderingLayerMask = 1; captureLight.shadows = LightShadows.None;
                foreach (var transform in fixture.Owned.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = fixture.OwnedLayer;
                fixture.CullingOwned = true; fixture.Camera.cullingMask = fixture.OwnedCulling;
                foreach (var canvas in fixture.Canvases.Keys) canvas.enabled = false;
                fixture.Owned.SetActive(true);
                VisualIsolation(fixture); Canvas.ForceUpdateCanvases();
                fixture.Pages = Plan.Pages(fixture.Contract, taskId, language, text => Fits(fixture, text));
                double duration = fixture.Pages.Sum(p => PageSeconds(p));
                if (duration > 1100) throw new InvalidOperationException("Readable caption pages exceed the bounded 1100-second acquisition. Review page layout instead of truncating facts.");
                Write(fixture, "fixture.json", new { schema = "s1.educational-fixture.1", status = "READY", configuration_kind = "SIMULATION_EXAMPLE",
                    capture_scope = "EDUCATIONAL_MODEL_CONFIGURATION_NOT_RUNTIME_INTERACTION_OR_E2E", actual_frames = 0,
                    output_role = "RAW_MODEL_ONLY", captions_in_metadata = true, participant_media_ready = false,
                    capture_cable_route = taskId == "A" ? new { owned_only = true, asset_id = "CABLE_SATA_DATA_S_500", applicable_section = "A.ssd", local_z_rotation_after_page_reset_degrees = 180 } : null,
                    session_id = fixture.Session, run_id = fixture.Run, task_id = taskId, language, caption_table_path = captionTablePath,
                    caption_table_sha256 = expectedTableSha256, content_sha256 = Plan.ContentSha, source_models = fixture.Contract.source_models,
                    observed_source_assets = fixture.Assets.Values.ToArray(), source_object_paths = fixture.Sources.Select(s => ObjectPath(s.Source)).ToArray(),
                    camera = CameraRecord(fixture), source_scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
                    capture_light = new { owned = true, type = captureLight.type.ToString(), intensity = captureLight.intensity, culling_mask = captureLight.cullingMask, rendering_layer_mask = captureLight.renderingLayerMask, shadows = captureLight.shadows.ToString(),
                        rotation = new[] { lightRoot.transform.rotation.x, lightRoot.transform.rotation.y, lightRoot.transform.rotation.z, lightRoot.transform.rotation.w } },
                    nominal_caption_size_at_720p = 44, planned_pages = fixture.Pages.Count, planned_seconds = duration, audio = "not_recorded",
                    model_motion = "Scripted movement of owned visual clones. No interaction or completion claim.",
                    restore_fields = new[] { "camera.cullingMask", "canvas.enabled", "owned objects/fonts/atlases/materials/meshes/lights", "coroutine", "Editor callbacks" },
                    camera_original_culling_mask = fixture.Culling, owned_visual_layer = fixture.OwnedLayer, owned_culling_mask = fixture.OwnedCulling,
                    fields_never_written = new[] { "source transforms", "source renderer.enabled", "camera pose/FOV", "input", "tracking", "flow", "task state", "logger", "approval", "Time.timeScale" } });
                PreparePage(fixture, 0); fixture.Status = "READY_WAITING_FOR_REAL_GAMEVIEW_RECORDING";
                EditorApplication.update += Watch; EditorApplication.playModeStateChanged += PlayState; AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                fixture.Routine = fixture.Controller.StartCoroutine(Pages(fixture));
                return Audit();
            }
            catch (Exception error) { fixture.Error = error.ToString(); Finish(fixture, "start_failed"); throw; }
        }

        static void Context(Fixture fixture)
        {
            var controller = fixture.Controller;
            if (!Application.isEditor || !EditorApplication.isPlaying || EditorApplication.isPaused || !ResearchFlowVisualInput.Running ||
                !controller || !controller.isActiveAndEnabled || controller.Record == null || !controller.Record.test_data || controller.Record.session_state != RegistryState.InProgress ||
                string.IsNullOrEmpty(controller.Record.participant_id) || !controller.Record.participant_id.StartsWith("DEV", StringComparison.Ordinal) ||
                controller.Flow == null || controller.Flow.State != ResearchFlowState.TutorialWelcome || controller.Flow.Paused ||
                controller.Logger == null || controller.Logger.State != HandsLabLogState.Active || string.IsNullOrEmpty(fixture.Session) || string.IsNullOrEmpty(fixture.Run) ||
                fixture.Session != controller.Record.session_id || fixture.Run != controller.Record.run_id || fixture.Run != controller.Logger.RunId ||
                controller.Record.language != (fixture.Language == "ja" ? ParticipantLanguage.Japanese : ParticipantLanguage.Thai))
                throw new InvalidOperationException("Educational acquisition requires an unpaused Editor TutorialWelcome DEV test-data InProgress session, Active logger, running simulator and exact session/run/logger/language identity.");
        }
        static string Fresh(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new ArgumentException("Use an absolute fresh external evidence directory.");
            string full = Path.GetFullPath(path); string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetPathRoot(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use a fully-qualified fresh external evidence directory, not a drive-relative or current-drive rooted path.");
            if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.Equals(project, StringComparison.OrdinalIgnoreCase) || full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Evidence must stay local and outside the Unity project.");
            if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Evidence path exists. No overwrite.");
            var parent = new DirectoryInfo(Path.GetDirectoryName(full)); if (!parent.Exists) throw new DirectoryNotFoundException("The external parent must already exist.");
            for (var ancestor = parent; ancestor != null; ancestor = ancestor.Parent) if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Evidence cannot traverse a reparse point.");
            Directory.CreateDirectory(full); return full;
        }
        static string ObjectPath(Transform value) => value.parent ? ObjectPath(value.parent) + "/" + value.name : value.name;
        static void BoundModels(Fixture fixture)
        {
            var imported = new HashSet<string>(fixture.Contract.source_models.SelectMany(m => new[] { m.imported_model_prefab, m.owned_model_prefab }).Where(f => f != null).Select(f => Path.GetFullPath(f.path)), StringComparer.OrdinalIgnoreCase);
            Transform[] roots = fixture.Task == "A" ? fixture.A.fixedAssembly.Concat(fixture.A.ram.Concat(fixture.A.ssds).Concat(fixture.A.gpus).Select(p => p.transform)).ToArray() :
                fixture.B.fixedAssembly.Concat(new[] { fixture.B.frontGuard.transform, fixture.B.blade.transform, fixture.B.spinner.transform, fixture.B.rearGuard.transform, fixture.B.rearLock.transform }).ToArray();
            foreach (var root in roots)
            {
                string path = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).Select(f => AssetDatabase.GetAssetPath(f.sharedMesh)).FirstOrDefault(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase));
                if (path == null || !imported.Contains(Path.GetFullPath(path))) throw new InvalidOperationException("Actual model binding is outside the frozen source contract: " + ObjectPath(root) + " / " + path);
            }
        }
        static void Asset(Fixture fixture, UnityEngine.Object asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;
            string absolute = Path.GetFullPath(path);
            if (!File.Exists(absolute))
            {
                if (path == "Library/unity default resources" || path == "Resources/unity_builtin_extra") return;
                throw new FileNotFoundException("Observed source asset has no file identity.", absolute);
            }
            if (!fixture.Assets.ContainsKey(absolute)) fixture.Assets.Add(absolute, new { path = absolute, sha256 = Plan.FileSha(absolute), bytes = new FileInfo(absolute).Length });
        }
        static Transform Copy(Fixture fixture, Transform source, Transform parent)
        {
            var clone = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer }.transform;
            clone.SetParent(parent, false); clone.localPosition = source.localPosition; clone.localRotation = source.localRotation; clone.localScale = source.localScale;
            clone.gameObject.SetActive(true); fixture.Clones.Add(source, clone);
            var filter = source.GetComponent<MeshFilter>(); var original = source.GetComponent<MeshRenderer>();
            if (filter && original && filter.sharedMesh)
            {
                var mesh = filter.sharedMesh;
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh))) { mesh = UnityEngine.Object.Instantiate(mesh); fixture.GeneratedMeshes.Add(mesh); mesh.hideFlags = HideFlags.HideAndDontSave; }
                clone.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = clone.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterials = original.sharedMaterials;
                renderer.enabled = fixture.Renderers[original]; renderer.shadowCastingMode = original.shadowCastingMode; renderer.receiveShadows = original.receiveShadows;
                fixture.Visuals.Add(original, renderer); Asset(fixture, filter.sharedMesh); foreach (var material in original.sharedMaterials) if (material) Asset(fixture, material);
            }
            foreach (Transform child in source) Copy(fixture, child, clone);
            return clone;
        }
        static Transform Clone(Fixture fixture, Transform source)
        {
            if (!source || !fixture.Clones.TryGetValue(source, out var clone)) throw new InvalidOperationException("Missing owned clone binding for " + (source ? ObjectPath(source) : "null"));
            return clone;
        }
        static void CopySkins(Fixture fixture)
        {
            foreach (var pair in fixture.Clones)
            {
                var original = pair.Key.GetComponent<SkinnedMeshRenderer>(); if (!original) continue;
                if (!original.sharedMesh) throw new InvalidOperationException("Source skinned mesh absent at " + ObjectPath(pair.Key));
                var renderer = pair.Value.gameObject.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = original.sharedMesh; renderer.sharedMaterials = original.sharedMaterials;
                renderer.bones = original.bones.Select(b => Clone(fixture, b)).ToArray(); renderer.rootBone = original.rootBone ? Clone(fixture, original.rootBone) : null;
                renderer.localBounds = original.localBounds; renderer.quality = original.quality; renderer.updateWhenOffscreen = true;
                renderer.enabled = fixture.Renderers[original]; fixture.Visuals.Add(original, renderer);
                Asset(fixture, original.sharedMesh); foreach (var material in original.sharedMaterials) if (material) Asset(fixture, material);
            }
        }
        static void CopyCables(Fixture fixture)
        {
            foreach (var original in fixture.A.wiring.cables)
            {
                if (!original.AuthoredValid()) throw new InvalidOperationException("Source cable binding invalid: " + original.assetId);
                var cable = Clone(fixture, original.transform).gameObject.AddComponent<ResearchS1Cable>(); cable.enabled = false; cable.assetId = original.assetId;
                cable.skin = (SkinnedMeshRenderer)fixture.Visuals[original.skin];
                cable.spans = original.spans.Select(s => new S1CableSpan { from = Clone(fixture, s.from), to = Clone(fixture, s.to),
                    fromExit = s.fromExit, toExit = s.toExit, fromDirection = s.fromDirection, toDirection = s.toDirection,
                    bones = s.bones.Select(b => Clone(fixture, b)).ToArray(), restPositions = (Vector3[])s.restPositions.Clone(),
                    restRotations = (Quaternion[])s.restRotations.Clone(), restDirections = (Vector3[])s.restDirections.Clone(),
                    restToPosition = s.restToPosition, restToRotation = s.restToRotation, restFromRotation = s.restFromRotation,
                    length = s.length, minimumRadius = s.minimumRadius }).ToArray();
                if (!cable.AuthoredValid()) throw new InvalidOperationException("Owned educational cable graph invalid: " + cable.assetId);
                fixture.Cables.Add(cable);
            }
        }
        static TMP_FontAsset Font(Fixture fixture, TMP_FontAsset source)
        {
            if (!source) throw new InvalidOperationException("An existing authored font is missing.");
            if (fixture.Fonts.TryGetValue(source, out var prior)) return prior;
            Asset(fixture, source);
            var clone = UnityEngine.Object.Instantiate(source); clone.atlasTextures = null; clone.material = null; fixture.Fonts.Add(source, clone); clone.hideFlags = HideFlags.HideAndDontSave;
            const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(TMP_FontAsset).GetField("m_AtlasTexture", hidden).SetValue(clone, null);
            clone.atlasTextures = source.atlasTextures.Select(t => t ? UnityEngine.Object.Instantiate(t) : null).ToArray();
            foreach (var texture in clone.atlasTextures) if (texture) texture.hideFlags = HideFlags.HideAndDontSave;
            if (source.material)
            {
                clone.material = UnityEngine.Object.Instantiate(source.material); clone.material.hideFlags = HideFlags.HideAndDontSave;
                int atlas = Array.IndexOf(source.atlasTextures, source.material.mainTexture); clone.material.mainTexture = clone.atlasTextures[atlas < 0 ? 0 : atlas];
            }
            clone.fallbackFontAssetTable = source.fallbackFontAssetTable == null ? new List<TMP_FontAsset>() : source.fallbackFontAssetTable.Select(f => Font(fixture, f)).ToList();
            var weights = (TMP_FontWeightPair[])source.fontWeightTable.Clone(); typeof(TMP_FontAsset).GetField("m_FontWeightTable", hidden).SetValue(clone, weights);
            for (int i = 0; i < weights.Length; i++) { if (weights[i].regularTypeface) weights[i].regularTypeface = Font(fixture, weights[i].regularTypeface); if (weights[i].italicTypeface) weights[i].italicTypeface = Font(fixture, weights[i].italicTypeface); }
            if (ReferenceEquals(source.glyphTable, clone.glyphTable) || ReferenceEquals(source.characterTable, clone.characterTable) ||
                source.characterTable.Zip(clone.characterTable, (a, b) => ReferenceEquals(a, b)).Any(shared => shared)) throw new InvalidOperationException("Font clone shares mutable tables.");
            clone.ReadFontAssetDefinition(); return clone;
        }
        static TMP_Text Text(Fixture fixture, Transform parent, string name, Vector2 minimum, Vector2 maximum, TMP_FontAsset font, float size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave }; go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = minimum; rect.anchorMax = maximum; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.AddComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = size; text.enableAutoSizing = false; text.richText = false;
            text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow; text.alignment = TextAlignmentOptions.TopLeft; text.color = ResearchUITheme.Ink; text.raycastTarget = false;
            return text;
        }
        static void Captions(Fixture fixture)
        {
            var canvasRoot = new GameObject("Educational captions", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave }; canvasRoot.transform.SetParent(fixture.Owned.transform, false);
            var canvas = canvasRoot.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000; canvas.enabled = false;
            var scaler = canvasRoot.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1;
            ((RectTransform)canvasRoot.transform).sizeDelta = new Vector2(scaler.referenceResolution.y * Screen.width / Screen.height, scaler.referenceResolution.y);
            var panel = new GameObject("Caption surface", typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave }; panel.transform.SetParent(canvasRoot.transform, false);
            var rect = (RectTransform)panel.transform; rect.anchorMin = new Vector2(.52f, .04f); rect.anchorMax = new Vector2(.99f, .97f); rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = panel.AddComponent<Image>(); image.color = ResearchUITheme.Glass; image.raycastTarget = false;
            TMP_FontAsset local = Font(fixture, fixture.Language == "th" ? fixture.Controller.view.thaiFont : fixture.Controller.view.japaneseFont);
            TMP_FontAsset english = Font(fixture, fixture.Controller.view.englishFont);
            fixture.Header = Text(fixture, canvasRoot.transform, "Section heading", new Vector2(.545f, .78f), new Vector2(.97f, .95f), local, 34);
            fixture.Caption = Text(fixture, canvasRoot.transform, "Literal fact", new Vector2(.545f, .13f), new Vector2(.97f, .755f), local, 44);
            fixture.Label = Text(fixture, canvasRoot.transform, "Example motion label", new Vector2(.03f, .81f), new Vector2(.495f, .95f), english, 28);
            fixture.Badge = Text(fixture, canvasRoot.transform, "Technical provenance", new Vector2(.03f, .02f), new Vector2(.97f, .105f), english, 24);
            fixture.Badge.text = "SIMULATION_EXAMPLE | SCRIPTED MODEL MOTION | NO E2E | NO AUDIO";
            foreach (var chapter in fixture.Contract.chapters.Where(c => c.task_id == fixture.Task))
                if (!local.HasCharacters(chapter.captions[fixture.Language].text, out uint[] missing, true, true)) throw new InvalidOperationException("Missing authored caption glyphs: " + string.Join(",", missing.Select(u => u.ToString("X"))));
            if (fixture.Task == "A")
                foreach (var slot in fixture.A.ramSlots.Concat(fixture.A.gpuSlots))
                {
                    var marker = Text(fixture, canvasRoot.transform, "Anchor " + slot.seatName, Vector2.zero, Vector2.zero, english, 24);
                    marker.text = slot.seatName.StartsWith("DIMM_", StringComparison.Ordinal) ? slot.seatName.Substring(5) : slot.seatName;
                    marker.rectTransform.sizeDelta = new Vector2(160, 38); marker.gameObject.SetActive(false); fixture.Markers.Add(marker); fixture.MarkerAnchors.Add(Clone(fixture, slot.seat));
                }
        }
        static bool Fits(Fixture fixture, string text) => fixture.Caption.GetPreferredValues(text, fixture.Caption.rectTransform.rect.width, float.PositiveInfinity).y <= fixture.Caption.rectTransform.rect.height - 12;
        static double PageSeconds(Plan.Page page) => Math.Max(12, Math.Min(36, page.Text.Length / 10d + 4));
        static object CameraRecord(Fixture fixture) => new { position = new[] { fixture.Camera.transform.position.x, fixture.Camera.transform.position.y, fixture.Camera.transform.position.z },
            rotation = new[] { fixture.Camera.transform.rotation.x, fixture.Camera.transform.rotation.y, fixture.Camera.transform.rotation.z, fixture.Camera.transform.rotation.w },
            fov_degrees = fixture.Camera.fieldOfView, culling_mask = fixture.Camera.cullingMask, width = Screen.width, height = Screen.height };

        static void Reset(Fixture fixture)
        {
            foreach (var stamp in fixture.ClonePoses) { stamp.Source.localPosition = stamp.Position; stamp.Source.localRotation = stamp.Rotation; stamp.Source.localScale = stamp.Scale; }
            foreach (var marker in fixture.Markers) marker.gameObject.SetActive(false);
            fixture.Visible.Clear();
        }
        static void Show(Fixture fixture, params Transform[] roots)
        {
            foreach (var pair in fixture.Visuals)
            {
                bool visible = fixture.Renderers[pair.Key] && roots.Any(r => pair.Key.transform == r || pair.Key.transform.IsChildOf(r));
                pair.Value.enabled = visible; if (visible) fixture.Visible.Add(pair.Value);
            }
        }
        static void Place(Fixture fixture, Transform root, Transform mate, Transform seat, float depthMm, float turn = 0)
        {
            Transform clone = Clone(fixture, root), clonedMate = Clone(fixture, mate), clonedSeat = Clone(fixture, seat);
            Matrix4x4 pose = SocketRules.RootPose(clonedSeat.localToWorldMatrix, depthMm, turn, clone.worldToLocalMatrix * clonedMate.localToWorldMatrix);
            clone.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }
        static void Fan(Fixture fixture, ResearchS1FanPart part, float depthMm = 0, float turn = 0) => Place(fixture, part.transform, part.mate, part.seat, depthMm, turn);
        static void Thread(Fixture fixture, ResearchS1FanPart part, double progress)
        {
            Fan(fixture, part, (float)ResearchS1FanKinematics.ThreadDepthMm(progress, part.fullTurnDegrees, part.threadTravelMm), (part.rightHanded ? -1 : 1) * (float)progress);
        }
        static void Clip(Fixture fixture, bool closed)
        {
            var clip = closed ? fixture.B.frontClip.clipClose : fixture.B.frontClip.clipOpen;
            if (!clip) throw new InvalidOperationException("The source guard clip animation is missing.");
            Sample(fixture, fixture.B.frontClip.clipAnimationRoot.transform, clip);
        }
        static void Sample(Fixture fixture, Transform source, AnimationClip clip)
        {
            if (!clip) throw new InvalidOperationException("Source latch animation is missing: " + ObjectPath(source));
            var root = Clone(fixture, source); var pose = new PoseStamp(root);
            try { clip.SampleAnimation(root.gameObject, clip.length); }
            finally { root.SetLocalPositionAndRotation(pose.Position, pose.Rotation); root.localScale = pose.Scale; }
            Asset(fixture, clip);
        }
        static void ClosedFan(Fixture fixture)
        {
            Fan(fixture, fixture.B.rearGuard); Thread(fixture, fixture.B.rearLock, fixture.B.rearLock.fullTurnDegrees);
            Fan(fixture, fixture.B.blade); Thread(fixture, fixture.B.spinner, fixture.B.spinner.fullTurnDegrees); Fan(fixture, fixture.B.frontGuard); Clip(fixture, true);
            Clone(fixture, fixture.B.motorShaft).localRotation = fixture.B.isolatedShaftRotation;
        }
        static void Frame(Fixture fixture, Transform focus, Quaternion view, float focusRadius = 0)
        {
            if (fixture.Visible.Count == 0) throw new InvalidOperationException("Section has no visible real model geometry.");
            Vector3 anchor = focus.position; Quaternion delta = fixture.CameraRotation * Quaternion.Inverse(view);
            fixture.Model.rotation = delta * fixture.Model.rotation; Vector3 rotatedAnchor = fixture.Model.position + delta * (anchor - fixture.Model.position);
            var points = new List<Vector3>();
            foreach (var renderer in fixture.Visible)
            {
                var bounds = renderer.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    points.Add(Quaternion.Inverse(fixture.CameraRotation) * (bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z))));
            }
            var box = new Bounds(points[0], Vector3.zero); foreach (var point in points) box.Encapsulate(point);
            float vertical = Mathf.Tan(fixture.Fov * Mathf.Deg2Rad / 2);
            float distance = focusRadius > 0 ? Mathf.Max(.15f, focusRadius / (vertical * .34f)) : Mathf.Max(box.extents.x / (vertical * fixture.Camera.aspect * .78f), box.extents.y / (vertical * .68f)) + box.extents.z + .06f;
            Vector3 center = focusRadius > 0 ? rotatedAnchor : fixture.CameraRotation * box.center;
            Vector3 destination = fixture.Camera.ViewportToWorldPoint(new Vector3(.5f, .5f, distance)); fixture.Model.position += destination - center;
        }
        static void PreparePage(Fixture fixture, int index)
        {
            fixture.PageIndex = index; fixture.Page = fixture.Pages[index]; fixture.Caption.text = fixture.Page.Text;
            fixture.Header.text = fixture.Page.Title + "\n" + fixture.Page.Chapter.section_id + " | " + fixture.Page.Part + "/" + fixture.Page.Parts;
            fixture.Caption.ForceMeshUpdate(); fixture.Header.ForceMeshUpdate();
            if (!Fits(fixture, fixture.Page.Text) || fixture.Caption.isTextOverflowing || fixture.Header.isTextOverflowing) throw new InvalidOperationException("Actual TMP caption or heading is clipped at " + fixture.Page.FactId);
            Reset(fixture); Recipe(fixture); fixture.PageRotation = fixture.Model.rotation; Motion(fixture, 0); fixture.Seconds = PageSeconds(fixture.Page);
        }
        static void Recipe(Fixture fixture)
        {
            string section = fixture.Page.Chapter.section_id; int fact = Array.IndexOf(fixture.Page.Chapter.fact_ids, fixture.Page.FactId);
            fixture.Mechanism = section + "/" + fixture.Page.FactId;
            fixture.MotionRoots.Clear();
            if (fixture.Task == "A")
            {
                var a = fixture.A; Transform board = a.fixedAssembly[1]; Transform front = Clone(fixture, a.ramSlots[0].seat);
                fixture.MotionRoots.Add(Clone(fixture, a.servicePivot));
                foreach (var part in a.ram.Concat(a.ssds).Concat(a.gpus)) fixture.MotionRoots.Add(Clone(fixture, part.transform));
                foreach (var end in a.wiring.ends) fixture.MotionRoots.Add(Clone(fixture, end.plug.transform));
                if (section == "A.overview" || section == "A.requirements" || section == "A.parts")
                {
                    var options = a.ram.Concat(a.ssds).Concat(a.gpus).Select(p => p.transform).ToArray();
                    for (int i = 0; i < options.Length; i++)
                    {
                        var clone = Clone(fixture, options[i]); clone.position = Clone(fixture, board).position + front.right * ((i % 5 - 2) * .24f) + front.up * ((i / 5 == 0 ? .43f : -.42f));
                    }
                    Show(fixture, section == "A.parts" ? options : new[] { a.servicePivot }.Concat(options).ToArray()); Frame(fixture, Clone(fixture, board), front.rotation);
                }
                else if (section == "A.slots")
                {
                    bool rear = fact == 3; Show(fixture, rear ? a.servicePivot : board);
                    Frame(fixture, Clone(fixture, rear ? a.traySeats[0] : board), rear ? Clone(fixture, a.traySeats[0]).rotation : front.rotation);
                    if (!rear) foreach (var marker in fixture.Markers) marker.gameObject.SetActive(true);
                }
                else if (section == "A.ram")
                {
                    var slot = a.ramSlots[fact == 1 ? 3 : 1]; var part = a.ram[fact == 1 ? 1 : 0];
                    Place(fixture, part.transform, part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal)), slot.seat, 35);
                    Show(fixture, board, part.transform); Frame(fixture, Clone(fixture, slot.seat), Clone(fixture, slot.seat).rotation * Quaternion.Euler(20, 0, 0), .105f);
                }
                else if (section == "A.ssd")
                {
                    fixture.Cables.Single(c => c.assetId == "CABLE_SATA_DATA_S_500").transform.localRotation *= Quaternion.Euler(0, 0, 180);
                    var sled = a.sleds[0]; Place(fixture, sled.transform, sled.mate, a.traySeats[0], 40);
                    Show(fixture, a.servicePivot, sled.transform); Frame(fixture, Clone(fixture, a.traySeats[0]), Clone(fixture, a.traySeats[0]).rotation * Quaternion.Euler(18, 0, 0), .19f);
                }
                else if (section == "A.gpu")
                {
                    var part = a.gpus[1]; Place(fixture, part.transform, part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal)), a.gpuSlots[0].seat, 35);
                    Show(fixture, a.servicePivot, part.transform, a.gpuScrews[0].transform);
                    Transform focus = fact == 1 || fact == 2 ? a.wiring.targets.Single(t => t.part == part && t.connector == S1Connector.GpuPcie8).socket.seat : a.gpuSlots[0].seat;
                    Frame(fixture, Clone(fixture, focus), Clone(fixture, focus).rotation * Quaternion.Euler(20, 0, 0), fact == 1 || fact == 2 ? .095f : .23f);
                }
                else if (section == "A.inspect") { Show(fixture, a.servicePivot); Frame(fixture, Clone(fixture, board), front.rotation); }
                else throw new InvalidOperationException("No model recipe for " + section);
            }
            else
            {
                var b = fixture.B; ClosedFan(fixture); Show(fixture, b.transform);
                foreach (var part in new[] { b.frontGuard, b.blade, b.spinner, b.rearGuard, b.rearLock, b.frontClip }) fixture.MotionRoots.Add(Clone(fixture, part.transform));
                if (section == "B.access" || section == "B.correction" || section == "B.reassembly")
                    Frame(fixture, Clone(fixture, b.rearLock.seat), Clone(fixture, b.rearLock.seat).rotation * Quaternion.Euler(0, -12, 0), .26f);
                else if (section == "B.causes" || section == "B.observations" || section == "B.symptom")
                    Frame(fixture, Clone(fixture, b.frontGuard.seat), Clone(fixture, b.frontGuard.seat).rotation, .28f);
                else Frame(fixture, Clone(fixture, b.fixedAssembly[2]), Clone(fixture, b.frontGuard.seat).rotation);
            }
        }

        static void Motion(Fixture fixture, double time)
        {
            string section = fixture.Page.Chapter.section_id; int fact = Array.IndexOf(fixture.Page.Chapter.fact_ids, fixture.Page.FactId);
            float insertion = (float)Plan.Insert(time); fixture.Label.text = fixture.Mechanism + "\nSCRIPTED SIMULATION EXAMPLE";
            if (fixture.Task == "A") MotionA(fixture, section, fact, time, insertion); else MotionB(fixture, section, fact, time, insertion);
            foreach (var cable in fixture.Cables)
            {
                if (!cable.Allows(null, Matrix4x4.identity)) throw new InvalidOperationException("Educational source cable path cannot reach this configuration: " + cable.assetId + " / " + fixture.Page.FactId);
                cable.RefreshShape();
            }
            for (int i = 0; i < fixture.Markers.Count; i++)
            {
                Vector3 screen = fixture.Camera.WorldToScreenPoint(fixture.MarkerAnchors[i].position);
                fixture.Markers[i].rectTransform.position = screen + new Vector3((i % 2 == 0 ? -64 : 10), (i % 2 == 0 ? 12 : -32));
            }
        }
        static void MotionA(Fixture fixture, string section, int fact, double time, float insertion)
        {
            var a = fixture.A;
            if (section == "A.ram")
            {
                var slot = a.ramSlots[fact == 1 ? 3 : 1]; var part = a.ram[fact == 1 ? 1 : 0];
                float depth = (float)Plan.RamDepth(fact, time);
                Place(fixture, part.transform, part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal)), slot.seat, depth);
                var latch = slot.latches.Single(); var closed = depth <= .3f && time < (fact == 1 ? .70 : .65);
                Sample(fixture, latch.animationRoot.transform, closed ? latch.closeClip : latch.openClip);
                fixture.Label.text = slot.seatName + " | single-module mechanical example\n" + (depth > .3f ? "PARTIAL / WITHDRAWN" : closed ? "SEATED / ONE-SIDED LATCH" : "RELEASED / WITHDRAWAL");
            }
            else if (section == "A.ssd")
            {
                var motion = Plan.Ssd(fact, time); var sled = a.sleds[0]; Place(fixture, sled.transform, sled.mate, a.traySeats[0], (float)motion.DepthMm);
                float fastener = (float)motion.Fastener;
                var thumb = sled.thumb; Place(fixture, thumb.transform, thumb.mate, thumb.captiveSeat, thumb.travelMm * (1 - fastener), -thumb.FullDegrees * fastener);
                CableHead(fixture, 0, a.ssds[0], S1Connector.SsdData, (float)motion.DataDepthMm);
                CableHead(fixture, 1, a.ssds[0], S1Connector.SsdPower, (float)motion.PowerDepthMm);
                fixture.Label.text = "REAR TRAY | CAPTIVE FASTENER | TWO SEPARATE HEADS\nSCRIPTED SIMULATION EXAMPLE";
            }
            else if (section == "A.gpu")
            {
                var motion = Plan.Gpu(fact, time); var part = a.gpus[1]; Place(fixture, part.transform, part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal)), a.gpuSlots[0].seat, (float)motion.DepthMm);
                var screw = a.gpuScrews[0]; float progress = (float)motion.Fastener;
                Place(fixture, screw.transform, screw.mate, a.gpuScrewSeats[0], screw.travelMm * (1 - progress), -screw.FullDegrees * progress);
                CableHead(fixture, 2, part, S1Connector.GpuPcie8, (float)motion.PowerDepthMm);
                var latch = a.gpuSlots[0].latches.Single(); bool closed = motion.DepthMm <= .3 && !(fact == 2 && time >= .7);
                Sample(fixture, latch.animationRoot.transform, closed ? latch.closeClip : latch.openClip);
                fixture.Label.text = "MECHANICAL EXAMPLE G02 | PRIMARY SLOT\nBRACKET SEPARATE | READY 8-PIN HEAD";
            }
            else if (section == "A.slots" && fact == 3)
            {
                var pivot = Clone(fixture, a.servicePivot); var stamp = fixture.ClonePoses.Single(s => s.Source == pivot);
                pivot.localRotation = stamp.Rotation * Quaternion.AngleAxis(30 * Mathf.Sin((float)time * Mathf.PI * 2), Vector3.up);
                fixture.Label.text = "REAR TRAY ACCESS | SERVICE HANDLE\nSCRIPTED YAW, NOT CONTROLLER EVIDENCE";
            }
            else if (section == "A.inspect")
            {
                fixture.Model.rotation = fixture.PageRotation * Quaternion.AngleAxis(8 * Mathf.Sin((float)time * Mathf.PI * 2), Vector3.up);
                fixture.Label.text = "INSPECT MEANINGS | REFERENCE EXAMPLES\nNO INSPECT CALL / NO RECORDED RESULT";
            }
            else
            {
                fixture.Model.rotation = fixture.PageRotation * Quaternion.AngleAxis(8 * Mathf.Sin((float)time * Mathf.PI * 2), Vector3.up);
                fixture.Label.text = section + " | ALL OPTIONS SHOWN EQUALLY\nSCRIPTED MODEL VIEW";
            }
        }
        static void CableHead(Fixture fixture, int index, QAPlug part, S1Connector connector, float depth)
        {
            var end = fixture.A.wiring.ends[index]; var target = fixture.A.wiring.targets.Single(t => t.part == part && t.connector == connector);
            Place(fixture, end.plug.transform, end.plug.mates.Single(), target.socket.seat, depth);
        }
        static void MotionB(Fixture fixture, string section, int fact, double time, float insertion)
        {
            var b = fixture.B; ClosedFan(fixture);
            if (section == "B.causes" || section == "B.observations" || section == "B.symptom")
            {
                int area = fact < 3 && section != "B.symptom" ? fact : Math.Min(2, (int)(time * 3));
                double phase = section == "B.symptom" || fact >= 3 ? time * 3 - area : time;
                bool abnormal = phase >= .5; float wobble = abnormal ? .006f * Mathf.Sin((float)phase * Mathf.PI * 12) : 0;
                if (area == 0) { Fan(fixture, b.frontGuard, abnormal ? 4 : 0, abnormal ? b.frontGuard.insertTurnDegrees : 0); Clip(fixture, !abnormal); Clone(fixture, b.frontGuard.transform).position += Clone(fixture, b.frontGuard.seat).right * wobble; }
                if (area == 1) { Fan(fixture, b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(fixture, false); Fan(fixture, b.blade, abnormal ? 6 : 0); Thread(fixture, b.spinner, abnormal ? b.spinner.fullTurnDegrees * .5 : b.spinner.fullTurnDegrees); Clone(fixture, b.blade.transform).position += Clone(fixture, b.blade.seat).right * wobble; }
                if (area == 2) { Thread(fixture, b.rearLock, abnormal ? b.rearLock.fullTurnDegrees * .5 : b.rearLock.fullTurnDegrees); Clone(fixture, b.rearGuard.transform).position += Clone(fixture, b.rearGuard.seat).right * wobble; }
                fixture.Label.text = new[] { "FRONT GUARD", "BLADE MOUNT", "REAR GUARD MOUNT" }[area] + "\n" + (abnormal ? "ABNORMAL EXAMPLE" : "NORMAL EXAMPLE") + " | SIMULATION ROUTINE";
            }
            else if (section == "B.access")
            {
                Clip(fixture, false); Fan(fixture, b.frontGuard, 450 * (float)Plan.Ramp(time, .15, .4), b.frontGuard.insertTurnDegrees * (float)Plan.Ramp(time, 0, .15));
                if (fact >= 1) { Thread(fixture, b.spinner, b.spinner.fullTurnDegrees * (1 - Plan.Ramp(time, .4, .7))); if (time > .7) Fan(fixture, b.spinner, b.spinner.threadTravelMm + 200 * (float)Plan.Ramp(time, .7, .8)); }
                if (fact >= 2) Fan(fixture, b.blade, 250 * (float)Plan.Ramp(time, .8, 1));
                if (fact == 3) { Fan(fixture, b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Fan(fixture, b.spinner, 240); Fan(fixture, b.blade, 250); Thread(fixture, b.rearLock, b.rearLock.fullTurnDegrees * (1 - Plan.Ramp(time, .15, .8))); }
                fixture.Label.text = fact == 3 ? "FRONT VIEW | REAR RING RIGHT-HAND THREAD\nCOUNTERCLOCKWISE LOOSEN / RETAINED WHILE ENGAGED" : fact == 0 ? "FRONT CLIP OPEN | BAYONET ALIGNMENT\nGUARD WITHDRAWAL | MOTOR NEVER ROTATES" : "FRONT VIEW | SPINNER LEFT-HAND THREAD\nCLOCKWISE LOOSEN | MOTOR NEVER ROTATES";
            }
            else if (section == "B.correction")
            {
                if (fact == 0) { Fan(fixture, b.frontGuard, 12 * insertion, b.frontGuard.insertTurnDegrees * insertion); Clip(fixture, time >= .4 && time < .7); }
                else if (fact == 1) { Fan(fixture, b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(fixture, false); BladeAssembly(fixture, Plan.BladeRepair(time)); }
                else if (fact == 2)
                {
                    Fan(fixture, b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(fixture, false); Fan(fixture, b.spinner, 240); Fan(fixture, b.blade, 250);
                    float aligned = (float)(1 - Plan.Ramp(time, .1, .4)); Fan(fixture, b.rearGuard, 20 * aligned, 20 * aligned);
                    if (time < .5) Fan(fixture, b.rearLock, b.rearLock.threadTravelMm + 60 * (float)(1 - Plan.Ramp(time, .4, .5)));
                    else Thread(fixture, b.rearLock, b.rearLock.fullTurnDegrees * Plan.Ramp(time, .5, .8));
                }
                else
                {
                    Fan(fixture, b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(fixture, false);
                    Thread(fixture, b.spinner, b.spinner.fullTurnDegrees * (1 - .5 * Math.Pow(Math.Sin(time * Math.PI), 2)));
                }
                fixture.Label.text = fact < 3 ? "CONDITIONAL REPAIR EXAMPLE " + (fact + 1) + "\nNOT THE CURRENT B-04 FAULT / NO E2E" : "STATE-BASED CHOICE | PAUSE / REVERSE THREAD\nNO ORDERED SOLUTION / NO E2E";
            }
            else if (section == "B.reassembly")
            {
                Clip(fixture, time >= .88); Fan(fixture, b.frontGuard, 450 * (float)(1 - Plan.Ramp(time, .6, .82)), b.frontGuard.insertTurnDegrees * (float)(1 - Plan.Ramp(time, .82, .88)));
                BladeAssembly(fixture, Plan.FanReassembly(time));
                fixture.Label.text = "KEYED SEATING | COUNTERCLOCKWISE SPINNER TIGHTEN\nBAYONET / CLIP CLOSE | MOTOR NEVER ROTATES";
            }
            else
            {
                fixture.Model.rotation = fixture.PageRotation * Quaternion.AngleAxis(8 * Mathf.Sin((float)time * Mathf.PI * 2), Vector3.up);
                fixture.Label.text = section == "B.inspect" ? "INSPECT MEANINGS | REFERENCE EXAMPLES\nNO INSPECT CALL / NO RECORDED RESULT" : "COMPLETE FAN EXTERIOR | NEUTRAL OVERVIEW\nSCRIPTED MODEL EXAMPLE | NO CURRENT FAULT CUE";
            }
        }

        static void BladeAssembly(Fixture fixture, Plan.FanAssemblyMotion motion)
        {
            var b = fixture.B; Fan(fixture, b.blade, (float)motion.BladeDepthMm, (float)motion.BladeTurn);
            if (motion.SpinnerClearanceMm > 0) Fan(fixture, b.spinner, b.spinner.threadTravelMm + (float)motion.SpinnerClearanceMm);
            else Thread(fixture, b.spinner, b.spinner.fullTurnDegrees * motion.SpinnerProgress);
        }

        static IEnumerator Pages(Fixture fixture)
        {
            try
            {
                var end = new WaitForEndOfFrame();
                while (!fixture.Stopping && !ResearchS1GameViewVideo.Running)
                {
                    if (EditorApplication.timeSinceStartup - fixture.Began > 60) { fixture.Error = "No independently owned Game View recording started within 60 seconds."; Finish(fixture, "capture_not_started"); yield break; }
                    yield return end;
                }
                if (fixture.Stopping) yield break;
                if (!fixture.RecordingStarted && EditorApplication.timeSinceStartup - fixture.Began > 60) { fixture.Error = "No independently owned Game View recording started within 60 seconds."; Finish(fixture, "capture_not_started"); yield break; }
                fixture.RecordingStarted = true; fixture.Status = "SCRIPTED_EDUCATIONAL_ACQUISITION_RUNNING";
                for (int index = 0; index < fixture.Pages.Count && !fixture.Stopping; index++)
                {
                    try { PreparePage(fixture, index); fixture.PageStarted = EditorApplication.timeSinceStartup; }
                    catch (Exception error) { fixture.Error = error.ToString(); Finish(fixture, "page_failed"); yield break; }
                    bool first = true;
                    while (!fixture.Stopping && EditorApplication.timeSinceStartup - fixture.PageStarted < fixture.Seconds)
                    {
                        yield return null;
                        try
                        {
                            Context(fixture); if (!ResearchS1GameViewVideo.Running) throw new InvalidOperationException("Real recording stopped before all educational pages were observed.");
                            CameraUnchanged(fixture); VisualIsolation(fixture);
                            double elapsed = EditorApplication.timeSinceStartup - fixture.PageStarted;
                            Motion(fixture, Math.Min(1, elapsed / fixture.Seconds));
                        }
                        catch (Exception error) { fixture.Error = error.ToString(); Finish(fixture, "acquisition_failed"); yield break; }
                        yield return end;
                        try
                        {
                            if (first) { first = false; fixture.PageStarted = EditorApplication.timeSinceStartup; Journal(fixture, "PAGE_VISIBLE", 0); }
                            fixture.ObservedFrames++; Journal(fixture, "MODEL_FRAME_OBSERVED", EditorApplication.timeSinceStartup - fixture.PageStarted);
                        }
                        catch (Exception error) { fixture.Error = error.ToString(); Finish(fixture, "observation_write_failed"); yield break; }
                    }
                    if (fixture.Stopping) yield break;
                    try { Journal(fixture, "PAGE_ENDED", EditorApplication.timeSinceStartup - fixture.PageStarted); fixture.ObservedPages++; }
                    catch (Exception error) { fixture.Error = error.ToString(); Finish(fixture, "page_end_write_failed"); yield break; }
                    if (index == fixture.Pages.Count - 1 || fixture.Pages[index + 1].Chapter.section_id != fixture.Page.Chapter.section_id) fixture.SectionsEnded++;
                }
                Finish(fixture, "all_pages_observed_pending_root_visual_review");
            }
            finally
            {
                if (!fixture.Stopping)
                {
                    fixture.Error = fixture.Error ?? "Educational acquisition routine ended unexpectedly.";
                    Finish(fixture, "routine_finally");
                }
            }
        }
        static void CameraUnchanged(Fixture fixture)
        {
            if (!fixture.Camera || !fixture.Camera.enabled || fixture.Camera.transform.position != fixture.CameraPosition || Quaternion.Angle(fixture.Camera.transform.rotation, fixture.CameraRotation) > .01f ||
                fixture.Camera.fieldOfView != fixture.Fov || fixture.Camera.cullingMask != fixture.OwnedCulling || fixture.Camera.targetDisplay != fixture.Display || fixture.Camera.targetTexture || Screen.width != fixture.Width || Screen.height != fixture.Height)
                throw new InvalidOperationException("Existing camera pose/FOV/culling or Game View dimensions changed during acquisition. Fixture will not overwrite external changes.");
        }
        static int IsolationLayer()
        {
            var objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int layer = 31; layer >= 8; layer--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(layer)) && !objects.Any(o => o.layer == layer)) return layer;
            throw new InvalidOperationException("No unused unnamed visual layer is available. No project layer settings were changed.");
        }
        static void VisualIsolation(Fixture fixture)
        {
            if (!fixture.Owned || !string.IsNullOrEmpty(LayerMask.LayerToName(fixture.OwnedLayer)))
                throw new InvalidOperationException("Owned visual layer reservation changed.");
            foreach (var canvas in fixture.Owned.GetComponentsInChildren<Canvas>(true))
                if (canvas.enabled) throw new InvalidOperationException("Metadata-only measurement canvas was enabled during raw model acquisition.");
            foreach (var renderer in fixture.Renderers.Keys)
                if (!renderer) throw new InvalidOperationException("An original renderer was removed.");
            foreach (var canvas in fixture.Canvases.Keys)
                if (!canvas || canvas.enabled) throw new InvalidOperationException("An original canvas was removed or re-enabled during isolated acquisition: " + (canvas ? ObjectPath(canvas.transform) : "removed"));
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.transform.IsChildOf(fixture.Owned.transform) ? go.layer != fixture.OwnedLayer : go.layer == fixture.OwnedLayer)
                    throw new InvalidOperationException("Owned visual layer was changed or used by a foreign object: " + ObjectPath(go.transform));
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
                if (!EditorUtility.IsPersistent(canvas) && !canvas.transform.IsChildOf(fixture.Owned.transform) && canvas.enabled)
                    throw new InvalidOperationException("A foreign canvas is enabled during isolated acquisition: " + ObjectPath(canvas.transform));
            if (!fixture.Camera || fixture.Camera.targetTexture || Resources.FindObjectsOfTypeAll<Camera>().Any(c => c && !EditorUtility.IsPersistent(c) && c != fixture.Camera && c.isActiveAndEnabled && !c.targetTexture && c.targetDisplay == fixture.Display))
                throw new InvalidOperationException("Another camera can contribute to the acquisition display, or the existing camera has a render target.");
        }
        static void RestoreCulling(Fixture fixture)
        {
            if (!fixture.CullingOwned) return;
            if (!fixture.Camera || fixture.Camera.cullingMask != fixture.OwnedCulling)
                throw new InvalidOperationException("Owned camera culling mask changed externally. Fixture will not overwrite it.");
            fixture.Camera.cullingMask = fixture.Culling; fixture.CullingOwned = false;
        }
        static void Journal(Fixture fixture, string kind, double elapsed)
        {
            File.AppendAllText(Path.Combine(fixture.Output, "pages.jsonl"), JsonConvert.SerializeObject(new { schema = "s1.educational-page-frame.1", kind,
                configuration_kind = "SIMULATION_EXAMPLE", input_provenance = "EDUCATIONAL_MODEL_CONFIGURATION_NOT_RUNTIME_INTERACTION",
                output_role = "RAW_MODEL_ONLY", captions_in_metadata = true, participant_media_ready = false,
                session_id = fixture.Session, run_id = fixture.Run, task_id = fixture.Task, language = fixture.Language,
                section_id = fixture.Page.Chapter.section_id, fact_id = fixture.Page.FactId, page_index = fixture.PageIndex, part = fixture.Page.Part, parts = fixture.Page.Parts,
                literal_caption = kind == "PAGE_VISIBLE" ? fixture.Page.Text : null, literal_caption_sha256 = Plan.Sha(fixture.Page.Text),
                captions_sha256 = fixture.Page.Chapter.captions[fixture.Language].sha256, content_sha256 = Plan.ContentSha,
                editor_realtime = EditorApplication.timeSinceStartup, unity_realtime = Time.realtimeSinceStartupAsDouble, unity_frame = Time.frameCount,
                utc = DateTime.UtcNow.ToString("O"), page_elapsed_seconds = elapsed, planned_page_seconds = fixture.Seconds,
                active_capture_running = ResearchS1GameViewVideo.Running, camera = CameraRecord(fixture),
                mechanism = fixture.Mechanism, scripted_label = fixture.Label.text,
                owned_model_transforms = fixture.MotionRoots.Select(t => new { object_path = ObjectPath(t), position = new[] { t.position.x, t.position.y, t.position.z },
                    rotation = new[] { t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w } }).ToArray(),
                caption_line_count = fixture.Caption.textInfo.lineCount, caption_overflow = fixture.Caption.isTextOverflowing,
                evidence_scope = "Observed fixture frame, not a capture receipt or semantic acceptance" }) + "\n", new UTF8Encoding(false));
        }
        static void Watch()
        {
            if (current == null || current.Stopping) return;
            try
            {
                Context(current); CameraUnchanged(current); VisualIsolation(current);
                if (!current.RecordingStarted && EditorApplication.timeSinceStartup - current.Began > 60)
                { current.Error = "No independently owned Game View recording started within 60 seconds."; Finish(current, "capture_not_started"); return; }
                if (ResearchS1GameViewVideo.Running) current.RecordingStarted = true;
                else if (current.RecordingStarted)
                { current.Error = "Real recording stopped before all educational pages were observed."; Finish(current, "capture_stopped"); }
            }
            catch (Exception error) { current.Error = error.ToString(); Finish(current, "context_changed"); }
        }
        static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); }
        static void BeforeReload() => Stop();
        static void Write(Fixture fixture, string file, object value) => File.WriteAllText(Path.Combine(fixture.Output, file), JsonConvert.SerializeObject(value, Formatting.Indented), new UTF8Encoding(false));
        static void Finish(Fixture fixture, string reason)
        {
            if (fixture.Stopping) return; fixture.Stopping = true;
            EditorApplication.update -= Watch; EditorApplication.playModeStateChanged -= PlayState; AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            var failures = new List<Exception>();
            try { if (fixture.Routine != null && fixture.Controller) fixture.Controller.StopCoroutine(fixture.Routine); } catch (Exception error) { failures.Add(error); }
            try { RestoreCulling(fixture); } catch (Exception error) { failures.Add(error); }
            foreach (var pair in fixture.Canvases) try { if (pair.Key) pair.Key.enabled = pair.Value; else throw new InvalidOperationException("An original canvas was removed."); } catch (Exception error) { failures.Add(error); }
            try { if (fixture.Owned) UnityEngine.Object.DestroyImmediate(fixture.Owned); } catch (Exception error) { failures.Add(error); }
            foreach (var font in fixture.Fonts.Values) try { if (font) UnityEngine.Object.DestroyImmediate(font); } catch (Exception error) { failures.Add(error); }
            foreach (var mesh in fixture.GeneratedMeshes) try { if (mesh) UnityEngine.Object.DestroyImmediate(mesh); } catch (Exception error) { failures.Add(error); }
            bool untouched = fixture.Sources.All(s => s.Unchanged);
            fixture.Restored = failures.Count == 0 && untouched && fixture.Camera && fixture.Camera.cullingMask == fixture.Culling && !fixture.CullingOwned && fixture.Renderers.All(p => p.Key) && fixture.Canvases.All(p => p.Key && p.Key.enabled == p.Value) && !fixture.Owned;
            fixture.Status = fixture.Error == null && fixture.Restored && fixture.ObservedPages == fixture.Pages?.Count && fixture.SectionsEnded == 8 ? "OBSERVED_EDUCATIONAL_PAGES_PENDING_ROOT_VISUAL_REVIEW" : "EDUCATIONAL_ACQUISITION_INCOMPLETE";
            if (failures.Count > 0) fixture.Error = (fixture.Error ?? "") + new AggregateException("Educational fixture cleanup failed.", failures);
            try { Write(fixture, "result.json", new { schema = "s1.educational-fixture-result.1", status = fixture.Status, stop_reason = reason, error = fixture.Error,
                session_id = fixture.Session, run_id = fixture.Run, task_id = fixture.Task, language = fixture.Language, content_sha256 = Plan.ContentSha,
                observed_fixture_frames = fixture.ObservedFrames, observed_pages = fixture.ObservedPages, ended_sections = fixture.SectionsEnded,
                actual_capture_frames = "UNVERIFIED_USE_GAMEVIEW_CAPTURE_RECEIPT", media_accepted = false, e2e = false,
                output_role = "RAW_MODEL_ONLY", captions_in_metadata = true, participant_media_ready = false,
                source_transforms_unchanged = untouched, canvas_flags_restored = fixture.Restored, source_renderer_flags_never_written = true, camera_culling_restored = fixture.Camera && fixture.Camera.cullingMask == fixture.Culling && !fixture.CullingOwned, cleanup_errors = failures.Select(e => e.ToString()).ToArray(),
                camera_after = fixture.Camera ? CameraRecord(fixture) : null, camera_fov_preserved = fixture.Camera && fixture.Camera.fieldOfView == fixture.Fov,
                input_and_timing_never_written = true, source_models = fixture.Contract.source_models, captions = fixture.Contract.chapters.Where(c => c.task_id == fixture.Task).Select(c => new { c.section_id, c.fact_ids, captions_sha256 = c.captions[fixture.Language].sha256 }).ToArray(),
                required_next = "Root must validate raw model pixels, every chapter, complete metadata facts, timing, source colors and cleanup. Separate participant composition and review are required before runtime packaging." }); }
            catch (Exception error) { fixture.Error = (fixture.Error ?? "") + "Educational terminal result write failed. " + error; fixture.Status = "EDUCATIONAL_ACQUISITION_INCOMPLETE"; }
        }
        public static string Stop() { if (current != null && !current.Stopping) Finish(current, "explicit_stop"); return Audit(); }
        public static string Audit() => JsonConvert.SerializeObject(current == null ? (object)new { status = "NOT_STARTED" } : new { status = current.Status, error = current.Error,
            output = current.Output, task_id = current.Task, language = current.Language, session_id = current.Session, run_id = current.Run,
            section_id = current.Page?.Chapter.section_id, fact_id = current.Page?.FactId, page_index = current.PageIndex,
            observed_fixture_frames = current.ObservedFrames, observed_pages = current.ObservedPages, ended_sections = current.SectionsEnded,
            planned_pages = current.Pages?.Count, planned_seconds = current.Pages == null ? (double?)null : current.Pages.Sum(p => PageSeconds(p)),
            recording_started = current.RecordingStarted, stopped = current.Stopping, restore_observed = current.Restored, media_accepted = false, e2e = false });
    }
}
#endif
