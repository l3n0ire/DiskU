# DiskU for Windows

A native Windows port of DiskU (WinUI 3, C#, .NET 10). It scans a folder or drive and draws every file as a rectangle sized by the space it uses, like the macOS app in the repo root. The Mac app's behavior is specified in `docs/mac-spec.md`; port decisions are in `docs/decisions.md`.

**Status:** the full UI runs: welcome, scanning, failure and browser states, the outline sidebar, the Win2D treemap, breadcrumbs, the inspector, the kind legend, banners, menus and shortcuts. It was checked with screenshots against fixtures and a 600,000-file synthetic tree.

## Using it

- **Scan** your profile folder (Home), the system drive (Full Disk; its drop-down lists the other fixed drives) or any folder (Scan…). `DiskU.exe "D:\Some Folder"` scans that folder at launch.
- **Treemap:** click selects, double-click zooms into a folder (or a file's folder), right-click opens the item menu. The breadcrumbs above zoom back out.
- **Sidebar:** the scan as a tree, largest first, 100 items per folder behind a "Show more" row. The button at the left of the title bar hides it.
- **Inspector:** size, share of the parent, modified date, location, actions, and bytes per kind for the whole scan.
- **Keys:** Ctrl+O scan folder, Ctrl+Shift+H scan home, Ctrl+R rescan, Esc cancel a scan, Ctrl+Shift+R show in File Explorer, Space or Ctrl+Down open, Ctrl+Alt+C copy path, Ctrl+Up zoom out, Delete move to the Recycle Bin (after a confirmation).

## Requirements

- Windows 10 1809 or later, x64 (ARM64 builds with `-Arch arm64`, untested).
- To build: the .NET 10 SDK. No Visual Studio workload is needed.
- To run a packaged zip: nothing. It is self-contained (.NET and the Windows App SDK ship inside).

## Layout

| Path | What |
|---|---|
| `DiskU.sln` | Core, Tests and the app. |
| `src/DiskU.Core/` | Model, scanner, squarify layout, store, formatting, strings, update check. No WinUI dependency. |
| `src/DiskU.App/` | The WinUI 3 app (unpackaged, self-contained). Views only. Builds `DiskU.exe`. |
| `tests/DiskU.Core.Tests/` | MSTest 4 tests: the Mac suite's literal values plus Windows-specific cases. |
| `scripts/` | PowerShell helpers. Each works from any directory. |

## Build, test, run

```powershell
windows\scripts\build.ps1            # dotnet build windows\DiskU.sln -c Release; prints the exe path
windows\scripts\test.ps1             # dotnet test (Release); extra args pass through, e.g. --filter Name~Scanner
windows\src\DiskU.App\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\DiskU.exe
```

The raw commands work too: `dotnet build windows/DiskU.sln -c Release` and `dotnet test windows/tests/DiskU.Core.Tests -c Release`. Core and Tests treat warnings as errors, including the MSTest analyzers.

Run the tests unelevated. As administrator, `UnreadableDirectoryCountsAsInaccessible` is inconclusive because admins read past the deny rule it sets.

## Package

```powershell
windows\scripts\package.ps1                     # windows\dist\DiskU-<version>-windows-x64.zip
windows\scripts\package.ps1 -Version 1.0.5      # override the version from Directory.Build.props
```

The version defaults to `Version` in `Directory.Build.props`, which tracks the latest `CHANGELOG.md` entry.

## Releasing

Pushing a `vX.Y.Z` tag on `main` publishes one GitHub release with the Mac zip and both Windows zips (see the root `README.md`). The Windows job runs `release.ps1`, which you can run locally too:

```powershell
windows\scripts\release.ps1 -Tag v1.0.5   # tests, then windows\dist\DiskU-1.0.5-windows-x64.zip and -arm64.zip
```

It rejects tags that don't look like `v1.2.3`, stops on the first failure, and prints the two zip paths last.

## Other scripts

- `make-fixture.ps1 <dir>`: port of `scripts/make-fixture.sh`. It writes 17 files of random data totalling 106,958,848 bytes (DiskU shows 107 MB) plus two links. Use a scratch directory. It refuses to replace a non-empty folder that isn't an earlier fixture.
- `screenshot.ps1 -Title DiskU -Out shot.png` (or `-Process DiskU`): saves a window with `PrintWindow` flag 2, which captures WinUI content even behind other windows.
- `make-icon.ps1`: regenerates `src/DiskU.App/Assets/AppIcon.ico` (16 to 256 px) from `Resources/AppIcon.png`. Never edit the `.ico` by hand.

Developer switches (environment variables, off unless set): `DISKU_LOG=<file>` appends scan and treemap timings, `DISKU_THEME=Light|Dark` forces a theme, `DISKU_CURRENT_VERSION=1.0.0` pretends to be an older version so the update banner shows.

## Differences from the Mac app

- **Recycle Bin, not Trash.** Deleting still means moving to the Recycle Bin after a confirmation naming the item and size. There is no permanent delete.
- **Full Disk** scans the system drive (`C:\`), and its drop-down lists the other fixed drives.
- **Administrator instead of Full Disk Access.** When folders can't be read, a banner offers "Restart as Administrator".
- **Links.** The scanner never follows reparse points (symlinks, junctions, mount points) and never crosses volumes.
- **Hard links count once per scan**, keyed by volume serial and file index, so `C:\Windows\WinSxS` isn't counted several times. The Mac app has no equivalent dedupe.
- **No app bundles.** A folder named `Tool.app` is an ordinary folder.
- **Show in File Explorer** replaces Reveal in Finder, and **Open** (the default app) replaces Quick Look.
- **Shortcuts** use Ctrl, Alt and Shift for ⌘, ⌥ and ⇧ (Ctrl+O, Ctrl+R, Ctrl+Shift+H and so on).
- Sizes use the Mac's 1000-based units (KB, MB, GB) so both apps show the same numbers.
