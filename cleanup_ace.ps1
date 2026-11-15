$ErrorActionPreference = 'Continue'
$log = 'D:\game\ALittle-Git\valorant_cleanup_log.txt'
function L($m) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}
Set-Content -Path $log -Value "=== cleanup start ===" -Encoding UTF8

$p = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
L ("Elevated: " + $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))

$targets = @(
    'HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ACLOS',
    'HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\ACLOS',
    'HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ACLOS',
    'HKLM\SOFTWARE\ACLOS',
    'HKLM\SOFTWARE\WOW6432Node\ACLOS',
    'HKLM\SOFTWARE\AntiCheatExpert',
    'HKLM\SOFTWARE\WOW6432Node\AntiCheatExpert'
)

foreach ($t in $targets) {
    $psPath = 'Registry::' + $t.Replace('HKLM\', 'HKEY_LOCAL_MACHINE\').Replace('HKCU\', 'HKEY_CURRENT_USER\')
    if (Test-Path $psPath) {
        L ("FOUND: " + $t)
        $out = & reg.exe delete $t /f 2>&1
        L ("  reg delete output: " + ($out -join ' '))
        Start-Sleep -Milliseconds 300
        if (Test-Path $psPath) {
            L ("  STILL EXISTS, trying PowerShell Remove-Item ...")
            try {
                Remove-Item -LiteralPath $psPath -Recurse -Force -ErrorAction Stop
                L "  Remove-Item OK"
            } catch {
                L ("  Remove-Item failed: " + $_.Exception.Message)
            }
        } else {
            L "  DELETED (reg.exe)"
        }
    } else {
        L ("not present: " + $t)
    }
}

L "=== verify ==="
foreach ($t in $targets) {
    $psPath = 'Registry::' + $t.Replace('HKLM\', 'HKEY_LOCAL_MACHINE\').Replace('HKCU\', 'HKEY_CURRENT_USER\')
    L ("  " + $t + " -> " + (Test-Path $psPath))
}

# scan for any remaining display names
L "=== scan uninstall entries ==="
$keys = @(
    'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$hits = Get-ItemProperty $keys -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'VALORANT|AntiCheatExpert|ACLOS|Riot' -or $_.PSChildName -match 'ACLOS|AntiCheat' }
if ($hits) {
    foreach ($h in $hits) { L ("  LEFT: " + $h.DisplayName + " @ " + $h.PSChildName) }
} else {
    L "  none left"
}
L "=== cleanup end ==="
