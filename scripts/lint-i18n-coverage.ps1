# lint-i18n-coverage.ps1 — 文案表与取词点的完整性
#
# 每条界面文案是 Core\Strings 里一行 Str 记录：稳定 id、中文原文、英文译名。取词点写 K.<界面>.<名字>。
# 本护栏盯四件事：
#   1 取词点必须引用字段。Loc.T("中文") 这种字面量参数在新机制里编译不过，写出来就是绕过表。
#   2 界面代码里不许有裸中文串。日志、Trace、异常消息不进表（AGENTS 文案第 7 条），行尾 // copyok 豁免。
#   3 表自身卫生：id 唯一且合式、中文列不重复、英文列不许空着。
#   4 每条文案都得有人用。没人取的字段是死串，删掉比留着强。
# 另外盯住安装器共用的 Core 源文件：那边没有 Loc 也没有 K，引用即 FAIL。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fail = 0
$stringsDir = Join-Path $root 'src\FrameCapture\Core\Strings'
$dirs = @('src\FrameCapture', 'src\CodecSetup') | ForEach-Object { Join-Path $root $_ }

function Write-Fail([string]$rel, [int]$line, [string]$msg) {
    if (-not $Quiet) { Write-Host "  FAIL $rel`:$line  $msg" }
    return 1
}

$reLiteral = [regex]'"(?:\\.|[^"\\])*"'
$reCjk = [regex]'[\u4e00-\u9fff]'
$reCall = [regex]'(?<![\w.])Loc\.(T|F)\('
# 不进译表的行：日志与异常消息
$reNonUi = 'Trace\.(Log|Fail|Warn)|Debug\.Write|Console\.Write|\bthrow\b'

# ---------- 1 · 2：取词点写法与裸中文串 ----------
$used = @{}        # 代码里引用过的 K.<界面>.<名字>
$callSites = 0
foreach ($f in Get-ChildItem -Path $dirs -Recurse -Include *.cs) {
    $rel = $f.FullName.Substring($root.Length + 1)
    $inTable = $f.DirectoryName -eq $stringsDir
    $lines = Get-Content $f.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($inTable) { continue }
        if ($l -match '^\s*(///|//|\*)') { continue }
        foreach ($m in [regex]::Matches($l, '\bK\.([A-Z]\w*)\.([A-Za-z]\w*)')) { $used["$($m.Groups[1].Value).$($m.Groups[2].Value)"] = $true }
        $code = [regex]::Replace($l, '\s//.*$', '')
        foreach ($m in $reCall.Matches($code)) {
            $callSites++
            # 取词点后面紧跟引号 = 把中文当键写在调用处
            if ($code.Substring($m.Index + $m.Length).TrimStart() -match '^@?"') {
                $fail += Write-Fail $rel ($i + 1) '取词点必须引用 K 里的字段，不能写字面量'
            }
        }
        if ($l -match $reNonUi) { continue }
        if ($l -match '//\s*copyok') { continue }
        foreach ($m in $reLiteral.Matches($code)) {
            if (-not $reCjk.IsMatch($m.Value)) { continue }
            $fail += Write-Fail $rel ($i + 1) "界面文案没走取词：$($m.Value)"
        }
    }
}

# ---------- 3：表自身 ----------
$reDecl = [regex]'public static readonly Str (\w+) = new\(\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*,\s*"((?:\\.|[^"\\])*)"\s*\)'
$byId = @{}
$byZh = @{}
$declCount = 0
foreach ($sf in Get-ChildItem $stringsDir -Filter *.cs) {
    $rel = $sf.FullName.Substring($root.Length + 1)
    $text = Get-Content $sf.FullName -Raw -Encoding UTF8
    $surface = $sf.BaseName
    foreach ($m in $reDecl.Matches($text)) {
        $declCount++
        $line = 1 + ([regex]::Matches($text.Substring(0, $m.Index), "`n")).Count
        $name = $m.Groups[1].Value; $id = $m.Groups[2].Value; $zh = $m.Groups[3].Value; $en = $m.Groups[4].Value
        if ($id -cne $id.Trim() -or $id -notmatch '^[a-z][A-Za-z0-9]*(\.[A-Za-z0-9]+)+$') {
            $fail += Write-Fail $rel $line "id 不合式（小写开头的点分驼峰）：$id"
        }
        # Common 的共享词允许保留稳定 id 的界面段。
        if ($surface -ne 'Common' -and $id.Split('.')[0] -ne $surface.ToLower()) {
            $fail += Write-Fail $rel $line "id 的界面段与所在文件不符：$id"
        }
        if ($byId.ContainsKey($id)) { $fail += Write-Fail $rel $line "id 重复：$id（$($byId[$id])）" } else { $byId[$id] = $rel }
        if ($zh.Length -gt 0 -and $byZh.ContainsKey($zh)) { $fail += Write-Fail $rel $line "同一句中文登记了两个字段：$zh（$($byZh[$zh])）" } elseif ($zh.Length -gt 0) { $byZh[$zh] = $rel }
        if ($en.Length -eq 0 -and $id -ne 'core.empty') { $fail += Write-Fail $rel $line "英文列空着：$id" }
    }
}

# ---------- 4：死串 ----------
$declared = @{}
foreach ($sf in Get-ChildItem $stringsDir -Filter *.cs) {
    $surface = $sf.BaseName
    foreach ($m in [regex]::Matches((Get-Content $sf.FullName -Raw -Encoding UTF8), 'public static readonly Str (\w+) = new\(')) {
        $declared["$surface.$($m.Groups[1].Value)"] = $sf.FullName.Substring($root.Length + 1)
    }
}
foreach ($k in ($declared.Keys | Sort-Object)) {
    if ($used.ContainsKey($k)) { continue }
    $fail += Write-Fail $declared[$k] 0 "没人取的文案：K.$k"
}

# ---------- 占位符：中文列与英文列的 {n} 必须一一对应 ----------
# 少写一个 {0} 编译不报错，只会把「保存到 D:\…」印成「保存到 」——数据在格式化那步静悄悄丢掉。
foreach ($sf in Get-ChildItem $stringsDir -Filter *.cs) {
    $raw = (Get-Content $sf.FullName -Raw -Encoding UTF8) -replace '\s+', ' '
    foreach ($m in [regex]::Matches($raw, '"([a-z]+\.[A-Za-z0-9]+)",\s*"(.*?[^"\\])",\s*"(.*?[^"\\])"')) {
        $zh = @([regex]::Matches($m.Groups[2].Value, '\{\d+\}') | ForEach-Object { $_.Value }) -join ','
        $en = @([regex]::Matches($m.Groups[3].Value, '\{\d+\}') | ForEach-Object { $_.Value }) -join ','
        if ($zh -ne $en) {
            $fail += Write-Fail ($sf.FullName.Substring($root.Length + 1)) 0 `
                "占位符对不上：$($m.Groups[1].Value) 中文[$zh] 英文[$en]"
        }
    }
}

# ---------- 安装器共用文件 ----------
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
            if ($lines[$i] -notmatch '(?<![\w.])(Loc\.(T|F)\(|K\.[A-Z]\w*\.[A-Za-z])') { continue }
            $fail += Write-Fail $rel ($i + 1) '安装器共用文件引用了取词——取词交给调用方'
        }
    }
}

if ($fail -eq 0) {
    Write-Host "LINT-I18N PASS FAIL=0  文案=$($declared.Count)  取词点=$callSites  裸中文串=0  缺英文=0  孤儿键=0"
    exit 0
}
Write-Host "LINT-I18N FAIL=$fail"
exit 1
