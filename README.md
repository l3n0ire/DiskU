# DiskU

DiskU is a native macOS disk-usage visualizer in the spirit of Disk Inventory X. It scans your home folder or a folder you choose, draws every file as a rectangle sized by the space it occupies, and lets you inspect, Quick Look, reveal, or move items to the Trash.

![DiskU showing a scanned home folder as a treemap](docs/screenshot.png)

Requires macOS 14 or later. Runs natively on both Apple Silicon and Intel Macs.

## Install

1. Download `DiskU-X.Y.Z.zip` from the [latest release](../../releases/latest) and unzip it.
2. Open `DiskU.app`. DiskU is not signed, so macOS blocks it the first time.
3. Go to **System Settings > Privacy & Security**, scroll to the bottom, find the message about DiskU, and click **Open Anyway**.

You only need to do this once.

## Build and run

`scripts/swift.sh` runs `swift` with the workarounds the Command Line Tools need (it pins the macOS 26 SDK and passes the Swift Testing plugin path). With full Xcode selected it runs plain `swift`.

```sh
scripts/swift.sh build
scripts/swift.sh test
scripts/swift.sh run DiskU
```

Build the app bundle (`DiskU.app` at the repo root, ad-hoc signed):

```sh
scripts/bundle.sh
open DiskU.app
```

The app icon comes from `Resources/AppIcon.png`. After replacing that image, regenerate `Resources/AppIcon.icns` and rebundle:

```sh
SDKROOT=/Library/Developer/CommandLineTools/SDKs/MacOSX26.sdk swift scripts/make-icon.swift "$PWD"
scripts/bundle.sh
```

## Releases

Pushing a version tag on `main` builds and publishes a GitHub release with `DiskU-X.Y.Z.zip`, a universal app for Apple Silicon and Intel Macs (`.github/workflows/release.yml`):

```sh
git tag v1.0.0
git push origin v1.0.0
```

The workflow runs the tests, then `scripts/release.sh`, which you can also run locally (`scripts/release.sh v1.0.0` writes `dist/DiskU-1.0.0.zip`). Tags must look like `v1.2.3`, and the tagged commit must be on `main`.

The app is ad-hoc signed and not notarized, so first-time users need the **Open Anyway** step in [Install](#install).

To scan a folder at launch, pass it as an argument:

```sh
open DiskU.app --args ~/Downloads
```

To retake the README screenshot without showing your own files, build the anonymized demo folder (about 1 GB of real disk; its files are APFS clones of one file) and open DiskU on it:

```sh
scripts/make-demo.sh /Users/Shared/DiskU-Demo
open DiskU.app --args /Users/Shared/DiskU-Demo/Home
```

## Using it

- **Scan.** Use the toolbar's Home or Scan… buttons. Scanning shows live progress and can be cancelled.
- **Outline (left).** Items are sorted by size. Each row has a bar showing its share of the parent folder.
- **Treemap (center).** Click to select, double-click to zoom into a folder, and use the breadcrumb bar to zoom back out. Hovering shows the path in the status bar.
- **Inspector (right).** Shows details for the selection, a legend of space used by kind, and the Reveal, Quick Look, Open, Copy Path (⌥⌘C), and Move to Trash actions.
- **Keyboard.** Space opens Quick Look. Delete (or ⌘⌫) asks to move the selection to the Trash.

DiskU never deletes files permanently. Move to Trash calls `FileManager.trashItem` after a confirmation, so you can restore items from the Trash in Finder.

## Full Disk Access

macOS hides some folders (Mail, Messages, Safari, other apps' containers, parts of `/Library`) from apps without Full Disk Access. DiskU counts folders it can't read and shows a banner when there are any. To see everything, open System Settings › Privacy & Security › Full Disk Access, add DiskU.app, and rescan. The banner's button opens that pane.

## How sizes are counted

- Sizes are allocated bytes on disk (`totalFileAllocatedSize`, falling back to `fileAllocatedSize`), so they match `du`, not the Finder's logical size.
- Symlinks are listed but never followed.
- Scans stay on the starting volume. Scanning `/` also includes the Data volume (reached through firmlinks) and skips `/System/Volumes`, `/Volumes`, `/.nofollow`, `/.resolve`, `/.vol`, and `/dev` so nothing is counted twice.
- App bundles and other packages are sized like folders but shown as a single item.
- Hard-linked files are counted once per link, and APFS clones and snapshots are not deduplicated.

## Verification tool

`diskusnap` scans a folder with the real scanner and renders the real browser view to a PNG, with the largest item selected. It also prints the scan time and file count.

```sh
scripts/swift.sh run -c release diskusnap ~ /tmp/home.png            # full window, offscreen
scripts/swift.sh run -c release diskusnap ~ /tmp/home.png --zoom     # zoomed into the largest folder
scripts/swift.sh run -c release diskusnap ~ /tmp/tm.png --renderer image  # ImageRenderer, treemap pane only
scripts/swift.sh run -c release diskusnap --home /tmp/home-screen.png      # home screen, no scan
scripts/make-fixture.sh /tmp/fixture                        # 106,958,848-byte fixture with a symlink loop
```

The offscreen capture can't draw system button bezels or vibrancy, so buttons and translucent fills appear as blank white shapes in the PNG. Judge those in the running app.
