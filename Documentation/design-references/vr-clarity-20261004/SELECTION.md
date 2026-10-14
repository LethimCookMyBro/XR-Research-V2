# Existing VR UI: visual reference selection

These ten AI-generated images are concepts, not Unity captures or wearer evidence. Images were generated with the built-in image tool. Exact prompts and file hashes are in `prompts.json` and `manifest.json`. The selected implementation preserves the existing Unity/uGUI architecture and pinned SDK versions.

| Reference | Adopt | Reject |
| --- | --- | --- |
| 01 Grab | One current action, large instructional illustration, short progress | Invented scenery, controller anatomy as authority, numbered sequence not matching the existing facts |
| 02 Regrab | Distinct regrab heading, one hold instruction, quiet retained progress | Hand/controller merged around a cup handle; new nested card backgrounds |
| 03 Select | Clear ray-to-button relation | The generated controller highlights the wrong button; use the existing Trigger diagram instead |
| 04 Rotate | Hand/object image with a local directional cue | Missing existing 30 degree instruction; extra three-item navigation |
| 05 Reference switch | Recognizable format icons alongside short names | Invented maintenance content, extra boxes; all existing availability and format identities remain |
| 06 Reference move | Actual handle is the target; hold, move, release relation | Generated Grip control is wrong: the existing panel handle uses Trigger or pinch. Neon glow and additional paragraphs |
| 07 Reference reading | Clear image/caption spacing and page controls | Invented exploded assembly as study content. Existing authored experimental reference is preserved |
| 08 Pause | One clear status and named actions | Invented resume permissions: protected experimental pause continues to require the existing researcher policy |
| 09 Active task | Keep the workpiece visible and controls grouped | Oversized task panel, large competing control cards and invented task instruction |
| 10 Minimized reference | Explicit restore verb beside a book symbol as a principle | Whole composition rejected after user critique: too many controls, competing panels and extra inset. Never render that inset in the app |

Two isolated hand/object cutouts were then generated for the existing Tutorial only. They contain no instructional text; the existing localized text and controller diagram carry the true input mapping. Gesture pictures describe virtual object manipulation, not the anatomy of a real controller. Texture import is capped at 512, sprites do not receive input, and Task A/B display no teaching picture or repair hint.

## Skills actually applied

- Impeccable `distill`: remove repeated headings/context, reduce choices competing with the current action, retain familiar labels where an icon alone is ambiguous.
- Make Interfaces Feel Better: consistent icon strokes, glyph padding, clear control states, measured separation, no decorative card nesting.
- Transitions / GSAP: animate only the instructional gesture locally; keep interaction targets stationary, keep existing state lifecycle and cleanup, add no GSAP/DOM/CSS dependency.
- pstack `principle-subtract-before-you-add`: remove redundant instruction and utilities before adding the useful illustration.

VR rules take precedence: spatial targets and spacing, stable placement, readable world-space UI, and no layout change on hover. Sources: [Meta Hands UI best practices](https://developers.meta.com/vr/design/hands-ui-best-practices/), [Unity XRI 3.4 UI setup](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@3.4/manual/ui-setup.html).

Research invariants: no change to task order/objectives, scoring, pass/fail, timer policy, raw event schema, reference availability, intentional difficulty or hidden information. Tutorial evidence remains sequential; early release keeps prior credit and requires actual reacquisition before the pending lift/rotation. Translation status remains pending human review.
