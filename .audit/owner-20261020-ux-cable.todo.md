# Owner goal 20 Oct 2026: cable visuals, single Pause, concise Task card

Feature playbook (verbatim steps):
- [x] 1. `how` over affected subsystem. (done inline: pause card, StopOffered, TaskBrief, Stop command, cable path read this session)
- [x] 2. `architect` parallel design exploration. skip: owner specified the design; open shape decisions delegated with named data shape
- [x] 3. Throughput checkpoint
  - Blocking first steps: stop my queued Play runs (done); Editor lock helper (scratchpad/editor_lock.py, self-tested)
  - Independent workstreams: (W1) cable visuals ResearchS1Cable/ResearchS1CablePath/ResearchTrainingStation(Builder)/cable assets; (W2) single Pause + Stop-and-wait pause card + concise Task card in ResearchFlowView/ResearchSessionFlowController/ResearchStrings/ResearchUIAction/design_rule
  - Shared mutable state: one Unity Editor (compile, domain reload, Play) and the Assets compile set -> serialized by EDITOR_LOCK.json; main scene edited only by W1
  - Smallest safe decomposition: 2 owners; W2 keeps Pause and Task card together because both rewrite the task panel in ResearchFlowView.cs and ResearchStrings.cs
- [x] 4. Delegate W1 and W2 (launched 20 Oct ~20:20, opus; W2 xhigh, W1 high) (worktrees skipped: a Unity worktree needs a second Editor and a full Library import; the lock is the fence)
- [x] 5. Verify on matching surface (Editor Game/Scene captures, suites, full S1 runs, trap; Quest after build)
- [x] 6. Small ordered commits per unit
- [ ] 7. interrogate if design contested. skip unless W2 pause semantics are disputed
- [ ] 8. Opening PR. skip: owner works on main only (CLAUDE.md 14 Oct); push needs owner decision (public repo)

Verification (owner list):
- [x] V1 before/after captures from Unity Game/Scene View, view type named
- [ ] V2 (W4 eye heights running; 7/8 matrix cells done, JA AB P pending idle rerun) TH/JA x Task A/B x Layout P/Q x project eye heights
- [x] V3 Pause -> Continue/Stop for DEV and Study, holding panel, tracking loss, recovery
- [x] V4 cables at every key pose with images and sink numbers
- [ ] V5 full workflow, Reader 4 formats, Inspect, logger, movement, export: no regression
- [ ] V6 323 ms stall from new evidence (trap run on the new source)
- [x] V7 human-readable before/after screenshots without dense/overlapping/overflowing text or ambiguous buttons
- [ ] V8 build + guarded install with backup and hash check when source passes and Quest is safe
