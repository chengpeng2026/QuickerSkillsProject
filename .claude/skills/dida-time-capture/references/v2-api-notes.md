# 嘀嗒 V2 API 技术笔记（dida365 中国版）

> 本文件记录 V2 专注统计接口的实测规格。V2 是非公开 web API，官方文档未收录，字段以真实响应为准，首跑后如有出入需校正此处。

## 认证方式：t cookie

- **获取**：浏览器登录 `https://www.dida365.com` → F12 DevTools → Application → Cookies → 复制名为 `t` 的 cookie 值
- **存放**：User 级环境变量 `DIDA_T_COOKIE`（用 `Set-DidaToken.ps1` 或系统设置）
- **过期**：约 30 天。过期后接口返回 401 / `user_not_sign_on`，重新复制即可
- **安全红线**：`t` cookie 是登录凭据，绝不硬编码进脚本、绝不提交 git、绝不写进 `.mcp.json`

## 请求头（所有 V2 请求）

```
Cookie: t=<session_token>
Content-Type: application/json
User-Agent: Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0
X-Device: {"platform":"web","os":"Linux x86_64","device":"Firefox 145.0","name":"","version":8006,"id":"6790a0b0c1d2e3f4a5b6c7d8","channel":"website","campaign":"","websocket":""}
```

`X-Device` 的 `id` 是设备指纹，可保持固定常量（服务端按此识别设备）。

## 接口

Base URL：`https://api.dida365.com/api/v2`

| 方法 | 路径 | 说明 | 返回 |
|------|------|------|------|
| GET | `/pomodoros/statistics/heatmap/{from}/{to}` | 每日专注时长 | `[{date: "YYYYMMDD", duration: 秒}, ...]` |
| GET | `/pomodoros/statistics/dist/{from}/{to}` | 按标签聚合专注时长 | `{"tagDurations": {"标签名": 秒, ...}}` |
| GET | `/statistics/general` | 生产力统计（分数/等级/连续天数） | 对象 |
| GET | `/user/status` | 账号状态（inbox ID / pro） | 对象 |

日期格式：`YYYYMMDD`（无分隔符，如 `20260801`）

## 错误码

| 场景 | 现象 |
|------|------|
| 未登录/过期 | dist 返回 `{"errorCode":"user_not_sign_on"}`；heatmap 返回 HTTP 401 |
| 登录失败 | POST `/user/signon` 返回 `username_password_not_match` |

## 关联

- 任务级明细（focusSummaries）走 V1 open API，见 `E:\Aiku2026\嘀嗒清单 Open API 完整技术参考.md`
- 采集闭环与试点：见 `E:\Aiku2026\执行时间采集协议.md`
