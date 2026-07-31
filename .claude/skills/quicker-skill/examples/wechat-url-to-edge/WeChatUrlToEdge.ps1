# ============================================================================
# 微信浏览器 URL 提取 + Edge 打开 (基础版)
# Quicker【运行 PowerShell】模块专用
# ============================================================================
# 功能：识别微信PC内置浏览器 → Ctrl+L 选中地址栏 → Ctrl+C 复制 →
#       校验 URL 合法性 → 启动 Edge 打开
# 兼容：最新版 PC微信 / 企业微信 / 微信多开
# ============================================================================

# ============================================================================
# [可调参数] — 根据电脑性能调整延时 (单位: 毫秒)
# ============================================================================
$DELAY_BEFORE_CTRL_L  = 200   # 切换到窗口后等待(防止窗口未获得焦点)
$DELAY_BETWEEN_KEYS   = 100   # Ctrl+L 与 Ctrl+C 之间的停顿(地址栏加载慢的加大)
$DELAY_BEFORE_CLIP    = 200   # 复制后等待剪贴板就绪
$DELAY_BEFORE_EDGE    = 300   # Edge 启动前缓冲(避免窗口切换抖动)

# ============================================================================
# [可调参数] — 输入法干扰保护
# ============================================================================
$SENDKEYS_METHOD      = "SendWait"  # "SendWait" 或 "SendInput"(更快但不稳定)
$ESCAPE_BEFORE_ACTION = $true       # 先按 Escape 退出可能的输入法状态

# ============================================================================
# [可调参数] — Edge 浏览器路径
# ============================================================================
$EDGE_PATHS = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:LOCALAPPDATA}\Microsoft\Edge\Application\msedge.exe"
)

# ============================================================================
# [可调参数] — 微信进程检测关键词
# ============================================================================
$WECHAT_PROCESS_NAMES = @("WeChat", "Weixin", "WXWork")  # PC微信 / 旧版 / 企业微信

# ============================================================================
# 加载必需程序集
# ============================================================================
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ============================================================================
# 日志函数 (运行时输出到 Quicker 文本变量)
# ============================================================================
$Global:LogMessages = @()
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    $entry = "[$timestamp][$Level] $Message"
    $Global:LogMessages += $entry
}

# ============================================================================
# 步骤1：查找微信浏览器窗口
# ============================================================================
function Find-WeChatBrowserWindow {
    Write-Log "正在查找微信浏览器窗口..."

    # 获取所有窗口进程
    $wechatWindows = @()
    foreach ($procName in $WECHAT_PROCESS_NAMES) {
        try {
            $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
            foreach ($proc in $procs) {
                if ($proc.MainWindowHandle -ne 0 -and $proc.MainWindowTitle -ne "") {
                    $wechatWindows += @{
                        Handle      = $proc.MainWindowHandle
                        Title       = $proc.MainWindowTitle
                        ProcessName = $proc.ProcessName
                        Id          = $proc.Id
                    }
                }
            }
        } catch { }
    }

    # 筛选浏览器类型窗口 (微信内置浏览器标题通常较长、不含"微信"二字但属于WeChat进程)
    # 过滤掉聊天主窗口(标题含"微信"关键词)
    $browserWindows = $wechatWindows | Where-Object {
        $_.Title -ne "" -and
        # 排除微信主窗口(通常标题就是"微信")，实际浏览器窗口标题是网页标题
        $_.Title -notmatch '^微信$' -and
        $_.Title -notmatch '^WeChat$' -and
        # 排除文件传输等辅助窗口
        $_.Title -notmatch '文件传输|聊天文件|图片|视频|会话'
    }

    if ($browserWindows.Count -eq 0) {
        Write-Log "未找到微信浏览器窗口，尝试使用前台窗口..." "WARN"

        # 回退方案：信任当前前台窗口(可能是模态浏览器)
        $hwnd = [System.Windows.Forms.Form]::ActiveForm
        if ($null -eq $hwnd) {
            # 通过 .NET 方法获取前台窗口句柄
            $signature = '[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();'
            $type = Add-Type -MemberDefinition $signature -Name "Win32Foreground" -Namespace "Win32" -PassThru
            $handle = $type::GetForegroundWindow()
            if ($handle -ne [IntPtr]::Zero) {
                return @{ Handle = $handle; Title = "(前台窗口)"; ProcessName = "Unknown" }
            }
        }
        return $null
    }

    # 如果有多个浏览器窗口，选择第一个(通常是最新的)
    $target = $browserWindows[0]
    Write-Log "找到目标窗口: [$($target.ProcessName)] $($target.Title)"
    return $target
}

