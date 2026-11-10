"""Readable S1 session report from raw ResearchFlow logs: what the participant did, in order, without watching video.

usage: python Tools/s1_session_report.py SESSION_DIR [OUT_DIR]
SESSION_DIR holds events.jsonl and session_metadata.json (Quest: .../ResearchSessionRegistry/Logs/<run_id>).
Writes report_th.md (timeline and per-task summary), timeline.csv (one row per logged step) and summary.json.
Only logged facts are reported: exposure is panel visibility, not gaze or comprehension; no intent is inferred.
The study measures stay in research_export.py; this report is their readable companion.
"""
import collections
import csv
import json
import sys
from pathlib import Path

PARTS = {
    "B.front_guard": "ตะแกรงหน้า", "B.front_guard_clip": "คลิปตะแกรงหน้า", "B.spinner": "ฝาล็อกกลางใบพัด", "B.blade": "ใบพัด",
    "B.rear_guard": "ตะแกรงหลัง", "B.rear_guard_lock": "วงแหวนล็อกตะแกรงหลัง",
}
CLOSE_REASONS = {"tab_changed": "เปลี่ยนแท็บ", "media_REFERENCE_VIEWER_ACTIVE_END": "แผงไม่อยู่ในสายตา", "reference_closed": "ปิดเอกสาร"}
KINDS = {"M": "แรม", "S": "SSD", "G": "การ์ดจอ"}


def part_name(target):
    """Thai label; Task A catalog ids (M01 RAM, S01 SSD, G01 GPU) keep their id so the exact part stays identifiable."""
    if target in PARTS:
        return PARTS[target]
    if target.endswith(".latch"):
        return "ล็อก " + target.split(".")[-2]
    if target.endswith("_fastener"):
        return "สกรู " + target.split(".")[0]
    named = {"A.service_handle": "มือจับฝาเคส", "A.sata_data": "สาย SATA ข้อมูล", "A.sata_power": "สาย SATA ไฟ", "A.pcie_power": "สายไฟการ์ดจอ"}
    if target in named:
        return named[target]
    if target.startswith("A.gpu_fastener"):
        return "สกรูการ์ดจอ"
    if len(target) == 3 and target[0] in KINDS and target[1:].isdigit():
        return f"{KINDS[target[0]]} {target}"
    return target


FORMATS = {"user_manual": "คู่มือ PDF", "step_by_step": "ทีละขั้นตอน", "illustrated": "ภาพประกอบ", "video": "วิดีโอ"}
BLOCKS = {
    "ClipClosed": "คลิปยังปิด", "FrontGuardPresent": "ตะแกรงหน้ายังอยู่", "BladeCapPresent": "ฝาล็อกยังอยู่", "BladePresent": "ใบพัดยังอยู่",
    "RearRingPresent": "วงแหวนหลังยังอยู่", "Alignment": "ยังไม่ตรงแนว", "MountNotReady": "ชิ้นด้านในยังไม่พร้อม", "Contact": "ชนชิ้นอื่น",
    "EndStop": "หมุนสุดแล้ว", "GuardNotSeated": "ตะแกรงยังไม่ล็อก", "Unspecified": "ระบบไม่อนุญาต",
}
PLACEMENT = {0: "ถอดออก", 1: "ใส่ไม่สุด", 2: "ใส่สุด"}
THREAD = {0: "หลุด", 1: "หลวม", 2: "แน่น"}
YES = {True: "ใช่", False: "ไม่"}
STATE_LABELS = {
    "frontGuardPlacement": ("ตะแกรงหน้า", PLACEMENT), "frontGuardClipped": ("คลิปตะแกรงหน้า", {True: "ปิด", False: "เปิด"}),
    "frontGuardAligned": ("ตะแกรงหน้าล็อก", {True: "ใช่", False: "ไม่"}), "bladePlacement": ("ใบพัด", PLACEMENT),
    "spinner": ("ฝาล็อกกลางใบพัด", THREAD), "rearGuardPlacement": ("ตะแกรงหลัง", PLACEMENT), "rearLock": ("วงแหวนล็อกหลัง", THREAD),
    "placement": ("การติดตั้ง", PLACEMENT), "user_created_issues": ("จุดที่ยังไม่เรียบร้อย", {}),
    "bladeKeyAligned": ("ใบพัดตรงร่อง", YES), "latchClosed": ("ล็อกปิด", YES), "retained": ("ถูกล็อกยึด", YES), "slot": ("ช่อง", {}),
    "mount": ("ตำแหน่งยึด", {}), "trayFastener": ("สกรูถาด", {}), "bracketFastener": ("สกรูขายึด", {}),
    "targetConnected": ("เสียบแล้ว", YES), "targetConnector": ("ช่องที่เสียบ", {}), "original_fault_active": ("ปัญหาเดิมยังอยู่", YES),
}
# Bookkeeping fields that change on every snapshot and say nothing about the equipment.
STATE_NOISE = {"capturedAtSeconds", "assemblyObstructed", "observation_accessible_targets", "systemIntegrityValid", "held"}


