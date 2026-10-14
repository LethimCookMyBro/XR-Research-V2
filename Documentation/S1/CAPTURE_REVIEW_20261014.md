# User-capture review — 14 October 2026

Scope: two original Quest JPEGs and two original Quest recordings, decoded locally; representative full-size frames plus overview sheets were inspected. This is a sampled temporal/visual review, NOT a new wearer experiment or a certification that every frame/interaction was tested. Raw images/video/audio stay private under ignored `Logs/AdvisorReview-20261014/` and are not published here.

## Evidence identity

| Local source basename | Length / hash |
|---|---|
| `...technical-20261014-172910.jpg` | SHA256 `4daef9a19b8a4ea56c354f90d675a44ac34c55ee7395e51d3f852c0dd0b0646c` |
| `...technical-20261014-172922.jpg` | SHA256 `22ce34ec0589e07e25a76a0165a5f1ba33202835d5545d90c468f863a872b3b1` |
| `...technical-20261014-173014-0.mp4` | 17.318 s; SHA256 `89677af1125cb763e6b59781e6d39d06eb3b4b91baa66bb60fc43914cc7692b3` |
| `...technical-20261014-173104-0.mp4` | 85.017 s; SHA256 `0fa3459984dca25e86b872ae1e02f7e7da3b389e4ea7bca6379bc3a8c93f5b62` |

Both recordings decode as 1920x1080 HEVC with AAC stereo. The unusual ffprobe nominal r_frame_rate value is NOT app FPS; do not use it to judge performance. A local faster-whisper base transcription attempt with Thai/VAD returned no speech segments in either clip. That does not prove the audio track is absent; no spoken user complaints were invented from these recordings.

The meeting automatic transcript (`เสียง 14 Pj_original.txt`, original audio 1090.7 s) was separately read at 12:58–16:14. It contains obvious recognition errors. Key supported topics: storage choice, ethics-consistent disposal, demo preparation, and restricting arbitrary UI movement. Original audio was not independently listened through here. Use the user's provided summary for the tentative 22/23 October afternoon target; the ASR date words alone are unreliable.

Build provenance: package name in capture filenames is not a build hash. Last earlier install evidence was Q4; Q5 was built-only. An attempted fresh package hash read during this review failed after USB disconnected again, so the captured APK version remains UNVERIFIED. Do not assign new failures to Q5 solely by date.

## Findings and fixes to reproduce

| ID | Evidence and bounded observation | Required action / priority |
|---|---|---|
| C01 | Short clip shows the panel at a steep angle, then close enough to be cropped, and later moved back. Free positioning remains part of the captured interaction. | New advisor fixed-UI policy replaces this freedom. Remove free drag and the required drag training together; author reachable station anchors. P1 |
| C02 | Long clip sampled around 30–45 s shows `กำลังโหลดวิดีโอ`, no decoded picture, and `--:-- / --:--`. The user changes/returns to surfaces. The footage alone does not establish a continuous 20-second prepare timeout. | Reproduce Android prepare/first-frame/error/retry. Record generation/time/path and provide bounded recovery. Do not claim permanent decoder failure from a sampled frame. P1 |
| C03 | Full-size `long_007.jpg` and `long_009.jpg`: green training success text occupies the same bottom region as the Video Play control/time. | Reserve separate feedback and transport rectangles. Test every locale/loading/playing state with actual hit targets and visible bounds, not only text metrics. P1 |
| C04 | Full-size `long_006.jpg`: next caption is clipped behind the paging/footer region of Illustrated view. | Fix viewport/content/footer ownership, scrolling and padding; don't shrink all fonts. P1 |
| C05 | The active tutorial instruction asks to drag the panel, while the Illustrated picture shows a hardware trainer. `long_015.jpg` asks Examine but displays the same generic ray-to-menu sequence. | Separate tutorial coaching from the selected document. Action-specific controller/model demonstration; remove obsolete panel-drag lesson in fixed mode. P1 |
| C06 | Captures show progress 8/24, 10/24, 12/24 and persistent previous-success messages, with multiple similar controller drawings. User separately reports training is too long/confusing. | Condense meaningful presentation groups; map required skills to real events once. No invented measured learning result or blanket deletion of essential skills. P1 |
| C07 | A solid black rectangular band appears in the original setup JPEG and sampled illustrated frame over the header/background. Owner and cause are unconfirmed. | Inspect captured-build geometry/canvas/material/debug overlay ownership. Do not blindly remove reflection probes or colliders. P1 investigation |
| C08 | Researcher Setup shows small English technical controls in the HMD (DEV/test data); another setup surface shows Thai readiness. | Keep operator vs participant surfaces distinct; verify locale and entry path. Technical operator UI in DEV is not by itself a production privacy leak. P2 |
| C09 | An empty portion of the table is visible while Tutorial asks to use a module; sampled view does not show the whole authored station. | Verify actual Trainer_FX1/prop visibility and reach in the captured build, rather than infer missing scene objects. Layout review needed. P1 investigation |
| C10 | System shell appears near clip end and the app's Pause panel is visible. | Treat as a focus/pause transition to verify, not evidence of a crash. Test predictable return and researcher ownership. P2 |
| C11 | Fine distant edges and capture text show rasterization/compression limitations. | Existing footage cannot isolate through-lens blur/timewarp or quantify shimmer improvement. Verify exact candidate and runtime eye/pass settings plus a matched wearer test. OPEN |

Frame-index evidence uses ffmpeg sampling at 5-second intervals for the long clip and 3-second intervals for the short clip; interval labels above are approximate. These image files are private local evidence, not missing public repository assets.

## Interpretation of advisor direction

The central change is **control panel placement while preserving voluntary information choice and task strategy**. It is not a request to freeze task objects, force a repair order, or discard the four-format research comparison. The source taxonomy remains Manual(PDF), Steps, Illustrated, Video until explicitly revised; the meeting summary's separate Manual/PDF wording is recorded as a discrepancy.

## Completion boundaries

This review changed source-publication protection and design/handoff requirements. It did NOT implement or validate the runtime fixed-position UI, shorten the runtime Training, rearrange the physical scene, install a new APK, or certify comfort. Cloud/local implementation must close the above items with new evidence. Do not relabel this document as a PASS report.
