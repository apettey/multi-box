<#
.SYNOPSIS
    Screenshots a MultiBox window to a PNG, for before/after images in changelog/.

.DESCRIPTION
    Finds a visible top-level window owned by the MultiBox process (optionally by title),
    optionally resizes it to a fixed size so before and after line up, brings it to the
    front and copies its on-screen pixels. Capturing from the screen rather than the
    window's own surface keeps the DWM client thumbnails in the cards.

    Dev tooling only: it never ships in the app and touches only MultiBox's own windows,
    never an EVE client. Windows only.

.EXAMPLE
    .\scripts\capture-window.ps1 -Out changelog\2026-10-18-foo\before.png
    .\scripts\capture-window.ps1 -Title 'SYNC LAYOUT' -Out changelog\2026-10-18-foo\after-sync.png
    .\scripts\capture-window.ps1 -Size 1600x1000 -Out after.png
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Out,
    # Substring of the window title; default is the process's main window.
    [string]$Title = '',
    # WIDTHxHEIGHT in pixels; resize the window first so before/after match.
    [string]$Size = '',
    [int]$SettleMs = 600
)

$ErrorActionPreference = 'Stop'
# Fixed, not a parameter: this must never point at an EVE client (AGENTS.md section 4).
$ProcessName = 'MultiBox'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'capture-window.ps1 needs Windows.' }

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class MbCapture {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    public static List<KeyValuePair<IntPtr, string>> Windows(uint[] pids) {
        var found = new List<KeyValuePair<IntPtr, string>>();
        var set = new HashSet<uint>(pids);
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (set.Contains(pid) && IsWindowVisible(h)) {
                var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity);
                if (sb.Length > 0) found.Add(new KeyValuePair<IntPtr, string>(h, sb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[void][MbCapture]::SetProcessDPIAware()
$procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) { throw "No running '$ProcessName' process. Start it first (dotnet run --project src/MultiBox.App)." }

$windows = [MbCapture]::Windows([uint32[]]@($procs.Id))
if ($Title) {
    $match = @($windows | Where-Object { $_.Value -like "*$Title*" })
} else {
    $mains = @($procs | ForEach-Object { $_.MainWindowHandle } | Where-Object { $_ -ne [IntPtr]::Zero })
    $match = @($windows | Where-Object { $mains -contains $_.Key })
}
if ($match.Count -eq 0) {
    $list = ($windows | ForEach-Object { "'$($_.Value)'" }) -join ', '
    throw "No matching window. Visible $ProcessName windows: $list"
}
$hwnd = $match[0].Key

[void][MbCapture]::ShowWindow($hwnd, 9) # SW_RESTORE
if ($Size) {
    if ($Size -notmatch '^(\d+)x(\d+)$') { throw "Size must be WIDTHxHEIGHT, e.g. 1600x1000." }
    # SWP_NOMOVE | SWP_NOZORDER
    [void][MbCapture]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, [int]$Matches[1], [int]$Matches[2], 0x0002 -bor 0x0004)
}
[void][MbCapture]::SetForegroundWindow($hwnd)
Start-Sleep -Milliseconds $SettleMs

# Extended frame bounds exclude the invisible resize border that GetWindowRect includes.
$r = New-Object MbCapture+RECT
$hr = [MbCapture]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, [Runtime.InteropServices.Marshal]::SizeOf($r))
if ($hr -ne 0) { throw "DwmGetWindowAttribute failed (0x{0:X})." -f $hr }
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
try {
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size $w, $h))
    $dir = Split-Path -Parent ([IO.Path]::GetFullPath($Out))
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $bmp.Save([IO.Path]::GetFullPath($Out), [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $g.Dispose(); $bmp.Dispose() }

Write-Host "Saved $Out (${w}x${h}) from '$($match[0].Value)'."
