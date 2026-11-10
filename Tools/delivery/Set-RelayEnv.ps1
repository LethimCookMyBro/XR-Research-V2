<#
Copies the relay's server secrets into Railway without printing them (values go through stdin, never the command line).
  -Google       reads %USERPROFILE%\.s1_relay\drive_oauth.json written by Tools\s1_cloud_relay\setup_google_drive.py
  -DevicesJson  a JSON file mapping device_id -> {token_sha256,label} (merge the relay_entry blocks from Provision-QuestCloud.ps1)
Uses --skip-deploys: deploy the current relay code afterwards so the new settings and code start together.
Requires the Railway CLI logged in to the account that owns the relay project.

  .\Set-RelayEnv.ps1 -Google
  .\Set-RelayEnv.ps1 -DevicesJson C:\private\relay_devices.json
#>
param([switch]$Google, [string]$DevicesJson,
      [string]$Project = "056eeace-e411-4559-bc44-1fd6a287ab4e", [string]$Service = "s1-quest-relay", [string]$Environment = "production")
$ErrorActionPreference = "Stop"
if (-not (Get-Command railway -ErrorAction SilentlyContinue)) { throw "Railway CLI not found (npm i -g @railway/cli, then railway login)" }

function Set-Secret([string]$Key, [string]$Value) {
  $Value | & railway variable set $Key --stdin --skip-deploys -p $Project -s $Service -e $Environment --json | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "railway variable set $Key failed" }
  "set $Key"
}

if ($Google) {
  $store = Join-Path $env:USERPROFILE ".s1_relay\drive_oauth.json"
  $g = Get-Content $store -Raw | ConvertFrom-Json
  foreach ($k in "client_id", "client_secret", "refresh_token", "folder_id") { if (-not $g.$k) { throw "$store is missing $k; rerun setup_google_drive.py" } }
  Set-Secret "S1_GOOGLE_CLIENT_ID" $g.client_id
  Set-Secret "S1_GOOGLE_CLIENT_SECRET" $g.client_secret
  Set-Secret "S1_GOOGLE_REFRESH_TOKEN" $g.refresh_token
  Set-Secret "S1_GOOGLE_FOLDER_ID" $g.folder_id
  Set-Secret "S1_SITE_CODE" "TMU"
  Set-Secret "S1_GOOGLE_ENABLED" "true"
}
if ($DevicesJson) {
  $map = Get-Content $DevicesJson -Raw | ConvertFrom-Json
  foreach ($p in $map.PSObject.Properties) {
    if ($p.Name -notmatch '^[A-Za-z0-9_-]{1,160}$' -or $p.Value.token_sha256 -notmatch '^[a-f0-9]{64}$') { throw "Bad device entry $($p.Name)" }
  }
  Set-Secret "S1_RELAY_DEVICE_TOKENS_JSON" ($map | ConvertTo-Json -Compress -Depth 4)
}
"Done. Deploy the relay code (Tools\s1_cloud_relay) so the settings take effect, then check /health shows upload_enabled true."
