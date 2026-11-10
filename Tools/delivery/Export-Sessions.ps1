<#
Copies finished session bundles from the Quest to the PC data folder, verifies every file hash, writes the PC receipt,
and only then acknowledges the bundle on the Quest. Safe to run again: verified bundles are not copied twice, and a
dropped cable leaves the job queued for the next run. Raw files are never edited.

  .\Export-Sessions.ps1 -DataRoot D:\ResearchData [-Serial <serial>]
#>
param([Parameter(Mandatory)][string]$DataRoot, [string]$Serial)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "Quest-Common.ps1")
$Serial = Resolve-QuestSerial $Serial
$tools = Join-Path $PSScriptRoot "..\tools"
if (-not (Test-Path (Join-Path $tools "research_export.py"))) { $tools = Join-Path $PSScriptRoot ".." }  # repository layout
New-Item -ItemType Directory -Force $DataRoot | Out-Null
$adb = (Get-Command adb).Source
$env:PYTHONIOENCODING = "utf-8"
& python (Join-Path $tools "research_export.py") pull-queue --adb $adb --serial $Serial --destination $DataRoot
exit $LASTEXITCODE
