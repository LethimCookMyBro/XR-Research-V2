# Shared helpers for the delivery scripts. Dot-source only.
if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
  throw "adb not found. Install Android SDK Platform-Tools and add its folder to PATH (see README_TH.md, step 1)."
}

function Resolve-QuestSerial([string]$Serial) {
  $devices = @(& adb devices | Select-String -Pattern '^(\S+)\s+device$' | ForEach-Object { $_.Matches[0].Groups[1].Value })
  if ($Serial) {
    if ($devices -notcontains $Serial) { throw "Quest $Serial is not connected/authorized (adb devices shows: $($devices -join ', '))" }
    return $Serial
  }
  if ($devices.Count -ne 1) { throw "Connect exactly one authorized Quest or pass -Serial (found: $($devices -join ', '))" }
  return $devices[0]
}

function Get-QuestApp([string]$Serial) {
  $dump = & adb -s $Serial shell dumpsys package th.ac.tmu.vr.researchflow.quest3.technical
  $code = ($dump | Select-String 'versionCode=(\d+)' | Select-Object -First 1).Matches.Groups[1].Value
  $name = ($dump | Select-String 'versionName=(\S+)' | Select-Object -First 1).Matches.Groups[1].Value
  $updated = ($dump | Select-String 'lastUpdateTime=(.+)$' | Select-Object -First 1).Matches.Groups[1].Value
  [ordered]@{ installed = [bool]$code; versionCode = $(if ($code) { [int]$code } else { $null }); versionName = $name; lastUpdateTime = $updated }
}
