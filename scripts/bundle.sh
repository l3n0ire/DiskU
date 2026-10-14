#!/bin/zsh
# Builds a universal release binary and assembles an ad-hoc signed DiskU.app at the repo root.
# DISKU_VERSION and DISKU_BUILD set the version shown in Finder; they default to the latest
# vX.Y.Z tag and the commit count, so a local build never looks older than the last release.
set -euo pipefail
cd "${0:A:h}/.."
latest_tag=$(git describe --tags --abbrev=0 --match 'v[0-9]*' 2>/dev/null || echo v1.0)
version=${DISKU_VERSION:-${latest_tag#v}}
build=${DISKU_BUILD:-$(git rev-list --count HEAD)}

# Universal binary so the app runs on both Apple Silicon and Intel Macs.
arch=(--arch arm64 --arch x86_64)
scripts/swift.sh build -c release --product DiskU $arch
binary=$(scripts/swift.sh build -c release $arch --show-bin-path)/DiskU

app=DiskU.app
rm -rf $app
mkdir -p $app/Contents/MacOS $app/Contents/Resources
cp $binary $app/Contents/MacOS/DiskU
cp Resources/AppIcon.icns $app/Contents/Resources/AppIcon.icns
cat > $app/Contents/Info.plist <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleIdentifier</key><string>com.colin.DiskU</string>
  <key>CFBundleName</key><string>DiskU</string>
  <key>CFBundleDisplayName</key><string>DiskU</string>
  <key>CFBundleExecutable</key><string>DiskU</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$build</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST
codesign --force --sign - $app
echo "built $PWD/$app"
