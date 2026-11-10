param([int]$UnityPid = 45248)
$ErrorActionPreference='Stop'
Add-Type @'
using System;using System.Text;using System.Runtime.InteropServices;
public static class S1Foreground {
 public delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")]public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
 [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]public static extern bool AttachThreadInput(uint a,uint b,bool join);
 [DllImport("user32.dll")]public static extern bool SetForegroundWindow(IntPtr h);
}
'@
$windows=[Collections.Generic.List[object]]::new()
[void][S1Foreground]::EnumWindows({param($h,$unused)
 [uint32]$owner=0;[void][S1Foreground]::GetWindowThreadProcessId($h,[ref]$owner)
 if($owner -eq $UnityPid -and [S1Foreground]::IsWindowVisible($h)) {
  $title=[Text.StringBuilder]::new(512);[void][S1Foreground]::GetWindowText($h,$title,512)
  if($title.ToString() -like 'XR-Research-V2*'){$windows.Add([pscustomobject]@{handle=$h;title=$title.ToString()})}
 };return $true
},[IntPtr]::Zero)
if($windows.Count -ne 1){throw 'Canonical project window is not unique'}
$target=$windows[0].handle
if([S1Foreground]::IsIconic($target)){[void][S1Foreground]::ShowWindow($target,9);Start-Sleep -Milliseconds 500}
[uint32]$owner=0
$foregroundThread=[S1Foreground]::GetWindowThreadProcessId([S1Foreground]::GetForegroundWindow(),[ref]$owner)
$currentThread=[S1Foreground]::GetCurrentThreadId()
$joined=$currentThread -ne $foregroundThread -and [S1Foreground]::AttachThreadInput($currentThread,$foregroundThread,$true)
try{[void][S1Foreground]::SetForegroundWindow($target)}finally{if($joined){[void][S1Foreground]::AttachThreadInput($currentThread,$foregroundThread,$false)}}
Start-Sleep -Milliseconds 250
if([S1Foreground]::GetForegroundWindow() -ne $target){throw 'Real canonical Unity foreground not acquired'}
[pscustomobject]@{project=$windows[0].title;foreground=$target.ToInt64();input_thread_detached=$true}|ConvertTo-Json -Compress
