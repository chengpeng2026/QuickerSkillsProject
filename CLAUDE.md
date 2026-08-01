# QuickerSkillsProject — Claude Code 项目指引

## 知识管理

本项目的长期知识存放在 **Obsidian vault**：`E:\Aiku2026\`

### 会话启动：加载相关笔记

SessionStart hook 会列出 vault 中的 MOC 笔记和最近修改。**根据用户任务关键词搜索 vault**：

```
Grep pattern="<keyword>" path="E:\Aiku2026" glob="*.md" output_mode="files_with_matches"
Select-String -Path "E:\Aiku2026\*.md" -Pattern 'tags:.*<tag>'
```

加载 1-3 篇最相关笔记。无匹配 → 任务完成后写入新知识。

### 知识沉淀规则

**应写入**（满足任一）：
1. 开发中发现新的模式/技巧/坑，有复用价值
2. 修复了一个 bug，root cause 可能再次出现
3. 完成了一个功能，有架构决策或设计理由
4. 学到了 Quicker 平台的新限制或新能力

**不应写入**：
- 临时调试日志和输出
- 仅单次任务上下文（这类放 `memory/`）
- 代码细节（在仓库中）

### 写入流程

```
识别知识点 → 中英双语搜索去重 → 确定分类 → 写入笔记
  ↓
写入后 → 更新对应 MOC 索引 → 建立 wikilinks 交叉引用
  ↓
必要时在 memory/ 建轻量 stub → 完成
```

### 去重规则

写入前搜索 vault：同时尝试**中文关键词**和**英文指纹**（API 名、类名、错误码），避免跨语言漏匹配。

- 无匹配 → 创建新笔记
- 有关联但角度不同 → 新建并互相交叉引用
- 可合并 → 更新已有笔记

### MOC 结构

Vault 采用 **领域文件夹 + 根目录 MOC** 结构：
- MOC 笔记（知识库、避坑、平台、规范）全部在根目录
- 动作笔记按领域放子文件夹：`CommonTools/`、`GeotechnicalTools/`、`FinanceTools/`、`ObsidianTools/`
- 知识笔记（如 `避免 AI 开发死循环.md`）仍在根目录

| MOC 笔记 | 涵盖内容 | tags |
|------|------|------|
| `Quicker 开发知识库.md` | 顶层索引（含所有动作 [[领域/动作名]]） | - |
| `Quicker Roslyn 避坑.md` | 编码/COM/构建陷阱 | `quicker/roslyn-pitfall` |
| `Quicker V2 平台.md` | 架构/子程序/表达式/Bug | `quicker/v2-platform` |
| `项目开发规范.md` | 对齐/构建/版本/防死循环 | `project/process` |

> MOC 中每条 wikilink 必须带**一句话摘要**：`- [[title]] — 什么场景下需要读它`
> 子文件夹中的动作笔记用 `[[Folder/Note]]` 路径链接

### memory ↔ Obsidian 关系

- **memory/**：即时会话记忆、临时约束、快速索引
- **Obsidian**：长期结构化知识、图谱化、跨项目复用
- 写入 Obsidian 后评估是否需要建 memory stub
- 更新 memory 后评估是否需要同步到 Obsidian

## 项目概述

本仓库是 **Quicker 自动化动作** 的开发项目，基于 C# Roslyn 脚本（V2 引擎）。

核心领域：**GeotechnicalTools** | **CommonTools** | **ObsidianTools**

## QK 扳手

- 扳手 ID：`0b6ea8ec-6e51-47d8-a921-1eb2471f7b51`（手工编辑，导入不变）
- 内嵌代码：`01_SourceCode/CommonTools/QKBuilder/src/qkbuilderv2.cs`
- 更新扳手代码：Quicker 编辑 `0b6ea8ec` → C# 脚本步骤 → 粘贴 → 保存
- 永远不要导出/导入扳手自身
- 三个 config.json 的 wrench_action_id 均指向此 ID
- 文件结构对齐项目规范：`src/` (.json + .cs + _简介.md) + `docs/` (CHANGELOG.md + README.md)

### 动作归档规范

每个动作目录结构（与 FileExactCopy/SimplePopup/RtfNumberMultiply 对齐）：

```
动作名/
├── src/
│   ├── 动作名.cs          → C# 逻辑代码（Roslyn v2，禁止 namespace/class）
│   ├── 动作名.json        → 元数据配置（ActionId/Title/Icon/Variables/References）
│   └── 动作名_简介.md     → Quicker 共享平台简介（短文本）
└── docs/
    ├── CHANGELOG.md       → 版本变更日志（每个发布版本的改动列表）
    └── README.md          → 人类可读的项目说明（用途、用法、技术要点）
```

> **注意**：.cs 含中文必存 UTF-8 with BOM。JSON 的 `References` 字段用于 DLL 引用（`//css_ref` 在反射构建中无效）。

## 开发规范

- 含中文的 .cs 文件必须 UTF-8 with BOM
- 每次修改动作代码后必须 build 导入验证
- 版本号变更必须同步更新 `docs/CHANGELOG.md`
- **禁止 MessageBox 诊断**：Quicker 动作 Sandbox 中 MessageBox 泵送消息队列会改变 UI 线程时序，导致误判（诊断通过→移除→问题复现）。诊断用写桌面 txt 替代。
- **修改任何 Quicker 动作 .cs 文件前**，必须先执行：加载 quicker-skill → 读取 memory `quicker-before-modification-check` → git log 基线 → 确认当前版本已测试通过 → 如果是修 Bug 收集 3 个数字（输入/期望/实际）。跳过任何一步 = 禁止改代码。
- **每次收到 Quicker 动作 Bug 报告时**，先搜索 vault 加载 [[RtfNumberMultiply 8-1 流程复盘]] 和 [[与 Claude Code 协作防翻车完全指南]]，执行诊断流程（复述→列选项→单变量→3次上限）

## 会话约定

- **会话启动**：SessionStart hook 执行健康检查 + 列出 vault 笔记
- **前 3 轮必须加载**：第 1-3 轮工具调用中，必须搜索 vault 并加载至少 1 篇相关笔记，在回复中明确引用（如"根据 [[笔记名]]..."）。若第 4 轮仍未加载，视为桥梁失效，用户可说「检查桥梁」触发修复
- **任务对齐**：不确定点列选项让用户选择，确认后再动手
- **构建验证**：改动动作代码后实时 build 验证
- **知识扫描**：每个 todo list 最后一项固定为「📝 知识扫描：本次有值得写入 Obsidian 的吗？」——标记 completed 前必须回答
- **写入验证**：knowledge-capture 写入文件后，立即用 Read 回读确认内容完整（至少包含 frontmatter + H1 标题 + 一段正文），确认 MOC wikilink 已追加
- **写入知识**：调用 knowledge-capture skill 写入 Obsidian，更新 MOC + 交叉引用
- **工作区整洁**：任务结束后确认 `git status` 干净。新产生的文件及时分类提交，禁止累积大量 untracked/staged 文件到下次任务。`temp/` 目录仅限临时调试，任务结束前必须清理
