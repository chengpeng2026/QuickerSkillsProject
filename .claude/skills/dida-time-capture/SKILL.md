---
name: dida-time-capture
description: 采集嘀嗒/滴答清单任务的「实际执行时间」（专注计时）用于校准预估。支持双路径：V2 专注统计接口拉按标签聚合 + V1 Open API 读任务级 focusSummaries 明细。触发场景：「执行时间周报」「时间校准」「试点评估」「拉专注统计」「拉专注时间」。
---

# 嘀嗒执行时间采集（dida-time-capture）

采集嘀嗒清单任务的**实际执行耗时**，与五维标签的**预估时长**对照，校准任务规划。完整闭环见 Obsidian `[[执行时间采集协议]]`。

## 一、双路径决策表

| 需求 | 路径 | 工具 |
|------|------|------|
| 按标签聚合专注时长 / 每日专注 | **V2** | `scripts/Get-DidaFocusStats.ps1` |
| 任务级明细 / 覆盖率 | **V1** | dida365 MCP 读 `focusSummaries` |
| 试点周报 | V2 聚合 + V1 补覆盖率 | 两者结合 |

**V2** = 标签级（省事，丢失任务明细）；**V1** = 任务级（精确到每个任务，但需遍历项目）。

## 二、t cookie 配置（一次性）

V2 需要登录 cookie，约 30 天过期：

1. 浏览器登录 `https://www.dida365.com`
2. F12 → Application → Cookies → 复制名为 `t` 的值
3. 运行 `Set-DidaToken.ps1` 写入 User 环境变量 `DIDA_T_COOKIE`，或手动设环境变量
4. 新开的 Claude 会话才能读到（已开会话需重启）

> 🔒 **安全红线**：t cookie 是登录凭据。绝不硬编码进脚本、绝不提交 git、绝不写进 `.mcp.json`。只存 User 级环境变量。

## 三、V2 聚合流程（按标签）

```powershell
# 拉近 7 天专注统计
powershell -File .claude/skills/dida-time-capture/scripts/Get-DidaFocusStats.ps1

# 指定日期范围（YYYYMMDD）
powershell -File .claude/skills/dida-time-capture/scripts/Get-DidaFocusStats.ps1 -From 20260801 -To 20260807

# 输出到文件
powershell -File .claude/skills/dida-time-capture/scripts/Get-DidaFocusStats.ps1 -OutFile stats.md
```

输出三段 Markdown：每日表 / 标签表 / 总计（含可选覆盖率行）。

## 四、V1 明细流程（任务级）

用 dida365 MCP 工具：

```
dida_get_completed_tasks (startDate/endDate 必须带 +0800 时区)
  → 每个任务读 focusSummaries 字段
  → {pomodoroDuration, stopwatchDuration} 单位秒
  → 求和非零者 = 实际专注时长
```

**覆盖率算法**：`有 focusSummaries 非零的任务数 / 该范围完成任务数`。覆盖率过低（<50%）时，V2 聚合信号弱，报告需标注。

## 五、试点对接（岩土复习）

试点方案（1 周）见 `[[执行时间采集协议]]`：
- 岩土复习任务 → 建任务存预估 + 嘀嗒点「开始专注」
- 周末触发本 skill → V2 拉标签聚合 + V1 拉任务明细补覆盖率
- 出「预估 vs 实际」对照 + 覆盖率 → 评估数据链路

## 六、周报输出契约

输出块用以下结构，可直接转成 `Reviews/Weekly/` 周报：

```markdown
## ⏱ 执行时间周报（YYYY-WNN）

### 专注时长（按标签）
| 标签 | 专注时长 |
|------|---------|

### 预估 vs 实际（试点）
| 任务 | 预估 | 实际 | 覆盖率 |
|------|------|------|--------|

### 校准建议
- 估/实比 > 1.2 → 该类型预估上浮
- 估/实比 < 0.8 → 下调
- 覆盖率 < 50% → 数据不足，暂不校准
```

## 七、边界与注意事项

- V2 接口是**非公开 web API**，官方文档未收录，字段可能变，首跑后校正 `references/v2-api-notes.md`
- `github.com` 在企业网络被拦截，参考实现（tick-mcp）配方以本文件记录为准
- 未配置 t cookie 时 V2 会报 401 / `user_not_sign_on`，按提示重新复制即可，不影响 V1

## 相关笔记

- Obsidian `[[执行时间采集协议]]` — 估→采→聚→校闭环与试点
- Obsidian `[[嘀嗒清单 Open API 完整技术参考]]` — V1 open API 全参考
- `references/v2-api-notes.md` — V2 接口实测规格
