# 安装器路径护栏：整目录移动必须同卷，已知文件夹必须先判空
#
# 语义：
#   FAIL —— src/Setup 里出现没有同卷判断的 Directory.Move。
#           安装目录由用户挑，临时目录在系统盘。把 D:\NekoCapture 移到 C:\ 下的临时目录，
#           Directory.Move 抛「源路径和目标路径必须具有相同的根」，整次安装带着回滚一起失败，
#           而且这类失败只在「装到非系统盘」的那台机器上出现，本机测不出来。
#   FAIL —— Path.Combine 的第一段内联 Environment.GetFolderPath。
#           已知文件夹被策略重定向到取不到的地方时返回空串而不是抛，拼出来的是相对路径：
#           安装把快捷方式建到引导器当前目录，卸载把当前目录里那个同名文件夹整个删掉。
# 规则：Directory.Move 只允许出现在 MoveAside 与 RestoreBackup 两个方法里——
#       前者只被同卷分支调用，后者自己先判 SameVolume。别处要挪目录，走这两个入口。
#       已知文件夹先取进局部变量、判过空，再进 Path.Combine。
# 安装备份只做同级重命名；跨卷恢复使用 CopyTree 并保留源。
#
# 用法: pwsh -NoProfile -File scripts/lint-setup-paths.ps1 [-Quiet]
param(
    [string[]]$Paths = @(),
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Paths.Count -eq 0) { $Paths = @((Join-Path $root "src\Setup")) }

$RxMove     = '\bDirectory\.Move\s*\('
$RxMethod   = '^\s*(?:private|internal|public|protected)\s+(?:static\s+)?[\w\?<>,\[\]\s]+?\s(\w+)\s*\('
$Allowed    = @('MoveAside', 'RestoreBackup')
$RxRawKnown = 'Path\.Combine\([^)]*Environment\.GetFolderPath'

$script:viol = [System.Collections.Generic.List[object]]::new()
$files = [System.Collections.Generic.List[string]]::new()

foreach ($p in $Paths) {
    if (-not (Test-Path $p)) { continue }
    $item = Get-Item $p
    $targets = if ($item.PSIsContainer) { @(Get-ChildItem -Path $p -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) }
               else { @($item) }
    foreach ($f in $targets) {
        $files.Add($f.FullName)
        $rel = $f.FullName.Substring($root.Length + 1)
        $lines = @(Get-Content -LiteralPath $f.FullName -Encoding UTF8)
        $method = ''
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $ln = $lines[$i]
            $m = [regex]::Match($ln, $RxMethod)
            if ($m.Success) { $method = $m.Groups[1].Value }
            $kind = $null
            if ($ln -match $RxMove -and $Allowed -notcontains $method) { $kind = "整目录移动没有同卷判断" }
            elseif ($ln -match $RxRawKnown) { $kind = "已知文件夹没判空就拼进路径" }
            if ($kind) {
                $script:viol.Add([pscustomobject]@{
                    Kind = $kind; File = $rel; Line = $i + 1; Method = $method; Text = $ln.Trim()
                })
            }
        }
    }
}

if (-not $Quiet -and $script:viol.Count -gt 0) {
    foreach ($g in ($script:viol | Group-Object Kind)) {
        Write-Host ("`n== {0}  ({1} 处) ==" -f $g.Name, $g.Count)
        foreach ($v in $g.Group) {
            Write-Host ("  [FAIL] {0}:{1}  方法 {2}：{3}" -f $v.File, $v.Line, $(if ($v.Method) { $v.Method } else { '<字段/顶层>' }), $v.Text)
        }
        if ($g.Name -eq '整目录移动没有同卷判断') {
            Write-Host "  跨卷没有「移动」：同卷走 MoveAside，跨卷走 CopyTree + DeleteWithRetry"
        } else {
            Write-Host "  空串拼出来的是相对路径：先取进局部变量、判过空，再拼"
        }
    }
}
Write-Host ("LINT-SETUP-PATHS {0}  FAIL={1} 文件={2}" -f `
    $(if ($script:viol.Count -eq 0) { "PASS" } else { "FAIL" }), $script:viol.Count, $files.Count)
if ($script:viol.Count -gt 0) { exit 1 } else { exit 0 }
