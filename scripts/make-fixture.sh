#!/bin/zsh
# Creates a disk-usage fixture with known sizes (all whole 4 KiB blocks, random data).
# Expected allocated total: 106,958,848 bytes (104,452 KiB) in 17 files plus 2 symlinks.
set -euo pipefail
root=${1:?usage: make-fixture.sh <dir>}
rm -rf "$root"
mkdir -p "$root"/{Movies,Pictures/Vacation,Pictures/Screens,Music,Code/app/src,Docs,Archives,Apps/Tool.app/Contents/MacOS}

mk() { dd if=/dev/urandom of="$root/$1" bs=1024 count=$2 2>/dev/null; }

mk Movies/trip.mov            51200   # 50 MiB
mk Pictures/Vacation/beach.heic 6144
mk Pictures/Vacation/sunset.jpg 4096
mk Pictures/Vacation/dunes.jpg  2048
mk Pictures/Screens/shot1.png   4096
mk Pictures/Screens/shot2.png   4096  # images total 20 MiB
mk Music/song.m4a              8192
mk Music/demo.wav              4096
mk Code/app/src/main.swift       64
mk Code/app/src/data.json      1984
mk Code/app/build.o            2048
mk Docs/report.pdf             3072
mk Docs/notes.txt                 4
mk Docs/slides.key             2044
mk Archives/backup.zip         6144
mk Apps/Tool.app/Contents/MacOS/Tool 5120
mk Apps/Tool.app/Contents/Info.plist    4

ln -s ../.. "$root/Code/app/loop"
ln -s ../Movies/trip.mov "$root/Docs/trip-alias.mov"
