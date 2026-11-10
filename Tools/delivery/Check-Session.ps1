<#
After a session: re-verifies every bundle under the data folder (hashes against its manifest) and prints, per session,
whether the data is complete and what is missing (questionnaires, FPV video, tracking gaps) with the recorded reason.
Missing values stay missing; nothing is filled in.

  .\Check-Session.ps1 -DataRoot D:\ResearchData [-Participant TH001]
#>
param([Parameter(Mandatory)][string]$DataRoot, [string]$Participant)
$ErrorActionPreference = "Stop"
$tools = Join-Path $PSScriptRoot "..\tools"
if (-not (Test-Path (Join-Path $tools "research_export.py"))) { $tools = Join-Path $PSScriptRoot ".." }
$env:PYTHONIOENCODING = "utf-8"
$bundles = Get-ChildItem -Path $DataRoot -Recurse -Filter COMPLETE.json | Where-Object { $_.Directory.Name -like 'v1_*' } | ForEach-Object { $_.Directory }
if ($Participant) { $bundles = $bundles | Where-Object { $_.FullName -match "\\$Participant\\" } }
$rows = foreach ($b in $bundles) {
  $verify = & python (Join-Path $tools "research_export.py") verify --bundle $b.FullName 2>&1 | Out-String
  $qcPath = Join-Path $b.FullName "qc\report.json"
  $qc = if (Test-Path $qcPath) { Get-Content $qcPath -Raw -Encoding utf8 | ConvertFrom-Json } else { $null }
  [pscustomobject]@{
    bundle = $b.Name; participant = $b.Parent.Parent.Name
    hashes = $(if ($LASTEXITCODE -eq 0) { "verified" } else { "FAILED" })
    data_complete = $qc.data_complete; lifecycle = $qc.lifecycle; task_order = ($qc.actual_task_order -join ",")
    failures = ($qc.failures -join "; "); missing_or_warnings = ($qc.warnings -join "; ")
  }
}
$rows | Format-List
if ($rows | Where-Object { $_.hashes -ne "verified" }) { exit 2 }
