#Requires -Version 5.1
<#
.SYNOPSIS
    Shows which firmware readings changed between two probe reports.

.DESCRIPTION
    Matches calls by method and input and lists those whose output differs. Sensor
    readings (temperatures, fan RPM) change on their own, so they are skipped unless
    -IncludeSensors is given.

.EXAMPLE
    powershell -File scripts\compare-probes.ps1 reports\probe-...-baseline.json reports\probe-...-kb-red.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Before,
    [Parameter(Mandatory)][string]$After,
    [switch]$IncludeSensors
)

$ErrorActionPreference = 'Stop'

function Get-CallIndex([string]$Path) {
    $report = Get-Content -Raw -Path $Path | ConvertFrom-Json
    $index = [ordered]@{}
    foreach ($c in $report.calls) { $index["$($c.method) $($c.input)"] = $c }
    $index
}

function Get-OutputText($Call) {
    if ($null -eq $Call) { return '(missing)' }
    if (-not $Call.ok) { return "error: $($Call.error)" }
    ($Call.output.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '  '
}

$a = Get-CallIndex $Before
$b = Get-CallIndex $After

$changes = foreach ($key in (@($a.Keys) + @($b.Keys) | Select-Object -Unique)) {
    $x = $a[$key]; $y = $b[$key]
    $note = if ($x) { $x.note } else { $y.note }
    if ($note -eq 'sensor reading' -and -not $IncludeSensors) { continue }
    $old = Get-OutputText $x
    $new = Get-OutputText $y
    if ($old -ne $new) { [pscustomobject]@{ Call = $key; Before = $old; After = $new } }
}

if ($changes) { $changes | Format-Table -AutoSize -Wrap }
else { Write-Host 'No firmware readings changed.' }
