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

## Recipient setup (before opening Unity)

The raw Japanese font is **not distributed** in the source/delivery archive. Its
tracked `.meta` file is required: `ResearchFlow_Japanese.asset` references GUID
`2918b8a07de0fe947bba0534b5a7c8a9`. Downloading to another path or allowing Unity to
create a new GUID breaks that dynamic TMP source-font reference.

From the received project root, with Unity closed, run in PowerShell:

```powershell
$font = Join-Path $PWD 'Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/NotoSansJP-Regular.otf'
$expected = 'DFF723BA59D57D136764A04B9B2D03205544F7CD785A711442D6D2D085AC5073'
if (-not (Test-Path -LiteralPath $font)) {
    Invoke-WebRequest 'https://raw.githubusercontent.com/notofonts/noto-cjk/Sans2.004/Sans/SubsetOTF/JP/NotoSansJP-Regular.otf' -OutFile $font
}
if ((Get-FileHash -LiteralPath $font -Algorithm SHA256).Hash -ne $expected) { throw 'Japanese font SHA256 mismatch; preserve the file and stop.' }
if (-not (Select-String -LiteralPath ($font + '.meta') -Pattern '^guid: 2918b8a07de0fe947bba0534b5a7c8a9$' -Quiet)) { throw 'Japanese font metadata GUID mismatch; preserve the metadata and stop.' }
```

Then open the project in its pinned Unity version. Keep the downloaded font local;
do not stage it or include it in a delivery archive. A successful download/hash/GUID
check proves dependency identity, **not** rendered-language or Quest acceptance.
