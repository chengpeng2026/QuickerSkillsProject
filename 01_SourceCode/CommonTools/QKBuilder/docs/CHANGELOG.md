# QK扳手 — 变更日志

## v2.0.0 (2026-07-29)

- 🔧 统一 V2/V3：采用 ActionItem2Store + ActionItem2 API，兼容 Quicker 2.1.0
- 🏗️ 内嵌代码无模板依赖：手工编辑不导出导入，ID 稳定（`0b6ea8ec`）
- ⚡ 5 个 action 入口：`build` / `update` / `read` / `publish` / `copycode`
- 🩹 `copycode`：反射修复壳动作的模板引用，自动切到稳定扳手 ID
- 🔬 DLL 逆向工程：确认 6 个关键类型在 .NET 10 环境中的正确 namespace
- 🧹 删除冗余 V3 文件：qkbuilderv3.cs + qkbuilderv3.json 不再维护

## v1.0.0 (历史)

- QKBuilder V2：基于 `AppState.DataService → ActionProfile → ActionItem` 的旧反射链（已废弃，不兼容 2.1.0）
