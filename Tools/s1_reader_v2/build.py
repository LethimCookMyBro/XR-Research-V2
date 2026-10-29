"""Build the S1 Reader packages from authored content and real Unity model captures.

  python Tools/s1_reader_v2/build.py check
  python Tools/s1_reader_v2/build.py build --shots <captures> --work <work> --keep-manual --install
  python Tools/s1_reader_v2/build.py build --task T --shots <captures> --work <t3_work> --install
  python Tools/s1_reader_v2/build.py build --task T --training-version v2 --shots <captures> --work <t2_work> --install
  python Tools/s1_reader_v2/build.py narration --task T --work <t3_work>

A/B support Thai and Japanese. Training defaults to T3 in Thai, Japanese and English;
--training-version v2 preserves the historical T2 lesson. Original T_* assets are untouched.
--keep-manual rebuilds A/B Steps, Illustrated and Video without changing accepted PDF/pages.
Package versions include the authored scripts and captured PNG bytes, not filenames alone.
Manual is reference; Steps use one action; Illustrated leads with pictures; Video synchronizes
actual Unity model/animation motion with authored narration and subtitles. These are instructional
renders, not headset gameplay or native controller evidence. Narration uses edge-tts; only the
authored script is sent. --install writes Resources and StreamingAssets; Unity creates new .meta files.
"""
import argparse, asyncio, base64, concurrent.futures, hashlib, html, io, json, math, re, shutil, subprocess, sys, time, wave
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
RES = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/Resources/ResearchReaderS1"
STREAM = ROOT / "Assets/StreamingAssets/ResearchReaderS1"
APPROVAL = "S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW"
LANGS = ("th", "ja")
TASK_LANGS = {"A": LANGS, "B": LANGS, "T": ("th", "ja", "en")}
# Package id per task: T2 is the TrainingTrainerV2 Reader; the old T_* packages stay byte-identical for sessions that used them.
PACKAGE = {"A": "A", "B": "B", "T": "T2"}
PDF_NAME = {"A": "S1_TaskA_Reference_v2", "B": "S1_TaskB_Reference_v2", "T": "S1_Training_T2_Reference"}
TRAINER_V2 = "S1_TRAINING_TRAINER_V2_20261028"
TRAINER_V3 = "S1_TRAINING_TRAINER_V3_20261029"
VOICE = {"th": ("th-TH-PremwadeeNeural", "-8%"), "ja": ("ja-JP-NanamiNeural", "-5%"), "en": ("en-US-AriaNeural", "-5%")}
W, H, FPS = 1280, 720, 24
FONT = {"th": "'Leelawadee UI','Noto Sans Thai',sans-serif", "ja": "'Yu Gothic UI','Yu Gothic','Meiryo',sans-serif",
        "en": "'Segoe UI','Liberation Sans',Arial,sans-serif"}
UI = {
    "en": {"manual": "Reference manual", "q": ["Now", "What to do", "What to watch", "Done when"], "do": "Do", "look": "Look", "ok": "Correct when",
           "stage": {"problem": "Start", "do": "Do", "watch": "Watch", "done": "Result"}, "page": "Page"},
    "th": {"manual": "คู่มืออ้างอิง", "q": ["ปัญหาตอนนี้", "ต้องทำอะไร", "ต้องสังเกต", "เสร็จเมื่อ"], "do": "ทำ", "look": "ดู", "ok": "ถูกต้องเมื่อ",
           "stage": {"problem": "ปัญหา", "do": "ทำ", "watch": "สังเกต", "done": "ผลที่ถูกต้อง"}, "page": "หน้า"},
    "ja": {"manual": "参照マニュアル", "q": ["いまの問題", "やること", "見るところ", "完了の目安"], "do": "やること", "look": "見るところ", "ok": "正しい状態",
           "stage": {"problem": "問題", "do": "やること", "watch": "見るところ", "done": "正しい結果"}, "page": "ページ"},
}
STAGE_COLOR = {"problem": "#B3261E", "do": "#1F5FAF", "watch": "#9A5B00", "done": "#2E7D32"}
KIND_COLOR = {"ok": "#2E7D32", "bad": "#B3261E", "info": "#1F5FAF", "warn": "#9A5B00"}
# Option IDs may appear only where every option is shown equally (manual part table, parts panel badges).
OPTION_ID = re.compile(r"\b[MSG]0\d\b")
# Task B media must not name the actual fault location.
CAUSE_CLAIM = re.compile(r"สาเหตุคือ|ต้นเหตุคือ|ต้นเหตุอยู่|原因は")


# Steps and captions are TextMeshPro text in the headset; these fonts (with fallbacks) must cover every character.
TMP_FONTS = {"th": ["Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai.ttf", "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
                    "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/NotoSansJP-Regular.otf"],
             "ja": ["Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/NotoSansJP-Regular.otf"],
             # ResearchFlow_English = LiberationSans with the Japanese asset as its fallback.
             "en": ["Assets/TextMesh Pro/Fonts/LiberationSans.ttf", "Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/NotoSansJP-Regular.otf"]}
TMP_SAFE = {" ↻": "", " ↺": "", "↻": "", "↺": "", "✗": "×"}


def tmp_safe(text):
    for symbol, plain in TMP_SAFE.items():
        text = text.replace(symbol, plain)
    return text


def missing_glyphs(text, lang, _cache={}):
    if lang not in _cache:
        from fontTools.ttLib import TTFont
        _cache[lang] = [set(TTFont(ROOT / f, fontNumber=0).getBestCmap()) for f in TMP_FONTS[lang]]
    return sorted({ch for ch in strip(text) if ch != "\n" and not any(ord(ch) in cmap for cmap in _cache[lang])})


def load(training_version="v3"):
    return {t: json.loads((HERE / ("content_T3.json" if t == "T" and training_version == "v3" else f"content_{t}.json")).read_text(encoding="utf-8")) for t in "ABT"}


def strip(text):
    return re.sub(r"<[^>]+>", "", text)


def joined(value):
    return " ".join(value) if isinstance(value, list) else value


# ---------------------------------------------------------------- checks
def texts(node):
    if isinstance(node, dict):
        if set(LANGS) <= set(node):
            yield node
        for value in node.values():
            yield from texts(value)
    elif isinstance(node, list):
        for value in node:
            yield from texts(value)


def coverage(c):
    m = c["manual"]
    manual = set()
    for s in m:
        for key in ("items", "after"):
            for item in s.get(key, []):
                manual |= set(item.get("units", []))
        for key in ("table", "table2"):
            if key in s:
                manual |= set(s[key]["units"])
    return {"manual": manual,
            "steps": set().union(*(set(s["units"]) for s in c["steps"])),
            "illustrated": set().union(*(set(p["units"]) for p in c["illustrated"])),
            "video": set().union(*(set(v["units"]) for v in c["video"]))}


