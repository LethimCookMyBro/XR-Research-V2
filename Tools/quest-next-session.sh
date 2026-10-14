#!/usr/bin/env bash
# Host-side helper for the next single-wear Quest test (DEV session + input + recorder ON/OFF).
# Ships nothing in the APK; changes no protocol, rendering or gate. See
# Logs/QuestFinalIntegration/NEXT_WEAR_RUNBOOK.md for the order of use.
#
#   ./quest-next-session.sh preflight              read-only device/app/flag check
#   ./quest-next-session.sh probe status|off|on    technical diagnostic flag: arms ONE next launch (app consumes it)
#   ./quest-next-session.sh relaunch               force-stop + start the technical app
#   ./quest-next-session.sh start-full-flow [Thai|Japanese] [Controller|HandTracking|TechnicalBoth] [AB|BA]
#                                                  (default Thai Controller AB) wait for app, worn+tracked HMD,
#                                                  both controllers, floor, logger -> create DEV (test_data) ->
#                                                  existing "start" -> confirm flow left ResearcherSetup and both
#                                                  guards released. REFUSES on timeout; never bypasses a gate.
#                                                  "session" is the old name for the same command.
#   ./quest-next-session.sh render-ab              resolution 1.00 -> 1.10 -> 1.15 -> 1.00 on the running DEV
#                                                  session, then restore its starting candidate; MSAA is separate
#   ./quest-next-session.sh gate                   readiness: HMD, L/R controllers, DeviceReady, flow, session, interaction
#   ./quest-next-session.sh perf start <run>|stop  stream VrApi + markers to Logs/QuestFinalIntegration/<run>/vrapi.txt
#   ./quest-next-session.sh window <label> [sec]   mark <label>_START, wait, mark <label>_END (default 60 s)
#   ./quest-next-session.sh summary <run> [render.jsonl]  app-PID FPS; optional native UTC capture windows
#   ./quest-next-session.sh events <run>           pull latest session events.jsonl, count input events
#   ./quest-next-session.sh selftest               check the summary parser and readiness predicate
#   ./quest-next-session.sh selftest-summary       offline parser regression; no ADB required
set -euo pipefail

cmd="${1:-preflight}"; shift || true
HERE="$(cd "$(dirname "$0")" && pwd)"
if [ "$cmd" = preflight ]; then
  candidate_identity=$(python "$HERE/quest_build_identity.py" "${QUEST_BUILD_RESULT:-$HERE/../Logs/QuestFinalIntegration/reader-20261008/build_result.json}" --source-root "$HERE/.." --source-manifest "${QUEST_BUILD_SOURCE_MANIFEST:-$HERE/../Logs/QuestFinalIntegration/reader-20261008/source-at-build.json}") || exit 3
  EXPECTED_SHA=$(printf '%s' "$candidate_identity" | python -c 'import json,sys; print(json.load(sys.stdin)["apk_sha256"])')
  echo "BUILT_APK_IDENTITY=$candidate_identity (hash agreement is not current-source or wearer acceptance)"
fi
case "$cmd" in summary|selftest-summary) ;;  # Offline evidence needs no device or adb executable.
  *) source "$(dirname "$0")/quest-adb.sh" ;; esac
PKG="${PKG:-th.ac.tmu.vr.researchflow.quest3.technical}"
# Offline parsers/readiness tests must not depend on the foreground profile.
case "$cmd" in summary|selftest|selftest-summary) ;; *) quest_profile_begin || exit 3 ;; esac
SELF="$HERE/$(basename "$0")"  # works whether run as ./x.sh, Tools/x.sh or bash x.sh
LOGS="${QUEST_LOGS_ROOT:-$HERE/../Logs/QuestFinalIntegration}"
EXPECTED_SHA="${EXPECTED_SHA:-}"
export MSYS2_ARG_CONV_EXCL="*"
sh_() { quest_profile_check || return 3; quest_adb shell "$@" </dev/null | tr -d '\r'; }
field() { python -c "import json,sys;d=json.load(sys.stdin);print(eval(sys.argv[1],{'d':d}))" "$1"; }
state_() { "$HERE/researcher-adb.sh" state 2>/dev/null || true; }
verify_apk_sha() { [ "$1" = "$EXPECTED_SHA" ] || { echo "FAIL installed APK sha $1" >&2; return 1; }; }
profile_state() {
  quest_profile_command request '{"command":"state"}' | python -c 'import json,sys;r=json.load(sys.stdin); b=r["transport_identity"]; b.update(serial=sys.argv[1],foreground_activity=sys.argv[2],request_id=r["request_id"]); print(json.dumps(b))' "$QUEST_SERIAL" "$(quest_profile_command identity | field 'd["foreground_activity"]')"
}
freeze_native_scope() {
  local frozen
  frozen=$(profile_state) || return 3
  printf '%s\n' "$frozen" > "$QUEST_PROFILE_BINDING"
}
freeze_scope_reply() {
  printf '%s' "$1" | python -c 'import json,sys;r=json.load(sys.stdin);b=r["request_binding"];b.update(r["transport_identity"]);print(json.dumps(b))' > "$QUEST_PROFILE_BINDING"
}

