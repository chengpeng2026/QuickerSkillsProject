# 03 — 端到端验证: build.ps1 → HelloPopup 自动导入

**What to build:** 验证完整构建链路：
1. 运行 `build.ps1 -JsonPath "HelloPopup.json"`
2. HelloPopup 出现在 Quicker 面板中
3. 通过 `QuickStarter.exe -c120 "runaction:<HelloPopup的ID>"` 成功弹出"你好"

**Blocked by:** 02-construct-actionitem2

**Status:** ready-for-agent

- [ ] HelloPopup 通过 build.ps1 成功自动导入
- [ ] 通过 runaction 调用 HelloPopup 成功弹出"你好"
- [ ] 修改 HelloPopup.cs 重新 build，自动更新
