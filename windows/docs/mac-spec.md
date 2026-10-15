# DiskU (macOS) — complete behaviour & UI spec for a WinUI 3 / C# port

Source of truth: repo `DiskU` at commit `d349d63` ("Release v1.0.4"). Every literal below is copied from the Swift code. Where the Mac code has a quirk, it is called out as **QUIRK**; where the port has to make a choice, it is called out as **PORT DECISION**.

Contents
0. Glossary & architecture
1. Features and every user-visible string
2. Window layout and visuals (incl. exact treemap rendering)
3. Model: FileNode, Kind, propagate
4. Scanner
5. Squarify and TreemapLayout (pseudocode)
6. DiskStore (state machine, paging, selection, trash, Full Disk, FDA, update check)
7. Test suite (to be ported 1:1)
8. Release / versioning / bundling / diskusnap / scripts
9. Known gaps
10. Mac-specific concepts → Windows equivalents
11. Ambiguities & decisions summary

---

## 0. Glossary & architecture

- **DiskUKit** (library): `FileNode`, `Kind`, `Scanner` (+ `ScanProgress`, `ScanState`, `ScanResult`), `squarify()`, `TreemapLayout`/`TreemapTile`, `DiskStore` (main-thread observable app state), `UpdateChecker`/`Version`/`Release`, `Format`, `Icons`, `Actions`, `QuickLookController`, `ToolbarTooltips`, and views (`ContentView`, `BrowserView`, `TreemapPane`, `Breadcrumbs`, `StatusBar`, `FullDiskAccessBanner`, `FullDiskAccessCard`, `UpdateBanner`, `ScanToolbar`, `WelcomeView`, `ScanningView`, `FailureView`, `InspectorView`, `ItemDetails`, `KindLegend`, `OutlinePane`, `OutlineRow`, `ProportionBar`, `ShowMoreRow`, `ItemMenu`, `TreemapView`, `TreemapCanvas`, `HighlightCanvas`, `Palette`, `DiskUCommands`).
- **DiskU** (app): entry point only. One window group titled `"DiskU"`, default size 1200×800, owns one `DiskStore` and one `UpdateChecker`.
- **diskusnap**: CLI verification tool (scan + render PNG). See §8.
- Suggested C# mapping: `DiskU.Core` (model, scanner, layout, store, update check — no UI deps, fully unit-testable), `DiskU.App` (WinUI 3), `DiskU.Snap` (optional CLI), `DiskU.Tests` (xUnit/MSTest).
- Mutation model: `FileNode` is **not** observable. All tree mutations go through `DiskStore`, which bumps an integer `revision`. Views that show sizes re-render when `revision` changes. (In WinUI: raise a single `RevisionChanged`/`PropertyChanged("Revision")` and re-bind; do not make every node INotifyPropertyChanged unless you want to.)

---

## 1. Features and every user-visible string

### 1.1 Feature list
1. Scan your **home folder**, the **whole startup disk** (`/`), or a **chosen folder** (folder picker). A folder passed as a command-line argument is scanned at launch.
2. Live scan progress (files count, bytes found, current directory path), cancellable.
3. Three-pane browser after a scan:
   - **Outline (left sidebar)**: tree sorted by size (largest first), each row with icon, name, size, and a thin proportion bar = share of parent. Each folder lists at most 100 children, then a "N smaller items / size / Show more" row.
   - **Treemap (center)**: nested squarified treemap with folder headers, cushion-shaded tiles colored by file kind, labels. Click selects, double-click zooms into a folder, hover shows path in status bar, right-click context menu. Breadcrumb bar above to zoom out; status bar below.
   - **Inspector (right)**: selection details (icon, name, kind, size, Contains, Share, Modified, Where), actions (Reveal, Quick Look, Open, Copy Path, Move to Trash…), and a "By Kind" legend of bytes per kind for the whole scan.
4. **Move to Trash** (never permanent delete) after a confirmation naming item + size; tree sizes update immediately without rescanning.
5. **Full Disk Access** onboarding card on the start screen and an "N folders couldn't be read" banner after a scan.
6. **Update check** against GitHub releases at launch, with a dismissable banner.
7. Keyboard: Space = Quick Look; Delete / Forward-Delete / ⌘⌫ = ask to trash selection; menu shortcuts (§1.6).
8. Rescan last folder.

Not present (do not invent): no recents list, no settings window, no persistence of anything between launches (no last-scanned memory, no dismissed-update memory), no search, no filtering, no permanent delete, no multi-select, no "marked for deletion" list.

### 1.2 Window / app
- Window title: `DiskU`
- Default size 1200 × 800 (points). Minimum content size 900 × 560.
- App quits when its last window closes.
- App forces regular activation policy (dock icon + menu bar) and activates itself on launch (only relevant when run unbundled).

### 1.3 Toolbar (primary action placement, right side of the title bar), in order
| Label (exact) | SF Symbol | Shown when | Action | Tooltip (`ToolbarTooltips.byLabel`) |
|---|---|---|---|---|
| `Cancel` | `xmark.circle` | state is `scanning` (replaces Rescan) | `cancelScan()` | `Stop scanning` |
| `Rescan` | `arrow.clockwise` | state is not `scanning`; **disabled** when `lastScanned == nil` | `rescan()` | `Scan the last folder again (⌘R)` |
| `Home` | `house` | always | scan home folder | `Scan your home folder (⇧⌘H)` |
| `Full Disk` | `internaldrive` | always | scan `/` | `Scan the whole startup disk` |
| `Scan…` | `folder.badge.gearshape` | always | folder picker → scan | `Choose a folder to scan (⌘O)` |

The toolbar is icon-only in practice (macOS shows the label only in "Icon and Text" mode); tooltips carry the text. Note the `…` is a single Unicode ellipsis U+2026 everywhere (`Scan…`, `Choose Folder…`, `Move to Trash…`, `Scan Folder…`).
The `NavigationSplitView` also adds the standard **sidebar toggle** button at the left of the toolbar (visible in screenshot) — only in the browser state.

### 1.4 Start screen (`WelcomeView`, state `idle`)
- Icon: SF Symbol `square.grid.3x3.square`, 64 pt, weight light, accent color.
- Title: `See what’s filling your disk` (large title, semibold). Note the curly apostrophe U+2019.
- Subtitle (secondary color): `DiskU scans a folder and draws every file as a rectangle sized by the space it uses.`
- Buttons (row, spacing 12, large control size, each label min width 150):
  1. `Scan Home Folder` (icon `house`, **prominent/accent** style) → scan home.
  2. `Scan Full Disk` (icon `internaldrive`) → scan `/`.
  3. `Choose Folder…` (icon `folder`) → folder picker → scan.
- Full Disk Access card below (top padding 12), see §1.9.
- Layout: vertical stack spacing 18, padding 40, centered in the window.

### 1.5 Scanning screen (`ScanningView`, state `scanning`)
- Large indeterminate spinner.
- Title: `Scanning <path>` where `<path>` = `lastScanned.path` (e.g. `Scanning /Users/me`). Font title2 semibold.
- Two stats side by side (spacing 28), each a vertical stack (spacing 2): value in 28 pt bold rounded monospaced-digits, caption below in secondary:
  - value = `filesSeen` formatted with locale grouping (e.g. `12,345`), caption `files`
  - value = `Format.bytes(bytesSeen)`, caption `found`
- Current path: `progress.currentPath` in callout monospaced, secondary, 1 line, **middle truncation**, max width 560.
- Button `Cancel` (cancel role) → `cancelScan()`; bound to Escape (`.cancelAction`).
- Stack spacing 16, padding 40, centered.

### 1.6 Menus & keyboard shortcuts
File menu: the "New" group is **replaced** with:
| Item | Shortcut | Enabled | Action |
|---|---|---|---|
| `Scan Folder…` | ⌘O | always | picker → scan |
| `Scan Home Folder` | ⇧⌘H | always | scan home |
| `Scan Full Disk` | none | always | scan `/` |
| `Rescan` | ⌘R | `lastScanned != nil` | rescan |

`Item` menu (new top-level menu):
| Item | Shortcut | Enabled | Action |
|---|---|---|---|
| `Reveal in Finder` | ⇧⌘R | selection != nil | reveal |
| `Quick Look` | ⌘Y | selection != nil | quick look toggle |
| `Copy Path` | ⌥⌘C | selection != nil | copy path |
| `Open` | ⌘↓ | selection != nil | open with default app |
| `Zoom Out` | ⌘↑ | zoomRoot != nil | `zoomRoot = (zoomRoot.parent === root) ? nil : zoomRoot.parent` |
| — separator — | | | |
| `Move to Trash…` | ⌘⌫ (Delete) | selection != nil && selection !== root | `pendingTrash = selection` |

Window-level key handlers (ContentView):
- **Space**: if a selection exists → Quick Look toggle on it (handled); else ignored.
- **Delete** (backspace) or **Forward Delete**: if selection exists and is not the root → `pendingTrash = selection` (handled); else ignored.
- Outline list also has `onDeleteCommand { pendingTrash = selection }` — **QUIRK**: no root check there; if the root row is selected the dialog could appear for the root, but `trash()` silently refuses the root. Port: block root everywhere.

### 1.7 Context menu (`ItemMenu`, identical on outline rows and treemap)
1. `Reveal in Finder`
2. `Quick Look`
3. `Copy Path`
4. `Zoom Into Folder` — only if node is a folder with ≥1 child → `zoom(into: node)`
5. separator
6. `Move to Trash…` (destructive/red) — disabled when node is the scan root → `pendingTrash = node`

On the treemap the menu targets `hovered ?? selection` (the tile under the mouse at right-click time; falls back to selection). If both nil, no menu.

### 1.8 Dialogs
**Trash confirmation** (shown whenever `pendingTrash != nil`; title always visible):
- Title: `Move “<name>” (<size>) to the Trash?` — curly quotes U+201C/U+201D, `<size>` = `Format.bytes(node.size)`.
- Message: `This frees about <size>. You can put it back from the Trash in Finder.` + newline + `<full path>`.
- Buttons: `Move to Trash` (destructive) → `confirmPendingTrash()`; `Cancel` (cancel role) → `pendingTrash = nil`. Dismissing the dialog any other way also clears `pendingTrash`.

