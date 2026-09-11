# Errata — Environment V2 Glass/Oak verification

Applies to commit ebae1f7 (Fix Environment V2 glass and oak materials).

## Console snapshot attribution

`VERIFICATION.txt` (Console line) and `environment_v2_glass_oak_report.json`
(`retained_known_warnings`) attribute the render-texture cleanup warning
("Releasing render texture that is set to be RenderTexture.active!") to the
final visual verification console. That warning belongs to
`rollback_console.json`.

Correct counts:

- `final_console.json`: 0 errors, 0 warnings, 1 log
- `after_playmode_console.json`: 0 errors, 6 warnings
- `rollback_console.json`: 0 errors, 1 warning

The raw JSON files remain authoritative and unchanged. `VERIFICATION.txt` and
`environment_v2_glass_oak_report.json` are left as originally written.
