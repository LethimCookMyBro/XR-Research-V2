#!/usr/bin/env bash
# Researcher control CLI for the STANDALONE ResearchFlow technical build.
#
# Quest Link is unusable on this machine (ovrError_XRStreamingPluginIssue -6101), so the
# researcher drives the standalone headset from the laptop over adb instead of from a
# second display. See Documentation/ResearchFlowQuestLinkErrata.md.
#
# User-aware ContentProvider is app-owned and restricted to the technical build.
# The app consumes the same command mailbox and returns the UUID/profile-bound reply.
#
# DEV / TEST / PILOT only. The app itself refuses real Thai/Japanese study IDs.
#
# Usage:
#   ./researcher-adb.sh state
#   ./researcher-adb.sh list   Dev
#   ./researcher-adb.sh create Dev
#   ./researcher-adb.sh start  DEV001 Thai Controller AB_P [fixed|legacy_movable]   (order AB|BA|AB_P|AB_Q|BA_P|BA_Q; omit = counterbalance; UI policy default fixed)
#   ./researcher-adb.sh resume
#   ./researcher-adb.sh calibrate_anchor   (wearer standing/seated at the station; Welcome or Task n Ready only)
#   ./researcher-adb.sh demo   pass|fail
#   ./researcher-adb.sh abort  [withdrawal|safety_stop|technical_failure|other] [invalid]   (no reason = other, never blocks a stop)
#   ./researcher-adb.sh amend_reason DEV001 withdrawal|safety_stop|technical_failure|other   (after abort; keeps first reason + history)
#   ./researcher-adb.sh export
#   ./researcher-adb.sh export_saved DEV001 <session> <run>
#   ./researcher-adb.sh benchmark resolution|predicted_time|msaa
#   ./researcher-adb.sh benchmark_cancel
#   ./researcher-adb.sh drag_filter on|off     (technical drag A/B; resets to on at app launch)
#   ./researcher-adb.sh motion_trace [seconds]  (DEV/TEST/PILOT technical only; 5-30 s in memory, one file afterwards)
#   ./researcher-adb.sh frame_timing [seconds]  (technical dev build; 5-60 s FrameTimingManager main/render/GPU percentiles)
#   ./researcher-adb.sh render_set 1.10 4        (technical dev build; live URP scale/MSAA for wear-round A/B, not persisted)
#   ./researcher-adb.sh view_probe '<plan json>' (idle, UNWORN headset only; e.g. '{"presets":["sign"],"configs":[[1.1,2]],"frames":6,"yaw_step":0.02,"hold":8}')
set -euo pipefail

source "$(dirname "$0")/quest-adb.sh"  # sets ADB or exits 3 with the paths tried
PKG="${PKG:-th.ac.tmu.vr.researchflow.quest3.technical}"
export MSYS2_ARG_CONV_EXCL="*"

