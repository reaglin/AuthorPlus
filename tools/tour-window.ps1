# Launch the app, walk through it with UI Automation, and capture its own window with PrintWindow
# (never a screen grab) at each "shot". Steps, in order:
#   invoke:<name>   press the button or menu item with this automation name
#   select:<name>   select the radio button / list item with this name (a section on the left)
#   type:<name>=<text>   set a text box's value (then Tab is sent so it commits, unless -NoKeys)
#   toggle:<name>   tick or untick a check box
#   expand:<name>   open a drop-down list or a menu, so its items can be chosen
#   collapse:<name> close a drop-down list or a menu again
#   pick:<name>=<item>   choose an item of a drop-down list without opening it
#   menu:<menu>=<item>   press an item of a menu (File, Edit, Help)
#   scroll:<name>=<percent>   scroll the area that holds the named element (0 = top, 100 = bottom)
#   close:<title>   close one of the app's own windows (a dialog or message) by its title, through UI Automation
#   size:<w>x<h>    move the main window to the top left and make it this size (UI Automation, no keys)
#   wait:<ms>       wait
#   shot:<file.png> capture the window, with the app's dialogs drawn where they sit
# Example:
#   .\tools\tour-window.ps1 -Exe manual-test\TrafficLabPlus.exe -NoKeys -Steps 'invoke:Open the LPGA Traffic Lab example','wait:3000','shot:shots\a.png'
#
# Everything is found inside the app's own process only, so a button with the same name in another
# program is never pressed. With -NoKeys no keystroke is ever sent (the 'keys:' step is refused).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string[]]$Steps,
    [string]$Args = "",
    [int]$WaitMs = 3000,
    [switch]$NoKeys
)

Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes, System.Windows.Forms

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Win2 {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    /// The visible top-level windows of a process, front first (EnumWindows' z-order).
    public static List<IntPtr> Windows(uint pid) {
        var all = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            RECT r;
            if (p == pid && IsWindowVisible(h) && GetWindowRect(h, out r) && r.Right - r.Left > 40 && r.Bottom - r.Top > 40) all.Add(h);
            return true;
        }, IntPtr.Zero);
        return all;
    }

    public static string Title(IntPtr h) { var sb = new StringBuilder(500); GetWindowText(h, sb, 500); return sb.ToString(); }

    /// The biggest visible top-level window of a process (MainWindowHandle can be 0 for WPF).
    public static IntPtr Main(uint pid) {
        IntPtr best = IntPtr.Zero; long bestArea = 0;
        foreach (var h in Windows(pid)) {
            RECT r; GetWindowRect(h, out r);
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { best = h; bestArea = area; }
        }
        return best;
    }
}
"@

$proc = if ($Args) { Start-Process -FilePath $Exe -ArgumentList $Args -PassThru } else { Start-Process -FilePath $Exe -PassThru }
Start-Sleep -Milliseconds $WaitMs
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) {
    $h = [Win2]::Main([uint32]$proc.Id)
    if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 250 }
}
if ($h -eq [IntPtr]::Zero) { Write-Output "NO WINDOW"; if (-not $proc.HasExited) { $proc.Kill() }; exit 1 }
Write-Output "window '$([Win2]::Title($h))'"
[Win2]::SetForegroundWindow($h) | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$mine = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)

function Find($name, $pattern = $null) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    if ($pattern) {
        # the element that can do it, not a text inside it with the same name
        $can = New-Object System.Windows.Automation.PropertyCondition($pattern, $true)
        $cond = New-Object System.Windows.Automation.AndCondition($cond, $can)
    }
    for ($i = 0; $i -lt 20; $i++) {
        # dialogs are other top-level windows of the process; only inside this process's windows is searched
        # (the page in the WebView2 belongs to another process, but sits inside the app's window)
        foreach ($w in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $mine)) {
            if ($w.Current.Name -eq $name -and (-not $pattern -or $w.GetCurrentPropertyValue($pattern))) { return $w }
            $e = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
            if ($e) { return $e }
        }
        Start-Sleep -Milliseconds 250
    }
    throw "not found: $name"
}

# Keystrokes go to whatever window is in front. They are sent only when that window belongs to the
# app being toured; otherwise the tour stops, so nothing is ever typed into the person's own windows.
function Keys($text) {
    if ($NoKeys) { throw "keys refused (-NoKeys)" }
    $fg = [Win2]::GetForegroundWindow(); $fgPid = 0
    [Win2]::GetWindowThreadProcessId($fg, [ref]$fgPid) | Out-Null
    if ($fgPid -ne $proc.Id) { throw "the app is not the window in front; no keys sent" }
    [System.Windows.Forms.SendKeys]::SendWait($text)
}

