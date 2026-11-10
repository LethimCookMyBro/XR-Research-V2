# S1 final engineering delivery — 2026-10-27 (deadline 2026-10-28)

Playbook: Autonomous run (steps verbatim)
- [x] 1. State the exit condition as a checkable predicate before the first iteration (tests green, repro fixed, all N PRs merged, pixel-diff zero).
- [ ] 2. Pick the wake mechanism using Claude Code's `loop` skill — skip: single foreground session, no external event to watch.
- [x] 3. Each iteration makes the smallest change the evidence justifies, verifies it against the predicate, commits if it advanced, discards changes that didn't help.
- [x] 4. Mid-run discoveries are yours.
- [x] 5. Checkpoint every iteration via show-me-your-work (.audit/s1-final-delivery-20261027.tsv).
- [x] 6. Stop when the predicate is met; never relax the predicate.

Exit predicate: a new delivery folder D:\VR_TMU\S1_Delivery_20261028_V2_RC5 (RC3/RC4 untouched) containing
tested APK + full LFS-real source snapshot + guides + SHA256 manifest + verified ZIP, where every P0 item
(Fan occlusion, Training transient, movement QC, privacy parse) is either FIXED with runtime evidence or
recorded OPEN with evidence, and one Editor-SDK full workflow + export + host QC ran on the shipped source.

Task todos
- [x] Secure: RC4 APK hash re-verified untouched; source snapshot made at 7b6c252 (LFS real), da0fdb3 = 7b6c252 minus evidence/source-transaction/RC5.patch
- [x] P0-A Fan Main occlusion: reproduce, fix, verify P/Q 1.0/1.65 TH/JA
- [x] P0-B Training transient clipping: reproduce, fix, verify
- [x] P0-C Movement gap: quiet run, attribute cause (bounded, OPEN)
- [x] P0-D Privacy: version-correct serialized settings check on APK
- [x] One full Editor SDK workflow → export → host QC
- [x] Commit reviewed fixes; build APK once; verify
- [x] Package RC5 + guides + ZIP verify
