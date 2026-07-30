---
name: obsidian-vault
description: Search, create, and manage notes in the Obsidian vault with wikilinks and MOC index notes. Use when user wants to find, create, or organize notes in Obsidian.
---

# Obsidian Vault

## Vault location

`E:\Aiku2026\`

Flat root for individual notes; **MOC (Map of Content)** notes serve as indexes linking to related notes.

## Vault structure conventions

- **Root directory**: MOC notes and standalone knowledge notes (flat)
- **Domain folders**: `CommonTools/`, `GeotechnicalTools/`, `FinanceTools/`, `ObsidianTools/` contain action-specific notes
- **Action notes** use `[[Folder/Note Name]]` path links in MOC entries
- **MOC notes**: Aggregate related topics as `[[wikilinks]]` lists with short descriptions
- **Individual notes**: One topic per note, with frontmatter and wikilinks
- **Chinese titles** for Quicker-related notes; English for general technical topics

## Note template

```markdown
---
tags: [<domain-tag>]
created: yyyy-MM-dd
aliases: [<alternative-title>]
---
# <Title>

## 问题 / 背景
...

## 方案 / 规则
...

## 关键代码 (if applicable)
...

## 相关笔记
- [[...]]
```

## Workflows

### Search by tags

Search notes with specific frontmatter tags:

```bash
# Find all notes tagged with a specific tag (PowerShell)
# Use recursive search to cover subfolders
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern 'tags:.*<tag>' | ForEach-Object { $_.Filename }

# Or with Grep
grep -rl "tags:.*quicker/roslyn-pitfall" "E:/Aiku2026/" --include="*.md"
```

### Search by date

Find notes created or modified on a specific date:

```bash
# Notes created on a date (PowerShell) — recursive
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern 'created: 2026-07-28' | ForEach-Object { $_.Filename }

# Recently modified (PowerShell)
Get-ChildItem "E:\Aiku2026\*.md" | Sort-Object LastWriteTime -Descending | Select-Object Name, LastWriteTime -First 10
```

### Search by filename

```bash
# PowerShell
Get-ChildItem "E:\Aiku2026\*.md" | Where-Object Name -match "keyword"

# Or with Glob
Glob pattern="*keyword*" path="E:\Aiku2026"
```

### Search by content (full-text)

```bash
# PowerShell — recursive search
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern "keyword"

# Or with Grep
Grep pattern="keyword" path="E:\Aiku2026" glob="**/*.md"
```

### Dedup check (before creating a note)

Before creating a new note, check if the topic is already covered:

```bash
# 1. Search by potential title keywords
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern "<核心关键词>"

# 2. Search by related tags
grep -rl "tags:.*<domain-tag>" "E:/Aiku2026/" --include="*.md"

# 3. Decision:
#    - Found 0 matches → create new note
#    - Found 1 match → evaluate: update existing or create new and cross-link
#    - Found 2+ matches → likely create new with cross-links to all
```

### Find related notes (backlinks)

```bash
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern '\[\[Note Title\]\]'
```

### Find MOC / Index notes

```bash
Get-ChildItem "E:\Aiku2026\" -Recurse -Include "*.md" | Where-Object Name -match "(知识库|Index|MOC|避坑|规范|平台)"
```

### Session startup: Load relevant context

When starting a new session, load relevant notes from the vault:

```bash
# Step 1: Identify keywords from user's task
# Step 2: Search vault by tags and content for matching notes
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern 'tags:.*<domain-tag>'
Grep pattern="<keyword>" path="E:\Aiku2026" glob="**/*.md" output_mode="files_with_matches"

# Step 3: Load 1-3 most relevant notes
Read file_path="E:\Aiku2026\<matched-note>.md"
```

### List all tags

```bash
Select-String -Path "E:\Aiku2026\**\*.md" -Pattern '^tags:' | ForEach-Object { "$($_.Filename): $($_.Line.Trim())" }
```

### Create a new note

1. **Dedup**: Search for existing coverage of the topic
2. Determine the **category** and **tags**
3. Write content with the template (frontmatter + sections)
4. Add `[[wikilinks]]` to related notes at the bottom
5. **Update the corresponding MOC note** — append `- [[New Note Title]]` with a short description

### Update a MOC note

After creating a note, append its wikilink to the relevant MOC section:

```bash
# Use Edit tool to append "- [[新笔记标题]] — 一句话描述" under the right section
```
