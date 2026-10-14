$ErrorActionPreference = 'Stop'
$flowPath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('ResearchTaskPort-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath | Out-Null
$port = [Security.SecurityElement]::Escape((Join-Path $flowPath 'Runtime\ResearchTaskPort.cs'))
$manual = [Security.SecurityElement]::Escape((Join-Path $flowPath 'UI\ResearchDemoManual.cs'))
$scheme = [Security.SecurityElement]::Escape((Join-Path $flowPath 'Core\ResearchReferenceScheme.cs'))
$strings = [Security.SecurityElement]::Escape((Join-Path $flowPath 'UI\ResearchStrings.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
<ItemGroup><Compile Include="$port" Link="ResearchTaskPort.cs"/><Compile Include="$manual" Link="ResearchDemoManual.cs"/><Compile Include="$scheme" Link="ResearchReferenceScheme.cs"/><Compile Include="$strings" Link="ResearchStrings.cs"/></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testPath 'Check.csproj') -Encoding UTF8
@'
using System;
using System.Collections.Generic;
using System.Linq;
using VRMaintenanceResearch.ResearchFlow;
namespace UnityEngine { public class MonoBehaviour {} }
class Check
{
    static int checks;
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Console.WriteLine("PASS: " + message); }
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, message);
    }
    static void Main()
    {
        var task = new ResearchTaskPort();
        Require(!task.IsReadyForStudy && !task.TryInspect(out _), "Unbound study must be blocked");
        Require(task.Reference("user_manual", "Thai") == "", "No demo manual leaks into study");
        task.Bind(() => new { installed = true }, () => true);
        task.contentApproved = true;
        Require(!task.IsReadyForStudy, "Missing real manual must block");
        task.BindReference((section, language) => "approved task content");
        Require(!task.IsReadyForStudy && !task.TryInspect(out _), "Approved content without physical interaction gate remains blocked");
        Require(task.Reference("user_manual", "Thai") == "", "Incomplete real adapter exposes no manual");
        Reject(() => task.CaptureState(), "Incomplete real adapter rejects state capture");
        bool physicalEnabled = true;
        int evaluations = 0;
        var gateCalls = new List<bool>();
        Action<bool> gate = allowed => { physicalEnabled = allowed; gateCalls.Add(allowed); };
        task.Bind(() => new { installed = true }, () => { evaluations++; return true; }, gate);
        Require(task.IsReadyForStudy && !task.InteractionAllowed && !physicalEnabled && gateCalls.SequenceEqual(new[] { false }), "Binding approved study starts mechanics and interactions disabled");
        Require(task.Reference("user_manual", "Thai") == "approved task content", "Approved active format reaches bound provider");
        Require(task.Reference("manual", "Thai") == "", "Legacy category is not an active format");
        Require(!task.TryInspect(out _) && evaluations == 0, "Paused inspection never invokes evaluator");
        Reject(() => task.Examine("module.C10", "Controller"), "Paused task rejects Examine");
        Reject(() => task.Action("module.C10", new { installed = false }, new { installed = true }, "Controller", true), "Paused task rejects Repair");
        task.SetInteractionAllowed(true);
        Require(physicalEnabled && task.InteractionAllowed && gateCalls.SequenceEqual(new[] { false, true }), "Active task explicitly enables physical adapter");
        Require(task.IsReadyForStudy && task.TryInspect(out var passed) && passed, "Bound approved study evaluator");
        task.SetInteractionAllowed(true);
        Require(gateCalls.Count == 2, "Unchanged interaction state does not repeatedly invoke physical adapter");
        task.SetInteractionAllowed(false);
        Require(!physicalEnabled && !task.InteractionAllowed && !task.TryInspect(out passed) && !passed && evaluations == 1, "Modal pause disables mechanics and prevents evaluation");
        task.Bind(() => new { installed = false }, () => false, gate);
        Require(!physicalEnabled && !task.InteractionAllowed, "Rebinding keeps physical mechanics disabled");
        task.SetInteractionAllowed(true);
        Require(task.TryInspect(out passed) && !passed, "Study fail remains fail");
        var events = new List<string>();
        object lastAfter = null;
        task.Behavior += (name, target, before, after) => { events.Add(name + ":" + target); lastAfter = after; };
        bool invalidInputRejected = false;
        try { task.Examine("module.C10", "controller"); } catch (ArgumentException) { invalidInputRejected = true; }
        Require(invalidInputRejected && events.Count == 0, "Noncanonical input is rejected without participant events");
        task.Examine("module.C10", "Controller");
        task.Action("module.C10", new { installed = false }, new { installed = true }, "Controller", true);
        Require(events.SequenceEqual(new[] { "EXAMINE:module.C10", "REPAIR:module.C10" }), "Deliberate event ordering");
        Require((string)((Dictionary<string, object>)lastAfter)["input_source"] == "Controller", "Input provenance");
        Require(!(bool)((Dictionary<string, object>)lastAfter)["test_data"], "Study event is not marked demo");
        int eventCount = events.Count;
        task.SetInteractionAllowed(false);
        Reject(() => task.Examine("module.C10", "Controller"), "Paused Examine cannot emit behavior");
        Reject(() => task.Action("module.C10", new { installed = false }, new { installed = true }, "Controller"), "Paused Action cannot emit behavior");
        Require(events.Count == eventCount, "Rejected interactions add no participant raw events");
        task.Bind(() => new { installed = true }, () => true, allowed => { if (allowed) throw new InvalidOperationException("fixture gate failure"); });
        Reject(() => task.SetInteractionAllowed(true), "Physical gate failure propagates to controller safety handling");
        Require(!task.InteractionAllowed && !task.TryInspect(out passed) && !passed, "Failed physical gate leaves port unavailable");
        Reject(() => task.Examine("module.C10", "Controller"), "Failed physical gate rejects Examine");
        task.Bind(() => throw new InvalidOperationException(), () => true, gate);
        task.SetInteractionAllowed(true);
        Require(!task.TryInspect(out passed) && !passed && events.Last() == "SYSTEM_ERROR:task.A.inspect", "Provider failure must not pass");
        var error = (Dictionary<string, object>)lastAfter;
        Require((string)error["source"] == "system" && !(bool)error["test_data"], "Provider failure keeps neutral system provenance");
        task.Bind(() => new { installed = true }, () => throw new InvalidOperationException(), gate);
        task.SetInteractionAllowed(true);
        Require(!task.TryInspect(out passed) && !passed && events.Last() == "SYSTEM_ERROR:task.A.inspect", "Evaluator failure must not pass");
        task.Bind(() => null, () => true, gate);
        task.SetInteractionAllowed(true);
        Require(!task.TryInspect(out passed) && !passed && events.Last() == "SYSTEM_ERROR:task.A.inspect", "Null task snapshot fails closed");
        task.demoMode = true;
        task.demoPassed = true;
        Require(!task.TryInspect(out passed) && !passed, "Bound technical demo rejects null state instead of trusting demoPassed");
        task.SetInteractionAllowed(false);
        task = new ResearchTaskPort { demoMode = true };
        task.SetInteractionAllowed(true);
        task.demoPassed = false;
        Require(!task.IsReadyForStudy && task.TryInspect(out passed) && !passed, "Demo is not study-ready or automatic pass");
        task.demoPassed = true;
        Require(task.TryInspect(out passed) && passed, "Explicit demo pass");
        task.SetInteractionAllowed(false);
        Require(!task.TryInspect(out passed) && !passed && !physicalEnabled, "Demo pass never bypasses paused interaction gate");
        task.SetInteractionAllowed(true);
        Require((bool)((Dictionary<string, object>)task.CaptureState())["test_data"], "Demo state tagged");
        task.taskId = "unknown";
        Require(!task.TryInspect(out _), "Invalid task blocked");
        foreach (var language in new[] { "English", "Thai", "Japanese" })
        {
            var content = ResearchDemoManual.All(language);
            Require(content.Values.All(s => !string.IsNullOrWhiteSpace(s)), "No empty demo content: " + language);
            Require(content.Keys.SequenceEqual(ResearchDemoManual.All("English").Keys), "Content key parity");
            foreach (var taskId in new[] { "A", "B" })
                foreach (var format in ResearchReferenceScheme.Active)
                    Require(!string.IsNullOrWhiteSpace(ResearchDemoManual.Get(taskId, format, language)), "Demo format available: " + taskId + "." + format + "/" + language);
        }
        Require(ResearchReferenceScheme.Active.SequenceEqual(new[] { "user_manual", "step_by_step", "illustrated", "video" }), "24 Sep four information formats stay active");
        Require(ResearchDemoManual.Get("A", "unknown", "Thai") == "", "Unknown demo format stays empty");
        Console.WriteLine("RESEARCH_TASK_PORT_PASS checks=" + checks + "; physical gate callback contract, neutral behavior, fail-closed providers and demo/manual parity; not physical hardware verification.");
    }
}
'@ | Set-Content -LiteralPath (Join-Path $testPath 'Program.cs') -Encoding UTF8
dotnet run --project (Join-Path $testPath 'Check.csproj') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Task port checks failed.' }

