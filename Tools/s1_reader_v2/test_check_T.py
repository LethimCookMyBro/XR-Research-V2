"""Self-check for the Training (T2) guards in build.py: each mutation of content_T.json must make `check` fail.

usage: python -X utf8 Tools/s1_reader_v2/test_check_T.py
"""
import copy, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build

base = build.load()
assert not build.check(base)[0], build.check(base)[0]


def fails(name, mutate, expect):
    content = copy.deepcopy(base)
    mutate(content["T"])
    errors = build.check(content)[0]
    assert any(expect in e for e in errors), f"{name}: expected an error containing {expect!r}, got {errors}"
    print("ok:", name)


def swap_insert_steps(t):
    s = t["steps"]
    i = next(k for k, step in enumerate(s) if "lock" in step["units"])
    s[i - 1], s[i] = s[i], s[i - 1]


def step(t, unit):
    return next(s for s in t["steps"] if unit in s["units"])


fails("lock before correct insertion", swap_insert_steps, "must come after 'insert'")
fails("fasten without 奥", lambda t: [step(t, "fasten")[k].update(ja=step(t, "fasten")[k]["ja"].replace("奥", "中")) for k in ("do", "look")], "fasten must say '奥'")
fails("loosen without 'out'", lambda t: [step(t, "loosen")[k].update(en=step(t, "loosen")[k]["en"].replace(" out", " away")) for k in ("do", "look")], "loosen must say")
fails("quoted label not in the app", lambda t: step(t, "inspect")["do"].update(th="เลือก “ตรวจสอบผล” อีกครั้ง"), "quoted label is not an in-app string")
fails("left/right end as direction", lambda t: step(t, "fasten")["look"].update(en="Turn it to the rightmost position."), "left/right end")
fails("names a Task A part", lambda t: step(t, "problem")["look"].update(en="Like the SSD tray."), "experimental-task content")
fails("result row differs from the app", lambda t: t["illustrated"][-1]["results"][1].update(ja="練習の結果: 合格。"), "result row is not the in-app result text")
fails("Thai glyph missing from the in-app fonts", lambda t: step(t, "problem")["ok"].update(th="ทำผิดได้ \U00013000"), "lack")
fails("English missing", lambda t: step(t, "problem")["do"].pop("en"), "empty en text")
fails("UI policy is not a training version", lambda t: t.update(training_version="S1_MOVABLE_UI_20261028"), "unknown training version")
print("all T guards fire")