# Reads a state response on stdin and prints what is still missing; empty = OK.
#   why_not ready          -> may a DEV session start? (same inputs as the app's DeviceReady/LoggerReady)
#   why_not started <id>   -> did <id> leave ResearcherSetup as test data with both guards released?
# The app re-checks readiness itself on "start"; this only waits and explains, it never bypasses.
why_not() {
python -c "
import json,sys
phase, pid = sys.argv[1], (sys.argv[2:] or [None])[0]
try: s = json.load(sys.stdin)['state']
except Exception: print('control channel not answering (app not running/foreground?)'); sys.exit()
h, c, r = s.get('headset') or {}, s.get('controllers') or [], []
if phase == 'ready':
    if not s.get('app_focused'): r.append('app not focused')
    if not (h.get('valid') and h.get('tracked') and h.get('user_present')): r.append('headset not worn/tracked')
    if len(c) != 2 or not all(x.get('tracking_valid') for x in c): r.append('controllers not both tracked (' + ','.join('%s=%s' % (x['side'], x['mode']) for x in c) + ')')
    if not s.get('floor_ready'): r.append('floor not ready')
    if not s.get('logger_ready'): r.append('logger not ready')
    if not s.get('device_ready'): r.append('device_ready=false')
    if s.get('diagnostic_capture'): r.append('heavy diagnostic capture ON (probe off + relaunch)')
else:
    if s.get('flow_state') == 'ResearcherSetup' or s.get('participant_id') != pid: r.append('%s not running (flow %s)' % (pid, s.get('flow_state')))
    if s.get('test_data') is not True: r.append('session is not test_data')
    if len(c) != 2: r.append('expected 2 controller guards')
    for x in c:
        if x.get('suspended') or not x.get('interactions_allowed'):
            r.append('%s guard held (%s)' % (x['side'], x.get('suspension_reason') or ('release trigger+grip' if x.get('waiting_for_neutral') else 'not allowed')))
if s.get('error'): r.append('error: ' + s['error'])
print('; '.join(r))" "$@"
}

