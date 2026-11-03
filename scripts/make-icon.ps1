#Requires -Version 5.1
<#
.SYNOPSIS
    Draws the RaptorSense icon and writes icon.png and raptorsense.ico.
.PARAMETER Style
    claw   three glowing claw marks (default)
    raptor a velociraptor head in profile
.PARAMETER OutDir
    Where to write the files. Defaults to src\RaptorSense.App\Assets.
.PARAMETER Preview
    Also writes a strip showing the icon at 256, 48, 32 and 16 px to this path, to check it reads at tray size.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1 -Style claw
#>
param(
    [ValidateSet('claw', 'raptor')][string]$Style = 'claw',
    [string]$OutDir = '',
    [string]$Preview = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\src\RaptorSense.App\Assets' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$teal = [System.Drawing.Color]::FromArgb(0, 174, 199)
$magenta = [System.Drawing.Color]::FromArgb(255, 0, 128)
$darkColor = [System.Drawing.Color]::FromArgb(255, 18, 20, 26)

function Pt([double]$x, [double]$y) { New-Object System.Drawing.PointF ([float]$x), ([float]$y) }

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddArc($x, $y, 2 * $r, 2 * $r, 180, 90)
    $p.AddArc($x + $w - 2 * $r, $y, 2 * $r, 2 * $r, 270, 90)
    $p.AddArc($x + $w - 2 * $r, $y + $h - 2 * $r, 2 * $r, 2 * $r, 0, 90)
    $p.AddArc($x, $y + $h - 2 * $r, 2 * $r, 2 * $r, 90, 90)
    $p.CloseFigure()
    $p
}

# Teal at the bottom left to magenta at the top right. The endpoints sit outside the tile, because past them
# a LinearGradientBrush repeats and leaves stray colour.
function New-Gradient([int]$alpha = 255) {
    New-Object System.Drawing.Drawing2D.LinearGradientBrush ((Pt 20 280), (Pt 260 0),
        [System.Drawing.Color]::FromArgb($alpha, $teal), [System.Drawing.Color]::FromArgb($alpha, $magenta))
}

# ---- Claw marks ----

# A slash tapering to points at both ends and bowed to one side, like a scratch from a curved claw.
function New-Slash([double[]]$start, [double[]]$end, [double]$halfWidth, [double]$bow) {
    $dx = $end[0] - $start[0]; $dy = $end[1] - $start[1]
    $len = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $nx = -$dy / $len; $ny = $dx / $len
    function At([double]$t, [double]$offset) { Pt ($start[0] + $dx * $t + $nx * $offset) ($start[1] + $dy * $t + $ny * $offset) }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddBezier((Pt $start[0] $start[1]), (At 0.3 ($bow + $halfWidth)), (At 0.7 ($bow + $halfWidth)), (Pt $end[0] $end[1]))
    $p.AddBezier((Pt $end[0] $end[1]), (At 0.7 ($bow - $halfWidth)), (At 0.3 ($bow - $halfWidth)), (Pt $start[0] $start[1]))
    $p.CloseFigure()
    $p
}

function Draw-ClawMarks($g) {
    $marks = @(
        (New-Slash @(124, 48) @(42, 196) 15 12),
        (New-Slash @(178, 32) @(92, 226) 17 13),
        (New-Slash @(228, 52) @(148, 204) 15 12))
    # Glow in widening, fading passes so it reads as light, not an outline
    foreach ($pass in @(@(26, 14), @(16, 24), @(8, 40))) {
        $glow = New-Object System.Drawing.Pen ((New-Gradient $pass[1])), $pass[0]
        $glow.LineJoin = 'Round'
        foreach ($m in $marks) { $g.DrawPath($glow, $m) }
    }
    $fill = New-Gradient
    foreach ($m in $marks) { $g.FillPath($fill, $m) }
    # A bright core along each mark makes it look freshly cut and keeps it crisp when small
    $core = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, 255, 255, 255))
    $cores = @(
        (New-Slash @(120, 62) @(50, 184) 3.5 11),
        (New-Slash @(174, 48) @(100, 212) 4 12),
        (New-Slash @(222, 66) @(154, 192) 3.5 11))
    foreach ($c in $cores) { $g.FillPath($core, $c) }
}

# ---- Velociraptor head ----