function Grab($hwnd) {
    $r = New-Object Win2+RECT
    [Win2]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Win2]::PrintWindow($hwnd, $hdc, 2) | Out-Null   # 2 = PW_RENDERFULLCONTENT, needed for WPF
    $g.ReleaseHdc($hdc); $g.Dispose()
    return @{ Bitmap = $bmp; Rect = $r }
}

# The main window, with the program's other windows (dialogs, messages, open menus) drawn onto it
# where they sit, back to front, so the picture is what a person sees.
function Shot($out) {
    $main = Grab $h
    [object[]]$others = @([Win2]::Windows([uint32]$proc.Id) | Where-Object { $_ -ne $h })
    [array]::Reverse($others)
    $g = [System.Drawing.Graphics]::FromImage($main.Bitmap)
    foreach ($w in @($others)) {
        $front = Grab $w
        $g.DrawImage($front.Bitmap, $front.Rect.Left - $main.Rect.Left, $front.Rect.Top - $main.Rect.Top)
        $front.Bitmap.Dispose()
    }
    $g.Dispose()
    $dir = Split-Path $out -Parent
    if ($dir) { New-Item -ItemType Directory -Force $dir | Out-Null }
    $main.Bitmap.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $main.Bitmap.Dispose()
    Write-Output "saved $out"
}

try {
    foreach ($step in $Steps) {
        $kind, $rest = $step -split ':', 2
        switch ($kind) {
            'invoke' { (Find $rest ($AE::IsInvokePatternAvailableProperty)).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
            'select' { (Find $rest ($AE::IsSelectionItemPatternAvailableProperty)).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
            'type' {
                $name, $text = $rest -split '=', 2
                $e = Find $name ($AE::IsValuePatternAvailableProperty)
                try { $e.SetFocus() } catch { }   # a combo box's edit part takes the value without focus
                $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
                if (-not $NoKeys) { Keys "{TAB}" }
            }
            'toggle' { (Find $rest ($AE::IsTogglePatternAvailableProperty)).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle() }
            'expand' {
                $e = Find $rest ($AE::IsExpandCollapsePatternAvailableProperty)
                try { $e.SetFocus() } catch { }   # a menu or list opened in a window that is not active closes at once
                $e.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            }
            'pick' {
                # choose an item of a drop-down list without opening it (its items are in the raw view only)
                $name, $item = $rest -split '=', 2
                $box = Find $name
                # the items are made the first time the list is opened (it may close again at once)
                try { $box.SetFocus() } catch { }
                $ec = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
                $ec.Expand(); Start-Sleep -Milliseconds 800
                $tw = [System.Windows.Automation.TreeWalker]::RawViewWalker
                $c = $tw.GetFirstChild($box)
                while ($c -and $c.Current.Name -ne $item) { $c = $tw.GetNextSibling($c) }
                if (-not $c) { throw "no item '$item' in '$name'" }
                $c.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
                try { $ec.Collapse() } catch { }
            }
            'menu' {
                # press a menu's item: the menu is opened, then its item found in the raw view and invoked
                $name, $item = $rest -split '=', 2
                $m = Find $name ($AE::IsExpandCollapsePatternAvailableProperty)
                try { $m.SetFocus() } catch { }
                $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep -Milliseconds 800
                $tw = [System.Windows.Automation.TreeWalker]::RawViewWalker
                $c = $tw.GetFirstChild($m)
                while ($c -and $c.Current.Name -ne $item) { $c = $tw.GetNextSibling($c) }
                if (-not $c) { throw "no item '$item' in menu '$name'" }
                $c.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            }
            'scroll' {
                # scroll the scrollable area that holds the named element to a percentage (0-100) down
                $name, $pct = $rest -split '=', 2
                $e = Find $name
                $tw = [System.Windows.Automation.TreeWalker]::RawViewWalker
                while ($e -and -not $e.GetCurrentPropertyValue($AE::IsScrollPatternAvailableProperty)) { $e = $tw.GetParent($e) }
                if (-not $e) { throw "nothing scrollable around '$name'" }
                $e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(-1, [double]$pct)
            }
            'collapse' { (Find $rest ($AE::IsExpandCollapsePatternAvailableProperty)).GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse() }
            'close' { (Find $rest ($AE::IsWindowPatternAvailableProperty)).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
            'keys' { Keys $rest }
            'size' {
                $w, $hh = $rest -split 'x', 2
                $tp = $AE::FromHandle($h).GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
                $tp.Move(0, 0); $tp.Resize([double]$w, [double]$hh); Start-Sleep -Milliseconds 800
            }
            'wait' { Start-Sleep -Milliseconds ([int]$rest) }
            'shot' { Shot $rest }
        }
        Start-Sleep -Milliseconds 300
    }
}
catch { Write-Output "STEP FAILED: $step - $_" }
finally { if (-not $proc.HasExited) { $proc.Kill() } }