**Trash error alert** (shown whenever `errorMessage != nil`):
- Title: `Couldn’t Move to Trash`
- Message: `“<name>” couldn’t be moved to the Trash. <error.localizedDescription>` (curly quotes and apostrophe).
- Button: `OK` → clears `errorMessage`.

**Folder picker** (`NSOpenPanel`): directories only, no files, single selection, packages treated as directories (user can pick inside `.app`), starts at the home folder, confirm button text `Scan`, message `Choose a folder to scan`. Cancel → nothing happens.

### 1.9 Full Disk Access card (start screen only)
State `granted` is computed at creation and re-computed every time the app becomes active (so returning from System Settings updates it).
- If granted: single line label with icon `checkmark.shield.fill`, green: `Full Disk Access is on. DiskU can read every folder without asking.`
- Else a card (padding 18, background = quaternary fill at 50% opacity, corner radius 12), vertical stack spacing 10, centered:
  - Label icon `lock.shield.fill` + `Allow Full Disk Access` (headline font).
  - `Scan your disk without the permission prompts.` (callout).
  - Steps (callout, secondary color, left aligned, max width 310, line-wrapping, spacing 4). Each step = number column `1.` / `2.` (monospaced digits) + text column, spacing 4, first-baseline aligned so wrapped lines indent under the text, not the number. Markdown bold is rendered:
    1. `Click **Open Full Disk Access Settings**.`
    2. `Turn on DiskU. If it isn’t listed, click **Show DiskU in Finder** and drag it into the list.`
  - Buttons row: `Open Full Disk Access Settings` (prominent) → opens `x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles`; `Show DiskU in Finder` → reveals the app bundle in Finder.
- FDA probe: "granted" iff `/Library/Application Support/com.apple.TCC/TCC.db` can be opened for reading.

### 1.10 Full Disk Access banner (browser, top of center pane, only when `inaccessible > 0`)
- Icon `lock.shield.fill`, orange, title3 size.
- Line 1 (semibold): `<count> folders couldn’t be read` / `1 folder couldn’t be read` (count with locale grouping; singular only when exactly 1).
- Line 2 (secondary): `Sizes are incomplete. Grant DiskU Full Disk Access in System Settings, then rescan.`
- Spacer, then button `Open Privacy Settings` → same settings URL as above.
- Font callout; HStack spacing 10; text VStack spacing 1; padding horizontal 12, vertical 8; background orange at 12% opacity.

### 1.11 Update banner (top of the whole window content, every state)
Visible iff `updates.available != nil && !updates.dismissed`.
- Icon `arrow.down.circle.fill` in accent color.
- Text (medium weight): `DiskU <version> is available.` where version = parsed version's description, e.g. `DiskU 1.0.5 is available.` (no leading v).
- Spacer, button `View Release` → opens the release `html_url` in the browser.
- Borderless button with icon `xmark`, tooltip `Dismiss` → `dismissed = true` (in memory only; reappears next launch).
- Font callout, spacing 10, padding h 12 / v 6, background accent at 12% opacity.

### 1.12 Failure screen (state `failed(message)`)
"Content unavailable" layout: icon `exclamationmark.triangle`, title `Scan Failed`, description = the error message, action button `Try Again` → `rescan()`.
The only realistic failure is scanning a path that is not a directory (Cocoa "file read invalid file name" error) or a path whose attributes can't be read. Unreadable subfolders do **not** fail the scan.

### 1.13 Inspector strings
- Empty state: icon `cursorarrow.click.2` (28 pt, tertiary) and `Select an item to inspect it` (secondary), centered, vertical padding 24, stack spacing 6.
- Detail rows (label column trailing-aligned, secondary):
  - `Contains` → `Format.items(itemCount)` — only for kind folder or bundle.
  - `Share` → `<pct> of <parent name>` e.g. `38.5% of photos` — only when parent exists and parent.size > 0. pct = size/parent.size formatted as percent with exactly 1 fraction digit (locale percent style).
  - `Modified` → content modification date, date style "abbreviated" + time "shortened" (en-US e.g. `Oct 14, 2026 at 3:05 PM`) — only if readable. Read live from disk each render.
  - `Where` → parent directory path (`url.deletingLastPathComponent().path`), e.g. `/Users/me/Movies`.
  Values: selectable text, up to 3 lines, middle truncation.
- Action buttons: `Reveal` (icon `folder`), `Quick Look` (`eye`), `Open` (`arrow.up.forward.app`), `Copy Path` (`doc.on.doc`) → after click shows `Copied` (`checkmark`) for 1.5 s, reset also when the selected node changes; tooltip on Copy Path = the full path. `Move to Trash…` (`trash`).
- Legend header: `By Kind`.

### 1.14 Outline strings
- Show-more row: `<N> smaller items` (N grouped), second line `<Format.bytes(sum of hidden sizes)>`, button `Show more` (link style), tooltip `Show the next <min(N, 500)>` (grouped).

### 1.15 Status bar strings
- Nothing hovered/selected: left (secondary) `Click to select · Double-click a folder to zoom in · Space to Quick Look` (U+00B7 middle dots); right `<Format.items(displayRoot.itemCount)> · <Format.bytes(displayRoot.size)>` e.g. `33 files · 13.17 GB`.
- Hovered or selected node: dot + full path + size (see §2.6).

### 1.16 Formatting helpers (exact)
- `Format.bytes(n)`: Apple `ByteCountFormatter`, `countStyle = .file`, `allowsNonnumericFormatting = false`. Meaning:
  - **Decimal units (1 KB = 1000 bytes)**, not 1024.
  - Adaptive precision: bytes and KB with 0 fraction digits, MB with 1, GB and larger with 2. Trailing zero fractions are dropped (`44 MB`, not `44.0 MB`; `2 GB`). Examples from the app: `13.17 GB`, `996.1 MB`, `471.9 MB`, `44 MB`, `20 KB` (20480 bytes), `107 MB` (106,958,848 bytes), `83.65 GB`.
  - 0 → `0 bytes` (not "Zero KB"); 1 → `1 byte`; 999 → `999 bytes`; 1,000 → `1 KB`.
  - Units: `bytes`, `KB`, `MB`, `GB`, `TB`, `PB`. Space between number and unit. Locale decimal separator.
  - PORT: do **not** use `StrFormatByteSize` (1024-based). Implement: pick unit by magnitude with base 1000; round to the unit's precision (KB:0, MB:1, GB+:2) with round-half-even/away (Apple uses standard rounding); strip trailing zeros. Edge case where rounding reaches 1000 of a unit (e.g. 999,999 B) is ambiguous; Apple shows `1 MB`. Recommend: if rounded value ≥ 1000 bump to next unit.
- `Format.items(n)`: `1 file` when n == 1, else `<n grouped> files` (e.g. `0 files`, `1,234 files`). Note it says "files" even for folders (itemCount counts files + symlinks only).
- Integer grouping uses the current locale (`count.formatted()`).

### 1.17 Icons
- The Mac uses Finder icons (`NSWorkspace.icon(forFile:)`) for: breadcrumbs (14×14), inspector (64×64), and outline rows of bundles, of the root, and of the root's direct children. All other outline rows share one icon per extension (or one generic folder icon) for speed (`Icons.outlineIcon`). Icons are cached by path / extension.
- PORT: `SHGetFileInfo` with `SHGFI_ICON|SHGFI_SMALLICON` on the real path for the "real icon" cases, and `SHGFI_USEFILEATTRIBUTES` + extension (`"*.ext"` / `FILE_ATTRIBUTE_DIRECTORY`) for shared ones; cache by key `"/folder"` or lowercase extension.
- SF Symbols used: see tables above. Suggested Segoe Fluent Icons equivalents: house→Home (E80F), internaldrive→HardDrive (EDA2), folder.badge.gearshape→FolderOpen/OpenFolderHorizontal (ED25), arrow.clockwise→Refresh (E72C), xmark.circle→Cancel (E711), xmark→ChromeClose (E8BB), lock.shield.fill→Shield (EA18)/Lock (E72E), checkmark.shield.fill→Shield + accent, arrow.down.circle.fill→Download (E896), exclamationmark.triangle→Warning (E7BA), cursorarrow.click.2→TouchPointer (E7C9), folder→Folder (E8B7), eye→View (E890), arrow.up.forward.app→OpenInNewWindow (E8A7), doc.on.doc→Copy (E8C8), checkmark→CheckMark (E73E), trash→Delete (E74D), ellipsis.circle→More (E712), chevron.right→ChevronRight (E76C), square.grid.3x3.square→ViewAll (E8A9)/GridView (F0E2).

---

## 2. Window layout and visuals

### 2.1 Top-level structure
```
Window "DiskU" (min 900×560, default 1200×800)
 ├─ Toolbar (title bar): [sidebar toggle (browser only)] ... [Cancel|Rescan] [Home] [Full Disk] [Scan…]
 └─ Content
     ├─ UpdateBanner (top inset, 0 spacing; only when update available & not dismissed)
     └─ switch state:
          idle      → WelcomeView
          scanning  → ScanningView
          failed    → FailureView
          done      → BrowserView(root, inaccessible)
```
Content views follow the system light/dark theme, except the treemap canvas which is always dark (it paints its own fills on a near-black background).

### 2.2 BrowserView (3 columns)
- Left: **sidebar** = OutlinePane. Column width min 240, ideal 300, max 480. Collapsible via the system sidebar toggle. Sidebar material (translucent "sidebar" list style).
- Center: **detail** = TreemapPane (flexible).
- Right: **inspector** = InspectorView, width min 250, ideal 290, max 400. Always presented (`showInspector = true`, no UI toggles it off).
- PORT: a 3-column `Grid` with `GridSplitter`s (CommunityToolkit) or `NavigationView` (Left, pane resizable) + right `SplitView`; respect the min/ideal/max widths.

### 2.3 TreemapPane (vertical stack, spacing 0)
1. `FullDiskAccessBanner` (if inaccessible > 0) — §1.10.
2. `Breadcrumbs`.
3. Divider (1 px separator).
4. `TreemapView`, flexible, background `Color(white: 0.08)` = **#141414**.
5. Divider.
6. `StatusBar` (height 26).

