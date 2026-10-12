#!/bin/zsh
# Builds release binaries and assembles an ad-hoc signed DiskU.app at the repo root.
set -euo pipefail
cd "${0:A:h}/.."

scripts/swift.sh build -c release --product DiskU
binary=$(scripts/swift.sh build -c release --show-bin-path)/DiskU

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
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>14.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST
codesign --force --sign - $app
echo "built $PWD/$app"
