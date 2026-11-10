<#
Installs or updates the research app on a Quest without losing data.
 - checks the APK against BUILD_IDENTITY.json (SHA-256, version);
 - refuses while the app is running (finish or abort the session in the app first);
 - runs Backup-Quest.ps1 and continues only on BACKUP_VERIFIED;
 - never uninstalls and never clears app data; refuses a lower version unless -AllowDowngrade;
 - verifies the installed version code and the installed APK SHA-256 afterwards.

  .\Install-Quest.ps1 -Apk ..\apk\ResearchFlowQuest3_<stamp>.apk [-Serial <serial>] [-AllowDowngrade]
#>
param([Parameter(Mandatory)][string]$Apk, [string]$Serial, [switch]$AllowDowngrade)
$ErrorActionPreference = "Stop"
$Package = "th.ac.tmu.vr.researchflow.quest3.technical"
. (Join-Path $PSScriptRoot "Quest-Common.ps1")

$Apk = (Resolve-Path $Apk).Path
$identityPath = Join-Path (Split-Path $Apk) "BUILD_IDENTITY.json"
if (-not (Test-Path $identityPath)) { throw "BUILD_IDENTITY.json must sit next to the APK" }
$identity = Get-Content $identityPath -Raw | ConvertFrom-Json
$sha = (Get-FileHash -Algorithm SHA256 $Apk).Hash.ToLower()
if ($sha -ne $identity.apk_sha256) { throw "APK SHA-256 $sha does not match BUILD_IDENTITY.json $($identity.apk_sha256)" }

$Serial = Resolve-QuestSerial $Serial
$before = Get-QuestApp $Serial
if ($before.installed) {
  if (& adb -s $Serial shell pidof $Package) { throw "The app is running. Finish or abort the session in the app, close it, then retry." }
  if ($before.versionCode -gt [int]$identity.version_code -and -not $AllowDowngrade) {
    throw "Installed version code $($before.versionCode) is newer than $($identity.version_code). See README_TH.md 'Rollback' before using -AllowDowngrade."
  }
  $backup = & (Join-Path $PSScriptRoot "Backup-Quest.ps1") -Serial $Serial | Out-String | ConvertFrom-Json
  if ($backup.status -ne "BACKUP_VERIFIED") { throw "Backup not verified; nothing installed. $($backup.problems -join '; ')" }
  Write-Host "Backup verified: $($backup.folder)"
}

& adb -s $Serial push $Apk /data/local/tmp/research_update.apk | Out-Null
if ($LASTEXITCODE -ne 0) { throw "adb push failed; nothing installed" }
$flags = if ($AllowDowngrade) { "-r -d" } else { "-r" }
$out = & adb -s $Serial shell "pm install $flags /data/local/tmp/research_update.apk"
if ("$out" -notmatch "Success") { throw "pm install failed: $out (the previous version is still installed)" }

$after = Get-QuestApp $Serial
$apkPath = ((& adb -s $Serial shell pm path $Package) -replace '^package:', '').Trim()
$installedSha = ((& adb -s $Serial shell sha256sum $apkPath) -split '\s+')[0]
$ok = ($after.versionCode -eq [int]$identity.version_code) -and ($installedSha -eq $identity.apk_sha256)
[ordered]@{ serial = $Serial; before = $before; after = $after; installed_apk_sha256 = $installedSha; expected = $identity.apk_sha256;
            status = $(if ($ok) { "INSTALL_VERIFIED" } else { "INSTALL_MISMATCH" }) } | ConvertTo-Json -Depth 4
if (-not $ok) { exit 2 }
