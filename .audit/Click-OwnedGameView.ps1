param([string]$PointFile, [int]$UnityPid=45248)
$ErrorActionPreference='Stop'
$point=Get-Content -LiteralPath $PointFile -Raw | ConvertFrom-Json
Add-Type @'
using System;using System.Runtime.InteropServices;
public static class S1OwnedClick {
    [StructLayout(LayoutKind.Sequential)]public struct Point {public int x,y;}
    [DllImport("user32.dll")]public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]public static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]public static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
}
'@
if([S1OwnedClick]::SetThreadDpiAwarenessContext([IntPtr](-4)) -eq [IntPtr]::Zero){throw 'Thread physical-pixel coordinate mode failed'}
$p=[S1OwnedClick+Point]::new();$p.x=[int]$point.px;$p.y=[int]$point.py
[uint32]$owner=0;[void][S1OwnedClick]::GetWindowThreadProcessId([S1OwnedClick]::WindowFromPoint($p),[ref]$owner)
if($owner -ne $UnityPid){throw 'Measured Game View point is not owned by canonical Unity; no click sent'}
if(-not [S1OwnedClick]::SetCursorPos($p.x,$p.y)){throw 'Pointer move failed'}
[S1OwnedClick]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
[S1OwnedClick]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
Start-Sleep -Milliseconds 150
[uint32]$foreground=0;[void][S1OwnedClick]::GetWindowThreadProcessId([S1OwnedClick]::GetForegroundWindow(),[ref]$foreground)
if($foreground -ne $UnityPid){throw 'Owned Game View click did not acquire Unity foreground'}
[pscustomobject]@{scope='One desktop focus click inside measured owned Game View; not VR controller acceptance';x=$p.x;y=$p.y;owner=$owner}|ConvertTo-Json -Compress
