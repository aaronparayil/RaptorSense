#Requires -Version 5.1
<#
.SYNOPSIS
    First per-key RGB test for the PH16-71 keyboard (Chicony 04F2:0117).

.DESCRIPTION
    Modes:
      info   Find the lighting interface and read the brightness. Changes nothing.
      keys   Light WASD red, the arrows teal and Space white; every other key off.
      sweep  Run a white column across the keyboard and time each frame upload.
      wave   Go back to the keyboard's built-in rainbow wave effect.
      pacing Fade the whole keyboard through the rainbow at 16, 4, 1 and 0 ms pacing (4 s each,
             1 s dark between). A region that lags or differs in colour means packets were lost.

    The cell numbers (cell = column * 6 + row) come from the verified PH16-71 key map in
    TrueSense (https://github.com/Order52/truesense, MIT licence).

.PARAMETER PacingMs
    Pause between the 8 packets of a frame. Acer's software uses about 16 ms; lower values
    are faster but may drop the end of a frame.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\rgb-test.ps1 -Mode keys
#>
[CmdletBinding()]
param(
    [ValidateSet('info', 'keys', 'sweep', 'wave', 'pacing')][string]$Mode = 'info',
    [ValidateRange(0, 50)][int]$Brightness = -1,
    [ValidateRange(0, 100)][int]$PacingMs = 16
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $scriptDir 'lib\RaptorHid.cs'))

$Cells = @{ A = 20; W = 21; Space = 24; S = 26; D = 32; Left = 84; Up = 90; Down = 96; Right = 102 }

function New-Frame { New-Object byte[] (128 * 3) }

function Set-Cell([byte[]]$Frame, [int]$Cell, [byte]$R, [byte]$G, [byte]$B) {
    $Frame[$Cell * 3] = $R; $Frame[$Cell * 3 + 1] = $G; $Frame[$Cell * 3 + 2] = $B
}

# Hue 0-360 at full saturation and value.
function Get-HueRgb([double]$Hue) {
    $h = ($Hue % 360) / 60
    $x = [byte](255 * (1 - [Math]::Abs($h % 2 - 1)))
    switch ([int][Math]::Floor($h)) {
        0 { 255, $x, 0 } 1 { $x, 255, 0 } 2 { 0, 255, $x }
        3 { 0, $x, 255 } 4 { $x, 0, 255 } default { 255, 0, $x }
    }
}

[RaptorSense.KeyboardHid]::SetPreciseTimer($true)
$kb = [RaptorSense.KeyboardHid]::Open()
try {
    Write-Host ("Keyboard 04F2:{0:X4}  feature {1} B, output {2} B, Acer mutex: {3}" -f $kb.Pid, $kb.FeatureLength, $kb.OutputLength, $kb.HoldsAcerMutex)
    $current = $kb.GetBrightness()
    Write-Host "Brightness reported by the keyboard: $current (0-50)"
    if ($Brightness -lt 0) { $Brightness = $(if ($current -gt 0) { $current } else { 25 }) }

    switch ($Mode) {
        'keys' {
            $frame = New-Frame
            foreach ($k in 'W', 'A', 'S', 'D') { Set-Cell $frame $Cells[$k] 255 0 0 }
            foreach ($k in 'Up', 'Down', 'Left', 'Right') { Set-Cell $frame $Cells[$k] 0 174 199 }
            Set-Cell $frame $Cells['Space'] 255 255 255
            $t = $kb.SendFrame($frame, $Brightness, $PacingMs)
            Write-Host ("Sent one frame in {0:N0} ms: WASD red, arrows teal, Space white." -f $t.TotalMilliseconds)
        }
        'sweep' {
            $times = foreach ($col in 0..21) {
                $frame = New-Frame
                foreach ($row in 0..5) {
                    $cell = $col * 6 + $row
                    if ($cell -lt 128) { Set-Cell $frame $cell 255 255 255 }
                }
                $kb.SendFrame($frame, $Brightness, $PacingMs).TotalMilliseconds
            }
            $avg = ($times | Measure-Object -Average).Average
            Write-Host ("{0} frames, average {1:N0} ms per frame = {2:N1} fps (pacing {3} ms)" -f @($times).Count, $avg, (1000 / $avg), $PacingMs)
        }
        'pacing' {
            $stage = 0
            foreach ($pacing in 16, 4, 1, 0) {
                $stage++
                $watch = [Diagnostics.Stopwatch]::StartNew()
                $frames = 0
                while ($watch.Elapsed.TotalSeconds -lt 4) {
                    $r, $g, $b = Get-HueRgb ($watch.Elapsed.TotalSeconds * 90)
                    $frame = New-Frame
                    for ($c = 0; $c -lt 128; $c++) { Set-Cell $frame $c $r $g $b }
                    [void]$kb.SendFrame($frame, $Brightness, $pacing)
                    $frames++
                }
                Write-Host ("Stage {0}: pacing {1,2} ms -> {2:N1} fps" -f $stage, $pacing, ($frames / $watch.Elapsed.TotalSeconds))
                [void]$kb.SendFrame((New-Frame), $Brightness, 16)
                Start-Sleep -Seconds 1
            }
            $kb.SetEffect(3, 0, 0, 0, 7, [byte]$Brightness, 0)
            Write-Host 'Built-in rainbow wave restored.'
        }
        'wave' {
            # Effect 3 = wave; speed byte 7 = medium.
            $kb.SetEffect(3, 0, 0, 0, 7, [byte]$Brightness, 0)
            Write-Host 'Built-in rainbow wave restored.'
        }
    }
} finally {
    $kb.Dispose()
    [RaptorSense.KeyboardHid]::SetPreciseTimer($false)
}
