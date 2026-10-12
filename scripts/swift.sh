#!/bin/zsh
# Runs `swift <args>` with the workarounds the Command Line Tools need; with full Xcode it is plain `swift`.
# CLT ship neither the SwiftUIMacros plugin the macOS 27 SDK's @State expands to, nor reliably wire up
# the Swift Testing macro plugin, so pin the macOS 26 SDK and pass the plugin path explicitly.
set -euo pipefail
cd "${0:A:h}/.."

clt=/Library/Developer/CommandLineTools
if [[ "$(xcode-select -p)" == $clt ]]; then
  export SDKROOT=$clt/SDKs/MacOSX26.sdk
  exec swift "$1" -Xswiftc -plugin-path -Xswiftc $clt/usr/lib/swift/host/plugins/testing "${@:2}"
fi
exec swift "$@"
