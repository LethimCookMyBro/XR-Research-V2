param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'TrainingCoreChecks.csproj'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$build = Join-Path $EvidenceDirectory 'core-build'
& dotnet build $project "-p:BaseIntermediateOutputPath=$build\obj\" "-p:OutputPath=$build\bin\" --nologo -v:minimal 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'core-build.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$dll = Join-Path $build 'bin/TrainingCoreChecks.dll'
& dotnet $dll BASELINE (Join-Path $root 'Assets/XRI_Examples/Global/Scripts/Research') 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'core-baseline.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet $dll MODIFIED (Join-Path $EvidenceDirectory 'core-test-logs') 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'core-modified.txt')
exit $LASTEXITCODE
