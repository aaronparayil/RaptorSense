<#
Builds the Windows download: dist\RaptorSense-<version>-win-x64.zip.
Everything is self-contained, so people don't need to install .NET.

  powershell -ExecutionPolicy Bypass -File scripts\package.ps1

The zip bundles other people's code, so it also carries their licences: licenses\NOTICES.txt is generated from the
metadata of the NuGet packages actually used, and any licence files they ship are copied beside it
(scripts\lib\licenses.ps1).
#>
[CmdletBinding()]
param([string]$Runtime = 'win-x64')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = @($props.Project.PropertyGroup.Version)[0]
$name = "RaptorSense-$version-$Runtime"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $stage | Out-Null

function Publish-Project([string]$project, [string]$output) {
    Write-Host "Publishing $project..."
    & dotnet publish "src\$project\$project.csproj" -c Release -r $Runtime --self-contained true -v q --nologo `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

# Separate folders: "raptorsense.exe" and "RaptorSense.exe" are the same name on Windows, and the helper's install
# command copies its whole folder to Program Files.
Publish-Project 'RaptorSense.App' $stage
Publish-Project 'RaptorSense.Cli' (Join-Path $stage 'cli')
Publish-Project 'RaptorSense.Helper' (Join-Path $stage 'helper')
Get-ChildItem $stage -Recurse -Filter '*.pdb' | Remove-Item   # native debug symbols: 100 MB nobody needs

Copy-Item LICENSE, README.md, THIRD_PARTY_NOTICES.md $stage

# Double-click helpers for the fan service. They ask for administrator rights themselves.
function Write-ServiceScript([string]$fileName, [string]$command) {
    $lines = @(
        '@echo off',
        'net session >nul 2>&1',
        'if errorlevel 1 (',
        "    powershell -NoProfile -Command `"Start-Process -Verb RunAs -FilePath '%~f0'`"",
        '    exit /b',
        ')',
        "`"%~dp0helper\RaptorSense.Helper.exe`" $command",
        'echo.',
        'pause'
    )
    [IO.File]::WriteAllLines((Join-Path $stage $fileName), $lines, (New-Object Text.ASCIIEncoding))
}
Write-ServiceScript 'Install fan control.cmd' 'install'
Write-ServiceScript 'Remove fan control.cmd' 'uninstall'

# Licences of everything bundled.
& (Join-Path $PSScriptRoot 'lib\licenses.ps1') -Stage $stage -Runtime $Runtime -Version $version `
    -Projects 'RaptorSense.App', 'RaptorSense.Cli', 'RaptorSense.Helper', 'RaptorSense.Core'

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ''
Write-Host ('{0} ({1:N0} MB)' -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "SHA-256: $hash"
