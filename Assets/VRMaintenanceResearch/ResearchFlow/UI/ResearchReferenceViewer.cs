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
        [Serializable] public sealed class Step { public string text_th, text_ja, text_en; }
        [Serializable] public sealed class Picture { public string resource, sha256, caption_th, caption_ja, caption_en; }
        [Serializable] public sealed class Movie { public string streaming_path, sha256; }
        [Serializable] public sealed class Package
        {
            public int schema_version;
            public string content_id, version, source_sha256, source_pdf, source_pdf_resource, approval;
            public Page[] pages; public Step[] steps; public Picture[] images; public Movie video;
        }
        enum MediaState { Empty, Blocked, Loading, Ready, PlayRequested, Playing, Paused, Seeking, Ended, Error, AwaitingSelection }
        sealed class Position { public int index = 1, part = 1; public float normalized = 1, zoom = 1, horizontal = .5f; public double time; }
        sealed class Registry { public Package package; public string hash, error; }

        const string PackageResource = "ResearchReaderDemo/package";
        const string DemoApproval = "DEMO_TEST_NOT_STUDY";
        // S1 engineering draft: technical/test sessions may read it; study sessions stay gated by the task port's contentApproved.
        const string S1DraftApproval = "S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW";
        static readonly string[] ResourceRoots = { "ResearchReaderS1/", "ResearchReaderDemo/" };

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
        ScrollRect scroll;
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
        bool visible, demoAllowed, suppressScroll, resumeAfterSeek, appFocused = true, appPaused, awaitingPreview, seekCompletedSeen, seekFrameSeen;
        int generation, index = 1;
        // Capture C02: video loading must never sit at "Loading" with no way out. After StallSeconds the Retry control is offered
        // while loading continues; each phase is logged so Android prepare/first-frame stalls can be diagnosed per build.
        float videoLoadStarted; string videoLoadPhase = ""; bool videoStallShown;
        // SHA256 of a StreamingAssets video is verified once per app run; re-reading ~20 MB on every format switch only added GC/IO.
        static readonly HashSet<string> verifiedVideos = new HashSet<string>(StringComparer.Ordinal);
        long decodedFrames, firstDecodedFrame = -1;
        double seekTarget;
        float seekStarted, playStarted, previewStarted, lastWidth, lastHeight, readingSince = -1;

        public bool IsReady => state == MediaState.Ready || state == MediaState.Playing || state == MediaState.Paused || state == MediaState.Ended;
        public string Status => state.ToString();
        public int PageIndex => index;
        public int PageCount => package == null ? 1 : format == "pdf" ? package.pages.Length : format == "steps" ? package.steps.Length : format == "images" ? package.images.Length : 1;
        public bool CanPrevious => IsReady && format != "video" && (index > 1 || (format == "images" && part > 1));
        public bool CanNext => IsReady && format != "video" && (index < PageCount || (format == "images" && part < PartCount));
        // Illustrated sub-pages (ResearchCaptionParts): image index keeps its meaning in logs; caption_part is reported separately.
        int part = 1;
        int PartCount => format == "images" && package != null && index >= 1 && index <= package.images.Length ? ResearchCaptionParts.Count(CaptionOf(index)) : 1;
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
        public bool CanScrollDocument => visible && IsReady && format != "video" && content.rect.height > scroll.viewport.rect.height + 1 && view && view.controller && view.controller.LocalUICommandsAllowed;
        public bool CanSeekTimeline => visible && IsReady && player && player.isPrepared && player.canSetTime && ValidDuration(player.length) && view && view.controller && view.controller.LocalUICommandsAllowed;
        public VideoPlayer NativeVideoPlayer => player;
        public long DecodedFrameCount => decodedFrames;
        public long FirstDecodedFrame => firstDecodedFrame;
        public string SemanticToken => generation + ":" + key + ":" + format + ":" + index + ":" + state;
        public string Title => state == MediaState.AwaitingSelection ? Local("เลือกรูปแบบสื่อ", "資料の形式を選択", "Choose a format") : format == "pdf" ? "PDF" : format == "steps" ? Local("ขั้นตอน", "手順", "Steps") : format == "images" ? Local("รูปภาพ", "画像", "Images") : "Video";
        public string DocumentName => package == null ? Title : System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(package.source_pdf).Replace('_', ' '), "([a-z])([A-Z])", "$1 $2");
        public bool NavigationVisible => IsReady && (format == "pdf" || ((format == "steps" || format == "images") && (PageCount > 1 || PartCount > 1)));
        public string NavigationLabel => format == "pdf" ? index + " / " + PageCount : format == "steps"
            ? Local("ขั้นที่ " + index + " จาก " + PageCount, "手順 " + index + " / " + PageCount, "Step " + index + " of " + PageCount)
            : Local("ภาพ " + index + " / " + PageCount, "画像 " + index + " / " + PageCount, "Image " + index + " of " + PageCount)
                + (PartCount > 1 ? " · " + part + "/" + PartCount : "");
        public object Metadata => new
        {
            event_schema = "viewer_v1", context = key, content_id = package?.content_id, version = package?.version,
            source_pdf = package?.source_pdf, source_sha256 = package?.source_sha256, registry_sha256 = packageHash,
            approval = package?.approval, media_type = format, page = format == "pdf" ? index : (int?)null,
            index, caption_part = format == "images" ? part : (int?)null, caption_parts = format == "images" ? PartCount : (int?)null,
            pdf_zoom = format == "pdf" ? Saved.zoom : (float?)null, pdf_horizontal = format == "pdf" && scroll ? scroll.horizontalNormalizedPosition : (float?)null,
            caption_layout = format == "images" ? ResearchCaptionParts.Version : null, normalized_offset = scroll ? scroll.verticalNormalizedPosition : 1,
            time_seconds = player && player.isPrepared ? player.time : Saved.time,
            seek_target_seconds = state == MediaState.Seeking ? seekTarget : (double?)null,
            status = Status, visible, active_reading = visible && IsReady, error,
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
            SavePosition(); EndReading("context_changed");
            if (guardedScrollbar) guardedScrollbar.Cancel("binding_changed");
            if (timeline) timeline.Cancel("binding_changed");
            generation++; CancelVideo(); ClearContent();
            key = contextKey; format = nextFormat; demoAllowed = allowDemo; fallback = fallbackText;
            error = null; package = null;
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
            Render(); Event("REFERENCE_VIEWER_BOUND", "System");
        }

        public void SetVisible(bool show)
        {
            show &= appFocused && !appPaused;
            if (visible == show) return;
            visible = show;
            if (!show)
            {
                if (state == MediaState.Seeking) Saved.time = seekTarget; else SavePosition();
                EndReading("hidden");
                if (timeline) timeline.Cancel("hidden");
                if (guardedScrollbar) guardedScrollbar.Cancel("hidden");
                if (format == "video")
                {
                    bool playing = player && player.isPlaying;
                    generation++; CancelVideo(); state = MediaState.Empty;
                    if (playing) Event("REFERENCE_VIDEO_PAUSED", "System");
                }
            }
            else if (state == MediaState.Empty && package != null) { ClearContent(); Render(); }
            if (show) BeginReading();
            Event("REFERENCE_VIEWER_VISIBILITY", "System"); RefreshVideoControls(); RefreshScrollbar();
        }

        public bool Navigate(int delta, string input = "None")
        {
            if (!visible || !IsReady || format == "video" || delta == 0) return false;
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
            switch (command)
            {
                case "reference_media.zoom_out": return ChangePdfView(-.25f, 0, false, input);
                case "reference_media.zoom_in": return ChangePdfView(.25f, 0, false, input);
                case "reference_media.zoom_reset": return ChangePdfView(0, 0, true, input);
                case "reference_media.pan_left": return ChangePdfView(0, -.2f, false, input);
                case "reference_media.pan_right": return ChangePdfView(0, .2f, false, input);
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
                case "reference_media.seek_back": return CanSkipVideo && Seek(player.time - 10, input, player.isPlaying);
                case "reference_media.seek_forward": return CanSkipVideo && Seek(player.time + 10, input, player.isPlaying);
                default: return false;
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
            if (format == "pdf") RenderPdf();
            else if (format == "steps")
            {
                var step = package.steps[index - 1];
                string text = Local(step.text_th, step.text_ja, step.text_en);
                if (string.IsNullOrWhiteSpace(text)) Fail("Step unavailable in selected language");
                else { ShowText(text); state = MediaState.Ready; }
            }
            else if (format == "images") RenderImage();
            else if (format == "video") RenderVideo();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = Mathf.Clamp01(Saved.normalized);
            scroll.horizontalNormalizedPosition = format == "pdf" ? Saved.horizontal : .5f;
            suppressScroll = false;
            if (format == "pdf" && state != MediaState.Error) UpdatePages();
            RefreshScrollbar(); BeginReading();
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
            bool show = visible && format == "pdf" && IsReady;
            if (!pdfControls && show)
            {
                pdfControls = Rect(scroll.transform.parent, "PdfViewControls", new Vector2(500, 48));
                pdfControls.anchoredPosition = new Vector2(-280, -350);
                string[] labels = { "-", "100%", "+", "<", ">" };
                string[] commands = { "zoom_out", "zoom_reset", "zoom_in", "pan_left", "pan_right" };
                pdfButtons = new UnityEngine.UI.Button[commands.Length];
                for (int i = 0; i < commands.Length; i++)
                {
                    pdfButtons[i] = Control(pdfControls, "Pdf_" + commands[i], labels[i], "reference_media." + commands[i]);
                    var rect = pdfButtons[i].GetComponent<RectTransform>();
                    rect.sizeDelta = new Vector2(88, 48); rect.anchoredPosition = new Vector2((i - 2) * 100, 0);
                    pdfButtons[i].GetComponentInChildren<TMP_Text>().margin = Vector4.zero;
                }
            }
            if (!pdfControls) return;
            pdfControls.gameObject.SetActive(show);
            if (!show) return;
            bool allowed = view && view.controller && view.controller.LocalUICommandsAllowed && !ScrollbarHeld;
            bool[] enabled = { Saved.zoom > 1, Saved.zoom != 1,
                Saved.zoom < 3, Saved.zoom > 1 && scroll.horizontalNormalizedPosition > .001f, Saved.zoom > 1 && scroll.horizontalNormalizedPosition < .999f };
            for (int i = 0; i < pdfButtons.Length; i++)
            {
                // No horizontal pan at fit width. Hide inert arrows that resemble page navigation.
                bool applicable = i < 3 || Saved.zoom > 1.001f;
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
            float textHeight = Mathf.Max(48, contentText.GetPreferredValues(contentText.text, width, 10000).y + 12);
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
            Stretch(videoSurface, new Vector2(12, 136), new Vector2(-12, -12));
            var frame = Rect(videoSurface, "VideoFrame", Vector2.zero); Stretch(frame, Vector2.zero, Vector2.zero);
            videoImage = frame.gameObject.AddComponent<UnityEngine.UI.RawImage>(); videoImage.raycastTarget = false;
            timeLabel = Text(content, "VideoTime", 24);
            timeLabel.alignment = TextAlignmentOptions.MidlineLeft; timeLabel.text = "--:-- / --:--";
            timeLabel.rectTransform.anchorMin = timeLabel.rectTransform.anchorMax = new Vector2(0, 0);
            timeLabel.rectTransform.sizeDelta = new Vector2(180, 48); timeLabel.rectTransform.anchoredPosition = new Vector2(278, 32);
            CreateTimeline();
            var commands = new[] { "play", "seek_back", "seek_forward" };
            var labels = new[] { Local("เล่น", "再生", "Play"), "-10 s", "+10 s" };
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
                    if (!HashMatches(request.downloadHandler.data, package.video.sha256)) { Fail("Video SHA256 mismatch"); prepare = null; yield break; }
                }
                verifiedVideos.Add(verifiedKey);
                LoadPhase("verified");
            }
            else LoadPhase("verified_cached");
            if (!ResearchVideoLoadRules.AcceptAsync(acquired, generation, visible)) { if (acquired == generation) prepare = null; yield break; }
            LoadPhase("preparing");
            var host = new GameObject("ReaderVideoPlayer"); host.transform.SetParent(content, false);
            player = host.AddComponent<VideoPlayer>(); videoAudio = host.AddComponent<AudioSource>();
            videoAudio.playOnAwake = false; videoAudio.mute = true; videoAudio.spatialBlend = 0; videoAudio.volume = .5f;
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
                if (Saved.time > .05 && source.canSetTime) Seek(Saved.time, "None", false, "System");
                Event("REFERENCE_VIDEO_FIRST_FRAME_DECODED", "System");
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
            var rect = Rect(content, "VideoTimeline", new Vector2(Mathf.Max(1, scroll.viewport.rect.width - 24), 72));
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 100);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, 64);
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
            if (pauseInput != null) { var input = pauseInput; pauseInput = null; Event("REFERENCE_VIDEO_PAUSED", Actor(input), input); }
            BeginReading(); RefreshVideoControls();
            view.RefreshPageControls();
        }

        bool CanSkipVideo => CanSeekTimeline && player.length > 20;
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
            else if (statusLabel && format == "video" && IsReady) statusLabel.gameObject.SetActive(false);
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
            RefreshPdfControls();
            if (!guardedScrollbar) return;
            bool show = CanScrollDocument;
            bool wasVertical = scroll.vertical; scroll.vertical = show;
            if (show && !wasVertical)
            { var bar = scroll.verticalScrollbar; scroll.verticalScrollbar = null; scroll.verticalScrollbar = bar; }
            if (!show) guardedScrollbar.Cancel("no_scroll_range");
            guardedScrollbar.interactable = show; guardedScrollbar.gameObject.SetActive(show);
        }
        void Update()
        {
            if (!scroll || !visible) return;
            float width = scroll.viewport.rect.width, height = scroll.viewport.rect.height;
            if (package != null && format != "video" && (Mathf.Abs(width - lastWidth) > 1 || Mathf.Abs(height - lastHeight) > 1))
            {
                lastWidth = width; lastHeight = height; SavePosition(); ClearContent(); Render();
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
        }
        void OnScroll(Vector2 position)
        {
            if (suppressScroll || !visible || !IsReady) return;
            string input = ScrollbarHeld ? guardedScrollbar.Input : view?.controller?.ReferenceScrollInputSource ?? "None";
            SavePosition(); if (format == "pdf") UpdatePages(Actor(input), input);
            view.RefreshPageControls();
        }
        void SavePosition()
        {
            if (key == null) return;
            var position = Saved; position.index = index; if (format == "images") position.part = part;
            if (scroll) position.normalized = scroll.verticalNormalizedPosition;
            if (scroll && format == "pdf") position.horizontal = position.zoom == 1 ? .5f : scroll.horizontalNormalizedPosition;
            if (player && player.isPrepared && !double.IsNaN(player.time)) position.time = player.time;
        }
        void BeginReading()
        {
            if (readingSince >= 0 || !visible || !IsReady) return;
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
        void OnApplicationFocus(bool focused) { appFocused = focused; if (!focused) SetVisible(false); }
        void OnApplicationPause(bool paused) { appPaused = paused; if (paused) SetVisible(false); }
        void OnDisable() { SetVisible(false); }
        void OnDestroy()
        {
            SavePosition(); EndReading("destroyed"); CancelVideo();
            foreach (var texture in pageCache.Values) ReleaseObject(texture); pageCache.Clear();
            if (scroll) scroll.onValueChanged.RemoveListener(OnScroll);
        }

        Texture2D Decode(string resource, string expected)
        {
            var asset = Resources.Load<TextAsset>(resource);
            if (!asset) { Fail("Missing PNG bytes: " + resource); return null; }
            byte[] bytes = asset.bytes; Resources.UnloadAsset(asset);
            if (!HashMatches(bytes, expected)) { Fail("PNG SHA256 mismatch: " + resource); return null; }
            // Pages and illustrations draw at roughly half their pixel size; without mips thin strokes shimmer in the headset.
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = resource, filterMode = FilterMode.Trilinear, anisoLevel = 2, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, bytes, true)) { ReleaseObject(texture); Fail("PNG decode failed: " + resource); return null; }
            return texture;
        }
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
        public static string ValidatePackage(Package value)
        {
            if (value == null || value.schema_version != 1 || (value.approval != DemoApproval && value.approval != S1DraftApproval) || string.IsNullOrWhiteSpace(value.content_id) || string.IsNullOrWhiteSpace(value.version)
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
            foreach (var image in value.images) if (image == null || !SafeResource(image.resource) || !IsHash(image.sha256)) return "Invalid image metadata";
            string movie = value.video.streaming_path;
            if (string.IsNullOrWhiteSpace(movie) || !ResourceRoots.Any(root => movie.StartsWith(root, StringComparison.Ordinal)) || movie.Contains("..") || movie.Contains(":") || movie.Contains("\\") || !IsHash(value.video.sha256)) return "Invalid video path or SHA256";
            return null;
        }
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
            base.OnPointerDown(data);
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
            base.OnDrag(data);
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
