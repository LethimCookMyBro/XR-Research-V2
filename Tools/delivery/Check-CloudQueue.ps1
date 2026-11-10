<#
Lists, for one connected Quest, every finished session export and its cloud state:
  provider_verified  Google Drive has the files and their SHA-256 matched (local receipt on the headset)
  staged_verified    the relay has a verified copy; Drive not confirmed yet (reason shown)
  uploading / queued waiting for network or retrying
  not_sent           no attempt yet (no provisioning, or study data without approval)
Read-only on the headset. Local data is never removed by upload.

  .\Check-CloudQueue.ps1 [-Serial <serial>]
#>
param([string]$Serial)
$ErrorActionPreference = "Stop"
$Package = "th.ac.tmu.vr.researchflow.quest3.technical"
$Root = "/sdcard/Android/data/$Package/files/ResearchSessionRegistry"
. (Join-Path $PSScriptRoot "Quest-Common.ps1")
$Serial = Resolve-QuestSerial $Serial

function Read-Json([string]$path) { $t = & adb -s $Serial shell cat $path 2>$null; if ($LASTEXITCODE -eq 0 -and $t) { ($t -join "`n") | ConvertFrom-Json } }
$jobs = @(& adb -s $Serial shell "ls $Root/ExportQueue 2>/dev/null" | Where-Object { $_ -like "*.json" })
$rows = foreach ($job in $jobs) {
  $q = Read-Json "$Root/ExportQueue/$job"
  if (-not $q) { continue }
  $id = $q.bundle_id
  $receipt = Read-Json "$Root/CloudReceipts/$id.json"
  $status = Read-Json "$Root/CloudStatus/$id.json"
  [pscustomobject]@{
    participant = $q.participant_code; session = $q.session_id; bundle = $id
    state = $(if ($receipt) { "provider_verified" } elseif ($status) { $status.state } else { "not_sent" })
    reason = $status.reason; updated_utc = $status.updated_utc
  }
}
$config = Read-Json "/sdcard/Android/data/$Package/files/s1_cloud_relay.json"
"Quest $Serial  device_id=$($config.device_id)  label=$($config.device_label)  upload_enabled=$($config.enabled -eq $true)"
$rows | Sort-Object state, participant | Format-Table -AutoSize
$pending = @($rows | Where-Object { $_.state -ne "provider_verified" })
"Not yet on Google Drive: $($pending.Count) of $($rows.Count)"
