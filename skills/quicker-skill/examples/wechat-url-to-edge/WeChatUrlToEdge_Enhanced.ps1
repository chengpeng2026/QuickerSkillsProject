# ============================================================================
# 微信浏览器 URL 提取 + Edge 打开 (增强版)
# Quicker【运行 PowerShell】模块专用
# ============================================================================
# 功能：识别微信PC内置浏览器 → Ctrl+L 选中地址栏 → Ctrl+C 复制 →
#       校验 URL 合法性 → 启动 Edge 打开
# 增强：Edge 无痕模式 / 自动重试机制 / 详细日志 / 多微信窗口选择
# 兼容：最新版 PC微信 / 企业微信 / 微信多开
# ============================================================================

# ============================================================================
# [可调参数] — 延时配置 (单位: 毫秒)
# ============================================================================
$DELAY_BEFORE_CTRL_L  = 200   # 切换到窗口后等待
$DELAY_BETWEEN_KEYS   = 100   # Ctrl+L 与 Ctrl+C 之间的停顿
$DELAY_BEFORE_CLIP    = 200   # 复制后等待剪贴板就绪
$DELAY_BEFORE_EDGE    = 300   # Edge 启动前缓冲

# ============================================================================
# [可调参数] — 功能开关
# ============================================================================
$INCOGNITO_MODE       = $false # Edge 无痕模式 (InPrivate)
$MAX_RETRIES          = 3      # 全局最大重试次数
$RETRY_DELAY_MS       = 500    # 重试间隔毫秒
$ENABLE_DETAILED_LOG  = $true  # 启用详细日志输出
$HIDE_POWERSHELL      = $true  # 隐藏 PowerShell 窗口 (Quicker 模块已默认隐藏)

# ============================================================================
# [可调参数] — 输入法干扰保护
# ============================================================================
$ESCAPE_BEFORE_ACTION = $true  # 先按 Escape 退出输入法状态
$SWITCH_IME_TO_EN     = $true  # 尝试切换到英文输入法 (需要系统支持)

# ============================================================================
# [可调参数] — 窗口检测
# ============================================================================
$WECHAT_PROCESS_NAMES = @("WeChat", "Weixin", "WXWork")
$USE_FOREGROUND_FALLBACK = $true  # 找不到微信窗口时信任前台窗口

# ============================================================================
# [可调参数] — Edge 浏览器路径 (按优先级排序)
# ============================================================================
$EDGE_PATHS = @(
    "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
    "${env:LOCALAPPDATA}\Microsoft\Edge\Application\msedge.exe"
)

# ============================================================================
# 加载必需程序集
# ============================================================================
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ============================================================================
# Win32 API 声明 (一次性加载)
# ============================================================================
$Win32Code = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32Api
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint Flags);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    public const uint GW_HWNDNEXT = 2;
    public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    public const uint KLF_ACTIVATE = 1;
}
'@
Add-Type -TypeDefinition $Win32Code -ErrorAction SilentlyContinue

# ============================================================================
# 日志系统
# ============================================================================
$Global:LogMessages = [System.Collections.ArrayList]::new()

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss.fff"
    $entry = "[$timestamp][$Level] $Message"
    [void]$Global:LogMessages.Add($entry)
    if (-not $HIDE_POWERSHELL) {
        Write-Host $entry
    }
}

