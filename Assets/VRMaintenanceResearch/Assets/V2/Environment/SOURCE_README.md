# Environment V2 - verified Blender export snapshot

Source: `blender/VR_Research_Factory_ENVIRONMENT_V2_WORKING.blend`
SHA256: `00feafda3f101ed75df3c4c748acf763606f8a48db8776634112c46dff47b42a`
Detailed source/output hashes: `source_manifest.json`.

Seven separate packages: Training, Computer, Fan, Corridor, Factory Static,
Factory Activity, Vehicles. Doors/drawers keep separate nodes and authored pivots;
Static in a filename does not mean moving parts were joined. Activity and Vehicles
remain independently selectable packages. Activity has one 48-second clip, eight
continuous pallets, 24fps sampling including the closing sample.

Export-only adaptations: procedural Oak.001 on five tables is baked to per-object
1024px base-color and tangent normal maps (embedded PNG, 8/16 bit respectively).
Original Steel/Rubber slots remain. Nine Glass objects use core glTF BLEND alpha
0.035 with metallic reflection: a visually checked approximation, not an exact
Glossy BSDF conversion. Authored V2 materials/UVs are unchanged.

Blender 5.2.1 LTS: seven clean-process structural reimports PASS. Expanded indexed
positions/normals, material groups and within-package mesh sharing match the prior
stage. 206,663 instance-counted triangles include converted text, not GPU draw work.
New UV seams add export vertices; see evidence/package_checks.json for per-package
deltas. No new instancing extension was enabled. Shared meshes do not promise
automatic Unity batching. Do not combine all packages into one renderer.

Visual evidence: outputs/environment_mesh_cleanup_20260914/continuation_gtr/
material_before and material_reimport (same cameras/lights, equivalent frame 373).
GT-R Solid viewport shadow streaks were isolated by A/B/A; only its viewport shadow
flag was disabled. Render shadows and vehicle geometry are unchanged.

Unity: NOT TESTED in this cleanup. Quest hardware/performance: NOT TESTED.
No FPS/draw-call or Quest-ready claim. Existing ASSET_CREDITS.md and textures/
are retained without modification. GLBs contain their required textures.

Rollback: use the continuation ROLLBACK.sh --test first; --restore requires Blender
closed and exact matching hashes. It restores the pre-continuation V2 (retaining
the earlier bench mesh sharing), prior export files and packages.py.
