# Toolchain notes

DiskU builds with the Command Line Tools (no Xcode) through `scripts/swift.sh`. This page records why the wrapper exists and when it can be removed.

## Current setup

- Command Line Tools for Xcode 27.0, Swift 6.4, on macOS 26.6.
- `scripts/swift.sh` does two things when `xcode-select -p` points at the Command Line Tools:
  1. It sets `SDKROOT` to `MacOSX26.sdk`. In the macOS 27 SDK, SwiftUI's `@State` is a macro whose plugin (`SwiftUIMacros`) ships only with full Xcode. Against that SDK the build fails with "plugin for module 'SwiftUIMacros' not found".
  2. It passes `-plugin-path .../usr/lib/swift/host/plugins/testing` right after the subcommand. Without it the default build system intermittently fails to find `TestingMacros` (3 of 4 clean test builds failed). The flags must go before the remaining arguments, or `swift run` hands them to the executable.
- With full Xcode selected, the wrapper runs plain `swift`.
- The app still targets macOS 14, so building against the 26 SDK does not limit where it runs.

## When to remove the wrapper

Delete `scripts/swift.sh` (and point `scripts/bundle.sh` and the docs back at `swift`) once both of these pass on clean builds without it:

```sh
rm -rf .build && swift build
rm -rf .build && swift test
```

That should happen with full Xcode, or with a Command Line Tools release that ships `SwiftUIMacros` and wires the testing plugin path correctly.

## History

- **Swift 6.1.2 Command Line Tools, partially updated.** `swift-package` crashed at load with `dyld: Symbol not found ... llbuild`. The tools were from May 2025 but `llbuild.framework` was from 2026. There were no package receipts, so Software Update offered no repair.
- **Reinstall with `xcode-select --install` (Command Line Tools 26.6).** This was mismatched again. Swift 6.3.3 tools shipped next to 6.4 frameworks, `swift-package` crashed on a missing `BuildServerProtocol` symbol, and the compiler rejected the newer Observation macros.
- **`sudo softwareupdate --install "Command Line Tools for Xcode 27.0-27.0"`.** This gave a consistent Swift 6.4 install. The two gaps above remain.

If `swift build` crashes in `dyld` before compiling anything, suspect a mismatched install first. Compare file dates under `/Library/Developer/CommandLineTools/usr/bin` and `usr/lib/swift/pm`, and check `swift --version`.
