# Windows port decisions (binding for all agents)

Input: mac-spec.md (behavior source of truth), in the same folder as this file.

## Repo layout
- The macOS app stays exactly where it is (Package.swift at the repo root, Sources/, Tests/, scripts/). Do NOT move or edit mac files: the release workflow and SwiftPM depend on the paths, and the mac build can't be verified on this PC.
- Everything Windows lives under `windows/`:
  - `windows/DiskU.sln`
  - `windows/src/DiskU.Core/` — net10.0 class library (no WinUI dependency): FileNode, Kind, Scanner, ScanState, Squarify, TreemapLayout, DiskStore-equivalent view model logic, size formatting, update check. Port of Sources/DiskUKit minus the views.
  - `windows/src/DiskU.App/` — WinUI 3 unpackaged app (net10.0-windows10.0.19041.0, x64 + arm64, WindowsPackageType=None, WindowsAppSDKSelfContained=true, Microsoft.WindowsAppSDK 2.5.1). Views only.
  - `windows/tests/DiskU.Core.Tests/` — MSTest 4 (xUnit v2 is blocked by Smart App Control here). Port all 18 mac tests with the same literal assertions where the semantics carry over, plus Windows-specific ones.
  - `windows/scripts/` — PowerShell: `build.ps1`, `test.ps1`, `make-fixture.ps1` (port of scripts/make-fixture.sh), `package.ps1` (zip of self-contained publish output), `screenshot.ps1` (PrintWindow flag 2).
  - `windows/README.md`.
- Keep a single `.gitignore` entry set for bin/obj under windows/.

## Behavior mapping
- Parity: every feature, string, shortcut, layout, color, and invariant in mac-spec.md, unless listed here.
- Byte sizes: keep the mac's 1000-based units and decimals (KB/MB/GB) so both apps show identical numbers.
- Sorting: size desc, ties by name using ordinal (uppercase first) comparison, as on mac.
- Sizes change only through FileNode.Propagate(size, items). Never set a folder size directly.
- ScanState single enum (Idle, Scanning, Done, Failed). Generation tagging: replaced/cancelled scans must not write state.
- Sidebar paging (outlineLimits / outlinePage / "Show more") exactly as mac; use virtualization-friendly cheap rows.
- Delete = move to Recycle Bin (IFileOperation or SHFileOperation with FOF_ALLOWUNDO), after a confirmation naming the item and size. Never permanent delete. Inject an ITrasher so tests record instead of deleting.
- Scanner: do not follow reparse points (symlinks, junctions, mount points) — treat as zero-size leaf or skip, matching mac's no-symlink rule. Do not cross volumes. Hard links: count each file ID once per scan (dedupe by volume serial + file index) so C:\ isn't massively overcounted by WinSxS; document this deviation. Use allocated size if mac uses allocated size, otherwise logical — mirror the spec. Use fast enumeration (FindFirstFileExW with FIND_FIRST_EX_LARGE_FETCH / FindExInfoBasic, or .NET EnumerationOptions with AttributesToSkip=0, IgnoreInaccessible) and parallelism comparable to mac.
- Full Disk button: scans the system drive (C:\ i.e. Path.GetPathRoot of Windows dir); also offer other fixed drives (a drop-down / split button). Skip list on a drive root: `System Volume Information`, `$Recycle.Bin` is fine to count (it's real usage) — decide by "would double count or is unreadable"; at minimum never descend into reparse points.
- Full Disk Access onboarding -> Windows: when a scan hits access-denied folders, show the equivalent banner explaining some folders were skipped and offering "Restart as Administrator" (ShellExecute runas). No FDA settings steps.
- Reveal in Finder -> "Show in File Explorer" (explorer.exe /select,"path"). Quick Look -> "Open" with default app (or omit if mac Quick Look has no sane equivalent). App bundles (.app) don't exist; folders are folders. Tests that rely on Tool.app: port using a plain folder with the same name and the same expectation where meaningful.
- Update check: same GitHub repo (l3n0ire/DiskU) latest release; set a User-Agent header. Releases will carry both mac and Windows assets under the same vX.Y.Z tag, so version compare stays shared. Banner links to the release page.
- Toolbar tooltips: WinUI ToolTipService works, but keep a single tooltip table keyed by label like mac's ToolbarTooltips.byLabel.
- Look: native Windows 11 — Mica backdrop, custom title bar with app icon, Segoe UI Variable / Fluent icons (Segoe Fluent Icons), light/dark following system, CommandBar/toolbar, NavigationView-free simple split layout (sidebar TreeView/ListView + treemap). Treemap drawn with Win2D or Composition/Canvas; must stay smooth with tens of thousands of nodes (draw only visible depth per spec). Same Kind colors as mac.
- App icon: generate windows/src/DiskU.App/Assets/AppIcon.ico from Resources/AppIcon.png (script, not by hand).
- Version: read from the assembly version, kept equal to the latest CHANGELOG version (1.0.4) for now.
