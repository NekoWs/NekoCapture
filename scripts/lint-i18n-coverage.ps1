# lint-i18n-coverage.ps1 — 已迁移文件里不许再出现没挂取词函数的中文文案
#
# 判定：一个 .cs 文件里出现过 Loc.T(/Loc.F( 就算「已迁移」；这类文件里带中文的字符串字面量必须就是那次取词调用的参数，
# 否则英文下会露出中文。
# 整文件还没动过的先跳过，只在末尾报数。
# 豁免：StringsEn.*.cs（中文键就是源语言本身）、Trace/Debug/Console 行与 throw 行（日志与异常消息不进译表）、
# 行尾 // copyok（这行的文案本就不用翻）、行尾 // lockeys（这行的中文是取词的键，取词在读取处）、
# 行尾 // locpending（等下一批整窗迁移，计数报出来）。
# 另外盯住安装器共用的 Core 源文件：那边没有 Loc，引用即 FAIL。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fail = 0
$migrated = 0
$locPending = 0
$keyLines = 0
$pending = @()

# 取词调用：宿主的 Loc.T/Loc.F，与库里注入的 T(...)。点号前的 T（Foo.T(）不算。
$reCall = [regex]'Loc\.(T|F)\('
$reCallArg = [regex]'Loc\.(T|F)\(\s*@?"(?:\\.|[^"\\])*"'
$reLiteral = [regex]'"(?:\\.|[^"\\])*"'
$reCjk = [regex]'[\u4e00-\u9fff]'
# 不进译表的行：日志与异常消息（AGENTS 文案第 7 条）
$reNonUi = 'Trace\.(Log|Fail|Warn)|Debug\.Write|Console\.Write|\bthrow\b'

$dirs = @('src\FrameCapture') | ForEach-Object { Join-Path $root $_ }
foreach ($f in Get-ChildItem -Path $dirs -Recurse -Include *.cs) {
    if ($f.Name -like 'StringsEn.*') { continue }
    $lines = Get-Content $f.FullName -Encoding UTF8
    $text = $lines -join "`n"
    if ($text -notmatch $reCall) {
        $n = 0
        foreach ($l in $lines) {
            if ($l -match '^\s*(///|//|\*)') { continue }
            if ($l -match $reNonUi) { continue }
            if ($l -match '//\s*copyok') { continue }
            # 中文是取词的键、取词发生在读取处：整窗没迁移也一样不算界面文案
            if ($l -match '//\s*lockeys') { $keyLines++; continue }
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

# 挂了取词却没登记译名 = 英文下静默回退中文：界面看着"迁移过了"，切语言却什么都不变。
# 因此取词调用里的每个中文键都必须在译表里出现（Loc.T(变量) 这种非字面量参数扫不到，交给整窗迁移时人工过）。
# 字面量有两种写法：普通串（反斜杠转义）与逐字串 @"…"（双写引号转义，路径类键常用）。
# 只认一种，另一种就是静默的缺译盲区——收表和扫调用点必须用同一套写法与同一个还原规则。
$reEntryKey = [regex]'\(\s*(?<at>@)?"(?<k>(?(at)(?:""|[^"])*|(?:\\.|[^"\\])*))"\s*,\s*'
$reKeyArg = [regex]'(?:Loc\.(?:T|F)|(?<![\w.])T)\(\s*(?<at>@)?"(?<k>(?(at)(?:""|[^"])*|(?:\\.|[^"\\])*))"'
function ConvertFrom-CSharpString([string]$raw, [bool]$verbatim) {
    if ($verbatim) { return $raw.Replace('""', '"') }
    return [regex]::Replace($raw, '\\(.)', '$1')
}
$regPath = Join-Path $root 'src\FrameCapture\Core'
$registered = @{}
foreach ($sf in Get-ChildItem $regPath -Filter 'StringsEn.*.cs') {
    foreach ($m in $reEntryKey.Matches((Get-Content $sf.FullName -Raw -Encoding UTF8))) {
        $registered[(ConvertFrom-CSharpString $m.Groups['k'].Value $m.Groups['at'].Success)] = $true
    }
}
foreach ($f in Get-ChildItem -Path $dirs -Recurse -Include *.cs) {
    if ($f.Name -like 'StringsEn.*') { continue }
    $i = 0
    foreach ($l in (Get-Content $f.FullName -Encoding UTF8)) {
        $i++
        if ($l -match '^\s*(///|//|\*)') { continue }
        foreach ($m in $reKeyArg.Matches($l)) {
            $k = ConvertFrom-CSharpString $m.Groups['k'].Value $m.Groups['at'].Success
            if (-not $reCjk.IsMatch($k)) { continue }
            if ($registered.ContainsKey($k)) { continue }
            if (-not $Quiet) { Write-Host "  FAIL $($f.Name)`:$i  取词键未登记译名：$k" }
            $fail++
        }
    }
}

# 键表（// lockeys）返回中文原文、读取处才取词，所以 Loc.T(变量) 的取值全住在这里。
# 非字面量参数上面的正则扫不到，键表里的每个中文原文却扫得到：它们必须登记译名，
# 否则英文界面就在读一个没有译名的键——帮助窗「绘图」那一页整栏露中文就是这么来的。
# 内插串 $"…{变量}…" 天生当不了键：它带着占位符原文，登记了也匹配不上，只能写成 Loc.F 的格式键。
$reAnyStr = [regex]'(?<at>@)?"(?<v>(?(at)(?:""|[^"])*|(?:\\.|[^"\\])*))"'
$keyTableMisses = 0
foreach ($f in Get-ChildItem -Path $dirs -Recurse -Include *.cs) {
    if ($f.Name -like 'StringsEn.*') { continue }
    $i = 0
    foreach ($l in (Get-Content $f.FullName -Encoding UTF8)) {
        $i++
        if ($l -notmatch '//\s*lockeys') { continue }
        foreach ($m in $reAnyStr.Matches([regex]::Replace($l, '\s//.*$', ''))) {
            $k = ConvertFrom-CSharpString $m.Groups['v'].Value $m.Groups['at'].Success
            if (-not $reCjk.IsMatch($k)) { continue }
            if ($registered.ContainsKey($k)) { continue }
            if (-not $Quiet) { Write-Host "  FAIL $($f.Name)`:$i  键表原文未登记译名：$k" }
            $keyTableMisses++
        }
    }
}
$fail += $keyTableMisses

# 安装器（net48）与宿主共用几个 Core 源文件，那边没有 Loc：这些文件里出现取词调用会让 Setup 编译失败，
# 而 Setup 只在护栏最后一关才编，等到那时才发现等于把错误留给下一轮。
$setupProj = Join-Path $root 'src\Setup\Setup.csproj'
if (Test-Path $setupProj) {
    $shared = Select-String -Path $setupProj -Pattern '<Compile Include="\.\.\\(FrameCapture\\[^"]+)"' |
        ForEach-Object { $_.Matches[0].Groups[1].Value }
    foreach ($rel in $shared) {
        $f = Join-Path $root (Join-Path 'src' $rel)
        if (-not (Test-Path $f)) { continue }
        $lines = Get-Content $f -Encoding UTF8
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*(///|//|\*)') { continue }
            if ($lines[$i] -notmatch '(?<![\w.])Loc\.(T|F)\(') { continue }
            if (-not $Quiet) { Write-Host "  FAIL $rel`:$($i + 1)  安装器共用文件引用了 Loc——取词交给调用方" }
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
