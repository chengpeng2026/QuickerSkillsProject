---
name: knowledge-capture
description: Capture reusable knowledge from development sessions and write structured notes to Obsidian vault with dedup, wikilinks, MOC updates, and optional memory stubs.
---

# Knowledge Capture

将开发过程中发现的可复用知识沉淀到 Obsidian vault (`E:\Aiku2026\`)。

## 触发条件

1. **开发中发现新模式/坑** — 修复了一个 tricky bug，root cause 有复用价值
2. **完成功能有架构决策** — 选择了某种方案，理由值得记录
3. **学到平台新限制/能力** — Quicker 或工具链的新发现
4. **会话结束** — 扫描本次对话，建议 1-3 条可写入的知识点

## 知识分类

| 分类标签 | MOC 笔记 | 判断标准 |
|------|------|------|
| `quicker/roslyn-pitfall` | Quicker Roslyn 避坑 | C# 编码、COM 交互、构建导入的陷阱 |
| `quicker/v2-platform` | Quicker V2 平台 | V2 架构、子程序、表达式、API 能力 |
| `project/process` | 项目开发规范 | 流程规则、对齐协议、版本管理 |
| `project/retrospective` | Quicker 开发知识库 | 项目复盘、架构演进记录 |

## 写入流程

### Step 1: 识别知识点

从当前会话中提取，满足**三者都有**才写入：
- **新发现**：之前不知道的事实或规则
- **可复用**：下次遇到类似场景能用上
- **有 why**：不只是"怎么做"，还有"为什么这样做"

### Step 2: 去重检查（必须）

写入前先在 vault 中搜索是否已有相关笔记：

```
# 搜索中文关键词
Select-String -Path "E:\Aiku2026\*.md" -Pattern "<中文关键词1>|<中文关键词2>"

# 搜索英文指纹（API 名、类名、错误码等）
Select-String -Path "E:\Aiku2026\*.md" -Pattern "<EnglishKeyword1>|<ErrorCode>"

# 搜索同标签笔记
Select-String -Path "E:\Aiku2026\*.md" -Pattern "tags:.*<domain-tag>"
```

> **中英双语搜索**：中文笔记库中可能用英文记录 API 名/类名/错误码。搜索时同时尝试两类关键词，避免漏匹配。

**去重决策**：
- 无匹配 → 创建新笔记
- 1 篇匹配且内容可合并 → 更新已有笔记（追加小节），不新建
- 1 篇匹配但角度不同 → 创建新笔记，与已有笔记互相交叉引用
- 2+ 篇匹配 → 创建新笔记，链接所有相关笔记

### Step 3: 确定分类和标题

- 标题简洁、具体，中文优先
- 例：`PS COM 属性赋值不能用 set_ 前缀` 优于 `COM 问题`

### Step 4: 写入笔记

使用 obsidian-vault skill 的模板格式：

```markdown
---
tags: [quicker, <category-tag>]
created: yyyy-MM-dd
aliases: [<alternative-title>]
---
# <Title>

## 问题 / 背景
<what happened, why it matters>

## 方案 / 规则
<the solution or rule to follow>

## 关键代码
<if applicable, minimal code snippet>

## 相关笔记
- [[MOC-name]]
- [[related-note-1]]
```

### Step 5: 更新 MOC

在对应的 MOC 笔记中追加 `- [[新笔记标题]] — 一句话描述`

### Step 6: 建立交叉引用

搜索 vault 中有无相关笔记可互相链接：
```
Select-String -Path "E:\Aiku2026\*.md" -Pattern '<关键词>'
```

如找到 → 新笔记「相关笔记」添加链接 + 相关笔记中也添加反向链接。

### Step 7: 必要时建 memory stub

如果笔记内容在后续开发中会频繁引用，在 `memory/` 建一个轻量 stub：

```markdown
---
name: <kebab-case-slug>
description: <one-line summary>
metadata:
  type: reference
---
→ Obsidian: [[Obsidian Note Title]]
```

目的：让 memory 系统感知到此知识存在于 Obsidian，后续可通过 MEMORY.md 索引发现。

## 写入前检查清单

- [ ] 已去重：搜索过 vault 确认无重复覆盖
- [ ] 有复用价值（非一次性细节）
- [ ] 标题具体、可检索
- [ ] 已链接到至少一个 MOC
- [ ] 已添加至少一个 wikilink 交叉引用
- [ ] frontmatter 含 tags 和 created
- [ ] 评估是否需要建 memory stub

## 写入后回读验证（必须）

写入文件后，立即执行回读验证。**缺少此步骤视为写入未完成。**

```
# 1. 回读刚写入的文件
Read file_path="E:\Aiku2026\<刚写入的文件>.md"

# 2. 验证以下三项都存在：frontmatter（tags + created）、H1 标题、至少一段正文

# 3. 确认 MOC 中已追加 wikilink
Grep pattern="\[\[<新笔记标题>\]\]" path="E:\Aiku2026" glob="**/*.md"
```

如果回读验证失败 → 重新写入。如果 MOC 中未找到链接 → 重新追加。

## 会话结束扫描

1. 回顾本次对话中的重要发现
2. 过滤已在 memory/ 或 Obsidian 中覆盖的内容
3. 建议 1-3 条写入条目
4. 列出建议给用户，确认后写入

## 参考

- obsidian-vault skill：vault 操作的具体命令和搜索方法
- CLAUDE.md：知识沉淀规则、MOC 结构、memory↔Obsidian 关系
