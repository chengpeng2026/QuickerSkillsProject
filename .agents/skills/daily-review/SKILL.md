---
name: daily-review
description: Generate a structured daily review from today's Claude Code session — tasks completed, key decisions, pitfalls, knowledge output, and next steps. Use when user says "daily review", "日报", "每日复盘", "今日总结", or at end of work day.
---

# Daily Review

将会话产出整理为结构化每日复盘，写入 Obsidian vault。

## 前置条件

- Obsidian vault `E:\Aiku2026\` 中存在 `Reviews/Daily/` 目录

## 模板

```markdown
---
tags: [daily-review, session-summary]
created: YYYY-MM-DD
---

# YYYY-MM-DD 工作复盘

## 完成的任务

| # | 任务 | 用时(估) | 产出 | 状态 |
|---|------|---------|------|------|

## 关键决策
- 决策：... 原因：... 替代方案：...

## 踩坑记录
- 坑：... 原因：... 解法：... 是否已沉淀到 vault？

## 知识产出
- 新建笔记：[[...]]
- 更新笔记：[[...]]

## 待办 / 断点续传
- [ ] ...

## 自评（1-5）
- 效率：?/5  质量：?/5  学习：?/5

## 指令质量反思

回顾今天给 agent 的指令，逐条评估：

| # | 我发出的指令 | 是否明确 | 问题（如果有） | 下次怎么改进 |
|---|------------|---------|-------------|------------|
| 1 | 「...」 | ✅/⚠️/❌ | 目标模糊 / 缺约束 / 隐含假设 / ... | 应该这样说：「...」 |

> 如果今天全部明确，写「今天指令全部明确，无需改进」。
> 评分参考：✅ 明确（agent 一次做对） ⚠️ 部分模糊但不影响结果 ❌ 导致走弯路或返工
```

## 流程

### Step 1: 计算今日日期

```powershell
Get-Date -Format "yyyy-MM-dd"
```

### Step 2: 检查是否已有今日复盘

```bash
test -f "E:/Aiku2026/Reviews/Daily/$(date +%Y-%m-%d).md" && echo "已存在" || echo "不存在"
```

- 若已存在 → 询问用户：追加还是覆盖？
- 若不存在 → 继续

### Step 3: 回顾本次会话

扫描以下内容填入模板：

| 字段 | 来源 |
|------|------|
| 完成的任务 | TodoWrite 中标记 completed 的项 |
| 关键决策 | 会话中做的架构选择、方案取舍 |
| 踩坑记录 | 出错的诊断、修复过程 |
| 知识产出 | 新建/更新的 Obsidian 笔记 |
| 待办 | TodoWrite 中标记 pending 的项 |
| 指令质量反思 | 回顾本次会话中每一条给 agent 的指令，评估是否明确、agent 是否一次做对、做错时是否是指令模糊导致 |

### Step 4: 写入文件

写入 `E:\Aiku2026\Reviews\Daily\YYYY-MM-DD.md`

### Step 5: 更新 Reviews MOC

在 `E:\Aiku2026\Reviews\Reviews MOC.md` 的「每日复盘」区域追加：

```
- [[Reviews/Daily/YYYY-MM-DD]] — 一句话摘要
```

### Step 6: 输出摘要

简要汇报今日复盘已写入，列出关键数字（完成任务数、产出笔记数、踩坑数）。

## 边界

- 不编造数据——会话中没讨论的不写
- 踩坑记录只写已确认 root cause 的，别写"怀疑是..."
- 自评分数诚实，别每项都是 5/5
- 指令质量反思要具体：每条指令写原文，❌ 的要写清「应该怎么说才对」
