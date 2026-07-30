# 01 — ApiDiag R3: 精确探测 ActionItem2 构造链路

**What to build:** 运行一次增强版反射诊断 (ApiDiag R3)，探测出具体用于构造 ActionItem2 的类型和方法签名：
- ActionItem2 的所有属性详情
- OperationPresentation 的属性
- IOperationPayload 的实现类 (XAction payload)
- ActionItem2Converter 的方法 (了解序列化格式)
- ActionItem2Store 的字段 (了解依赖)

**Blocked by:** None — can start immediately.

**Status:** ready-for-agent

- [ ] ApiDiag R3 代码写入 temp/ApiDiagR3.cs
- [ ] 用户在 Quicker 中运行 ApiDiag R3
- [ ] 输出结果写入 Desktop/quicker_api_diag_r3.txt
- [ ] 从输出中确定 XAction payload 类型名和属性
