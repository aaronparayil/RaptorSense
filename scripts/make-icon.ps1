#Requires -Version 5.1
<#
.SYNOPSIS
    Draws the RaptorSense icon (a grid of lit keycaps) and writes Assets\icon.png and Assets\raptorsense.ico.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\RaptorSense.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    $p
}

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

# Dark rounded tile
$tile = New-RoundedRect 8 8 240 240 48
$g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 20, 26))), $tile)

# 3 x 3 keycaps fading from teal (top left) to magenta (bottom right)
$teal = [System.Drawing.Color]::FromArgb(0, 174, 199)
$magenta = [System.Drawing.Color]::FromArgb(255, 0, 128)
$cap = 56; $gap = 14; $origin = (256 - (3 * $cap + 2 * $gap)) / 2
for ($row = 0; $row -lt 3; $row++) {
    for ($col = 0; $col -lt 3; $col++) {
        $t = ($row + $col) / 4
        $c = [System.Drawing.Color]::FromArgb(
            [int]($teal.R + ($magenta.R - $teal.R) * $t),
            [int]($teal.G + ($magenta.G - $teal.G) * $t),
            [int]($teal.B + ($magenta.B - $teal.B) * $t))
        $key = New-RoundedRect ($origin + $col * ($cap + $gap)) ($origin + $row * ($cap + $gap)) $cap $cap 12
        $g.FillPath((New-Object System.Drawing.SolidBrush $c), $key)
    }
}
$g.Dispose()

$png = Join-Path $assets 'icon.png'
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ICO containing the 256 px PNG (supported since Windows Vista)
$data = [IO.File]::ReadAllBytes($png)
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]1)            # header: reserved, type icon, 1 image
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)  # 256 x 256 (0 = 256), no palette
$w.Write([uint16]1); $w.Write([uint16]32)                                  # planes, bits per pixel
$w.Write([uint32]$data.Length); $w.Write([uint32]22)                       # image size, offset
$w.Write($data)
[IO.File]::WriteAllBytes((Join-Path $assets 'raptorsense.ico'), $ms.ToArray())
"Wrote $png and raptorsense.ico"