def check(content):
    errors, report = [], []
    for task, c in content.items():
        langs = TASK_LANGS[task]
        units = set(c["units"])
        for fmt, got in coverage(c).items():
            missing, unknown = units - got, got - units
            if missing: errors.append(f"{task} {fmt}: missing essential units {sorted(missing)}")
            if unknown: errors.append(f"{task} {fmt}: unknown units {sorted(unknown)}")
            report.append(f"{task} {fmt}: {len(got & units)}/{len(units)} essential units")
        empty = [f"{task}: empty {lang} text near {str(node)[:60]}" for node in texts(c) for lang in langs
                 if not node.get(lang) or not joined(node[lang]) or not all(isinstance(v, str) and v.strip() for v in (node[lang] if isinstance(node[lang], list) else [node[lang]]))]
        if empty:
            errors += empty; continue
        if [s["id"] for s in c["manual"]] != ["goal", "problem", "parts", "cautions", "notes", "pass"]:
            errors.append(f"{task} manual: sections must be goal/problem/parts/cautions/notes/pass")
        for i, s in enumerate(c["steps"], 1):
            for key in ("do", "look", "ok"):
                if key not in s: errors.append(f"{task} step {i}: missing {key}")
            for lang in langs:
                if len(s["do"][lang]) > {"th": 90, "ja": 60, "en": 70}[lang]:
                    errors.append(f"{task} step {i}: action too long for one action ({lang})")
        for i, p in enumerate(c["illustrated"], 1):
            for lang in langs:
                lines = strip(p["caption"][lang]).split("\n")
                if len(lines) != 2 or len(lines[1]) > {"th": 75, "ja": 52, "en": 80}[lang]:
                    errors.append(f"{task} panel {i}: caption must be a title plus one short line ({lang})")
            if not (p.get("callouts") or p.get("arrows") or p.get("rotate") or p.get("layout") in ("parts", "areas", "result", "triple", "pair", "reader")):
                errors.append(f"{task} panel {i}: picture has no arrows or callouts")
        for lang in langs:
            tmp_text = "".join(steps_text(lang, c)) + "".join(tmp_safe(p["caption"][lang]) for p in c["illustrated"])
            if missing_glyphs(tmp_text, lang):
                errors.append(f"{task} {lang}: in-app fonts lack {missing_glyphs(tmp_text, lang)}")
        stages = [v["stage"] for v in c["video"]]
        if stages[0] != "problem" or stages[-1] != "done" or not {"do", "watch"} <= set(stages):
            errors.append(f"{task} video: must open with the problem, show actions and checks, and end with the result")
        for i, v in enumerate(c["video"], 1):
            if len({len(v["say"][lang]) for lang in langs}) != 1:
                errors.append(f"{task} scene {i}: narration sentence counts differ between {'/'.join(langs)}")
        if task == "T": errors += check_training(c)
        first, last = c["illustrated"][0]["units"], c["illustrated"][-1]["units"]
        if "problem" not in first or "done" not in last or "problem" not in c["steps"][0]["units"] or "done" not in c["steps"][-2]["units"] + c["steps"][-1]["units"]:
            errors.append(f"{task}: steps/illustrated must start with the problem and end with the completion check")
        scrutinized = [c["steps"], c["video"], [p["caption"] for p in c["illustrated"]]]
        for node in texts(scrutinized):
            for lang in langs:
                text = joined(node[lang])
                if task == "A" and OPTION_ID.search(text):
                    errors.append(f"A: option ID outside the equal parts table: {text[:50]}")
                if task == "B" and CAUSE_CLAIM.search(text):
                    errors.append(f"B: text names a cause: {text[:50]}")
    for task, c in content.items():
        for i, scene in enumerate(c["video"]):
            windows = scene.get("motion_windows")
            if windows is None:
                continue
            if any(len(windows) != len(scene["say"][lang]) for lang in TASK_LANGS[task]) or any(
                    len(window) != 2 or not 0 <= window[0] <= window[1] <= 1 for window in windows):
                errors.append(f"{task} video {i + 1}: invalid sentence-motion windows")
    return errors, report


STATION_CS = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/Runtime/ResearchTrainingStation.cs"
STRINGS_CS = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/UI/ResearchStrings.cs"
POLICY_CS = ROOT / "Assets/VRMaintenanceResearch/ResearchFlow/Core/ResearchUIPolicy.cs"
QUOTE = {"th": re.compile(r"“([^”]+)”"), "en": re.compile(r"“([^”]+)”"), "ja": re.compile(r"「([^」]+)」")}
# Owner rule: the screw is taught as moving in and out, never by a left/right end alone.
DIRECTION_WORDS = {"fasten": {"th": ["ขันเข้า"], "ja": ["締め", "奥"], "en": ["tighten", r"\bin\b"]},
                   "loosen": {"th": ["คลายออก"], "ja": ["緩め", "手前"], "en": ["loosen", r"\bout\b"]}}
SIDE_ONLY = re.compile(r"ซ้ายสุด|ขวาสุด|左端|右端|いちばん左|いちばん右|leftmost|rightmost|far left|far right", re.I)
# Training must not name Task A/B parts, options or fault locations.
TASK_REFERENCE = re.compile(r"\b[MSG]0\d\b|พัดลม|คอมพิวเตอร์|ตะแกรง|การ์ดจอ|扇風機|パソコン|コンピュータ|メモリ|\bfan\b|computer|\bRAM\b|SSD|GPU|"
                            r"task ?[AB]\b|งาน ?[AB]\b|課題 ?[AB]\b", re.I)
DRAWN_SYMBOLS = set("↻↺✓✗→↓↑·≠")  # drawn by the browser in pictures, video and PDF, never TextMeshPro


def app_sequence(training_version):
    """The actual station sequence for the recorded training version."""
    name = "TrainerV3Sequence" if training_version == TRAINER_V3 else "FixedSequence"
    match = re.search(r"string\[\] " + name + r" = \{([^}]*)\}", STATION_CS.read_text(encoding="utf-8"))
    return re.findall(r'"(\w+)"', match.group(1))


def app_strings():
    """lang -> every in-app string (ResearchStrings rows: key, English, Thai, Japanese)."""
    rows = re.findall(r'new\[\]\s*\{\s*"[^"]+",\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"\s*\}', STRINGS_CS.read_text(encoding="utf-8"))
    unescape = lambda s: s.replace("\\n", "\n").replace('\\"', '"')
    return {lang: [unescape(row[i]) for row in rows] for i, lang in enumerate(("en", "th", "ja"))}


def in_app(quote, values):
    if any(quote in v for v in values): return True
    patterns = (re.escape(v).replace(r"\{0\}", ".+?").replace(r"\{1\}", ".+?") for v in values if "{0}" in v)
    return any(re.fullmatch(p, quote) for p in patterns)


def check_training(c):
    errors, langs = [], TASK_LANGS["T"]
    sequence = app_sequence(c["training_version"])
    if c["training_version"] not in (TRAINER_V2, TRAINER_V3) or c["training_version"] not in POLICY_CS.read_text(encoding="utf-8"):
        errors.append(f"T: unknown training version {c['training_version']}")
    if not set(sequence) <= set(c["units"]):
        errors.append(f"T: app steps without an essential unit: {sorted(set(sequence) - set(c['units']))}")
    # Steps and video follow the running order (fitting insert before the blocked try); a step teaches one app step.
    for fmt, strict in (("steps", True), ("video", False)):
        first = [next((i for i, item in enumerate(c[fmt]) if fact in item["units"]), None) for fact in sequence]
        if None in first: continue
        for (a, ia), (b, ib) in zip(zip(sequence, first), zip(sequence[1:], first[1:])):
            if ib < ia or (strict and ib == ia):
                errors.append(f"T {fmt}: '{b}' must come after '{a}' as in ResearchTrainingStation.FixedSequence")
    strings = app_strings()
    items = [("step", s) for s in c["steps"]] + [("panel", p) for p in c["illustrated"]] + [("scene", v) for v in c["video"]] + \
            [("manual", i) for s in c["manual"] for key in ("items", "after") for i in s.get(key, [])]
    for kind, item in items:
        for lang in langs:
            text = " ".join(joined(n[lang]) for n in texts({k: v for k, v in item.items() if k != "units"}))
            for unit, words in DIRECTION_WORDS.items():
                if unit in item.get("units", []):
                    for word in words[lang]:
                        if not re.search(word, text, re.I):
                            errors.append(f"T {kind} '{strip(text)[:40]}': {unit} must say {word!r} ({lang})")
            if SIDE_ONLY.search(text): errors.append(f"T {kind}: left/right end used as a direction cue: {text[:50]}")
    for node in texts(c):
        for lang in langs:
            text = joined(node[lang])
            if TASK_REFERENCE.search(text): errors.append(f"T: names experimental-task content ({lang}): {text[:50]}")
            for quote in QUOTE[lang].findall(text):
                if not in_app(quote, strings[lang]):
                    errors.append(f"T: quoted label is not an in-app string ({lang}): {quote}")
    for row in (r for p in c["illustrated"] for r in p.get("results", [])):
        for lang in langs:
            if not in_app(row[lang], strings[lang]): errors.append(f"T: result row is not the in-app result text ({lang}): {row[lang]}")
    thai = "".join(joined(n["th"]) for n in texts(c))
    lacking = [ch for ch in missing_glyphs(thai, "th") if ch not in DRAWN_SYMBOLS]
    if lacking: errors.append(f"T th: characters with no glyph in the in-app Thai fonts: {lacking}")
    return errors


