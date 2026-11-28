$ErrorActionPreference = 'Continue'
$log = 'D:\game\ALittle-Git\move_apps_log.txt'
function L($m) {
    $line = "[{0}] {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}
Set-Content -Path $log -Value "=== move apps start ===" -Encoding UTF8

$items = @(
    @{ S = 'C:\Users\14401\AppData\Local\JianyingPro';                      T = 'E:\programs\JianyingPro';   N = 'JianyingPro-6.3' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\Quark';                   T = 'E:\programs\Quark';         N = 'Quark' },
    @{ S = 'C:\Users\14401\AppData\Local\SYSTEMAX Software Development';    T = 'E:\programs\PaintToolSAI';  N = 'PaintToolSAI' },
    @{ S = 'C:\Users\14401\AppData\Local\GitHubDesktop';                    T = 'E:\programs\GitHubDesktop'; N = 'GitHubDesktop' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\Tuanjie Cowork';          T = 'E:\programs\TuanjieCowork'; N = 'TuanjieCowork' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\cron-web';                T = 'E:\programs\cron-web';      N = 'cron-web' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\Kimi 智能助手';            T = 'E:\programs\Kimi';          N = 'Kimi' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\Notion';                  T = 'E:\programs\Notion';        N = 'Notion' },
    @{ S = 'C:\Users\14401\AppData\Local\Programs\Python';                  T = 'E:\programs\Python';        N = 'Python' }
)

foreach ($it in $items) {
    L ("---- " + $it.N + " ----")
    if (-not (Test-Path $it.S)) { L ("  SKIP: source missing " + $it.S); continue }
    if (Test-Path $it.T) { L ("  SKIP: target already exists " + $it.T); continue }

    $srcFiles = (Get-ChildItem $it.S -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum)
    L ("  source: " + $srcFiles.Count + " files, " + [math]::Round($srcFiles.Sum/1MB,0) + " MB")

    # 用 robocopy 跨盘复制并删除源
    $null = & robocopy $it.S $it.T /E /MOVE /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    $rc = $LASTEXITCODE
    L ("  robocopy exit code = " + $rc)

    $dstOk = Test-Path $it.T
    $srcLeft = Test-Path $it.S
    L ("  after copy: target=" + $dstOk + " sourceStillThere=" + $srcLeft)

    if ($dstOk) {
        if ($srcLeft) {
            # 清理残留空壳
            try {
                Remove-Item $it.S -Recurse -Force -ErrorAction Stop
                L "  leftover source removed"
            } catch {
                L ("  WARN cannot remove leftover source: " + $_.Exception.Message)
            }
        }
        if (-not (Test-Path $it.S)) {
            try {
                $j = New-Item -ItemType Junction -Path $it.S -Target $it.T -ErrorAction Stop
                L ("  JUNCTION OK: " + $it.S + " -> " + $it.T)
            } catch {
                L ("  JUNCTION FAILED: " + $_.Exception.Message)
            }
        } else {
            L "  SKIP junction (source path still occupied)"
        }
    } else {
        L "  ERROR: target not created"
    }
}

L "=== verify ==="
foreach ($it in $items) {
    $ok = Test-Path $it.S
    $linkType = $null
    $target = $null
    if ($ok) {
        $i = Get-Item $it.S -Force -ErrorAction SilentlyContinue
        $linkType = $i.LinkType
        $target = $i.Target
    }
    L ("  " + $it.N.PadRight(16) + " srcExists=" + $ok + " linkType=" + $linkType)
}
L "=== move apps end ==="