# ============================================================================
# 步骤2：激活窗口并赋予焦点
# ============================================================================
function Set-ActiveWindow {
    param($WindowInfo)

    if ($null -eq $WindowInfo) { return $false }

    try {
        $signature = @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
'@
        $type = Add-Type -MemberDefinition $signature -Name "Win32Window" -Namespace "Win32" -PassThru

        # 如果窗口最小化，先恢复
        $type::ShowWindow($WindowInfo.Handle, 9) # SW_RESTORE
        Start-Sleep -Milliseconds 50

        # 强制前置
        $result = $type::SetForegroundWindow($WindowInfo.Handle)
        if (-not $result) {
            Write-Log "SetForegroundWindow 失败，尝试 Alt 键唤起..." "WARN"
            # Alt 键技巧：部分情况下 Windows 限制前台切换，先模拟按键
            [System.Windows.Forms.SendKeys]::SendWait("%")
            Start-Sleep -Milliseconds 100
            $result = $type::SetForegroundWindow($WindowInfo.Handle)
        }

        Start-Sleep -Milliseconds $DELAY_BEFORE_CTRL_L
        Write-Log "窗口激活 $(if($result){'成功'}else{'失败'})"
        return $result
    } catch {
        Write-Log "窗口激活异常: $_" "ERROR"
        return $false
    }
}

# ============================================================================
# 步骤3：模拟快捷键 Ctrl+L → Ctrl+C
# ============================================================================
function Invoke-UrlCopySequence {
    try {
        # 3.0 输入法干扰保护：先按 Escape 退出输入法状态
        if ($ESCAPE_BEFORE_ACTION) {
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 50
            Write-Log "已发送 Escape (输入法干扰保护)"
        }

        # 3.1 清除剪贴板旧内容 (避免误判)
        [System.Windows.Forms.Clipboard]::Clear()
        Start-Sleep -Milliseconds 50

        # 3.2 Ctrl+L 选中地址栏
        Write-Log "发送 Ctrl+L (选中地址栏)..."
        [System.Windows.Forms.SendKeys]::SendWait("^l")
        Start-Sleep -Milliseconds $DELAY_BETWEEN_KEYS

        # 3.3 Ctrl+A 全选(确保完整选中，部分微信内置浏览器Ctrl+L后可能只聚焦但不全选)
        Write-Log "发送 Ctrl+A (全选)..."
        [System.Windows.Forms.SendKeys]::SendWait("^a")
        Start-Sleep -Milliseconds 100

        # 3.4 Ctrl+C 复制
        Write-Log "发送 Ctrl+C (复制)..."
        [System.Windows.Forms.SendKeys]::SendWait("^c")
        Start-Sleep -Milliseconds $DELAY_BEFORE_CLIP

        Write-Log "按键序列执行完毕"
        return $true
    } catch {
        Write-Log "按键模拟异常: $_" "ERROR"
        return $false
    }
}

# ============================================================================
# 步骤4：剪贴板读取 + URL 合法性校验
# ============================================================================
function Get-ValidatedUrl {
    param([int]$MaxRetries = 3)

    for ($retry = 1; $retry -le $MaxRetries; $retry++) {
        try {
            # 使用 STA 模式读取剪贴板 (Quicker 模块已在 STA 下运行)
            $clipText = [System.Windows.Forms.Clipboard]::GetText(
                [System.Windows.Forms.TextDataFormat]::Text
            )

            if ([string]::IsNullOrWhiteSpace($clipText)) {
                Write-Log "剪贴板为空 (重试 $retry/$MaxRetries)..." "WARN"
                Start-Sleep -Milliseconds 200
                continue
            }

            # 去除前后空白字符和可能的换行
            $url = $clipText.Trim()

            # 清理可能的特殊字符 (部分应用复制URL时会带上不可见字符)
            $url = $url -replace '[ -  -‏ - ﻿]', ''

            Write-Log "剪贴板内容: $url"

            # URL 合法性校验
            if ($url -match '^https?://') {
                Write-Log "URL 校验通过: $url"
                return $url
            }

            # 尝试提取文本中的 URL (部分情况复制的文本可能夹杂了URL)
            $urlPattern = 'https?://[^\s"''<>，。；;]+'
            $matches = [regex]::Matches($url, $urlPattern)
            if ($matches.Count -gt 0) {
                $extractedUrl = $matches[0].Value.TrimEnd('.', ',', ')', ']', '，', '。')
                Write-Log "从文本中提取到 URL: $extractedUrl"
                return $extractedUrl
            }

            Write-Log "剪贴板内容不是有效 URL" "WARN"
            return $null

        } catch {
            Write-Log "剪贴板读取异常 (重试 $retry/$MaxRetries): $_" "ERROR"
            Start-Sleep -Milliseconds 300
        }
    }

    return $null
}