def deep(value, key):
    """First value for key anywhere in nested dicts/lists (payload layouts differ between event versions)."""
    if isinstance(value, dict):
        if key in value:
            return value[key]
        children = value.values()
    elif isinstance(value, list):
        children = value
    else:
        return None
    for child in children:
        found = deep(child, key)
        if found is not None:
            return found
    return None


def equipment(event):
    """Innermost equipment snapshot the logger attached to the event itself ({task_id, task_state: {...}} is unwrapped)."""
    snapshot = event.get("task_state")
    while isinstance(snapshot, dict) and isinstance(snapshot.get("task_state"), dict):
        snapshot = snapshot["task_state"]
    return snapshot if isinstance(snapshot, dict) else {}


def flatten(value, prefix=""):
    """Dotted keys; list items with an id are keyed by it (Task A: "M01.placement")."""
    rows = {}
    if isinstance(value, dict):
        for key, child in value.items():
            if key in STATE_NOISE or key.endswith("Id"):
                continue
            rows.update(flatten(child, f"{prefix}{key}."))
    elif isinstance(value, list):
        if value and all(not isinstance(child, (dict, list)) for child in value):
            rows[prefix[:-1]] = frozenset(value)
        for child in value:
            if isinstance(child, dict) and "id" in child:
                rows.update(flatten(child, f"{prefix}{child['id']}."))
    else:
        rows[prefix[:-1]] = round(value, 1) if isinstance(value, float) else value
    return rows


def state_changes(before, after):
    before, after = flatten(before), flatten(after)
    rows = []
    for key in sorted(set(before) | set(after)):
        if before.get(key) == after.get(key):
            continue
        if isinstance(before.get(key, frozenset()), frozenset) and isinstance(after.get(key, frozenset()), frozenset):
            added, removed = after.get(key, frozenset()) - before.get(key, frozenset()), before.get(key, frozenset()) - after.get(key, frozenset())
            label = STATE_LABELS.get(key, (key, {}))[0]
            rows.append(f"{label}: " + ", ".join([f"+{x}" for x in sorted(added)] + [f"-{x}" for x in sorted(removed)]))
            continue
        leaf = key.rsplit(".", 1)[-1]
        owner = key.rsplit(".", 1)[0] if "." in key else ""
        label, names = STATE_LABELS.get(key) or STATE_LABELS.get(leaf) or (key, {})
        if owner and key not in STATE_LABELS:
            label = f"{part_name(owner.split('.')[-1])} {label if leaf in STATE_LABELS else leaf}"
        rows.append(f"{label}: {names.get(before.get(key), before.get(key))} → {names.get(after.get(key), after.get(key))}")
    return rows


def clock(seconds, origin):
    s = max(0, seconds - origin)
    return f"{int(s // 60):02d}:{s % 60:04.1f}"


class Task:
    def __init__(self, task_id):
        self.id, self.start, self.end, self.outcome = task_id, None, None, "ไม่จบ (log สิ้นสุดก่อน)"
        self.active = None
        self.paused = 0.0
        self.inspects = []
        self.info = collections.defaultdict(lambda: dict(opens=0, exposure_s=0.0, pages=set()))
        self.info_order = []
        self.parts = collections.defaultdict(lambda: dict(grabs=0, changed=0, blocked=collections.Counter()))
        self.panel_moves = self.panel_rejected = 0