cmd="${1:-state}"
case "$cmd" in
  state)  body='{"command":"state"}' ;;
  haptic_probe) body='{"command":"haptic_probe"}' ;;
  calibrate_anchor) body='{"command":"calibrate_anchor"}' ;;
  list)   body="{\"command\":\"list\",\"group\":\"${2:-Dev}\"}" ;;
  create) body="{\"command\":\"create\",\"group\":\"${2:-Dev}\"}" ;;
  start)
    [ $# -ge 2 ] || { echo "usage: start <participant> [language] [input] [order]" >&2; exit 2; }
    body="{\"command\":\"start\",\"participant\":\"$2\",\"language\":\"${3:-Thai}\",\"input\":\"${4:-Controller}\""
    [ -n "${5:-}" ] && body="$body,\"order\":\"$5\""
    # Participant UI policy: fixed (default) | legacy_movable (technical regression of the retired drag UI only)
    if [ -n "${6:-}" ]; then case "$6" in fixed|legacy_movable) body="$body,\"ui_policy\":\"$6\"" ;; *) echo 'ui_policy must be fixed or legacy_movable' >&2; exit 2 ;; esac; fi
    body="$body}"
    ;;
  resume) body='{"command":"resume"}' ;;
  abort)
    reason=""; invalid=false
    for arg in "${@:2}"; do
      case "$arg" in
        invalid) invalid=true ;;
        withdrawal|safety_stop|technical_failure|other) reason="$arg" ;;
        *) echo "usage: abort [withdrawal|safety_stop|technical_failure|other] [invalid]" >&2; exit 2 ;;
      esac
    done
    body="{\"command\":\"abort\",\"invalid\":$invalid"
    [ -n "$reason" ] && body="$body,\"reason\":\"$reason\""
    body="$body}" ;;
  amend_reason)
    [ $# -eq 3 ] || { echo "usage: amend_reason <DEV/TEST/PILOT participant> withdrawal|safety_stop|technical_failure|other" >&2; exit 2; }
    [[ "$2" =~ ^[A-Za-z0-9_-]+$ ]] || { echo "invalid participant" >&2; exit 2; }
    case "$3" in withdrawal|safety_stop|technical_failure|other) ;; *) echo "invalid reason" >&2; exit 2 ;; esac
    body="{\"command\":\"amend_reason\",\"participant\":\"$2\",\"reason\":\"$3\"}" ;;
  demo)   if [ "${2:-pass}" = "fail" ]; then body='{"command":"demo","pass":false}'; else body='{"command":"demo","pass":true}'; fi ;;
  export) body='{"command":"export"}' ;;
  export_saved)
    [ $# -eq 4 ] || { echo "usage: export_saved <DEV/TEST/PILOT participant> <session> <run>" >&2; exit 2; }
    for identity in "$2" "$3" "$4"; do [[ "$identity" =~ ^[A-Za-z0-9_-]+$ ]] || { echo "invalid saved identity" >&2; exit 2; }; done
    body="{\"command\":\"export_saved\",\"participant\":\"$2\",\"session_id\":\"$3\",\"run_id\":\"$4\"}" ;;

  technical_scenario) order="${2:-AB}"; case "$order" in AB|BA) ;; *) echo 'order must be AB or BA' >&2; exit 2 ;; esac
    mode="${3:-msaa}"; case "$mode" in msaa|resolution) ;; *) echo 'render mode must be msaa or resolution' >&2; exit 2 ;; esac
    if [ "$mode" = resolution ]; then
      preflight="$(bash "$0" state)"
      if ! printf '%s' "$preflight" | python -c 'import json,sys; r=json.load(sys.stdin); sys.exit(0 if r.get("ok") is True and "resolution" in r.get("state",{}).get("supported_scenario_render_modes",[]) else 1)'; then
        echo 'Installed app does not support the isolated resolution scenario; no scenario started.' >&2
        exit 2
      fi
    fi
    body="{\"command\":\"technical_scenario\",\"order\":\"$order\",\"render_mode\":\"$mode\",\"simulated_presence\":true}" ;;
  benchmark)
    mode="${2:?usage: benchmark resolution|predicted_time|msaa [selected_scale]}"
    case "$mode" in resolution|predicted_time) body="{\"command\":\"benchmark\",\"mode\":\"$mode\"}" ;;
      msaa)
        case "${3:-}" in 1|1.0|1.00|1.1|1.10|1.15) ;;
          *) echo 'MSAA requires the selected render scale: 1.00, 1.10 or 1.15' >&2; exit 2 ;; esac
        body="{\"command\":\"benchmark\",\"mode\":\"msaa\",\"selected_scale\":$3}" ;;
      *) echo 'Unknown benchmark mode' >&2; exit 2 ;; esac ;;
  benchmark_cancel) body='{"command":"benchmark_cancel"}' ;;
  drag_filter) case "${2:-}" in on) body='{"command":"drag_filter","enabled":true}' ;; off) body='{"command":"drag_filter","enabled":false}' ;;
               *) echo "usage: drag_filter on|off" >&2; exit 2 ;; esac ;;
  motion_trace) secs="${2:-20}"; [[ "$secs" =~ ^[0-9]+$ ]] || { echo "usage: motion_trace [seconds 5-30]" >&2; exit 2; }
                body="{\"command\":\"motion_trace\",\"seconds\":$secs}" ;;
  frame_timing) secs="${2:-20}"; [[ "$secs" =~ ^[0-9]+$ ]] || { echo "usage: frame_timing [seconds 5-60]" >&2; exit 2; }
                body="{\"command\":\"frame_timing\",\"seconds\":$secs}" ;;
  render_set) [[ "${2:-}" =~ ^[0-9]+(\.[0-9]+)?$ && "${3:-}" =~ ^[124]$ ]] || { echo "usage: render_set <scale 0.8-1.3> <msaa 1|2|4>" >&2; exit 2; }
              body="{\"command\":\"render_set\",\"scale\":$2,\"msaa\":$3}" ;;
  view_probe) plan="${2:?usage: view_probe '<plan json>'}"
              printf '%s' "$plan" | python -c 'import json,sys; json.load(sys.stdin)' || { echo "plan must be JSON" >&2; exit 2; }
              body="{\"command\":\"view_probe\",\"plan\":$plan}" ;;
  *) echo "unknown command: $cmd" >&2; exit 2 ;;
esac

quest_profile_command request "$body"
