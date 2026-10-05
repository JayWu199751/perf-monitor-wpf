<#
.SYNOPSIS
    整进程树 Private Bytes 测量探针（工票 15 / 验收 A43-A45）。

.DESCRIPTION
    按固定间隔对目标进程的整棵进程树（经 Win32_Process.ParentProcessId 递归，
    含 GPU 采样子进程等短命子进程）逐进程读取 PrivateMemorySize64（Private Bytes）
    求和，MiB = 字节 / 1024^2。每轮重建进程树，进程退出/新建即时反映。
    输出 CSV（时间戳、树内 PID、总 Private Bytes MiB）与结束摘要
    （样本数、稳定态均值、P95（最近邻秩法）、峰值、最小值）。

    口径依据 docs/memory-benchmark.md 与 spec.md F13：整棵 app 进程树的
    Private Bytes，不用工作集/单一进程名筛选/仅托管堆替代。

.NOTES
    PowerShell 5.1 兼容（不使用 PS7 语法）。需 Windows PowerShell 或已安装
    CIM cmdlet 的环境。运行示例：

        powershell -ExecutionPolicy Bypass -File tools\memory-probe.ps1 ^
            -ProcessId 12345 -IntervalMs 1000 -DurationSeconds 300 ^
            -OutputCsv out\wpf-round1.csv

.EXAMPLE
    对自身 powershell 进程树采样 10 秒做冒烟：
    powershell -ExecutionPolicy Bypass -File tools\memory-probe.ps1 -ProcessId $PID -IntervalMs 500 -DurationSeconds 10 -OutputCsv .\smoke.csv
#>
[CmdletBinding(DefaultParameterSetName = 'ById')]
param(
    # 目标进程 ID（与 ProcessName 二选一，ById 组优先）
    [Parameter(ParameterSetName = 'ById', Mandatory = $true)]
    [int]$ProcessId,

    # 目标进程名（不带 .exe；命中多个进程时报错，要求唯一）
    [Parameter(ParameterSetName = 'ByName', Mandatory = $true)]
    [string]$ProcessName,

    # 采样间隔（毫秒），最小 200
    [ValidateRange(200, [int]::MaxValue)]
    [int]$IntervalMs = 1000,

    # 持续时长（秒）；与 Rounds 二选一，都提供时以先到者为准
    [ValidateRange(1, [int]::MaxValue)]
    [int]$DurationSeconds = 0,

    # 采样轮数（与 DurationSeconds 二选一）
    [ValidateRange(1, [int]::MaxValue)]
    [int]$Rounds = 0,

    # 预热轮数：这些轮仍写入 CSV，但不计入稳定态统计（如启动抖动期）
    [ValidateRange(0, [int]::MaxValue)]
    [int]$WarmupRounds = 0,

    # CSV 输出路径（目录不存在会创建）
    [Parameter(Mandatory = $true)]
    [string]$OutputCsv
)

$ErrorActionPreference = 'Stop'

if ($DurationSeconds -le 0 -and $Rounds -le 0) {
    throw '必须提供 -DurationSeconds 或 -Rounds 之一。'
}

# ---- 解析目标根进程 -------------------------------------------------------
function Resolve-RootProcess {
    if ($PSCmdlet.ParameterSetName -eq 'ById') {
        $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $proc) { throw "未找到进程 ID = $ProcessId。" }
        return $proc
    }
    $procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($procs.Count -eq 0) { throw "未找到进程名 = $ProcessName。" }
    if ($procs.Count -gt 1) {
        $ids = ($procs | ForEach-Object { $_.Id }) -join ', '
        throw "进程名 $ProcessName 命中多个 PID（$ids），请改用 -ProcessId 指定唯一目标。"
    }
    return $procs[0]
}

$root = Resolve-RootProcess
$rootId = $root.Id

# ---- 进程树收集：每轮重建 -------------------------------------------------
function Get-TreeProcessIds {
    param([int]$RootPid)

    # 一次性快照全部进程父子关系，再从根 BFS（树内进程中途退出不影响其余成员统计）
    $all = Get-CimInstance -ClassName Win32_Process -Property ProcessId, ParentProcessId -ErrorAction Stop
    $childrenOf = @{}
    foreach ($p in $all) {
        $pidVal = [int]$p.ProcessId
        $parentId = [int]$p.ParentProcessId
        if (-not $childrenOf.ContainsKey($parentId)) {
            $childrenOf[$parentId] = New-Object System.Collections.Generic.List[int]
        }
        $childrenOf[$parentId].Add($pidVal)
    }

    # 防环：已访问集合
    $visited = New-Object System.Collections.Generic.HashSet[int]
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue($RootPid)
    [void]$visited.Add($RootPid)

    while ($queue.Count -gt 0) {
        $current = [int]$queue.Dequeue()
        if ($childrenOf.ContainsKey($current)) {
            foreach ($child in $childrenOf[$current]) {
                if ($visited.Add($child)) { $queue.Enqueue($child) }
            }
        }
    }
    return @($visited | Sort-Object)
}

