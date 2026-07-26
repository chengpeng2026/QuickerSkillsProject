# CONTEXT — QuickerSkillsProject

> 本文档是项目的领域术语表（Ubiquitous Language）。只记录领域概念的定义，不包含实现细节。
> 当对话中使用的术语与本文档冲突时，以本文档为准。
> 格式遵循 [[工程标准化目录+版本维护全方案]] 中定义的工程规范。

---

## 核心概念

### 动作定义（Action Definition）

一个 Quicker 自动化功能的**源码形态**。由两个同名配对文件组成：

- **动作脚本（Action Script, `.cs`）**：C# 逻辑代码，遵循 Roslyn v2 零样板模式（禁止 namespace/class），入口为 `public static string Exec(IStepContext context)`
- **动作清单（Action Manifest, `.json`）**：元数据配置，包含 Title、Version、Icon、Variables、ActionId 等字段

动作定义存放在 `01_SourceCode/{Domain}/{ActionName}/src/` 下，两个文件必须同名（如 `MyAction.cs` + `MyAction.json`）。

### 已安装动作（Installed Action）

动作定义经**构建（Build）**后，在 Quicker 面板中可运行的**运行时实例**。由唯一的 `ActionId`（GUID）标识。同一个动作定义重新构建会覆盖对应的已安装动作。

### 功能领域（Functional Domain）

按宿主软件或使用场景对动作定义的顶层分组。当前领域：

| 领域 | 路径 | 说明 |
|------|------|------|
| ObsidianTools | `01_SourceCode/ObsidianTools/` | Obsidian 笔记软件相关动作 |
| CommonTools | `01_SourceCode/CommonTools/` | 跨场景通用工具动作 |

### 动作版本号（Action Version）

遵循 SemVer `X.Y.Z` 的语义化版本号，记录在三个位置（必须一致）：
1. 动作清单 `.json` 的 `Version` 字段
2. 动作脚本 `.cs` 顶部的版本注释头（`vX.Y.Z Build: YYYYMMDD`）
3. 动作版本注册表 `VERSION_MAP.json`

**版本升级规则**：X（不兼容重写）→ Y（向下兼容新功能）→ Z（Bug 修复/优化）

### 构建（Build）

将动作定义编译并安装到本地 Quicker 面板的过程。由 `skills/quicker-skill/scripts/build.ps1` 驱动，通过 QuickerStarter.exe 调用 Roslyn v2 编译器。

构建 ≠ 发布。构建是本地操作；发布是将已安装动作分享到 Quicker 动作库。

### 动作版本注册表（Action Version Registry）

项目根目录的 `VERSION_MAP.json`，是记录所有动作定义当前状态的**单一事实来源**。每个条目包含：version、build_date、status、action_id、changes。

### 变更日志（CHANGELOG）

分两级：
- **动作级**：`{ActionName}/docs/CHANGELOG.md`，仅记录该动作自身的迭代
- **项目级**：`CHANGELOG_GLOBAL.md`，记录所有动作的变更，按时间倒序

标准格式：`## [vX.Y.Z] - YYYY-MM-DD` → `### 新增/优化/修复`

---

## 工程结构

### 标准动作目录（Standard Action Directory）

```
{ActionName}/
├── src/          # 动作定义（Action Script + Action Manifest）
├── docs/         # 动作级文档（README + CHANGELOG）
├── assets/       # 素材（截图、图标、演示 GIF）
└── test/         # 测试用例
```

### 输出与归档

| 目录 | 用途 |
|------|------|
| `02_BuildOutput/Latest/` | 最新稳定版 `.quicker` 成品 |
| `03_DocsPublic/` | 对外宣传资料（闲鱼文案、客户教程） |
| `04_ReleaseArchive/` | 历史版本压缩包备份 |

---

## 相关文档

- [[工程标准化目录+版本维护全方案]] — 完整方案原文
- [[工程管理方案-零基础使用手册]] — 零基础操作指南
- `README_工程说明.md` — 维护规范速查