def build(session):
    session = Path(session)
    with open(session / "events.jsonl", encoding="utf-8") as stream:
        events = [json.loads(line) for line in stream if line.strip()]
    metadata = json.loads((session / "session_metadata.json").read_text(encoding="utf-8"))
    events.sort(key=lambda e: (e.get("timestamp", 0), e.get("seq", 0)))
    origin = events[0]["timestamp"] if events else 0
    timeline, tasks, grabs = [], {}, {}
    pause_at = None

    def add(event, kind, text):
        timeline.append(dict(time=clock(event["timestamp"], origin), seconds=round(event["timestamp"] - origin, 3), phase=event.get("phase"),
                             task=event.get("task_id"), kind=kind, target=event.get("target"), text=text, seq=event.get("seq"), action_id=event.get("action_id")))

    for e in events:
        name, target, task_id = e["event"], e.get("target") or "", e.get("task_id")
        task = tasks.setdefault(task_id, Task(task_id)) if task_id in ("A", "B") else None
        after = e.get("after") or {}
        if name == "TASK_STARTED" and task:
            task.start = e["timestamp"]; add(e, "task", f"เริ่มงาน {task_id}")
        elif name == "TASK_ENDED" and task:
            task.end = e["timestamp"]; task.active = after.get("active_elapsed_s")
            task.outcome = ("สำเร็จ" if after.get("completed") else "ไม่สำเร็จ") + f" ({e.get('result')}, {after.get('end_reason') or e.get('reason') or '-'})"
            add(e, "task", f"จบงาน {task_id}: {task.outcome}")
        elif name in ("SESSION_PAUSED", "SESSION_RESUMED"):
            if name == "SESSION_PAUSED":
                pause_at = e["timestamp"]
            elif pause_at is not None and task:
                task.paused += e["timestamp"] - pause_at; pause_at = None
            add(e, "pause", "หยุดพัก" if name == "SESSION_PAUSED" else "กลับมาทำต่อ")
        elif name == "INFORMATION_OPEN":
            fmt = target.split(".", 1)[-1]
            how = "เลือกเอง" if after.get("selection_kind") == "deliberate" else "เปิดอัตโนมัติ" if after.get("default_presentation") else "แสดงอีกครั้ง"
            if task:
                task.info[fmt]["opens"] += 1
                if fmt not in task.info_order:
                    task.info_order.append(fmt)
            add(e, "information", f"เปิดข้อมูล {FORMATS.get(fmt, fmt)} ({how})")
        elif name == "INFORMATION_CLOSE":
            fmt = target.split(".", 1)[-1]; seconds = float(after.get("exposure_seconds") or 0)
            if task:
                task.info[fmt]["exposure_s"] += seconds
            reason = e.get("reason") or e.get("result") or ""
            add(e, "information", f"หยุดแสดง {FORMATS.get(fmt, fmt)} หลัง {seconds:.1f} วินาที ({CLOSE_REASONS.get(reason, reason)})")
        elif name == "REFERENCE_VIEWER_NAVIGATE":
            fmt = target.split(".", 1)[-1]; page = deep(after, "index")
            if task and page is not None:
                task.info[fmt]["pages"].add(page)
            add(e, "information", f"{FORMATS.get(fmt, fmt)}: ไปหน้า/ขั้น {page}")
        elif name.startswith("REFERENCE_VIDEO_") and name in ("REFERENCE_VIDEO_PLAY_REQUESTED", "REFERENCE_VIDEO_PAUSE_REQUESTED", "REFERENCE_VIDEO_SEEK_REQUESTED"):
            add(e, "information", {"REFERENCE_VIDEO_PLAY_REQUESTED": "กดเล่นวิดีโอ", "REFERENCE_VIDEO_PAUSE_REQUESTED": "กดหยุดวิดีโอ",
                                   "REFERENCE_VIDEO_SEEK_REQUESTED": "กดข้ามช่วงวิดีโอ"}[name])
        elif name in ("EXAMINE", "EXAMINE_REJECTED"):
            detail = after.get("examine") or {}
            observed = detail.get("shown_text") or ""
            reason = after.get("rejected_reason") or ""
            add(e, "examine", f"{name} {part_name(target)} {reason} {observed}".strip())
        elif name in ("PART_CONDITION_SHOWN", "PART_CONDITION_HIDDEN"):
            add(e, "part_feedback", f"{name} {part_name(target)} {after.get('shown_text') or ''}".strip())
        elif name in ("REFERENCE_PDF_VIEW_CHANGED", "REFERENCE_IMAGE_VIEW_CHANGED"):
            add(e, "information", f"{name} {FORMATS.get(target.split('.', 1)[-1], target)} {json.dumps(after, ensure_ascii=False)}")
        elif name in ("REFERENCE_VIEWER_SECTION_SELECTED", "REFERENCE_VIEWER_CONTENTS_OPENED"):
            add(e, "information", f"{name} {after.get('to_section') or ''}".strip())
        elif name == "GRAB":
            grabs[e.get("action_id")] = (e, equipment(e), collections.Counter())
            if task:
                task.parts[target]["grabs"] += 1
        elif name == "RELEASE" and e.get("action_id") in grabs:
            start, before_state, reasons = grabs.pop(e.get("action_id"))
            changes = state_changes(before_state, equipment(e))
            held = e["timestamp"] - start["timestamp"]
            text = f"จับ{part_name(target)} {held:.1f} วินาที: " + ("; ".join(changes) if changes else "ไม่มีอะไรเปลี่ยน")
            if reasons:
                text += " (ระบบกันไว้: " + ", ".join(BLOCKS.get(r, r) for r in reasons) + ")"
                if task:
                    task.parts[target]["blocked"].update(reasons.keys())
            if task and changes:
                task.parts[target]["changed"] += 1
            add(e, "part", text)
        elif name == "PART_INTERACTION_CHANGED" and e.get("action_id") in grabs:
            reason = after.get("block_reason")
            if reason and reason != "None":
                grabs[e["action_id"]][2][reason] += 1
        elif name == "INSPECT":
            add(e, "inspect", "กดตรวจผล (Inspect)")
        elif name in ("INSPECT_RESULT", "INSPECT_PASS", "INSPECT_FAIL"):
            result = e.get("result") if name == "INSPECT_RESULT" else name[len("INSPECT_"):].capitalize()
            if task:
                task.inspects.append(result)
            add(e, "inspect", f"ผลตรวจ: {result}")
        elif name == "PANEL_DRAG_METRICS" and task:
            task.panel_moves += 1; task.panel_rejected += int(after.get("rejected_samples") or 0)
        elif name in ("CHECKPOINT_COMPLETED", "RUN_COMPLETED", "RUN_CLOSED_INCOMPLETE"):
            add(e, "session", {"CHECKPOINT_COMPLETED": "ผู้วิจัยยืนยันช่วงพัก", "RUN_COMPLETED": "จบ session", "RUN_CLOSED_INCOMPLETE": "session ปิดก่อนจบ"}[name])
    return metadata, timeline, tasks, origin


