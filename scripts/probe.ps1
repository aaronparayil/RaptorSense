#Requires -Version 5.1
<#
.SYNOPSIS
    Read-only hardware probe for Acer Predator laptops (RaptorSense, phase 0).

.DESCRIPTION
    Calls only the Get* methods of the AcerGamingFunction WMI class and saves the
    raw results to a JSON report, together with best-guess decodings taken from the
    Linux acer-wmi driver. It never calls a Set* method: Invoke-AcerGet refuses to.

    It also records the Acer WMI interfaces (GUIDs, method IDs) and the HID devices,
    flagging vendor-defined HID collections that may carry keyboard RGB traffic.

    Workflow: run it once as a baseline, change ONE setting in PredatorSense, run it
    again with a -Label, then diff the two reports with compare-probes.ps1.

.PARAMETER Label
    Short tag for the report file name, e.g. "fans-max".

.PARAMETER OutDir
    Where reports are written. Defaults to <repo>\reports.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\probe.ps1 -Label baseline
#>
[CmdletBinding()]
param(
    [string]$Label = '',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is empty in param defaults on Windows PowerShell 5.1, so resolve it here.
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\reports' }
$ProbeVersion = '0.1.0'

# --- Best-known field layouts, from Linux drivers/platform/x86/acer-wmi.c. Unverified on PH16-71. ---
# Every output is a status byte in bits 0-7 (0 = success) followed by the value.

$SensorNames = @{
    0x01 = 'CPU temperature (C)'
    0x02 = 'CPU fan (RPM)'
    0x03 = 'External temperature 2 (C)'
    0x06 = 'GPU fan (RPM)'
    0x0A = 'GPU temperature (C)'
}
$ProfileNames = @{ 0x00 = 'Quiet'; 0x01 = 'Balanced'; 0x04 = 'Performance'; 0x05 = 'Turbo'; 0x06 = 'Eco' }
$FanModeNames = @{ 0 = 'unknown (0)'; 1 = 'Auto'; 2 = 'Max'; 3 = 'Custom' }
$MiscSettingNames = @{ 0x05 = 'OC 1'; 0x07 = 'OC 2'; 0x0A = 'Supported profiles (bitmap)'; 0x0B = 'Platform profile' }

function Get-Bits([uint64]$Value, [int]$Shift, [uint64]$Mask) {
    ($Value -shr $Shift) -band $Mask
}

function Format-Value($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [array]) { return (($Value | ForEach-Object { '{0:X2}' -f $_ }) -join ' ') }
    if ($Value -is [ValueType]) { return ('0x{0:X}' -f $Value) }
    "$Value"
}

function Get-Decoded([string]$Method, [uint64]$In, $Out) {
    if ($null -eq $Out -or $Out -is [array]) { return $null }
    $o = [uint64]$Out
    $status = Get-Bits $o 0 0xFF
    switch ($Method) {
        'GetGamingSysInfo' {
            $command = Get-Bits $In 0 0xFF
            if ($command -eq 0) {
                return [ordered]@{ status = $status; supportedSensors = ('0x{0:X4}' -f (Get-Bits $o 24 0xFFFF)) }
            }
            if ($command -eq 1) {
                $id = [int](Get-Bits $In 8 0xFF)
                $name = if ($SensorNames.ContainsKey($id)) { $SensorNames[$id] } else { 'sensor 0x{0:X2}' -f $id }
                return [ordered]@{ status = $status; sensor = $name; value = (Get-Bits $o 8 0xFFFF) }
            }
            return [ordered]@{ status = $status }
        }
        'GetGamingMiscSetting' {
            $index = [int]$In
            $value = [int](Get-Bits $o 8 0xFF)
            $decoded = [ordered]@{ status = $status; value = $value }
            if ($MiscSettingNames.ContainsKey($index)) { $decoded.setting = $MiscSettingNames[$index] }
            if ($index -eq 0x0B -and $ProfileNames.ContainsKey($value)) { $decoded.profile = $ProfileNames[$value] }
            return $decoded
        }
        'GetGamingFanBehavior' {
            return [ordered]@{
                status  = $status
                cpuMode = $FanModeNames[[int](Get-Bits $o 8 0x3)]
                gpuMode = $FanModeNames[[int](Get-Bits $o 14 0x3)]
            }
        }
        'GetGamingFanSpeed' {
            return [ordered]@{ status = $status; percent = (Get-Bits $o 8 0xFF) }
        }
    }
    $null
}

