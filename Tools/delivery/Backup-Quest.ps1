<#
Copies every research log/registry file and the installed APK from the Quest to this PC, then proves the copy:
same file list, same sizes and same SHA-256 as on the headset. Read-only on the headset. Run it before any install.

  .\Backup-Quest.ps1 [-Serial <adb serial>] [-Out <folder>]
#>
param([string]$Serial, [string]$Out = (Join-Path $PSScriptRoot "..\..\quest_backups"))
$ErrorActionPreference = "Stop"
$Package = "th.ac.tmu.vr.researchflow.quest3.technical"
$Remote = "/sdcard/Android/data/$Package/files/ResearchSessionRegistry"
. (Join-Path $PSScriptRoot "Quest-Common.ps1")

$Serial = Resolve-QuestSerial $Serial
$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$dest = Join-Path ([IO.Path]::GetFullPath($Out)) "$Serial`_$stamp"
New-Item -ItemType Directory -Force $dest | Out-Null

$info = Get-QuestApp $Serial
$info | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $dest "installed_app.json")
Write-Host "Installed: $($info.versionName) (code $($info.versionCode))"

# Device-side list with SHA-256, taken before the copy.
$deviceList = & adb -s $Serial shell "cd '$Remote' 2>/dev/null && find . -type f -exec sha256sum {} +"
if ($LASTEXITCODE -ne 0) { throw "Cannot list $Remote on the headset (is the app installed and has it run once?)" }
$device = @{}
foreach ($line in $deviceList) { if ($line -match '^([0-9a-f]{64})\s+\./(.+)$') { $device[$Matches[2]] = $Matches[1] } }
$device.GetEnumerator() | Sort-Object Name | ForEach-Object { "$($_.Value)  $($_.Name)" } | Set-Content -Encoding utf8 (Join-Path $dest "device_sha256.txt")

& adb -s $Serial pull $Remote (Join-Path $dest "ResearchSessionRegistry") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "adb pull failed; nothing was changed on the headset" }

$localRoot = Join-Path $dest "ResearchSessionRegistry"
$bad = @()
foreach ($rel in $device.Keys) {
  $p = Join-Path $localRoot ($rel -replace '/', '\')
  if (-not (Test-Path $p)) { $bad += "missing $rel"; continue }
  $h = (Get-FileHash -Algorithm SHA256 $p).Hash.ToLower()
  if ($h -ne $device[$rel]) { $bad += "hash differs $rel" }
}
$apkPath = (& adb -s $Serial shell pm path $Package) -replace '^package:', ''
& adb -s $Serial pull $apkPath.Trim() (Join-Path $dest "installed.apk") | Out-Null
$apkDevice = ((& adb -s $Serial shell sha256sum $apkPath.Trim()) -split '\s+')[0]
$apkLocal = (Get-FileHash -Algorithm SHA256 (Join-Path $dest "installed.apk")).Hash.ToLower()
if ($apkDevice -ne $apkLocal) { $bad += "installed.apk hash differs" }

$result = [ordered]@{ serial = $Serial; folder = $dest; files = $device.Count; installed = $info; apk_sha256 = $apkLocal; problems = $bad;
                      status = $(if ($bad.Count -eq 0) { "BACKUP_VERIFIED" } else { "BACKUP_INCOMPLETE" }) }
$result | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $dest "BACKUP_RESULT.json")
$result | ConvertTo-Json -Depth 4
if ($bad.Count -ne 0) { exit 2 }
