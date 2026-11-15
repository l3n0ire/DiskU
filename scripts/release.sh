#!/bin/zsh
# Builds a release zip for a version tag: scripts/release.sh v1.2.3 → dist/DiskU-1.2.3-macos.zip
set -euo pipefail
cd "${0:A:h}/.."

tag=${1:?usage: release.sh vX.Y.Z}
[[ $tag =~ '^v[0-9]+\.[0-9]+\.[0-9]+$' ]] || { echo "tag must look like v1.2.3, got $tag" >&2; exit 64; }
version=${tag#v}

# Release builds must use the macOS 26 SDK or newer, as local builds do. A binary built with
# an older SDK runs in SwiftUI's compatibility mode on newer macOS and behaves differently.
sdk=${SDKROOT:+$(xcrun --sdk $SDKROOT --show-sdk-version)}
sdk=${sdk:-$(xcrun --sdk macosx --show-sdk-version)}
(( ${sdk%%.*} >= 26 )) || { echo "release builds need the macOS 26 SDK or newer, found $sdk" >&2; exit 1; }

scripts/swift.sh test
DISKU_VERSION=$version DISKU_BUILD=$(git rev-list --count HEAD) scripts/bundle.sh

mkdir -p dist
zip=dist/DiskU-$version-macos.zip
rm -f $zip
ditto -c -k --keepParent DiskU.app $zip
echo $zip
