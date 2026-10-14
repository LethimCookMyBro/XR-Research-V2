param([Parameter(Mandatory=$true)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
$build = Join-Path $EvidenceDirectory 'resume-build'
& dotnet build (Join-Path $PSScriptRoot 'ResearchLoggerResumeChecks.csproj') "-p:BaseIntermediateOutputPath=$build\obj\" "-p:OutputPath=$build\bin\" --nologo -v:minimal 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'resume-build.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$data = Join-Path $EvidenceDirectory ('resume-data-' + [guid]::NewGuid().ToString('N'))
& dotnet (Join-Path $build 'bin/ResearchLoggerResumeChecks.dll') $data 2>&1 | Tee-Object (Join-Path $EvidenceDirectory 'resume-checks.txt')
exit $LASTEXITCODE
