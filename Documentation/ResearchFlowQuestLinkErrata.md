# Quest Link errata — 2026-09-22

**Quest Link v207 is currently unusable on this machine due to an XR streaming timing
failure; standalone hardware QA was used instead.**

This is a machine/runtime level fault, not a project rendering fault. It is recorded here
so the next session does not re-diagnose it as a Unity or ResearchFlow problem.

## Symptom

Quest Link runs at approximately 2 FPS, including in Link Home, with Unity Play Mode
stopped. The Meta service log repeatedly reports:

```
ovrError_XRStreamingPluginIssue (-6101)
"Could not retrieve round trip frame timing"
"Prediction interval too high"   (~0.86 s)
```

A prediction interval of ~0.86 s means the compositor cannot establish round-trip frame
timing with the headset at all; it is not a frame-rate budget problem.

## Evidence that this is not the project

| Observation | Source |
| --- | --- |
| Failure reproduces in Link Home with no Unity content presenting | Operator, with Play Mode stopped |
| Failure persists after a full Link restart | Operator |
| `OculusDash` and `com.oculus.xrstreamingclient` both running during failure | Machine + device process listing |
| Quest USB XRSP / Highwind / ADB / Commlib interfaces all present and OK | Machine evidence |
| `scrcpy` not running (ruled out as a USB/adb contender) | Machine evidence |
| Active OpenXR runtime correctly registered as Oculus | `HKLM:\SOFTWARE\Khronos\OpenXR\1` → `oculus_openxr_64.json`, runtime `Oculus 1.207.0` |
| Extra Unity processes were only this project's `AssetImportWorker8/9` | `Win32_Process` command lines, parent PID 43284 |
| Machine not memory starved | 19 GB free of 63.2 GB |

An earlier probe showed Unity reporting `xrDeviceActive: true` with only a head-tracking
device and no controllers, while `OculusDash` was absent from the process list. Unity had
retained a stale OpenXR session across the operator's Link restart. **Do not trust
`XRSettings.isDeviceActive` alone as proof that Link is presenting** — corroborate with
`OculusDash` on the PC and `com.oculus.xrstreamingclient` on the device.

## Consequence for the researcher workflow

`ResearcherSetupView` is a `ScreenSpaceOverlay` canvas on `targetDisplay = 1` and is
deliberately disabled when `Display.displays.Length <= 1`
(`ResearcherSetupView.cs:34`). On a standalone headset there is exactly one display, so
the researcher UI correctly never reaches the participant. The designed researcher path
was therefore the PC — either the Editor `ResearcherConsole` or a second display — which
Link was meant to provide.

With Link unusable, the standalone build had **no way to start a session at all**:
`StartSession` is reachable only from `ResearcherConsole` (Editor only),
`ResearchFlowPlayChecks` (Editor only) and `ResearcherSetupView` (disabled on one display).
The participant would wait at "รอผู้วิจัยเริ่มบทฝึก" indefinitely.

## Resolution adopted

A minimal researcher control channel over adb:
`Assets/VRMaintenanceResearch/ResearchFlow/Runtime/ResearchAdbControlChannel.cs`,
driven by `Tools/researcher-adb.sh`.

Chosen over an Android intent / `BroadcastReceiver` because the file channel needs no Java
plugin, no `AndroidManifest` change and no JNI bridge, and it mirrors the file-command
pattern already proven in this repository (`HandsLabEditor.Poll`).

Constraints held:

- Participant headset stays standalone; no researcher UI on the participant display.
- No alternate session state machine. The channel only calls the existing
  `StartSession`, `ResearcherResume`, `ResearcherAbort`, `SetDemoPass` and
  `ResearchParticipantRegistry`. It never mutates `ResearchSessionFlow` directly.
- DEV / TEST / PILOT only. A participant id is resolved by scanning only the technical
  groups, so a real Thai/Japanese study record is structurally unreachable from the channel.
- Existing guards are not weakened: `DeviceReady` and `LoggerReady` are still required
  before `start` is forwarded.
- Compiled in only when the application identifier ends in `.technical`.
- No server, no networking framework, no new persistence, no companion app.
- Every external command is logged as `RESEARCHER_COMMAND` with `source=ADB`.

## When Link is next attempted

Suggested order, outside project time:

1. Try a different USB port/cable, preferring a known-good USB 3 port.
2. Re-run the Meta Quest Link app's USB test and check the reported bandwidth.
3. Consider a Link/Horizon OS version mismatch; v207 is the version that failed here.
4. Confirm the fault still reproduces in Link Home with no Unity running before
   spending any project time on it.

Standalone is the verified path for this presentation; Link is not required for it.