function Add-Teeth($path, [float[]]$from, [float[]]$to, [int]$count, [float]$length, [int]$side) {
    # Triangles along the edge from->to, pointing to one side of it ($side = 1 or -1).
    $dx = $to[0] - $from[0]; $dy = $to[1] - $from[1]
    $len = [Math]::Sqrt($dx * $dx + $dy * $dy)
    $nx = -$dy / $len * $side; $ny = $dx / $len * $side
    $half = $len / $count / 2.6
    for ($i = 0; $i -lt $count; $i++) {
        $t = ($i + 0.5) / $count
        $cx = $from[0] + $dx * $t; $cy = $from[1] + $dy * $t
        $ux = $dx / $len * $half; $uy = $dy / $len * $half
        $path.AddPolygon([System.Drawing.PointF[]]@((Pt ($cx - $ux) ($cy - $uy)), (Pt ($cx + $ux) ($cy + $uy)), (Pt ($cx + $nx * $length) ($cy + $ny * $length))))
    }
}

function Draw-Raptor($g) {
    # Long flat snout, open jaws, thin S-curved neck. Drawn in a 256 px design space, enlarged to fill the tile.
    $g.TranslateTransform(-31, -40)
    $g.ScaleTransform(1.12, 1.12)
    $head = New-Object System.Drawing.Drawing2D.GraphicsPath
    $head.StartFigure()
    $head.AddLine(70, 280, 70, 252)                          # neck runs off the bottom of the tile
    $head.AddBezier(70, 252, 62, 200, 66, 160, 88, 132)      # back of the neck
    $head.AddBezier(88, 132, 100, 112, 116, 98, 136, 90)     # back of the skull
    $head.AddBezier(136, 90, 160, 80, 186, 80, 204, 86)      # flat skull top
    $head.AddBezier(204, 86, 214, 90, 228, 96, 240, 106)     # long snout
    $head.AddLine(240, 106, 239, 115)                        # nose tip
    $head.AddLine(239, 115, 148, 123)                        # upper jaw edge, back to the mouth corner
    $head.AddLine(148, 123, 226, 138)                        # lower jaw edge, forward again (mouth open)
    $head.AddBezier(226, 138, 228, 144, 222, 150, 212, 150)  # chin
    $head.AddBezier(212, 150, 186, 152, 160, 154, 140, 162)  # under the jaw
    $head.AddBezier(140, 162, 118, 172, 108, 200, 116, 252)  # throat
    $head.AddLine(116, 252, 116, 280)
    $head.CloseFigure()
    $g.FillPath((New-Gradient), $head)

    $teeth = New-Object System.Drawing.Drawing2D.GraphicsPath
    Add-Teeth $teeth @(234, 115.5) @(160, 122) 6 10 1
    Add-Teeth $teeth @(162, 126) @(222, 137.5) 5 8 -1
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 240, 245))), $teeth)

    $dark = New-Object System.Drawing.SolidBrush $darkColor
    $g.FillEllipse($dark, 158, 91, 22, 13)                   # eye, under a heavy brow ridge
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 220, 0))), 166, 92, 5, 11)
    $g.FillPolygon($dark, [System.Drawing.PointF[]]@((Pt 150 90), (Pt 190 84), (Pt 186 91), (Pt 154 95)))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(150, 18, 20, 26))), 226, 101, 7, 4)
    $g.DrawBezier((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 18, 20, 26)), 3), 148, 123, 136, 128, 124, 128, 112, 124)
    $g.ResetTransform()
}

# ---- Render ----

$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)
$tile = New-RoundedRect 8 8 240 240 48
$g.FillPath((New-Object System.Drawing.SolidBrush $darkColor), $tile)
$g.SetClip($tile)
if ($Style -eq 'claw') { Draw-ClawMarks $g } else { Draw-Raptor $g }
$g.ResetClip()
$g.Dispose()

$png = Join-Path $OutDir 'icon.png'
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

if ($Preview) {
    $strip = New-Object System.Drawing.Bitmap 400, 270
    $sg = [System.Drawing.Graphics]::FromImage($strip)
    $sg.Clear([System.Drawing.Color]::FromArgb(40, 40, 40))
    $sg.InterpolationMode = 'HighQualityBicubic'
    $sg.DrawImage($bmp, 5, 5, 256, 256)
    $x = 275
    foreach ($s in 48, 32, 16) { $sg.DrawImage($bmp, $x, 20, $s, $s); $x += $s + 10 }
    $sg.Dispose(); $strip.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png); $strip.Dispose()
}
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
[IO.File]::WriteAllBytes((Join-Path $OutDir 'raptorsense.ico'), $ms.ToArray())
"Wrote $Style icon to $OutDir"