# ============================================================================
# 步骤5：Edge 安全启动 (避开 cmd /c start)
# ============================================================================
function Invoke-EdgeBrowser {
    param([string]$Url)

    # 5.1 定位 Edge 可执行文件
    $edgePath = $null
    foreach ($tryPath in $EDGE_PATHS) {
        # 展开环境变量
        $expanded = [Environment]::ExpandEnvironmentVariables($tryPath)
        if (Test-Path -LiteralPath $expanded) {
            $edgePath = $expanded
            Write-Log "找到 Edge: $edgePath"
            break
        }
    }

    if ($null -eq $edgePath) {
        # 最后回退：从注册表读取默认浏览器路径
        try {
            $regPath = "HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"
            $progId = (Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue).ProgId
            if ($progId) {
                $cmdPath = "HKCU:\Software\Classes\$progId\shell\open\command"
                $command = (Get-ItemProperty -Path $cmdPath -ErrorAction SilentlyContinue).'(default)'
                if ($command -match '"(.+?\.exe)"') {
                    $edgePath = $matches[1]
                }
            }
        } catch { }

        if ($null -eq $edgePath) {
            Write-Log "无法定位 Edge 浏览器路径" "ERROR"
            return $false
        }
    }

    # 5.2 启动 Edge (直接用 .exe + 引号包裹 URL, 避免 shell 注入)
    try {
        Start-Sleep -Milliseconds $DELAY_BEFORE_EDGE

        # 使用 Start-Process 而非 cmd /c start (根据避坑指南第3条)
        # 引号包裹 URL 防止 & 等特殊字符被解析
        $process = Start-Process -FilePath $edgePath -ArgumentList """$Url""" -WindowStyle Normal -PassThru

        if ($process -and $process.Id) {
            Write-Log "Edge 启动成功: PID=$($process.Id), URL=$Url"
            return $true
        } else {
            Write-Log "Edge 启动结果未知" "WARN"
            return $false
        }
    } catch {
        Write-Log "Edge 启动失败: $_" "ERROR"

        # 回退方案：rundll32 (Windows 底层 URL 分发)
        try {
            Write-Log "尝试回退方案: rundll32..."
            Start-Process -FilePath "rundll32.exe" -ArgumentList "url.dll,FileProtocolHandler $Url"
        } catch { }

        return $false
    }
}

# ============================================================================
# 步骤6：提示框 (WinForms MessageBox, 适配后台线程)
# ============================================================================
function Show-Notification {
    param([string]$Message, [string]$Title = "微信→Edge", [bool]$IsError = $false)

    try {
        $icon = if ($IsError) { "Error" } else { "Information" }
        [System.Windows.Forms.MessageBox]::Show($Message, $Title,
            [System.Windows.Forms.MessageBoxButtons]::OK,
            [System.Windows.Forms.MessageBoxIcon]::$icon)
    } catch {
        Write-Log "弹窗失败: $_" "ERROR"
    }
}

# ============================================================================
# 主流程
# ============================================================================
function Main {
    Write-Log "========== 微信→Edge URL提取器 启动 =========="
    Write-Log "延时配置: CtrlL前=${DELAY_BEFORE_CTRL_L}ms 按键间=${DELAY_BETWEEN_KEYS}ms 剪贴板=${DELAY_BEFORE_CLIP}ms"

    # 1. 查找微信浏览器窗口
    $window = Find-WeChatBrowserWindow
    if ($null -eq $window) {
        $msg = "未检测到微信浏览器窗口。`n`n请确保：`n1. 微信内置浏览器已打开`n2. 浏览器窗口在前台显示`n3. 当前页面已加载完成"
        Write-Log $msg "ERROR"
        Show-Notification -Message $msg -IsError $true
        return "FAIL:NO_WINDOW"
    }

    # 2. 激活窗口
    $activated = Set-ActiveWindow -WindowInfo $window
    if (-not $activated) {
        Write-Log "窗口激活失败，仍将继续尝试按键..." "WARN"
    }

    # 3. 模拟 Ctrl+L → Ctrl+C
    $keyResult = Invoke-UrlCopySequence
    if (-not $keyResult) {
        Show-Notification -Message "按键模拟失败，请检查输入法状态" -IsError $true
        return "FAIL:KEY_SEQUENCE"
    }

    # 4. 校验 URL
    $url = Get-ValidatedUrl
    if ($null -eq $url) {
        $msg = "未能获取到有效网页链接。`n`n可能原因：`n1. 地址栏未被正确选中`n2. 页面还在加载中`n3. 键盘布局/输入法冲突"
        Write-Log $msg "ERROR"
        Show-Notification -Message $msg -IsError $true
        return "FAIL:INVALID_URL"
    }

    # 5. 启动 Edge
    $edgeResult = Invoke-EdgeBrowser -Url $url
    if (-not $edgeResult) {
        Show-Notification -Message "Edge 浏览器启动失败，但 URL 已保留在剪贴板。`n`n可手动粘贴到浏览器打开：$url" -IsError $true
        return "FAIL:EDGE_LAUNCH"
    }

    Write-Log "========== 成功完成 =========="
    return "OK:$url"
}

# ============================================================================
# 执行 (返回结果给 Quicker)
# ============================================================================
try {
    $result = Main
    Write-Output $result
    # 输出完整日志供调试
    Write-Output "--- LOG ---"
    $Global:LogMessages | ForEach-Object { Write-Output $_ }
} catch {
    $errMsg = "未预期异常: $_"
    Write-Output $errMsg
    try {
        [System.Windows.Forms.MessageBox]::Show($errMsg, "微信→Edge 错误", "OK", "Error")
    } catch { }
}