summary() {
local render_json=${3:-}
[ -z "$render_json" ] || render_json=$(cygpath -w "$render_json")
python - "$(cygpath -w "$1")" "$(cygpath -w "$2")" "$PKG" "$render_json" <<'PY'
import datetime as dt, json, math, re, statistics as st, sys
# Attribute at ingestion, before aggregating: unrelated producers remain in the untouched raw log.
try:
    mapping = open(sys.argv[2], encoding="utf-8").read()
    raw = open(sys.argv[1], encoding="utf-8", errors="replace").read()
except OSError as ex: sys.exit("REFUSED: cannot read raw VrApi/PID map: " + str(ex))
headers = re.findall(r"^package=(\S+) pid=(\d+) utc=\S+(?: log_time=epoch)?$", mapping, re.M)
if len(headers) != 1 or len(re.findall(r"^package=", mapping, re.M)) != 1 or headers[0][0] != sys.argv[3]:
    sys.exit("REFUSED: expected exactly one PID map for package " + sys.argv[3])
app = headers[0][1]
bindings = re.findall(r"^profile_binding=(.+)$", mapping, re.M)
end_bindings = re.findall(r"^end_profile_binding=(.+)$", mapping, re.M)
if bindings:
    try:
        if len(bindings) != 1 or len(end_bindings) != 1: raise ValueError("missing/duplicate profile lifetime boundary")
        start_binding, end_binding = json.loads(bindings[0]), json.loads(end_bindings[0])
        keys = ("serial", "android_user", "package", "uid", "pid", "process_start_ticks", "boot_id", "build_sha256", "apk_path", "foreground_activity", "instance_id", "session_id", "run_id")
        if not isinstance(end_binding, dict) or any(key not in start_binding or start_binding[key] != end_binding.get(key) for key in keys): raise ValueError("profile/process/session/build changed during capture")
        if str(start_binding["pid"]) != app or start_binding["package"] != sys.argv[3]: raise ValueError("PID/profile mapping disagreement")
        if not start_binding.get("request_id") or not end_binding.get("request_id"): raise ValueError("missing app request UUID boundaries")
    except (ValueError, KeyError, TypeError) as ex: sys.exit("REFUSED: " + str(ex))
processes = re.findall(r"^\s*(\d+)\s+(\S+)\s*$", mapping, re.M)
instances = [(pid, name) for pid, name in processes if name == sys.argv[3] or name.startswith(sys.argv[3] + ":")]
if instances != [(app, sys.argv[3])] or any(pid == app and name != sys.argv[3] for pid, name in processes):
    sys.exit("REFUSED: wrong or mixed expected-package instance mapping")
markers = set(re.findall(r"QUESTRUN\s*(?:\(\s*\d+\))?:\s*PID_(\d+)\b", raw))
if markers != {app}: sys.exit(f"REFUSED: missing or stale PID marker (map {app}, log {sorted(markers)})")
end_maps = re.findall(r"^end_package=(\S+) pid=(\S+) utc=\S+$", mapping, re.M)
if (end_maps and end_maps != [(sys.argv[3], app)]) or ("log_time=epoch" in mapping and not end_maps):
    sys.exit("REFUSED: capture package PID changed or was not checked by 'perf stop'")
captures = None
if sys.argv[4]:
    try:
        events = [json.loads(line) for line in open(sys.argv[4], encoding="utf-8") if line.strip()]
        endings = [e for e in events if e.get("event") == "experiment_end"]
        restores = [e for e in events if e.get("event") == "restore_readback"]
        begins = [e for e in events if e.get("event") == "capture_begin"]
        completes = [e for e in events if e.get("event") == "capture_complete"]
        mode = endings[0].get("mode") if endings else None
        if mode not in ("resolution", "msaa", "predicted_time"): raise ValueError("unknown experiment mode")
        count = 4 if mode == "resolution" else 3
        if len(endings) != 1 or endings[0].get("status") != "completed_windows" or endings[0].get("completed_windows") != count:
            raise ValueError("experiment incomplete/aborted")
        if len(restores) != 1 or (restores[0].get("readback") or {}).get("restored") is not True:
            raise ValueError("native restore not verified")
        refreshes = [float(e["readback"]["refresh_hz"]) for e in events
                     if (e.get("readback") or {}).get("refresh_hz") is not None]
        if any(not math.isfinite(hz) or abs(hz - 72) > .5 for hz in refreshes):
            raise ValueError("native refresh does not match the controlled 72 Hz app target")
        if sorted(e["phase"] for e in begins) != list(range(count)) or sorted(e["phase"] for e in completes) != list(range(count)):
            raise ValueError("missing/duplicate capture boundaries")
        utc = lambda value: dt.datetime.fromisoformat(value.replace("Z", "+00:00")).timestamp()
        captures = {}
        for begin in begins:
            if begin.get("mode") != mode: raise ValueError("mixed experiment modes")
            end = next(e for e in completes if e["phase"] == begin["phase"])
            start, stop = utc(begin["utc"]), utc(end["utc"])
            if stop - start < 30: raise ValueError("short capture window")
            prefix = "RES" if begin["mode"] == "resolution" else begin["mode"].upper()
            captures[prefix + str(begin["phase"])] = (start, stop)
        if "log_time=epoch" not in mapping: raise ValueError("exact UTC correlation needs epoch logcat from 'perf start'")
    except (OSError, ValueError, KeyError, StopIteration) as ex:
        sys.exit("REFUSED: native capture windows: " + str(ex))
windows = {name: [] for name in captures} if captures is not None else {}
cur, invalid, ignored = None, [], 0
for line in raw.splitlines():
    m = re.search(r"QUESTRUN\s*(?:\(\s*\d+\))?:\s*(\S+)_(START|END)", line)
    if m and captures is None:
        name, action = m.groups()
        if action == "START":
            if cur is not None or name in windows: invalid.append(name + ": overlapping/duplicate window")
            cur = name; windows.setdefault(name, [])
        else:
            if cur != name: invalid.append(name + ": END without matching START")
            cur = None
        continue
    if "VrApi" not in line or "FPS=" not in line: continue
    pid = re.search(r"VrApi\s*\(\s*(\d+)\)", line)
    if not pid: invalid.append("unattributed VrApi producer"); continue
    if pid.group(1) != app: ignored += 1; continue
    name = cur or "(unmarked)"
    stamp = None
    if captures is not None:
        timestamp = re.match(r"\s*(\d{9,}\.\d+)\s", line)
        if not timestamp: invalid.append("missing epoch timestamp"); continue
        stamp = float(timestamp[1])
        # Per-second VrApi stats describe the preceding interval; drop the first boundary-straddling second.
        matches = [key for key, (start, stop) in captures.items() if start + 1 <= stamp <= stop]
        if len(matches) > 1: invalid.append("overlapping UTC capture windows"); continue
        if not matches: continue  # Warm-up and samples outside native capture are excluded.
        name = matches[0]
    m = re.search(r"FPS=(\d+)/(\d+).*?Tear=(\d+).*?Stale=(\d+),Stale2/5/10/max=(\d+)/(\d+)/(\d+)/(\d+).*?App=([\d.]+)ms.*?CPU&GPU=([\d.]+)ms", line)
    if not m: invalid.append(name + ": unparseable app VrApi metrics"); continue
    v = [float(x) for x in m.groups()]
    cpu = re.search(r"CPU\d/GPU=(\d+)/(\d+)", line); temp = re.search(r"Temp=([\d.]+)C", line)
    r = dict(stamp=stamp, fps=v[0], target=v[1], tear=v[2], stale=v[3], s2=v[4], s5=v[5], s10=v[6], smax=v[7], app=v[8], cg=v[9],
             lvl=f"{cpu.group(1)}/{cpu.group(2)}" if cpu else "?", temp=float(temp.group(1)) if temp else None)
    windows.setdefault(name, []).append(r)
def p95(x): x = sorted(x); return x[min(len(x) - 1, int(round(.95 * (len(x) - 1))))]
# Only the proven app's display target must be unambiguous; never select a dominant producer.
if cur is not None: invalid.append(cur + ": missing END"); windows.pop(cur, None)
run_targets = sorted({r["target"] for w in windows.values() for r in w})
for name, w in list(windows.items()):
    targets = sorted(set(r["target"] for r in w))
    if not w or len(targets) != 1 or targets[0] <= 0:
        invalid.append(f"{name}: " + (f"no app samples for package pid {app}" if not w else f"ambiguous app FPS target {targets}"))
        del windows[name]
        continue
    if captures is not None:
        start, stop = captures[name]
        times = [r["stamp"] for r in w]
        gaps = [b - a for a, b in zip(times, times[1:])]
        # VrApi is ~1 Hz: 30 s normally gives 29 samples / 28 s span. Allow .25 s jitter,
        # one fewer sample, and one log interval at each edge after the guarded first second.
        if (len(w) < max(2, int(stop - start) - 2) or times[0] > start + 2.25 or times[-1] < stop - 1.25
                or times[-1] - times[0] < stop - start - 2.25 or any(gap <= 0 or gap > 1.25 for gap in gaps)):
            invalid.append(f"{name}: insufficient app metric coverage ({len(w)} samples, span {times[-1]-times[0]:.2f}s, gap range {min(gaps, default=0):.2f}..{max(gaps, default=0):.2f}s)")
            del windows[name]
if captures is not None and run_targets and run_targets != [72]:
    invalid.append(f"native app FPS target must be 72 throughout the run (found {run_targets})")
    windows.clear()
for problem in invalid: print("REJECTED " + problem)
print(f"package pid {app}")
print(f"ignored unrelated VrApi records: {ignored} (raw log retained)")
print("native UTC capture windows; app target 72 Hz; metric coverage checked; warm-up excluded; restore verified" if captures is not None else
      "PID attribution only: markers do not prove experiment completion, restore, or wearer acceptance")
print("window            s  FPS_med  stale_med stale_p95 stale/s  s2+  tear  appGPU_med cpu&gpu_med  lvl  temp")
for name, w in windows.items():
    stale = [r["stale"] for r in w]
    print(f"{name:<16}{len(w):>4}  {st.median(r['fps'] for r in w):>6.0f}  {st.median(stale):>9.1f} {p95(stale):>9.0f} "
          f"{sum(stale)/len(w):>7.2f} {sum(r['s2'] for r in w):>4.0f} {sum(r['tear'] for r in w):>5.0f} "
          f"{st.median(r['app'] for r in w):>10.2f} {st.median(r['cg'] for r in w):>11.2f}  {w[-1]['lvl']:>4} {w[-1]['temp']}")
if not windows: sys.exit("REFUSED: no attributable app windows")
if invalid: sys.exit(1)
PY
}