# --- Firmware access ---

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error 'Windows only lets administrators use the Acer gaming WMI interface. Rerun from an elevated PowerShell (Run as administrator).'
    exit 1
}

try {
    $gamingClass = Get-CimClass -Namespace root\wmi -ClassName AcerGamingFunction
    $gaming = Get-CimInstance -Namespace root\wmi -ClassName AcerGamingFunction | Select-Object -First 1
} catch {
    throw "AcerGamingFunction WMI class not available ($($_.Exception.Message)). This laptop may not be supported."
}

$methodDefs = @{}
foreach ($m in $gamingClass.CimClassMethods) { $methodDefs[$m.Name] = $m }

function Invoke-AcerGet([string]$Method, $InputValue, [string]$Note) {
    if ($Method -notlike 'Get*') { throw "Refusing to call ${Method}: the probe only calls Get* methods." }

    $entry = [ordered]@{ method = $Method; input = $null; note = $Note; ok = $false }
    $def = $methodDefs[$Method]
    if (-not $def) { $entry.error = 'method not present on this firmware'; return [pscustomobject]$entry }
    if (-not $gaming) { $entry.error = 'no AcerGamingFunction instance (access denied?)'; return [pscustomobject]$entry }

    $arguments = @{}
    $inParam = @($def.Parameters | Where-Object { $_.Qualifiers.Name -contains 'In' }) | Select-Object -First 1
    if ($inParam) {
        $typed = switch ($inParam.CimType) {
            'UInt64' { [uint64]$InputValue }
            'UInt32' { [uint32]$InputValue }
            'UInt16' { [uint16]$InputValue }
            'UInt8'  { [byte]$InputValue }
            default  { $InputValue }
        }
        $arguments[$inParam.Name] = $typed
        $entry.input = '0x{0:X}' -f $typed
    }

    try {
        $result = Invoke-CimMethod -InputObject $gaming -MethodName $Method -Arguments $arguments
        $entry.ok = $true
        $outputs = [ordered]@{}
        foreach ($p in ($def.Parameters | Where-Object { $_.Qualifiers.Name -contains 'Out' })) {
            $outputs[$p.Name] = Format-Value $result.($p.Name)
        }
        $entry.output = $outputs
        $decoded = Get-Decoded $Method $InputValue $result.gmOutput
        if ($decoded) { $entry.decoded = $decoded }
    } catch {
        $entry.error = $_.Exception.Message
    }
    [pscustomobject]$entry
}

# --- What to read. Inputs outside the known ones are small scans; getters answer unknown ones with an error status. ---

$plan = New-Object System.Collections.Generic.List[object]
function Add-Probe([string]$Method, $InputValue, [string]$Note) { $plan.Add(@($Method, $InputValue, $Note)) }

Add-Probe 'GetGamingSysInfo' 0x0000 'supported sensors'
foreach ($id in 0x01..0x10) { Add-Probe 'GetGamingSysInfo' (0x01 -bor ($id -shl 8)) 'sensor reading' }
Add-Probe 'GetGamingSysInfo' 0x0002 'battery status'
foreach ($i in 0x00..0x1F) { Add-Probe 'GetGamingMiscSetting' $i 'misc setting index' }
Add-Probe 'GetGamingFanBehavior' 0x1 'CPU fan'
Add-Probe 'GetGamingFanBehavior' 0x8 'GPU fan'
Add-Probe 'GetGamingFanBehavior' 0x9 'CPU + GPU fans'
Add-Probe 'GetGamingFanSpeed' 0x1 'CPU fan'
Add-Probe 'GetGamingFanSpeed' 0x8 'GPU fan'
Add-Probe 'GetGamingFanTable' $null 'fan table'
foreach ($i in 0..3) { Add-Probe 'GetGamingProfile' $i 'scan' }
foreach ($i in 0x0..0xF) { Add-Probe 'GetGamingProfileSetting' $i 'scan' }
foreach ($zone in 0x0, 0x1, 0x2, 0x4, 0x8, 0xF) { Add-Probe 'GetGamingRgbKb' $zone 'zone mask?' }
foreach ($i in 0..3) { Add-Probe 'GetGamingKBBacklight' $i 'scan' }
foreach ($method in 'GetGamingLED', 'GetGamingLEDBehavior', 'GetGamingLEDColor') {
    foreach ($i in 0..3) { Add-Probe $method $i 'scan' }
}

$calls = foreach ($p in $plan) { Invoke-AcerGet $p[0] $p[1] $p[2] }

# --- Context: machine, WMI interfaces, HID devices ---

