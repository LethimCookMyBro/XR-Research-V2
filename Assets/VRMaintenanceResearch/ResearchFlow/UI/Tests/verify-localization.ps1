# Run from any directory with Windows PowerShell and .NET SDK 10 installed.
$ErrorActionPreference = 'Stop'
$uiPath = Split-Path -Parent $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $uiPath '..\..\..\..')).Path
$sourcePath = Join-Path $uiPath 'ResearchStrings.cs'
$testPath = Join-Path ([IO.Path]::GetTempPath()) ('ResearchLocalization-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath | Out-Null
$escapedSource = [Security.SecurityElement]::Escape($sourcePath)
$escapedScheme = [Security.SecurityElement]::Escape((Join-Path $uiPath '..\Core\ResearchReferenceScheme.cs'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="$escapedSource" Link="ResearchStrings.cs" /><Compile Include="$escapedScheme" Link="ResearchReferenceScheme.cs" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $testPath 'Check.csproj') -Encoding UTF8
@'
using System;
using System.Linq;
using VRMaintenanceResearch.ResearchFlow;
class Check
{
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var english = ResearchStrings.All("English");
        string[] pageLanguages = { "English", "Thai", "Japanese" };
        string[] previousPages = { "Previous page", "หน้าก่อนหน้า", "前のページ" };
        string[] nextPages = { "Next page", "หน้าถัดไป", "次のページ" };
        string[] pagePositions = { "Page 2 of 5", "หน้า 2 จาก 5", "2 / 5 ページ" };
        for (int i = 0; i < pageLanguages.Length; i++)
        {
            Require(ResearchStrings.Get("action.previous", pageLanguages[i]) == previousPages[i], "Previous action identifies a page: " + pageLanguages[i]);
            Require(ResearchStrings.Get("action.next", pageLanguages[i]) == nextPages[i], "Next action identifies a page: " + pageLanguages[i]);
            Require(string.Format(ResearchStrings.Get("reference.page_position", pageLanguages[i]), 2, 5) == pagePositions[i], "Reference progress names current page and total pages: " + pageLanguages[i]);
        }
        Require(ResearchStrings.Get("welcome.title", "Thai") == "ยินดีต้อนรับสู่การทดลอง VR", "Welcome names the VR experiment");
        Require(ResearchStrings.Get("welcome.body", "Thai") == "คุณจะเริ่มจากบทฝึกสั้น ๆ\nจากนั้นทำงานซ่อมบำรุง 2 งาน\n\nหากรู้สึกไม่สบาย\nให้หยุดและแจ้งผู้วิจัยได้ทุกเมื่อ", "Welcome explains practice, two tasks and the stop action");
        Require(ResearchStrings.Get("tutorial.complete", "Thai") == "บทฝึกเสร็จแล้ว", "Tutorial completion names what finished");
        Require(ResearchStrings.Get("tutorial.complete.body", "Thai") == "ต่อไปคือ งานที่ 1 จากทั้งหมด 2 งาน", "Tutorial completion states the next stage");
        Require(ResearchStrings.Get("task1.completed", "Thai") == "งานที่ 1 เสร็จแล้ว", "First task completion names what finished");
        Require(ResearchStrings.Get("inspect.pass.body", "Thai") == "ยังไม่ต้องถอดแว่น\nหน้าถัดไปจะบอกสิ่งที่ต้องทำต่อ", "Legacy pass copy does not prematurely ask for headset removal");
        Require(string.Format(ResearchStrings.Get("task.progress", "Thai"), 2) == "งานที่ 2 จาก 2", "Shared ready progress names the second stage");
        Require(string.Format(ResearchStrings.Get("task.progress", "Japanese"), 2) == "課題 2 / 2", "Shared Japanese ready progress retains task count (draft)");
        Require(ResearchStrings.Get("checkpoint.body", "Thai") == "1  ถอดแว่น VR\n2  ทำ NASA-TLX และตรวจอาการกับผู้วิจัย\n3  พักและรอผู้วิจัยเริ่มงานที่ 2", "Checkpoint separates participant steps and researcher responsibility");
        string[] predictiveActions = { "action.go_task1", "action.start_task", "action.go_checkpoint", "action.finish_vr", "action.return_task" };
        string[] predictiveThai = { "ไปงานที่ 1", "เริ่มงาน", "ไปช่วงพักและแบบประเมิน", "จบงาน VR", "กลับไปทำงาน" };
        for (int i = 0; i < predictiveActions.Length; i++)
            Require(ResearchStrings.Get(predictiveActions[i], "Thai") == predictiveThai[i], "Predictive Thai action: " + predictiveActions[i]);
        foreach (var language in new[] { "Thai", "Japanese", "English" })
        {
            var content = ResearchStrings.All(language);
            Require(content.Keys.SequenceEqual(english.Keys), "Key parity: " + language);
            Require(content.Values.All(s => !string.IsNullOrWhiteSpace(s)), "Empty content: " + language);
            Require(content.Values.All(s => !s.Contains("??")), "Encoding corruption: " + language);
            Require(ResearchReferenceScheme.Active.SequenceEqual(new[] { "user_manual", "step_by_step", "illustrated", "video" }), "24 Sep four information formats");
            foreach (var format in ResearchReferenceScheme.Active)
                Require(content.ContainsKey("reference." + format) && content.ContainsKey("reference.training." + format), "Localized active format and practice content: " + format);
            Require(ResearchStrings.Get("inspect.fail.body", language).Length > 0, "Neutral failure text");
            Require(ResearchStrings.Get("checkpoint.body", language).Contains("NASA-TLX"), "External NASA checkpoint");
            foreach (var key in predictiveActions.Concat(new[] { "task1.completed", "task2.completed", "task.progress", "task.ready.reference", "task.ready.goal", "tutorial.compact.now", "tutorial.compact.switch", "tutorial.compact.drag", "tutorial.compact.drag.hand", "tutorial.compact.minimize", "tutorial.compact.restore", "tutorial.compact.close", "illustrated.title", "illustrated.title.hand" }))
                Require(content.ContainsKey(key), "Localized workflow/illustration key: " + language + " " + key);
            Require(string.Format(content["task.ready.goal"], "NEUTRAL_GOAL").Contains("NEUTRAL_GOAL"), "Ready preserves the supplied neutral goal: " + language);
            Require(string.Format(content["tutorial.compact.now"], "CURRENT_ACTION").Contains("CURRENT_ACTION"), "Compact coach names only the current action: " + language);
            foreach (var action in new[] { "switch", "drag", "drag.hand", "minimize", "restore", "close", "inspect" })
            {
                string key = "tutorial.compact." + action;
                Require(content.TryGetValue(key, out var cue) && !string.IsNullOrWhiteSpace(cue) && cue != key, "Reachable compact coach action has localized copy: " + language + " " + action);
            }
            Require(content["tutorial.compact.inspect"].Contains(content["action.inspect"]), "Reopened Reference retains the existing Inspect term: " + language);
            for (int panel = 1; panel <= 4; panel++)
            {
                Require(content.ContainsKey("illustrated.caption." + panel), "Controller illustrated caption: " + language + " " + panel);
                Require(content.ContainsKey("illustrated.caption." + panel + ".hand"), "Hand illustrated caption: " + language + " " + panel);
            }
            Require(content["reference.training.step_by_step"].Contains("1.") && content["reference.training.step_by_step"].Contains("4."), "Training steps contain actions, not format metadata: " + language);
            string unavailable = language == "Thai" ? "วิดีโอยังไม่พร้อม" : language == "Japanese" ? "動画は利用できません" : "Video unavailable";
            Require(content["reference.training.video"].StartsWith(unavailable), "Missing practice video has an explicit unavailable status: " + language);
            Require(content.Where(row => !row.Key.StartsWith("researcher.", StringComparison.Ordinal))
                .All(row => !System.Text.RegularExpressions.Regex.IsMatch(row.Value, @"\bDEV(?:\s+ONLY)?\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)),
                "Participant copy has no visible DEV label: " + language);
        }
        Require(ResearchStrings.Get("welcome.title", "TH") == ResearchStrings.Get("welcome.title", "Thai"), "TH alias");
        Require(ResearchStrings.Get("welcome.title", "ja") == ResearchStrings.Get("welcome.title", "Japanese"), "JA alias");
        Require(ResearchStrings.Get("welcome.title", "unknown") == english["welcome.title"], "English fallback");
        Require(ResearchStrings.Get(null, "Thai") == "", "Null key");
        Require(ResearchStrings.Get("missing.key", "Thai") == "missing.key", "Missing key diagnostic");
        Require(ResearchStrings.Get("inspect.fail.body", "English") == "The current task state does not satisfy the completion requirements.", "Failure must not add a hint");
        string[] terms = { "action.reference", "action.inspect", "action.examine", "action.pause", "action.minimize", "action.close", "action.recenter", "tutorial.start", "action.continue", "helper.action", "action.restore", "reference.video", "action.previous", "action.next", "reference.user_manual", "reference.step_by_step", "reference.illustrated", "action.practice", "setup.wait", "action.resume", "action.help", "action.pause_short" };
        string[] thai = { "เอกสารอ้างอิง", "ตรวจผล", "ตรวจดู", "พัก / ขอความช่วยเหลือ", "ย่อ", "ปิด", "กลับมาตรงหน้า", "เริ่มบทฝึก", "ดำเนินการต่อ", "วิธีใช้งาน", "เปิดแผง", "วิดีโอ", "หน้าก่อนหน้า", "หน้าถัดไป", "คู่มือ", "ทีละขั้นตอน", "ภาพประกอบ", "ปุ่มฝึก", "รอผู้วิจัยเริ่มเซสชัน", "ทำต่อ", "ขอความช่วยเหลือ", "พัก" };
        string[] japanese = { "参照資料", "検査", "確認", "一時停止 / ヘルプ", "最小化", "閉じる", "正面に戻す", "練習を開始", "続ける", "操作方法", "パネルを開く", "動画", "前のページ", "次のページ", "マニュアル", "手順", "図解", "練習ボタン", "研究者がセッションを開始するまでお待ちください。", "再開", "ヘルプを依頼", "一時停止" };
        for (int i = 0; i < terms.Length; i++) {
            Require(ResearchStrings.Get(terms[i], "Thai") == thai[i], "Thai glossary: " + terms[i]);
            Require(ResearchStrings.Get(terms[i], "Japanese") == japanese[i], "Japanese glossary: " + terms[i]);
        }
        Require(ResearchStrings.TranslationStatus == "BILINGUAL_RESEARCHER_REVIEW_PENDING", "Human review must remain pending");
        Require(ResearchStrings.Get("status.wait_researcher", "Thai") == "กำลังรอผู้วิจัย", "Checkpoint waits for researcher");
        var taskTerms = new[] {
            new[] { "English", "A", "W2 memory, storage and graphics requirements", "stopped and isolated from power", "Do not remove the CPU, cooler or PSU", "or install an operating system", "Choose your method and order", "not all provided parts are needed" },
            new[] { "Thai", "A", "W2 ด้านหน่วยความจำ หน่วยเก็บข้อมูล และกราฟิก", "หยุดและแยกไฟแล้ว", "ไม่ถอด CPU ชุดระบายความร้อน หรือ PSU", "ไม่ติดตั้งระบบปฏิบัติการ", "เลือกวิธีและลำดับเองได้", "ไม่ต้องใช้ทุกชิ้นส่วน" },
            new[] { "Japanese", "A", "メモリ・ストレージ・グラフィックス", "電源から切り離されています", "CPU・クーラー・PSUは取り外さず", "OSもインストールしません", "方法と順序は自由", "すべての部品を使う必要はありません" },
            new[] { "English", "B", "fan vibrates or knocks abnormally", "guards, blade and fastenings", "Do not open the motor or electrical circuit", "isolated from power while handling parts", "Complete the protective assembly before selecting Inspect", "Choose where to check and how to work" },
            new[] { "Thai", "B", "เสียงสั่นหรือเสียงกระทบผิดปกติ", "ตะแกรง ใบพัด และชุดยึด", "ไม่เปิดมอเตอร์หรือวงจรไฟฟ้า", "คงการแยกไฟขณะจัดชิ้นส่วน", "ประกอบชุดป้องกันให้ครบก่อนกด", "เลือกจุดตรวจและวิธีทำงานเองได้" },
            new[] { "Japanese", "B", "振動や異常な打音", "ガード・羽根・固定部品のみ", "モーターや電気回路は開けません", "電源から切り離した状態を保ち", "保護部品をすべて組み立ててから", "点検場所と作業方法は自由" }
        };
        foreach (var row in taskTerms) {
            string body = ResearchStrings.Get("task." + row[1] + ".body", row[0]);
            string summary = ResearchStrings.Get("task." + row[1] + ".summary", row[0]);
            Require(body.Split('\n').Length == 4 && row.Skip(2).All(body.Contains) && body.Split('\n')[3].Contains(ResearchStrings.Get("action.reference", row[0])), "Full task details retain authored S1 scope, safety and method choice: " + row[0] + " " + row[1]);
            Require(summary.Split('\n').Length == 2 && summary.Contains(ResearchStrings.Get("action.inspect", row[0])), "Compact task has only goal and safety, with the real Inspect term: " + row[0] + " " + row[1]);
        }
        var summarySafetyTerms = new[] {
            new[] { "English", "A", "Power is isolated", "Do not remove the CPU, cooler or PSU" },
            new[] { "Thai", "A", "แยกไฟแล้ว", "ไม่ถอด CPU ชุดระบายความร้อน หรือ PSU" },
            new[] { "Japanese", "A", "電源は切り離されています", "CPU・クーラー・PSUは取り外さない" },
            new[] { "English", "B", "Keep power isolated", "Do not open the motor or electrical circuit", "Refit all guards before Inspect" },
            new[] { "Thai", "B", "แยกไฟแล้ว", "ไม่เปิดมอเตอร์หรือวงจรไฟฟ้า", "ใส่ตะแกรงครบก่อนตรวจผล" },
            new[] { "Japanese", "B", "電源を切り離したまま", "モーター・電気回路は開けず", "ガードをすべて取り付けてから検査" }
        };
        foreach (var row in summarySafetyTerms)
            Require(row.Skip(2).All(ResearchStrings.Get("task." + row[1] + ".summary", row[0]).Contains), "Compact safety retains the authored work boundaries: " + row[0] + " " + row[1]);
        Require(ResearchStrings.Get("inspect.fail.body", "Thai") == "สถานะปัจจุบันยังไม่ตรง\nตามเงื่อนไขของงาน", "Exact neutral fail copy");
        Require(ResearchStrings.Get("checkpoint.safety", "Thai") == "หากรู้สึกไม่สบาย ให้แจ้งผู้วิจัยทันที", "Concise checkpoint safety");
        Require(ResearchStrings.Get("completion.title", "Thai") == "งาน VR เสร็จครบแล้ว", "Exact completion title");
        var checkpointTerms = new[] {
            new[] { "English", "researcher", "start task 2", "final questionnaire", "symptom check", "interview", "finalize the session", "researcher has confirmed the end" },
            new[] { "Thai", "ผู้วิจัย", "เริ่มงานที่ 2", "แบบประเมินสุดท้าย", "ตรวจอาการ", "สัมภาษณ์", "ยืนยันจบเซสชัน", "ผู้วิจัยยืนยันจบเซสชันแล้ว" },
            new[] { "Japanese", "研究者", "課題2を開始", "最終質問票", "体調確認", "面接", "セッションを終了", "研究者がセッションの終了を確認しました" }
        };
        foreach (var row in checkpointTerms) {
            string language = row[0], first = ResearchStrings.Get("checkpoint.body", language), second = ResearchStrings.Get("checkpoint2.body", language);
            Require(first.Split('\n').Length == 3 && first.Contains("NASA-TLX") && first.Contains(row[1]) && first.Contains(row[2]), "First checkpoint names external assessment and researcher-owned Task 2: " + language);
            Require(second.Split('\n').Length == 3 && second.Contains(row[1]) && row.Skip(3).Take(4).All(second.Contains), "Second checkpoint names final assessment and researcher-owned session finalization: " + language);
            Require(ResearchStrings.Get("completion.body", language).Contains(row[7]), "Completion follows researcher confirmation of the session end: " + language);
            Require(string.Format(ResearchStrings.Get("task.progress", language), 1).Contains("1") && ResearchStrings.Get("task.progress", language).Contains("2"), "Task n of 2: " + language);
            foreach (var task in new[] { "A", "B" })
                Require(!System.Text.RegularExpressions.Regex.IsMatch(ResearchStrings.Get("task." + task + ".title", language), @"(?:\bTask\s+|งาน\s*|課題\s*)[AB](?![-\d])", System.Text.RegularExpressions.RegexOptions.IgnoreCase), "Participant title hides counterbalancing letters without hiding the authored job code: " + language + " " + task);
        }
        // Handling preserves reach/lift/rotate/release and Touch Grip; the helper explicitly teaches moving while held.
        Require(ResearchStrings.Get("tutorial.handling.body", "Thai") == "เข้าใกล้วัตถุ ยก หมุน แล้วปล่อย (Touch: กด Grip ด้านข้างค้างไว้เพื่อจับ)", "Thai handling and Touch Grip instructions");
        Require(ResearchStrings.Get("tutorial.handling.body", "Japanese") == "手を近づけてつかみ、持ち上げ、回して放します。Touchでは側面のGripボタンを押し続けます。", "Japanese handling and Touch Grip instructions");
        Require(ResearchStrings.Get("tutorial.handling.helper", "Thai") == "จับวัตถุค้างไว้ แล้วขยับมือเพื่อย้าย", "Thai helper retains movement while holding");
        Require(ResearchStrings.Get("tutorial.handling.helper", "Japanese") == "物体をつかんだまま、手を動かしてください。", "Japanese helper retains movement while holding");
        // ResearchFlowPlayChecks checks handling TMP fit and <=26 visible characters per rendered Japanese line; pixel review is also required.
        foreach (var step in new[] { "movement", "reference" })
            Require(ResearchStrings.Get("tutorial." + step + ".body", "Japanese").Split('\n').All(line => line.Length <= 20), "Compact Japanese active detail: " + step);
        Console.WriteLine("PASS: " + english.Count + " keys x 3 languages; key parity, aliases, fallback, checkpoint, neutral failure, TH/JP glossary and pending human review.");
    }
}
'@ | Set-Content -LiteralPath (Join-Path $testPath 'Program.cs') -Encoding UTF8
dotnet run --project (Join-Path $testPath 'Check.csproj') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Localization runtime checks failed.' }

Add-Type -AssemblyName PresentationCore
$fontFiles = @(
    @('English', 1, 'Assets\TextMesh Pro\Fonts\LiberationSans.ttf'),
    @('Thai', 2, 'Assets\VRMaintenanceResearch\TrainingV2\UI\Fonts\NotoSansThai.ttf'),
    @('Japanese', 3, 'Assets\VRMaintenanceResearch\ResearchFlow\UI\Fonts\NotoSansJP-Regular.otf')
)
$rows = Get-Content -LiteralPath $sourcePath -Encoding UTF8 | Where-Object { $_ -match '^\s*new\[\]\s*\{' }
foreach ($language in $fontFiles) {
    $font = [System.Windows.Media.GlyphTypeface]::new([Uri](Join-Path $projectRoot $language[2]))
    $chars = @($rows | ForEach-Object { [regex]::Matches($_, '"([^"]*)"')[$language[1]].Groups[1].Value.ToCharArray() } | Sort-Object -Unique)
    $missing = @($chars | Where-Object { -not $font.CharacterToGlyphMap.ContainsKey([int]$_) })
    if ($missing.Count -gt 0) { throw ($language[0] + ' missing glyphs: ' + [string]::Join('', $missing)) }
    Write-Output ('PASS: ' + $language[0] + ' source font covers ' + $chars.Count + ' unique UI characters.')
}
Write-Output 'Rendered TMP layout and bilingual semantic review are separate required checks.'