# ---------------------------------------------------------------- html rendering
CSS = """
*{box-sizing:border-box} body{margin:0;color:#14202B}
.chip{position:absolute;padding:6px 14px;border-radius:12px;font-weight:700;font-size:26px;color:#fff;white-space:nowrap;box-shadow:0 2px 6px rgba(0,0,0,.35)}
.card{position:absolute;background:rgba(255,255,255,.95);border-radius:14px;padding:16px 20px;box-shadow:0 2px 10px rgba(0,0,0,.35)}
.card h3{margin:0 0 8px;font-size:28px;color:#1F5FAF}.card div{font-size:24px;line-height:1.35;margin:4px 0}
.tag{position:absolute;padding:6px 12px;border-radius:10px;font-size:24px;font-weight:700;color:#fff}
.sub{position:absolute;left:90px;right:90px;bottom:26px;text-align:center}
.sub span{display:inline-block;background:rgba(10,18,28,.84);color:#fff;font-size:34px;line-height:1.45;padding:10px 22px;border-radius:12px}
.stage{position:absolute;left:24px;top:22px;padding:8px 20px;border-radius:14px;color:#fff;font-size:30px;font-weight:700;box-shadow:0 2px 6px rgba(0,0,0,.35)}
.prog{position:absolute;left:24px;top:82px;font-size:22px;color:#fff;background:rgba(10,18,28,.7);padding:3px 10px;border-radius:8px}
"""


def arrow_svg(x1, y1, x2, y2, color, width=6):
    ang = math.atan2(y2 - y1, x2 - x1)
    hx, hy = x2 - 26 * math.cos(ang), y2 - 26 * math.sin(ang)
    lx, ly = hx + 13 * math.cos(ang + math.pi / 2), hy + 13 * math.sin(ang + math.pi / 2)
    rx, ry = hx - 13 * math.cos(ang + math.pi / 2), hy - 13 * math.sin(ang + math.pi / 2)
    return (f'<line x1="{x1:.0f}" y1="{y1:.0f}" x2="{hx:.0f}" y2="{hy:.0f}" stroke="white" stroke-width="{width + 5}" stroke-linecap="round"/>'
            f'<line x1="{x1:.0f}" y1="{y1:.0f}" x2="{hx:.0f}" y2="{hy:.0f}" stroke="{color}" stroke-width="{width}" stroke-linecap="round"/>'
            f'<polygon points="{x2:.0f},{y2:.0f} {lx:.0f},{ly:.0f} {rx:.0f},{ry:.0f}" fill="{color}" stroke="white" stroke-width="2"/>')


def ring_svg(x, y, color):
    return (f'<circle cx="{x:.0f}" cy="{y:.0f}" r="22" fill="none" stroke="white" stroke-width="9"/>'
            f'<circle cx="{x:.0f}" cy="{y:.0f}" r="22" fill="none" stroke="{color}" stroke-width="5"/>')


def rotate_svg(x, y, direction, color, r=78):
    if direction == "yaw":
        return (f'<path d="M {x - 120:.0f} {y + 30:.0f} Q {x:.0f} {y + 90:.0f} {x + 120:.0f} {y + 30:.0f}" fill="none" stroke="white" stroke-width="13"/>'
                f'<path d="M {x - 120:.0f} {y + 30:.0f} Q {x:.0f} {y + 90:.0f} {x + 120:.0f} {y + 30:.0f}" fill="none" stroke="{color}" stroke-width="8"/>'
                + arrow_svg(x + 70, y + 52, x + 128, y + 26, color, 8) + arrow_svg(x - 70, y + 52, x - 128, y + 26, color, 8))
    cw = direction == "cw"
    a0, a1 = (-150, 120) if cw else (-30, -300)
    pts = [(x + r * math.cos(math.radians(a)), y + r * math.sin(math.radians(a))) for a in range(a0, a1 + (1 if cw else -1), 6 if cw else -6)]
    d = "M " + " L ".join(f"{px:.0f} {py:.0f}" for px, py in pts[:-2])
    (ex, ey), (fx, fy) = pts[-3], pts[-1]
    return (f'<path d="{d}" fill="none" stroke="white" stroke-width="15" stroke-linecap="round"/>'
            f'<path d="{d}" fill="none" stroke="{color}" stroke-width="9" stroke-linecap="round"/>' + arrow_svg(ex, ey, fx + (fx - ex) * .6, fy + (fy - ey) * .6, color, 9))


class Frames:
    def __init__(self, shots_dir):
        self.dir = Path(shots_dir)
        self.manifest = json.loads((self.dir / "shots.json").read_text(encoding="utf-8"))
        self.cache = {}

    def segment(self, shot, seg):
        frames = [f for f in self.manifest[shot]["frames"] if f["segment"] == seg]
        if not frames: raise ValueError(f"shot {shot} has no segment {seg}")
        return frames

    def pick(self, shot, pick):
        seg, at = pick
        frames = self.segment(shot, seg)
        return frames[min(len(frames) - 1, round(at * (len(frames) - 1)))]

    def path(self, shot, frame):
        # Inline data: Chromium blocks file:// images on a set_content page.
        key = (shot, frame["file"])
        if key not in self.cache:
            self.cache[key] = "data:image/png;base64," + base64.b64encode((self.dir / shot / frame["file"]).read_bytes()).decode()
        return self.cache[key]


def place(frame_anchor, box):
    """Map a 1280x720 frame point into a placed/cropped box (x, y, w, h, crop=(cx, cy, cw, ch))."""
    x, y, w, h, crop = box
    cx, cy, cw, ch = crop
    return x + (frame_anchor[0] - cx) * w / cw, y + (frame_anchor[1] - cy) * h / ch


