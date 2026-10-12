#!/bin/zsh
# Prints the GitHub release notes for a tag: its CHANGELOG.md section plus install instructions.
# Fails if CHANGELOG.md has no "## <tag>" section, so every release ships with notes.
set -euo pipefail
cd "${0:A:h}/.."

tag=${1:?usage: release-notes.sh vX.Y.Z}
changes=$(awk -v tag="## $tag" '$0 == tag { found = 1; next } found && /^## / { exit } found && (NF || started) { started = 1; print }' CHANGELOG.md)
[[ -n ${changes//[[:space:]]/} ]] || { echo "CHANGELOG.md has no entry for $tag" >&2; exit 1; }

print -r -- "$changes"
cat <<'NOTES'

### Install

Runs on Apple Silicon and Intel Macs (macOS 14 or later).

DiskU is not signed. After unzipping and opening DiskU.app, go to System Settings > Privacy & Security, scroll to the bottom, find DiskU, and click "Open Anyway".
NOTES
