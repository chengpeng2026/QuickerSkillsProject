<#
.SYNOPSIS
  一次性将嘀嗒 V2 的 t cookie 写入 User 级环境变量 DIDA_T_COOKIE。

.DESCRIPTION
  从浏览器 www.dida365.com 登录后复制 t cookie 值，运行本脚本写入环境变量。
  Cookie 约 30 天过期，过期后需重新执行本脚本。

.PARAMETER Token
  t cookie 值。不传则提示输入（不回显）。

.EXAMPLE
  .\Set-DidaToken.ps1 -Token "abcd1234..."
.EXAMPLE
  .\Set-DidaToken.ps1   # 交互式输入，不回显
#>
param(
    [string]$Token
)

# 统一控制台编码为 UTF-8，避免中文输出乱码
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $Utf8NoBom
[Console]::OutputEncoding = $Utf8NoBom
$OutputEncoding = $Utf8NoBom

if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "请输入 t cookie 值（输入时不回显，粘贴后回车）：" -ForegroundColor Cyan -NoNewline
    $Token = Read-Host -AsSecureString
    $Bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Token)
    try {
        $Token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($Bstr)
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($Bstr)
    }
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "错误：未提供 t cookie 值。" -ForegroundColor Red
    exit 1
}

# 写入 User 级环境变量（持久化，不打印值本身）
[Environment]::SetEnvironmentVariable('DIDA_T_COOKIE', $Token, 'User')

Write-Host "✅ 已写入 User 级环境变量 DIDA_T_COOKIE（值已隐藏）。" -ForegroundColor Green
Write-Host "注意：当前已打开的终端/Claude 会话读不到新值，请新开会话后再用。" -ForegroundColor Yellow
