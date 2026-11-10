param([int]$UnityPid = 45248, [switch]$Activate)
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class S1Window {
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc fn, IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder t, int n);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int x; public int y; }
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
'@
$process = Get-Process -Id $UnityPid
if ($process.MainWindowTitle -notlike 'XR-Research-V2*') { throw 'Unity project identity mismatch' }
$rows = [System.Collections.Generic.List[object]]::new()
[S1Window]::EnumWindows({param($h,$unused)
 [uint32]$owner=0
 [void][S1Window]::GetWindowThreadProcessId($h,[ref]$owner)
 if ($owner -eq $UnityPid -and [S1Window]::IsWindowVisible($h)) {
  $title=[Text.StringBuilder]::new(512); [void][S1Window]::GetWindowText($h,$title,512)
  $rows.Add([pscustomobject]@{handle=$h.ToInt64();title=$title.ToString();foreground=($h -eq [S1Window]::GetForegroundWindow())})
 }
 return $true
},[IntPtr]::Zero) | Out-Null
if ($Activate) {
 $main=@($rows | Where-Object title -like 'XR-Research-V2*')
 if ($main.Count -ne 1) { throw 'Canonical Unity window is not unique' }
 if ([S1Window]::IsIconic([IntPtr]$main[0].handle)) { [void][S1Window]::ShowWindow([IntPtr]$main[0].handle,9) }
 # shortcut: this empty Game View corner was measured at 1.5 DPI; remeasure after Editor layout changes.
 try {
  [void][S1Window]::SetWindowPos([IntPtr]$main[0].handle,[IntPtr](-1),0,0,0,0,0x13)
  Start-Sleep -Milliseconds 250
  $point=[S1Window+Point]::new(); $point.x=535; $point.y=160
  $root=[S1Window]::GetAncestor([S1Window]::WindowFromPoint($point),2)
  if ($root -ne [IntPtr]$main[0].handle) { throw 'Measured Game View point is covered; no click sent' }
  [void][S1Window]::SetCursorPos(535,160)
  [S1Window]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  [S1Window]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
 } finally { [void][S1Window]::SetWindowPos([IntPtr]$main[0].handle,[IntPtr](-2),0,0,0,0,0x13) }
 Start-Sleep -Milliseconds 250
 if ([S1Window]::GetForegroundWindow() -ne [IntPtr]$main[0].handle) { throw 'Native Game View click did not acquire foreground' }
}
$rows | ConvertTo-Json -Compress