### 2.4 Breadcrumbs
- Trail = `[root, ..., displayRoot]` (root first). `displayRoot = zoomRoot ?? root`.
- Horizontal scroll view without scroll indicators. HStack spacing 2, font **callout** (macOS callout = 12 pt; body = 13 pt), padding horizontal 10 / vertical 6.
- Between crumbs: `chevron.right` glyph, caption2 (10 pt) semibold, tertiary color.
- Each crumb: HStack spacing 4 of real Finder icon 14×14 + name; padding h 6 / v 3; background = primary color at 12% opacity in a rounded rect radius 5 **only for the current displayRoot crumb**, else clear; whole crumb is clickable: `zoomRoot = (node === root) ? nil : node`.
- When zoomed (`zoomRoot != nil`), after the last crumb: `Format.bytes(zoomRoot.size)` in secondary color, leading padding 8.

### 2.5 OutlinePane (left)
- A selectable `List` in sidebar style, single selection bound to `store.selection`. Contents: one top-level row = the scan root (expanded right after a scan finishes).
- Folders with ≥1 child are disclosure groups (chevron), expansion bound to `store.expanded` (a set of nodes). Files, bundles, symlinks and empty folders have no chevron.
- Children shown = first `outlineLimit(folder)` children (already sorted largest first). If `children.count > limit` a **ShowMoreRow** follows the visible children.
- Whenever `selection` changes (from any source — treemap click, menu, etc.) the list scrolls to that row with an ease-in-out 0.2 s animation.
- Row (`OutlineRow`):
  ```
  HStack(spacing 6):
    icon 16×16
    VStack(alignment leading, spacing 3):
      HStack: name (1 line, middle truncation) · Spacer(min 6) · size (callout, monospaced digits, secondary)
      ProportionBar(fraction, kind color)   // height 3
  vertical padding 1
  context menu = ItemMenu(node)
  ```
  fraction = node.size / parent.size; for the root (no parent) or parent.size == 0 → 1.
- **ProportionBar** (drawn, no layout measuring): height 3, full row width. Track: rounded rect (corner radius = height/2 = 1.5), fill = primary color at 10% opacity. Bar: x=0, width = `max(2, width * fraction)`, same height and corner radius, fill = kind color. Kind color for folders = folder gray #6B7380 (that's why folder rows have gray bars in the screenshot).
- **ShowMoreRow**:
  ```
  HStack(spacing 6):
    SF ellipsis.circle in a 16-wide frame
    VStack(leading, spacing 1) in secondary color:
      "<N> smaller items"
      <bytes of hidden> (caption font, monospaced digits)
    Spacer(min 6)
    Button "Show more" (link style) tooltip "Show the next <min(N,500)>"
  font callout
  ```
  Clicking → `store.showMore(in: folder)` (limit += 500). The row itself is not a selectable item.
- Performance invariant to keep: never materialise more than `limit` rows per folder; share icons; no per-row measuring. (WinUI: use a virtualized `TreeView` or, better, a flattened virtualized `ListView` of visible rows with indentation, regenerated on expand/collapse/revision.)

### 2.6 StatusBar
HStack spacing 8, font callout, padding horizontal 10, height 26.
- If `hovered ?? selection` != nil: Circle 8×8 filled with node.kind.color · full path (1 line, **middle** truncation) · Spacer(min 12) · `Format.bytes(size)` (monospaced digits).
- Else if displayRoot exists: hint text (secondary) · Spacer · `<items> · <bytes>` of displayRoot (monospaced digits).

### 2.7 Inspector (right)
ScrollView → VStack(leading, spacing 18), padding 16:
1. Item details (if selection) or the empty state (§1.13).
2. Divider.
3. KindLegend.

**ItemDetails** VStack(leading, spacing 14):
- Header HStack(center, spacing 12): Finder icon 64×64; VStack(leading, spacing 4): name (title3 = 15 pt, semibold, max 2 lines, middle truncation); Label{kind.label} with icon = 8×8 circle of kind color, callout, secondary.
- Size: `Format.bytes(size)` in **30 pt bold rounded, monospaced digits**.
- Grid (leading, h-spacing 10, v-spacing 6), callout: rows from §1.13 in order Contains, Share, Modified, Where.
- Actions VStack(spacing 8):
  - 2×2 grid (spacing 8 both ways), bordered buttons, regular size, each label stretched to equal width (`maxWidth: .infinity`): row 1 `Reveal`, `Quick Look`; row 2 `Open`, `Copy Path`/`Copied`.
  - Full-width `Move to Trash…` (icon trash): prominent bordered, **red tint**, large control size, disabled when node is the root. Action: `pendingTrash = node`.

**KindLegend**: totals = `store.kindTotals` (whole scan, not zoom). `largest = totals.first.bytes ?? 1`.
```
VStack(leading, spacing 8):
  "By Kind" (headline = 13 pt bold)
  for each (kind, bytes):
    VStack(leading, spacing 3), font callout:
      HStack: RoundedRect(radius 3) 12×12 kind color · kind.label (1 line) · Spacer · Format.bytes(bytes) (monospaced, secondary)
      ProportionBar(fraction = bytes/largest, kind color)
```