def overlay_items(lang, frame, box, callouts=(), arrows=(), rotates=(), scale=1.0):
    svg, divs = [], []
    for c in callouts:
        ax, ay, vis = frame["anchors"].get(c["anchor"], (0, 0, False))
        if not vis:
            print(f"  warn: anchor {c['anchor']} not visible", file=sys.stderr)
            continue
        x, y = place((ax, ay), box)
        color = KIND_COLOR[c.get("kind", "info")]
        lx = min(max(x + c.get("dx", 120) * scale, box[0] + 10), box[0] + box[2] - 40)
        ly = min(max(y + c.get("dy", -60) * scale, box[1] + 10), box[1] + box[3] - 50)
        mark = "✓ " if c.get("kind") == "ok" else "✗ " if c.get("kind") == "bad" else ""
        svg.append(arrow_svg(lx, ly, x, y, color) + ring_svg(x, y, color))
        divs.append(f'<div class="chip" data-x="{lx:.0f}" data-y="{ly:.0f}" style="background:{color};font-size:{26 * max(scale, .8):.0f}px">{mark}{html.escape(c["label"][lang])}</div>')
    for a in arrows:
        ax, ay, vis = frame["anchors"].get(a["anchor"], (0, 0, False))
        if not vis: continue
        x, y = place((ax, ay), box)
        color = KIND_COLOR[a.get("kind", "ok")]
        if a.get("dir") == "up":  # away from the part (a screw coming out); the default points down onto it
            svg.append(arrow_svg(x, y - 20 * scale, x, y - 170 * scale, color, 10))
        else:
            svg.append(arrow_svg(x, y - 170 * scale, x, y - 20 * scale, color, 10))
        divs.append(f'<div class="chip" data-x="{x + 20:.0f}" data-y="{y - 190 * scale:.0f}" style="background:{color}">{html.escape(a["label"][lang])}</div>')
    for r in rotates:
        ax, ay, vis = frame["anchors"].get(r["anchor"], (0, 0, False))
        if not vis and "at" not in r: continue
        x, y = place((ax, ay) if vis else r["at"], box)
        direction = "yaw" if r["anchor"] == "service_handle" else r["dir"]
        color = KIND_COLOR["warn"]
        svg.append(rotate_svg(x, y, direction, color, 78 * max(scale, .6)))
        divs.append(f'<div class="chip" data-x="{x + 90 * scale:.0f}" data-y="{y - 130 * scale:.0f}" style="background:{color};font-size:30px">{html.escape(r["label"][lang])}</div>')
    return "".join(svg), "".join(divs)


# Chips are placed by their top-left and then pushed inside the canvas by the browser script.
PLACE_JS = """() => { for (const d of document.querySelectorAll('[data-x]')) { const w=d.offsetWidth,h=d.offsetHeight;
  d.style.left=Math.max(8,Math.min(%W%-w-8,+d.dataset.x-w/2))+'px'; d.style.top=Math.max(8,Math.min(%H%-h-8,+d.dataset.y-h/2))+'px'; } }"""


def page_html(lang, body, transparent=False, size=(W, H)):
    bg = "transparent" if transparent else "#ffffff"
    return (f"<!doctype html><html><head><meta charset='utf-8'><style>{CSS} body{{font-family:{FONT[lang]};background:{bg};width:{size[0]}px;height:{size[1]}px;position:relative;overflow:hidden}}</style></head>"
            f"<body>{body}</body></html>")


def card_html(lang, card, x, y, w):
    lines = "".join(f"<div>• {html.escape(l[lang])}</div>" for l in card["lines"])
    return f'<div class="card" style="left:{x}px;top:{y}px;width:{w}px"><h3>{html.escape(card["title"][lang])}</h3>{lines}</div>'


def panel_body(lang, panel, frames, content, video=False):
    """Returns HTML for one 1280x720 illustrated picture (also used as the still background of video scenes)."""
    layout = panel.get("layout", "single")
    imgs, svg, divs = [], [], []
    full = (0, 0, W, H, (0, 0, W, H))
    if layout in ("single", "result"):
        f = frames.pick(panel["shot"], panel["pick"])
        imgs.append(f'<img src="{frames.path(panel["shot"], f)}" style="position:absolute;left:0;top:0;width:{W}px;height:{H}px;{"filter:brightness(.55)" if layout == "result" else ""}">')
        s, d = overlay_items(lang, f, full, panel.get("callouts", []), panel.get("arrows", []), panel.get("rotate", []))
        svg.append(s); divs.append(d)
        if "card" in panel:
            cx, cy = panel.get("card_at", (880, 24))
            divs.append(card_html(lang, panel["card"], cx, cy, 376))
        if layout == "result":
            y = 92
            for r in panel["results"]:
                color = KIND_COLOR[r["kind"]]
                icon = "✓" if r["kind"] == "ok" else "✗"
                divs.append(f'<div class="card" style="left:110px;top:{y}px;width:1060px;border-left:16px solid {color}">'
                            f'<div style="font-size:30px;font-weight:700;color:{color}">{icon} {html.escape(r[lang])}</div>'
                            f'<div style="font-size:26px">→ {html.escape(r["note"][lang])}</div></div>')
                y += 160
    elif layout in ("pair", "triple"):
        n = len(panel["frames"])
        w = 620 if n == 2 else 412
        h = round(w * H / W)
        gap = (W - n * w) // (n + 1)
        top = 150 if n == 2 else 200
        for i, fr in enumerate(panel["frames"]):
            f = frames.pick(fr["shot"], fr["pick"])
            x = gap + i * (w + gap)
            crop = (0, 0, W, H)
            if "zoom" in fr:  # 2x crop around a named anchor so small states (half-seated, latch) are visible
                ax, ay, _ = f["anchors"][fr["zoom"]]
                crop = (min(max(ax - 320, 0), W - 640), min(max(ay - 180, 0), H - 360), 640, 360)
            box = (x, top, w, h, crop)
            color = KIND_COLOR[fr.get("kind", "info")]
            mark = "✓ " if fr.get("kind") == "ok" else "✗ " if fr.get("kind") == "bad" else ""
            cx, cy, cw_, ch_ = crop
            imgs.append(f'<div style="position:absolute;left:{x}px;top:{top}px;width:{w}px;height:{h}px;overflow:hidden;outline:6px solid {color};border-radius:10px">'
                        f'<img src="{frames.path(fr["shot"], f)}" style="position:absolute;left:{-cx * w / cw_:.0f}px;top:{-cy * h / ch_:.0f}px;width:{W * w / cw_:.0f}px;height:{H * h / ch_:.0f}px"></div>')
            divs.append(f'<div class="tag" style="left:{x}px;top:{top - 70 if n == 2 else top - 110}px;width:{w}px;background:{color};{"font-size:22px" if n == 3 else ""}">{mark}{html.escape(fr["tag"][lang])}</div>')
            s, d = overlay_items(lang, f, box, [c for c in panel.get("callouts", []) if c.get("frame", 0) == i],
                                 [a for a in panel.get("arrows", []) if a.get("frame", 0) == i], [r for r in panel.get("rotate", []) if r.get("frame", 0) == i], w / crop[2])
            svg.append(s); divs.append(d)
        if "card" in panel: divs.append(card_html(lang, panel["card"], 120, top + h + 20, 1040))
    elif layout == "parts":
        values = {row[0]: row[1] for row in next(s for s in content["manual"] if s["id"] == "parts")["table2"]["rows"]}
        quads = [("A_parts_ram", 0, 0), ("A_parts_ssd", 1, 0), ("A_parts_G01", 0, 1), ("A_parts_G02", 1, 1)]
        iw, ih, lw, top = 352, 198, 250, 112
        for shot, gx, gy in quads:
            f = frames.pick(shot, [0, 0])
            pts = [(name.replace("part_", ""), ax, ay) for name, (ax, ay, vis) in f["anchors"].items() if vis]
            # Zoom onto the options so every part (and its ID badge) stays readable.
            xs, ys = [p[1] for p in pts], [p[2] for p in pts]
            cw_ = min(W, max(max(xs) - min(xs) + 300, (max(ys) - min(ys) + 220) * 16 / 9, 520)); ch_ = cw_ * 9 / 16
            cx = min(max((max(xs) + min(xs)) / 2 - cw_ / 2, 0), W - cw_); cy = min(max((max(ys) + min(ys)) / 2 - ch_ / 2, 0), H - ch_)
            x, y = 20 + gx * (iw + lw + 30), top + gy * (ih + 100)
            imgs.append(f'<div style="position:absolute;left:{x}px;top:{y}px;width:{iw}px;height:{ih}px;overflow:hidden;border-radius:8px">'
                        f'<img src="{frames.path(shot, f)}" style="position:absolute;left:{-cx * iw / cw_:.0f}px;top:{-cy * ih / ch_:.0f}px;width:{W * iw / cw_:.0f}px;height:{H * ih / ch_:.0f}px"></div>')
            legend = []
            for k, (pid, ax, ay) in enumerate(sorted(pts, key=lambda p: p[1])):
                px, py = place((ax, ay), (x, y, iw, ih, (cx, cy, cw_, ch_)))
                svg.append(ring_svg(px, py, "#22313F"))
                divs.append(f'<div class="chip" data-x="{px:.0f}" data-y="{py + (-34 if k % 2 == 0 else 34):.0f}" style="background:#22313F;font-size:20px;padding:2px 8px">{pid}</div>')
                legend.append(f"<div><b>{pid}</b> {html.escape(values[pid])}</div>")
            divs.append(f'<div class="card" style="left:{x + iw + 8}px;top:{y}px;width:{lw}px;padding:8px 10px;font-size:18px;line-height:1.35">{"".join(legend)}</div>')
        req = panel["card"]
        line = " &nbsp;|&nbsp; ".join(html.escape(l[lang]) for l in req["lines"][:3])
        # In video the stage chip sits top-left and the subtitle owns the bottom, so the requirement card shifts right and the note is spoken.
        left = 200 if video else 20
        divs.append(f'<div class="card" style="left:{left}px;top:10px;width:{W - left - 20}px;padding:10px 16px"><div style="font-size:{21 if video else 23}px"><b style="color:#1F5FAF">{html.escape(req["title"][lang])}:</b> {line}</div></div>')
        if not video:
            divs.append(f'<div class="card" style="left:20px;top:{H - 56}px;width:1240px;padding:6px 16px"><div style="font-size:22px">{html.escape(req["lines"][3][lang])}</div></div>')
    elif layout == "areas":
        w, h = 400, 225
        for i, row in enumerate(panel["rows"]):
            seg = frames.segment(panel["shot"], row["segment"])
            x = 20 + i * (w + 20)
            divs.append(f'<div class="tag" style="left:{x}px;top:12px;width:{w}px;background:#22313F;text-align:center">{html.escape(row["title"][lang])}</div>')
            for j, (kind, at) in enumerate((("ok", .2), ("bad", .9))):
                f = seg[min(len(seg) - 1, round(at * (len(seg) - 1)))]
                ax, ay, _ = f["anchors"][row["anchor"]]
                cw_, ch_ = 640, 360
                cx = min(max(ax - cw_ / 2, 0), W - cw_); cy = min(max(ay - ch_ / 2, 0), H - ch_)
                y = 70 + j * (h + 92)
                color = KIND_COLOR[kind]
                imgs.append(f'<div style="position:absolute;left:{x}px;top:{y}px;width:{w}px;height:{h}px;overflow:hidden;border:6px solid {color};border-radius:10px">'
                            f'<img src="{frames.path(panel["shot"], f)}" style="position:absolute;left:{-cx * w / cw_:.0f}px;top:{-cy * h / ch_:.0f}px;width:{W * w / cw_:.0f}px;height:{H * h / ch_:.0f}px"></div>')
                px, py = place((ax, ay), (x, y, w, h, (cx, cy, cw_, ch_)))
                svg.append(ring_svg(px, py, color))
                if kind == "bad":  # the wobble is too small to see in a still, so mark the movement explicitly
                    svg.append(arrow_svg(px - 20, py + 40, px - 70, py + 40, color, 6) + arrow_svg(px + 20, py + 40, px + 70, py + 40, color, 6))
                divs.append(f'<div class="tag" style="left:{x}px;top:{y + h + 8}px;width:{w}px;background:{color};font-size:22px">{"✓" if kind == "ok" else "✗"} {html.escape(row[kind][lang])}</div>')
    elif layout == "reader":
        reader_schematic(lang, panel["reader"], imgs, svg, divs)
    else:
        raise ValueError("unknown layout " + layout)
    return ("".join(imgs) + f'<svg style="position:absolute;left:0;top:0" width="{W}" height="{H}">{"".join(svg)}</svg>' + "".join(divs))