# ============================================================================
# 步骤1：智能查找微信浏览器窗口 (增强版 — 多窗口遍历)
# ============================================================================
function Find-WeChatBrowserWindow {
    Write-Log "正在扫描微信进程窗口..."

    # 收集所有候选窗口
    $allWindows = [System.Collections.ArrayList]::new()

    foreach ($procName in $WECHAT_PROCESS_NAMES) {
        try {
            $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
            foreach ($proc in $procs) {
                if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { continue }
                $title = $proc.MainWindowTitle
                if ([string]::IsNullOrWhiteSpace($title)) { continue }

                [void]$allWindows.Add(@{
                    Handle      = $proc.MainWindowHandle
                    Title       = $title
                    ProcessName = $proc.ProcessName
                    Id          = $proc.Id
                    MainWindow  = $true
                })
            }
        } catch { }
    }

    # 如果进程级窗口不足，枚举所有顶层窗口
    if ($allWindows.Count -le 1) {
        Write-Log "进程级窗口较少，枚举所有可见窗口..." "DEBUG"

        $enumWindows = {
            param([IntPtr]$hWnd, [IntPtr]$lParam)
            if (-not [Win32Api]::IsWindowVisible($hWnd)) { return $true }

            $sb = New-Object System.Text.StringBuilder(256)
            [Win32Api]::GetWindowText($hWnd, $sb, $sb.Capacity) | Out-Null
            $title = $sb.ToString()
            if ([string]::IsNullOrWhiteSpace($title)) { return $true }

            $procId = 0
            [Win32Api]::GetWindowThreadProcessId($hWnd, [ref]$procId) | Out-Null

            foreach ($procName in $WECHAT_PROCESS_NAMES) {
                try {
                    $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
                    if ($proc -and $proc.ProcessName -eq $procName) {
                        [void]$allWindows.Add(@{
                            Handle      = $hWnd
                            Title       = $title
                            ProcessName = $procName
                            Id          = $procId
                            MainWindow  = $false
                        })
                    }
                } catch { }
            }
            return $true
        }

        $callback = [Win32Api+EnumWindowsProc]$enumWindows
        [Win32Api]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    }

    Write-Log "找到 $($allWindows.Count) 个微信相关窗口"

    # 过滤逻辑：排除主窗口，保留浏览器窗口
    $browserWindows = $allWindows | Where-Object {
        $t = $_.Title
        # 排除聊天主界面
        $t -notmatch '^微信$|^WeChat$|^企业微信$' -and
        # 排除系统/辅助窗口
        $t -notmatch '微信\(调试\)|文件传输|聊天文件|图片和视频|设置$|关于' -and
        # 排除聊天窗口 (格式: "联系人名" 短标题)
        $t.Length -gt 6
    }

    if ($browserWindows.Count -eq 0) {
        Write-Log "未匹配到浏览器窗口，尝试前台窗口回退..." "WARN"
        if ($USE_FOREGROUND_FALLBACK) {
            $fgHandle = [Win32Api]::GetForegroundWindow()
            if ($fgHandle -ne [IntPtr]::Zero) {
                $sb = New-Object System.Text.StringBuilder(256)
                [Win32Api]::GetWindowText($fgHandle, $sb, $sb.Capacity) | Out-Null
                Write-Log "回退使用前台窗口: $($sb.ToString())"
                return @{ Handle = $fgHandle; Title = $sb.ToString(); ProcessName = "Foreground" }
            }
        }
        return $null
    }

    # 多窗口处理：选择最可能的浏览器窗口
    # 优先级：标题最长的 > 网页关键词 > 第一个
    $target = $browserWindows | Sort-Object { $_.Title.Length } -Descending | Select-Object -First 1
    Write-Log "选择目标窗口 (#$($allWindows.Count)窗口中): [$($target.ProcessName)] $($target.Title)"
    return $target
}

# ============================================================================
# 步骤2：激活窗口
# ============================================================================
function Set-ActiveWindow {
    param($WindowInfo)
    if ($null -eq $WindowInfo) { return $false }

    try {
        # 恢复最小化窗口
        [Win32Api]::ShowWindow($WindowInfo.Handle, 9) | Out-Null  # SW_RESTORE
        Start-Sleep -Milliseconds 50

        # 强制切换前台
        $result = [Win32Api]::SetForegroundWindow($WindowInfo.Handle)
        if (-not $result) {
            Write-Log "SetForegroundWindow 被阻止，尝试键鼠辅助..." "WARN"
            # Windows 安全机制可能阻止前台切换，通过 Alt 键绕过
            [System.Windows.Forms.SendKeys]::SendWait("%")
            Start-Sleep -Milliseconds 150
            $result = [Win32Api]::SetForegroundWindow($WindowInfo.Handle)
        }

        Start-Sleep -Milliseconds $DELAY_BEFORE_CTRL_L
        Write-Log "窗口激活结果: $(if($result){'成功'}else{'失败(继续尝试)'})"
        return $true  # 即使 SetForegroundWindow 失败也不终止，按键可能仍然有效
    } catch {
        Write-Log "窗口激活异常: $_" "ERROR"
        return $false
    }
}

