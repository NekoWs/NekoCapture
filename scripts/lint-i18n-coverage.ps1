# lint-i18n-coverage.ps1 — 已迁移文件里不许再出现没挂取词函数的中文文案
#
# 判定：一个 .cs 文件里出现过 Loc.T(/Loc.F(（宿主）或 T(（GestureEditor 库，取词函数由宿主注入）
# 就算「已迁移」；这类文件里带中文的字符串字面量必须就是那次取词调用的参数，否则英文下会露出中文。
# 整文件还没动过的先跳过，只在末尾报数。
# 豁免：StringsEn.*.cs（中文键就是源语言本身）、Trace/Debug/Console 行与 throw 行（日志与异常消息不进译表）、
# 行尾 // copyok（这行的文案本就不用翻）、行尾 // lockeys（这行的中文是取词的键，取词在读取处）、
# 行尾 // locpending（等下一批整窗迁移，计数报出来）。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fail = 0
$migrated = 0
$locPending = 0
$keyLines = 0
$pending = @()

# 取词调用：宿主的 Loc.T/Loc.F，与库里注入的 T(...)。点号前的 T（Foo.T(）不算。
$reCall = [regex]'Loc\.(T|F)\(|(?<![\w.])T\('
$reCallArg = [regex]'(Loc\.(T|F)|(?<![\w.])T)\(\s*@?"(?:\\.|[^"\\])*"'
$reLiteral = [regex]'"(?:\\.|[^"\\])*"'
$reCjk = [regex]'[\u4e00-\u9fff]'
# 不进译表的行：日志与异常消息（AGENTS 文案第 7 条）
$reNonUi = 'Trace\.(Log|Fail|Warn)|Debug\.Write|Console\.Write|\bthrow\b'

$dirs = @('src\FrameCapture', 'src\GestureEditor') | ForEach-Object { Join-Path $root $_ }
foreach ($f in Get-ChildItem -Path $dirs -Recurse -Include *.cs) {
    if ($f.Name -like 'StringsEn.*') { continue }
    $lines = Get-Content $f.FullName -Encoding UTF8
    $text = $lines -join "`n"
    if ($text -notmatch $reCall) {
        $n = 0
        foreach ($l in $lines) {
            if ($l -match '^\s*(///|//|\*)') { continue }
            if ($l -match $reNonUi) { continue }
            $n += @($reLiteral.Matches($l) | Where-Object { $reCjk.IsMatch($_.Value) }).Count
        }
        if ($n -gt 0) { $pending += [pscustomobject]@{ File = $f.Name; Count = $n } }
        continue
    }
    $migrated++
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l -match '^\s*(///|//|\*)') { continue }
        if ($l -match $reNonUi) { continue }
        if ($l -match '//\s*copyok') { continue }
        if ($l -match '//\s*lockeys') { $keyLines++; continue }
        if ($l -match '//\s*locpending') { $locPending++; continue }
        # 行尾注释里引用的中文不是界面文案
        $code = [regex]::Replace($l, '\s//.*$', '')
        $code = $reCallArg.Replace($code, '')
        foreach ($m in $reLiteral.Matches($code)) {
            if (-not $reCjk.IsMatch($m.Value)) { continue }
            $rel = $f.FullName.Substring($root.Length + 1)
            if (-not $Quiet) { Write-Host "  FAIL $rel`:$($i + 1)  $($m.Value)" }
            $fail++
        }
    }
}

$todo = ($pending | Measure-Object Count -Sum).Sum
if ($fail -eq 0) {
    Write-Host "LINT-I18N PASS FAIL=0  已迁移文件=$migrated  待整窗迁移文件=$($pending.Count)（中文 $todo 条）  挂起行=$locPending  键表行=$keyLines"
}
else {
    Write-Host "LINT-I18N FAIL=$fail  已迁移文件里残留未挂取词函数的中文文案"
}
exit $(if ($fail -gt 0) { 1 } else { 0 })