def reader_schematic(lang, r, imgs, svg, divs):
    """A drawn Reader panel (the Reader is UI, so no model footage shows it): tabs, minimize, close, drag bar, reopen bar."""
    x0, y0, w, h = 60, 64, 700, 470
    box = lambda x, y, bw, bh, style, text="": (f'<div style="position:absolute;left:{x}px;top:{y}px;width:{bw}px;height:{bh}px;border-radius:12px;'
                                               f'display:flex;align-items:center;justify-content:center;text-align:center;{style}">{text}</div>')
    imgs.append(f'<div style="position:absolute;left:0;top:0;width:{W}px;height:{H}px;background:linear-gradient(#6B7F93,#2E3D4B)"></div>')
    imgs.append(box(x0, y0, w, h, "background:#fff;box-shadow:0 6px 24px rgba(0,0,0,.45)"))
    imgs.append(f'<div style="position:absolute;left:{x0 + 26}px;top:{y0 + 22}px;font-size:30px;font-weight:700">{html.escape(r["title"][lang])}</div>')
    imgs.append(box(x0 + w - 160, y0 + 16, 64, 52, "background:#E3ECF7;font-size:38px;font-weight:700", "−"))
    imgs.append(box(x0 + w - 84, y0 + 16, 64, 52, "background:#E3ECF7;font-size:34px;font-weight:700", "×"))
    tab_w = (w - 52 - 30) / 4
    for i, tab in enumerate(r["tabs"]):
        active = i == 1
        imgs.append(box(x0 + 26 + i * (tab_w + 10), y0 + 88, tab_w, 52, f"background:{'#1F5FAF' if active else '#E3ECF7'};color:{'#fff' if active else '#14202B'};font-size:22px",
                        html.escape(tab[lang])))
    imgs.append(box(x0 + 26, y0 + 158, w - 52, h - 182, "background:#F2F6FB"))
    for i in range(7):
        imgs.append(f'<div style="position:absolute;left:{x0 + 56}px;top:{y0 + 190 + i * 38}px;width:{(w - 112) * (.92 - .14 * (i % 3)):.0f}px;height:14px;border-radius:7px;background:#CCD7E3"></div>')
    imgs.append(box(x0 + w / 2 - 190, y0 + h + 16, 380, 50, "background:#22313F;color:#fff;font-size:22px;border-radius:25px", html.escape(r["handle"][lang])))
    bar_x, bar_y = 860, 560
    imgs.append(f'<div style="position:absolute;left:{bar_x}px;top:{bar_y - 40}px;width:360px;text-align:center;color:#fff;font-size:22px">{html.escape(r["bar_note"][lang])}</div>')
    imgs.append(box(bar_x, bar_y, 360, 56, "background:#1F5FAF;color:#fff;font-size:24px;font-weight:700;border-radius:28px", html.escape(r["bar"][lang])))
    spots = {"tabs": (x0 + 26 + 1.5 * tab_w + 10, y0 + 114), "minimize": (x0 + w - 128, y0 + 42), "close": (x0 + w - 52, y0 + 42),
             "handle": (x0 + w / 2, y0 + h + 41), "bar": (bar_x + 180, bar_y + 28)}
    for note in r["notes"]:
        x, y = spots[note["at"]]
        lx, ly = x + note.get("dx", 200), y + note.get("dy", -60)
        color = KIND_COLOR[note.get("kind", "info")]
        svg.append(arrow_svg(lx, ly, x, y, color) + ring_svg(x, y, color))
        divs.append(f'<div class="chip" data-x="{lx:.0f}" data-y="{ly:.0f}" style="background:{color}">{html.escape(note["label"][lang])}</div>')


