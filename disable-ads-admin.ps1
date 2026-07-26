# Windows 10 彻底关闭弹窗广告 - 管理员权限脚本
# 请右键 → 以管理员身份运行 PowerShell，然后执行此脚本

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Windows 10 全面关闭广告/推广脚本"
Write-Host "========================================" -ForegroundColor Cyan

$ErrorActionPreference = "Continue"

# ===================== 第一部分：HKLM 注册表 =====================
Write-Host "`n[1/5] 设置机器级别策略 (HKLM)..." -ForegroundColor Yellow

# 1.1 禁用消费者功能（最重要的广告总开关）
$cloudContent = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
if (-not (Test-Path $cloudContent)) { New-Item -Path $cloudContent -Force | Out-Null }
Set-ItemProperty -Path $cloudContent -Name "DisableWindowsConsumerFeatures" -Value 1 -Type DWord

# 1.2 禁用锁屏广告策略
Set-ItemProperty -Path $cloudContent -Name "DisableSoftLanding" -Value 1 -Type DWord
Set-ItemProperty -Path $cloudContent -Name "DisableWindowsSpotlightFeatures" -Value 1 -Type DWord

# 1.3 禁用 OneDrive 广告
$oneDrive = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive"
if (-not (Test-Path $oneDrive)) { New-Item -Path $oneDrive -Force | Out-Null }
Set-ItemProperty -Path $oneDrive -Name "DisableFileSyncNGSC" -Value 1 -Type DWord

# 1.4 禁用 Explorer 广告
$explorer = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer"
if (-not (Test-Path $explorer)) { New-Item -Path $explorer -Force | Out-Null }
Set-ItemProperty -Path $explorer -Name "NoUseStoreOpenWith" -Value 1 -Type DWord

# 1.5 遥测设为最低
$dataCollection = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection"
if (-not (Test-Path $dataCollection)) { New-Item -Path $dataCollection -Force | Out-Null }
Set-ItemProperty -Path $dataCollection -Name "AllowTelemetry" -Value 0 -Type DWord

Write-Host "  完成: HKLM 策略已设置" -ForegroundColor Green

# ===================== 第二部分：禁用计划任务 =====================
Write-Host "`n[2/5] 禁用广告相关的计划任务..." -ForegroundColor Yellow

$tasksToDisable = @(
    "\Microsoft\Windows\WindowsUpdate\Scheduled Start",
    "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
    "\Microsoft\Windows\Application Experience\ProgramDataUpdater",
    "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
    "\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
    "\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
    "\Microsoft\Windows\Feedback\Siuf\DmClient",
    "\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
)

foreach ($taskPath in $tasksToDisable) {
    try {
        Disable-ScheduledTask -TaskPath (Split-Path $taskPath -Parent) -TaskName (Split-Path $taskPath -Leaf) -ErrorAction Stop
        Write-Host "  OK: $taskPath" -ForegroundColor Green
    } catch {
        Write-Host "  SKIP: $taskPath ($_)" -ForegroundColor DarkGray
    }
}

# 搜索并禁用 360 相关任务
$allTasks = Get-ScheduledTask -ErrorAction SilentlyContinue
$adwarePatterns = @("360", "baidu", "sougou", "sogou", "tencent", "qq", "kugou", "kankan", "xunlei", "kingsoft", "wpsupdate")
foreach ($task in $allTasks) {
    foreach ($pattern in $adwarePatterns) {
        if ($task.TaskName -match $pattern) {
            try {
                Disable-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -ErrorAction Stop
                Write-Host "  OK: $($task.TaskPath)$($task.TaskName)" -ForegroundColor Green
            } catch {
                Write-Host "  FAIL: $($task.TaskPath)$($task.TaskName) ($_)" -ForegroundColor Red
            }
            break
        }
    }
}

# ===================== 第三部分：停止并禁用 360 服务 =====================
Write-Host "`n[3/5] 停止并禁用 360 相关服务..." -ForegroundColor Yellow

$servicesToKill = @("ZhuDongFangYu", "Q360AMPPL", "360bpsvc", "360NetHelperSvc")
foreach ($svcName in $servicesToKill) {
    try {
        $svc = Get-Service -Name $svcName -ErrorAction Stop
        if ($svc.Status -eq "Running") {
            Stop-Service -Name $svcName -Force -ErrorAction Stop
            Write-Host "  OK: $svcName → 已停止" -ForegroundColor Green
        }
        Set-Service -Name $svcName -StartupType Disabled -ErrorAction Stop
        Write-Host "  OK: $svcName → 已禁用" -ForegroundColor Green
    } catch {
        Write-Host "  SKIP: $svcName (未找到或无法操作)" -ForegroundColor DarkGray
    }
}

# ===================== 第四部分：完成后台进程清理 =====================
Write-Host "`n[4/5] 终止残留广告进程..." -ForegroundColor Yellow

$adProcesses = @(
    "360huabao", "360Tray", "360UDiskPro", "360rp", "360sd", "360safe",
    "sogoucloud", "sogouexplorer", "sogoupinyin",
    "baidunetdisk", "baidusd",
    "kxescore", "kxe"
)

foreach ($procName in $adProcesses) {
    try {
        $proc = Get-Process -Name $procName -ErrorAction Stop
        $proc | Stop-Process -Force
        Write-Host "  OK: $procName → 已终止" -ForegroundColor Green
    } catch {
        # 进程未运行，正常
    }
}

# ===================== 第五部分：防止 360画报 复活 =====================
Write-Host "`n[5/5] 阻止 360画报 重新安装..." -ForegroundColor Yellow

$blockDirs = @(
    "$env:LOCALAPPDATA\360huabao",
    "$env:APPDATA\360huabao",
    "$env:LOCALAPPDATA\360Safe",
    "$env:APPDATA\360Safe"
)

foreach ($dir in $blockDirs) {
    if (Test-Path $dir) {
        try {
            # 先删除目录内容
            Get-ChildItem -Path $dir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
            # 设置只读+拒绝写入权限，防止重新创建
            $acl = Get-Acl $dir
            $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                "Everyone", "Write", "Deny"
            )
            $acl.SetAccessRule($rule)
            Set-Acl -Path $dir -AclObject $acl
            Write-Host "  OK: $dir → 已加锁" -ForegroundColor Green
        } catch {
            Write-Host "  PARTIAL: $dir ($_)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  SKIP: $dir (已不存在)" -ForegroundColor DarkGray
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  完成！建议重启电脑使所有设置生效。" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
