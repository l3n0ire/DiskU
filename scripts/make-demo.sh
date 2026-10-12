#!/bin/zsh
# Builds an anonymized demo home folder for README screenshots: scripts/make-demo.sh /Users/Shared/DiskU-Demo
# Every file is an APFS clone of one 1 GiB file truncated to size, so it reports realistic sizes while the
# whole tree costs about 1 GiB of real disk.
set -euo pipefail
root=${1:?usage: make-demo.sh <dir>}
home=$root/Home
rm -rf "$root"
mkdir -p "$root"
base=$root/.base
dd if=/dev/urandom of=$base bs=1m count=1024 2>/dev/null

f() {  # f <path> <MiB>
  mkdir -p "$home/${1:h}"
  cp -c $base "$home/$1"
  truncate -s $(( $2 * 1048576 )) "$home/$1"
}

f "Movies/Family Trip 2025.mov" 1024
f "Movies/Graduation.mov" 760
f "Movies/Screen Recordings/Demo walkthrough.mov" 410
f "Movies/Screen Recordings/Bug report.mov" 180
f "Pictures/Photos Library.photoslibrary/originals/A/IMG_2041.heic" 620
f "Pictures/Photos Library.photoslibrary/originals/B/IMG_3310.heic" 480
f "Pictures/Photos Library.photoslibrary/originals/C/IMG_4122.mov" 350
f "Pictures/Wallpapers/mountains.jpg" 24
f "Pictures/Wallpapers/coast.png" 18
f "Music/Logic/Album Draft.logicx/Audio Files/vocals.wav" 520
f "Music/Logic/Album Draft.logicx/Audio Files/drums.wav" 310
f "Music/Library/Favorites.m4a" 90
f "Downloads/macOS Installer.dmg" 1024
f "Downloads/Xcode_Archive.xip" 880
f "Downloads/dataset.zip" 300
f "Downloads/Invoice-0423.pdf" 2
f "Documents/Design/Brand Kit.sketch" 140
f "Documents/Design/Mockups.fig" 95
f "Documents/Reports/Q3 Report.pdf" 38
f "Documents/Reports/Budget.xlsx" 12
f "Documents/Thesis/thesis.docx" 26
f "Developer/web-app/node_modules/.cache/webpack.pack" 420
f "Developer/web-app/node_modules/typescript/lib/typescript.js" 22
f "Developer/web-app/src/index.ts" 1
f "Developer/ml-project/checkpoints/model-final.pt" 900
f "Developer/ml-project/checkpoints/model-epoch-10.pt" 900
f "Developer/ml-project/data/train.csv" 260
f "Library/Developer/Xcode/DerivedData/App-abcd/Build/Intermediates.noindex/build.db" 700
f "Library/Developer/CoreSimulator/Devices/iPhone 17/data/container.img" 950
f "Library/Caches/com.apple.Safari/Cache.db" 310
f "Library/Caches/Homebrew/downloads/python.tar.gz" 140
f "Library/Application Support/Slack/Cache/data_1" 230
f "Library/Mail/V10/Mailboxes/Inbox.mbox/messages.db" 420
rm $base
du -sh "$home"
