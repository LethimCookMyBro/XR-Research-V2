# Quest wear retest after the 16 Oct 2026 review (about 20 minutes)

Purpose: confirm on the headset the fixes for the 16 Oct technical wear capture. This is a technical DEV test, not a study session, and not human or language approval.

Build: check the installed APK hash before starting. `adb shell pm path th.ac.tmu.vr.researchflow.quest3.technical`, then `sha256sum` that path. Write the hash in the notes. Builds before 3618d2b do not contain the FX1 latch fix. Sections 3.2 and 3.4 to 3.6 and 5.3 need the 16 Oct evening build (see CURRENT.json for its hash).

Record: Quest video capture (Meta button > Camera > Record) plus `adb logcat -v time > wear.log` on the PC. Keep both on the local PC only.

## 1. Setup card (2 min)

1. Launch the app. The card shows "System ready" and "Starts as DEVnnn · table layout P/Q · test data".
2. Press the language button twice and the order button twice. Each press changes the label once.
3. Do not open "Researcher: advanced setup". Press "Start Full Test".
4. Note: was every button easy to hit with the ray? Did anything need two presses?

## 2. Stop and Continue (2 min)

1. On the Welcome card, press Stop (square icon). The card reads "Test stopped (technical test mode)" with "Continue".
2. Press Continue. You return to the same Welcome step.
3. Repeat once during Training while holding the stone. After Continue, training credits are kept.

## 3. Training feedback (8 min)

1. While the card asks for teleport, pick up the stone. Expect a short "Not counted yet: follow the instruction above first" line, with no tick.
2. Repeat a finished skill (for example release the stone again). Expect no message; the log still records the duplicate.
3. Turning (snap) after the snap step should show no message.
4. FX1 group: the mug and stone are gone from the table. Only the part the card names can be grabbed (module for the insert
   steps, latch for lock, handle for turning, screw for fasten/loosen, plug head for the cable).
5. "Wrong way blocked": grab the yellow module and lower it over the slot with its cut corner on the opposite side to the
   slot's cut corner. Expect it to stop part-way and the step to tick. Before that, a module held a few cm to one side shows
   "Move the module so it sits right over the slot"; tilted shows "Hold the module flat"; the right way round shows
   "This way goes in. Turn it around and try again". Note how long the step took and whether any hint was confusing.
6. "Insert module": turn it the right way, push it fully down, release. Releasing part-way shows "Push it all the way down".
7. Close and open the latch. The latch part must visibly move.

## 4. Panel steadiness (3 min)

1. During Training, look from the card down to the table and back about ten times at a normal pace.
2. The small Panel/Stop strip should not pop up on every glance. When it shows, it should stay where it was.
3. Afterwards pull `events.jsonl` for the run and count `PANEL_PLACEMENT` with target `recovery`. The 16 Oct run had 34 in about two minutes.

## 5. Reader (5 min)

1. Open Reference in Training. Visit all four formats.
2. Video: press Play, let it run 5 s, Pause, seek +10 s, -10 s, let it reach the end, press Play again.
3. Press the frame-and-plus button under the Reader. It moves nearer (1.0 to 1.3 m from the eyes). Note whether PDF text is
   now readable without leaning, and whether the panel covers the table or a hand. Press it again (frame-and-minus): the
   Reader returns exactly to its first place. Closing and reopening keeps whichever place was chosen.
4. Pull `events.jsonl` and read `READER_DISTANCE` rows: `head_distance_m` is the measured distance on Quest.

## 6. Comfort and clarity (2 min)

Ask the wearer, through the lenses: are the far edges or text blurry or shimmering? Is controller movement smooth? Record the answer as the wearer's words. Offscreen scores are not a substitute.

Stop here if the wearer feels unwell at any point. Do not clear app data, uninstall, or change the Quest account.
