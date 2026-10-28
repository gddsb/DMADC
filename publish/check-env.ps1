# DMADC 运行环境检测（PowerShell 5.1+ 即可，Windows 10/11 自带）
# 被 check-env.bat 双击触发

$ErrorActionPreference = 'Continue'

# --- 颜色 ---
$R = "`e[0m"; $G = "`e[92m"; $Y = "`e[93m"; $RD = "`e[91m"; $C = "`e[96m"
function OK($msg)  { Write-Host "  $G✅$R $msg" }
function BAD($msg) { Write-Host "  $RD❌$R $msg" }
function INFO($msg){ Write-Host "  $C→$R $msg" }

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "       DMADC 运行环境检测工具" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$allOk = $true

# ───────────────────── 1. OS ─────────────────────
Write-Host "$C[1/3]$R 操作系统"
$os = [Environment]::OSVersion
$osName = (Get-CimInstance Win32_OperatingSystem).Caption
$major = $os.Version.Major
$build = $os.Version.Build

$supported = $false
if ($major -eq 11) { $supported = $true }
if ($major -eq 10 -and $build -ge 18362) { $supported = $true }  # 1903+
if ($supported) {
    OK "$osName (Build $build) — 满足"
} else {
    BAD "$osName (Build $build) — 不支持"
    INFO "需要 Windows 10 1903+ 或 Windows 11"
    $allOk = $false
}
Write-Host ""

# ───────────────────── 2. 架构 ─────────────────────
Write-Host "$C[2/3]$R 系统架构"
$arch = [Environment]::Is64BitOperatingSystem
if ($arch) {
    OK "x64 — 满足"
} else {
    BAD "32 位/ARM — 不支持（本程序只有 x64 版本）"
    $allOk = $false
}
Write-Host ""

# ───────────────────── 3. .NET 8 Desktop Runtime ─────────────────────
Write-Host "$C[3/3]$R .NET 8 Desktop Runtime"
$desktopOk = $false
$runtimeList = @()

try {
    $runtimeList = & dotnet --list-runtimes 2>$null
} catch {}

foreach ($line in $runtimeList) {
    if ($line -match 'Microsoft\.WindowsDesktop\.App 8\.\d+\.\d+') {
        $desktopOk = $true
        OK ($Matches[0] + " — 满足")
        break
    }
}

if (-not $desktopOk) {
    BAD "未检测到 .NET 8 Desktop Runtime"
    Write-Host ""
    Write-Host "  $Y════════════════════════════════════════════════════════$R"
    Write-Host "  $Y  安装指引（任选其一）$R"
    Write-Host "  $Y════════════════════════════════════════════════════════$R"
    Write-Host ""
    INFO "方式 ① 浏览器下载（推荐）"
    Write-Host "      $C https://dotnet.microsoft.com/download/dotnet/8.0$R"
    Write-Host "      页面往下翻 → 找到 Runtime 板块"
    Write-Host "      → Desktop runtime → Download x64"
    Write-Host "      安装包约 50MB，安装后重启 cmd 再双击 DMADC.exe"
    Write-Host ""
    INFO "方式 ② winget 一键（如果你有 winget）"
    Write-Host "      $C winget install Microsoft.DotNet.DesktopRuntime.8$R"
    Write-Host ""
    INFO "方式 ③ 如果你拿到的是 Self-Contained 单文件 exe"
    Write-Host "      不需要安装任何运行时，直接双击 exe 即可"
    Write-Host ""
    $allOk = $false
}
Write-Host ""

# ───────────────────── 总结 ─────────────────────
Write-Host "============================================"
if ($allOk) {
    Write-Host "  $G🎉 全部通过！可以直接运行 DMADC.exe$R"
} else {
    Write-Host "  $RD⚠️  有未通过项 —— 请按上方指引安装后重试$R"
}
Write-Host "============================================"
Write-Host ""
