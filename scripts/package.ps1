<#
Builds the Windows download: dist\RaptorSense-<version>-win-x64.zip.
Everything is self-contained, so people don't need to install .NET.

  powershell -ExecutionPolicy Bypass -File scripts\package.ps1

The zip bundles other people's code, so it also carries their licences: licenses\NOTICES.txt is generated from the
metadata of the NuGet packages actually used, and any licence files they ship are copied beside it.
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
$licenses = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Path $licenses | Out-Null
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

$packages = @{}
foreach ($project in 'RaptorSense.App', 'RaptorSense.Cli', 'RaptorSense.Helper', 'RaptorSense.Core') {
    $assets = Get-Content "src\$project\obj\project.assets.json" -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package') { $packages[$library.Name.ToLowerInvariant()] = $library.Name }
    }
}
# The .NET runtime itself is bundled but isn't listed as a library: add the newest runtime pack that was used.
$runtimePack = Get-ChildItem (Join-Path $packageRoot "microsoft.netcore.app.runtime.$Runtime") -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
$packages["microsoft.netcore.app.runtime.$Runtime/$($runtimePack.Name)"] = "Microsoft.NETCore.App.Runtime.$Runtime/$($runtimePack.Name)"

$notices = New-Object Collections.Generic.List[string]
$notices.Add("Third-party software bundled in RaptorSense $version ($Runtime).")
$notices.Add('RaptorSense itself is GPL-3.0-or-later: see LICENSE. See also THIRD_PARTY_NOTICES.md.')
$notices.Add('')
$unknown = New-Object Collections.Generic.List[string]
$usesMit = $false

foreach ($key in ($packages.Keys | Sort-Object)) {
    $id, $packageVersion = $key -split '/', 2
    $folder = Join-Path $packageRoot "$id\$packageVersion"
    $nuspecFile = Get-ChildItem $folder -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $nuspecFile) { $unknown.Add($packages[$key]); continue }
    $nuspec = New-Object Xml.XmlDocument
    $nuspec.Load($nuspecFile.FullName)   # honours the file's encoding, unlike Get-Content in PowerShell 5.1
    $meta = $nuspec.package.metadata

    # A licence is an SPDX expression, or the path of a file inside the package (copied below).
    $license = ''
    if ($meta.license -and $meta.license.type -eq 'expression') { $license = $meta.license.'#text' }
    $copied = @()
    $licenseFile = if ($meta.license -and $meta.license.type -eq 'file') { $meta.license.'#text' } else { '' }
    Get-ChildItem $folder -File | Where-Object { $_.Name -match '^(license|licence|copying|notice|third-party-notices|thirdpartynotices)' -or $_.Name -eq $licenseFile } | ForEach-Object {
        $target = Join-Path $licenses "$($meta.id)"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Copy-Item $_.FullName $target
        $copied += $_.Name
    }
    if ($license -eq 'MIT') { $usesMit = $true }
    if (-not $license -and $copied.Count -eq 0) { $unknown.Add($packages[$key]) }

    $notices.Add("$($meta.id) $packageVersion")
    if ($license) { $notices.Add("  Licence: $license") }
    if ($meta.copyright) { $notices.Add("  Copyright: $($meta.copyright)") }
    if ($meta.authors) { $notices.Add("  Authors: $($meta.authors)") }
    if ($meta.projectUrl) { $notices.Add("  Project: $($meta.projectUrl)") }
    if ($copied.Count -gt 0) { $notices.Add("  Licence files: licenses\$($meta.id)\ ($($copied -join ', '))") }
    $notices.Add('')
}

if ($usesMit) {
    $notices.Add('---------- MIT License, for the packages marked MIT above ----------')
    $notices.Add('')
    $notices.Add('Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:')
    $notices.Add('')
    $notices.Add('The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.')
    $notices.Add('')
    $notices.Add('THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.')
}
[IO.File]::WriteAllLines((Join-Path $licenses 'NOTICES.txt'), $notices, (New-Object Text.UTF8Encoding $false))

if ($unknown.Count -gt 0) { Write-Warning "No licence found for: $($unknown -join ', ')" }

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Packages listed: $($packages.Count)"
Write-Host ('{0} ({1:N0} MB)' -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "SHA-256: $hash"