case "$cmd" in
  preflight)
    binding=$(quest_profile_command identity)
    sha=$(printf '%s' "$binding" | field 'd["build_sha256"]')
    verify_apk_sha "$sha"
    echo "PASS installed APK matches EXPECTED_SHA ($sha)"
    echo "diagnostic flag (next launch): $(bash "$SELF" probe status) (expect OFF for normal research mode)"
    echo "guardian_pause=$(sh_ getprop debug.oculus.guardian_pause) (expect 0)"
    sh_ dumpsys vrpowermanager | grep -E "Virtual proximity state|^ *State:" | sed 's/^ */  /'
    sh_ dumpsys battery | grep -E "level|temperature" | sed 's/^ */  battery /'
    echo "app pid: $(quest_profile_command identity | field 'd["pid"]')"
    echo "logcat buffer: $(sh_ logcat -g | head -1)"
    ;;
  probe)
    case "${1:-status}" in status|off|on) quest_profile_command probe "${1:-status}" | python -c 'import json,sys;print(json.load(sys.stdin))' ;;
      *) echo "probe status|off|on" >&2; exit 2 ;; esac ;;
  relaunch)
    quest_profile_command relaunch
    echo "relaunch targeted the verified Android user; run gate after startup" ;;
  session) exec bash "$SELF" start-full-flow "$@" ;;  # old name; same guarded path
  start-full-flow)
    lang=${1:-Thai} input=${2:-Controller} order=${3:-AB} wait=${WAIT_SECONDS:-180}
    [[ "$lang $input $order" =~ ^(Thai|Japanese)\ (Controller|HandTracking|TechnicalBoth)\ (AB|BA)$ ]] ||
      { echo "usage: start-full-flow [Thai|Japanese] [Controller|HandTracking|TechnicalBoth] [AB|BA]" >&2; exit 2; }
    echo "technical build $PKG | DEV test_data session | $lang $input $order"
    echo "adb: $ADB"
    [ "$(quest_adb get-state 2>/dev/null | tr -d '\r')" = device ] ||
      { echo "FAIL: no Quest attached/authorized ($ADB devices):"; "$ADB" devices; exit 1; }
    deadline=$((SECONDS + wait)) last=
    while :; do
      if [ -z "$(quest_profile_command identity | field 'd["pid"]')" ]; then why="app not running ($0 relaunch)"; else
        st=$(state_)
        flow=$(echo "$st" | field "d['state']['flow_state']" 2>/dev/null || true)
        [ -z "$flow" ] || [ "$flow" = ResearcherSetup ] ||
          { echo "REFUSED: a session is already running (flow $flow); abort it first with researcher-adb.sh abort"; bash "$SELF" gate; exit 1; }
        why=$(echo "$st" | why_not ready)
      fi
      [ -z "$why" ] && break
      [ "$why" = "$last" ] || echo "waiting: $why"; last=$why
      [ $SECONDS -lt $deadline ] || { echo "REFUSED: not ready after ${wait}s: $why"; bash "$SELF" gate; exit 1; }
      sleep 2
    done
    echo "ready: headset tracked, both controllers tracked, floor, logger, device_ready"
    frozen_scope=$(profile_state); printf '%s\n' "$frozen_scope" > "$QUEST_PROFILE_BINDING"
    created=$("$HERE/researcher-adb.sh" create Dev)
    id=$(echo "$created" | field "d['data']['created'] if d['ok'] and d['data']['group'] == 'Dev' and d['data']['test_data'] is True else ''" || true)
    [ -n "$id" ] || { echo "REFUSED: create Dev did not return a DEV test_data record"; echo "$created"; exit 1; }
    res=$("$HERE/researcher-adb.sh" start "$id" "$lang" "$input" "$order")
    [ "$(echo "$res" | field "d['ok']")" = True ] || { echo "REFUSED by app: $(echo "$res" | field "d.get('error')") ($id stays Available)"; bash "$SELF" gate; exit 1; }
    echo "started $id (DEV, test_data) -> $(echo "$res" | field "d['state']['flow_state']")"
    freeze_scope_reply "$res"
    deadline=$((SECONDS + 30)) last=
    while why=$(state_ | why_not started "$id"); [ -n "$why" ]; do
      [ "$why" = "$last" ] || echo "waiting: $why"; last=$why
      [ $SECONDS -lt $deadline ] || { echo "FAIL: $id started but not released after 30 s: $why"; bash "$SELF" gate; exit 1; }
      sleep 1
    done
    bash "$SELF" gate
    echo "FULL FLOW READY: $id DEV test_data, flow left ResearcherSetup, both guards released" ;;
  render-ab)
    # Resolution only: 1.00 -> 1.10 -> 1.15 -> 1.00 (5 s warm-up + 30 s capture each) at ONE flow state.
    # MSAA 2 vs 4 is a separate later run; never both. Mark only the app's capture stage, not warm-up.
    res=$("$HERE/researcher-adb.sh" benchmark resolution)
    [ "$(echo "$res" | field "d['ok']")" = True ] || { echo "REFUSED: $(echo "$res" | field "d.get('error')")"; exit 1; }
    freeze_scope_reply "$res"
    prev= stage= status=unknown fails=0
    while :; do
      b=$(state_ | field "(lambda b: '%s %s %s %s' % (b['Running'], b['phase'], b['status'], b['window']))(d['state']['benchmark'])" 2>/dev/null) ||
        { fails=$((fails + 1)); [ $fails -lt 5 ] || { echo "state unreadable 5x; cancel benchmark and check $0 gate"; "$HERE/researcher-adb.sh" benchmark_cancel >/dev/null || true; break; }; sleep 1; continue; }
      fails=0
      set -- $b
      status=$3
      [ "$1" = True ] || [ "$3" = pending ] || break
      if [ "$1" = True ] && [ "$2:$4" != "$stage" ]; then
        [ -z "$prev" ] || { sh_ log -t QUESTRUN "RES${prev}_END"; prev=; }
        stage="$2:$4"
        echo "window $(($2 + 1))/4 $4 (researcher key RES$2; do not name it to the wearer; keep flow unchanged)."
        if [ "$4" = capture ]; then
          sh_ log -t QUESTRUN "RES${2}_START"; prev=$2
          echo "Ask clarity near the end of capture."
        fi
      fi
      sleep 1
    done
    [ -z "$prev" ] || sh_ log -t QUESTRUN "RES${prev}_END"
    echo "benchmark: $b (key RES0=1.00 RES1=1.10 RES2=1.15 RES3=1.00; aborted = windows invalid, re-run later)"
    # The app verifies its starting candidate after the ladder; old APKs use the 1.00 / MSAA 2 baseline.
    for _ in 1 2 3 4 5; do
      restore=$(state_ | field "(lambda b: '%s %.2f %s' % (b.get('restore_verified'), b.get('baseline_scale', 1), b.get('baseline_msaa', 2)))(d['state']['benchmark'])" 2>/dev/null || echo 'None ? ?')
      set -- $restore; r=$1; baseline_scale=$2; baseline_msaa=$3
      [ "$r" = None ] || break; sleep 1
    done
    case "$r" in True) echo "RESTORE VERIFIED: native readback starting scale $baseline_scale / MSAA $baseline_msaa / predicted OFF / starting eye size" ;;
      *) echo "RESTORE NOT VERIFIED ($r): do not use these windows; relaunch before the next test" ;; esac
    [ "$status" = completed_windows ] && [ "$r" = True ] || exit 1 ;;
  gate)
    echo "app pid: $(quest_profile_command identity | field 'd["pid"]')"
    "$HERE/researcher-adb.sh" state | python -c "
