<#
Provisions ONE Quest for automatic upload to the cloud relay (no PC needed afterwards).
 - device_id = the headset serial (stable, unique; the same name the PC route uses for its Drive folder);
 - a fresh random device token is written ONLY to this headset's app folder; it is never printed or saved on the PC;
 - prints the token SHA-256 + label to add to the relay's S1_RELAY_DEVICE_TOKENS_JSON (server secret).
Running it again rotates the token (update the relay entry; the old token stops working there).
Study (TH/JP) uploads stay off unless -AllowStudy is given AND the relay holds the real approval.

  .\Provision-QuestCloud.ps1 -Label "Quest 1" -Endpoint https://<relay-host> [-Serial <serial>] [-AllowStudy]
  .\Provision-QuestCloud.ps1 -Disable [-Serial <serial>]      # stop uploads from this headset (local data untouched)
#>
param([string]$Label, [string]$Endpoint, [string]$Serial, [switch]$AllowStudy, [switch]$Disable)
$ErrorActionPreference = "Stop"
$Package = "th.ac.tmu.vr.researchflow.quest3.technical"
$Remote = "/sdcard/Android/data/$Package/files/s1_cloud_relay.json"
. (Join-Path $PSScriptRoot "Quest-Common.ps1")
$Serial = Resolve-QuestSerial $Serial
$deviceId = ((& adb -s $Serial shell getprop ro.serialno) -replace '[^A-Za-z0-9_-]', '').Trim()
if (-not $deviceId) { throw "Could not read the headset serial" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ("s1cfg_" + [guid]::NewGuid().ToString("N") + ".json")
try {
  if ($Disable) {
    @{ schema = "s1.quest-relay-config.1"; enabled = $false } | ConvertTo-Json | Set-Content -Encoding utf8 $temp
  } else {
    if (-not $Label -or -not $Endpoint) { throw "-Label and -Endpoint are required" }
    $uri = [Uri]$Endpoint
    if ($uri.Scheme -ne "https" -or $uri.Query -or $uri.UserInfo) { throw "Endpoint must be a bare https:// address" }
    $bytes = New-Object byte[] 30; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $token = ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x')
    $sha = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($token))).Replace("-", "").ToLower()
    [ordered]@{ schema = "s1.quest-relay-config.1"; enabled = $true; technical_only = (-not $AllowStudy); allow_study = [bool]$AllowStudy
                endpoint = $uri.AbsoluteUri.TrimEnd('/'); device_id = $deviceId; device_label = $Label; device_token = $token } |
      ConvertTo-Json | Set-Content -Encoding utf8 $temp
    $token = $null
  }
  & adb -s $Serial push $temp $Remote | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "adb push failed; the headset was not changed" }
  $local = (Get-FileHash -Algorithm SHA256 $temp).Hash.ToLower()
  $device = ((& adb -s $Serial shell sha256sum $Remote) -split '\s+')[0]
  if ($local -ne $device) { throw "Config readback differs on the headset" }
} finally { if (Test-Path $temp) { Remove-Item -Force $temp } }

if ($Disable) { "Uploads disabled on $deviceId. Also remove its entry from the relay device list." ; exit 0 }
[ordered]@{
  device_id = $deviceId; label = $Label; allow_study = [bool]$AllowStudy
  relay_entry = @{ $deviceId = @{ token_sha256 = $sha; label = $Label } }
  next = "Add relay_entry to S1_RELAY_DEVICE_TOKENS_JSON on the relay, then close and reopen the app on the headset."
} | ConvertTo-Json -Depth 4
