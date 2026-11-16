#!/bin/bash
# Builds the Linux download: dist/RaptorSense-<version>-linux-x64.tar.gz. Self-contained, so people don't need .NET.
#
#   scripts/package-linux.sh                    on Linux with the .NET SDK and pwsh: publishes, then packs
#   PUBLISHED=<folder> scripts/package-linux.sh  packs what was published elsewhere into <folder>/app and <folder>/cli
#                                               (how it is done from Windows through WSL, which has no .NET)
#
# The packing happens on a Linux filesystem because a Windows drive doesn't keep the "may be run" mark on files.
#
# Like the Windows download it carries the licences of everything bundled, in licenses/, written by
# scripts/lib/licenses.ps1. That script needs the NuGet packages the publish used, so with PUBLISHED it has to have
# been run where the publish was, into <folder>/app. From Windows, for a <folder> of your choosing:
#
#   dotnet publish src\RaptorSense.App\RaptorSense.App.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true `
#     -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o <folder>\app
#   dotnet publish src\RaptorSense.Cli\RaptorSense.Cli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true `
#     -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o <folder>\cli
#   powershell -ExecutionPolicy Bypass -File scripts\lib\licenses.ps1 -Stage <folder>\app -Runtime linux-x64 `
#     -Version <version> -Projects RaptorSense.App,RaptorSense.Cli,RaptorSense.Core
#   wsl -e env PUBLISHED=<folder as /mnt/c/...> bash scripts/package-linux.sh
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
runtime="linux-x64"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -1)"
name="RaptorSense-$version-$runtime"
work="$(mktemp -d)"
stage="$work/$name"
trap 'rm -rf "$work"' EXIT
mkdir -p "$stage/cli"

if [ -n "${PUBLISHED:-}" ]; then
  cp -r "$PUBLISHED/app/." "$stage/"
  cp -r "$PUBLISHED/cli/." "$stage/cli/"
else
  for project in App:"$stage" Cli:"$stage/cli"; do
    dotnet publish "$root/src/RaptorSense.${project%%:*}/RaptorSense.${project%%:*}.csproj" -c Release -r "$runtime" \
      --self-contained true -v q --nologo -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
      -p:DebugType=none -p:DebugSymbols=false -o "${project#*:}"
  done
fi
find "$stage" -name '*.pdb' -delete

cp "$root/LICENSE" "$root/README.md" "$root/THIRD_PARTY_NOTICES.md" "$stage/"

if [ ! -f "$stage/licenses/NOTICES.txt" ]; then
  if [ -n "${PUBLISHED:-}" ]; then
    echo "No licences in $PUBLISHED/app/licenses: run scripts/lib/licenses.ps1 where the publish was done (see above)." >&2
    exit 1
  fi
  pwsh -NoProfile -File "$root/scripts/lib/licenses.ps1" -Stage "$stage" -Runtime "$runtime" -Version "$version" \
    -Projects RaptorSense.App,RaptorSense.Cli,RaptorSense.Core
fi

mkdir -p "$stage/linux"
cp "$root"/linux/* "$stage/linux/"

# Text files checked out or written on Windows end their lines the Windows way. A Linux download shouldn't.
sed -i 's/\r$//' "$stage/LICENSE" "$stage"/*.md "$stage"/linux/* "$stage/licenses/NOTICES.txt"

find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/RaptorSense" "$stage/cli/raptorsense" "$stage"/linux/*.sh

mkdir -p "$root/dist"
tar -C "$work" -czf "$root/dist/$name.tar.gz" "$name"
echo "$root/dist/$name.tar.gz ($(du -h "$root/dist/$name.tar.gz" | cut -f1))"
echo "SHA-256: $(sha256sum "$root/dist/$name.tar.gz" | cut -d' ' -f1)"
