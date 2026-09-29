# Launches Optim on a specific page (--page <tag>) and captures the window to
# a PNG (local tooling; output lands in gitignored docs/screenshots/). UAC
# prompt is expected: the app requires elevation. Captures only the app
# window, not the whole desktop.
param(
    [Parameter(Mandatory)][string]$Page,
    [Parameter(Mandatory)][string]$Out,
    # Generous default: the Optimize page batches card creation after load.
    [int]$SettleSeconds = 14
)

$ErrorActionPreference = "Stop"
$exe = "$PSScriptRoot\..\src\Optim.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\Optim.App.exe"

Start-Process $exe -ArgumentList "--page", $Page -Verb RunAs
Start-Sleep -Seconds $SettleSeconds

Add-Type -AssemblyName System.Drawing

# Find the main-window rectangle of the freshly started Optim process.
$proc = Get-Process Optim.App -ErrorAction Stop | Select-Object -First 1
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32Rect {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
$null = [Win32Rect]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 600

$rect = New-Object Win32Rect+RECT
if (-not [Win32Rect]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)) {
    throw "Could not read the Optim window rectangle."
}

$w = $rect.Right - $rect.Left
$h = $rect.Bottom - $rect.Top
if ($w -le 0 -or $h -le 0) { throw "Optim window has no size." }

$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$g.Dispose()

$dir = Split-Path -Parent $Out
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "Wrote $Out (${w}x${h})"
