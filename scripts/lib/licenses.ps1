<#
Writes the licences of everything bundled in a download into <Stage>\licenses: NOTICES.txt, generated from the
metadata of the NuGet packages actually used, and beside it any licence files those packages ship.

Used by scripts\package.ps1 (Windows) and scripts/package-linux.sh (Linux, through pwsh), so it has to run in
Windows PowerShell 5.1 and in PowerShell 7 on Linux alike. Run it after the projects were published for <Runtime>:
it reads obj\project.assets.json, which the publish leaves behind.

  powershell -ExecutionPolicy Bypass -File scripts\lib\licenses.ps1 -Stage <folder> -Runtime linux-x64 -Version 1.8.5 -Projects RaptorSense.App,RaptorSense.Cli,RaptorSense.Core

Prints the number of packages listed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string[]]$Projects
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$Projects = @($Projects | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # "-File" hands a list over as one word
$slash = if ($Runtime -like 'win*') { '\' } else { '/' }   # how the notices spell a path, for whoever reads them

$licenses = Join-Path $Stage 'licenses'
if (Test-Path $licenses) { Remove-Item $licenses -Recurse -Force }
New-Item -ItemType Directory -Path $licenses | Out-Null
$packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path (Join-Path $HOME '.nuget') 'packages' }

$packages = @{}
foreach ($project in $Projects) {
    $assetsFile = Join-Path (Join-Path (Join-Path (Join-Path $root 'src') $project) 'obj') 'project.assets.json'
    $assets = Get-Content $assetsFile -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package') { $packages[$library.Name.ToLowerInvariant()] = $library.Name }
    }
}
# The .NET runtime itself is bundled but isn't listed as a library: add the newest runtime pack that was used.
$runtimePack = Get-ChildItem (Join-Path $packageRoot "microsoft.netcore.app.runtime.$Runtime") -Directory |
    Sort-Object { [version]($_.Name -replace '-.*$', '') } | Select-Object -Last 1
$packages["microsoft.netcore.app.runtime.$Runtime/$($runtimePack.Name)"] = "Microsoft.NETCore.App.Runtime.$Runtime/$($runtimePack.Name)"

$notices = New-Object Collections.Generic.List[string]
$notices.Add("Third-party software bundled in RaptorSense $Version ($Runtime).")
$notices.Add('RaptorSense itself is GPL-3.0-or-later: see LICENSE. See also THIRD_PARTY_NOTICES.md.')
$notices.Add('')
$unknown = New-Object Collections.Generic.List[string]
$usesMit = $false

foreach ($key in ($packages.Keys | Sort-Object)) {
    $id, $packageVersion = $key -split '/', 2
    $folder = Join-Path (Join-Path $packageRoot $id) $packageVersion
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
    if ($copied.Count -gt 0) { $notices.Add("  Licence files: licenses$slash$($meta.id)$slash ($($copied -join ', '))") }
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
Write-Host "Packages listed: $($packages.Count)"
