# The screenshot harness for the README.
#
#   powershell -ExecutionPolicy Bypass -File tools\screenshots.ps1 <out-dir> <plan.ps1> [app arguments]
#
# The plan is a script of its own, dot-sourced at the end, which drives the window
# with the Click, Hover, Drag, Key, Type, Wheel, Shot and Dialog functions below.
# Kept apart because what is photographed changes far more often than how.
#
# Two rules it did not have before. Nothing is clicked until the window is proved to
# be the one listening, so a refused foreground request cannot drive somebody else's
# application; and the picture is asked of the window itself rather than scraped off
# the screen, so nothing behind it can be in the shot.
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WB {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int t, bool repaint);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(IntPtr from, IntPtr to, bool attach);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThreadId();
    public delegate bool Enum(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Enum cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
"@

$script:out = $args[0]
$script:plan = $args[1]
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\Waybill\bin\Debug\net9.0-windows\Waybill.exe"
if (-not (Test-Path $exe)) { Write-Output "Build it first: dotnet build src/Waybill"; exit 1 }

$extra = if ($args.Count -gt 2) { $args[2..($args.Count-1)] } else { @() }
$script:p = if ($extra.Count -gt 0) { Start-Process $exe -ArgumentList $extra -PassThru } else { Start-Process $exe -PassThru }
for ($i = 0; $i -lt 80 -and $script:p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 250; $script:p.Refresh() }
Start-Sleep -Seconds 4
$script:h = $script:p.MainWindowHandle
$script:main = $script:h
[void][WB]::MoveWindow($script:h, 30, 30, 1280, 820, $true)
Start-Sleep -Seconds 1

# Windows will not hand the foreground to a process nobody clicked on, which is a
# sensible rule and the reason the last attempt drove the wrong window. The way past
# it is to borrow the input queue of whatever holds the foreground for a moment,
# which is what a window manager does; the result is still checked rather than
# assumed, and nothing is clicked if it did not work.
function Front {
  for ($i = 0; $i -lt 15; $i++) {
    try { [Microsoft.VisualBasic.Interaction]::AppActivate($script:p.Id) } catch { }
    $fg = [WB]::GetForegroundWindow()
    $theirs = [WB]::GetWindowThreadProcessId($fg, [IntPtr]::Zero)
    $mine = [WB]::GetCurrentThreadId()
    $joined = $false
    if ($theirs -ne $mine) { $joined = [WB]::AttachThreadInput($mine, $theirs, $true) }
    [void][WB]::ShowWindow($script:h, 9)
    [void][WB]::BringWindowToTop($script:h)
    [void][WB]::SetForegroundWindow($script:h)
    if ($joined) { [void][WB]::AttachThreadInput($mine, $theirs, $false) }
    Start-Sleep -Milliseconds 400
    if ([WB]::GetForegroundWindow() -eq $script:h) { return $true }
  }
  return $false
}
if (-not (Front)) {
  Write-Output "REFUSED: the window would not come to the front, so nothing was clicked."
  $script:p.Kill(); exit 1
}
# Everything is aimed at the client area, not the window: the picture is cropped to
# the client area too, so a coordinate read off a screenshot means the same thing to
# the mouse. Aimed at the window it was out by the title bar and the border, and
# every click landed a row above where it was read.
$script:r = New-Object WB+POINT; [void][WB]::ClientToScreen($script:h, [ref]$script:r)

function Guard { if ([WB]::GetForegroundWindow() -ne $script:h) { throw "the window lost the foreground" } }
function Hover($x, $y) { Guard; [void][WB]::SetCursorPos($script:r.X+$x, $script:r.Y+$y); Start-Sleep -Milliseconds 700 }
function Click($x, $y) {
  Hover $x $y
  [WB]::mouse_event(0x02,0,0,0,[IntPtr]::Zero); [WB]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
  Start-Sleep -Milliseconds 900
}
function Key($vk, $times = 1) {
  for ($i = 0; $i -lt $times; $i++) {
    Guard
    [WB]::keybd_event($vk, 0, 0, [IntPtr]::Zero)
    [WB]::keybd_event($vk, 0, 2, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 90
  }
  Start-Sleep -Milliseconds 500
}
function Wheel($x, $y, $notches) {
  Hover $x $y
  for ($i = 0; $i -lt [Math]::Abs($notches); $i++) {
    Guard
    [WB]::mouse_event(0x0800, 0, 0, [uint32](if ($notches -lt 0) { 4294967176 } else { 120 }), [IntPtr]::Zero)
    Start-Sleep -Milliseconds 220
  }
  Start-Sleep -Milliseconds 500
}
function Type($text) { Guard; [System.Windows.Forms.SendKeys]::SendWait($text); Start-Sleep -Milliseconds 900 }
function Enter { Guard; [WB]::keybd_event(0x0D,0,0,[IntPtr]::Zero); [WB]::keybd_event(0x0D,0,2,[IntPtr]::Zero); Start-Sleep -Milliseconds 1400 }
function Drag($x1,$y1,$x2,$y2) {
  Hover $x1 $y1
  [WB]::mouse_event(0x02,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 250
  for ($i = 1; $i -le 16; $i++) {
    Guard
    [void][WB]::SetCursorPos($script:r.X + [int]($x1 + ($x2-$x1)*$i/16), $script:r.Y + [int]($y1 + ($y2-$y1)*$i/16))
    Start-Sleep -Milliseconds 40
  }
  [WB]::mouse_event(0x04,0,0,0,[IntPtr]::Zero); Start-Sleep -Milliseconds 1000
}
function Shot($name) {
  $c = New-Object WB+RECT; [void][WB]::GetClientRect($script:h, [ref]$c)
  $q = New-Object WB+RECT; [void][WB]::GetWindowRect($script:h, [ref]$q)
  $o = New-Object WB+POINT; [void][WB]::ClientToScreen($script:h, [ref]$o)
  $full = New-Object System.Drawing.Bitmap ($q.Right-$q.Left), ($q.Bottom-$q.Top)
  $g = [System.Drawing.Graphics]::FromImage($full)
  $dc = $g.GetHdc(); [void][WB]::PrintWindow($script:h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
  $crop = New-Object System.Drawing.Rectangle ($o.X-$q.Left), ($o.Y-$q.Top), ($c.Right-$c.Left), ($c.Bottom-$c.Top)
  $part = $full.Clone($crop, $full.PixelFormat)
  $part.Save((Join-Path $script:out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $part.Dispose(); $full.Dispose()
  "  $name"
}

# A dialog is a window of its own, so it is found rather than assumed, sized to show
# everything it holds at once, and photographed the same way as the main window.
# Aims everything that follows at a window of the app's own that is not the main one,
# so a dialog can be filled in the same way the window is. UseMain puts it back.
function FindDialog {
  $cb = [WB+Enum]{
    param($wh, $lp)
    $owner = 0
    [void][WB]::GetWindowThreadProcessId($wh, [ref]$owner)
    if ($owner -eq $script:p.Id -and $wh -ne $script:main -and [WB]::IsWindowVisible($wh)) {
      $script:found = $wh
      return $false
    }
    return $true
  }
  $script:found = [IntPtr]::Zero
  [void][WB]::EnumWindows($cb, [IntPtr]::Zero)
  return $script:found
}
function UseDialog {
  $wh = FindDialog
  if ($wh -eq [IntPtr]::Zero) { throw "no dialog of the app's own is open" }
  $script:h = $wh
  $script:r = New-Object WB+POINT; [void][WB]::ClientToScreen($script:h, [ref]$script:r)
}
function UseMain {
  $script:h = $script:main
  $script:r = New-Object WB+POINT; [void][WB]::ClientToScreen($script:h, [ref]$script:r)
}

function Dialog($name, $w, $ht) {
  $wh = FindDialog
  if ($wh -eq [IntPtr]::Zero) { return "  no dialog found for $name" }
  $was = $script:h
  $script:h = $wh
  [void][WB]::MoveWindow($script:h, 60, 60, $w, $ht, $true)
  Start-Sleep -Milliseconds 900
  Shot $name
  $script:h = $was
}

. $script:plan

$script:p.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2
if (-not $script:p.HasExited) { $script:p.Kill() }
"done"