import json,sys;s=json.load(sys.stdin)['state']
print('app_focused', s.get('app_focused'), '| headset', s.get('headset'), '| floor_ready', s.get('floor_ready'), '| device_ready', s['device_ready'])
for c in s.get('controllers') or []: print('  %-5s' % c['side'], {k: c[k] for k in ('mode','tracking_valid','interactions_allowed','suspended','suspension_reason')})
print('flow', s['flow_state'], '| session', s['participant_id'], '| paused', s['paused'], '| can_interact', s.get('can_interact'), '| transitioning', s.get('transitioning'))
print('logger_ready', s['logger_ready'], '| error', s['error'], '| diagnostic_capture', s.get('diagnostic_capture'))" ;;
  perf)
    case "${1:-}" in
      start) run="${2:?run name}"; [[ "$run" =~ ^[A-Za-z0-9][A-Za-z0-9_-]{0,159}$ ]] || { echo "unsafe run name" >&2; exit 2; }
             binding=$(profile_state); user=$(printf '%s' "$binding" | field 'd["android_user"]'); pids=$(printf '%s' "$binding" | field 'd["pid"]')
             capture="$LOGS/.vrapi-$QUEST_SERIAL-u$user"; [ ! -e "$capture.pid" ] || { echo "REFUSED: profile capture already recorded; stop/review it first" >&2; exit 1; }
             out="$LOGS/$run/profile-$QUEST_SERIAL-u$user"; [ ! -e "$out" ] || { echo "REFUSED: immutable perf output already exists" >&2; exit 1; }; mkdir -p "$out"
             printf '%s\n' "$binding" > "$out/profile-binding.json"
             printf '%s\n' "$binding" > "$QUEST_PROFILE_BINDING"
             { echo "package=$PKG pid=$pids utc=$(date -u +%FT%TZ) log_time=epoch"; echo "$pids $PKG"; echo "profile_binding=$binding"; } > "$out/pid-map.txt"
             quest_profile_check
             nohup "$ADB" -s "$QUEST_SERIAL" logcat -v time -v epoch -T 1 -s VrApi:I QUESTRUN:I > "$out/vrapi.txt" 2>&1 &
             echo $! > "$capture.pid"; echo "$out" > "$capture.out"
             sh_ log -t QUESTRUN "PID_$pids"
             echo "streaming -> $out/vrapi.txt (bound user $user app pid $pids)" ;;
      stop) user=$(quest_profile_command identity | field 'd["android_user"]'); capture="$LOGS/.vrapi-$QUEST_SERIAL-u$user"
            if [ -f "$capture.pid" ] && kill "$(cat "$capture.pid")" 2>/dev/null; then
              out=$(cat "$capture.out"); end=null
              export QUEST_PROFILE_BINDING="$(cygpath -w "$out/profile-binding.json")"
              if quest_profile_check; then end=$(profile_state); fi
              echo "end_profile_binding=$end" >> "$out/pid-map.txt"
              pids=$(printf '%s' "$end" | field 'd["pid"]' 2>/dev/null || echo none)
              echo "end_package=$PKG pid=$pids utc=$(date -u +%FT%TZ)" >> "$out/pid-map.txt"
              rm -f "$capture.out" "$capture.pid"; echo stopped
            else echo "not running"; fi ;;
      *) echo "perf start <run>|stop" >&2; exit 2 ;;
    esac ;;
  mark) freeze_native_scope; sh_ log -t QUESTRUN "${1:?label}"; echo "marked $1" ;;
  window)
    freeze_native_scope
    sh_ log -t QUESTRUN "${1:?label}_START"; echo "$1 START (${2:-60} s)"; sleep "${2:-60}"
    sh_ log -t QUESTRUN "$1_END"; echo "$1 END" ;;
  summary)
    out="$LOGS/${1:?run name}"
    if [ ! -f "$out/vrapi.txt" ]; then
      captures=(); for path in "$out"/profile-*/vrapi.txt; do [ ! -f "$path" ] || captures+=("$path"); done
      [ "${#captures[@]}" -eq 1 ] || { echo "REFUSED: select one explicit run/profile-serial-uN capture" >&2; exit 1; }
      out="$(dirname "${captures[0]}")"
    fi
    summary "$out/vrapi.txt" "$out/pid-map.txt" "${2:-}" ;;
  events)
    echo "events now use immutable profile-scoped exports, not latest /sdcard logs"
    freeze_native_scope
    "$HERE/researcher-adb.sh" export
    python "$(cygpath -w "$HERE/research_export.py")" pull-queue --adb "$(cygpath -w "$ADB")" --serial "$QUEST_SERIAL" --destination "D:\VR_TMU\ResearchData" ;;
  selftest|selftest-summary)
    t=$(mktemp); cat > "$t" <<'EOF'
