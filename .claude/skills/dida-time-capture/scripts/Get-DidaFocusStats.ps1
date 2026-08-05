<#
.SYNOPSIS
  拉取嘀嗒（dida365 中国版）专注统计并聚合输出。

.DESCRIPTION
  走 V2 非公开 web API（需 t cookie 认证），拉取指定日期范围的专注数据：
  - heatmap：每日专注时长
  - dist：按标签聚合的专注时长
  输出三段 Markdown（每日表 / 标签表 / 总计），可直接转成周报。

.PARAMETER From
  开始日期 YYYYMMDD，默认近 7 天。

.PARAMETER To
  结束日期 YYYYMMDD，默认今天。

.PARAMETER Token
  t cookie 值。缺省读 $env:DIDA_T_COOKIE。

.PARAMETER OutFile
  可选，把 Markdown 输出写入文件。

.PARAMETER Json
  可选，同时输出原始 JSON（heatmap 和 dist）便于调试。

.EXAMPLE
  .\Get-DidaFocusStats.ps1
  .\Get-DidaFocusStats.ps1 -From 20260801 -To 20260807
  .\Get-DidaFocusStats.ps1 -OutFile stats.md
#>
param(
    [string]$From,
    [string]$To,
    [string]$Token,
    [string]$OutFile,
    [switch]$Json
)

# 统一控制台编码为 UTF-8，避免中文输出乱码
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom

$BaseUrl = "https://api.dida365.com/api/v2"

# ---------- 参数处理 ----------
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = $env:DIDA_T_COOKIE
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "❌ 未配置 t cookie。请先运行 Set-DidaToken.ps1 配置 DIDA_T_COOKIE，或传 -Token 参数。" -ForegroundColor Red
    Write-Host "获取方法：浏览器登录 www.dida365.com → F12 → Application → Cookies → 复制 t 的值。" -ForegroundColor Yellow
    exit 1
}

if ([string]::IsNullOrWhiteSpace($To)) {
    $To = (Get-Date).ToString("yyyyMMdd")
}
if ([string]::IsNullOrWhiteSpace($From)) {
    $From = (Get-Date).AddDays(-6).ToString("yyyyMMdd")
}

# ---------- HTTP 会话（cookie 用 WebRequestSession 传递，PS5.1 可靠方式）----------
$XDevice = '{"platform":"web","os":"Linux x86_64","device":"Firefox 145.0","name":"","version":8006,"id":"6790a0b0c1d2e3f4a5b6c7d8","channel":"website","campaign":"","websocket":""}'
$Headers = @{
    "Content-Type" = "application/json"
    "User-Agent"   = "Mozilla/5.0 (X11; Linux x86_64; rv:145.0) Gecko/20100101 Firefox/145.0"
    "X-Device"     = $XDevice
}
# 用 WebRequestSession 承载 t cookie —— 直接放 Headers["Cookie"] 在 PS5.1 下会 401
$Session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$Cookie = New-Object System.Net.Cookie("t", $Token, "/", "api.dida365.com")
$Session.Cookies.Add($Cookie)

# ---------- HTTP 辅助函数（PS5.1 兼容，无 -SkipHttpErrorCheck）----------
function Invoke-DidaGet {
    param([string]$Path)
    $uri = "$BaseUrl$Path"
    try {
        $resp = Invoke-WebRequest -Uri $uri -Headers $Headers -WebSession $Session -Method GET -UseBasicParsing -TimeoutSec 15
        return @{ Status = [int]$resp.StatusCode; Body = $resp.Content }
    } catch {
        $status = $null
        if ($_.Exception.Response -ne $null) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        # 读错误响应体
        $errBody = ""
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errBody = $reader.ReadToEnd()
            $reader.Close()
        } catch { }
        return @{ Status = $status; Body = $errBody }
    }
}

# ---------- 拉取两个接口 ----------
Write-Host "拉取专注统计 $From ~ $To ..." -ForegroundColor Cyan

$heatmap = Invoke-DidaGet "/pomodoros/statistics/heatmap/$From/$To"
if ($heatmap.Status -eq 401 -or $heatmap.Body -match "user_not_sign_on") {
    Write-Host "❌ t cookie 无效或已过期。" -ForegroundColor Red
    Write-Host "请重新登录 www.dida365.com → F12 → Cookies → 复制 t 值 → 运行 Set-DidaToken.ps1。" -ForegroundColor Yellow
    exit 401
}
if ($heatmap.Status -ne 200) {
    Write-Host "❌ heatmap 接口失败 (HTTP $($heatmap.Status))：$($heatmap.Body.Substring(0, [Math]::Min(200, $heatmap.Body.Length)))" -ForegroundColor Red
    exit 1
}

$dist = Invoke-DidaGet "/pomodoros/statistics/dist/$From/$To"
if ($dist.Status -eq 401 -or $dist.Body -match "user_not_sign_on") {
    Write-Host "❌ t cookie 无效或已过期。" -ForegroundColor Red
    Write-Host "请重新登录 www.dida365.com → F12 → Cookies → 复制 t 值 → 运行 Set-DidaToken.ps1。" -ForegroundColor Yellow
    exit 401
}
if ($dist.Status -ne 200) {
    Write-Host "❌ dist 接口失败 (HTTP $($dist.Status))：$($dist.Body.Substring(0, [Math]::Min(200, $dist.Body.Length)))" -ForegroundColor Red
    exit 1
}

