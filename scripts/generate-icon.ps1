# Generates an original Optim app icon (teal gradient tile + white "O" gauge
# mark with accent dot) as a multi-size PNG-compressed .ico. No external
# assets used. Each size is drawn from a 4x supersampled master for crisp
# edges at 16px title-bar up to 256px.
param(
    [string]$Out = "$PSScriptRoot\..\src\Optim.App\Assets\AppIcon.ico"
)

Add-Type -AssemblyName System.Drawing

function New-Master([int]$px) {
    $bmp = New-Object System.Drawing.Bitmap($px, $px)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Rounded-square gradient background in Optim's brand teal (#1B9AAA).
    $rect = New-Object System.Drawing.Rectangle(0, 0, $px, $px)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 27, 154, 170),
        [System.Drawing.Color]::FromArgb(255, 9, 66, 84),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $radius = [int]($px * 0.24)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($px - $d, 0, $d, $d, 270, 90)
    $path.AddArc($px - $d, $px - $d, $d, $d, 0, 90)
    $path.AddArc(0, $px - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)

    $penW = [Math]::Max(4, [int]($px * 0.075))
    $pad = [int]($px * 0.24)

    # Dim track ring behind the gauge sweep, so the mark reads as a dial.
    $trackPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), $penW)
    $trackPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $trackPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($trackPen, $pad, $pad, $px - 2 * $pad, $px - 2 * $pad, 0, 360)

    # White gauge sweep with a gap at top-right (tuning-dial motif).
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, $pad, $pad, $px - 2 * $pad, $px - 2 * $pad, 60, 300)

    # Accent dot in the gap.
    $dotR = [int]($px * 0.055)
    $cx = [int]($px * 0.5 + ($px * 0.26) * [Math]::Cos(-0.5))
    $cy = [int]($px * 0.5 + ($px * 0.26) * [Math]::Sin(-0.5))
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillEllipse($dotBrush, $cx - $dotR, $cy - $dotR, $dotR * 2, $dotR * 2)

    $g.Dispose(); $brush.Dispose(); $trackPen.Dispose(); $pen.Dispose(); $dotBrush.Dispose(); $path.Dispose()
    return $bmp
}

function Downscale([System.Drawing.Bitmap]$master, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($master, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $pstream = New-Object System.IO.MemoryStream
    $bmp.Save($pstream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $pstream.ToArray()
    $pstream.Dispose()
    Write-Output -NoEnumerate $bytes
}

# Full Windows size ladder so title bar (16/20), taskbar (32/40),
# About (48) and Explorer (256) each get an exact entry.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 256)
$master = New-Master 1024
$entries = @()
foreach ($s in $sizes) {
    $bmp = Downscale $master $s
    [byte[]]$png = Get-PngBytes $bmp
    $entries += @{ Size = $s; Png = $png }
    $bmp.Dispose()
}
$master.Dispose()

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $w = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$w); $bw.Write([byte]$w)
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Png.Length); $bw.Write([uint32]$offset)
    $offset += $e.Png.Length
}
foreach ($e in $entries) { $bw.Write($e.Png) }
$bw.Dispose(); $fs.Dispose()

$bytes = (Get-Item -LiteralPath $Out).Length
Write-Output "Wrote $Out ($bytes bytes, $($entries.Count) sizes)"