function Get-TreePrivateBytesMib {
    param([int[]]$TreePids)

    $totalBytes = [double]0
    $found = 0
    foreach ($pidValue in $TreePids) {
        # 进程可能在枚举后退出：静默跳过，每轮重建树会即时反映
        $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($null -ne $proc) {
            $totalBytes += $proc.PrivateMemorySize64
            $found++
        }
    }
    return @{ TotalBytes = $totalBytes; Found = $found; Mib = ($totalBytes / 1048576.0) }
}

# ---- 计划轮数 -------------------------------------------------------------
$totalRounds = $Rounds
if ($totalRounds -le 0) {
    $totalRounds = [int][math]::Ceiling(($DurationSeconds * 1000.0) / $IntervalMs)
}

# ---- 输出准备 -------------------------------------------------------------
$csvPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputCsv)
$csvDir = Split-Path -Parent $csvPath
if (-not (Test-Path -LiteralPath $csvDir)) {
    New-Item -ItemType Directory -Path $csvDir -Force | Out-Null
}
$summaryPath = [IO.Path]::ChangeExtension($csvPath, '.summary.txt')

if (Test-Path -LiteralPath $csvPath) { Remove-Item -LiteralPath $csvPath -Force }
$sw = New-Object System.Diagnostics.Stopwatch
$sw.Start()

Write-Host ("目标根进程: PID {0} ({1})" -f $rootId, $root.ProcessName)
Write-Host ("采样: 间隔 {0} ms x {1} 轮（预热 {2} 轮）" -f $IntervalMs, $totalRounds, $WarmupRounds)
Write-Host ("CSV: {0}" -f $csvPath)

$round = 0
while ($round -lt $totalRounds) {
    $round++
    $timestamp = (Get-Date).ToString('o')
    $treePids = Get-TreeProcessIds -RootPid $rootId
    $sample = Get-TreePrivateBytesMib -TreePids $treePids

    $row = [pscustomobject]@{
        TimestampUtc      = $timestamp
        Round             = $round
        RootPid           = $rootId
        TreePids          = ($treePids -join ';')
        ProcessCount      = $sample.Found
        TotalPrivateBytes = [int64]$sample.TotalBytes
        TotalPrivateMiB   = [math]::Round($sample.Mib, 3)
    }
    $row | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8 -Append

    Write-Host ("轮 {0}/{1}: 树内 {2} 进程, Private Bytes {3} MiB" -f $round, $totalRounds, $sample.Found, $row.TotalPrivateMiB)

    if ($round -lt $totalRounds) {
        $elapsedMs = [int]$sw.ElapsedMilliseconds
        $wait = $IntervalMs - ($elapsedMs % $IntervalMs)
        if ($wait -gt 0) { Start-Sleep -Milliseconds $wait }
    }
}
$sw.Stop()

# ---- 结束摘要（最近邻秩法 P95）--------------------------------------------
$samples = @(Import-Csv -Path $csvPath | ForEach-Object { [double]$_.TotalPrivateMiB })
$steady = @()
if ($samples.Count -gt $WarmupRounds) {
    $steady = @($samples | Select-Object -Skip $WarmupRounds)
}

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("memory-probe 摘要 $(Get-Date -Format o)")
$summary.Add("目标根进程: PID $rootId ($($root.ProcessName))")
$summary.Add("口径: 整进程树 Private Bytes 求和, MiB = bytes / 1024^2 (1048576)")
$summary.Add("采样: 间隔 ${IntervalMs} ms, 共 $($samples.Count) 轮 (预热 $WarmupRounds 轮)")
$summary.Add("CSV: $csvPath")

function Format-MibStats {
    param([double[]]$Values, [string]$Label)
    if ($Values.Count -eq 0) { return "$Label : 无样本" }
    $sorted = $Values | Sort-Object
    $mean = ($Values | Measure-Object -Average).Average
    # P95 最近邻秩法（nearest-rank）：rank = ceil(0.95 * N)，取升序第 rank 个
    $rank = [int][math]::Ceiling(0.95 * $sorted.Count)
    if ($rank -lt 1) { $rank = 1 }
    if ($rank -gt $sorted.Count) { $rank = $sorted.Count }
    $p95 = $sorted[$rank - 1]
    $peak = $sorted[$sorted.Count - 1]
    $min = $sorted[0]
    return ("{0}: 样本 {1}, 均值 {2:F3} MiB, P95 {3:F3} MiB (最近邻秩 rank={4}), 峰值 {5:F3} MiB, 最小 {6:F3} MiB" -f `
        $Label, $sorted.Count, $mean, $p95, $rank, $peak, $min)
}

$summary.Add((Format-MibStats -Values ([double[]]$samples) -Label '全部样本'))
if ($steady.Count -gt 0) {
    $summary.Add((Format-MibStats -Values ([double[]]$steady) -Label "稳定态(剔除前 $WarmupRounds 预热轮)"))
}
$summary.Add('P95 方法: 最近邻秩法 (nearest-rank, rank = ceil(0.95 * N))')

$summary | ForEach-Object { Write-Host $_ }
$summary | Set-Content -Path $summaryPath -Encoding UTF8
Write-Host ("摘要: {0}" -f $summaryPath)
