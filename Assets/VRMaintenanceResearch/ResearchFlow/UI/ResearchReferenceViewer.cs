using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;
using VRMaintenanceResearch.TrainingV2;

namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchReferenceViewer : MonoBehaviour
    {
        [Serializable] public sealed class Page { public int page; public float width, height; public string resource, sha256; }
        [Serializable] public sealed class Step { public string step_id, topic_id, text_th, text_ja, text_en; public Picture picture; }
        [Serializable] public sealed class Picture { public string id, resource, sha256, caption_th, caption_ja, caption_en; public Region[] regions; }
        [Serializable] public sealed class Region { public string id; public float x, y, width, height; public string[] units; }
        [Serializable] public sealed class Chapter { public string stage; public double start_seconds, end_seconds; }
        [Serializable] public sealed class Movie { public string streaming_path, sha256; public Chapter[] chapters; }
        // Named contents per format (owner 4 Nov): pdf index = page and offset = 0..1 down that page; steps/images index = item;
        // video start_seconds = chapter start. Packages without contents keep the earlier Reader unchanged.
        [Serializable] public sealed class Entry { public string id, format, title_th, title_ja, title_en; public int index = 1; public float offset; public double start_seconds; }
        [Serializable] public sealed class Package
        {
            public int schema_version;
            public string content_id, version, source_sha256, source_pdf, source_pdf_resource, approval;
            public string[] reader_features;
            public Page[] pages; public Step[] steps; public Picture[] images; public Movie video; public Entry[] contents;
        }
        enum MediaState { Empty, Blocked, Loading, Ready, PlayRequested, Playing, Paused, Seeking, Ended, Error, AwaitingSelection }
        sealed class Position { public int index = 1, part = 1; public float normalized = 1, zoom = 1, horizontal = .5f; public double time; public bool selected, allSteps; }
        sealed class Registry { public Package package; public string hash, error; }

        #if UNITY_EDITOR
        public static Func<string, byte[]> EditorCaptureResource;
        public static Func<string, string> EditorCaptureVideoPath;
#endif
        const string PackageResource = "ResearchReaderDemo/package";
        const string DemoApproval = "DEMO_TEST_NOT_STUDY";
        // S1 engineering draft: technical/test sessions may read it; study sessions stay gated by the task port's contentApproved.
        const string S1DraftApproval = "S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW";
        // DEV-only front-guard prototype (ResearchBuildFlavor); its own resource root keeps it apart from participant packages.
        const string DevPrototypeApproval = "S1_DEV_PROTOTYPE_NOT_STUDY";
        public const string DevPrototypeContent = "s1.reader.dev.B_front_guard";
        // Amendment 8 Nov 2026 (proposed): a video the system hid while it played resumes if the Reader returns within this window.
        public const string VideoResumeVersion = "S1_VIDEO_RESUME_AFTER_OCCLUSION_20261108";
        public const float AutoResumeSeconds = 10f;
        float occludedSince = -1; double occludedAt; string occludedCause; bool autoResumePending, resumeHint, resumeHintShown;
        static readonly string[] ResourceRoots = { "ResearchReaderS1/", "ResearchReaderDemo/", "ResearchReaderS1Dev/" };
        public static string DevPrototypeFor(string language) =>
            "ResearchReaderS1Dev/B_FG_" + (language == "Japanese" ? "ja" : language == "English" ? "en" : "th") + "/package";

        // One package per context: T = Training (neutral Trainer_FX1), A/B = tasks; language th/ja (en exists only for Training).
        public static string ResourceFor(string task, string language) =>
            "ResearchReaderS1/" + (string.IsNullOrEmpty(task) ? "T" : task) + "_" + (language == "Japanese" ? "ja" : language == "English" ? "en" : "th") + "/package";

        public void UseRegistry(string resource)
        {
            if (resource == registryResource) return;
            if (!SafeResource(resource)) throw new ArgumentException("Registry must be a Reader resource.");
            SavePosition(); EndReading("registry_changed"); CancelVideo(); ClearContent();
            registryResource = resource; state = MediaState.Empty; key = null; package = null;
        }
        static readonly Dictionary<string, Registry> registries = new Dictionary<string, Registry>();
        readonly Dictionary<string, Position> positions = new Dictionary<string, Position>();
        readonly Dictionary<int, Texture2D> pageCache = new Dictionary<int, Texture2D>();
        readonly List<UnityEngine.UI.RawImage> pageImages = new List<UnityEngine.UI.RawImage>();
        readonly List<float> pageTops = new List<float>();
        readonly List<UnityEngine.UI.Button> videoButtons = new List<UnityEngine.UI.Button>();
        const string SectionCommand = "reference_media.section.";
        Entry[] entries = new Entry[0];
        Entry pinnedEntry; float pinnedPosition;
        string contentsState;
        bool contentsOpen;
        string shownSection;
        RectTransform contentsPanel, infographic;
        UnityEngine.UI.Button allStepsButton;
        readonly List<RectTransform> stepCards = new List<RectTransform>();
        bool Feature(string feature) => package?.reader_features != null && Array.IndexOf(package.reader_features, feature) >= 0;
        bool PicturedSteps => format == "steps" && Feature("steps_view_all");
        bool ZoomableImage => format == "images" && Feature("image_viewport");
        UnityEngine.UI.Button contentsButton;
        ScrollRect scroll, contentsScroll;
        TMP_Text template, statusLabel, contentText, timeLabel;
        ResearchFlowView view;
        RectTransform content, videoSurface, pdfControls;
        UnityEngine.UI.Button[] pdfButtons;
        UnityEngine.UI.RawImage videoImage;
        ResearchReaderTimeline timeline;
        ResearchReaderScrollbar guardedScrollbar;
        VideoPlayer player;
        AudioSource videoAudio;
        RenderTexture videoTexture;
        Coroutine prepare;
        Package package;
        MediaState state;
        string key, format, fallback, error, packageHash, pauseInput, registryResource = PackageResource;
        bool visible, readable, demoAllowed, suppressScroll, resumeAfterSeek, appFocused = true, appPaused, awaitingPreview, seekCompletedSeen, seekFrameSeen;
        int generation, index = 1;
        // Capture C02: video loading must never sit at "Loading" with no way out. After StallSeconds the Retry control is offered
        // while loading continues; each phase is logged so Android prepare/first-frame stalls can be diagnosed per build.
        float videoLoadStarted; string videoLoadPhase = ""; bool videoStallShown;
        // SHA256 of a StreamingAssets video is verified once per app run; re-reading ~20 MB on every format switch only added GC/IO.
        static readonly HashSet<string> verifiedVideos = new HashSet<string>(StringComparer.Ordinal);
        long decodedFrames, firstDecodedFrame = -1;
        double seekTarget;
        float seekStarted, playStarted, previewStarted, lastWidth, lastHeight, readingSince = -1;

        public bool StatusMessageVisible => statusLabel && statusLabel.gameObject.activeInHierarchy;
        public bool IsReady => state == MediaState.Ready || state == MediaState.Playing || state == MediaState.Paused || state == MediaState.Ended;
        public string Status => state.ToString();
        public int PageIndex => index;
        public int PageCount => package == null ? 1 : format == "pdf" ? package.pages.Length : format == "steps" ? package.steps.Length : format == "images" ? package.images.Length : 1;
        public bool CanPrevious => IsReady && !ContentsOpen && format != "video" && (index > 1 || (format == "images" && part > 1));
        public bool CanNext => IsReady && !ContentsOpen && format != "video" && (PicturedSteps || index < PageCount || (format == "images" && part < PartCount));
        // Illustrated sub-pages (ResearchCaptionParts): image index keeps its meaning in logs; caption_part is reported separately.
        int part = 1;
        int PartCount => !ZoomableImage && format == "images" && package != null && index >= 1 && index <= package.images.Length ? ResearchCaptionParts.Count(CaptionOf(index)) : 1;
        string CaptionOf(int imageIndex) { var image = package.images[imageIndex - 1]; return Local(image.caption_th, image.caption_ja, image.caption_en); }
        public int CachedPageCount => pageCache.Count;
        public string BoundRegistryResource => registryResource;
        public string BoundSection => format == "pdf" ? "user_manual" : format == "steps" ? "step_by_step" : format == "images" ? "illustrated" : format;
        public Slider Timeline => timeline;
        public bool TimelineHeld => timeline && timeline.Held;
        public Scrollbar GuardedScrollbar => guardedScrollbar;
        public bool ScrollbarHeld => guardedScrollbar && guardedScrollbar.Held;
        public bool ControlHeld => TimelineHeld || ScrollbarHeld;
        public string BindingToken => generation + ":" + key + ":" + format + ":" + packageHash + ":" + visible;
        public bool CanScrollDocument => visible && IsReady && !ContentsOpen && format != "video" && content.rect.height > scroll.viewport.rect.height + 1 && view && view.controller && view.controller.LocalUICommandsAllowed;
        internal ScrollRect ScrollTarget => ContentsOpen ? contentsScroll : scroll;
        public bool CanSeekTimeline => visible && IsReady && player && player.isPrepared && player.canSetTime && ValidDuration(player.length) && view && view.controller && view.controller.LocalUICommandsAllowed;
        public VideoPlayer NativeVideoPlayer => player;
        public long DecodedFrameCount => decodedFrames;
        public long FirstDecodedFrame => firstDecodedFrame;
        public string SemanticToken => generation + ":" + key + ":" + format + ":" + index + ":" + state;
        public string Title => (state == MediaState.AwaitingSelection ? Local("เลือกรูปแบบสื่อ", "資料の形式を選択", "Choose a format") : format == "pdf" ? "PDF" : format == "steps" ? Local("ขั้นตอน", "手順", "Steps") : format == "images" ? Local("รูปภาพ", "画像", "Images") : "Video")
            + (CurrentEntry is Entry entry ? " · " + EntryTitle(entry) : "");
        public bool ContentsAvailable => visible && IsReady && entries.Length > 0;
        public bool ContentsOpen => contentsOpen && ContentsAvailable;
        public string SectionId => CurrentEntry?.id;
        public string[] SectionIds => entries.Select(e => e.id).ToArray();
        string EntryTitle(Entry entry) => Local(entry.title_th, entry.title_ja, entry.title_en);
        // The topic in view: the last entry that starts at or before the reading position (pdf: upper quarter of the viewport).
        Entry CurrentEntry
        {
            get
            {
                // A video mid-seek or mid-play request already has its target time; other formats need a rendered page.
                if (ContentsOpen || (PicturedSteps && Saved.allSteps) || entries.Length == 0 || (!IsReady && !(format == "video" && (state == MediaState.Seeking || state == MediaState.PlayRequested)))) return null;
                if (format == "video")
                {
                    double t = state == MediaState.Seeking ? seekTarget : player && player.isPrepared ? player.time : Saved.time;
                    return entries.LastOrDefault(e => e.start_seconds <= t + .05) ?? entries[0];
                }
                if (format == "pdf")
                {
                    if (pageTops.Count == 0 || !scroll) return entries[0];
                    // A topic chosen from the contents stays current until the reader scrolls; a heading near the page end
                    // may never reach the reading line, so at the end of the document the last heading in view counts.
                    if (pinnedEntry != null && Mathf.Abs(scroll.verticalNormalizedPosition - pinnedPosition) < .002f) return pinnedEntry;
                    float range = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height), top = (1 - scroll.verticalNormalizedPosition) * range;
                    float line = top + scroll.viewport.rect.height * (range - top < 2 ? 1 : .25f);
                    return entries.LastOrDefault(e => PdfOffset(e) <= line) ?? entries[0];
                }
                return entries.LastOrDefault(e => e.index <= index) ?? entries[0];
            }
        }
        float PdfOffset(Entry entry)
        {
            int page = Mathf.Clamp(entry.index, 1, pageTops.Count) - 1;
            return pageTops[page] + Mathf.Clamp01(entry.offset) * pageImages[page].rectTransform.rect.height;
        }
        public string DocumentName => package == null ? Title : System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(package.source_pdf).Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
        public bool NavigationVisible => IsReady && !ContentsOpen && !(PicturedSteps && Saved.allSteps) && (format == "pdf" || ((format == "steps" || format == "images") && (PageCount > 1 || PartCount > 1)));
        public string NavigationLabel => format == "pdf" ? index + " / " + PageCount : format == "steps"
            ? Local("ขั้นที่ " + index + " จาก " + PageCount, "手順 " + index + " / " + PageCount, "Step " + index + " of " + PageCount)
            : Local("ภาพ " + index + " / " + PageCount, "画像 " + index + " / " + PageCount, "Image " + index + " of " + PageCount)
                + (PartCount > 1 ? " · " + part + "/" + PartCount : "");
        public object Metadata => new
        {
            event_schema = Feature("steps_view_all") || Feature("image_viewport") ? "viewer_v2" : "viewer_v1", context = key, content_id = package?.content_id, version = package?.version,
            source_pdf = package?.source_pdf, source_sha256 = package?.source_sha256, registry_sha256 = packageHash,
            approval = package?.approval, media_type = format, page = format == "pdf" ? index : (int?)null, section_id = SectionId,
            index, caption_part = format == "images" ? part : (int?)null, caption_parts = format == "images" ? PartCount : (int?)null,
            pdf_zoom = format == "pdf" ? Saved.zoom : (float?)null, pdf_horizontal = format == "pdf" && scroll ? scroll.horizontalNormalizedPosition : (float?)null,
            caption_layout = format == "images" ? ResearchCaptionParts.Version : null, normalized_offset = scroll ? scroll.verticalNormalizedPosition : 1,
            time_seconds = player && player.isPrepared ? player.time : Saved.time,
            seek_target_seconds = state == MediaState.Seeking ? seekTarget : (double?)null,
            status = Status, visible, in_view = readable, active_reading = readable && IsReady && !ContentsOpen, error,
            contents_open = ContentsOpen, contents_offset = ContentsOpen && contentsScroll ? contentsScroll.verticalNormalizedPosition : (float?)null, steps_view_all = PicturedSteps && Saved.allSteps,
            visible_step_ids = VisibleStepIds(), image_id = ZoomableImage && !ContentsOpen ? package.images[index - 1].id : null,
            image_zoom = ZoomableImage ? Saved.zoom : (float?)null, image_viewport = ImageViewport(),
            visible_region_ids = VisibleRegions(),
            cached_pages = CachedPageCount, semantic_token = SemanticToken,
            decoded_frames = decodedFrames, first_decoded_frame = firstDecodedFrame, timeline_held = TimelineHeld
        };
        Position Saved
        {
            get
            {
                string id = (key ?? "") + ":" + (packageHash ?? "") + ":" + (format ?? "");
                if (!positions.TryGetValue(id, out var position)) positions[id] = position = new Position();
                return position;
            }
        }

        public static bool DemoAvailable(string requestedFormat) => Available(requestedFormat, PackageResource);
        public static bool Available(string requestedFormat, string resource)
        {
            var registry = LoadRegistry(resource);
            if (registry.package == null) return false;
            var demo = registry.package;
            switch (Normalize(requestedFormat))
            {
                case "pdf": return demo.pages.Length > 0;
                case "steps": return demo.steps.Length > 0;
                case "images": return demo.images.Length > 0;
                case "video": return demo.video != null;
                default: return false;
            }
        }

        public void Initialize(ScrollRect existingScroll, TMP_Text textTemplate, ResearchFlowView owner, string resource = PackageResource)
        {
            if (!existingScroll || !existingScroll.viewport || !existingScroll.content || !textTemplate || !owner)
                throw new ArgumentException("Reader requires existing ScrollRect, viewport, content, text template and view.");
            if (!SafeResource(resource)) throw new ArgumentException("Registry must be a Reader resource.");
            if (scroll)
            {
                SavePosition(); EndReading("registry_changed"); generation++; CancelVideo(); ClearContent();
                scroll.onValueChanged.RemoveListener(OnScroll);
                if (statusLabel) ReleaseObject(statusLabel.gameObject);
            }
            scroll = existingScroll; template = textTemplate; view = owner;
            registryResource = resource; state = MediaState.Empty; key = null; package = null;
            template.gameObject.SetActive(false);
            var mask = scroll.viewport.GetComponent<RectMask2D>();
            if (!mask && !scroll.viewport.GetComponent<UnityEngine.UI.Mask>()) scroll.viewport.gameObject.AddComponent<RectMask2D>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.onValueChanged.AddListener(OnScroll);
            content = scroll.content;
            GuardScrollbar();
            statusLabel = Text(scroll.viewport, "ViewerStatus", 26);
            statusLabel.alignment = TextAlignmentOptions.Center;
            Stretch(statusLabel.rectTransform, new Vector2(16, 16), new Vector2(-16, -16));
            statusLabel.gameObject.SetActive(false);
        }

        public void Bind(string contextKey, string requestedFormat, bool allowDemo, string fallbackText)
        {
            if (!scroll) throw new InvalidOperationException("Initialize reader before Bind.");
            statusLabel.font = view.Font;
            string nextFormat = Normalize(requestedFormat);
            if (key == contextKey && format == nextFormat && demoAllowed == allowDemo && fallback == fallbackText && state != MediaState.Empty) return;
            if (key != contextKey || format != nextFormat) CancelAutoResume();
            SavePosition(); EndReading("context_changed");
            if (guardedScrollbar) guardedScrollbar.Cancel("binding_changed");
            if (timeline) timeline.Cancel("binding_changed");
            generation++; CancelVideo(); ClearContent();
            key = contextKey; format = nextFormat; demoAllowed = allowDemo; fallback = fallbackText;
            error = null; package = null; entries = new Entry[0]; contentsOpen = false; shownSection = null; pinnedEntry = null;
            index = 1;
            if (!allowDemo)
            {
                state = MediaState.Blocked;
                ShowText(string.IsNullOrWhiteSpace(fallbackText) ? Local("ยังไม่มีเนื้อหาที่อนุมัติ", "承認済みの内容はありません", "No approved content available") : fallbackText); RefreshScrollbar();
                return;
            }
            if (string.IsNullOrWhiteSpace(contextKey) || (nextFormat != "" && nextFormat != "pdf" && nextFormat != "steps" && nextFormat != "images" && nextFormat != "video"))
            { Fail("Invalid reader context or format"); return; }
            if (nextFormat == "")
            {
                state = MediaState.AwaitingSelection; packageHash = null;
                statusLabel.gameObject.SetActive(false);
                ShowText(Local("เลือกรูปแบบสื่อจากเมนูด้านซ้าย", "左側のメニューから資料の形式を選んでください", "Choose a format from the menu on the left.")); RefreshScrollbar();
                return;
            }
            var registry = LoadRegistry(registryResource); packageHash = registry.hash;
            if (registry.package == null) { Fail(registry.error); return; }
            package = registry.package; index = Mathf.Clamp(Saved.index, 1, PageCount);
            entries = (package.contents ?? new Entry[0]).Where(e => e != null && Normalize(e.format) == nextFormat).ToArray();
            contentsOpen = (PicturedSteps || ZoomableImage) && !Saved.selected;
            Render(); Event("REFERENCE_VIEWER_BOUND", "System");
        }

        // show = the Reader surface is rendered and accepts input; inView = it is also fully inside both eyes, so it counts as reading.
        // A head turn that only moves a corner out of view must not disable controls or tear down the video decoder.
        public void SetVisible(bool show) => SetPresentation(show, true);
        /// <param name="systemHideCause">Set only when the Reader is hidden by placement (blocked, edge-on, recenter) while it stays open,
        /// unminimized and unpaused; user close/minimize/pause and app focus loss pass null and never auto-resume.</param>
        public void SetPresentation(bool show, bool inView, string systemHideCause = null)
        {
            bool focused = appFocused && !appPaused;
            show &= focused;
            bool read = show && inView;
            if (visible == show && readable == read) return;
            if (visible == show)
            {
                readable = read;
                if (read) BeginReading(); else EndReading("out_of_view");
                Event("REFERENCE_VIEWER_VISIBILITY", "System"); return;
            }
            visible = show; readable = read;
            if (!show)
            {
                if (state == MediaState.Seeking) Saved.time = seekTarget; else SavePosition();
                EndReading("hidden");
                if (timeline) timeline.Cancel("hidden");
                if (guardedScrollbar) guardedScrollbar.Cancel("hidden");
                if (format == "video")
                {
                    bool playing = player && player.isPlaying;
                    bool restoring = autoResumePending || occludedSince >= 0;
                    if ((playing || restoring) && systemHideCause != null && focused)
                    {
                        // Hidden again while the restore was still loading keeps the original resume point.
                        if (!restoring) occludedAt = Saved.time;
                        occludedSince = Time.realtimeSinceStartup; occludedCause = systemHideCause;
                    }
                    else occludedSince = -1;
                    autoResumePending = false;
                    generation++; CancelVideo(); state = MediaState.Empty;
                    if (playing) Event("REFERENCE_VIDEO_PAUSED", "System");
                }
            }
            else if (state == MediaState.Empty && package != null) { ClearContent(); Render(); }
            if (show) BeginReading(); RefreshStepControls(); RenderContents();
            Event("REFERENCE_VIEWER_VISIBILITY", "System"); RefreshVideoControls(); RefreshScrollbar();
        }

        public bool Navigate(int delta, string input = "None")
        {
            if (!visible || !IsReady || ContentsOpen || format == "video" || delta == 0) return false;
            if (PicturedSteps && delta > 0 && (index == PageCount || package.steps[index].topic_id != package.steps[index - 1].topic_id))
            { contentsOpen = true; EndReading("topic_end"); RefreshScrollbar(); Event("REFERENCE_VIEWER_CONTENTS_OPENED", Actor(input), input); view.RefreshPageControls(); return true; }
            if (format == "images")
            {
                int nextIndex = index, nextPart = part + Math.Sign(delta);
                if (nextPart > PartCount) { nextIndex = index + 1; nextPart = 1; }
                else if (nextPart < 1) { nextIndex = index - 1; nextPart = nextIndex >= 1 ? ResearchCaptionParts.Count(CaptionOf(nextIndex)) : 1; }
                if (nextIndex < 1 || nextIndex > PageCount) return false;
                index = nextIndex; part = nextPart; Saved.index = index; Saved.part = part; Saved.normalized = 1; ClearContent(); Render();
                SavePosition();
                Event("REFERENCE_VIEWER_NAVIGATE", Actor(input), input); view.RefreshPageControls(); return true;
            }
            int next = Mathf.Clamp(index + delta, 1, PageCount);
            if (next == index) return false;
            if (format == "pdf")
            {
                float range = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
                suppressScroll = true;
                scroll.verticalNormalizedPosition = range > 0 ? 1 - Mathf.Clamp01(pageTops[next - 1] / range) : 1;
                suppressScroll = false; UpdatePages(Actor(input), input);
            }
            else { index = next; Saved.index = next; Saved.normalized = 1; ClearContent(); Render(); }
            Saved.index = index; SavePosition();
            Event("REFERENCE_VIEWER_NAVIGATE", Actor(input), input); view.RefreshPageControls(); return true;
        }

        public bool Command(string command, string input)
        {
            if (!visible || !view || !view.controller || !view.controller.LocalUICommandsAllowed) return false;
            if (TimelineHeld) return false;
            CancelAutoResume();
            switch (command)
            {
                case "reference_media.zoom_out": return ChangeDocumentView(-.25f, 0, false, input);
                case "reference_media.zoom_in": return ChangeDocumentView(.25f, 0, false, input);
                case "reference_media.zoom_reset": return ChangeDocumentView(0, 0, true, input);
                case "reference_media.pan_left": return ChangeDocumentView(0, -.2f, false, input);
                case "reference_media.pan_right": return ChangeDocumentView(0, .2f, false, input);
                case "reference_media.retry":
                    if (!ResearchVideoLoadRules.RetryAllowed(state == MediaState.Error, state == MediaState.Loading, videoStallShown)) return false;
                    string retriedPhase = state == MediaState.Loading ? "stalled:" + videoLoadPhase : "error";
                    registries.Remove(registryResource);
                    string oldKey = key, oldFormat = format, oldFallback = fallback;
                    bool allowed = demoAllowed;
                    key = null; Bind(oldKey, oldFormat, allowed, oldFallback);
                    view?.controller?.ReferenceMediaEvent("REFERENCE_VIEWER_RETRY", new { viewer = Metadata, retried_from = retriedPhase }, Actor(input), input, BoundSection);
                    return state != MediaState.Error;
                case "reference_media.play":
                    if (!player || !player.isPrepared || !IsReady || player.isPlaying) return false;
                    if (state == MediaState.Ended) return Seek(0, input, true);
                    if (videoAudio) videoAudio.mute = false;
                    EndReading("play_requested"); player.Play(); state = MediaState.PlayRequested; playStarted = Time.realtimeSinceStartup;
                    Event("REFERENCE_VIDEO_PLAY_REQUESTED", Actor(input), input); RefreshVideoControls(); return true;
                case "reference_media.pause":
                    if (!player || !player.isPrepared || (!player.isPlaying && state != MediaState.Seeking && state != MediaState.PlayRequested)) return false;
                    if (!player.canSetTime) { player.Pause(); Fail("Native decoder cannot pin paused display"); return false; }
                    double pauseTime = player.time; pauseInput = input; resumeAfterSeek = false;
                    EndReading("pause_requested");
                    if (state == MediaState.Seeking) { player.Pause(); if (videoAudio) videoAudio.mute = true; Event("REFERENCE_VIDEO_PAUSE_REQUESTED", Actor(input), input); RefreshVideoControls(); return true; }
                    // Native Pause can finish queued frames; acknowledge only the frozen target's decoded frame.
                    return Seek(pauseTime, "None", false, "System", true);
                case "reference_media.seek_back": return CanSkipVideo && Seek(SkipTarget(player.time, package?.video?.chapters, -1), input, player.isPlaying);
                case "reference_media.seek_forward": return CanSkipVideo && Seek(SkipTarget(player.time, package?.video?.chapters, 1), input, player.isPlaying);
                case "reference_media.contents":
                    if (!ContentsAvailable) return false;
                    contentsOpen = !contentsOpen; if (contentsOpen) EndReading("contents"); else { Saved.selected = true; BeginReading(); } RefreshScrollbar(); view.RefreshPageControls();
                    Event(contentsOpen ? "REFERENCE_VIEWER_CONTENTS_OPENED" : "REFERENCE_VIEWER_CONTENTS_CLOSED", Actor(input), input); return true;
                case "reference_media.contents_up":
                case "reference_media.contents_down":
                    if (!ContentsOpen || !contentsScroll) return false;
                    float range = contentsScroll.content.rect.height - contentsScroll.viewport.rect.height;
                    if (range <= 0) return false;
                    float previousOffset = contentsScroll.verticalNormalizedPosition;
                    float nextOffset = Mathf.Clamp01(previousOffset
                        + (command.EndsWith("_up", StringComparison.Ordinal) ? 1 : -1) * contentsScroll.viewport.rect.height * .8f / range);
                    if (Mathf.Approximately(previousOffset, nextOffset)) return false;
                    contentsScroll.verticalNormalizedPosition = nextOffset;
                    Event("REFERENCE_CONTENTS_SCROLLED", Actor(input), input); return true;
                case "reference_media.view_all":
                    if (!PicturedSteps || !IsReady || ScrollbarHeld) return false;
                    Saved.allSteps = !Saved.allSteps; Saved.selected = true; Saved.normalized = 1;
                    ClearContent(); Render(); RenderContents();
                    Event("REFERENCE_STEPS_VIEW_CHANGED", Actor(input), input); view.RefreshPageControls(); return true;
                default:
                    if (PicturedSteps && IsReady && Saved.allSteps && !ContentsOpen && !ScrollbarHeld && command.StartsWith("reference_media.step.", StringComparison.Ordinal)
                        && int.TryParse(command.Substring("reference_media.step.".Length), out int selectedStep)
                        && selectedStep >= 1 && selectedStep <= PageCount)
                    {
                        Saved.allSteps = false; Saved.selected = true; index = selectedStep; Saved.index = index; Saved.normalized = 1;
                        ClearContent(); Render(); Event("REFERENCE_STEP_SELECTED", Actor(input), input); view.RefreshPageControls(); return true;
                    }
                    return command.StartsWith(SectionCommand, StringComparison.Ordinal) && SelectSection(command.Substring(SectionCommand.Length), input);
            }
        }

        // A named jump is a choice of where to read, not progress: it never marks a step done or credits a repair.
        bool SelectSection(string id, string input)
        {
            var entry = ContentsAvailable ? entries.FirstOrDefault(e => e.id == id) : null;
            if (entry == null) return false;
            string from = SectionId;
            contentsOpen = false; Saved.selected = true; Saved.allSteps = false;
            if (format == "video") Seek(entry.start_seconds, input, player && player.isPlaying);
            else if (format == "pdf")
            {
                float range = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
                suppressScroll = true;
                scroll.verticalNormalizedPosition = range > 0 ? 1 - Mathf.Clamp01((PdfOffset(entry) - 12) / range) : 1;
                suppressScroll = false; pinnedEntry = entry; pinnedPosition = scroll.verticalNormalizedPosition; UpdatePages(Actor(input), input);
            }
            else
            {
                index = Mathf.Clamp(entry.index, 1, PageCount); part = 1;
                Saved.index = index; Saved.part = 1; Saved.normalized = 1; ClearContent(); Render();
            }
            SavePosition(); BeginReading();
            view?.controller?.ReferenceMediaEvent("REFERENCE_VIEWER_SECTION_SELECTED", new { viewer = Metadata, from_section = from, to_section = entry.id }, Actor(input), input, BoundSection);
            RenderContents(); view?.RefreshPageControls(); return true;
        }

        // Contents button sits in the sidebar slot under the four format tabs (Task Reader; Training packages carry no contents).
        // The list covers the viewport while open; rows are ordinary Reader buttons, the current topic carries a side bar.
        void RenderContents()
        {
            bool show = ContentsAvailable;
            bool allowed = view && view.controller && view.controller.LocalUICommandsAllowed;
            if (!contentsButton && show)
            {
                contentsButton = Control(scroll.transform.parent, "ReaderContents", "", "reference_media.contents");
                var rect = contentsButton.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(240, 56); rect.anchoredPosition = new Vector2(-438, -350);
            }
            if (contentsButton)
            {
                contentsButton.gameObject.SetActive(show);
                var label = contentsButton.GetComponentInChildren<TMP_Text>(true); label.font = view.Font;
                label.text = ContentsOpen ? Local("ปิดสารบัญ", "目次を閉じる", "Close contents") : Local("สารบัญ", "目次", "Contents") + " (" + entries.Length + ")";
                contentsButton.interactable = show && allowed;
            }
            if (!ContentsOpen) { if (contentsPanel) contentsPanel.gameObject.SetActive(false); return; }
            if (!contentsPanel)
            {
                contentsPanel = Rect(scroll.viewport, "ReaderContentsList", Vector2.zero); Stretch(contentsPanel, Vector2.zero, Vector2.zero);
                var surface = contentsPanel.gameObject.AddComponent<UnityEngine.UI.Image>(); surface.color = Color.white; surface.raycastTarget = true;
                contentsPanel.gameObject.AddComponent<ResearchReaderContentsSurface>();
            }
            for (int i = contentsPanel.childCount - 1; i >= 0; i--) { var child = contentsPanel.GetChild(i).gameObject; child.SetActive(false); ReleaseObject(child); }
            contentsPanel.gameObject.SetActive(true); contentsPanel.SetAsLastSibling();
            float width = contentsPanel.rect.width, height = contentsPanel.rect.height;
            var heading = Text(contentsPanel, "ContentsHeading", 28); heading.text = "<b>" + Local("สารบัญ", "目次", "Contents") + "</b>  " + Title.Split('·')[0].Trim();
            heading.rectTransform.anchorMin = heading.rectTransform.anchorMax = heading.rectTransform.pivot = new Vector2(0, 1);
            heading.rectTransform.sizeDelta = new Vector2(width - 220, 56); heading.rectTransform.anchoredPosition = new Vector2(24, -12);
            heading.alignment = TextAlignmentOptions.MidlineLeft;
            var close = Control(contentsPanel, "ContentsClose", Local("ปิด", "閉じる", "Close"), "reference_media.contents");
            var closeRect = close.GetComponent<RectTransform>(); closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = new Vector2(1, 1);
            closeRect.sizeDelta = new Vector2(160, 56); closeRect.anchoredPosition = new Vector2(-24, -12); close.interactable = allowed;
            var listViewport = Rect(contentsPanel, "ContentsViewport", Vector2.zero);
            Stretch(listViewport, new Vector2(0, 12), new Vector2(0, -84));
            listViewport.gameObject.AddComponent<RectMask2D>();
            var listSurface = listViewport.gameObject.AddComponent<UnityEngine.UI.Image>(); listSurface.color = Color.white;
            var listContent = Rect(listViewport, "ContentsRows", Vector2.zero);
            listContent.anchorMin = new Vector2(0, 1); listContent.anchorMax = new Vector2(1, 1); listContent.pivot = new Vector2(.5f, 1);
            const float rowHeight = 72;
            listContent.sizeDelta = new Vector2(0, entries.Length * (rowHeight + 8));
            var listScroll = listViewport.gameObject.AddComponent<ScrollRect>(); contentsScroll = listScroll;
            listScroll.viewport = listViewport; listScroll.content = listContent; listScroll.horizontal = false;
            listScroll.movementType = ScrollRect.MovementType.Clamped; listScroll.inertia = false; listScroll.scrollSensitivity = 48;
            string[] listCommands = { "contents_up", "contents_down" };
            for (int i = 0; i < listCommands.Length; i++)
            {
                var button = Control(contentsPanel, "ContentsMove_" + i, i == 0 ? "↑" : "↓", "reference_media." + listCommands[i]);
                var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
                rect.sizeDelta = new Vector2(64, 56); rect.anchoredPosition = new Vector2(-196 - i * 72, -12);
                button.interactable = allowed && listContent.sizeDelta.y > height - 96;
            }
            heading.rectTransform.sizeDelta = new Vector2(width - 380, 56);
            string current = SectionId;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                string lead = format == "video" ? DisplayTime(entry.start_seconds) : (i + 1).ToString(CultureInfo.InvariantCulture);
                var row = Control(listContent, "Section_" + entry.id, lead + "   " + EntryTitle(entry), SectionCommand + entry.id);
                var rect = row.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
                rect.sizeDelta = new Vector2(width - 48, rowHeight); rect.anchoredPosition = new Vector2(0, -i * (rowHeight + 8));
                var label = row.GetComponentInChildren<TMP_Text>(true); label.alignment = TextAlignmentOptions.MidlineLeft; label.fontSize = 26;
                label.rectTransform.offsetMin = new Vector2(28, 4);
                row.interactable = allowed;
                if (entry.id == current)
                {
                    var bar = Rect(rect, "CurrentSection", new Vector2(8, rowHeight - 16)).gameObject.AddComponent<UnityEngine.UI.Image>();
                    bar.color = ResearchUITheme.Blue; bar.raycastTarget = false;
                    bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = new Vector2(0, .5f); bar.rectTransform.anchoredPosition = new Vector2(12, 0);
                }
            }
        }

        void Render()
        {
            suppressScroll = true;
            lastWidth = scroll.viewport.rect.width; lastHeight = scroll.viewport.rect.height;
            scroll.StopMovement(); statusLabel.gameObject.SetActive(false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, scroll.viewport.rect.height);
            scroll.vertical = format != "video"; scroll.horizontal = false;
            // Owner 31 Oct: the four formats looked alike (same frame, same screenshots). Each now has its own surface: paper pages on a
            // grey desk, a text-only step card, a picture board, and a dark player.
            var backdrop = scroll.viewport.GetComponent<UnityEngine.UI.Image>();
            if (backdrop) backdrop.color = format == "pdf" ? PdfDesk : format == "video" ? PlayerBlack : new Color(0, 0, 0, .001f);
            statusLabel.color = format == "video" ? Color.white : ResearchUITheme.Ink;
            if (format == "pdf") RenderPdf();
            else if (format == "steps")
            {
                if (PicturedSteps) RenderSteps();
                else {
                    var step = package.steps[index - 1];
                    string text = Local(step.text_th, step.text_ja, step.text_en);
                    if (string.IsNullOrWhiteSpace(text)) Fail("Step unavailable in selected language");
                    else { ShowText(text); state = MediaState.Ready; }
                }
            }
            else if (format == "images") { if (ZoomableImage) RenderInfographic(); else RenderImage(); }
            else if (format == "video") RenderVideo();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(Saved.normalized);
            scroll.horizontalNormalizedPosition = format == "pdf" || ZoomableImage ? Saved.horizontal : .5f;
            suppressScroll = false;
            if (format == "pdf" && state != MediaState.Error) UpdatePages();
            RefreshScrollbar(); RefreshStepControls(); BeginReading();
        }

        void RenderPdf()
        {
            foreach (var page in package.pages)
            {
                var image = Rect(content, "PDFPage_" + page.page, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.RawImage>();
                image.raycastTarget = false; image.color = Color.white;
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(.5f, 1); image.rectTransform.pivot = new Vector2(.5f, 1);
                pageImages.Add(image);
            }
            LayoutPdf(); state = MediaState.Ready;
        }

        // Every portrait S1 task page keeps at least 8% white on each side (Tools/pdf_margins.py), so 100% trims 7%
        // and the text column, not the paper edge, fills the viewer. Pages stay centered; nothing pans at 100%.
        public const float PortraitPageCrop = .07f;
        static readonly Color PdfDesk = new Color(.78f, .81f, .85f, 1), PlayerBlack = new Color(.03f, .04f, .05f, 1);
        void LayoutPdf()
        {
            float fit = Mathf.Max(1, scroll.viewport.rect.width - 24), top = 12, widest = 0;
            pageTops.Clear();
            for (int i = 0; i < package.pages.Length; i++)
            {
                var page = package.pages[i];
                float width = fit * Saved.zoom / (page.height > page.width ? 1 - 2 * PortraitPageCrop : 1);
                float height = width * page.height / page.width;
                pageImages[i].rectTransform.sizeDelta = new Vector2(width, height);
                pageImages[i].rectTransform.anchoredPosition = new Vector2(0, -top);
                pageTops.Add(top); top += height + 16; widest = Mathf.Max(widest, width);
            }
            content.sizeDelta = new Vector2(Mathf.Max(0, widest + 24 - scroll.viewport.rect.width), Mathf.Max(scroll.viewport.rect.height, top));
        }

        bool ImageHasHorizontalOverflow => infographic && scroll && infographic.rect.width > scroll.viewport.rect.width + .01f;

        bool ChangeDocumentView(float zoomDelta, float panDelta, bool reset, string input)
        {
            if (ContentsOpen) return false;
            if (!ZoomableImage) return ChangePdfView(zoomDelta, panDelta, reset, input);
            if (!IsReady || ScrollbarHeld || !infographic) return false;
            if (panDelta != 0 && !ImageHasHorizontalOverflow) return false;
            var previousViewport = VisibleImageRect();
            float zoom = reset ? 1 : Mathf.Clamp(Saved.zoom + zoomDelta, 1, 3);
            float horizontal = reset || zoom == 1 ? .5f : Mathf.Clamp01(scroll.horizontalNormalizedPosition + panDelta);
            if (Mathf.Approximately(zoom, Saved.zoom) && Mathf.Approximately(horizontal, scroll.horizontalNormalizedPosition)) return false;
            float vertical = reset ? 1 : scroll.verticalNormalizedPosition;
            suppressScroll = true; scroll.StopMovement(); Saved.zoom = zoom; LayoutInfographic(); Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = vertical; scroll.horizontalNormalizedPosition = horizontal;
            suppressScroll = false;
            if (panDelta != 0 && Mathf.Approximately(previousViewport.x, VisibleImageRect().x)) return false;
            SavePosition(); RefreshScrollbar(); Event("REFERENCE_IMAGE_VIEW_CHANGED", Actor(input), input); return true;
        }

        bool ChangePdfView(float zoomDelta, float panDelta, bool reset, string input)
        {
            if (format != "pdf" || !IsReady || ScrollbarHeld || pageImages.Count == 0) return false;
            float oldZoom = Saved.zoom, oldHorizontal = scroll.horizontalNormalizedPosition;
            float zoom = reset ? 1 : Mathf.Clamp(oldZoom + zoomDelta, 1, 3);
            float horizontal = zoom == 1 || reset ? .5f : Mathf.Clamp01(oldHorizontal + panDelta);
            if (Mathf.Approximately(zoom, oldZoom) && (zoom == 1 || Mathf.Abs(horizontal - oldHorizontal) < .001f)) return false;
            float offset = (1 - scroll.verticalNormalizedPosition) * Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
            int page = 0;
            for (int i = 1; i < pageTops.Count; i++) if (pageTops[i] <= offset + 16) page = i;
            float fraction = (offset - pageTops[page]) / Mathf.Max(1, pageImages[page].rectTransform.rect.height);
            scroll.StopMovement(); suppressScroll = true;
            Saved.zoom = zoom; LayoutPdf(); Canvas.ForceUpdateCanvases();
            float range = Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
            float nextOffset = pageTops[page] + fraction * pageImages[page].rectTransform.rect.height;
            scroll.verticalNormalizedPosition = range > 0 ? 1 - Mathf.Clamp01(nextOffset / range) : 1;
            scroll.horizontalNormalizedPosition = horizontal;
            suppressScroll = false; SavePosition(); UpdatePages(Actor(input), input); RefreshPdfControls();
            Event("REFERENCE_PDF_VIEW_CHANGED", Actor(input), input); return true;
        }

        void RefreshPdfControls()
        {
            bool show = visible && (format == "pdf" || ZoomableImage) && IsReady && !ContentsOpen;
            if (!pdfControls && show)
            {
                // Viewer footer, left of the page counter (x>=108); the sidebar slot below the format tabs holds Training feedback.
                pdfControls = Rect(scroll.transform.parent, "PdfViewControls", new Vector2(384, 48));
                pdfControls.anchoredPosition = new Vector2(-88, -350);
                string[] labels = { "-", "100%", "+", "<", ">" };
                string[] commands = { "zoom_out", "zoom_reset", "zoom_in", "pan_left", "pan_right" };
                pdfButtons = new UnityEngine.UI.Button[commands.Length];
                for (int i = 0; i < commands.Length; i++)
                {
                    pdfButtons[i] = Control(pdfControls, "Pdf_" + commands[i], labels[i], "reference_media." + commands[i]);
                    var rect = pdfButtons[i].GetComponent<RectTransform>();
                    // "100%" keeps the original 88 px so it never wraps; symbol buttons are 64 px on an 80 px pitch.
                    rect.sizeDelta = new Vector2(i == 1 ? 88 : 64, 48); rect.anchoredPosition = new Vector2((i - 2) * 80, 0);
                    pdfButtons[i].GetComponentInChildren<TMP_Text>().textWrappingMode = TextWrappingModes.NoWrap;
                    pdfButtons[i].GetComponentInChildren<TMP_Text>().margin = Vector4.zero;
                }
            }
            if (!pdfControls) return;
            pdfControls.gameObject.SetActive(show);
            if (!show) return;
            bool allowed = view && view.controller && view.controller.LocalUICommandsAllowed && !ScrollbarHeld;
            bool horizontalPanAvailable = Saved.zoom > 1 && (!ZoomableImage || ImageHasHorizontalOverflow);
            bool[] enabled = { Saved.zoom > 1, Saved.zoom != 1,
                Saved.zoom < 3, horizontalPanAvailable && scroll.horizontalNormalizedPosition > .001f, horizontalPanAvailable && scroll.horizontalNormalizedPosition < .999f };
            for (int i = 0; i < pdfButtons.Length; i++)
            {
                // No horizontal pan at fit width. Hide inert arrows that resemble page navigation.
                bool applicable = i < 3 || horizontalPanAvailable;
                pdfButtons[i].gameObject.SetActive(applicable);
                pdfButtons[i].interactable = applicable && allowed && enabled[i];
            }
            pdfButtons[1].GetComponentInChildren<TMP_Text>().text = Mathf.RoundToInt(Saved.zoom * 100) + "%";
        }

        void UpdatePages(string actor = "System", string input = "None")
        {
            if (package == null || format != "pdf" || pageImages.Count == 0 || state == MediaState.Error) return;
            float offset = (1 - scroll.verticalNormalizedPosition) * Mathf.Max(0, content.rect.height - scroll.viewport.rect.height);
            int next = 1;
            for (int i = 1; i < pageTops.Count; i++) if (pageTops[i] <= offset + 16) next = i + 1;
            bool changed = index != next; index = next;
            var remove = new List<int>();
            foreach (var pair in pageCache) if (Mathf.Abs(pair.Key - index) > 1) remove.Add(pair.Key);
            foreach (int page in remove)
            { pageImages[page - 1].texture = null; ReleaseObject(pageCache[page]); pageCache.Remove(page); }
            for (int page = Mathf.Max(1, index - 1); page <= Mathf.Min(PageCount, index + 1); page++)
            {
                if (pageCache.ContainsKey(page)) continue;
                var entry = package.pages[page - 1];
                Texture2D texture = Decode(entry.resource, entry.sha256);
                if (!texture) return;
                if (Mathf.Abs((float)texture.width / texture.height - entry.width / entry.height) > .02f)
                { ReleaseObject(texture); Fail("PDF page aspect mismatch: " + page); return; }
                pageCache.Add(page, texture); pageImages[page - 1].texture = texture;
            }
            SavePosition();
            if (changed) Event("REFERENCE_VIEWER_PAGE_BOUNDARY", actor, input);
            view.RefreshPageControls();
        }

        void RefreshStepControls()
        {
            bool show = visible && PicturedSteps && IsReady;
            if (!allStepsButton && show)
            {
                allStepsButton = Control(scroll.transform.parent, "StepsViewAll", "", "reference_media.view_all");
                var rect = allStepsButton.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(240, 56); rect.anchoredPosition = new Vector2(-144, -350);
            }
            if (!allStepsButton) return;
            allStepsButton.gameObject.SetActive(show);
            allStepsButton.interactable = show && view && view.controller && view.controller.LocalUICommandsAllowed && !ScrollbarHeld;
            allStepsButton.GetComponentInChildren<TMP_Text>(true).text = Saved.allSteps
                ? Local("ดูทีละขั้น", "一つずつ表示", "One step") : Local("ดูทั้งหมด", "すべて表示", "View all");
        }

        void RenderSteps()
        {
            float top = 12, width = Mathf.Max(1, scroll.viewport.rect.width - 40);
            int first = Saved.allSteps ? 1 : index, last = Saved.allSteps ? PageCount : index;
            for (int n = first; n <= last; n++)
            {
                var step = package.steps[n - 1];
                var card = Rect(content, "Step_" + step.step_id, Vector2.zero); stepCards.Add(card);
                card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 1); card.anchoredPosition = new Vector2(0, -top);
                var texture = Decode(step.picture.resource, step.picture.sha256); if (!texture) return;
                pageCache.Add(n, texture);
                float pictureHeight = Mathf.Min(scroll.viewport.rect.height * .48f, width * texture.height / texture.width);
                float pictureWidth = pictureHeight * texture.width / texture.height;
                var graphic = Rect(card, "Picture", new Vector2(pictureWidth, pictureHeight)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
                graphic.texture = texture; graphic.raycastTarget = false;
                graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = graphic.rectTransform.pivot = new Vector2(.5f, 1);
                var label = Text(card, "Instruction", 30); label.text = Local(step.text_th, step.text_ja, step.text_en);
                if (string.IsNullOrWhiteSpace(label.text)) { Fail("Step unavailable in selected language"); return; }
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(.5f, 1);
                label.rectTransform.sizeDelta = new Vector2(width, 10000); label.ForceMeshUpdate(true, true);
                float textHeight = Mathf.Max(48, label.preferredHeight + 16);
                label.rectTransform.sizeDelta = new Vector2(width, textHeight); label.rectTransform.anchoredPosition = new Vector2(0, -pictureHeight - 16);
                float height = pictureHeight + textHeight + 32;
                if (Saved.allSteps)
                {
                    var select = Control(card, "ChooseStep", Local("เปิดขั้นที่ ", "手順を開く ", "Open step ") + n, "reference_media.step." + n);
                    var rect = select.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
                    rect.sizeDelta = new Vector2(240, 64); rect.anchoredPosition = new Vector2(0, -height); height += 80;
                }
                card.sizeDelta = new Vector2(width, height); top += height + 24;
            }
            content.sizeDelta = new Vector2(0, Mathf.Max(scroll.viewport.rect.height, top)); state = MediaState.Ready;
        }

        void RenderInfographic()
        {
            var image = package.images[index - 1]; var texture = Decode(image.resource, image.sha256); if (!texture) return;
            pageCache.Add(index, texture);
            infographic = Rect(content, "Infographic_" + image.id, Vector2.zero);
            var graphic = infographic.gameObject.AddComponent<UnityEngine.UI.RawImage>(); graphic.texture = texture; graphic.raycastTarget = false;
            infographic.anchorMin = infographic.anchorMax = infographic.pivot = new Vector2(.5f, 1);
            infographic.anchoredPosition = new Vector2(0, -12);
            LayoutInfographic(); state = MediaState.Ready;
        }

        void LayoutInfographic()
        {
            var texture = pageCache[index];
            float fit = Mathf.Min((scroll.viewport.rect.width - 24) / texture.width, (scroll.viewport.rect.height - 24) / texture.height);
            float width = texture.width * fit * Saved.zoom, height = texture.height * fit * Saved.zoom;
            infographic.sizeDelta = new Vector2(width, height);
            content.sizeDelta = new Vector2(Mathf.Max(0, width - scroll.viewport.rect.width), Mathf.Max(scroll.viewport.rect.height, height + 24));
            scroll.horizontal = ImageHasHorizontalOverflow;
        }

        Rect VisibleImageRect()
        {
            if (!ZoomableImage || !infographic || !visible || !readable || ContentsOpen) return UnityEngine.Rect.zero;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, infographic);
            var window = scroll.viewport.rect;
            float left = Mathf.Max(window.xMin, bounds.min.x), right = Mathf.Min(window.xMax, bounds.max.x);
            float bottom = Mathf.Max(window.yMin, bounds.min.y), top = Mathf.Min(window.yMax, bounds.max.y);
            return NormalizedViewport(bounds.min.x, bounds.max.y, bounds.size.x, bounds.size.y, left, top, right, bottom);
        }

        public static Rect NormalizedViewport(float imageLeft, float imageTop, float imageWidth, float imageHeight, float left, float top, float right, float bottom)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || right <= left || top <= bottom) return UnityEngine.Rect.zero;
            float x0 = Mathf.Clamp01((left - imageLeft) / imageWidth), x1 = Mathf.Clamp01((right - imageLeft) / imageWidth);
            float y0 = Mathf.Clamp01((imageTop - top) / imageHeight), y1 = Mathf.Clamp01((imageTop - bottom) / imageHeight);
            return new Rect(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
        }

        object ImageViewport()
        {
            if (!ZoomableImage) return null;
            var rect = VisibleImageRect(); return new { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
        }

        string[] VisibleRegions()
        {
            var rect = VisibleImageRect(); if (rect.width <= 0 || rect.height <= 0) return new string[0];
            return (package.images[index - 1].regions ?? new Region[0]).Where(r => rect.Overlaps(new Rect(r.x, r.y, r.width, r.height))).Select(r => r.id).ToArray();
        }

        string[] VisibleStepIds()
        {
            if (!PicturedSteps || !visible || !readable || ContentsOpen) return new string[0];
            var result = new List<string>(); var window = scroll.viewport.rect;
            for (int i = 0; i < stepCards.Count; i++)
            {
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, stepCards[i]);
                if (window.Overlaps(new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y)))
                    result.Add(package.steps[Saved.allSteps ? i : index - 1].step_id);
            }
            return result.ToArray();
        }

        void RenderImage()
        {
            var image = package.images[index - 1];
            Texture2D texture = Decode(image.resource, image.sha256);
            if (!texture) return;
            pageCache.Add(index, texture);
            float width = Mathf.Max(1, scroll.viewport.rect.width - 24), viewportHeight = scroll.viewport.rect.height;
            contentText = Text(content, "ImageCaption", 26);
            var (heading, parts) = ResearchCaptionParts.Split(Local(image.caption_th, image.caption_ja, image.caption_en));
            part = Mathf.Clamp(Saved.part, 1, parts.Length);
            // Heading repeated as context on every sub-page; rich text only when the reviewed text has no markup characters.
            contentText.text = heading == null ? parts[part - 1]
                : (heading.IndexOf('<') < 0 ? "<b>" + heading + "</b>" : heading) + "\n" + parts[part - 1];
            contentText.rectTransform.sizeDelta = new Vector2(width, 10000);
            contentText.ComputeMarginSize(); contentText.ForceMeshUpdate(true, true);
            float textHeight = Mathf.Max(48, contentText.preferredHeight + 12);
            // Capture C04: a fixed 75% image floor pushed 2-3 line captions below the viewport edge (looked clipped by the footer).
            // Reserve the measured caption first; only captions longer than ~45% of the viewport still need the scrollbar.
            float availableHeight = Mathf.Max(viewportHeight * .55f, viewportHeight - textHeight - 48);
            float scale = Mathf.Min(width / texture.width, availableHeight / texture.height);
            float imageWidth = texture.width * scale, imageHeight = texture.height * scale;
            var graphic = Rect(content, "SourceImage_" + index, new Vector2(imageWidth, imageHeight)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            graphic.texture = texture; graphic.raycastTarget = false;
            graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = new Vector2(.5f, 1); graphic.rectTransform.pivot = new Vector2(.5f, 1);
            graphic.rectTransform.anchoredPosition = new Vector2(0, -12);
            contentText.rectTransform.anchorMin = contentText.rectTransform.anchorMax = new Vector2(.5f, 1); contentText.rectTransform.pivot = new Vector2(.5f, 1);
            contentText.rectTransform.sizeDelta = new Vector2(width, textHeight); contentText.rectTransform.anchoredPosition = new Vector2(0, -imageHeight - 24);
            content.sizeDelta = new Vector2(0, Mathf.Max(viewportHeight, imageHeight + textHeight + 48));
            state = MediaState.Ready;
        }

        void RenderVideo()
        {
            EndReading("loading");
            state = MediaState.Loading;
            decodedFrames = 0; firstDecodedFrame = -1; awaitingPreview = false;
            statusLabel.text = Local("กำลังโหลดวิดีโอ", "動画を読み込み中", "Loading video"); statusLabel.gameObject.SetActive(true);
            videoLoadStarted = Time.realtimeSinceStartup; videoLoadPhase = "created"; videoStallShown = false;
            videoSurface = Rect(content, "VideoSurface", Vector2.zero);
            Stretch(videoSurface, new Vector2(12, 80), new Vector2(-12, -12));
            var frame = Rect(videoSurface, "VideoFrame", Vector2.zero); Stretch(frame, Vector2.zero, Vector2.zero);
            videoImage = frame.gameObject.AddComponent<UnityEngine.UI.RawImage>(); videoImage.raycastTarget = false;
            timeLabel = Text(content, "VideoTime", 24);
            timeLabel.alignment = TextAlignmentOptions.MidlineLeft; timeLabel.text = "--:-- / --:--"; timeLabel.color = Color.white;
            timeLabel.rectTransform.anchorMin = timeLabel.rectTransform.anchorMax = new Vector2(0, 0);
            timeLabel.rectTransform.sizeDelta = new Vector2(180, 48); timeLabel.rectTransform.anchoredPosition = new Vector2(278, 32);
            CreateTimeline();
            var commands = new[] { "play", "seek_back", "seek_forward" };
            bool scenes = package?.video?.chapters?.Length > 1;
            var labels = new[] { Local("เล่น", "再生", "Play"), scenes ? Local("ย้อน", "前へ", "Back") : "-10 s", scenes ? Local("ถัดไป", "次へ", "Next") : "+10 s" };
            for (int i = 0; i < commands.Length; i++)
            {
                var button = Control(content, "Video_" + commands[i], labels[i], "reference_media." + commands[i]);
                var rt = button.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(i == 0 ? 0 : 1, 0);
                rt.anchoredPosition = new Vector2(i == 0 ? 90 : i == 1 ? -116 : -44, 32); rt.sizeDelta = new Vector2(i == 0 ? 156 : 64, 64);
                var icon = Rect(rt, "TransportIcon", new Vector2(28, 28)).gameObject.AddComponent<ResearchUtilityIcon>();
                icon.kind = commands[i]; icon.color = ResearchUITheme.Ink; icon.raycastTarget = false;
                icon.rectTransform.anchoredPosition = new Vector2(i == 0 ? -48 : 0, i == 0 ? 0 : 12);
                var label = button.GetComponentInChildren<TMP_Text>(true); label.fontSize = i == 0 ? 20 : 16;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .5f);
                label.rectTransform.sizeDelta = new Vector2(i == 0 ? 104 : 64, 40); label.rectTransform.anchoredPosition = new Vector2(i == 0 ? 14 : 0, i == 0 ? 0 : -16);
                videoButtons.Add(button);
            }
            RefreshVideoControls();
            if (visible && isActiveAndEnabled && Application.isPlaying) prepare = StartCoroutine(PrepareVideo(generation));
        }

        IEnumerator PrepareVideo(int acquired)
        {
            string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + package.video.streaming_path;
#if UNITY_EDITOR
            // The DEMO fixture video is Editor-only so it never ships in a build.
            if (package.video.streaming_path.StartsWith("ResearchReaderDemo/", StringComparison.Ordinal))
                path = System.IO.Path.GetFullPath("Assets/VRMaintenanceResearch/ResearchFlow/Editor/ReaderDemoFixtures/" + package.video.streaming_path.Substring("ResearchReaderDemo/".Length));
            if (EditorCaptureVideoPath != null) path = EditorCaptureVideoPath(package.video.streaming_path) ?? path;
#endif
            string url = path.Contains("://") || path.StartsWith("jar:", StringComparison.Ordinal) ? path : new Uri(path).AbsoluteUri;
            string verifiedKey = url + "|" + package.video.sha256;
            if (!verifiedVideos.Contains(verifiedKey))
            {
                LoadPhase("reading");
                using (var request = UnityWebRequest.Get(url))
                {
                    request.timeout = 20;
                    yield return request.SendWebRequest();
                    if (!ResearchVideoLoadRules.AcceptAsync(acquired, generation, visible)) { if (acquired == generation) prepare = null; yield break; }
                    if (request.result != UnityWebRequest.Result.Success) { Fail("Video read failed: " + request.error); prepare = null; yield break; }
                    // 20-31 MB per clip: hash off the main thread so the headset keeps rendering while "reading" shows.
                    byte[] videoBytes = request.downloadHandler.data; string expectedHash = package.video.sha256;
                    var hashing = System.Threading.Tasks.Task.Run(() => HashMatches(videoBytes, expectedHash));
                    while (!hashing.IsCompleted) yield return null;
                    if (!ResearchVideoLoadRules.AcceptAsync(acquired, generation, visible)) { if (acquired == generation) prepare = null; yield break; }
                    if (hashing.IsFaulted || !hashing.Result) { Fail("Video SHA256 mismatch"); prepare = null; yield break; }
                }
                verifiedVideos.Add(verifiedKey);
                LoadPhase("verified");
            }
            else LoadPhase("verified_cached");
            if (!ResearchVideoLoadRules.AcceptAsync(acquired, generation, visible)) { if (acquired == generation) prepare = null; yield break; }
            LoadPhase("preparing");
            var host = new GameObject("ReaderVideoPlayer"); host.transform.SetParent(content, false);
            player = host.AddComponent<VideoPlayer>(); videoAudio = host.AddComponent<AudioSource>();
            videoAudio.playOnAwake = false; videoAudio.mute = true; videoAudio.spatialBlend = 0; videoAudio.volume = 1;
            player.playOnAwake = false; player.isLooping = false; player.waitForFirstFrame = true;
            player.sendFrameReadyEvents = true;
            player.source = VideoSource.Url; player.url = url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource; player.controlledAudioTrackCount = 1;
            player.SetTargetAudioSource(0, videoAudio);
            player.prepareCompleted += Prepared; player.errorReceived += VideoError; player.loopPointReached += Ended; player.seekCompleted += SeekCompleted; player.frameReady += FrameDecoded;
            player.Prepare();
            float started = Time.realtimeSinceStartup;
            while (acquired == generation && player && !player.isPrepared && state != MediaState.Error)
            {
                if (Time.realtimeSinceStartup - started > 20) { Fail("Video prepare timeout"); break; }
                yield return null;
            }
            prepare = null;
        }

        void Prepared(VideoPlayer source)
        {
            if (!source || !player || source != player || !visible || state != MediaState.Loading) return;
            source.Pause(); videoAudio.mute = true;
            LoadPhase("prepared");
            timeline.maxValue = (float)Math.Max(.01, source.length);
            float ratio = source.height > 0 ? (float)source.width / source.height : 16f / 9;
            int width = Mathf.Max(2, Mathf.Min(1280, (int)source.width));
            int height = Mathf.Max(2, Mathf.RoundToInt(width / ratio));
            if (height > 720) { height = 720; width = Mathf.Max(2, Mathf.RoundToInt(height * ratio)); }
            videoTexture = new RenderTexture(width, height, 0) { name = "ResearchReaderVideoFrame" }; videoTexture.Create(); source.targetTexture = videoTexture;
            videoImage.texture = videoTexture;
            var fit = videoImage.gameObject.GetComponent<AspectRatioFitter>(); if (!fit) fit = videoImage.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = ratio;
            Event("REFERENCE_VIDEO_PREPARED", "System");
            if (!source.canStep) { Fail("Native decoder cannot produce paused preview (canStep=false)"); return; }
            awaitingPreview = true; previewStarted = Time.realtimeSinceStartup;
            source.StepForward(); RefreshVideoControls();
        }
        void FrameDecoded(VideoPlayer source, long frame)
        {
            if (!source || !player || source != player || !visible || state == MediaState.Error || state == MediaState.Empty) return;
            decodedFrames++; if (firstDecodedFrame < 0) firstDecodedFrame = frame;
            if (awaitingPreview && state == MediaState.Loading)
            {
                awaitingPreview = false; source.Pause(); if (videoAudio) videoAudio.mute = true;
                state = MediaState.Ready; statusLabel.gameObject.SetActive(false);
                LoadPhase("first_frame");
                var stalledRetry = scroll.viewport.Find("ViewerRetry"); if (stalledRetry) stalledRetry.gameObject.SetActive(false);
                // Decide here: every reload after a hide (SetPresentation or a Bind refresh) reaches its first decoded frame.
                if (occludedSince >= 0 && !autoResumePending)
                {
                    autoResumePending = Time.realtimeSinceStartup - occludedSince <= AutoResumeSeconds;
                    resumeHint = !autoResumePending; if (!autoResumePending) occludedSince = -1;
                }
                bool seeking = Saved.time > .05 && source.canSetTime && Seek(Saved.time, "None", autoResumePending, "System");
                Event("REFERENCE_VIDEO_FIRST_FRAME_DECODED", "System");
                if (autoResumePending && !seeking && state == MediaState.Ready)
                {
                    videoAudio.mute = false; source.Play(); state = MediaState.PlayRequested; playStarted = Time.realtimeSinceStartup; AutoResumed();
                }
                if (resumeHint && !autoResumePending) { resumeHintShown = true; resumeHint = false; }
                if (state == MediaState.Ready) { SavePosition(); BeginReading(); RefreshVideoControls(); view.RefreshPageControls(); }
            }
            else if (state == MediaState.Seeking)
            {
                double tolerance = source.frameRate > 0 ? 2 / source.frameRate : .1;
                if (Math.Abs(source.time - seekTarget) <= tolerance) seekFrameSeen = true;
                CompleteSeek();
            }
        }
        void VideoError(VideoPlayer source, string message) { if (source && player && source == player && state != MediaState.Error) Fail("Video error: " + message); }
        void Ended(VideoPlayer source)
        {
            if (!source || !player || source != player || !visible) return;
            state = MediaState.Ended; source.Pause(); if (videoAudio) videoAudio.mute = true;
            SavePosition(); Event("REFERENCE_VIDEO_ENDED", "System"); RefreshVideoControls();
        }
        bool Seek(double target, string input, bool resume, string actor = null, bool force = false)
        {
            if (!visible || !player || !player.isPrepared || (!IsReady && !(force && state == MediaState.PlayRequested)) || !player.canSetTime || double.IsNaN(target) || double.IsInfinity(target)) return false;
            double length = player.length;
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0) return false;
            seekTarget = Math.Max(0, Math.Min(length - .01, target));
            // Re-seeking the already decoded start can omit native seekCompleted; forced Pause still pins its frame.
            if (!force && state != MediaState.Ended && seekTarget == 0 && !player.isPlaying && decodedFrames > 0 && player.frame >= 0 && player.frame <= 1
                && player.frameRate > 0 && player.time <= 1 / player.frameRate + .001)
            { RejectControl("seek", "already_at_decoded_start", input); return false; }
            if (!force && Math.Abs(seekTarget - player.time) < .01 && state != MediaState.Ended) return false;
            EndReading("seeking"); resumeAfterSeek = resume; player.Pause(); if (videoAudio) videoAudio.mute = true;
            state = MediaState.Seeking; seekStarted = Time.realtimeSinceStartup;
            seekCompletedSeen = seekFrameSeen = false;
            if (force && pauseInput != null) Event("REFERENCE_VIDEO_PAUSE_REQUESTED", Actor(pauseInput), pauseInput);
            player.time = seekTarget;
            Event("REFERENCE_VIDEO_SEEK_REQUESTED", actor ?? Actor(input), input); RefreshVideoControls(); return true;
        }
        public bool SeekTimelineSeconds(double target, string input = "None") => CanSeekTimeline && Seek(target, input, player.isPlaying);
        public string InputForPointer(int pointer) => view?.controller?.InputSourceForPointer(pointer) ?? "None";
        public void RejectControl(string control, string reason, string input)
        {
            view?.controller?.ReferenceMediaEvent("REFERENCE_VIEWER_INPUT_REJECTED", new { viewer = Metadata, control, reason }, "System", input, BoundSection);
        }
        void CreateTimeline()
        {
            // One transport row (Play, time, timeline, seek) leaves the rest of the viewer to the picture (Quest 16 Oct: video looked small).
            var rect = Rect(content, "VideoTimeline", Vector2.zero);
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(372, 0); rect.offsetMax = new Vector2(-156, 64);
            var background = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = Color.clear;
            timeline = rect.gameObject.AddComponent<ResearchReaderTimeline>(); timeline.viewer = this;
            timeline.direction = Slider.Direction.LeftToRight; timeline.minValue = 0; timeline.maxValue = 1;
            var track = Rect(rect, "Track", Vector2.zero); Stretch(track, new Vector2(24, 30), new Vector2(-24, -30));
            var trackImage = track.gameObject.AddComponent<UnityEngine.UI.Image>(); trackImage.color = ResearchUITheme.Muted; trackImage.raycastTarget = false;
            var fill = Rect(track, "Fill", Vector2.zero); Stretch(fill, Vector2.zero, Vector2.zero);
            var fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>(); fillImage.color = ResearchUITheme.Blue; fillImage.raycastTarget = false;
            var handleArea = Rect(rect, "HandleArea", Vector2.zero); Stretch(handleArea, new Vector2(24, 0), new Vector2(-24, 0));
            var handle = Rect(handleArea, "Handle", new Vector2(48, 0)); handle.anchorMin = handle.anchorMax = new Vector2(0, .5f);
            var hit = handle.gameObject.AddComponent<UnityEngine.UI.Image>(); hit.color = Color.clear;
            var thumb = Rect(handle, "VisibleThumb", new Vector2(20, 20)).gameObject.AddComponent<TrainingRoundedGraphic>();
            thumb.color = ResearchUITheme.Blue; thumb.Radius = 10; thumb.raycastTarget = false;
            timeline.fillRect = fill; timeline.handleRect = handle; timeline.targetGraphic = thumb;
        }
        void GuardScrollbar()
        {
            var existing = scroll.verticalScrollbar;
            if (!existing) return;
            guardedScrollbar = existing as ResearchReaderScrollbar;
            if (guardedScrollbar) { guardedScrollbar.viewer = this; return; }
            var original = existing.GetComponent<RectTransform>(); var handle = existing.handleRect; var target = existing.targetGraphic;
            var direction = existing.direction; var colors = existing.colors; var navigation = existing.navigation;
            var transition = existing.transition; var animation = existing.animationTriggers;
            float value = existing.value, size = existing.size; int steps = existing.numberOfSteps; bool interactable = existing.interactable;
            bool active = original.gameObject.activeSelf;
            scroll.verticalScrollbar = null; existing.enabled = false;
            var replacement = Rect(original.parent, original.name, original.sizeDelta);
            replacement.anchorMin = original.anchorMin; replacement.anchorMax = original.anchorMax; replacement.pivot = original.pivot;
            replacement.sizeDelta = original.sizeDelta; replacement.anchoredPosition3D = original.anchoredPosition3D;
            replacement.localRotation = original.localRotation; replacement.localScale = original.localScale;
            replacement.SetSiblingIndex(original.GetSiblingIndex()); replacement.gameObject.SetActive(active);
            while (original.childCount > 0) original.GetChild(0).SetParent(replacement, false);
            original.name += "_Retired"; original.gameObject.SetActive(false);
            var host = replacement.gameObject;
            guardedScrollbar = host.AddComponent<ResearchReaderScrollbar>(); guardedScrollbar.viewer = this;
            guardedScrollbar.handleRect = handle; guardedScrollbar.targetGraphic = target; guardedScrollbar.direction = direction;
            guardedScrollbar.colors = colors; guardedScrollbar.navigation = navigation; guardedScrollbar.transition = transition; guardedScrollbar.animationTriggers = animation;
            guardedScrollbar.size = size; guardedScrollbar.numberOfSteps = steps; guardedScrollbar.SetValueWithoutNotify(value); guardedScrollbar.interactable = interactable;
            scroll.verticalScrollbar = guardedScrollbar;
            ReleaseObject(original.gameObject);
        }
        void SeekCompleted(VideoPlayer source)
        {
            if (!source || !player || source != player || state != MediaState.Seeking) return;
            seekCompletedSeen = true;
            if (!seekFrameSeen)
            {
                if (!source.canStep) { Fail("Native decoder cannot preview seek result"); return; }
                source.StepForward();
            }
            CompleteSeek();
        }
        void CompleteSeek()
        {
            if (state != MediaState.Seeking || !player || !seekCompletedSeen || !seekFrameSeen) return;
            var source = player;
            bool play = resumeAfterSeek && visible; resumeAfterSeek = false;
            state = play ? MediaState.PlayRequested : MediaState.Paused;
            if (play) { videoAudio.mute = false; source.Play(); playStarted = Time.realtimeSinceStartup; }
            else { source.Pause(); videoAudio.mute = true; }
            SavePosition(); Event("REFERENCE_VIDEO_SEEK_COMPLETED", "System");
            if (play && autoResumePending) AutoResumed(); else autoResumePending = false;
            if (pauseInput != null) { var input = pauseInput; pauseInput = null; Event("REFERENCE_VIDEO_PAUSED", Actor(input), input); }
            BeginReading(); RefreshVideoControls();
            view.RefreshPageControls();
        }

        // Any participant command or rebind owns playback from here: drop a pending system resume and the resume hint.
        void CancelAutoResume()
        {
            autoResumePending = resumeHint = resumeHintShown = false; occludedSince = -1;
            if (statusLabel && statusLabel.gameObject.activeSelf && IsReady) statusLabel.gameObject.SetActive(false);
        }

        // System resume: not a participant Play decision (no PLAY_REQUESTED, source System).
        void AutoResumed()
        {
            autoResumePending = false;
            view?.controller?.ReferenceMediaEvent("REFERENCE_VIDEO_AUTO_RESUMED", new { viewer = Metadata, paused_at_s = Math.Round(occludedAt, 3),
                hidden_seconds = Math.Round(Time.realtimeSinceStartup - occludedSince, 3), hide_cause = occludedCause,
                window_seconds = AutoResumeSeconds, version = VideoResumeVersion }, "System", "None", BoundSection);
            occludedSince = -1;
        }

        bool CanSkipVideo => CanSeekTimeline && player.length > 20;
        // Owner 29 Oct: a 2-3 minute narrated video needs scene jumps, not 10 s steps. Back restarts the current scene,
        // or goes to the previous one when already near its start; videos without chapters keep 10 s steps.
        public static double SkipTarget(double time, Chapter[] chapters, int direction)
        {
            if (chapters == null || chapters.Length < 2) return time + 10 * direction;
            var starts = chapters.Select(c => c.start_seconds).OrderBy(s => s).ToArray();
            if (direction > 0) return starts.Where(s => s > time + .25).DefaultIfEmpty(time + 10).First();
            return starts.Where(s => s < time - 1.5).DefaultIfEmpty(0).Last();
        }
        static bool ValidDuration(double seconds) => seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
        static string DisplayTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--:--";
            int total = (int)Math.Min(int.MaxValue, Math.Floor(seconds));
            return (total / 60).ToString(CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        void RefreshVideoControls()
        {
            bool playing = state == MediaState.PlayRequested || state == MediaState.Playing;
            if (statusLabel && state == MediaState.Seeking)
            { statusLabel.text = Local("กำลังเลื่อนวิดีโอ", "動画の移動中", "Seeking video"); statusLabel.gameObject.SetActive(true); }
            else if (statusLabel && format == "video" && IsReady)
            {
                // Hidden past the resume window: keep saying how to continue until the participant acts (amendment 8 Nov, section 3).
                if (resumeHintShown) statusLabel.text = Local("วิดีโอหยุดอยู่ กด ▶ เพื่อดูต่อ", "動画は一時停止中です。▶ で続きを再生", "Video paused. Press ▶ to continue.");
                statusLabel.gameObject.SetActive(resumeHintShown);
            }
            for (int i = 0; i < videoButtons.Count; i++)
            {
                var button = videoButtons[i];
                if (i == 0)
                {
                    button.gameObject.SetActive(state != MediaState.Error);
                    button.GetComponent<ResearchUIAction>().command = playing ? "reference_media.pause" : "reference_media.play";
                    button.GetComponentInChildren<TMP_Text>(true).text = playing ? Local("พัก", "一時停止", "Pause") : state == MediaState.Ended ? Local("เล่นซ้ำ", "再再生", "Replay") : Local("เล่น", "再生", "Play");
                    var icon = button.GetComponentInChildren<ResearchUtilityIcon>(); icon.kind = playing ? "pause" : state == MediaState.Ended ? "replay" : "play"; icon.SetVerticesDirty();
                    button.interactable = visible && player && player.isPrepared && (playing || IsReady);
                }
                else { button.gameObject.SetActive(CanSkipVideo); button.interactable = CanSkipVideo; }
            }
            if (timeLabel) timeLabel.gameObject.SetActive(state != MediaState.Error);
            if (timeline) { timeline.gameObject.SetActive(state != MediaState.Error); timeline.interactable = CanSeekTimeline; }
        }
        void RefreshScrollbar()
        {
            RefreshPdfControls(); RenderContents();
            if (!guardedScrollbar) return;
            bool show = CanScrollDocument;
            bool wasVertical = scroll.vertical; scroll.vertical = show;
            if (show && !wasVertical)
            { var bar = scroll.verticalScrollbar; scroll.verticalScrollbar = null; scroll.verticalScrollbar = bar; }
            if (!show) { scroll.StopMovement(); guardedScrollbar.Cancel("no_scroll_range"); }
            guardedScrollbar.interactable = show; guardedScrollbar.gameObject.SetActive(show);
        }
        void Update()
        {
            if (!scroll || !visible) return;
            float width = scroll.viewport.rect.width, height = scroll.viewport.rect.height;
            if (package != null && format != "video" && (Mathf.Abs(width - lastWidth) > 1 || Mathf.Abs(height - lastHeight) > 1))
            {
                lastWidth = width; lastHeight = height; SavePosition(); bool wasContents = contentsOpen; ClearContent(); contentsOpen = wasContents; Render();
            }
            if (player && player.isPrepared && timeLabel)
                timeLabel.text = DisplayTime(TimelineHeld ? timeline.value : player.time) + " / " + (ValidDuration(player.length) ? DisplayTime(player.length) : "--:--");
            if (timeline && player && player.isPrepared && !TimelineHeld) timeline.SetValueWithoutNotify((float)player.time);
            if (state == MediaState.PlayRequested && player && player.isPlaying)
            { state = MediaState.Playing; BeginReading(); Event("REFERENCE_VIDEO_PLAYING", "System"); RefreshVideoControls(); }
            if (state == MediaState.PlayRequested && Time.realtimeSinceStartup - playStarted > 5) Fail("Video play timeout");
            if (state == MediaState.Seeking && Time.realtimeSinceStartup - seekStarted > 5) Fail("Video seek timeout");
            if (awaitingPreview && state == MediaState.Loading && Time.realtimeSinceStartup - previewStarted > 5) Fail("Native first decoded frame timeout");
            if (ResearchVideoLoadRules.StallDue(format == "video", visible, state == MediaState.Loading, videoStallShown, Time.realtimeSinceStartup - videoLoadStarted))
            {
                videoStallShown = true;
                statusLabel.text = Local("วิดีโอยังโหลดไม่เสร็จ รอต่อได้ หรือเลือก “ลองอีกครั้ง”", "動画の読み込みに時間がかかっています。待つか「再試行」を選んでください。",
                    "The video is still loading. Wait, or choose Retry.");
                // Above the timeline/time label, which stay visible while loading continues.
                statusLabel.gameObject.SetActive(true); ShowRetry(196);
                view?.controller?.ReferenceMediaEvent("REFERENCE_VIDEO_LOAD_STALLED", new { viewer = Metadata, phase = videoLoadPhase,
                    elapsed_seconds = Time.realtimeSinceStartup - videoLoadStarted }, "System", boundSection: BoundSection);
            }
            if (format == "video" && visible && state == MediaState.Loading && prepare == null && !player && isActiveAndEnabled && Application.isPlaying)
                prepare = StartCoroutine(PrepareVideo(generation));
            // Video readiness changes outside Render (first decoded frame, seek, error): keep the contents control in step.
            RefreshStepControls();
            string contentsNow = ContentsAvailable + "/" + (view && view.controller && view.controller.LocalUICommandsAllowed);
            if (contentsNow != contentsState) { contentsState = contentsNow; RenderContents(); }
            // One event per change of the topic in view (scroll, step/image change, playback or seek), with section_id in Metadata.
            if (readable && IsReady && !ContentsOpen && entries.Length > 0 && SectionId != shownSection)
            {
                shownSection = SectionId; Event("REFERENCE_VIEWER_SECTION_VISIBLE", "System");
                if (ContentsOpen) RenderContents();
                view.RefreshPageControls();
            }
        }
        void OnScroll(Vector2 position)
        {
            if (suppressScroll || !visible || !IsReady) return;
            SavePosition(); if (format == "pdf") UpdatePages("System", "None");
            if (ZoomableImage && !ContentsOpen) Event("REFERENCE_IMAGE_VIEW_CHANGED", "System", "None");
            view.RefreshPageControls();
        }
        internal void RecordScroll(string input)
        {
            if (suppressScroll || !visible || !IsReady) return;
            if (ContentsOpen) { Event("REFERENCE_CONTENTS_SCROLLED", Actor(input), input); return; }
            SavePosition(); if (format == "pdf") UpdatePages(Actor(input), input);
            if (ZoomableImage) Event("REFERENCE_IMAGE_VIEW_CHANGED", Actor(input), input);
            view.RefreshPageControls();
        }
        void SavePosition()
        {
            if (key == null) return;
            var position = Saved; position.index = index; if (format == "images") position.part = part;
            if (scroll) position.normalized = scroll.verticalNormalizedPosition;
            if (scroll && (format == "pdf" || ZoomableImage)) position.horizontal = position.zoom == 1 ? .5f : scroll.horizontalNormalizedPosition;
            if (player && player.isPrepared && !double.IsNaN(player.time)) position.time = player.time;
        }
        void BeginReading()
        {
            if (readingSince >= 0 || !readable || !IsReady || ContentsOpen) return;
            readingSince = Time.realtimeSinceStartup; Event("REFERENCE_VIEWER_ACTIVE_BEGIN", "System");
        }
        void EndReading(string reason)
        {
            if (readingSince < 0) return;
            view?.controller?.ReferenceMediaEvent("REFERENCE_VIEWER_ACTIVE_END", new { viewer = Metadata, active_seconds = Math.Max(0, Time.realtimeSinceStartup - readingSince), reason }, "System", boundSection: BoundSection);
            readingSince = -1;
        }
        void Event(string name, string actor, string input = "None") => view?.controller?.ReferenceMediaEvent(name, Metadata, actor, input, BoundSection);
        string Actor(string input) => input == "None" && view && view.controller && view.controller.IsTechnical ? "Tester" : "User";
        string Local(string th, string ja, string en) => view && view.controller && view.controller.Language == "Japanese" ? ja : view && view.controller && view.controller.Language == "English" ? en : th;

        void ShowText(string text)
        {
            contentText = Text(content, "ReaderText", 30); contentText.text = text;
            float width = Mathf.Max(1, scroll.viewport.rect.width - 40);
            contentText.rectTransform.anchorMin = contentText.rectTransform.anchorMax = new Vector2(.5f, 1);
            contentText.rectTransform.pivot = new Vector2(.5f, 1); contentText.rectTransform.sizeDelta = new Vector2(width, 10000);
            contentText.ForceMeshUpdate(true, true);
            float height = Mathf.Max(scroll.viewport.rect.height - 24, contentText.preferredHeight + 24);
            contentText.rectTransform.sizeDelta = new Vector2(width, height);
            contentText.rectTransform.anchoredPosition = new Vector2(0, -12); content.sizeDelta = new Vector2(0, height + 24);
        }
        void Fail(string message)
        {
            EndReading("error"); error = message ?? "Unknown media error"; state = MediaState.Error;
            if (player) player.Pause(); if (videoAudio) videoAudio.mute = true;
            if (videoImage) videoImage.enabled = false;
            statusLabel.text = Local("อ่านสื่อไม่ได้: ", "メディアを読み込めません: ", "Media error: ") + error;
            statusLabel.gameObject.SetActive(true);
            RefreshVideoControls(); RefreshScrollbar(); Event("REFERENCE_VIEWER_ERROR", "System");
            ShowRetry();
        }
        void LoadPhase(string phase)
        {
            videoLoadPhase = phase;
            view?.controller?.ReferenceMediaEvent("REFERENCE_VIDEO_LOAD_PHASE", new { viewer = Metadata, phase,
                elapsed_ms = Math.Round((Time.realtimeSinceStartup - videoLoadStarted) * 1000) }, "System", boundSection: BoundSection);
        }
        void ShowRetry(float bottom = 52)
        {
            var existing = scroll.viewport.Find("ViewerRetry");
            if (!existing)
            {
                var retry = Control(scroll.viewport, "ViewerRetry", Local("ลองอีกครั้ง", "再試行", "Retry"), "reference_media.retry");
                existing = retry.transform;
            }
            else
            {
                var label = existing.GetComponentInChildren<TMP_Text>(true);
                label.font = view.Font; label.text = Local("ลองอีกครั้ง", "再試行", "Retry");
                existing.gameObject.SetActive(true);
            }
            var rect = existing.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, bottom); rect.sizeDelta = new Vector2(240, 72);
        }
        void CancelVideo()
        {
            pauseInput = null;
            if (timeline) timeline.Cancel("video_cancelled");
            if (prepare != null) { StopCoroutine(prepare); prepare = null; }
            if (player)
            {
                player.prepareCompleted -= Prepared; player.errorReceived -= VideoError; player.loopPointReached -= Ended; player.seekCompleted -= SeekCompleted; player.frameReady -= FrameDecoded;
                player.Stop(); if (videoAudio) { videoAudio.Stop(); videoAudio.mute = true; }
                ReleaseObject(player.gameObject); player = null; videoAudio = null;
            }
            if (videoTexture) { videoTexture.Release(); ReleaseObject(videoTexture); videoTexture = null; }
            resumeAfterSeek = false;
            awaitingPreview = seekCompletedSeen = seekFrameSeen = false;
        }
        void ClearContent()
        {
            if (pdfControls) pdfControls.gameObject.SetActive(false);
            if (allStepsButton) allStepsButton.gameObject.SetActive(false);
            infographic = null; stepCards.Clear();
            contentsOpen = false; if (contentsPanel) contentsPanel.gameObject.SetActive(false);
            foreach (var texture in pageCache.Values) ReleaseObject(texture);
            pageCache.Clear(); pageImages.Clear(); pageTops.Clear(); videoButtons.Clear();
            if (!content) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (template && child == template.transform) continue;
                child.gameObject.SetActive(false); ReleaseObject(child.gameObject);
            }
            var retry = scroll.viewport.Find("ViewerRetry"); if (retry) retry.gameObject.SetActive(false);
            contentText = timeLabel = null; videoImage = null;
            timeline = null;
        }
        void OnEnable() { appFocused = true; appPaused = false; }
        void OnApplicationFocus(bool focused) { appFocused = focused; if (!focused) SetVisible(false); }
        void OnApplicationPause(bool paused) { appPaused = paused; if (paused) SetVisible(false); else if (!Application.isEditor) appFocused = true; }
        void OnDisable() { SetVisible(false); }
        void OnDestroy()
        {
            SavePosition(); EndReading("destroyed"); CancelVideo();
            foreach (var texture in pageCache.Values) ReleaseObject(texture); pageCache.Clear();
            if (scroll) scroll.onValueChanged.RemoveListener(OnScroll);
        }

        Texture2D Decode(string resource, string expected)
        {
            byte[] bytes = null;
#if UNITY_EDITOR
            if (EditorCaptureResource != null) bytes = EditorCaptureResource(resource);
#endif
            if (bytes == null)
            {
                var asset = Resources.Load<TextAsset>(resource);
                if (!asset) { Fail("Missing PNG bytes: " + resource); return null; }
                bytes = asset.bytes; Resources.UnloadAsset(asset);
            }
            if (!HashMatches(bytes, expected)) { Fail("PNG SHA256 mismatch: " + resource); return null; }
            // Pages and illustrations draw at roughly half their pixel size; without mips thin strokes shimmer in the headset.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = resource, filterMode = FilterMode.Trilinear, anisoLevel = 2, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, bytes, true)) { ReleaseObject(texture); Fail("PNG decode failed: " + resource); return null; }
            return texture;
        }
        // Hashing every page and image of a package made the first Reader open of a task the longest frame of the session
        // (0.13-0.26 s in the Editor, 10 Nov), at the 0.25 s movement-sample limit of the export QC.
        public static void Warm(string resource) { if (SafeResource(resource)) LoadRegistry(resource); }
        static Registry LoadRegistry(string resource)
        {
            if (registries.TryGetValue(resource, out var cached)) return cached;
            var registry = new Registry(); registries[resource] = registry;
            var asset = Resources.Load<TextAsset>(resource);
            if (!asset) { registry.error = "Missing content registry"; return registry; }
            try
            {
                registry.hash = Hash(asset.bytes); var parsed = JsonUtility.FromJson<Package>(asset.text);
                string failure = ValidatePackage(parsed);
                if (failure != null) { registry.error = failure; return registry; }
                string source = string.IsNullOrWhiteSpace(parsed.source_pdf_resource) ? "ResearchReaderDemo/source_pdf" : parsed.source_pdf_resource;
                if (!ResourceHash(source, parsed.source_sha256)) { registry.error = "Original PDF missing or SHA256 mismatch"; return registry; }
                foreach (var page in parsed.pages) if (!ResourceHash(page.resource, page.sha256)) { registry.error = "PDF page missing or SHA256 mismatch: " + page.page; return registry; }
                foreach (var image in parsed.images) if (!ResourceHash(image.resource, image.sha256)) { registry.error = "Image missing or SHA256 mismatch: " + image.resource; return registry; }
                registry.package = parsed; return registry;
            }
            catch (Exception exception) { registry.error = "Registry error: " + exception.GetType().Name; return registry; }
            finally { Resources.UnloadAsset(asset); }
        }
        // JsonUtility can materialize an omitted nested object as an empty instance.
        // Empty optional pictures are text-only steps; partially specified pictures remain invalid.
        static bool HasStepPictureMetadata(Picture picture) => picture != null
            && (!string.IsNullOrEmpty(picture.resource) || !string.IsNullOrEmpty(picture.sha256)
                || !string.IsNullOrEmpty(picture.caption_th) || !string.IsNullOrEmpty(picture.caption_ja)
                || !string.IsNullOrEmpty(picture.caption_en));

        public static string ValidatePackage(Package value)
        {
            if (value == null || value.schema_version != 1 || (value.approval != DemoApproval && value.approval != S1DraftApproval && value.approval != DevPrototypeApproval) || string.IsNullOrWhiteSpace(value.content_id) || string.IsNullOrWhiteSpace(value.version)
                || string.IsNullOrWhiteSpace(value.source_pdf) || !IsHash(value.source_sha256)) return "Invalid registry identity, schema, provenance or approval";
            if (value.pages == null || value.pages.Length == 0 || value.steps == null || value.steps.Length == 0 || value.images == null || value.images.Length == 0 || value.video == null) return "Registry must contain all four formats";
            if (!string.IsNullOrWhiteSpace(value.source_pdf_resource) && !SafeResource(value.source_pdf_resource)) return "Invalid original PDF resource";
            for (int i = 0; i < value.pages.Length; i++)
            {
                var page = value.pages[i];
                if (page == null || page.page != i + 1 || page.width <= 0 || page.height <= 0 || float.IsNaN(page.width) || float.IsNaN(page.height)
                    || float.IsInfinity(page.width) || float.IsInfinity(page.height) || !SafeResource(page.resource) || !IsHash(page.sha256)) return "Invalid PDF page metadata";
            }
            foreach (var step in value.steps) if (step == null || (string.IsNullOrWhiteSpace(step.text_th) && string.IsNullOrWhiteSpace(step.text_ja) && string.IsNullOrWhiteSpace(step.text_en))) return "Empty step";
            foreach (var step in value.steps) if (HasStepPictureMetadata(step.picture) && (!SafeResource(step.picture.resource) || !IsHash(step.picture.sha256))) return "Invalid step image metadata";
            foreach (var image in value.images) if (image == null || !SafeResource(image.resource) || !IsHash(image.sha256)) return "Invalid image metadata";
            bool stepsFeature = value.reader_features != null && Array.IndexOf(value.reader_features, "steps_view_all") >= 0;
            bool imageFeature = value.reader_features != null && Array.IndexOf(value.reader_features, "image_viewport") >= 0;
            if (stepsFeature)
            {
                var ids = new HashSet<string>();
                foreach (var step in value.steps)
                    if (string.IsNullOrWhiteSpace(step.step_id) || !ids.Add(step.step_id) || string.IsNullOrWhiteSpace(step.topic_id)
                        || !HasStepPictureMetadata(step.picture)) return "Pictured steps require unique step_id, topic_id and picture";
                if (!(value.contents ?? new Entry[0]).Any(e => e != null && Normalize(e.format) == "steps")) return "Pictured steps require contents";
            }
            if (imageFeature)
            {
                var ids = new HashSet<string>();
                foreach (var image in value.images)
                {
                    if (string.IsNullOrWhiteSpace(image.id) || !ids.Add(image.id)) return "Infographics require unique image ids";
                    var regionIds = new HashSet<string>();
                    foreach (var region in image.regions ?? new Region[0])
                        if (region == null || string.IsNullOrWhiteSpace(region.id) || !regionIds.Add(region.id)
                            || !ValidRegion(region.x, region.y, region.width, region.height)) return "Invalid infographic region";
                }
                if (!(value.contents ?? new Entry[0]).Any(e => e != null && Normalize(e.format) == "images")) return "Infographics require contents";
            }
            string movie = value.video.streaming_path;
            if (string.IsNullOrWhiteSpace(movie) || !ResourceRoots.Any(root => movie.StartsWith(root, StringComparison.Ordinal)) || movie.Contains("..") || movie.Contains(":") || movie.Contains("\\") || !IsHash(value.video.sha256)) return "Invalid video path or SHA256";
            // The prototype approval is valid only under its own root, so it can never stand in for a participant package.
            if ((value.approval == DevPrototypeApproval) != (value.source_pdf_resource ?? "").StartsWith("ResearchReaderS1Dev/", StringComparison.Ordinal)) return "DEV prototype approval outside the DEV root";
            foreach (var entry in value.contents ?? new Entry[0])
            {
                string kind = Normalize(entry?.format);
                int count = kind == "pdf" ? value.pages.Length : kind == "steps" ? value.steps.Length : kind == "images" ? value.images.Length : kind == "video" ? 1 : 0;
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.id.IndexOfAny(new[] { ' ', '.', ':', '/' }) >= 0 || count == 0
                    || (kind != "video" && (entry.index < 1 || entry.index > count)) || entry.offset < 0 || entry.offset > 1 || entry.start_seconds < 0
                    || (string.IsNullOrWhiteSpace(entry.title_th) && string.IsNullOrWhiteSpace(entry.title_ja) && string.IsNullOrWhiteSpace(entry.title_en))) return "Invalid contents entry";
            }
            return null;
        }
        public static bool ValidRegion(float x, float y, float width, float height) =>
            !float.IsNaN(x) && !float.IsNaN(y) && !float.IsNaN(width) && !float.IsNaN(height)
            && x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= 1.00001f && y + height <= 1.00001f;

        static bool ResourceHash(string resource, string expected)
        {
            var asset = Resources.Load<TextAsset>(resource); if (!asset) return false;
            bool matches = HashMatches(asset.bytes, expected); Resources.UnloadAsset(asset); return matches;
        }
        static bool SafeResource(string resource) => !string.IsNullOrWhiteSpace(resource) && ResourceRoots.Any(root => resource.StartsWith(root, StringComparison.Ordinal)) && !resource.Contains("..") && !resource.Contains(":") && !resource.Contains("\\");
        public static bool HashMatches(byte[] bytes, string expected) => bytes != null && IsHash(expected) && string.Equals(Hash(bytes), expected, StringComparison.OrdinalIgnoreCase);
        static bool IsHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            { var output = new StringBuilder(64); foreach (byte b in sha.ComputeHash(bytes)) output.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return output.ToString(); }
        }
        static string Normalize(string value) { switch ((value ?? "").Trim().ToLowerInvariant()) { case "user_manual": return "pdf"; case "step_by_step": return "steps"; case "illustrated": return "images"; default: return (value ?? "").Trim().ToLowerInvariant(); } }
        TMP_Text Text(Transform parent, string name, float fontSize)
        {
            var rect = Rect(parent, name, new Vector2(500, 100));
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font = view && view.Font ? view.Font : template.font;
            label.fontSize = fontSize; label.color = ResearchUITheme.Ink; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.TopLeft; label.margin = new Vector4(4, 4, 4, 4); return label;
        }
        UnityEngine.UI.Button Control(Transform parent, string name, string label, string command)
        {
            var rect = Rect(parent, name, new Vector2(180, 64));
            var graphic = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); graphic.color = ResearchUITheme.Elevated;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = graphic;
            var text = Text(rect, "Label", 24); text.text = label; text.alignment = TextAlignmentOptions.Center; Stretch(text.rectTransform, new Vector2(6, 4), new Vector2(-6, -4));
            var action = rect.gameObject.AddComponent<ResearchUIAction>(); action.view = view; action.command = command;
            ResearchUITheme.Button(button, ResearchButtonVariant.Secondary);
            var colors = button.colors; colors.disabledColor = new Color(.92f, .95f, 1f, .85f); button.colors = colors; return button;
        }
        static RectTransform Rect(Transform parent, string name, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
        }
        static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
        static void ReleaseObject(UnityEngine.Object item)
        { if (!item) return; if (Application.isPlaying) Destroy(item); else DestroyImmediate(item); }
    }

    // Swallows drags and wheel input on the contents list so the document hidden beneath does not scroll.
    public sealed class ResearchReaderContentsSurface : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public void OnBeginDrag(PointerEventData data) { }
        public void OnDrag(PointerEventData data) { }
        public void OnEndDrag(PointerEventData data) { }
        public void OnScroll(PointerEventData data) { }
    }

    public sealed class ResearchReaderTimeline : Slider
    {
        public ResearchReferenceViewer viewer;
        int pointer = int.MinValue;
        string token, input;
        public bool Held => pointer != int.MinValue;
        bool Valid => viewer && viewer.CanSeekTimeline && IsActive() && IsInteractable() && token == viewer.SemanticToken && input == viewer.InputForPointer(pointer);
        public override void OnPointerDown(PointerEventData data)
        {
            if (data == null || data.button != PointerEventData.InputButton.Left || Held || !viewer || !viewer.CanSeekTimeline || !IsActive() || !IsInteractable()) return;
            pointer = data.pointerId; token = viewer.SemanticToken; input = viewer.InputForPointer(pointer);
            base.OnPointerDown(data);
        }
        public override void OnDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel("stale_drag"); return; }
            base.OnDrag(data);
        }
        public override void OnPointerUp(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            bool accepted = Valid; float target = value; string source = input;
            base.OnPointerUp(data); pointer = int.MinValue; token = input = null;
            if (accepted) viewer.SeekTimelineSeconds(target, source);
            else viewer?.RejectControl("timeline", "stale_release", source);
        }
        public override void OnMove(AxisEventData data)
        {
            if (!viewer || !viewer.CanSeekTimeline || Held) return;
            float before = value; base.OnMove(data);
            if (!Mathf.Approximately(before, value)) viewer.SeekTimelineSeconds(value);
        }
        protected override void Update() { if (Held && !Valid) Cancel("context_changed"); base.Update(); }
        public void Cancel(string reason)
        {
            if (!Held) return;
            string source = input; pointer = int.MinValue; token = input = null;
            InstantClearState();
            viewer?.RejectControl("timeline", reason, source);
        }
        protected override void OnDisable() { Cancel("disabled"); base.OnDisable(); }
    }

    public sealed class ResearchReaderScrollbar : Scrollbar
    {
        public ResearchReferenceViewer viewer;
        int pointer = int.MinValue;
        string token, input;
        public bool Held => pointer != int.MinValue;
        public string Input => input ?? "None";
        bool Valid => viewer && viewer.CanScrollDocument && IsActive() && IsInteractable() && token == viewer.BindingToken && input == viewer.InputForPointer(pointer);
        public override void OnPointerDown(PointerEventData data)
        {
            if (data == null || data.button != PointerEventData.InputButton.Left || Held || !viewer || !viewer.CanScrollDocument || !IsActive() || !IsInteractable()) return;
            pointer = data.pointerId; token = viewer.BindingToken; input = viewer.InputForPointer(pointer);
            float before = value; base.OnPointerDown(data);
            if (!Mathf.Approximately(before, value)) viewer.RecordScroll(Input);
        }
        public override void OnBeginDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel("stale_drag"); return; }
            base.OnBeginDrag(data);
        }
        public override void OnDrag(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) { Cancel("stale_drag"); return; }
            float before = value; base.OnDrag(data);
            if (!Mathf.Approximately(before, value)) viewer.RecordScroll(Input);
        }
        public override void OnPointerUp(PointerEventData data)
        {
            if (data == null || data.pointerId != pointer) return;
            if (!Valid) viewer?.RejectControl("scrollbar", "stale_release", Input);
            base.OnPointerUp(data); StopAllCoroutines(); pointer = int.MinValue; token = input = null;
        }
        protected override void Update() { if (Held && !Valid) Cancel("context_changed"); base.Update(); }
        public void Cancel(string reason)
        {
            if (!Held) return;
            string source = Input; StopAllCoroutines(); pointer = int.MinValue; token = input = null;
            InstantClearState();
            viewer?.RejectControl("scrollbar", reason, source);
        }
        protected override void OnDisable() { Cancel("disabled"); StopAllCoroutines(); base.OnDisable(); }
    }
}
