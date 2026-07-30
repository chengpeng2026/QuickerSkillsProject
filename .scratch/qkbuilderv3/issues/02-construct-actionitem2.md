# 02 — QKBuilder V3: 构造 ActionItem2 并调用 AddOrUpdateAction

**What to build:** 基于 R3 结果，完善 QKBuilder V3 的 ActionItem2 构造逻辑：
- 创建 OperationPresentation 并赋值 Title/Icon
- 创建 XAction payload (IOperationPayload 实现) 并填入 JSON+CS 数据
- 创建 SharedActionInfo (如果 SharedActionId 非空)
- 调用 ActionItem2Store.AddOrUpdateAction

**Blocked by:** 01-ApiDiag-R3-xaction-payload

**Status:** ready-for-agent

- [ ] 用户手动导入 QKBuilder V3 到 Quicker
- [ ] 运行 build.ps1 -JsonPath "HelloPopup.json"
- [ ] QuickerStarter 返回 BUILD_OK 或明确错误
- [ ] 如果失败，根据错误信息修正
