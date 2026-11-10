"""Run the existing S1 SDK harness with an isolated registry and owned cleanup."""
import importlib.util
import json
import sys
import time
from pathlib import Path

HARNESS = Path(r"D:\VR_TMU\S1_Unity_PC_20261011\claude-takeover-20261015\scripts\s1_full_run.py")
spec = importlib.util.spec_from_file_location("existing_s1_harness", HARNESS)
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
output = Path(sys.argv[1]).resolve()
registry_scope = output / "isolated-session"
original_fresh_play = h.fresh_play
owned_play = False


def isolated_play():
    global owned_play
    # Do not let the legacy fresh_play routine abort an unrelated Editor run.
    if h.playing():
        raise RuntimeError("Editor already in Play; no takeover/abort of an unowned session")
    original_fresh_play()
    owned_play = True
    h.focus()
    prep = h.invoke(h.NS + "ResearchFlowPlayChecks.PrepareCanonicalSdkRun", str(registry_scope))
    (output / "isolation-receipt.json").write_text(prep, encoding="utf-8")
    h.log("isolated registry", registry_scope)


h.fresh_play = isolated_play
try:
    h.main()
finally:
    cleanup = {"scope": "Editor simulated SDK run only", "owns_play": owned_play}
    if owned_play:
        try:
            rep = h.report()
            cleanup["before"] = rep
            if rep.get("registry") == "InProgress":
                # The only session this wrapper can create is under the verified isolated scope above.
                receipt = json.loads((output / "isolation-receipt.json").read_text(encoding="utf-8"))
                if Path(receipt["registry"]).resolve() != (registry_scope / "registry").resolve():
                    raise RuntimeError("Cleanup refused: isolated registry proof mismatched")
                cleanup["abort"] = h.invoke(h.DRIVER + ".Abort", "technical_failure")
            cleanup["visual_stop"] = h.invoke(h.NS + "ResearchFlowVisualInput.Stop")
            cleanup["play_stop"] = h.bridge({"play": False}, 60)
            until = time.time() + 90
            while h.playing() and time.time() < until:
                time.sleep(2)
            cleanup["stopped"] = not h.playing()
        except Exception as error:
            cleanup["error"] = str(error)
    if output.exists():
        (output / "cleanup.json").write_text(json.dumps(cleanup, indent=2, ensure_ascii=False), encoding="utf-8")
result = json.loads((output / "run.json").read_text(encoding="utf-8"))
if result["status"] != "PASS_EDITOR_SIMULATED":
    raise SystemExit(2)