class Browser:
    def __init__(self):
        from playwright.sync_api import sync_playwright
        self.pw = sync_playwright().start()
        self.browser = self.pw.chromium.launch()
        self.page = self.browser.new_page(viewport={"width": W, "height": H})

    def shot(self, markup, out, transparent=False, size=(W, H)):
        self.page.set_viewport_size({"width": size[0], "height": size[1]})
        self.page.set_content(markup, wait_until="load")
        self.page.evaluate(PLACE_JS.replace("%W%", str(size[0])).replace("%H%", str(size[1])))
        self.page.screenshot(path=str(out), omit_background=transparent, clip={"x": 0, "y": 0, "width": size[0], "height": size[1]})

    def close(self):
        self.browser.close(); self.pw.stop()


# ---------------------------------------------------------------- manual
PAGE_W, PAGE_H = 1273, 1800
MANUAL_CSS = """
*{box-sizing:border-box} body{margin:0;color:#14202B;background:#fff}
.page{width:1273px;height:1800px;padding:110px 130px 120px;position:relative;overflow:hidden;break-after:page}
.top{display:flex;justify-content:space-between;align-items:baseline;border-bottom:4px solid #1F5FAF;padding-bottom:12px;margin-bottom:26px}
.top h1{font-size:46px;margin:0;color:#14202B}.top span{font-size:28px;color:#1F5FAF;font-weight:700}
.q{display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-bottom:30px}
.q div{border-radius:14px;padding:16px 20px;font-size:29px;line-height:1.45;background:#F2F6FB;border-left:12px solid #1F5FAF}
.q b{display:block;font-size:26px;margin-bottom:4px}
h2{font-size:40px;color:#1F5FAF;margin:26px 0 12px}
ul{margin:0;padding-left:34px} li{font-size:31px;line-height:1.5;margin:8px 0}
li.ok{color:#1E5E22} li.bad{color:#8F1D16} li.warn{font-weight:700;color:#7A4700} li.subhead{list-style:none;margin-left:-34px;font-weight:700}
table{border-collapse:collapse;width:100%;margin:10px 0 16px} td,th{border:2px solid #C9D6E5;padding:10px 14px;font-size:28px;line-height:1.4;text-align:left;vertical-align:top}
th{background:#E3ECF7} caption{font-size:30px;font-weight:700;text-align:left;padding:8px 0}
.foot{position:absolute;bottom:60px;left:130px;right:130px;text-align:right;font-size:24px;color:#5B6B7F}
"""


def cell(value, lang):
    return html.escape(value if isinstance(value, str) else value[lang])


def manual_blocks(lang, c):
    q = [label[lang] for label in c["q_labels"]] if "q_labels" in c else UI[lang]["q"]
    core = c["core"]
    blocks = ["<div class='q'>" + "".join(
        f"<div style='border-left-color:{col}'><b style='color:{col}'>{q[i]}</b>{html.escape(core[k][lang])}</div>"
        for i, (k, col) in enumerate(zip(("problem", "do", "watch", "done"), ("#B3261E", "#1F5FAF", "#9A5B00", "#2E7D32")))) + "</div>"]
    for s in c["manual"]:
        # One block per bullet/table so pages break between items; the heading travels with the first block.
        section = [f"<ul><li class='{i.get('style', '')}'>{html.escape(i[lang])}</li></ul>" for i in s.get("items", [])]
        for key in ("table", "table2"):
            if key in s:
                t = s[key]
                cap = f"<caption>{cell(t['title'], lang)}</caption>" if "title" in t else ""
                head = "".join(f"<th>{cell(h, lang)}</th>" for h in t["head"])
                rows = "".join("<tr>" + "".join(f"<td>{cell(v, lang)}</td>" for v in row) + "</tr>" for row in t["rows"])
                section.append(f"<table>{cap}<tr>{head}</tr>{rows}</table>")
        section += [f"<ul><li>{html.escape(i[lang])}</li></ul>" for i in s.get("after", [])]
        section[0] = f"<div><h2>{html.escape(s['title'][lang])}</h2>{section[0]}</div>"
        blocks += section
    return blocks


PAGINATE_JS = """(args) => { const [blocks, title, label, pageWord] = args; const root = document.body; root.innerHTML = '';
  const pages = []; let page = null, body = null;
  const fresh = () => { page = document.createElement('div'); page.className = 'page';
    page.innerHTML = `<div class='top'><h1>${title}</h1><span>${label}</span></div><div class='body'></div><div class='foot'></div>`;
    root.appendChild(page); body = page.querySelector('.body'); pages.push(page); };
  fresh();
  for (const b of blocks) { const holder = document.createElement('div'); holder.innerHTML = b; const node = holder.firstChild;
    body.appendChild(node);
    if (body.getBoundingClientRect().bottom > page.getBoundingClientRect().top + 1800 - 130) {
      body.removeChild(node); fresh(); body.appendChild(node);
      if (body.getBoundingClientRect().bottom > page.getBoundingClientRect().top + 1800 - 130) throw new Error('block taller than a page'); } }
  pages.forEach((p, i) => p.querySelector('.foot').textContent = `${pageWord} ${i + 1} / ${pages.length}`);
  return pages.length; }"""


def build_manual(browser, lang, task, c, out_dir):
    browser.page.set_viewport_size({"width": PAGE_W, "height": PAGE_H})
    browser.page.set_content(f"<!doctype html><html><head><meta charset='utf-8'><style>{MANUAL_CSS} body{{font-family:{FONT[lang]}}} @page{{size:{PAGE_W}px {PAGE_H}px;margin:0}}</style></head><body></body></html>")
    count = browser.page.evaluate(PAGINATE_JS, [manual_blocks(lang, c), html.escape(c["title"][lang]), UI[lang]["manual"], UI[lang]["page"]])
    pages = []
    for i, element in enumerate(browser.page.query_selector_all(".page")):
        out = out_dir / f"page{i + 1:02d}.png"
        element.screenshot(path=str(out))
        pages.append(out)
    pdf = out_dir / "manual.pdf"
    browser.page.pdf(path=str(pdf), width=f"{PAGE_W}px", height=f"{PAGE_H}px", print_background=True)
    browser.page.set_viewport_size({"width": W, "height": H})
    assert count == len(pages)
    return pages, pdf


# ---------------------------------------------------------------- steps
def steps_text(lang, c):
    ui, out = UI[lang], []
    for s in c["steps"]:
        out.append(tmp_safe(f"<size=24><color=#5B6B7F>{s['group'][lang]}</color></size>\n"
                            f"<size=36><b>{s['do'][lang]}</b></size>\n\n"
                            f"<color=#1F5FAF><b>{ui['look']}:</b></color> {s['look'][lang]}\n\n"
                            f"<color=#2E7D32><b>{ui['ok']}:</b></color> {s['ok'][lang]}"))
    return out


# ---------------------------------------------------------------- narration
def tts(text, lang, cache):
    voice, rate = VOICE[lang]
    key = hashlib.sha256(f"{voice}|{rate}|{text}".encode("utf-8")).hexdigest()[:24]
    mp3, wav = cache / f"{key}.mp3", cache / f"{key}.wav"
    if not wav.exists():
        import edge_tts
        for attempt in range(12):
            try:
                # Playwright's sync API owns this thread's event loop, so synthesize on a worker thread.
                with concurrent.futures.ThreadPoolExecutor(1) as pool:
                    pool.submit(asyncio.run, edge_tts.Communicate(text, voice, rate=rate).save(str(mp3))).result(timeout=60)
                if mp3.stat().st_size > 1000: break
            except Exception as error:  # the service intermittently returns no audio for Thai; retry
                last = error
            time.sleep(1.5)
        else:
            raise RuntimeError(f"TTS failed for {text[:40]}: {last}")
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(mp3), "-ar", "48000", "-ac", "1", "-c:a", "pcm_s16le", str(wav)], check=True)
    with wave.open(str(wav)) as w:
        return wav, w.getnframes() / w.getframerate()


