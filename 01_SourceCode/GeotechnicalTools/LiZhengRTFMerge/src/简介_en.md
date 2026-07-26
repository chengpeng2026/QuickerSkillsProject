# LiZhengRTFMerge — Lizheng Deep Foundation RTF Report Merger

## What it does

Merges multiple RTF calculation reports exported by Lizheng Deep Foundation software into a
single **A3 landscape Word document** (.docx), sorted by file name.

- **Cover page** — from a template .docx, single-column
- **Table of Contents** — auto-generated from Heading 1 entries
- **Body** — two-column layout with a line between columns
- **Page numbering** — no numbers on cover/TOC; body starts at page 1

## Use case

Lizheng Deep Foundation outputs each calculation report as one or more A3-landscape RTF files.
Before finalizing a design package you need them merged into one Word document.

## How it works (technical)

1. **Natural sort** — RTF files sorted by filename with numeric-aware ordering
2. **One section per RTF** — file name becomes Heading 1, original RTF content follows
3. **Section-level layout** — cover + TOC are single-column; body is two-column
4. **PowerShell subprocess for COM** — avoids Roslyn's inability to create Word COM objects
   (the infamous `CLSID` marshalling error)
5. **Bookmark-based section tracking** — bookmarks mark TOC start and body start so that
   column count and page-number restart are applied to the correct sections
6. **Graceful degradation** — if the cover template is missing, skips straight to TOC+body

## Requirements

- Windows + Word 2016+ or WPS Office with VBA components
- Quicker client (Roslyn v2 runtime)
- Cover template at `C:\Users\12089\Desktop\最终计算书\封面.docx`

## Output

- `合并计算书.docx` — A3 landscape, cover + TOC + body