# ---------- 解析（防御式，未知结构打印原始 JSON）----------
$heatData = $null
$distData = $null
try {
    $heatData = $heatmap.Body | ConvertFrom-Json
} catch {
    Write-Host "❌ heatmap 返回无法解析：$($heatmap.Body.Substring(0, [Math]::Min(300, $heatmap.Body.Length)))" -ForegroundColor Red
    exit 1
}
try {
    $distData = $dist.Body | ConvertFrom-Json
} catch {
    Write-Host "❌ dist 返回无法解析：$($dist.Body.Substring(0, [Math]::Min(300, $dist.Body.Length)))" -ForegroundColor Red
    exit 1
}

# ---------- 聚合：每日专注 ----------
$dailyRows = @()
if ($heatData -is [System.Array]) {
    foreach ($item in $heatData) {
        $date = $item.date
        $dur = 0
        if ($null -ne $item.duration) { $dur = [int]$item.duration }
        if ($dur -gt 0) {
            $dailyRows += [PSCustomObject]@{ Date = "$date"; Minutes = [Math]::Round($dur / 60, 1); Hours = [Math]::Round($dur / 3600, 2) }
        }
    }
}

# ---------- 聚合：按标签 ----------
$tagRows = @()
$totalSeconds = 0
if ($null -ne $distData.tagDurations) {
    $props = $distData.tagDurations.PSObject.Properties
    foreach ($p in $props) {
        $sec = 0
        if ($null -ne $p.Value) { $sec = [int]$p.Value }
        if ($sec -gt 0) {
            $totalSeconds += $sec
            $tagRows += [PSCustomObject]@{ Tag = $p.Name; Minutes = [Math]::Round($sec / 60, 1); Hours = [Math]::Round($sec / 3600, 2) }
        }
    }
    $tagRows = $tagRows | Sort-Object Minutes -Descending
}

# ---------- 输出 Markdown ----------
$sb = New-Object System.Text.StringBuilder

[void]$sb.AppendLine("## ⏱ 专注统计（$From ~ $To）")
[void]$sb.AppendLine()

[void]$sb.AppendLine("### 每日专注")
if ($dailyRows.Count -eq 0) {
    [void]$sb.AppendLine("_该范围无专注记录_")
} else {
    [void]$sb.AppendLine("| 日期 | 分钟 | 小时 |")
    [void]$sb.AppendLine("|------|------|------|")
    foreach ($r in $dailyRows) {
        [void]$sb.AppendLine("| $($r.Date) | $($r.Minutes) | $($r.Hours) |")
    }
}
[void]$sb.AppendLine()

[void]$sb.AppendLine("### 按标签聚合")
if ($tagRows.Count -eq 0) {
    [void]$sb.AppendLine("_该范围无标签聚合数据_")
} else {
    [void]$sb.AppendLine("| 标签 | 分钟 | 小时 |")
    [void]$sb.AppendLine("|------|------|------|")
    foreach ($r in $tagRows) {
        [void]$sb.AppendLine("| $($r.Tag) | $($r.Minutes) | $($r.Hours) |")
    }
}
[void]$sb.AppendLine()

[void]$sb.AppendLine("### 总计")
[void]$sb.AppendLine("- 专注总时长：**$([Math]::Round($totalSeconds / 3600, 2)) 小时**（$([Math]::Round($totalSeconds / 60, 1)) 分钟）")
if ($dailyRows.Count -gt 0) {
    [void]$sb.AppendLine("- 有专注记录的天数：**$($dailyRows.Count) 天**")
}
[void]$sb.AppendLine()

# 原始 JSON（可选调试）
if ($Json) {
    [void]$sb.AppendLine("### 原始数据")
    $sb.AppendLine('<details><summary>heatmap</summary>') > $null
    [void]$sb.AppendLine('```json')
    [void]$sb.AppendLine(($heatData | ConvertTo-Json -Depth 5 -Compress))
    [void]$sb.AppendLine('```')
    $sb.AppendLine('</details>') > $null
    [void]$sb.AppendLine()
    $sb.AppendLine('<details><summary>dist</summary>') > $null
    [void]$sb.AppendLine('```json')
    [void]$sb.AppendLine(($distData | ConvertTo-Json -Depth 5 -Compress))
    [void]$sb.AppendLine('```')
    $sb.AppendLine('</details>') > $null
}

$out = $sb.ToString()

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    # 用 UTF-8 no BOM 写文件
    [System.IO.File]::WriteAllText((Resolve-Path (Split-Path $OutFile -Parent)).Path + "\" + (Split-Path $OutFile -Leaf), $out, $Utf8NoBom)
    Write-Host "✅ 已写入 $OutFile" -ForegroundColor Green
} else {
    Write-Output $out
}