def pcm(path):
    with wave.open(str(path)) as w:
        return w.readframes(w.getnframes())


# ---------------------------------------------------------------- video
def motion_position(windows, starts, ends, t, duration):
    if not windows:
        return min(t / max(duration - .9, .5), 1)
    current = max((i for i, start in enumerate(starts) if start <= t), default=0)
    a, b = windows[current]
    phase = max(0, min(1, (t - starts[current]) / max(ends[current] - starts[current], .001)))
    return a + (b - a) * phase


def video_sentence_anchors(segment, scene, sentence_index):
    """Use the sentence's actual source pose; never pin a label to a moving target."""
    if not segment:
        return None, []
    windows = scene.get("motion_windows")
    a, b = windows[sentence_index] if windows else (0, 1)
    last = len(segment) - 1
    start, stop = (min(last, int(p * last + .5)) for p in (a, b))
    used = segment[min(start, stop):max(start, stop) + 1]

    def stationary(callout):
        points = [frame["anchors"].get(callout["anchor"]) for frame in used]
        # One rendered source pixel only accommodates coordinate rounding, not task tolerance.
        return all(p and p[2] for p in points) and all(
            max(p[axis] for p in points) - min(p[axis] for p in points) <= 1
            for axis in (0, 1))

    return segment[start], [c for c in scene.get("callouts", []) if stationary(c)]


def build_video(browser, lang, task, c, frames, work, out_mp4):
    from PIL import Image
    cache = work / "tts"; cache.mkdir(parents=True, exist_ok=True)
    tmp = work / "overlay" / f"{task}_{lang}"; tmp.mkdir(parents=True, exist_ok=True)
    LEAD, GAP, TAIL = .45, .35, .7
    timeline, audio, chapters, t0 = [], bytearray(), [], 0.0
    silence = lambda s: b"\x00\x00" * int(48000 * s)
    for n, scene in enumerate(c["video"]):
        voiced = [tts(s, lang, cache) for s in scene["say"][lang]]
        audio += silence(LEAD); starts, ends, t = [], [], LEAD
        for i, (wav, d) in enumerate(voiced):
            starts.append(t); audio += pcm(wav); t += d; ends.append(t)
            if i < len(voiced) - 1: audio += silence(GAP); t += GAP
        audio += silence(TAIL); duration = t + TAIL
        # Base footage: real motion for this segment, or the composed still for parts/result layouts.
        if scene.get("layout") in ("parts", "result", "reader"):
            panel = dict(scene); panel.setdefault("pick", [0, 0])
            if scene.get("layout") == "parts":
                panel["card"] = next(p for p in c["illustrated"] if p.get("layout") == "parts")["card"]
            elif scene.get("layout") == "reader":
                panel["reader"] = next(p for p in c["illustrated"] if p.get("layout") == "reader")["reader"]
            else:
                panel["results"] = next(p for p in c["illustrated"] if p.get("layout") == "result")["results"]
            still = tmp / f"s{n:02d}_base.png"
            still_body = panel_body(lang, panel, frames, c, video=True)
            if scene.get("layout") == "reader":
                still_body = ('<div style="position:absolute;left:170px;top:0;transform:scale(.8);'
                              'transform-origin:top left;width:1280px;height:720px">' + still_body + "</div>")
            browser.shot(page_html(lang, still_body), still)
            sources = [Image.open(still).convert("RGB")]
            anchor_frame = None
        else:
            seg = frames.segment(scene["shot"], scene["segment"])
            sources = [Image.open(frames.dir / scene["shot"] / f["file"]).convert("RGB") for f in seg]
            anchor_frame = seg[0]
        overlays = []
        for i, sentence in enumerate(scene["say"][lang]):
            sentence_frame, callouts = (None, []) if anchor_frame is None else video_sentence_anchors(seg, scene, i)
            svg, divs = ("", "") if sentence_frame is None else overlay_items(lang, sentence_frame, (0, 0, W, H, (0, 0, W, H)),
                                                                         callouts, scene.get("arrows", []), scene.get("rotate", []))
            body = (f'<svg style="position:absolute;left:0;top:0" width="{W}" height="{H}">{svg}</svg>{divs}'
                    f'<div class="stage" style="background:{STAGE_COLOR[scene["stage"]]}">{c.get("stage_labels", {}).get(scene["stage"], {}).get(lang) or UI[lang]["stage"][scene["stage"]]}</div>'
                    f'<div class="prog">{n + 1} / {len(c["video"])}</div>'
                    f'<div class="sub"><span>{html.escape(sentence)}</span></div>')
            out = tmp / f"s{n:02d}_{i}.png"
            browser.shot(page_html(lang, body, transparent=True), out, transparent=True)
            overlays.append(Image.open(out).convert("RGBA"))
        timeline.append((duration, sources, overlays, starts, ends, scene.get("motion_windows")))
        chapters.append({"stage": scene["stage"], "start_seconds": round(t0, 2), "end_seconds": round(t0 + duration, 2)})
        t0 += duration
    wav_path = work / f"{task}_{lang}_narration.wav"
    with wave.open(str(wav_path), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(48000); w.writeframes(bytes(audio))
    encoder = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
                                "-i", str(wav_path), "-c:v", "libx264", "-preset", "medium", "-crf", "21", "-pix_fmt", "yuv420p", "-profile:v", "high",
                                "-c:a", "aac", "-b:a", "128k", "-ac", "2", "-ar", "48000", "-shortest", "-movflags", "+faststart", str(out_mp4)], stdin=subprocess.PIPE)
    for duration, sources, overlays, starts, ends, windows in timeline:
        count = round(duration * FPS)
        for k in range(count):
            t = k / FPS
            src = sources[min(len(sources) - 1, int(motion_position(windows, starts, ends, t, duration) * (len(sources) - 1) + .5))]
            current = max(i for i, s in enumerate(starts) if s <= t + 1e-6) if t >= starts[0] else 0
            encoder.stdin.write(Image.alpha_composite(src.convert("RGBA"), overlays[current]).convert("RGB").tobytes())
    encoder.stdin.close()
    if encoder.wait() != 0: raise RuntimeError("ffmpeg failed")
    return chapters, len(audio) / 2 / 48000