09-24 14:59:59.000 I/QUESTRUN( 1): PID_25484
09-24 15:00:00.000 I/QUESTRUN( 1): ON_START
09-24 15:00:01.000 I/VrApi   (25484): FPS=72/72,Prd=24ms,Tear=0,Early=0,Stale=4,Stale2/5/10/max=1/0/0/2,VSnc=1,Lat=-4,Fov=0,CPU4/GPU=2/2,1382/456MHz,OC=3F,TA=0/38/38,SP=N/N/N,Mem=1555MHz,Free=3461MB,PLS=0,Temp=39.0C/0.0C,TW=0.83ms,App=10.10ms,GD=0.00ms,CPU&GPU=12.00ms,LCnt=1
09-24 15:00:02.000 I/VrApi   (25484): FPS=72/72,Prd=24ms,Tear=1,Early=0,Stale=6,Stale2/5/10/max=0/0/0/1,VSnc=1,Lat=-4,Fov=0,CPU4/GPU=2/2,1382/456MHz,OC=3F,TA=0/38/38,SP=N/N/N,Mem=1555MHz,Free=3461MB,PLS=0,Temp=39.5C/0.0C,TW=0.83ms,App=9.90ms,GD=0.00ms,CPU&GPU=11.00ms,LCnt=1
09-24 15:00:03.000 I/QUESTRUN( 1): ON_END
09-24 15:00:04.000 I/VrApi   (25484): FPS=72/72,Prd=24ms,Tear=0,Early=0,Stale=0,Stale2/5/10/max=0/0/0/0,VSnc=1,Lat=-4,Fov=0,CPU4/GPU=2/2,1382/456MHz,OC=3F,TA=0/38/38,SP=N/N/N,Mem=1555MHz,Free=3461MB,PLS=0,Temp=39.0C/0.0C,TW=0.83ms,App=8.00ms,GD=0.00ms,CPU&GPU=9.00ms,LCnt=1
EOF
    m=$(mktemp); printf 'package=%s pid=25484 utc=selftest\n25484 %s\n1234 com.oculus.vrshell\n' "$PKG" "$PKG" > "$m"
    got=$(summary "$t" "$m"); echo "$got"
    echo "$got" | grep -Eq "^ON +2 +72 +5\.0 +6 +5\.00 +1 +1 +10\.00 +11\.50" &&
    echo "$got" | grep -Eq "^\(unmarked\) +1 .* 0\.00 " || { echo "SELFTEST FAIL summary"; exit 1; }
    # A genuine unrelated producer (even with another display target) must not invalidate app samples.
    extra=$(sed -n '3p' "$t" | sed 's/(25484)/(1234)/; s/FPS=72\/72/FPS=36\/90/; s/Stale=4/Stale=99/; s/App=10.10ms/App=99.00ms/')
    sed -i "4i$extra" "$t"
    got=$(summary "$t" "$m")
    echo "$got" | grep -Eq "^ON +2 +72 +5\.0 +6 +5\.00 +1 +1 +10\.00 +11\.50" &&
    echo "$got" | grep -Fq 'ignored unrelated VrApi records: 1' || { echo "SELFTEST FAIL unrelated producer"; exit 1; }
    bad=$(mktemp)
    reject() { rc=0; summary "$bad" "$m" "${2:-}" >"$bad.result" 2>&1 || rc=$?; [ "$rc" != 0 ] && grep -Fq "$1" "$bad.result" || { echo "SELFTEST FAIL rejection: $1"; cat "$bad.result"; exit 1; }; }
    sed 's/(25484)/(1234)/g' "$t" > "$bad"
    reject 'no app samples'
    cp "$t" "$bad"
    # Change just one expected-app target; a single 90 Hz window is attributable, mixed targets are not.
    sed -i '5s/FPS=72\/72/FPS=72\/90/' "$bad"
    reject 'ambiguous app FPS target'
    sed 's/App=10.10ms/App=bad/' "$t" > "$bad"
    reject 'unparseable app VrApi metrics'
    sed 's/PID_25484/PID_1234/' "$t" > "$bad"
    reject 'missing or stale PID marker'
    cp "$t" "$bad"; echo "9999 $PKG" >> "$m"
    reject 'wrong or mixed expected-package instance mapping'
    sed -i '$d' "$m"
    sed -i "s/^package=$PKG /package=wrong.package /" "$m"
    reject 'expected exactly one PID map'
    sed -i "s/^package=wrong.package /package=$PKG /" "$m"
    sed '/ON_END/d' "$t" > "$bad"
    reject 'missing END'
    rc=0; summary "$t" /nonexistent/pid-map.txt >/dev/null 2>&1 || rc=$?
    [ "$rc" != 0 ] || { echo "SELFTEST FAIL summary without PID map"; exit 1; }
    # Exact UTC filtering excludes an intentionally bad warm-up sample and rejects aborted captures.
    j=$(mktemp)
    python - "$(cygpath -w "$t")" "$(cygpath -w "$m")" "$(cygpath -w "$j")" "$PKG" "$(cygpath -w "$bad")" <<'PY'