$cs = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$os = Get-CimInstance Win32_OperatingSystem
$predatorSense = Get-AppxPackage *PredatorSense* | Select-Object -First 1

$environment = [ordered]@{
    manufacturer  = $cs.Manufacturer
    model         = $cs.Model
    family        = $cs.SystemFamily
    bios          = $bios.SMBIOSBIOSVersion
    biosDate      = $bios.ReleaseDate.ToString('yyyy-MM-dd')
    os            = "$($os.Caption) $($os.BuildNumber)"
    predatorSense = $(if ($predatorSense) { "$($predatorSense.Version)" } else { $null })
    elevated      = $isAdmin
}

$interfaces = foreach ($c in (Get-CimClass -Namespace root\wmi | Where-Object { $_.CimClassName -like 'Acer*' })) {
    [ordered]@{
        class   = $c.CimClassName
        guid    = ($c.CimClassQualifiers | Where-Object Name -eq 'guid').Value
        methods = @(foreach ($m in $c.CimClassMethods) {
            $methodId = $m.Qualifiers | Where-Object Name -eq 'WmiMethodId'
            [ordered]@{
                name   = $m.Name
                id     = $(if ($methodId) { $methodId.Value } else { $null })
                params = @($m.Parameters | ForEach-Object {
                    $direction = if ($_.Qualifiers.Name -contains 'In') { 'in' } else { 'out' }
                    "$direction $($_.CimType) $($_.Name)"
                })
            }
        })
    }
}

# Vendor-defined usage pages (HID_DEVICE_UP:FFxx) are where custom RGB protocols usually live.
$hidDevices = foreach ($d in (Get-PnpDevice -PresentOnly | Where-Object { $_.InstanceId -match '^HID\\VID_' })) {
    [ordered]@{
        instanceId    = $d.InstanceId
        name          = $d.FriendlyName
        class         = $d.Class
        vendorDefined = [bool](@($d.HardwareID) -match 'HID_DEVICE_UP:FF')
        hardwareIds   = @($d.HardwareID)
    }
}

# --- Report ---

$timestamp = Get-Date
$report = [ordered]@{
    tool        = "RaptorSense probe $ProbeVersion"
    generatedAt = $timestamp.ToString('o')
    label       = $Label
    notes       = 'Read-only. Decodings come from the Linux acer-wmi driver and are hypotheses; raw outputs are authoritative. status 0 = success.'
    environment = $environment
    interfaces  = @($interfaces)
    hidDevices  = @($hidDevices)
    calls       = @($calls)
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$name = 'probe-' + $timestamp.ToString('yyyyMMdd-HHmmss')
if ($Label) { $name += '-' + ($Label -replace '[^\w-]', '_') }
$path = Join-Path (Resolve-Path $OutDir) "$name.json"
$report | ConvertTo-Json -Depth 8 | Out-File -Encoding utf8 -FilePath $path

# --- Console summary ---

$okCalls = @($calls | Where-Object ok)
Write-Host ''
Write-Host "$($environment.model), BIOS $($environment.bios)  |  $($okCalls.Count)/$(@($calls).Count) firmware calls answered"

$highlights = foreach ($c in $okCalls) {
    if (-not $c.decoded -or $c.decoded.status -ne 0) { continue }
    $d = $c.decoded
    $what = if ($d.Contains('sensor')) { $d.sensor }
            elseif ($d.Contains('setting')) { $d.setting }
            else { "$($c.method) ($($c.note))" }
    $value = if ($d.Contains('profile')) { "$($d.value) = $($d.profile)" }
             elseif ($d.Contains('cpuMode')) { "CPU $($d.cpuMode), GPU $($d.gpuMode)" }
             elseif ($d.Contains('percent')) { "$($d.percent)%" }
             elseif ($d.Contains('supportedSensors')) { $d.supportedSensors }
             else { "$($d.value)" }
    [pscustomobject]@{ Reading = $what; Value = $value }
}
if ($highlights) { $highlights | Format-Table -AutoSize | Out-String | Write-Host }

$vendorHid = @($hidDevices | Where-Object { $_.vendorDefined })
if ($vendorHid.Count) {
    Write-Host 'Vendor-defined HID collections (RGB candidates):'
    $vendorHid | ForEach-Object { Write-Host "  $($_.instanceId)" }
}

$failed = @($calls | Where-Object { -not $_.ok })
if ($failed.Count) {
    Write-Host "$($failed.Count) calls failed; first error: $($failed[0].error)"
}
Write-Host "Report: $path"
