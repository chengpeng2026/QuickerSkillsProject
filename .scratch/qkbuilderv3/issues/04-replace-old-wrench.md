# 04 — QKBuilder V3: 覆盖老扳手 ActionId + 支持更新

**What to build:**
1. QKBuilder V3 使用与老扳手相同的 ActionId (`f10c350a-f5cf-4726-543d-08de40f9c963`)，覆盖旧版
2. 支持更新已有动作 (ActionId 已填充时走 Update 分支)
3. 清理临时诊断文件，提交到仓库

**Blocked by:** 03-e2e-validation

**Status:** ready-for-agent

- [ ] QKBuilder V3 使用 f10c350a ID 覆盖老扳手
- [ ] build.ps1 无需修改即可使用
- [ ] Update 模式验证通过 (修改已存在动作的 JSON/CS 后 build)
- [ ] 代码提交到 git
