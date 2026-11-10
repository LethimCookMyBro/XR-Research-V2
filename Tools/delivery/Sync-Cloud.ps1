<#
Second copy to the approved cloud folder. The PC copy stays the primary record; a failed or offline sync never blocks a session.
  status          what is uploaded, queued offline, waiting for approval
  sync            copy verified PC bundles to the synced folder and read them back (add -Force to retry now)
  provider-verify download each file back from Google Drive by file ID and compare SHA-256 (proves the upload finished)
Study (TH/JP) bundles stay "cloud_pending_approval" until the config carries the real ethics approval (see README_TH.md).

  .\Sync-Cloud.ps1 -DataRoot D:\ResearchData -Config C:\private\drive_research_s1.config.json -Action status|sync|provider-verify [-Force]
#>
param([Parameter(Mandatory)][string]$DataRoot, [Parameter(Mandatory)][string]$Config,
      [ValidateSet("status", "sync", "provider-verify")][string]$Action = "status", [switch]$Force)
$ErrorActionPreference = "Stop"
$tools = Join-Path $PSScriptRoot "..\tools"
if (-not (Test-Path (Join-Path $tools "research_cloud_sync.py"))) { $tools = Join-Path $PSScriptRoot ".." }
$env:PYTHONIOENCODING = "utf-8"
$pyArgs = @((Join-Path $tools "research_cloud_sync.py"), $Action, "--data-root", $DataRoot, "--config", $Config)
if ($Force -and $Action -eq "sync") { $pyArgs += "--force" }
& python @pyArgs
exit $LASTEXITCODE
