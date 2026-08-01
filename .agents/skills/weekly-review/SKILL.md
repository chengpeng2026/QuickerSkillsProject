---
name: weekly-review
description: Scan this week's daily reviews in the Obsidian vault, identify recurring patterns and failures, propose new CLAUDE.md/memory rules, and flag stale rules for removal. Use when user says "weekly review", "周报", "每周复盘", or wants to reflect on the week's work.
---

# Weekly Review

扫描本周所有每日复盘，提炼模式、生成规则、清理过时约束。

## 前置条件

- Obsidian vault 存在 `E:\Aiku2026\Reviews\Daily\` 目录
- 至少有 1 篇本周的每日复盘

## 流程

### Step 1: 确定本周日期范围

计算本周一 ~ 本周日的日期范围。用 PowerShell：

```powershell
$today = Get-Date
$monday = $today.AddDays(-([int]$today.DayOfWeek - 1))
$sunday = $monday.AddDays(6)
$weekLabel = "W" + [int]($monday.DayOfYear / 7 + 1)
Write-Output "本周: $($monday.ToString('yyyy-MM-dd')) ~ $($sunday.ToString('yyyy-MM-dd')) (第 $weekLabel 周)"
```

### Step 2: 加载本周全部每日复盘

```bash
ls "E:/Aiku2026/Reviews/Daily/" | sort
```

读入本周日期范围内的所有 `YYYY-MM-DD.md` 文件。

### Step 3: 四项统计

从所有日报中汇总：

| 维度 | 来源字段 | 计算方式 |
|------|---------|---------|
| 总会话数 | `完成的任务` 表格 | 直接计数 |
| 完成任务数 | 状态列 = ✅ | `grep "✅"` 计数 |
| 总产出笔记 | `知识产出` 节 | 合并所有 `[[...]]` |
| 踩坑次数 | `踩坑记录` 节 | 直接计数 |

### Step 4: 重复问题识别

扫描所有日报的 `踩坑记录` 和 `关键决策` 小节，寻找：

1. **同一坑出现 ≥2 次** → 标记为"重复问题"，需生成规则
2. **同类决策反复出现** → 标记为"模式"，可固化到 CLAUDE.md
3. **待办项连续跨天未完成** → 标记为"阻塞"，需要决策

### Step 5: 规则提案

针对每个重复问题，提案一条新规则：

```
问题: <描述>
出现次数: <N>
根因: <why it keeps happening>
建议规则: <具体、可执行的约束>
写入位置: [CLAUDE.md | memory/ | Obsidian 避坑笔记]
```

### Step 6: 过时规则审计

回顾 `memory/` 和 CLAUDE.md 中现有规则，评估：

- **最近 2 周内触发过吗？**（从未触发 → 可能过时）
- **是否已被更精确的规则替代？**
- **对应的坑是否已修复？**

对建议清理的规则标注原因，**不直接删除**——列出供用户确认。

### Step 7: 指令质量回顾

汇总本周所有日报的「指令质量反思」区域，分析：

1. **模糊指令占比**：❌ 和 ⚠️ 标记的指令数 / 本周总指令数
2. **重复出现的指令问题**：同一类模糊模式出现 ≥2 次？
   - 例如：目标没说清楚、缺验收标准、隐含假设、边界没定义
3. **改进效果**：上周提出的指令改进建议，本周落实了吗？
4. **本周最佳指令**：挑一条 agent 一次做对的典范指令，分析它为什么好

### Step 8: 生成周报复盘

将以上所有产出写入 `E:\Aiku2026\Reviews\Weekly\YYYY-WNN.md`：

```markdown
---
tags: [weekly-review]
created: YYYY-MM-DD
review_period: YYYY-MM-DD ~ YYYY-MM-DD
---

# 第 N 周复盘 (MM/DD - MM/DD)

## 本周统计
| 指标 | 数值 |
|------|------|
| 总会话数 | ? |
| 完成任务 | ? |
| 产出笔记 | [[...]] |
| 踩坑次数 | ? |

## 每日摘要
| 日期 | 主要任务 | 坑 |
|------|---------|-----|
| Mon | ... | ... |

## 重复问题与规则提案
| 问题 | 次数 | 提案规则 | 写入位置 |
|------|------|---------|---------|

## 过时规则审计
| 规则 | 原因 | 建议 |
|------|------|------|

## 指令质量回顾
| 指标 | 数值 |
|------|------|
| 本周总指令数 | ? |
| 明确 (✅) | ? |
| 部分模糊 (⚠️) | ? |
| 导致返工 (❌) | ? |
| 模糊率 | ?% |

### 重复出现的指令问题
- （同类模糊模式出现 ≥2 次时才填）

### 上周改进跟进
- （如果上周有指令改进建议，本周执行情况如何）

### 本周最佳指令示例
> 「...」

为什么这条好：（具体、有约束、有验收标准...）

### 本周改进建议
- [ ] （下周写指令时要注意的改变）

## 下周重点
- [ ] ...

## 相关笔记
- [[Claude Code 复盘体系]]
- [[Reviews/Reviews MOC]]
```

### Step 9: 更新 Reviews MOC

在 `Reviews/Reviews MOC.md` 的「每周复盘」区域追加新条目。

### Step 10: 汇报给用户

输出精简摘要（统计 + 重复问题 + 指令质量亮点/问题 + 最多 3 条规则提案），详细内容在周报文件中。

## 边界

- **不要**在用户确认前修改 CLAUDE.md 或 memory/
- **不要**在用户确认前删除任何规则
- 如果本周只有 1 篇日报，跳过重复问题分析（数据不足）
- 如果本周无日报，仅生成空白模板，提示用户先补日报
- 指令质量回顾不是自我批评大会——重点是「下次怎么说更好」，不是「我怎么这么差」
