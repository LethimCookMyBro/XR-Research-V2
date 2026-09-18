param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$build=Join-Path $EvidenceDirectory 'logger-build'
& dotnet build (Join-Path $PSScriptRoot 'HandsLabLoggerChecks.csproj') "-p:BaseIntermediateOutputPath=$build\obj\" "-p:OutputPath=$build\bin\" --nologo -v:minimal 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'logger-build.txt')
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet (Join-Path $build 'bin/HandsLabLoggerChecks.dll') (Join-Path $EvidenceDirectory 'logger-test-data') 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'logger-checks.txt')
exit $LASTEXITCODE
