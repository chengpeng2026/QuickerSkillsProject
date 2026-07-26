# Quicker Skill 工程说明

> 维护人：@12089 | 项目：QuickerSkillsProject

---

## 目录规范

```
QuickerSkillsProject/
├── .gitignore                   # Git 忽略配置
├── README_工程说明.md            # 本文件：维护规范、目录规范、打包流程
├── CHANGELOG_GLOBAL.md          # 全局更新日志
├── 01_SourceCode/               # 源码核心仓库（按领域分组）
│   ├── ObsidianTools/           #   Obsidian 系列技能
│   └── CommonTools/             #   通用工具系列
├── 02_BuildOutput/              # 打包成品输出
│   └── Latest/                  #   始终存放最新稳定版
├── 03_DocsPublic/               # 对外宣传、售卖配套
├── 04_ReleaseArchive/           # 历史版本压缩包归档
└── skills/                      # quicker-skill AI 开发套件
```

## 领域分类

| 目录 | 用途 | 示例 |
|------|------|------|
| `ObsidianTools` | Obsidian 笔记软件相关 | 日期插入、模板、搜索增强 |
| `CommonTools` | 通用小工具 | 弹窗、剪贴板、文件处理 |
| *(未来扩展)* | 浏览器、开发工具等 | |

## 单技能目录模板

```
SkillName/
├── src/          # 源码：SkillName.cs + SkillName.json（同名配对）
├── docs/         # 文档：README.md、CHANGELOG.md、VERSION_MAP.json
├── assets/       # 素材：图标、截图、GIF
└── test/         # 测试：边界场景、测试配置
```

## 版本号规则

`X.Y.Z`（SemVer 语义化版本）

- **X - 主版本**：不兼容改动、完全重做
- **Y - 次版本**：向下兼容的新功能
- **Z - 补丁**：Bug 修复、优化

配套规则：
1. 每次分发必须升级版本号
2. JSON 内 `Version` 字段必须与文档一致
3. CHANGELOG 固定格式：`## [vX.Y.Z] - YYYY-MM-DD`

## 修改流程

```
需求记录 → 代码修改 → 本地测试 → 文档同步 → 打包归档 → Git提交
```

1. 在技能 `CHANGELOG.md` 新增待更新条目
2. 修改 `.cs` / `.json`，版本号升级
3. 全场景测试 + 边界测试
4. 更新 README、CHANGELOG、截图
5. 导出 `.quicker` → `02_BuildOutput/vX.Y.Z/`
6. Git commit + 归档压缩

## 常用命令

### 构建技能
```powershell
.\skills\quicker-skill\scripts\build.ps1 -JsonPath "E:\QuickerSkillsProject\01_SourceCode\ObsidianTools\ObsidianInsertDateTime\src\ObsidianInsertDateTime.json"
```

### 全局配置
`config.json` 包含扳手 ID 和 Quicker 启动器路径。

## 禁止事项

- ❌ 在 BuildOutput 目录改代码
- ❌ 多版本文件混放同一目录
- ❌ 改代码不更新 JSON 版本号
- ❌ 不写 CHANGELOG
- ❌ 宣传素材和源码混放
