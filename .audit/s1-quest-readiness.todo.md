# S1 Quest-readiness run (2026-10-17) — todo

## Autonomous run playbook (verbatim)
- [ ] 1. State the exit condition as a checkable predicate before the first iteration (tests green, repro fixed, all N PRs merged, pixel-diff zero).
- [ ] 2. Pick the wake mechanism using Claude Code's `loop` skill (a built-in, not a pstack skill). An event to watch (CI, a merge, a ref advancing) gets a watcher subagent that wakes you on the event, with a long time-based heartbeat as fallback. No event gets a fixed-interval heartbeat sized to when the result is worth re-checking.
  skip: single interactive session, no external event; Unity calls are synchronous.
- [ ] 3. Each iteration makes the smallest change the evidence justifies, verifies it against the predicate, commits if it advanced, discards changes that didn't help. Belt-and-suspenders that "might help" gets reverted, not left to ride.
- [ ] 4. Mid-run discoveries are yours. Address broken skills, related bugs, flaky verifiers, review noise, tooling failures, orphaned follow-ups, and fixable drift yourself via poteto-mode. Put out-of-band fixes in their own PR. Do not park reversible work for the human or use `AskUserQuestion`. Surface only irreversible actions, genuine product or preference calls no experiment can settle, or a real dead end. Keep the predicate as the main drive, and return to it after each side fix.
- [ ] 5. Checkpoint every iteration via the **show-me-your-work** skill, a row for what changed and whether the predicate moved.
- [ ] 6. Stop when the predicate is met. A plateau is not a stop, so keep going and pivot your approach to push past it. Surface a genuine dead end rather than spinning, and never relax the predicate to declare victory.

## Exit predicate (all must hold, evidence path per item)
- P1 uncommitted 17-Oct work reviewed; build side effects (4 font atlases, ProjectSettings) restored; 0 compile errors.
- P2 startup card: ID-to-use + mode visible, one aggregate status line, readiness rows only on demand/not ready with a reason next to Start; Editor screenshots TH+JA.
- P3 drag readability checks both eyes + panel extents; Training_Practice_A exemption justified or removed; spatial checks pass.
- P4 Editor suites (spatial, uictrl, safety, uirec, Reader production) pass; >=2 full-flow Editor runs pass with QC.
- P5 Reader audit A/B x TH/JA x 4 formats; video length vs 420 s reported; approval status unchanged.
- P6 clarity audit (mipmaps/filter/aniso, runtime textures, TMP, frame timing) with evidence-based fix or explicit open item.
- P7 APK from committed source, SHA256, side effects restored; Quest install/wearer = PENDING (device absent).

## Task todos
- [ ] ui_concept_01/02 search (background)
- [ ] Review diff hunks: Examine grab-credit, auto-focus, Training_Practice_A, ReadableFromHeadForDrag
