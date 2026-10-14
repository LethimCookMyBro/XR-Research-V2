# Noto Sans Japanese source font

- Upstream: https://github.com/notofonts/noto-cjk
- Release: `Sans2.004` (static Japanese subset; unmodified)
- Font: https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/SubsetOTF/JP/NotoSansJP-Regular.otf
- License: SIL Open Font License 1.1, included as `NotoSansJP-OFL.txt`.
- License source: https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/LICENSE
- SHA256: `DFF723BA59D57D136764A04B9B2D03205544F7CD785A711442D6D2D085AC5073`

The existing `TrainingV2/UI/Fonts/NotoSansThai.ttf` remains the Thai source;
`Assets/TextMesh Pro/Fonts/LiberationSans.ttf` remains an available Latin source.
No existing font asset or global TMP configuration was changed for this import.
Use dedicated ResearchFlow TMP assets and populate them from `ResearchStrings.All(language)`.

Source glyph coverage is necessary but does not establish correct rendered combining marks,
line wrapping, padding, clipping, or Quest legibility. Check actual Thai and Japanese Game View
pixels and the final target headset. Translation equivalence requires bilingual researcher review.