import datetime as dt, json, pathlib, sys
t, m, j = map(pathlib.Path, sys.argv[1:4]); base = 1790935200
app = next(line for line in t.read_text().splitlines() if 'VrApi' in line and '(25484)' in line)
payload = app.split(': ', 1)[1]
marker = f'{base}.000 I/QUESTRUN( 1): PID_25484'
lines = [marker]
variants = {name: [marker] for name in ('targets', 'all90', 'sparse', 'gap', 'late', 'early', 'duplicate', 'cadence')}
events = []
utc = lambda stamp: dt.datetime.fromtimestamp(stamp, dt.timezone.utc).isoformat()
for phase in range(4):
    start = base + phase * 40 + 5
    lines += [f'{start-2}.000 I/VrApi (25484): ' + payload.replace('Stale=4', 'Stale=99'),
              f'{start}.500 I/VrApi (25484): ' + payload.replace('Stale=4', 'Stale=98'),
              f'{start+1}.000 I/VrApi (1234): ' + payload.replace('FPS=72/72', 'FPS=36/90')]
    for second in range(1, 30):
        line = f'{start+second}.000 I/VrApi (25484): ' + payload
        lines.append(line)
        for name, variant in variants.items():
            if name == 'sparse' and second != 1 or name == 'late' and second < 4 or name == 'early' and second > 26: continue
            value = line.replace('FPS=72/72', 'FPS=72/90') if name == 'all90' or name == 'targets' and phase == 1 else line
            if name == 'gap' and second == 15: value = line.replace('(25484)', '(1234)').replace('FPS=72/72', 'FPS=36/90')
            if name == 'cadence':
                if second == 29: continue
                value = f'{start + .05 + second * 1.03:.3f} I/VrApi (25484): ' + payload
            variant.append(value)
            if name == 'duplicate' and second == 12: variant.append(value)
    events += [dict(event=event, mode='resolution', phase=phase, utc=utc(stamp), readback=dict(refresh_hz=72))
               for event, stamp in [('capture_begin', start), ('capture_complete', start+30)]]