Add-Type -AssemblyName PresentationCore
$projectRoot = (Resolve-Path (Join-Path $flowPath '..\..\..')).Path
$fontFiles = @(@('English', 1, 'Assets\TextMesh Pro\Fonts\LiberationSans.ttf'), @('Thai', 2, 'Assets\VRMaintenanceResearch\TrainingV2\UI\Fonts\NotoSansThai.ttf'), @('Japanese', 3, 'Assets\VRMaintenanceResearch\ResearchFlow\UI\Fonts\NotoSansJP-Regular.otf'))
$rows = Get-Content -LiteralPath (Join-Path $flowPath 'UI\ResearchDemoManual.cs') -Encoding UTF8 | Where-Object { $_ -match '^\s*new\[\]\s*\{' }
foreach ($language in $fontFiles) {
    $font = [System.Windows.Media.GlyphTypeface]::new([Uri](Join-Path $projectRoot $language[2]))
    $chars = @($rows | ForEach-Object { [regex]::Matches($_, '"([^"]*)"')[$language[1]].Groups[1].Value.ToCharArray() } | Sort-Object -Unique)
    $missing = @($chars | Where-Object { -not $font.CharacterToGlyphMap.ContainsKey([int]$_) })
    if ($missing.Count -gt 0) { throw ($language[0] + ' missing glyphs: ' + [string]::Join('', $missing)) }
    Write-Output ('PASS: ' + $language[0] + ' source font covers ' + $chars.Count + ' unique manual characters.')
}
'Standalone checks use a MonoBehaviour shim. Unity component integration and rendered layout remain separate checks.'
