---
name: verify-disku
description: Prove a DiskU change works on the real app. Use after changing the scanner, store, treemap, or any view, and before saying a DiskU change is done.
---

# Verify DiskU

Run these from the repo root. Put fixtures and PNGs in your scratch directory, never in the repo.

## 1. Tests

```sh
scripts/swift.sh test
```

Expect `Test run with N tests ... passed`. A run that stalls with no CPU use is a hung polling loop. Kill it and add a deadline to the loop.

## 2. Exact scan totals

```sh
scripts/make-fixture.sh "$SCRATCH/fixture"
scripts/swift.sh run -c release diskusnap "$SCRATCH/fixture" "$SCRATCH/fixture.png"
```

The fixture is exactly **106,958,848 bytes** (104,452 KiB). It includes a `loop -> ../..` symlink and an app bundle. A different total means the scanner double counts or misses something. The scan should finish in well under a second, which shows the symlink loop is not followed. Cross-check with `du -sk "$SCRATCH/fixture"`.

## 3. Look at the render

Open each PNG with the Read tool and check it yourself:

- Rectangle areas match the sizes. Movies (52.4 MB of 107 MB) fills about half the treemap.
- Folders nest with readable headers, colors match the legend, and labels are legible.
- With `--zoom`, the breadcrumb shows the zoomed folder.

For real data, render `~` and `~/Library` as well. When scanning `/`, the total should come in below the "used" figure from `df -h /`. A total above it means a path is counted twice.

Known capture limits that are not app bugs:

- The default window renderer draws button bezels and translucent fills over vibrancy as blank white shapes.
- `--renderer image` (ImageRenderer) cannot draw `NavigationSplitView` or `List`, so it renders only the treemap pane.

## 4. The app bundle

```sh
scripts/bundle.sh
open DiskU.app
pgrep -lf DiskU.app/Contents/MacOS
osascript -e 'quit app "DiskU"'
```

Confirm the process starts and quits cleanly.

## 5. What you cannot verify

Without Screen Recording permission (`screencapture -l` fails), an agent cannot drive the real window. Clicking, hover, Quick Look, keyboard shortcuts, and the Move to Trash confirmation are checked only through the `DiskStore` tests. Report these as unverified and ask the user to try them. Never trash a real file to test the flow.