# ---------------------------------------------------------------- install
def version_digest(paths):
    # Git normalizes text line endings; a recipient checkout must derive the same content version.
    return hashlib.sha256(b"".join(path.read_text(encoding="utf-8").encode("utf-8") for path in paths)).hexdigest()[:16]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def meta_for(path):
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        relative = path.relative_to(ROOT / "Assets").as_posix()
        guid = hashlib.sha256(("s1.reader.v2/" + relative).encode()).hexdigest()[:32]
        if path.is_dir():
            meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")
        else:
            meta.write_text(f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n", encoding="utf-8")


def put(src, dst, meta=True):
    dst.parent.mkdir(parents=True, exist_ok=True)
    if meta and not Path(str(dst.parent) + ".meta").exists(): meta_for(dst.parent)
    shutil.copyfile(src, dst)
    if meta: meta_for(dst)


def remove(path):
    for p in (path, Path(str(path) + ".meta")):
        if p.exists(): p.unlink()


def install(task, lang, c, built, version, keep_manual=False):
    pid = PACKAGE[task]
    # A/B keep their pinned page/image .meta GUIDs; T2 is new, so Unity creates its .meta files on import.
    meta = task != "T"
    pkg_dir = RES / f"{pid}_{lang}"
    page_dir = pkg_dir / "pages"
    previous = json.loads((pkg_dir / "package.json").read_text(encoding="utf-8")) if keep_manual else None
    if keep_manual:
        pages = previous["pages"]
    else:
        for old in page_dir.glob("p*.bytes"):
            if int(old.stem[1:]) > len(built["pages"]): remove(old)
        pages = []
        for i, png in enumerate(built["pages"], 1):
            dst = page_dir / f"p{i:02d}.bytes"; put(png, dst, meta)
            from PIL import Image
            w, h = Image.open(png).size
            pages.append({"page": i, "width": w, "height": h, "resource": f"ResearchReaderS1/{pid}_{lang}/pages/p{i:02d}", "sha256": sha(dst)})
        put(built["pdf"], pkg_dir / "source_pdf.bytes", meta)
        # T2 keeps its pictures inside its own package folder so the install touches nothing outside T2_*.
    img_dir, img_prefix = (pkg_dir / "images", "") if task == "T" else (RES / "images_v2", f"{pid}_{lang}_")
    img_resource = f"ResearchReaderS1/{pid}_{lang}/images/" if task == "T" else "ResearchReaderS1/images_v2/"
    for old in img_dir.glob(f"{img_prefix}*.bytes"):
        if int(old.stem.rsplit("_", 1)[-1]) > len(built["images"]): remove(old)
    images = []
    for i, (png, caption) in enumerate(built["images"], 1):
        dst = img_dir / f"{img_prefix}{i:02d}.bytes"; put(png, dst, meta)
        images.append({"resource": f"{img_resource}{img_prefix}{i:02d}", "sha256": sha(dst), f"caption_{lang}": caption})
    movie = STREAM / f"{pid}_{lang}.mp4"
    shutil.copyfile(built["video"], movie)
    package = {"schema_version": 1, "content_id": f"s1.reader.{pid}.{lang}", "version": version, "approval": APPROVAL,
               "source_pdf": f"{PDF_NAME[task]}_{lang.upper()}.pdf", "source_pdf_resource": f"ResearchReaderS1/{pid}_{lang}/source_pdf",
               "source_sha256": sha(pkg_dir / "source_pdf.bytes"), "pages": pages,
               "steps": [{f"text_{lang}": text} for text in built["steps"]], "images": images,
               "video": {"streaming_path": f"ResearchReaderS1/{pid}_{lang}.mp4", "sha256": sha(movie),
                         "duration_seconds": round(built["duration"], 2), "audio": "narration", "chapters": built["chapters"]}}
    if keep_manual:
        for key in ("source_pdf", "source_pdf_resource", "source_sha256"):
            package[key] = previous[key]
    (pkg_dir / "package.json").write_text(json.dumps(package, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return package


def illustration_size(panel):
    if panel.get("card"):
        return W, H
    return W, {"pair": 520, "triple": 460}.get(panel.get("layout"), H)


def capture_digest(directory, task):
    directory = Path(directory).resolve()
    manifest = json.loads((directory / "shots.json").read_text(encoding="utf-8"))
    digest, count = hashlib.sha256(), 0
    for name, shot in sorted(manifest.items()):
        if shot["task"] != task:
            continue
        digest.update(json.dumps(shot, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))
        for frame in shot["frames"]:
            path = (directory / name / frame["file"]).resolve()
            path.relative_to(directory)
            digest.update((name + "/" + frame["file"] + "\0").encode())
            digest.update(hashlib.sha256(path.read_bytes()).digest())
            count += 1
    if not count:
        raise ValueError(f"No captured frames for task {task}")
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["check", "build", "narration"])
    parser.add_argument("--shots"); parser.add_argument("--work"); parser.add_argument("--install", action="store_true")
    parser.add_argument("--only", help="e.g. A_th or T_en")
    parser.add_argument("--task", choices=["A", "B", "T"], help="build one task; without it, build makes A and B")
    parser.add_argument("--training-version", choices=["v2", "v3"], default="v3")
    parser.add_argument("--keep-manual", action="store_true", help="Rebuild A/B media without touching accepted PDF/pages")
    args = parser.parse_args()
    if args.keep_manual and args.task == "T": parser.error("--keep-manual applies only to A/B")
    content = load(args.training_version)
    PACKAGE["T"] = "T3" if args.training_version == "v3" else "T2"
    PDF_NAME["T"] = "S1_Training_" + PACKAGE["T"] + "_Reference"
    errors, report = check(content)
    print("\n".join(report))
    if errors:
        print("\n".join("ERROR " + e for e in errors)); return 1
    print("check: PASS")
    if args.command == "check": return 0
    work = Path(args.work)
    tasks = args.task or "AB"
    if args.command == "narration":
        return narration(content, tasks, work)
    frames = Frames(args.shots)
    version = "v2-" + version_digest(HERE / n for n in ("content_A.json", "content_B.json", "shots.json", "build.py"))
    versions = {"A": version, "B": version,
                "T": f"{content['T']['training_version']}.reader-{PACKAGE['T'].lower()}-" + version_digest(HERE / n for n in (("content_T3.json" if args.training_version == "v3" else "content_T.json"), "shots.json", "build.py"))}
    for task in tasks:
        versions[task] += "." + capture_digest(args.shots, task)[:16]
    browser = Browser()
    summary = {}
    try:
        for task in tasks:
            for lang in TASK_LANGS[task]:
                name = f"{task}_{lang}"
                if args.only and args.only != name: continue
                out = work / "out" / name; out.mkdir(parents=True, exist_ok=True)
                c = content[task]
                pages, pdf = ([], None) if args.keep_manual else build_manual(browser, lang, task, c, out)
                images = []
                for i, panel in enumerate(c["illustrated"], 1):
                    png = out / f"img{i:02d}.png"
                    size = illustration_size(panel)
                    browser.shot(page_html(lang, panel_body(lang, panel, frames, c), size=size), png, size=size)
                    images.append((png, tmp_safe(panel["caption"][lang])))
                mp4 = out / f"{name}.mp4"
                chapters, duration = build_video(browser, lang, task, c, frames, work, mp4)
                built = {"pages": pages, "pdf": pdf, "images": images, "steps": steps_text(lang, c), "video": mp4, "chapters": chapters, "duration": duration}
                summary[name] = {"version": versions[task], "pages": len(pages), "steps": len(built["steps"]), "images": len(images), "video_seconds": round(duration, 1), "scenes": len(chapters)}
                if args.install:
                    package = install(task, lang, c, built, versions[task], keep_manual=args.keep_manual)
                    summary[name]["package_sha256"] = sha(RES / f"{PACKAGE[task]}_{lang}" / "package.json"); summary[name]["video_sha256"] = package["video"]["sha256"]
                print(name, summary[name], flush=True)
    finally:
        browser.close()
    (work / "build_summary.json").write_text(json.dumps({"version": version if tasks == "AB" else {t: versions[t] for t in tasks}, "packages": summary},
                                                        ensure_ascii=False, indent=2), encoding="utf-8")
    return 0


def narration(content, tasks, work):
    """Synthesizes (and caches under <work>/tts) every narration sentence; the later build reuses the cache."""
    cache = work / "tts"; cache.mkdir(parents=True, exist_ok=True)
    totals = {}
    for task in tasks:
        for lang in TASK_LANGS[task]:
            seconds = []
            for scene in content[task]["video"]:
                voiced = [tts(s, lang, cache)[1] for s in scene["say"][lang]]
                seconds.append(.45 + sum(voiced) + .35 * (len(voiced) - 1) + .7)  # LEAD + sentences + GAPs + TAIL, as build_video
            totals[f"{task}_{lang}"] = round(sum(seconds), 1)
            print(f"{task}_{lang}: {len(seconds)} scenes, {totals[f'{task}_{lang}']} s, longest scene {max(seconds):.1f} s", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
