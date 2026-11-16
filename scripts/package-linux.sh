#!/bin/bash
# Builds the Linux download: dist/RaptorSense-<version>-linux-x64.tar.gz. Self-contained, so people don't need .NET.
#
#   scripts/package-linux.sh                    on Linux with the .NET SDK: publishes, then packs
#   PUBLISHED=<folder> scripts/package-linux.sh  packs what was published elsewhere into <folder>/app and <folder>/cli
#                                               (how it is done from Windows through WSL, which has no .NET)
#
# The packing happens on a Linux filesystem because a Windows drive doesn't keep the "may be run" mark on files.
#
# Not a finished release package yet: unlike scripts/package.ps1 it doesn't generate the licences folder for the
# bundled libraries, which a public download needs.
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
mkdir -p "$stage/linux"
cp "$root"/linux/* "$stage/linux/"

find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/RaptorSense" "$stage/cli/raptorsense" "$stage"/linux/*.sh

mkdir -p "$root/dist"
tar -C "$work" -czf "$root/dist/$name.tar.gz" "$name"
echo "$root/dist/$name.tar.gz ($(du -h "$root/dist/$name.tar.gz" | cut -f1))"
echo "SHA-256: $(sha256sum "$root/dist/$name.tar.gz" | cut -d' ' -f1)"
