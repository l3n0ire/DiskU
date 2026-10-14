# DiskU

A native macOS disk-usage visualizer (SwiftUI, SwiftPM, macOS 14+). See `README.md` for what the app does.

## Rules

- **Build and test only through `scripts/swift.sh`** (`scripts/swift.sh build`, `scripts/swift.sh test`, `scripts/swift.sh run -c release diskusnap ...`). Plain `swift` fails on this machine's Command Line Tools. `docs/toolchain.md` explains why and when the wrapper can go.
- **Bundle the app with `scripts/bundle.sh`.** It writes an ad-hoc signed `DiskU.app` at the repo root. The icon is `Resources/AppIcon.icns`, generated from `Resources/AppIcon.png` by `scripts/make-icon.swift`. Never edit the `.icns` by hand.
- **Releases come from version tags** (`vX.Y.Z` on `main`) via `.github/workflows/release.yml`, which runs `scripts/release.sh`. Change the release steps in that script, not the workflow, so they stay runnable locally. Every release needs a `## vX.Y.Z` section in `CHANGELOG.md` written for users; `scripts/release-notes.sh` turns it into the release notes.
- **Never trash or delete real user files while testing.** Tests inject a recording trasher (`DiskStore(trasher:)`). Manual checks use the fixture from `scripts/make-fixture.sh` inside a scratch directory.
- **Every polling loop in a test needs a deadline** (see `cancelledScanDoesNotOverwriteItsReplacement` in `DiskStoreTests.swift`). A loop without one once hung the whole suite for minutes.
- **Prove UI changes with the `verify-disku` skill** (`.claude/skills/verify-disku/`). Say plainly which parts you could not drive.

## Layout

- `Sources/DiskUKit` is the library. It holds the model (`FileNode`, `Kind`), the scanner (`Scanner`, `ScanState`), the layout (`Squarify`, `TreemapLayout`), the store (`DiskStore`), and the SwiftUI views under `Views/`.
- `Sources/DiskU` is the app entry point only.
- `Sources/diskusnap` is a verification tool. It scans a folder with the real scanner and renders the real view to a PNG.
- `Tests/DiskUKitTests` uses Swift Testing (`import Testing`) and asserts literal values.

## Design invariants

- **Sizes change only through `FileNode.propagate(size:items:)`.** It pushes a delta up the parent chain and keeps every folder's children sorted largest first. Scanning and trashing both go through it. Never set a folder's `size` directly.
- **File kinds and colors live in `Kind.swift`.** The extension table maps extensions to a `Kind`, and one `switch` maps a `Kind` to its label and color. Add a file type there, never with `if` checks in views.
- **Scan status is the single `ScanState` enum** (`idle`, `scanning`, `done`, `failed`). Views switch on it. Don't add parallel booleans such as `isScanning`.
- **A replaced or cancelled scan must not write state.** `DiskStore` tags each scan with a generation number and drops results and progress from older ones.
- **Toolbar tooltips live in `ToolbarTooltips.byLabel`** (`Platform.swift`), keyed by the button label. SwiftUI `.help` on toolbar buttons does not show on macOS 26. A new toolbar button needs an entry there.
- **The sidebar never lists a whole folder at once.** `DiskStore.outlineLimits` caps each folder at its largest `outlinePage` children behind a "Show more" row, and selecting a hidden item lifts the cap. SwiftUI builds every row of an expanded `List` group up front (5,000 rows took about 30 s), so keep rows cheap: `Icons.outlineIcon` shares icons per file type, and no `GeometryReader` per row.
- **Deleting means moving to the Trash** (`FileManager.trashItem`) after a confirmation that names the item and its size. Never add a permanent delete.
- **The scanner does not follow symlinks or cross volumes.** The Full Disk button scans `/`. When scanning `/` it skips `/System/Volumes`, `/Volumes`, `/.nofollow`, `/.resolve`, `/.vol` and `/dev`. `/.nofollow` and `/.resolve` re-list the whole disk and would double count it.

## Known gaps

`docs/known-gaps.md` lists open issues and the pending product decision. Check it before starting related work, and update it when you close or find one.
