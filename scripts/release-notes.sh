#!/bin/zsh
# Prints the GitHub release notes for a tag: its CHANGELOG.md section plus install instructions.
# Fails if CHANGELOG.md has no "## <tag>" section, so every release ships with notes.
set -euo pipefail
cd "${0:A:h}/.."

tag=${1:?usage: release-notes.sh vX.Y.Z}
version=${tag#v}
changes=$(awk -v tag="## $tag" '$0 == tag { found = 1; next } found && /^## / { exit } found && (NF || started) { started = 1; print }' CHANGELOG.md)
[[ -n ${changes//[[:space:]]/} ]] || { echo "CHANGELOG.md has no entry for $tag" >&2; exit 1; }

print -r -- "$changes"
cat <<'NOTES'

### Install

**macOS**

NOTES
print -r -- "Download \`DiskU-$version-macos.zip\`. It runs on Apple Silicon and Intel Macs (macOS 14 or later)."
cat <<'NOTES'

DiskU is not signed. After unzipping and opening DiskU.app, go to System Settings > Privacy & Security, scroll to the bottom, find DiskU, and click "Open Anyway".

**Windows**

NOTES
print -r -- "Download \`DiskU-$version-windows-x64.zip\` for most PCs, or \`DiskU-$version-windows-arm64.zip\` for ARM PCs such as Snapdragon laptops. It runs on Windows 10 version 1809 or later and on Windows 11."
cat <<'NOTES'

There is no installer. Unzip the folder anywhere and run DiskU.exe inside it.

DiskU is not signed, so Windows SmartScreen may warn you the first time. Click "More info", then "Run anyway".
NOTES
