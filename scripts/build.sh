#!/bin/zsh
# Builds the package targets with swiftc directly.
# Exists because SwiftPM can be unusable in a mismatched Command Line Tools install
# (swift-package failing to load llbuild); `swift build` remains the primary path.
# Usage: scripts/build.sh [debug|release]   -> .build/swiftc/<config>/{DiskU,diskusnap}
#        scripts/build.sh test               -> builds and runs the Swift Testing suite
set -euo pipefail
cd "${0:A:h}/.."

mode=${1:-debug}
config=$([[ $mode == release ]] && echo release || echo debug)
out=.build/swiftc/$config
mkdir -p $out

if [[ -z ${SDKROOT:-} ]]; then
  # The default SDK may be newer than the compiler; fall back to the newest one it can import.
  probe=$(mktemp -d)
  echo 'import SwiftUI' > $probe/p.swift
  for sdk in "$(xcrun --show-sdk-path)" /Library/Developer/CommandLineTools/SDKs/MacOSX15.sdk /Library/Developer/CommandLineTools/SDKs/MacOSX14.sdk; do
    if [[ -d $sdk ]] && SDKROOT=$sdk swiftc -typecheck $probe/p.swift 2>/dev/null; then export SDKROOT=$sdk; break; fi
  done
  rm -rf $probe
fi

flags=(-target arm64-apple-macos14.0 -swift-version 5)
if [[ $config == release ]]; then flags+=(-O); else flags+=(-Onone -g -enable-testing); fi

swiftc "${flags[@]}" -parse-as-library -module-name DiskUKit \
  -emit-library -static -emit-module -emit-module-path $out/DiskUKit.swiftmodule \
  -o $out/libDiskUKit.a Sources/DiskUKit/**/*.swift

for exe in DiskU diskusnap; do
  swiftc "${flags[@]}" -parse-as-library -module-name $exe -I $out -L $out -lDiskUKit \
    -o $out/$exe Sources/$exe/**/*.swift
done

if [[ $mode == test ]]; then
  fw=/Library/Developer/CommandLineTools/Library/Developer/Frameworks
  # The CLT ship Testing without its Foundation cross-import overlay module.
  swiftc "${flags[@]}" -parse-as-library -module-name DiskUKitTests -I $out -L $out -lDiskUKit \
    -Xfrontend -disable-cross-import-overlays -F $fw -Xlinker -rpath -Xlinker $fw \
    -o $out/DiskUKitTests Tests/DiskUKitTests/**/*.swift scripts/TestRunner.swift
  $out/DiskUKitTests
fi
