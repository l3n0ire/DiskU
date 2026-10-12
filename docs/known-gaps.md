# Known gaps

Open issues found while building and verifying DiskU. Remove an entry when it is fixed.

## Open decision

- **Immediate vs. batched trashing.** Today each Move to Trash asks for confirmation and runs right away. The alternative is a "marked for deletion" list the user empties in one go. This is the user's call.

## Issues

- **Memory per node.** Each `FileNode` keeps its own `URL`, about 1.8 KB per node. A whole-disk scan of 765k files used about 1.4 GB. Storing a name and deriving paths from the parent chain would cut this.
- **Hard links.** A hard-linked file is counted once per link, so totals can overstate real usage.
- **Hover hit-testing.** `rect(of:)` is a linear search over the tiles, so hover gets slower as the file count grows.
- **Whole-disk total vs. `df`.** Scanning `/` reported 83.65 GB against about 130 GB used. The likely causes are folders DiskU cannot read without Full Disk Access, APFS snapshots, and purgeable space. This is unconfirmed.
- **No in-app UI verification.** Clicking, Quick Look, keyboard shortcuts, and the trash confirmation have not been driven in the real window by an agent. See `.claude/skills/verify-disku/SKILL.md`.
