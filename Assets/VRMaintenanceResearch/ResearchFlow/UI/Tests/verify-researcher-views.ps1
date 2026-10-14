# Compile the views against installed Unity/TMP assemblies without refreshing or mutating the Editor.
# The controller contract is stubbed; parent integration must also compile and exercise the real controller.
$ErrorActionPreference = 'Stop'
$flowPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$projectRoot = (Resolve-Path (Join-Path $flowPath '..\..\..')).Path
$version = ((Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings\ProjectVersion.txt') | Select-String '^m_EditorVersion:').Line -split ': ')[1]
$managed = Join-Path $env:ProgramFiles ('Unity\Hub\Editor\' + $version + '\Editor\Data\Managed')
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) ('ResearcherViews-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryPath | Out-Null
$references = @(Get-ChildItem -LiteralPath (Join-Path $managed 'UnityEngine') -Filter '*.dll' | Where-Object Name -ne 'UnityEngine.dll')
$references += @(Get-Item -LiteralPath (Join-Path $projectRoot 'Library\ScriptAssemblies\Unity.TextMeshPro.dll'), (Join-Path $projectRoot 'Library\ScriptAssemblies\UnityEngine.UI.dll'), (Join-Path $projectRoot 'Library\ScriptAssemblies\Unity.InputSystem.dll'))
$newtonsoftPackage = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Library\PackageCache') -Directory | Where-Object Name -like 'com.unity.nuget.newtonsoft-json@*' | Select-Object -First 1
$references += Get-Item -LiteralPath (Join-Path $newtonsoftPackage.FullName 'Runtime\Newtonsoft.Json.dll')
$xmlReferences = $references | ForEach-Object { '<Reference Include="' + [Security.SecurityElement]::Escape($_.BaseName) + '"><HintPath>' + [Security.SecurityElement]::Escape($_.FullName) + '</HintPath><Private>false</Private></Reference>' }
$sources = @(Get-Item -LiteralPath (Join-Path $flowPath 'UI\ResearcherSetupView.cs'), (Join-Path $flowPath 'UI\ResearcherParticipantDropdown.cs'), (Join-Path $flowPath 'UI\ResearcherDesktopPointer.cs'), (Join-Path $flowPath 'Editor\ResearcherConsole.cs')) + @(Get-ChildItem -LiteralPath (Join-Path $flowPath 'Core') -Filter '*.cs')
$sources += @(Get-Item -LiteralPath (Join-Path $flowPath 'UI\ResearchUITheme.cs'), (Join-Path $flowPath 'UI\ResearchUIFeedback.cs'), (Join-Path $flowPath 'UI\ResearchGlassEffect.cs'))
$xmlSources = $sources | ForEach-Object { '<Compile Include="' + [Security.SecurityElement]::Escape($_.FullName) + '" Link="' + $_.Name + '" />' }
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DefineConstants>UNITY_EDITOR</DefineConstants><EnableDefaultCompileItems>true</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup>$($xmlReferences -join "`n")$($xmlSources -join "`n")</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $temporaryPath 'Check.csproj') -Encoding UTF8
@'
using UnityEngine;
namespace VRMaintenanceResearch.ResearchFlow
{
    public class LoggerContract { public bool DataComplete; }
    public class ResearchSessionFlowController : MonoBehaviour
    {
        public LoggerContract Logger;
        public ResearchParticipantRegistry Registry;
        public ParticipantRecord Record;
        public ResearchSessionFlow Flow;
        public bool DeviceReady, LoggerReady;
        public string Error;
        public void StartSession(string id, ParticipantLanguage lang, ExperimentInputMode input, TaskOrder? order = null) {}
        public void ResearcherResume() {}
        public void ResearcherAbort(bool invalid = false) {}
        public void SetDemoPass(bool passed) {}
        public string ExportSession() => "";
        public string SummaryText() => "";
    }
}
'@ | Set-Content -LiteralPath (Join-Path $temporaryPath 'ControllerContract.cs') -Encoding UTF8
dotnet build (Join-Path $temporaryPath 'Check.csproj') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Researcher view compile check failed.' }
'PASS: researcher view and Editor console compile against installed Unity/TMP with the controller API contract.'