### 2.8 Treemap view — interaction
- The layout is computed for the view's current size (points) and the current displayRoot; cached on (displayRoot identity, width, height, revision).
- Two layers: (a) the expensive **TreemapCanvas** (all tiles, redrawn only when layout/revision/root changes; draws asynchronously), (b) the cheap **HighlightCanvas** overlay (hover + selection outlines), non hit-testable.
- **Hover** (continuous): `hovered = layout.tile(at: point)?.node`; on mouse exit `hovered = nil`.
- **Single click**: `selection = layout.tile(at: location)?.node` (always non-nil inside bounds because the root folder tile covers everything). Clicking a folder header, folder border/inset margin, or a remainder tile selects that folder.
- **Double click** (recognised simultaneously with single click, so the first click also selects): `if let tile = tile(at:) { zoom(into: tile.node) }`. `zoom` zooms into the node if it is a folder with children, else into its parent (so double-clicking a file zooms to its containing folder; double-clicking a bundle zooms into the bundle's parent folder).
- **Right click**: context menu `ItemMenu(hovered ?? selection)`.
- `tile(at:)` returns the **last** tile in paint order containing the point (deepest). Point containment = CGRect.contains: minX ≤ x < maxX and minY ≤ y < maxY.
- `rect(of: node)` = rect of the **first** non-remainder tile whose node is `node`. If the node isn't in the current layout (too small, outside the zoom), no highlight is drawn.

### 2.9 Treemap rendering — exact
Coordinate system: origin top-left, y down, units = DIPs (points). All fills are opaque unless noted.

**Palette** (SwiftUI `Color(white:)` / sRGB):
| Name | Value | Hex |
|---|---|---|
| canvas background | white 0.08 | #141414 |
| emptyFolder (leaf folder with no children / zero size) | white 0.35 | #595959 |
| remainder | white 0.30 | #4D4D4D |
| folderFill(depth) | white `max(0.10, 0.20 - depth*0.02)` | d0 #333333, d1 #2E2E2E, d2 #292929, d3 #242424, d4 #1F1F1F, d≥5 #1A1A1A |
| headerFill(depth) | white `max(0.16, 0.28 - depth*0.025)` | d0 #474747, d1 #414141, d2 #3B3B3B, d3 #343434, d4 #2E2E2E, d≥5 #292929 |

**Cushion gradient per kind** (3 stops; `blended(with:fraction)` is linear RGB interpolation `c*(1-f) + other*f` in sRGB components):
- stop 0.00: color blended 35% toward white  (`c + (1-c)*0.35`)
- stop 0.45: color
- stop 1.00: color blended 45% toward black  (`c * 0.55`)

| Kind | base (stop .45) | stop 0 (highlight) | stop 1 (shadow) |
|---|---|---|---|
| folder | #6B7380 | #9FA4AC | #3B3F46 |
| bundle | #9E66DB | #C09CE8 | #573879 |
| image | #33B373 | #7ACDA4 | #1C623F |
| video | #E64D4D | #EE8B8B | #7E2A2A |
| audio | #F29933 | #F7BD7A | #85541C |
| archive | #D9BF40 | #E6D683 | #776923 |
| code | #40A6D9 | #83C5E6 | #235B77 |
| document | #4D73E6 | #8BA4EE | #2A3F7E |
| other | #8C949E | #B4B9C0 | #4D5157 |
| emptyFolder (empty cushion) | #595959 | #939393 | #313131 |

The gradient runs **linearly from the tile's top-left corner point (minX,minY) to its bottom-right corner point (maxX,maxY)** in absolute coordinates (iso-color lines perpendicular to that diagonal vector). PORT: Win2D `CanvasLinearGradientBrush` with StartPoint/EndPoint in absolute coords, or XAML `LinearGradientBrush` with `MappingMode="Absolute"`. Do not use relative (0,0)-(1,1) mapping — that skews the iso-lines on non-square tiles.

**Per tile, in paint order** (layout order, §5):
1. **folder** tile: fill whole rect with `folderFill(depth)`. If it has a header rect: fill header with `headerFill(depth)` and draw a **header label** in `header.insetBy(dx: 4, dy: 0)`: title = name, 10.5 pt **semibold**; detail = `Format.bytes(size)`, 9.5 pt regular, monospaced digits.
2. **leaf** tile: kind = node.kind unless the node is a folder (empty folder or zero-size folder), in which case "no kind" (→ emptyFolder colors).
   - If rect.width < 3 **or** rect.height < 3: solid fill with kind color (or #595959), no stroke.
   - Else: fill with the cushion gradient; then stroke the rect inset by 0.25 with black at 35% opacity, line width 0.5.
   - If rect.width > 56 **and** rect.height > 30: draw a **stacked label** inside `rect.insetBy(dx: 5, dy: 4)`: title = name 11 pt **medium**, detail = `Format.bytes(size)` 10 pt regular monospaced digits.
3. **remainder** tile: solid fill #4D4D4D. No label, no stroke.

**Label drawing** (both kinds):
- Clip to the label rect (hard clip, **no ellipsis**; text is drawn single-line and cut off — visible as "Demo walkthrough.mo", "Brand Kit.s" in the screenshot).
- Title color white; detail color white at 80% opacity.
- Drop shadow on the whole label layer: black at 60% opacity, blur radius 1, offset (0, +0.5).
- Font: system font (SF Pro). PORT: Segoe UI Variable Text (or Segoe UI) with tabular figures for the detail.
- Stacked (leaf): title top-left at (minX, minY) of the inset rect; detail top-left at (minX, minY + size + 3) = 14 pt below the title's top, drawn only if the inset rect height > 30 (i.e. original tile height > 38).
- Inline (folder header): title left-aligned at (minX, midY), vertically centered; detail right-aligned at (maxX, midY) vertically centered, drawn only if `titleWidth + 60 < rect.width` (titleWidth = measured single-line width of the title).

**Highlight overlay** (drawn after everything):
- Hover: only if hovered !== selection and rect(of: hovered) exists: stroke rect inset 0.5, white at 70% opacity, width 1.
- Selection: if rect(of: selection) exists: stroke rect inset 1.5 with black 60% width 3, then stroke the same rect with opaque white width 1.5.

**Visual facts implied by the layout** (§5): each folder is drawn as a dark frame with a 2-pt border (inset) around its contents, plus a 15-pt header strip when big enough; deeper folders get progressively darker frames; tiny children are aggregated into one gray remainder tile at the end of their folder; the root itself is a folder tile at depth 0 covering the full canvas, so the canvas always shows a top header "Home ... 13.17 GB".

---

## 3. Model

### 3.1 FileNode
Reference type, identity semantics (equality and hash by object identity, never by path).
| Field | Type | Notes |
|---|---|---|
| `url` | URL (absolute path) | immutable. (PORT: store full path string or, better per known gap, name + parent chain.) |
| `name` | String | `url.lastPathComponent`, or the full path if that is empty. For `/` it is `/`. (PORT: for `C:\` use `C:\`.) |
| `kind` | Kind | immutable |
| `size` | Int64 | allocated bytes; for folders = sum of children; private set |
| `itemCount` | Int | number of regular files + symlinks in the subtree; a file counts itself as 1, a bundle counts its inner files, a folder node itself counts 0 |
| `children` | [FileNode] | always sorted **largerFirst**; private set |
| `parent` | weak FileNode? | private set |

Derived:
- `isFolder` = `kind == .folder` (bundles are **not** folders).
- `outlineChildren` = children if folder and non-empty, else nil (no chevron).
- `ancestors` = [parent, grandparent, …, root] (nearest first).
- `isDescendant(of: other)` = `self === other || ancestors contains other` (note: a node is its own descendant).

Ordering `largerFirst(a, b)`: `a.size != b.size ? a.size > b.size : a.name < b.name`. **The name tiebreak is Swift `String <`, which is Unicode-scalar (ordinal-like) ordering: uppercase before lowercase** ("Tool.app" < "photos", "b.jpg" < "deep"). PORT: use `string.CompareOrdinal`, not culture compare. Tests depend on this.

Operations:
```
adopt(newChildren):
    addedSize = 0; addedItems = 0
    for c in newChildren: c.parent = self; addedSize += c.size; addedItems += c.itemCount
    children.append(newChildren); children.sort(largerFirst)
    propagate(addedSize, addedItems)

removeFromParent():
    if parent == nil: return
    parent.children.removeAll(identical to self)
    parent.propagate(-size, -itemCount)
    parent = nil          // own size/itemCount are kept

propagate(delta, itemDelta):      // THE ONLY place sizes change
    n = self
    while n != nil:
        n.size += delta; n.itemCount += itemDelta
        if n.parent != nil and delta != 0: n.parent.reposition(n)
        n = n.parent

reposition(child):   // keeps sibling list sorted after child's size changed
    i = index of child (identity); if none return
    remove at i
    insertAt = first index j where largerFirst(child, children[j]) else end
    insert child at insertAt
```
Invariants: folder size == Σ children sizes; folder itemCount == Σ children itemCounts; every children list sorted largerFirst. Never assign size directly.

### 3.2 Kind
Enum, declaration order (= `CaseIterable` order): `folder, bundle, image, video, audio, archive, code, document, other`.

| Kind | label (exact) | color (sRGB 0–1) | hex |
|---|---|---|---|
| folder | `Folders` | (0.42, 0.45, 0.50) | #6B7380 |
| bundle | `Apps & Bundles` | (0.62, 0.40, 0.86) | #9E66DB |
| image | `Images` | (0.20, 0.70, 0.45) | #33B373 |
| video | `Video` | (0.90, 0.30, 0.30) | #E64D4D |
| audio | `Audio` | (0.95, 0.60, 0.20) | #F29933 |
| archive | `Archives & Disk Images` | (0.85, 0.75, 0.25) | #D9BF40 |
| code | `Code & Data` | (0.25, 0.65, 0.85) | #40A6D9 |
| document | `Documents` | (0.30, 0.45, 0.90) | #4D73E6 |
| other | `System & Other` | (0.55, 0.58, 0.62) | #8C949E |

(hex = round(x*255); image green 0.70*255 = 178.5 → B3.)

`Kind.forFile(named:)`: ext = `(name as NSString).pathExtension.lowercased()`; return table[ext] ?? `.other`. pathExtension = text after the last `.` of the last path component, `""` if no dot. (Edge: Cocoa treats `.bashrc` as having no extension in practice and `file.` → `""`; PORT: take substring after last '.', lowercase invariant, empty if no dot or dot is first char — none of those collide with the table either way.) `x.tar.gz` → `gz` → archive. Folders are assigned `.folder`, packages `.bundle`, symlinks `.other` by the scanner, not by this table.

Extension table (verbatim):
- **image**: `jpg, jpeg, png, gif, heic, heif, tif, tiff, bmp, webp, raw, cr2, nef, arw, dng, psd, svg, ico, icns`
- **video**: `mov, mp4, m4v, avi, mkv, wmv, flv, webm, mpg, mpeg, 3gp`
- **audio**: `mp3, m4a, aac, wav, aiff, aif, flac, ogg, caf, alac`
- **archive**: `zip, gz, tgz, bz2, xz, 7z, rar, tar, dmg, iso, pkg, sparseimage, sparsebundle, zst, xip`
- **code**: `swift, m, h, c, cc, cpp, hpp, rs, go, py, js, mjs, ts, tsx, jsx, java, kt, rb, php, sh, json, yaml, yml, toml, xml, plist, sql, sqlite, db, html, css, o, a, dylib, so, wasm, jar, class, pyc`
- **document**: `pdf, doc, docx, xls, xlsx, ppt, pptx, pages, numbers, key, txt, md, rtf, csv, epub, odt`
No extension appears in two groups.

PORT DECISION: Windows-common types (`exe, dll, msi, sys, vhd, vhdx, cab, lnk, ps1, bat, cs, …`) fall into "System & Other" with the verbatim table. Keep verbatim for parity, or extend (only in this one table).

---

## 4. Scanner

### 4.1 API
```
ScanProgress { filesSeen: Int = 0, bytesSeen: Int64 = 0, currentPath: String = "" }   // value type, equatable
ScanState    = idle | scanning(progress) | done(root: FileNode, inaccessible: Int) | failed(message: String)
ScanResult   { root: FileNode, inaccessible: Int, elapsed: Duration }
Scanner.scan(url, onProgress: @MainActor (ScanProgress) -> Void = no-op) async throws -> ScanResult
```
Guarantees: `onProgress` runs on the main thread, at most ~10×/s, and **never after `scan` returns or throws**. One final `onProgress(final counts)` call is made right before returning successfully.

### 4.2 Setup (`Walker.init`)
1. `root = url.standardizedFileURL` (removes `.`/`..`, no symlink resolution).
2. Read `isDirectory` and `volumeIdentifier` of root; if not a directory → throw (Cocoa `fileReadInvalidFileName` with the path) → store state `failed(localizedDescription)`. If attributes can't be read at all → throws too.
3. `allowedVolumes = [root's volume id]`.
4. If `root.path == "/"`: also allow the volume id of `/System/Volumes/Data` (macOS user data lives there, reached through firmlinks), and `skippedPaths = {"/System/Volumes", "/Volumes", "/.nofollow", "/.resolve", "/.vol", "/dev"}`. Otherwise `skippedPaths = {}`.
5. Counters (under a lock): progress + `inaccessible` count.

### 4.3 Per-directory listing (`entries(of: dir)`)
```
contents = list dir (non-recursive, INCLUDING hidden files), prefetching
           isDirectory, isSymbolicLink, isPackage, totalFileAllocatedSize, fileAllocatedSize, volumeIdentifier
on failure: inaccessible += 1; return ([], [])        // the folder still appears, empty, size 0
leaves = []; subdirs = []; bytes = 0; files = 0
for child in contents:
    values = read attributes; on failure: skip child silently
    if child is symlink:
        leaves += FileNode(child, kind .other, size = allocated(values) /* of the link itself */, itemCount 1)
        files += 1                      // QUIRK: link size not added to bytesSeen (it is ~0 anyway)
    elif child is directory:
        if skippedPaths non-empty and trimTrailingSlash(child.path) in skippedPaths: continue   // not shown at all
        if volume id present and not in allowedVolumes: continue                                // mount points: not shown at all
        if child is package:
            (size, count) = packageTotals(child)
            leaves += FileNode(child, kind .bundle, size, itemCount count)
            bytes += size; files += count
        else:
            subdirs += child
    else:   // regular file, or any other non-directory (fifo, socket, device)
        size = allocated(values)
        leaves += FileNode(child, Kind.forFile(child.name), size, itemCount 1)
        bytes += size; files += 1
lock: progress.filesSeen += files; progress.bytesSeen += bytes; progress.currentPath = dir.path
return (leaves, subdirs)
```
`allocated(values) = totalFileAllocatedSize ?? fileAllocatedSize ?? 0` → **allocated bytes on disk** (block-rounded; includes extended attributes/resource fork for "total"). Matches `du`, not the Finder's logical size.

`trimTrailingSlash`: if path length > 1 and ends with `/`, drop the slash (Foundation reports `/.nofollow/`).

**packageTotals(pkg)**: recursive enumeration of everything inside (hidden included, symlinks not followed, errors ignored and enumeration continues); for each entry that is **not** a directory: size += allocated, count += 1. Volume/skip rules are not re-checked inside packages. What is a "package" is decided by macOS (directories with a registered bundle extension or the package bit: `.app`, `.photoslibrary`, `.logicx`, `.bundle`, `.framework`, `.rtfd`, …). Packages appear as one opaque leaf (kind bundle, no children).

### 4.4 Recursion & concurrency
```
parallelDepth = 3
scanParallel(dir, depth):
    if depth >= 3: return scanSync(dir)
    throw if cancelled
    (leaves, subdirs) = entries(dir)
    children = leaves + [await scanParallel(s, depth+1) for s in subdirs, run CONCURRENTLY in a task group]
    node = FileNode(dir, kind .folder); node.adopt(children); return node
scanSync(dir):
    throw if cancelled
    (children, subdirs) = entries(dir)
    for s in subdirs: children += scanSync(s)     // sequential, same task
    node = FileNode(dir, .folder); node.adopt(children); return node
```
So directories at depth 0, 1, 2 fan out their subdirectories into parallel tasks; from depth 3 down everything is synchronous inside that task. Each subtree is built by exactly one task and handed up only when complete (no shared mutation). Order of arrival doesn't matter because `adopt` sorts.
PORT: `Parallel.ForEachAsync`/`Task.WhenAll` for depth < 3, plain recursion below; use a lock or `Interlocked` for counters. Consider `NtQueryDirectoryFile` / `FindFirstFileExW(FindExInfoBasic, FIND_FIRST_EX_LARGE_FETCH)` for speed.

### 4.5 Progress ticker
A side task loops: sleep 100 ms → `onProgress(snapshot)` on main thread; until cancelled. After the walk finishes (or throws), the ticker is cancelled **and awaited** (so no callback can land later), then on success one final `onProgress(snapshot)` is delivered. currentPath = the last directory whose listing completed (from any task).

### 4.6 Cancellation
Cooperative: checked at the entry of every directory (`Task.checkCancellation()` throws `CancellationError`). Cancelling the scan task propagates to child tasks. `Scanner.scan` rethrows; `DiskStore` maps `CancellationError` → `idle` (but in practice `cancelScan()` has already bumped the generation so the result is dropped).

### 4.7 What is / isn't counted (summary)
- Allocated size, not logical. Sparse/compressed/cloned files report allocated blocks; APFS clones and snapshots not deduplicated.
- Hard links: counted once **per link** (overcount).
- Symlinks: listed as leaves (kind other, size of the link ≈ 0, itemCount 1) and **never followed** (fixture has `loop -> ../..`).
- Never crosses onto another volume (mounted dirs are omitted entirely). Exception: scanning `/` also accepts the Data volume.
- Hidden files included.
- Unreadable directories: counted in `inaccessible`, kept as empty folder nodes; scan continues. Unreadable file attributes: entry silently skipped.
- Empty folders: folder nodes with size 0, itemCount 0.
- Directory entries' own metadata blocks are not counted (only files' allocations), so totals are a bit below `du`.

---

## 5. Squarify & TreemapLayout

### 5.1 `squarify(sizes, rect) -> [Rect]` (Bruls, Huizing, van Wijk 2000)
Precondition: `sizes` sorted descending. Output: one rect per input in the same order; tiles `rect` exactly when all sizes > 0.
```
total = sum(sizes)
if total <= 0 or rect.w <= 0 or rect.h <= 0: return [Rect.zero] * len(sizes)
scale = rect.w * rect.h / total
areas = sizes * scale
result = []; free = rect; start = 0
while start < len(areas):
    side = min(free.w, free.h)
    end = start + 1
    rowSum = areas[start]
    best = worst(areas[start:end], rowSum, side)
    while end < len(areas):
        candSum = rowSum + areas[end]
        cand = worst(areas[start:end+1], candSum, side)
        if cand > best: break          // equal ratio → keep adding
        best = cand; rowSum = candSum; end += 1
    free = layoutRow(areas[start:end], rowSum, free, result)
    start = end
return result

worst(row, sum, side):
    if row empty or sum <= 0 or min(row) <= 0: return +inf
    s2 = side*side; sum2 = sum*sum
    return max(s2 * max(row) / sum2, sum2 / (s2 * min(row)))

layoutRow(row, sum, free, result):
    if free.w >= free.h:                      // lay a COLUMN on the left, top to bottom
        colW = sum / free.h
        y = free.minY
        for a in row: h = a / colW; result += Rect(free.minX, y, colW, h); y += h
        return Rect(free.minX + colW, free.minY, max(0, free.w - colW), free.h)
    else:                                     // lay a ROW on the top, left to right
        rowH = sum / free.w
        x = free.minX
        for a in row: w = a / rowH; result += Rect(x, free.minY, w, rowH); x += w
        return Rect(free.minX, free.minY + rowH, free.w, max(0, free.h - rowH))
```
All math in double. (y grows downward.) A zero area makes `worst` = +inf, so a zero after positive entries closes the current row and ends up alone, with a zero-height/width rect.

### 5.2 TreemapLayout
Constants: `headerHeight = 15`, `inset = 2`, `minArea = 1` (square points).
```
TreemapTile { node, rect, depth, role: folder(header: Rect?) | leaf | remainder }
TreemapLayout(root, bounds): tiles = []; place(root, bounds, depth 0)

place(node, rect, depth):
    if rect.w < 1 or rect.h < 1: return                         // dropped entirely, no tile
    if not node.isFolder or node.children empty or node.size <= 0:
        tiles += (node, rect, depth, leaf); return
    content = rect; header = nil
    if rect.w > 8 and rect.h > 8:                               // 4*inset
        content = rect inset by 2 on every side
        if content.h > 37.5 and content.w > 80:                 // headerHeight*2.5
            header = Rect(content.minX, content.minY, content.w, 15)
            content = Rect(content.minX, content.minY + 15, content.w, content.h - 15)
    tiles += (node, rect, depth, folder(header))                // folder tile = FULL rect
    scale = content.w * content.h / node.size
    visible = longest PREFIX of node.children with child.size * scale >= 1   // children are size-desc, so prefix == all big ones
    sizes = visible.sizes
    rest = node.size - sum(sizes)
    if rest > 0: sizes.append(rest)
    rects = squarify(sizes, content)
    for (child, r) in zip(visible, rects): place(child, r, depth + 1)
    if rest > 0 and rects.last.w >= 1 and rects.last.h >= 1:
        tiles += (node /* the folder */, rects.last, depth + 1, remainder)
```
- Paint order = tiles order (pre-order: folder tile, then its contents recursively, then its remainder after all its children).
- `tile(at: p)` = last tile whose rect contains p. `rect(of: n)` = rect of first non-remainder tile with node === n.
- Depth is relative to the displayRoot (zoom root is depth 0).
- Note children of size 0 are never visible and contribute 0 to `rest`.
- Bundles are leaves (not folders), so they are drawn as one purple cushion.
- Layout uses `node.children` order (sorted), no other sorting.

---

## 6. DiskStore

Main-thread observable object. One instance per window/app.

### 6.1 State
| Property | Type | Default | Notes |
|---|---|---|---|
| `state` | ScanState | `.idle` | read-only outside |
| `revision` | Int | 0 | bumped on every tree mutation (finish, trash) |
| `selection` | FileNode? | nil | setter side-effect: `expandAncestors(of: selection)` when non-nil |
| `zoomRoot` | FileNode? | nil | nil means "the root" |
| `hovered` | FileNode? | nil | |
| `expanded` | Set<FileNode> | {} | outline expansion |
| `outlineLimits` | [FileNode: Int] | {} | read-only outside |
| `outlinePage` | static 100 | | |
| `outlineMorePage` | static 500 | | |
| `lastScanned` | URL? | nil | read-only outside; NOT cleared by cancel |
| `pendingTrash` | FileNode? | nil | drives the confirmation dialog |
| `errorMessage` | String? | nil | drives the error alert |
| private `scanTask`, `scanGeneration` (Int, 0), `trasher: (URL) throws -> Void`, `layoutCache`, `kindTotalsCache` | | | |

Constructor: `DiskStore(trasher:)`, default trasher = `FileManager.trashItem(at:)` (move to Trash). Tests inject a recording/throwing trasher.

Derived: `root` = the root in `.done`, else nil. `displayRoot = zoomRoot ?? root`.

### 6.2 scan(url)
```
scanTask?.cancel()
lastScanned = url
selection = nil; zoomRoot = nil; hovered = nil; expanded = {}; outlineLimits = {}
state = scanning(ScanProgress())
scanGeneration += 1; gen = scanGeneration
scanTask = async on main:
    try:
        result = await Scanner.scan(url, onProgress: p => if scanGeneration == gen: state = scanning(p))
        if scanGeneration == gen: finish(result)
    catch e:
        if scanGeneration != gen: return
        state = (e is CancellationError) ? idle : failed(e.localizedDescription)
```
`finish(result)`: `state = done(root, inaccessible)`; `expanded = {root}`; `revision += 1`. (Also used directly by tools/tests.)
`cancelScan()`: cancel task; task = nil; `scanGeneration += 1`; `state = idle` (→ back to the Welcome screen; any previous scan's result is gone).
`rescan()`: if lastScanned != nil → scan(lastScanned).
Generation rule: any result, progress or error from a scan whose generation ≠ current is dropped. A replaced or cancelled scan must not write state.

Entry points: toolbar Home / Full Disk / Scan…; Welcome buttons; File menu; Failure "Try Again"; launch argument (first argv entry not starting with `-`, `~` expanded, that is an existing directory).
- Home = `FileManager.homeDirectoryForCurrentUser`.
- Full Disk = `URL(fileURLWithPath: "/")`.

### 6.3 zoom(into: node)
```
folder = node.isFolder ? node : node.parent
if folder == nil or folder.children empty: return
zoomRoot = (folder === root) ? nil : folder
```
Zoom Out (menu ⌘↑): `zoomRoot = (zoomRoot.parent === root) ? nil : zoomRoot.parent`. Breadcrumb click: `zoomRoot = node === root ? nil : node`. Zooming does not change selection.

### 6.4 Outline paging
```
outlineLimit(folder) = outlineLimits[folder] ?? 100
showMore(folder): outlineLimits[folder] = outlineLimit(folder) + 500
expandAncestors(node):          // runs whenever selection is set to non-nil
    expanded ∪= node.ancestors (only insert missing ones)
    child = node
    while parent = child.parent:
        idx = index of child in parent.children (identity)
        if idx >= outlineLimit(parent): outlineLimits[parent] = idx + 1
        child = parent
```
So selecting a hidden item (e.g. by clicking a small tile) lifts the cap exactly enough to show it (limit = index+1), and all ancestors get expanded. `scan()` resets limits to empty. Trash does not touch limits.

### 6.5 Trash flow
1. User triggers (inspector button, Item menu, context menu, Delete key) → `pendingTrash = node` (not for root).
2. Confirmation dialog (§1.8). Cancel → `pendingTrash = nil`.
3. Confirm → `confirmPendingTrash()`:
```
node = pendingTrash; if nil return
pendingTrash = nil
try trash(node)
catch e: errorMessage = "“\(node.name)” couldn’t be moved to the Trash. \(e.localizedDescription)"

trash(node):   // throws
    if node === root: return                       // silent no-op
    trasher(node.url)                              // throws → nothing below runs, tree unchanged
    parent = node.parent
    if zoomRoot != nil and zoomRoot.isDescendant(of: node): zoomRoot = (parent === root) ? nil : parent
    if selection != nil and selection.isDescendant(of: node): selection = parent   // (triggers expandAncestors)
    if hovered != nil and hovered.isDescendant(of: node): hovered = nil
    expanded.remove(node)
    node.removeFromParent()                        // propagate(-size, -items) up the chain + re-sort
    revision += 1
```
No rescan happens; sizes update everywhere via revision. The trash is synchronous on the main thread.

### 6.6 Layout cache & kind totals
- `layout(for: size)`: nil if no displayRoot. Cache key = (displayRoot identity, width, height, revision); recompute `TreemapLayout(displayRoot, Rect(0,0,w,h))` only on key change.
- `kindTotals`: cached by revision. DFS over the **scan root** (not zoom): for each node with **no children** (files, symlinks, bundles, empty folders) add node.size to totals[node.kind]; drop kinds with 0 bytes; sort by bytes descending (tie order unspecified — PORT: tiebreak by Kind declaration order for determinism). Consequence: "Folders" never appears (empty folders are 0 bytes).

### 6.7 Actions (platform)
| Action | Mac implementation | Notes |
|---|---|---|
| Reveal | `NSWorkspace.activateFileViewerSelecting([url])` | Finder window with the item selected |
| Open | `NSWorkspace.open(url)` | default app; folders open in Finder |
| Quick Look | toggle shared QLPreviewPanel; if the same URL is already visible → close; else show/refresh for url. Fallback: `qlmanage -p <path>` | Space and ⌘Y |
| Copy Path | general pasteboard ← `url.path` as plain string | POSIX path, no quotes |
| Open FDA settings | open `x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles` | |
| Show app in Finder | reveal the running `.app` bundle | |
| hasFullDiskAccess | can open `/Library/Application Support/com.apple.TCC/TCC.db` for reading | |
| chooseFolder | NSOpenPanel per §1.8 | |

### 6.8 Update check (`UpdateChecker`)
- URL: `https://api.github.com/repos/l3n0ire/DiskU/releases/latest` (PORT: point at the Windows repo/asset or keep same repo; decision).
- Request: GET, header `Accept: application/vnd.github+json`, timeout 10 s. (No User-Agent set explicitly; .NET HttpClient **must** set one or GitHub returns 403.)
- Runs once per launch (window task). Skipped entirely if the running app has no version (not bundled: `CFBundleShortVersionString` missing).
- Success requires HTTP 200 and JSON with `tag_name` (string parsable as Version) and `html_url` (URL). Anything else (offline, rate-limited, bad JSON, non-version tag like `nightly`) → silently do nothing.
- `available = (current < latest.version) ? latest : nil`. `dismissed` is in-memory only.
- `Version(string)`: strip **one** leading `v` if present; split on `.` (Swift `split` **omits empty pieces**, so `1..0` → [1,0]); every piece must parse as Int (Swift `Int()` accepts a leading `+`/`-`); empty list → nil. Examples: `v1.0.10` ok, `v1.0-beta` → nil, `latest` → nil, `v` → nil.
- Compare: piecewise numeric, missing pieces = 0 (so `1.0 == 1.0.0`, `1.0.10 > 1.0.9`, `1.1 > 1.0.3`). description = pieces joined by `.` (`v2.0.0` → `2.0.0`).

### 6.9 Launch
- On window appear: if launch folder argument → `store.scan(folder)`; concurrently `await updates.check()`.
- Window min 900×560 is on the content; default 1200×800.

---

## 7. Test suite (port 1:1; they assert literal values)

Shared fixture `makeFixture()` (ScannerTests.swift): a fresh temp dir `DiskUTests-<UUID>` containing (block = 4096 bytes of non-zero patterned data; byte i = `UInt8(truncating: i &* 2654435761 >> 7)` — any incompressible-ish non-zero data works):
| Path | Blocks |
|---|---|
| `movie.mov` | 5 |
| `photos/a.png` | 2 |
| `photos/b.jpg` | 1 |
| `photos/deep/notes.txt` | 1 |
| `Tool.app/Contents/MacOS/Tool` | 3 |
| `Tool.app/Contents/Info.plist` | 1 |
| `empty/` (empty dir) | — |
| `photos/deep/loop` → symlink to `../..` | — |
| `movie-alias.mov` → symlink to `movie.mov` | — |
Total 13 blocks = 53,248 bytes; itemCount 8 (6 files + 2 symlinks).
PORT notes: on Windows `Tool.app` won't be a package — the port must decide how to emulate (e.g. treat `*.app` dirs as bundles in tests, or drop the bundle assertions); symlink creation needs Developer Mode or admin (or use a junction for the dir loop); NTFS 4 KiB clusters make the sizes exact, but files < ~700 B would be MFT-resident (0 allocated) — fixture files are all ≥ 4096 so fine.

### FileNodeTests
Helpers: `file(name, size)` = FileNode(path `/r/<name>`, kind from name, size, itemCount 1); `folder(name, children)` = folder node + adopt.
1. **adoptSumsAndSortsChildren**: root = folder(root, [a.txt 10, folder(sub, [b.mp4 30, c.png 50]), d.zip 40]) → root.size == 130; root.itemCount == 4; root.children names == [sub, d.zip, a.txt]; sub.children names == [c.png, b.mp4].
2. **removingNodeUpdatesEveryAncestorAndResorts**: big.mov 100; inner = [big, small.txt 5]; mid = [inner, m.png 20]; root = [mid, other.zip 60]. Before: root.size == 185, root children == [mid, other.zip]. After `big.removeFromParent()`: big.parent == nil; inner.size == 5; mid.size == 25; root.size == 85; root.itemCount == 3; mid.children == [m.png, inner]; root.children == [other.zip, mid].
3. **kindRegistryMapsExtensions**: `Clip.MOV` → video; `x.tar.gz` → archive; `main.swift` → code; `README` → other.

### ScannerTests
4. **scanTotalsFixtureExactlyWithoutFollowingSymlinks**: tree.size == 13*4096; tree.itemCount == 8; inaccessible == 0; tree.children names == [`movie.mov`, `Tool.app`, `photos`, `empty`, `movie-alias.mov`]; sizes == [20480, 16384, 16384, 0, 0] (Tool.app before photos via ordinal name tiebreak); photos.kind == folder, photos.size == 4*4096, photos.children == [`a.png`, `b.jpg`, `deep`]; Tool.app kind == bundle, itemCount == 2, children empty; `photos/deep/loop` kind == other, size == 0; movie.mov kind == video.
5. **unreadableDirectoryCountsAsInaccessible**: chmod 000 on `photos/deep` → inaccessible == 1; root.size == 12*4096. (Restore perms in cleanup.) PORT: deny ListDirectory ACL.
6. **scanningAFileFails**: `Scanner.scan(<fixture>/movie.mov)` throws.
7. **progressReportsFinalCounts**: last progress callback has filesSeen == 8 and bytesSeen == 13*4096.
8. **cancelledScanThrowsCancellation**: start scan in a task, cancel immediately → awaiting throws CancellationError (PORT: OperationCanceledException).

### SquarifyTests (tolerance 0.001 on minX, minY, width, height)
9. **squarifyMatchesBrulsPaperExample**: sizes [6,6,4,3,2,2,1] in (0,0,6,4) →
   (0,0,3,2), (0,2,3,2), (3,0,12/7,7/3), (3+12/7,0,9/7,7/3), (3,7/3,1.2,5/3), (4.2,7/3,1.2,5/3), (5.4,7/3,0.6,5/3).
10. **squarifyScalesToRectAndOffset**: [1,1] in (10,20,100,50) → (10,20,50,50), (60,20,50,50).
11. **squarifyDegenerateInputsYieldZeroRects**: [0,0] in 10×10 → [zero, zero]; [] → [].

### UpdateTests
12. **versionsCompareNumberByNumber**: v1.0.10 > 1.0.9; v1.1 > v1.0.3; 1.0 == v1.0.0; Version("v2.0.0").description == "2.0.0"; Version("v1.0-beta") == nil; Version("latest") == nil.
13. **releaseDecodesFromGitHubResponse**: JSON `{"tag_name": "v1.0.4", "html_url": "https://github.com/l3n0ire/DiskU/releases/tag/v1.0.4", "draft": false}` → version "1.0.4", url string exact; `{"tag_name": "nightly", "html_url": "https://x"}` → decoding error.

### DiskStoreTests (main thread)
14. **trashingRemovesNodeAndUpdatesAncestorsAndSelection**: recording trasher (collects last path components). finish(scan(fixture)). selection = photos/a.png; zoom(into: photos); remember revision; pendingTrash = a.png; confirmPendingTrash() → trashed == [`a.png`]; pendingTrash == nil; errorMessage == nil; revision == old+1; photos.size == 2*4096; tree.size == 11*4096; tree.itemCount == 7; photos.children == [`b.jpg`, `deep`]; tree.children == [`movie.mov`, `Tool.app`, `photos`, `empty`, `movie-alias.mov`]; selection === photos; zoomRoot === photos.
15. **trashingZoomedFolderZoomsOutAndRefusalSurfacesError**: trasher throws "no permission" when `refuse`. zoom(into: photos/deep) → zoomRoot === deep. Trash photos → zoomRoot == nil; tree.size == 9*4096; tree.children == [`movie.mov`, `Tool.app`, `empty`, `movie-alias.mov`]. Then refuse = true; trash movie.mov → errorMessage has prefix `“movie.mov” couldn’t be moved to the Trash.`; tree.size still 9*4096; movie.parent === tree.
16. **layoutTilesAreProportionalAndHitTestable**: layout(400×300): first tile rect == (0,0,400,300); movie.mov tile area ≈ 5/13 × 396 × 281 within 0.5 (content = 400−4 by 300−4−15); tile(at: movie center).node.name == `movie.mov`.
17. **cancelledScanDoesNotOverwriteItsReplacement**: two fixtures; scan(first); cancelScan(); scan(second); poll every 10 ms while state is scanning **with a 5 s deadline**; root.url == second; root.size == 13*4096.
18. **outlineListsLargestChildrenFirstAndRevealsHiddenSelections**: temp dir with `many/f1.bin … f150.bin`, fi = i*4096 zero bytes. folder = root.children.first (many). outlineLimit == 100; children[99].name == `f51.bin`; children[119].name == `f31.bin`; selection = that → outlineLimit == 120; showMore → 620; scan(root) → outlineLimit(folder) == 100 (limits cleared); cancelScan().

Rule for the port: every polling loop in a test needs a deadline.

---

## 8. Release, versioning, bundling, tools

### 8.1 Build wrapper
`scripts/swift.sh`: runs `swift` with SDK pin (MacOSX26.sdk) and Swift Testing plugin path when only Command Line Tools are installed. Irrelevant on Windows (use `dotnet build/test`).

### 8.2 Bundling (`scripts/bundle.sh`)
- Version = `$DISKU_VERSION` or latest tag `v[0-9]*` without `v` (fallback `1.0`); build = `$DISKU_BUILD` or `git rev-list --count HEAD`.
- Universal binary (arm64 + x86_64), release config.
- Writes `DiskU.app` at repo root: `Contents/MacOS/DiskU`, `Contents/Resources/AppIcon.icns`, `Info.plist` with: bundle id `com.colin.DiskU`, name/display name `DiskU`, executable `DiskU`, icon `AppIcon`, package type APPL, short version = version, CFBundleVersion = build, min system 14.0, category `public.app-category.utilities`, high-res capable, principal class NSApplication.
- Ad-hoc codesign (`codesign --force --sign -`). Not notarized.
- PORT: x64 + arm64 builds; version in the csproj/AppxManifest from the tag; the app's own version is what `UpdateChecker` compares against (unpackaged dev builds should skip the check like unbundled Mac builds).

### 8.3 Icon
`Resources/AppIcon.png` → `scripts/make-icon.swift` → `Resources/AppIcon.icns`: Vision foreground cut-out of the art, placed on a rounded-square tile occupying 824/1024 of the canvas, corner radius 0.225×side, vertical linear gradient top→bottom from sRGB (0.55, 0.80, 1.00) #8CCCFF to (0.15, 0.45, 0.95) #2673F2; subject scaled to 88% of the tile side, centered. Sizes 16, 32, 128, 256, 512 at 1× and 2×. Never edit the .icns by hand. PORT: produce an `.ico` (16, 24, 32, 48, 64, 256) from the same PNG, plus MSIX tile assets if packaged.

### 8.4 Release
- Trigger: push tag matching `v*.*.*`; the workflow (`macos-15` runner, contents: write) checks the tagged commit is an ancestor of `origin/main` (else error `<tag> points at a commit that is not on main`).
- Release notes: `scripts/release-notes.sh <tag>` prints the `## <tag>` section of `CHANGELOG.md` (everything until the next `## `, leading blank lines skipped) then:
  ```

  ### Install

  Runs on Apple Silicon and Intel Macs (macOS 14 or later).

  DiskU is not signed. After unzipping and opening DiskU.app, go to System Settings > Privacy & Security, scroll to the bottom, find DiskU, and click "Open Anyway".
  ```
  Fails with `CHANGELOG.md has no entry for <tag>` if the section is missing/blank.
- `scripts/release.sh <tag>`: tag must match `^v[0-9]+\.[0-9]+\.[0-9]+$` (else exit 64 `tag must look like v1.2.3, got <tag>`); runs tests; bundles with DISKU_VERSION/DISKU_BUILD; zips with `ditto -c -k --keepParent DiskU.app dist/DiskU-<version>.zip`; prints the zip path last.
- Publish: `gh release create <tag> <zip> --title "DiskU <version>" --notes-file notes.md --generate-notes`.
- Asset name: `DiskU-X.Y.Z.zip`. PORT DECISION: if Windows releases share this repo, the update checker's "latest" release must carry both assets or the Windows app must filter (GitHub "latest" is repo-wide).
- CHANGELOG entries so far: v1.0.0 first release; v1.0.1 universal app + launch-folder argument; v1.0.2 Copy Path (⌥⌘C) and roomier inspector; v1.0.3 Full Disk button, sidebar paging (100 largest + Show more), simpler FDA card; v1.0.4 update notices.

### 8.5 diskusnap (verification CLI)
```
diskusnap <dir> <out.png> [--renderer window|image] [--zoom]
diskusnap --home <out.png> [--pretend-version X.Y.Z]
```
- Scan mode: expands `~`, runs the real `Scanner.scan`; on error prints `scan failed: <msg>` to stderr, exit 1. Prints `scanned <path> in <s.ss>s: <files> files, <bytes> bytes (<formatted>), <n> inaccessible`. Creates a DiskStore, `finish(result)`, selects the root's largest child; with `--zoom` and that child a folder, zooms into it and selects its largest child. Prints `selected <name> (<size>)`. Renders the real `BrowserView` at **1400×900** in dark appearance in an offscreen window (pumps the run loop 20× 50 ms), or with `--renderer image` renders only `TreemapPane` (dark, background white 0.12, scale 2) and prints `note: image renderer draws the treemap pane only (no outline or inspector)`. Writes PNG; prints `wrote <path> via <renderer> renderer`. Errors: `render failed`, `write failed: <msg>` (exit 1). Bad args → usage text, exit 64.
- Home mode: optional `--pretend-version` runs the real update check as that version and prints `latest release newer than <v>: <version url | none>`; renders `ContentView` (welcome screen) 1400×900 on white-0.12 background; prints `wrote <path> (Full Disk Access: on|off)`.
- PORT: a `DiskU.Snap` console tool that scans and renders the treemap via Win2D `CanvasRenderTarget` to PNG is very valuable for pixel comparison against Mac renders; the full-window render can use `RenderTargetBitmap`.

### 8.6 Fixture & demo scripts
- `scripts/make-fixture.sh <dir>`: 17 random-data files + 2 symlinks, expected allocated total **106,958,848 bytes (104,452 KiB)**: `Movies/trip.mov` 51200 KiB; `Pictures/Vacation/beach.heic` 6144, `sunset.jpg` 4096, `dunes.jpg` 2048; `Pictures/Screens/shot1.png` 4096, `shot2.png` 4096; `Music/song.m4a` 8192, `demo.wav` 4096; `Code/app/src/main.swift` 64, `Code/app/src/data.json` 1984, `Code/app/build.o` 2048; `Docs/report.pdf` 3072, `notes.txt` 4, `slides.key` 2044; `Archives/backup.zip` 6144; `Apps/Tool.app/Contents/MacOS/Tool` 5120, `Apps/Tool.app/Contents/Info.plist` 4 (all KiB); symlinks `Code/app/loop -> ../..`, `Docs/trip-alias.mov -> ../Movies/trip.mov`. Should scan in well under a second (proves the loop isn't followed). Movies ≈ half the treemap.
- `scripts/make-demo.sh <dir>`: anonymised demo home (`<dir>/Home`) of APFS-clone files with given MiB sizes for screenshots (Movies, Pictures/Photos Library.photoslibrary, Music/Logic/Album Draft.logicx, Downloads, Documents, Developer, Library/... — 33 files). PORT: equivalent PowerShell script with sparse/real files.
- verify-disku skill: run tests; scan the fixture and expect exactly 106,958,848; look at the PNG (areas proportional, nesting, legend colors, breadcrumb with `--zoom`); `--home` render; real-data renders of `~`, `~/Library`, `/` (total should be below `df` used); bundle, launch, quit. Agents cannot drive the real window (clicks, hover, Quick Look, shortcuts, trash dialog are unverified except through DiskStore tests).

---

## 9. Known gaps (docs/known-gaps.md, verbatim in substance)

Open decision:
- **Immediate vs. batched trashing.** Today each Move to Trash asks for confirmation and runs right away. The alternative is a "marked for deletion" list the user empties in one go. User's call.

Issues:
- **Full Disk Access resets on every rebuild** (ad-hoc signature; a stable signing identity would fix it). Mac-only.
- **Memory per node.** Each FileNode keeps its own URL (~1.8 KB/node); a 765k-file whole-disk scan used ~1.4 GB. Storing a name and deriving paths from the parent chain would cut this. → PORT: do this from day one (store `Name` only; compute `FullPath` by walking parents).
- **Hard links** counted once per link → totals can overstate. (Much worse on Windows: `C:\Windows\WinSxS` is mostly hard links.)
- **Hover hit-testing** is a linear search over tiles (`rect(of:)` and `tile(at:)`), slower as file count grows. → PORT: consider a spatial index or descend the tree.
- **Total for `/` vs `df`**: picker-chosen `/` gave 83.65 GB vs ~130 GB used; likely unreadable folders, APFS snapshots, purgeable space. Unconfirmed.
- **No in-app UI verification** by an agent (clicks, Quick Look, shortcuts, trash dialog).

---

## 10. Mac-specific concepts → suggested Windows equivalents

| Mac concept | Where | Windows suggestion |
|---|---|---|
| Move to Trash (`FileManager.trashItem`) | trash flow | Recycle Bin: `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile/DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)` or `IFileOperation` with `FOFX_RECYCLEONDELETE | FOF_NOCONFIRMATION`. Never permanent delete. Note: items on drives without a Recycle Bin (some removable/network) would be deleted permanently — **must refuse** and surface an error instead. Strings: replace "Trash" with "Recycle Bin" (`Move “x” (size) to the Recycle Bin?`, `You can restore it from the Recycle Bin.`) — PORT DECISION on wording. |
| `allocated size` (`totalFileAllocatedSize`) | scanner | `FILE_STANDARD_INFO.AllocationSize` / `FILE_ID_BOTH_DIR_INFO.AllocationSize` from `NtQueryDirectoryFile`/`GetFileInformationByHandleEx(FileIdBothDirectoryInfo)` (no per-file open; fast). Alternatively `GetCompressedFileSizeW` (actual on-disk for compressed/sparse but not cluster-rounded). Cloud placeholders (OneDrive, `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS`/`OFFLINE`) report ~0 allocated — good, and must never be opened (would trigger hydration). MFT-resident small files report 0. |
| Symlinks never followed | scanner | Any **reparse point** (`FILE_ATTRIBUTE_REPARSE_POINT`: symlinks, junctions, mount points, AppExecLinks) is a leaf of kind other, never descended. Exception to consider: OneDrive/cloud-file reparse tags are files, not links — treat as normal files by allocation. |
| Don't cross volumes (volume identifier) | scanner | Compare volume serial number (`GetFileInformationByHandle` → `dwVolumeSerialNumber`) or simply never traverse reparse points (volume mount points are junction-like reparse points). Mapped/SUBST drives aren't reachable from a directory walk anyway. |
| `/` full disk + skip list (`/System/Volumes`, `/Volumes`, `/.nofollow`, `/.resolve`, `/.vol`, `/dev`) | Full Disk | "Full Disk" → system drive (`%SystemDrive%\`, e.g. `C:\`); optionally a drive picker (DriveInfo.GetDrives(), fixed/removable). Skip-list equivalents: `C:\System Volume Information` (unreadable; counts as inaccessible), `$Recycle.Bin` (decision: include — it's real usage), `C:\$Extend` etc. NTFS metafiles aren't enumerated. Junctions like `C:\Documents and Settings`, `C:\Users\All Users`, `%USERPROFILE%\Application Data` are reparse points → leaf, so no double counting. `pagefile.sys`/`hiberfil.sys`/`swapfile.sys` are enumerable from the directory listing even though they can't be opened — use directory-entry info, not open handles. |
| Packages/bundles (`isPackage`) | scanner, kind bundle | Windows has no packages. PORT DECISION: (a) no bundles at all (kind unused; legend never shows "Apps & Bundles"), or (b) treat specific dirs as opaque bundles (e.g. `*.app` for test parity, `node_modules`?). Recommend (a) plus test adaptation. |
| Full Disk Access (TCC) + probe + settings deep-link + "Show DiskU in Finder" | onboarding card, banner | No equivalent permission. Unreadable folders come from ACLs/UAC: offer "Restart as administrator" (relaunch with `runas` verb, passing the folder to rescan) instead of "Open Privacy Settings". Probe "granted" = process is elevated (`WindowsPrincipal.IsInRole(Administrator)` with elevated token). Card text must be rewritten (PORT DECISION), or drop the card and keep only the banner. Banner: `N folders couldn’t be read` / `Sizes are incomplete. Restart DiskU as administrator, then rescan.` + `Restart as Administrator`. |
| Reveal in Finder | menu/inspector | `explorer.exe /select,"<path>"` (or `SHOpenFolderAndSelectItems`). Label "Show in Explorer"/"Reveal in File Explorer" (PORT DECISION; keep `Reveal` on the inspector button). |
| Open (`NSWorkspace.open`) | | `Process.Start(new ProcessStartInfo(path){UseShellExecute=true})` / `Launcher.LaunchFileAsync`. |
| Quick Look (Space, ⌘Y, toggle) | | No built-in. Options: in-app preview window using shell **preview handlers** (`IPreviewHandler`, what Explorer's preview pane uses) or thumbnails (`IShellItemImageFactory`); or invoke PowerToys Peek if installed; fallback = Open. Keep Space as a toggle. |
| Copy Path | | `Clipboard.SetContent` with the Windows path (backslashes, no quotes). Keep `Copied` feedback for 1.5 s. |
| NSOpenPanel | Scan… | `FolderPicker` (WinRT, needs window handle via `InitializeWithWindow`) with start location = user profile; or `IFileOpenDialog` with `FOS_PICKFOLDERS` to set title "Choose a folder to scan" and OK label "Scan". |
| Home folder | | `Environment.GetFolderPath(SpecialFolder.UserProfile)` (`C:\Users\<name>`). |
| ⌘ shortcuts | menus | ⌘→Ctrl, ⌥→Alt, ⇧→Shift: Ctrl+O, Ctrl+Shift+H, Ctrl+R, Ctrl+Shift+R (Reveal), Ctrl+Y (Quick Look — conflicts with Redo convention; acceptable, no editing here), Ctrl+Alt+C (Copy Path), Ctrl+Down (Open; consider also Enter), Ctrl+Up (Zoom Out; consider also Alt+Up/Backspace), Delete (Move to Recycle Bin…). Tooltips: replace `(⌘R)` → `(Ctrl+R)` etc. |
| Menu bar (File / Item) | | WinUI `MenuBar` with `File` and `Item` menus, or put commands in a `CommandBar` overflow; keep KeyboardAccelerators on the window. |
| Toolbar in title bar | | Custom title bar (`ExtendsContentIntoTitleBar`) with icon buttons right-aligned + ToolTips from a single `ToolbarTooltips` dictionary keyed by label; or a `CommandBar` under the title bar. |
| NavigationSplitView sidebar + inspector | | 3-column Grid with splitters; sidebar collapse toggle button at left of title bar. |
| SF Pro / text styles | | Segoe UI Variable. macOS sizes: largeTitle 26, title 22, title2 17, title3 15, headline 13 bold, body 13, callout 12, caption/caption2 10. Map to the same pt sizes in DIPs, or scale proportionally to Windows' 14-px body (PORT DECISION — the treemap's 11/10.5/10/9.5 pt label sizes should stay literal for pixel parity). |
| Monospaced digits | sizes | `Typography.NumeralAlignment = Tabular` (Segoe UI supports tnum). |
| Accent tint, secondary/tertiary/quaternary colors | | `SystemAccentColor`, `TextFillColorSecondary/Tertiary`, `ControlFillColorTertiary`/`SubtleFillColorSecondary`. |
| `.borderedProminent` red Move to Trash | | `Button` with `AccentButtonStyle` and a red (`SystemFillColorCritical`) background. |
| Sidebar vibrancy / material | | Mica backdrop (`MicaBackdrop`) on the window; sidebar transparent. |
| Canvas (async render) | treemap | Win2D `CanvasControl` (or `CanvasVirtualControl` for big trees); draw highlights in a second CanvasControl overlay so hover doesn't redraw tiles. Cache a `CanvasCommandList`/`CanvasRenderTarget` for the tile layer. Respect DPI: layout in DIPs. |
| `onContinuousHover`, tap/double-tap | | `PointerMoved`/`PointerExited`, `Tapped`, `DoubleTapped`, `RightTapped` → `MenuFlyout`. |
| Finder icons (`NSWorkspace.icon`) | | `SHGetFileInfo`/`IShellItemImageFactory`; per-extension sharing via `SHGFI_USEFILEATTRIBUTES`. |
| GitHub update check | | Same API; HttpClient with `User-Agent: DiskU/<version>` (required by GitHub) + Accept header, 10 s timeout, silent failure. Version from `Package.Current.Id.Version` (packaged) or assembly informational version; skip in dev builds. |
| Launch argument (`open DiskU.app --args ~/Downloads`) | | `DiskU.exe "C:\path"` — first arg not starting with `-` that is an existing directory; expand `~` to the profile for parity, also env vars. |
| Ad-hoc signing / Open Anyway | release notes | SmartScreen "More info → Run anyway" for unsigned exe; install text must change. |
| APFS clones/snapshots, firmlinks | | NTFS: hard links (WinSxS), compression, sparse, dedup (Server), VSS shadow copies (not visible in walk). |
| `@MainActor`, `Task`, generation numbers | store | `DispatcherQueue` marshalling, `CancellationTokenSource` per scan + the same integer generation check (both are needed: tokens alone race with already-queued progress callbacks). |
| Locale-formatted numbers/dates/percent | | `ToString("N0")`, `ToString("P1")`, date: `"MMM d, yyyy 'at' h:mm tt"` for en-US parity or `DateTimeFormatInfo` short patterns (PORT DECISION). |

---

## 11. Ambiguities & decisions the port must make (summary)

1. **Bundles**: no package concept on Windows → drop `Apps & Bundles` behaviour or define pseudo-bundles; fixture tests reference `Tool.app` as a bundle (itemCount 2, no children).
2. **Full Disk Access UI**: no equivalent → replace with an elevation flow or drop; banner text/button must change.
3. **Full Disk target**: system drive only (literal port) vs. drive picker; skip list for Windows.
4. **Quick Look**: needs a custom previewer or PowerToys Peek; decide fallback.
5. **Wording**: Trash → Recycle Bin; Finder → File Explorer; ⌘ → Ctrl in tooltips/menus.
6. **Byte formatting**: must re-implement Apple's decimal, adaptive-precision formatter (not Windows' 1024-based).
7. **Name tiebreak** must be ordinal (uppercase before lowercase) to match tests.
8. **Hard links** (WinSxS) will massively overcount `C:\` — consider dedup by FileId (would diverge from Mac behaviour; Mac lists it as a known gap).
9. **Unreadable-root / failure messages**: Mac uses Cocoa error text; port writes its own `Scan Failed` description.
10. **Memory**: store names, not full paths (Mac's known gap).
11. **Update source**: same GitHub repo's "latest" release is shared with Mac releases — need platform-aware assets or a separate repo/tag scheme.
12. **Quirks to keep or fix**: outline Delete key lacks the root guard (trash of root is a silent no-op); symlink bytes not added to progress `bytesSeen`; `Version` accepts `1..0` and signed numbers; kind-legend ties unordered; `cancelScan()` returns to the Welcome screen even if a previous scan was showing; `Format.items` says "files" for a folder's contents; label text is hard-clipped (no ellipsis).
13. **Fonts/sizes**: keep macOS point sizes literally (pixel parity) or adapt to Windows type ramp.
