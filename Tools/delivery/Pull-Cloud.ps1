<#
Downloads every session the Quests uploaded to the Railway bucket into a PC folder (one folder per headset: Quest_01, Quest_02, ...)
and checks each bundle's SHA-256 against its device manifest. Bucket keys come from the relay service via `railway run`
and are never saved on this PC. Needs: Railway CLI logged in (railway login), Python 3 with boto3 (pip install boto3).

  .\Pull-Cloud.ps1 -Dest D:\S1_Cloud_Pull
#>
param([Parameter(Mandatory = $true)][string]$Dest,
      [string]$Project = "056eeace-e411-4559-bc44-1fd6a287ab4e", [string]$Service = "s1-quest-relay", [string]$Environment = "production")
$ErrorActionPreference = "Stop"
if (-not (Get-Command railway -ErrorAction SilentlyContinue)) { throw "Railway CLI not found (npm i -g @railway/cli, then railway login)" }
& python -c "import boto3" 2>$null
if ($LASTEXITCODE -ne 0) { throw "Python package boto3 missing: pip install boto3" }
$script = Join-Path $PSScriptRoot "..\s1_cloud_relay\pull_bucket.py"
if (-not (Test-Path $script)) { $script = Join-Path $PSScriptRoot "..\tools\s1_cloud_relay\pull_bucket.py" }
& railway run -p $Project -s $Service -e $Environment python $script --dest $Dest
exit $LASTEXITCODE