# ============================================================================
# 步骤3：模拟快捷键 Ctrl+L → Ctrl+C (增强版)
# ============================================================================
function Invoke-UrlCopySequence {
    param([int]$Attempt = 1)

    Write-Log "按键序列 — 第 $Attempt 次尝试"

    try {
        # 3.0 输入法干扰保护
        if ($ESCAPE_BEFORE_ACTION) {
            [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
            Start-Sleep -Milliseconds 80
            Write-Log "→ Escape (退出输入法)"
        }

        # 3.0.1 可选：切换到英文键盘布局
        if ($SWITCH_IME_TO_EN) {
            try {
                [Win32Api]::LoadKeyboardLayout("00000409", [Win32Api]::KLF_ACTIVATE) | Out-Null
                Start-Sleep -Milliseconds 50
                Write-Log "→ 切换到英文键盘布局"
            } catch {
                Write-Log "键盘布局切换失败(忽略)" "WARN"
            }
        }

        # 3.1 清空剪贴板
        [System.Windows.Forms.Clipboard]::Clear()
        Start-Sleep -Milliseconds 80

        # 3.2 先按 F6 尝试聚焦地址栏 (部分微信版本支持 F6)
        Write-Log "→ F6 (聚焦地址栏)"
        [System.Windows.Forms.SendKeys]::SendWait("{F6}")
        Start-Sleep -Milliseconds 100

        # 3.3 若 F6 失败，Ctrl+L 作为主要方案
        Write-Log "→ Ctrl+L (地址栏全选)"
        [System.Windows.Forms.SendKeys]::SendWait("^l")
        Start-Sleep -Milliseconds $DELAY_BETWEEN_KEYS

        # 3.4 确保全选：Ctrl+A
        Write-Log "→ Ctrl+A (全选)"
        [System.Windows.Forms.SendKeys]::SendWait("^a")
        Start-Sleep -Milliseconds 80

        # 3.5 Ctrl+C 复制
        Write-Log "→ Ctrl+C (复制)"
        [System.Windows.Forms.SendKeys]::SendWait("^c")
        Start-Sleep -Milliseconds $DELAY_BEFORE_CLIP

        Write-Log "按键序列完成"
        return $true
    } catch {
        Write-Log "按键异常: $_" "ERROR"
        return $false
    }
}

# ============================================================================
# 步骤4：剪贴板读取 + URL 校验 (增强版)
# ============================================================================
function Get-ValidatedUrl {
    param([int]$MaxRetries = 5)

    $retryDelay = 200  # ms

    for ($retry = 1; $retry -le $MaxRetries; $retry++) {
        try {
            # 检查剪贴板是否包含文本
            if (-not [System.Windows.Forms.Clipboard]::ContainsText()) {
                Write-Log "剪贴板不含文本 (重试 $retry/$MaxRetries)" "WARN"
                Start-Sleep -Milliseconds $retryDelay
                $retryDelay += 100  # 递增延时
                continue
            }

            $clipText = [System.Windows.Forms.Clipboard]::GetText(
                [System.Windows.Forms.TextDataFormat]::Text
            )

            if ([string]::IsNullOrWhiteSpace($clipText)) {
                Write-Log "剪贴板文本为空 (重试 $retry/$MaxRetries)" "WARN"
                Start-Sleep -Milliseconds $retryDelay
                $retryDelay += 100
                continue
            }

            # 清理文本
            $url = $clipText.Trim()
            # 移除不可见控制字符 (Unicode 控制区)
            $url = $url -replace '[\p{C}-[\t\r\n]]', ''
            # 移除零宽字符
            $url = $url -replace '[​-‍﻿]', ''

            Write-Log "剪贴板原始内容: [$($clipText.Substring(0, [Math]::Min(200, $clipText.Length)))]" "DEBUG"
            Write-Log "清理后内容: [$([string]::Join('', $url.ToCharArray() | ForEach-Object { "0x{0:X2} " -f [int]$_ }))]" "DEBUG"
            Write-Log "清理后 URL: $url"

            # URL 合法性校验
            if ($url -match '^https?://[^\s]+') {
                $cleanUrl = $url -replace '\s.*', ''  # 截取到第一个空格前
                Write-Log "✓ URL 校验通过: $cleanUrl"
                return $cleanUrl
            }

            # 尝试提取嵌入的 URL
            $urlPattern = 'https?://[^\s"' + "'" + '<>，。；;、]+'
            $matches = [regex]::Matches($url, $urlPattern)
            if ($matches.Count -gt 0) {
                $extractedUrl = ($matches[0].Value -split '\s')[0].TrimEnd('.', ',', ')', ']', '，', '。')
                Write-Log "从文本提取 URL: $extractedUrl"
                return $extractedUrl
            }

            Write-Log "✗ 不是有效 HTTP(S) URL" "WARN"
            return $null

        } catch {
            Write-Log "剪贴板读取异常 (重试 $retry/$MaxRetries): $_" "ERROR"
            Start-Sleep -Milliseconds ($retryDelay + 100)
        }
    }

    return $null
}

# ============================================================================
# 步骤5：Edge 启动 (增强版 — 支持无痕模式)
# ============================================================================
function Invoke-EdgeBrowser {
    param(
        [string]$Url,
        [bool]$Incognito = $INCOGNITO_MODE
    )

    # 5.1 定位 Edge
    $edgePath = $null
    foreach ($tryPath in $EDGE_PATHS) {
        $expanded = [Environment]::ExpandEnvironmentVariables($tryPath)
        if (Test-Path -LiteralPath $expanded) {
            $edgePath = $expanded
            break
        }
    }

    # 注册表回退
    if ($null -eq $edgePath) {
        Write-Log "文件路径未找到，尝试注册表查询..." "WARN"
        $regPaths = @(
            "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe",
            "HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice"
        )

        foreach ($rp in $regPaths) {
            try {
                # 方式1: App Paths
                if ($rp -match 'App Paths') {
                    $val = (Get-ItemProperty -Path $rp -ErrorAction SilentlyContinue).'(default)'
                    if ($val -and (Test-Path -LiteralPath $val)) {
                        $edgePath = $val
                        break
                    }
                }
                # 方式2: 默认浏览器 → Edge
                if ($rp -match 'UserChoice') {
                    $progId = (Get-ItemProperty -Path $rp -ErrorAction SilentlyContinue).ProgId
                    if ($progId -match 'Edge|MSEdge') {
                        $cmdKey = "HKCU:\Software\Classes\$progId\shell\open\command"
                        $cmd = (Get-ItemProperty -Path $cmdKey -ErrorAction SilentlyContinue).'(default)'
                        if ($cmd -match '"(.+?msedge\.exe)"') {
                            $edgePath = $matches[1]
                            break
                        }
                    }
                }
            } catch { }
        }
    }

    if ($null -eq $edgePath -or -not (Test-Path -LiteralPath $edgePath)) {
        Write-Log "无法定位 msedge.exe" "ERROR"
        return $false
    }

    Write-Log "Edge 路径: $edgePath"

    # 5.2 构建启动参数
    $args = @()
    if ($Incognito) {
        $args += "--inprivate"
        Write-Log "启用无痕模式 (InPrivate)"
    }
    $args += "`"$Url`""  # 引号包裹 URL 防止特殊字符截断

    $argString = $args -join ' '

    # 5.3 启动
    try {
        Start-Sleep -Milliseconds $DELAY_BEFORE_EDGE

        Write-Log "启动命令: `"$edgePath`" $argString"
        $process = Start-Process -FilePath $edgePath -ArgumentList $argString -PassThru

        if ($process) {
            Write-Log "Edge 已启动: PID=$($process.Id)"
            return $true
        }
        return $false
    } catch {
        Write-Log "Edge 启动异常: $_" "ERROR"

        # 终极回退: rundll32 (系统 URL 协议分发)
        try {
            Write-Log "终极回退: rundll32 url.dll..."
            Start-Process -FilePath "rundll32.exe" -ArgumentList "url.dll,FileProtocolHandler `"$Url`""
            return $true
        } catch {
            Write-Log "所有 Edge 启动方案均失败" "ERROR"
            return $false
        }
    }
}

# ============================================================================
# 步骤6：消息提示
# ============================================================================
function Show-Notification {
    param([string]$Message, [string]$Title = "微信→Edge", [bool]$IsError = $false)

    try {
        $icon = if ($IsError) {
            [System.Windows.Forms.MessageBoxIcon]::Error
        } else {
            [System.Windows.Forms.MessageBoxIcon]::Information
        }
        [System.Windows.Forms.MessageBox]::Show(
            $Message, $Title,
            [System.Windows.Forms.MessageBoxButtons]::OK,
            $icon,
            [System.Windows.Forms.MessageBoxDefaultButton]::Button1,
            [System.Windows.Forms.MessageBoxOptions]::DefaultDesktopOnly
        )
    } catch {
        Write-Log "弹窗失败: $_" "ERROR"
    }
}

# ============================================================================
# 主流程 (带重试机制)
# ============================================================================
function Main {
    Write-Log "══════════ 微信→Edge 增强版 启动 ══════════"
    Write-Log "模式: 无痕=$INCOGNITO_MODE 重试=$MAX_RETRIES 输入法保护=$ESCAPE_BEFORE_ACTION"
    Write-Log "延时: CtrlL前=${DELAY_BEFORE_CTRL_L}ms 按键间=${DELAY_BETWEEN_KEYS}ms 剪贴板=${DELAY_BEFORE_CLIP}ms"

    $finalUrl = $null
    $attemptUrl = $null

    for ($attempt = 1; $attempt -le $MAX_RETRIES; $attempt++) {
        Write-Log "──────── 第 $attempt / $MAX_RETRIES 次尝试 ────────"

        # 1. 查找窗口
        $window = Find-WeChatBrowserWindow
        if ($null -eq $window) {
            $msg = "未检测到微信浏览器窗口。`n`n请确认：`n• 微信内置浏览器页面已打开`n• 浏览器窗口可见`n• 页面已完成加载"
            Write-Log $msg "ERROR"
            if ($attempt -lt $MAX_RETRIES) {
                Write-Log "等待 ${RETRY_DELAY_MS}ms 后重试..."
                Start-Sleep -Milliseconds $RETRY_DELAY_MS
                continue
            }
            Show-Notification -Message $msg -IsError $true
            return "FAIL:NO_WINDOW"
        }

        # 2. 激活窗口
        $activated = Set-ActiveWindow -WindowInfo $window
        if (-not $activated -and $attempt -lt $MAX_RETRIES) {
            Start-Sleep -Milliseconds $RETRY_DELAY_MS
            continue
        }

        # 3. 模拟按键
        $keyOk = Invoke-UrlCopySequence -Attempt $attempt
        if (-not $keyOk) {
            Write-Log "按键模拟失败" "WARN"
            if ($attempt -lt $MAX_RETRIES) {
                Start-Sleep -Milliseconds $RETRY_DELAY_MS
                continue
            }
            Show-Notification -Message "快捷键模拟失败，请检查输入法状态。`n`n建议：关闭中文输入法后重试" -IsError $true
            return "FAIL:KEY_SEQUENCE"
        }

        # 4. 校验 URL
        $attemptUrl = Get-ValidatedUrl
        if ($null -ne $attemptUrl) {
            $finalUrl = $attemptUrl
            break  # 获取成功，跳出重试循环
        }

        Write-Log "URL 校验失败 (尝试 $attempt/$MAX_RETRIES)" "WARN"
        if ($attempt -lt $MAX_RETRIES) {
            Write-Log "准备重试... (等待 ${RETRY_DELAY_MS}ms)"
            Start-Sleep -Milliseconds $RETRY_DELAY_MS
        }
    }

    # 所有重试耗尽的最终检查
    if ($null -eq $finalUrl) {
        $msg = "重试 $MAX_RETRIES 次后仍未获取到有效链接。`n`n可能原因：`n• 页面地址栏未正确聚焦`n• 输入法干扰快捷键`n• 页面不支持 Ctrl+L`n`n建议：手动复制URL或关闭输入法后重试"
        Write-Log $msg "ERROR"
        Show-Notification -Message $msg -IsError $true
        return "FAIL:URL_NOT_FOUND"
    }

    # 5. 启动 Edge
    $edgeOk = Invoke-EdgeBrowser -Url $finalUrl -Incognito $INCOGNITO_MODE
    if (-not $edgeOk) {
        Show-Notification -Message "Edge 启动失败。`n`nURL 已保留在剪贴板：`n$finalUrl" -IsError $true
        return "FAIL:EDGE_LAUNCH"
    }

    # 成功
    $modeStr = if ($INCOGNITO_MODE) { "无痕模式" } else { "正常模式" }
    Write-Log "══════════ 成功 ($modeStr): $finalUrl ══════════"
    return "OK:$finalUrl"
}

# ============================================================================
# 入口
# ============================================================================
try {
    $result = Main
    Write-Output $result

    if ($ENABLE_DETAILED_LOG) {
        Write-Output "`r`n══════════ 详细日志 ══════════"
        $Global:LogMessages | ForEach-Object { Write-Output $_ }
    }
} catch {
    $errMsg = "致命异常: $_`r`n$($_.ScriptStackTrace)"
    Write-Output "FATAL:$errMsg"
    try {
        [System.Windows.Forms.MessageBox]::Show(
            "动作执行异常：`n$($_.Exception.Message)`n`n请检查 QuIcker 运行日志。",
            "微信→Edge 致命错误", "OK", "Error")
    } catch { }
}
