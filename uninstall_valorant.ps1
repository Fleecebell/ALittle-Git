# 卸载 无畏契约 (ACLOS) + ACE 反作弊
# 本脚本需要管理员权限运行
$ErrorActionPreference = 'Continue'
$log = 'D:\game\ALittle-Git\valorant_uninstall_log.txt'
function L($m) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    $line | Tee-Object -FilePath $log -Append | Out-Host
}
Set-Content -Path $log -Value "=== 卸载开始 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" -Encoding UTF8

L "当前用户: $([Security.Principal.WindowsIdentity]::GetCurrent().Name)"
$p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
L "已提权: $($p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"

# ---------- 0. 确认游戏/启动器未在运行 ----------
$running = Get-Process | Where-Object { $_.Name -match 'aclos|VALORANT|ACE-Service|ACE-Tray|SGuard' }
if ($running) {
    L "发现运行中的进程，先结束："
    foreach ($r in $running) {
        L "  $($r.Name) (PID $($r.Id))"
        Stop-Process -Id $r.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 3
}

# ---------- 1. 卸载 ACLOS 启动器 (NSIS, 静默) ----------
$aclosUn = 'C:\Program Files (x86)\ACLOS\uninst.exe'
if (Test-Path $aclosUn) {
    L "步骤1: 运行 ACLOS 卸载器 /S ..."
    try {
        Start-Process -FilePath $aclosUn -ArgumentList '/S' -Wait -ErrorAction Stop
        L "  ACLOS 卸载器已退出"
    } catch { L "  ❌ ACLOS 卸载器失败: $($_.Exception.Message)" }
    Start-Sleep -Seconds 5
    L "  ACLOS 目录仍存在: $(Test-Path 'C:\Program Files (x86)\ACLOS')"
} else {
    L "步骤1: 未找到 ACLOS 卸载器，跳过"
}

# ---------- 2. 卸载 ACE 反作弊 (GUI, 需人工确认) ----------
$aceUn = 'C:\Program Files\AntiCheatExpert\Uninstaller.exe'
if (Test-Path $aceUn) {
    L "步骤2: 启动 ACE 卸载器（会弹窗，请点确认）..."
    try {
        Start-Process -FilePath $aceUn -Wait -ErrorAction Stop
        L "  ACE 卸载器已退出"
    } catch { L "  ❌ ACE 卸载器失败: $($_.Exception.Message)" }
    Start-Sleep -Seconds 5
    L "  AntiCheatExpert 目录仍存在: $(Test-Path 'C:\Program Files\AntiCheatExpert')"
} else {
    L "步骤2: 未找到 ACE 卸载器，跳过"
}

# ---------- 3. 清理残留目录 ----------
function Remove-Dir($path, $desc) {
    if (Test-Path $path) {
        try {
            Remove-Item $path -Recurse -Force -ErrorAction Stop
            L "  已删除 $desc : $path"
        } catch {
            L "  ⚠ 无法完全删除 $desc : $($_.Exception.Message)"
        }
    } else {
        L "  $desc 已不存在: $path"
    }
}
L "步骤3: 清理残留"
Remove-Dir 'C:\Program Files (x86)\ACLOS' 'ACLOS 启动器目录'
Remove-Dir 'C:\Program Files\AntiCheatExpert' 'ACE 反作弊目录'
Remove-Dir 'C:\Users\14401\AppData\Roaming\ACLOS' 'ACLOS 用户数据'
Remove-Dir 'C:\Users\14401\AppData\Local\ACLOS' 'ACLOS 本地缓存'
Remove-Dir 'C:\ProgramData\ACLOS' 'ACLOS 程序数据'

# ---------- 4. 清理 ACE 残留服务与驱动 ----------
L "步骤4: 检查 ACE 残留服务/驱动"
foreach ($svc in @('AntiCheatExpert Protection', 'AntiCheatExpert Service')) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($s) {
        L "  停止并删除服务: $svc (当前 $($s.Status))"
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
        $out = & sc.exe delete "$svc" 2>&1
        L "    sc delete 结果: $out"
    } else {
        L "  服务已不存在: $svc"
    }
}
foreach ($drv in @('ACE-ADVT', 'ACE-BASE', 'ACE-BOOT', 'ace-game', 'ACE-SSC-DRV64')) {
    $d = Get-CimInstance Win32_SystemDriver -Filter "Name='$drv'" -ErrorAction SilentlyContinue
    if ($d) {
        $out = & sc.exe delete "$drv" 2>&1
        L "  删除驱动服务 $drv : $out"
    }
}
foreach ($f in @('ACE-BASE.sys','ACE-GAME.sys','ACE-ADVT.sys','ACE-CORE10405.sys','ACE-CORE20405.sys')) {
    $fp = "C:\Windows\System32\drivers\$f"
    if (Test-Path $fp) {
        try { Remove-Item $fp -Force -ErrorAction SilentlyContinue; L "  已删除驱动文件 $f" } catch { L "  ⚠ 驱动文件占用中 $f" }
    }
}

# ---------- 5. 清理注册表卸载项 ----------
L "步骤5: 清理注册表卸载项"
$keys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
foreach ($k in $keys) {
    Get-ItemProperty $k -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match '无畏契约|ACLOS|AntiCheatExpert' } |
        ForEach-Object {
            $path = $_.PSPath -replace '^Microsoft\.PowerShell\.Core\\Registry::', ''
            L "  删除卸载项: $($_.DisplayName) -> $path"
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        }
}
foreach ($k in @('HKLM:\SOFTWARE\WOW6432Node\ACLOS','HKLM:\SOFTWARE\ACLOS','HKCU:\SOFTWARE\ACLOS','HKLM:\SOFTWARE\WOW6432Node\AntiCheatExpert','HKLM:\SOFTWARE\AntiCheatExpert')) {
    if (Test-Path $k) {
        Remove-Item $k -Recurse -Force -ErrorAction SilentlyContinue
        L "  删除注册表键: $k"
    }
}

# ---------- 6. 汇总 ----------
L "=== 卸载完成，最终状态 ==="
L "  ACLOS 目录:          $(Test-Path 'C:\Program Files (x86)\ACLOS')"
L "  AntiCheatExpert 目录: $(Test-Path 'C:\Program Files\AntiCheatExpert')"
L "  ACLOS 用户数据:      $(Test-Path 'C:\Users\14401\AppData\Roaming\ACLOS')"
$svcLeft = Get-Service | Where-Object { $_.Name -match 'AntiCheatExpert' }
L "  残留 ACE 服务数: $(@($svcLeft).Count)"
$drvLeft = Get-CimInstance Win32_SystemDriver -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'ACE-' }
L "  残留 ACE 驱动数: $(@($drvLeft).Count)"
L "=== 日志结束 ==="

Write-Host ""
Write-Host "按任意键关闭本窗口..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