def write(session, out):
    metadata, timeline, tasks, origin = build(session)
    out = Path(out); out.mkdir(parents=True, exist_ok=True)
    with open(out / "timeline.csv", "w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=["time", "seconds", "phase", "task", "kind", "target", "text", "seq", "action_id"])
        writer.writeheader(); writer.writerows(timeline)
    summary = {}
    lines = [f"# รายงาน session {metadata.get('run_id')}", "",
             f"เริ่ม {metadata.get('started_utc')} UTC · ทดสอบระบบ: {metadata.get('technical_test')} · ข้อมูลทดสอบ: {metadata.get('test_data')}",
             "เวลาที่แสดงข้อมูล = แผงข้อมูลอยู่ในสายตา ไม่ใช่การจ้องหรือความเข้าใจ", ""]
    for task_id in sorted(tasks):
        t = tasks[task_id]
        elapsed = (t.end - t.start) if t.start is not None and t.end is not None else None
        info = {fmt: dict(opens=v["opens"], exposure_s=round(v["exposure_s"], 1), pages_seen=sorted(v["pages"])) for fmt, v in t.info.items()}
        parts = {p: dict(grabs=v["grabs"], grabs_that_changed_state=v["changed"], blocked=dict(v["blocked"])) for p, v in t.parts.items()}
        summary[task_id] = dict(outcome=t.outcome, active_seconds=t.active, elapsed_seconds=elapsed, paused_seconds=round(t.paused, 1),
                                inspect_results=t.inspects, information_order=t.info_order, information=info, parts=parts,
                                panel_moves=t.panel_moves, panel_rejected_samples=t.panel_rejected)
        lines += [f"## งาน {task_id}", "", f"- ผล: {t.outcome}",
                  f"- เวลาทำงาน: {t.active:.0f} วินาที" if isinstance(t.active, (int, float)) else "- เวลาทำงาน: ไม่มี (งานยังไม่จบ)",
                  f"- กด Inspect {len(t.inspects)} ครั้ง: {', '.join(t.inspects) or '-'}",
                  f"- ลำดับรูปแบบข้อมูลที่เปิด: {' → '.join(FORMATS.get(f, f) for f in t.info_order) or 'ไม่ได้เปิด'}"]
        for fmt, v in info.items():
            lines.append(f"  - {FORMATS.get(fmt, fmt)}: เปิด {v['opens']} ครั้ง, แสดง {v['exposure_s']} วินาที, หน้า/ขั้นที่ไป {v['pages_seen'] or '-'}")
        for p, v in parts.items():
            blocked = ", ".join(f"{BLOCKS.get(k, k)} {n}" for k, n in v["blocked"].items())
            lines.append(f"  - {part_name(p)}: จับ {v['grabs']} ครั้ง, เปลี่ยนสภาพ {v['grabs_that_changed_state']} ครั้ง" + (f", ระบบกัน: {blocked}" if blocked else ""))
        lines += [f"- ย้ายแผง {t.panel_moves} ครั้ง", ""]
    lines += ["## ลำดับเหตุการณ์", "", "| เวลา | ช่วง | งาน | สิ่งที่ทำ |", "|---|---|---|---|"]
    lines += [f"| {r['time']} | {r['phase']} | {r['task'] or ''} | {r['text']} |" for r in timeline]
    (out / "report_th.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (out / "summary.json").write_text(json.dumps(dict(run_id=metadata.get("run_id"), tasks=summary), ensure_ascii=False, indent=1), encoding="utf-8")
    return summary


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    session = Path(sys.argv[1])
    result = write(session, sys.argv[2] if len(sys.argv) > 2 else session / "report")
    print(json.dumps(result, ensure_ascii=False)[:2000])
