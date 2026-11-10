param([int]$UnityPid=45248,[ValidateSet('prepare','snapshot','restore')][string]$Action='snapshot',[string]$StateFile)
$ErrorActionPreference='Stop'
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class S1Window {
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [StructLayout(LayoutKind.Sequential)]public struct Rect{public int left,top,right,bottom;}
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc p,IntPtr x);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]public static extern bool ShowWindowAsync(IntPtr h,int n);
 [DllImport("user32.dll")]public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll",EntryPoint="GetWindowLongW")]public static extern int GetWindowLong(IntPtr h,int n);
 [DllImport("user32.dll")]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
}
'@
$windows=[Collections.Generic.List[object]]::new()
[void][S1Window]::EnumWindows({param($h,$unused)
 [uint32]$owner=0;[void][S1Window]::GetWindowThreadProcessId($h,[ref]$owner)
 if($owner -eq $UnityPid -and [S1Window]::IsWindowVisible($h)){
  $title=[Text.StringBuilder]::new(512);[void][S1Window]::GetWindowText($h,$title,512)
  if($title.ToString() -like 'XR-Research-V2*'){$windows.Add([pscustomobject]@{handle=$h;title=$title.ToString()})}
 };return $true
},[IntPtr]::Zero)
if($windows.Count -ne 1){throw 'Canonical project window not unique'}
$h=$windows[0].handle
if($Action -eq 'prepare'){
 if(-not $StateFile -or (Test-Path -LiteralPath $StateFile)){throw 'Fresh scope state required'}
 [pscustomobject]@{pid=$UnityPid;handle=$h.ToInt64();topmost=(([S1Window]::GetWindowLong($h,-20) -band 8) -ne 0);minimized=[S1Window]::IsIconic($h)}|ConvertTo-Json|Set-Content -LiteralPath $StateFile -Encoding UTF8
 [void][S1Window]::ShowWindowAsync($h,3)
 [void][S1Window]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x53)
 Start-Sleep -Milliseconds 500
}
if($Action -eq 'restore'){
 $s=Get-Content -LiteralPath $StateFile -Raw|ConvertFrom-Json
 if($s.pid -ne $UnityPid -or $s.handle -ne $h.ToInt64()){throw 'Window identity drift; no restore'}
 if(-not $s.topmost){[void][S1Window]::SetWindowPos($h,[IntPtr](-2),0,0,0,0,0x13)}
}
$r=[S1Window+Rect]::new();[void][S1Window]::GetWindowRect($h,[ref]$r)
[uint32]$fg=0;[void][S1Window]::GetWindowThreadProcessId([S1Window]::GetForegroundWindow(),[ref]$fg)
[pscustomobject]@{pid=$UnityPid;handle=$h.ToInt64();minimized=[S1Window]::IsIconic($h);foreground_pid=$fg;rect=$r;topmost=(([S1Window]::GetWindowLong($h,-20) -band 8) -ne 0)}|ConvertTo-Json -Compress
