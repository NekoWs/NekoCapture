# 原生常驻与按需界面的真实进程检查，使用隔离配置和应用内输入。
param(
    [string]$ResidentExe,
    [string]$UiExe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $ResidentExe) { $ResidentExe = Join-Path $root 'test-out/resident-native/NekoCapture.Resident.exe' }
if (-not $UiExe) { $UiExe = Join-Path $root 'src/NekoCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/NekoCapture.exe' }
$selfTest = Join-Path $root 'tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe'
foreach ($file in @($ResidentExe, $UiExe, $selfTest)) { if (-not (Test-Path -LiteralPath $file)) { throw "缺少产物: $file" } }
$fixture = Join-Path $root ('test-out/resident-e2e/' + [Guid]::NewGuid().ToString('N'))
$runtime = Join-Path $fixture 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$saved = @{}
foreach ($name in @('TEMP', 'TMP', 'NC_SETTINGS_DIR', 'NC_TEST_NO_FG', 'NC_TEST_NO_WINDOWS', 'NC_NO_ELEVATE',
    'NC_RESIDENT_ENABLED', 'NC_RESIDENT_DISABLED', 'NC_RESIDENT_INSTANCE', 'NC_RESIDENT_PATH', 'NC_RESIDENT_UI_PATH', 'NC_TRACE_FILE')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$native = $null
$owned = [Collections.Generic.HashSet[int]]::new()
$checks = 0
function Check([string]$name, [bool]$value) {
    if (-not $value) { throw "FAIL $name" }
    $script:checks++; "PASS $name"
}
function Wait-For([scriptblock]$condition, [int]$timeout = 10000) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if (& $condition) { return $true }
        Start-Sleep -Milliseconds 50
    } while ($watch.ElapsedMilliseconds -lt $timeout)
    return $false
}
function Workers {
    if (-not $script:native) { return @() }
    $ids = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:native.Id)" | Where-Object Name -eq 'NekoCapture.exe' | ForEach-Object ProcessId)
    foreach ($workerId in $ids) { [void]$script:owned.Add([int]$workerId) }
    return @($ids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
}
function Invoke-Ui([string[]]$commands) {
    $start = [Diagnostics.ProcessStartInfo]::new($UiExe)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    foreach ($part in $commands) { $start.ArgumentList.Add($part) }
    $process = [Diagnostics.Process]::Start($start)
    [void]$script:owned.Add($process.Id)
    try {
        if (-not $process.WaitForExit(8000) -or $process.ExitCode -ne 0) { throw "启动转发失败: $commands" }
    } finally { $process.Dispose() }
}
function Key([int]$key, [string]$direction) { Invoke-Ui @('--resident-test-key', "$key", '7', $direction) }
$lastIdle = 0
function Idle {
    $ready = Wait-For { (Trace-Count 'resident idle-ready') -gt $script:lastIdle -and @(Workers).Count -eq 1 } 15000
    if ($ready) { $script:lastIdle = Trace-Count 'resident idle-ready' }
    return $ready
}
function Products { @(Get-ChildItem -LiteralPath (Join-Path $fixture 'products') -Filter '*.png' -ErrorAction SilentlyContinue).Count }
function Trace-Count([string]$text) {
    if (-not (Test-Path -LiteralPath $env:NC_TRACE_FILE)) { return 0 }
    return @(Select-String -LiteralPath $env:NC_TRACE_FILE -Pattern $text -SimpleMatch).Count
}
function Session {
    $file = Join-Path $fixture 'resident-session.json'
    if (Test-Path -LiteralPath $file) { return Read-Json $file }
    return $null
}
function Read-Json([string]$path) {
    $file = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]7)
    $reader = [IO.StreamReader]::new($file)
    try { return $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
}
function Find-Resident {
    Get-Process NekoCapture.Resident -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -eq $env:NC_RESIDENT_PATH -and $_.StartTime -gt (Get-Item -LiteralPath $fixture).CreationTime
    } | Sort-Object StartTime -Descending | Select-Object -First 1
}
try {
    $env:TEMP = $runtime; $env:TMP = $runtime; $env:NC_SETTINGS_DIR = $fixture
    $env:NC_TEST_NO_FG = '1'; $env:NC_TEST_NO_WINDOWS = '1'; $env:NC_NO_ELEVATE = '1'
    $env:NC_RESIDENT_ENABLED = '1'; $env:NC_RESIDENT_DISABLED = $null
    $env:NC_RESIDENT_INSTANCE = [Guid]::NewGuid().ToString('N')
    $env:NC_RESIDENT_PATH = [IO.Path]::GetFullPath($ResidentExe)
    $env:NC_RESIDENT_UI_PATH = [IO.Path]::GetFullPath($UiExe)
    $env:NC_TRACE_FILE = Join-Path $fixture 'trace.log'
    & $selfTest resident-fixture $fixture
    if ($LASTEXITCODE -ne 0) { throw '隔离配置生成失败' }
    Invoke-Ui @()
    $found = Wait-For {
        $script:native = Find-Resident
        $null -ne $script:native
    }
    Check 'native-startup' $found
    Check 'idle-with-prewarmed-ui' (Idle)
    Check 'combined-idle-commit-under-100mb' (($native.PrivateMemorySize64 + (@(Workers) | Measure-Object PrivateMemorySize64 -Sum).Sum) -lt 100MB)
    $native.Refresh(); $cpu = $native.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 2
    $native.Refresh(); $cpuDelta = $native.TotalProcessorTime.TotalMilliseconds - $cpu
    $working = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$($native.Id)"
    Check 'idle-private-commit-under-10mb' ($native.PrivateMemorySize64 -lt 10MB)
    Check 'idle-private-working-set-under-10mb' ($working.WorkingSetPrivate -lt 10MB)
    Check 'idle-no-timer-spin' ($cpuDelta -lt 150)
    "memory: private_commit=$($native.PrivateMemorySize64) private_working_set=$($working.WorkingSetPrivate) total_working_set=$($native.WorkingSet64) cpu_2s_ms=$cpuDelta"

    Invoke-Ui @('--run-binding', 'still')
    Check 'warm-capture-saves-product' (Wait-For { (Products) -eq 1 })
    Check 'capture-releases-idle-cache' (Idle)
    $session = Session
    Check 'still-retained-on-disk' ($session.Still -and (Test-Path -LiteralPath $session.Still.Path))
    $before = Trace-Count 'open editor frames=1'
    Invoke-Ui @('--edit-last')
    Check 'reopen-last-still-from-idle-cache' (Wait-For { (Trace-Count 'open editor frames=1') -gt $before })
    Invoke-Ui @('--editorkey', '27')
    Check 'editor-close-restores-idle' (Idle)

    $loops = Trace-Count 'loop start region='
    Key 135 'down'
    Check 'hold-starts-warm-recording' (Wait-For { (Trace-Count 'loop start region=') -gt $loops })
    Check 'hold-keeps-ui-alive' (@(Workers).Count -eq 1)
    Invoke-Ui @('--resident-test-stall', '1500')
    Start-Sleep -Milliseconds 100
    Key 135 'up'
    Check 'release-through-busy-ui-reaches-stop' (Idle)
    $session = Session
    Check 'recording-spool-retained-in-idle-cache' ($session.Video -and $session.Video.Index.Count -gt 1 -and (Test-Path -LiteralPath $session.Video.Path))
    $previousWorker = @(Workers)[0]
    $previousWorkerId = $previousWorker.Id
    $previousWorker.Kill(); [void]$previousWorker.WaitForExit(3000); $previousWorker.Dispose()
    Check 'worker-prewarms-after-recycle' ((Idle) -and @(Workers)[0].Id -ne $previousWorkerId)
    $before = Trace-Count 'open editor frames='
    Invoke-Ui @('--run-binding', 'edit-video')
    Check 'last-video-reopens-after-recycle' (Wait-For { (Trace-Count 'open editor frames=') -gt $before })
    Invoke-Ui @('--editorkey', '27')
    Check 'video-editor-restores-warm-idle' (Idle)

    Invoke-Ui @('--pause-toggle')
    Check 'pause-restores-warm-idle' (Idle)
    $before = Products
    Key 134 'down'; Key 134 'up'
    Start-Sleep -Milliseconds 800
    Check 'paused-native-ignores-gesture' ((Products) -eq $before -and @(Workers).Count -eq 1)
    Invoke-Ui @('--pause-toggle')
    Check 'resume-restores-warm-idle' (Idle)
    Key 134 'down'; Key 134 'up'
    Check 'resume-native-routes-gesture' (Wait-For { (Products) -eq $before + 1 })
    Check 'resumed-capture-restores-warm-idle' (Idle)

    $settingsFile = Join-Path $fixture 'settings.json'
    $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
    $settings.Gestures.Enabled = $false
    $settings | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $settingsFile -Encoding utf8
    Check 'external-settings-disable-reaches-native' (Wait-For {
        -not (Read-Json (Join-Path $fixture 'resident-inputs.json')).Enabled
    })
    Check 'settings-sync-restores-warm-idle' (Idle)
    $before = Products
    Key 134 'down'; Key 134 'up'
    Check 'disabled-native-ignores-gesture' ((Products) -eq $before -and @(Workers).Count -eq 1)
    $settings.Gestures.Enabled = $true
    $settings | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $settingsFile -Encoding utf8
    Check 'external-settings-enable-reaches-native' (Wait-For {
        (Read-Json (Join-Path $fixture 'resident-inputs.json')).Enabled
    })
    Check 'enabled-settings-sync-restores-warm-idle' (Idle)

    foreach ($cycle in 1..2) {
        $before = Products
        Invoke-Ui @('--run-binding', 'save')
        Check "wake-cycle-$cycle-product" (Wait-For { (Products) -eq $before + 1 })
        Check "wake-cycle-$cycle-idle" (Idle)
    }
    $native.Refresh()
    $working = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$($native.Id)"
    "memory_after_use: private_commit=$($native.PrivateMemorySize64) private_working_set=$($working.WorkingSetPrivate) total_working_set=$($native.WorkingSet64)"
    Check 'used-resident-private-commit-under-10mb' ($native.PrivateMemorySize64 -lt 10MB)
    Check 'input-snapshots-publish-without-contention' ((Trace-Count 'resident inputs:') -eq 0)
    $settings = Read-Json $settingsFile
    $settings.StartupToast = $true; $settings.FirstRunHintShown = $false
    $settings | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $settingsFile -Encoding utf8
    Check 'first-startup-settings-sync' ((Wait-For {
        (Read-Json (Join-Path $fixture 'resident-inputs.json')).SourceTicks -eq (Get-Item -LiteralPath $settingsFile).LastWriteTimeUtc.Ticks
    }) -and (Idle))
    Stop-Process -Id $native.Id -Force; [void]$native.WaitForExit(3000); $native.Dispose(); $native = $null
    Invoke-Ui @()
    Check 'first-startup-records-welcome-shown' (Wait-For {
        $script:native = Find-Resident
        $script:native -and (Read-Json $settingsFile).FirstRunHintShown -and @(Workers).Count -eq 1
    })
    Start-Sleep -Seconds 4
    Check 'first-welcome-keeps-ui-alive' (@(Workers).Count -eq 1)
    foreach ($log in @('error.log', 'resident.log')) {
        $file = Join-Path $fixture $log
        Check "empty-$log" (-not (Test-Path -LiteralPath $file) -or (Get-Item -LiteralPath $file).Length -eq 0)
    }
    "RESIDENT PASS checks=$checks fixture=$fixture"
} catch {
    "RESIDENT FAIL checks=$checks fixture=$fixture $($_.Exception.Message)"
    throw
} finally {
    if ($native) {
        [void](Workers)
        Stop-Process -Id $native.Id -Force -ErrorAction SilentlyContinue
        $native.Dispose()
    }
    foreach ($workerId in $owned) { Stop-Process -Id $workerId -Force -ErrorAction SilentlyContinue }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
