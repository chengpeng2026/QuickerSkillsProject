# QK扳手

AI 全自动构建安装 Quicker 动作的反射扳手。搭配 AI Agent（Claude Code + quicker-skill）实现一句话编写、安装、更新 Quicker 动作。

## 核心机制

通过反射访问 `ActionItem2Store.AddOrUpdateAction`，直接构造 `ActionItem2` → `XActionDto` → `ActionStepDto` 对象图，注入 C# 脚本代码，实现动作的免 UI 构建安装。

## 使用方式

命令行：`build.ps1 -JsonPath <JSON绝对路径>`

支持 5 个 action：`build` / `update` / `read` / `publish` / `copycode`

## 相关链接

- GitHub: https://github.com/chengpeng2026/QuickerSkillsProject
- Quicker 官网: https://getquicker.net/