events += [dict(event='experiment_end', mode='resolution', status='completed_windows', completed_windows=4), dict(event='restore_readback', readback=dict(restored=True))]
t.write_text('\n'.join(lines)+'\n'); j.write_text('\n'.join(map(json.dumps, events))+'\n')
m.write_text(f'package={sys.argv[4]} pid=25484 utc=selftest log_time=epoch\n25484 {sys.argv[4]}\nend_package={sys.argv[4]} pid=25484 utc=selftest\n')
for name, variant in variants.items(): pathlib.Path(sys.argv[5] + '.' + name).write_text('\n'.join(variant)+'\n')
PY
    got=$(summary "$t" "$m" "$j")
    [ "$(echo "$got" | grep -Ec '^RES[0-3] +29 +72 +4\.0 +4 +4\.00')" = 4 ] || { echo "SELFTEST FAIL UTC warm-up exclusion"; echo "$got"; exit 1; }
    for variant in targets all90; do cp "$bad.$variant" "$bad"; reject 'native app FPS target' "$j"; done
    for variant in sparse gap late early duplicate; do cp "$bad.$variant" "$bad"; reject 'app metric coverage' "$j"; done
    got=$(summary "$bad.cadence" "$m" "$j")
    [ "$(echo "$got" | grep -Ec '^RES[0-3] +28 +72')" = 4 ] || { echo "SELFTEST FAIL UTC cadence tolerance"; echo "$got"; exit 1; }
    cp "$t" "$bad"; sed -i 's/"refresh_hz": 72/"refresh_hz": 90/g' "$j"
    reject 'native refresh' "$j"
    sed -i 's/"refresh_hz": 90/"refresh_hz": 72/g' "$j"
    sed -i 's/"refresh_hz": 72/"refresh_hz": null/g' "$j"
    got=$(summary "$t" "$m" "$j")
    [ "$(echo "$got" | grep -Ec '^RES[0-3] +29 +72')" = 4 ] || { echo "SELFTEST FAIL optional native refresh"; echo "$got"; exit 1; }
    sed -i 's/"refresh_hz": null/"refresh_hz": 72/g' "$j"
    cp "$t" "$bad"; sed -i 's/"restored": true/"restored": false/' "$j"
    reject 'native restore not verified' "$j"
    sed -i 's/"restored": false/"restored": true/; s/"completed_windows",/"aborted",/' "$j"
    reject 'experiment incomplete/aborted' "$j"
    sed -i 's/pid=25484 utc=selftest$/pid=9999 utc=selftest/' "$m"
    reject 'capture package PID changed'
    sed -i '$d' "$m"
    reject 'capture package PID changed'
    rm -f "$t" "$m" "$bad" "$bad.result" "$j" "$bad.targets" "$bad.all90" "$bad.sparse" "$bad.gap" "$bad.late" "$bad.early" "$bad.duplicate" "$bad.cadence"
    [ "$cmd" != selftest-summary ] || { echo "SELFTEST SUMMARY PASS"; exit 0; }
    # Readiness predicate on the 123718 idle-launch state shape (Logs/QuestFinalIntegration/p14-launch/state.json).
    idle='{"state":{"flow_state":"ResearcherSetup","participant_id":null,"test_data":null,"device_ready":false,"app_focused":true,"headset":{"valid":true,"tracked":false,"user_present":false},"floor_ready":false,"controllers":[{"side":"Left","mode":"None","tracking_valid":false,"interactions_allowed":false,"suspended":true,"suspension_reason":"researcher_setup","waiting_for_neutral":true},{"side":"Right","mode":"None","tracking_valid":false,"interactions_allowed":false,"suspended":true,"suspension_reason":"researcher_setup","waiting_for_neutral":true}],"diagnostic_capture":false,"logger_ready":true,"error":""}}'
    ready=$(echo "$idle" | sed -e 's/"tracked":false/"tracked":true/; s/"user_present":false/"user_present":true/; s/"floor_ready":false/"floor_ready":true/; s/"device_ready":false/"device_ready":true/; s/"mode":"None","tracking_valid":false/"mode":"Controller","tracking_valid":true/g')
    started=$(echo "$ready" | sed -e 's/"ResearcherSetup","participant_id":null,"test_data":null/"TutorialWelcome","participant_id":"DEV001","test_data":true/; s/"interactions_allowed":false,"suspended":true,"suspension_reason":"researcher_setup","waiting_for_neutral":true/"interactions_allowed":true,"suspended":false,"suspension_reason":null,"waiting_for_neutral":false/g')
    neutral=$(echo "$started" | sed -e '0,/"interactions_allowed":true,"suspended":false,"suspension_reason":null,"waiting_for_neutral":false/s//"interactions_allowed":false,"suspended":false,"suspension_reason":null,"waiting_for_neutral":true/')
    expect() { [ "$2" = "$3" ] || { echo "SELFTEST FAIL $1: got '$2' want '$3'"; exit 1; }; }
    expect idle "$(echo "$idle" | why_not ready)" "headset not worn/tracked; controllers not both tracked (Left=None,Right=None); floor not ready; device_ready=false"
    expect ready "$(echo "$ready" | why_not ready)" ""
    expect diag "$(echo "$ready" | sed 's/"diagnostic_capture":false/"diagnostic_capture":true/' | why_not ready)" "heavy diagnostic capture ON (probe off + relaunch)"
    expect error "$(echo "$ready" | sed 's/"error":""/"error":"x"/' | why_not ready)" "error: x"
    expect not-started "$(echo "$ready" | why_not started DEV001)" "DEV001 not running (flow ResearcherSetup); session is not test_data; Left guard held (researcher_setup); Right guard held (researcher_setup)"
    expect other-id "$(echo "$started" | why_not started DEV002)" "DEV002 not running (flow TutorialWelcome)"
    expect not-test "$(echo "$started" | sed 's/"test_data":true/"test_data":false/' | why_not started DEV001)" "session is not test_data"
    expect neutral "$(echo "$neutral" | why_not started DEV001)" "Left guard held (release trigger+grip)"
    expect released "$(echo "$started" | why_not started DEV001)" ""
    expect no-app "$(echo 'ERROR' | why_not ready)" "control channel not answering (app not running/foreground?)"
    # adb resolution: a broken explicit ADB fails at once (exit 3), never falls into a wait loop.
    t0=$SECONDS; rc=0; ADB=/nonexistent/adb.exe bash "$HERE/researcher-adb.sh" state >/dev/null 2>&1 || rc=$?
    expect bad-adb "$rc/$((SECONDS - t0 < 3))" "3/1"
    expect unity-first "$(env -u ADB bash -c "source '$HERE/quest-adb.sh'; echo \$ADB")" "/c/Program Files/Unity/Hub/Editor/6000.3.20f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
    # The same installed-hash predicate is used by the native/profile-bound preflight.
    rc=0; verify_apk_sha "$(printf '%064d' 0)" >/dev/null 2>&1 || rc=$?
    expect installed-hash-mismatch "$rc" "1"
    echo "SELFTEST PASS" ;;
  *) echo "unknown command: $cmd" >&2; exit 2 ;;
esac
