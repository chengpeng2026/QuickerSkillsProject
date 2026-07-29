# QK扳手 — README

AI 全自动构建安装 Quicker 动作的反射扳手。搭配 AI Agent（Claude Code + quicker-skill）实现一句话编写、安装、更新 Quicker 动作。

## 核心机制

通过反射访问 `ActionItem2Store.AddOrUpdateAction`，直接构造 `ActionItem2` → `XActionDto` → `ActionStepDto` 对象图，注入 C# 脚本代码，实现动作的免 UI 构建安装。

## 使用方式

```
build.ps1 -JsonPath <JSON绝对路径>
```

或直接调用 QuickerStarter:

```
QuickerStarter.exe -c120 "runaction:0b6ea8ec?action=build&filePath=<JSON绝对路径>"
```

## 支持的操作

| action | 功能 |
|--------|------|
| `build` | 构建/更新动作 |
| `update` | 更新简介（`_简介.md`） |
| `read` | 读取动作信息 |
| `publish` | 发布到共享平台 |
| `copycode` | 修复壳动作模板引用 |

## 文件结构

```
QKBuilder/
├── src/
│   ├── qkbuilderv2.json   → ActionId: 0b6ea8ec
│   └── qkbuilderv2.cs     → 内嵌 C# 脚本代码
└── docs/
    ├── CHANGELOG.md
    └── README.md          ← 本文件
```

## 相关

- `skills/quicker-skill/` — 技能定义和 build.ps1
- `config.json` — wrench_action_id 指向 0b6ea8ec
- Obsidian [[QK 扳手导入后 ID 变化]] / [[Quicker 开发知识库]]
