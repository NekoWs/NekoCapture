# i18n-rekey.ps1 — 把「中文原文当取词键」迁成「独立英文 key」
#
# 阶段一（-Validate）：解析现有中英对照表 + 收集全部取词点，只报数不改文件。
#   要求：表里每条都能解析出中英两列；每个 Loc.T/Loc.F 字面量都能在表里找到译名；
#   两个条件任一不满足就 FAIL 退出——迁移脚本不许带着未知文案去改写源码。
# 阶段二（-Apply）：按界面分组给每条文案生成 K.<界面>.<名字> 字段与 id，重写调用点，
#   生成 Core/Strings/*.cs，删掉旧的 StringsEn.*.cs。
param([switch]$Validate, [switch]$Apply, [switch]$Catalogs)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# ---------- C# 字符串字面量扫描 ----------
# 只认 "…" 与 @"…"，反斜杠转义按 C# 规则处理；返回 (值, 结束位置) 对。
function Read-Literal([string]$s, [ref]$i) {
    $n = $s.Length
    if ($i.Value -ge $n -or $s[$i.Value] -ne '"') { return $null }
    $verbatim = $i.Value -gt 0 -and $s[$i.Value - 1] -eq '@'
    $i.Value++
    $sb = New-Object System.Text.StringBuilder
    while ($i.Value -lt $n) {
        $c = $s[$i.Value]
        if ($verbatim) {
            if ($c -eq '"') {
                if ($i.Value + 1 -lt $n -and $s[$i.Value + 1] -eq '"') { [void]$sb.Append('"'); $i.Value += 2; continue }
                $i.Value++; return $sb.ToString()
            }
            [void]$sb.Append($c); $i.Value++; continue
        }
        if ($c -eq '\') {
            $nx = if ($i.Value + 1 -lt $n) { $s[$i.Value + 1] } else { '' }
            # \uXXXX 与 \xXX 解成码位本身：不认它们，「← 上一页」就会变成字面量 u2190
            if ($nx -eq 'u' -or $nx -eq 'x') {
                $len = if ($nx -eq 'u') { 4 } else { 2 }
                $hex = $s.Substring($i.Value + 2, [Math]::Min($len, $n - $i.Value - 2))
                if ($hex -match '^[0-9A-Fa-f]+$') {
                    [void]$sb.Append([char][Convert]::ToInt32($hex, 16))
                    $i.Value += 2 + $hex.Length
                    continue
                }
            }
            switch ($nx) {
                'n' { [void]$sb.Append("`n") } 't' { [void]$sb.Append("`t") } 'r' { [void]$sb.Append("`r") }
                '0' { [void]$sb.Append([char]0) } '"' { [void]$sb.Append('"') } '\' { [void]$sb.Append('\') }
                default { [void]$sb.Append($nx) }
            }
            $i.Value += 2; continue
        }
        if ($c -eq '"') { $i.Value++; return $sb.ToString() }
        [void]$sb.Append($c); $i.Value++
    }
    return $null
}

# 跳空白、可选 @，再读一个字面量；读不到返回 null。表里 "…" 与 @"…" 混用，逐字面量那条带反斜杠。
function Next-Lit([string]$s, [ref]$i) {
    $n = $s.Length
    while ($i.Value -lt $n -and $s[$i.Value] -match '\s') { $i.Value++ }
    if ($i.Value -lt $n -and $s[$i.Value] -eq '@') { $i.Value++ }
    return Read-Literal $s $i
}

# ---------- 解析对照表：每个 ("中", "英") 元组一行 ----------
function Parse-Table([string]$text) {
    $pairs = New-Object System.Collections.Generic.List[object]
    $i = 0; $n = $text.Length
    while ($i -lt $n) {
        $j = $text.IndexOf('(', $i)
        if ($j -lt 0) { break }
        $p = $j + 1
        $zh = Next-Lit $text ([ref]$p)
        if ($null -eq $zh) { $i = $j + 1; continue }
        while ($p -lt $n -and $text[$p] -notin @(',', '"')) { $p++ }
        if ($p -ge $n -or $text[$p] -ne ',') { $i = $j + 1; continue }
        $p++
        $en = Next-Lit $text ([ref]$p)
        if ($null -eq $en) { $i = $j + 1; continue }
        # 相邻的 + "…" 属于同一个英文值：拼回去，别让它们被当成下一条
        while ($true) {
            $q = $p
            while ($q -lt $n -and $text[$q] -match '\s') { $q++ }
            if ($q -lt $n -and $text[$q] -eq '+') {
                $q++
                $more = Next-Lit $text ([ref]$q)
                if ($null -eq $more) { break }
                $en += $more; $p = $q
            } else { break }
        }
        $pairs.Add([pscustomobject]@{ Zh = $zh; En = $en })
        $i = $p
    }
    return $pairs
}

# ---------- 读已生成的声明：Str 名字 = new("id", "中", "英")，允许折行 ----------
function Parse-Decls([string]$text) {
    $out = @()
    foreach ($m in [regex]::Matches($text, 'Str (\w+) = new\(')) {
        $p = $m.Index + $m.Length
        $id = Next-Lit $text ([ref]$p); if ($null -eq $id) { continue }
        while ($p -lt $text.Length -and $text[$p] -ne ',') { $p++ }; $p++
        $zh = Next-Lit $text ([ref]$p); if ($null -eq $zh) { continue }
        while ($p -lt $text.Length -and $text[$p] -ne ',') { $p++ }; $p++
        $en = Next-Lit $text ([ref]$p); if ($null -eq $en) { continue }
        $out += [pscustomobject]@{ Name = $m.Groups[1].Value; Id = $id; Zh = $zh; En = $en }
    }
    return $out
}

# ---------- 收集取词点 ----------
function Scan-Calls([string]$text) {
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($m in [regex]::Matches($text, 'Loc\.(T|F)\(\s*(@?")')) {
        $start = $m.Groups[2].Index                          # Group.Index 已是全文绝对位置，替换区间连 @ 一起吃掉
        $p = $start
        if ($text[$p] -eq '@') { $p++ }
        $v = Read-Literal $text ([ref]$p)
        if ($null -ne $v) {
            $out.Add([pscustomobject]@{ Kind = $m.Groups[1].Value; Zh = $v; Start = $start; End = $p })
        }
    }
    return $out
}

# ---------- 界面归属：一个文件只属于一个界面，没登记的文件直接 FAIL ----------
$SurfaceExact = @{
    'HelpWindow.cs' = 'Help'
    'TrayService.cs' = 'Tray'
    'App.xaml.cs' = 'Tray'
    'PinWindow.cs' = 'Pin'
    'FilmStripDialog.cs' = 'Editor'
    'SettingsWindow.Hotkeys.cs' = 'Hotkeys'
    'ChordCaptureBox.cs' = 'Hotkeys'
    'GestureConfig.cs' = 'Hotkeys'
    'OcrResultWindow.cs' = 'Capture'
    'OcrService.cs' = 'Capture'
    'CaptureToast.cs' = 'Capture'
    'WelcomeToast.cs' = 'Capture'
    'NoticeToast.cs' = 'Capture'
    'CountdownWindow.cs' = 'Capture'
    'ScrollHintWindow.cs' = 'Capture'
    'ScrollPreviewWindow.cs' = 'Capture'
    'RegionOutlineWindow.cs' = 'Capture'
}
$SurfacePrefix = @(
    @{ P = 'SettingsWindow'; S = 'Settings' }
    @{ P = 'EditorWindow'; S = 'Editor' }
    @{ P = 'RegionPickerWindow'; S = 'Capture' }
    @{ P = 'CaptureController'; S = 'Capture' }
)
function Surface-Of([string]$name) {
    if ($SurfaceExact.ContainsKey($name)) { return $SurfaceExact[$name] }
    foreach ($r in $SurfacePrefix) { if ($name.StartsWith($r.P)) { return $r.S } }
    return $null
}
$SurfaceDoc = @{
    Help = '使用指南窗'; Settings = '设置窗'; Hotkeys = '快捷键页与录键框'
    Editor = '标注与视频编辑器'; Pin = '贴图窗'; Tray = '托盘与进程入口'; Capture = '捕获过程与各种浮窗'
    Common = '两个界面以上共用的文案'
}

# 英文原文取不出词形的条目（纯标点这类），名字手工给，缺了就直接 FAIL 而不是编一个看不懂的。
$NameOverride = @{ '，' = 'Comma'; '、' = 'ListSeparator'; '；' = 'Semicolon'; '「{0}」' = 'QuotedName' }

function Slug-Of([string]$en) {
    $words = @([regex]::Matches($en, '[A-Za-z][A-Za-z0-9]*') | ForEach-Object { $_.Value } | Select-Object -First 5)
    if ($words.Count -eq 0) { return '' }
    # 长句子截出来的名字结尾常挂着 the / of 这种没信息量的词，去掉再拼
    while ($words.Count -gt 1 -and 'The', 'A', 'An', 'Of', 'To', 'At', 'In', 'On', 'And', 'Or', 'For', 'Is' -contains $words[-1]) {
        $words = $words[0..($words.Count - 2)]
    }
    $s = ($words | ForEach-Object { $_.Substring(0, 1).ToUpper() + $_.Substring(1).ToLower() }) -join ''
    if ($s.Length -gt 44) { $s = $s.Substring(0, 44) }
    return $s
}
function Esc([string]$s) {
    # 反斜杠与引号先转义，再把扫描时还原出来的真换行/制表符写回转义形式。
    # 替换串里的反斜杠不参与转义：'\\' 就是两个字符，多写一个文件里就成两条反斜杠。
    $t = ($s -replace '\\', '\\') -replace '"', '\"'
    ($t -replace "`r`n", '\n') -replace "`n", '\n' -replace "`r", '\n' -replace "`t", '\t'
}
function Camel([string]$s) { if ($s.Length -lt 2) { return $s.ToLower() } $s.Substring(0, 1).ToLower() + $s.Substring(1) }

$tableFiles = Get-ChildItem (Join-Path $root 'src\FrameCapture\Core') -Filter 'StringsEn*.cs'
$map = @{}
$dups = 0
foreach ($f in $tableFiles) {
    foreach ($p in Parse-Table ([System.IO.File]::ReadAllText($f.FullName))) {
        if ($map.ContainsKey($p.Zh)) {
            if ($map[$p.Zh] -ne $p.En) { Write-Host ("  同一条中文有两个译名：{0}" -f $p.Zh) -ForegroundColor Yellow; $dups++ }
        } else { $map[$p.Zh] = $p.En }
    }
}
$codeFiles = Get-ChildItem (Join-Path $root 'src\FrameCapture') -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\Core\\StringsEn' }
$calls = 0; $missing = New-Object System.Collections.Generic.List[string]; $emptyEn = 0
$perFile = @{}
foreach ($f in $codeFiles) {
    $cs = @(Scan-Calls ([System.IO.File]::ReadAllText($f.FullName)))
    if ($cs.Count -eq 0) { continue }
    $perFile[$f.FullName] = $cs
    $calls += $cs.Count
    foreach ($c in $cs) {
        if (-not $map.ContainsKey($c.Zh)) { $missing.Add(("{0}: {1}" -f $f.Name, $c.Zh)) }
        elseif ([string]::IsNullOrWhiteSpace($map[$c.Zh])) { $emptyEn++ }
    }
}
Write-Host ("对照表 {0} 条（{1} 个文件），取词点 {2} 处（{3} 个文件）" -f $map.Count, $tableFiles.Count, $calls, $perFile.Count)
if ($dups) { Write-Host ("  冲突译名 {0} 条" -f $dups) -ForegroundColor Red }
if ($emptyEn) { Write-Host ("  英文列为空 {0} 条" -f $emptyEn) -ForegroundColor Yellow }
if ($missing.Count) {
    Write-Host ("  表里找不到的取词键 {0} 条：" -f $missing.Count) -ForegroundColor Red
    $missing | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
}
$ok = ($missing.Count -eq 0 -and $dups -eq 0)
Write-Host ("I18N-REKEY {0}" -f $(if ($ok) { 'VALIDATE PASS' } else { 'VALIDATE FAIL' }))
if (-not $ok) { exit 1 }
if (-not $Apply -and -not $Catalogs) { exit 0 }
if ($Apply) {

# ---------- 归属与命名 ----------
$unmapped = $perFile.Keys | ForEach-Object { Surface-Of ([System.IO.Path]::GetFileName($_)) } | Where-Object { $null -eq $_ }
if ($unmapped) { Write-Host "  未登记界面的文件：" -ForegroundColor Red; $unmapped | ForEach-Object { Write-Host "    $_" }; exit 1 }

$byZh = @{}
foreach ($path in $perFile.Keys) {
    $s = Surface-Of ([System.IO.Path]::GetFileName($path))
    foreach ($c in $perFile[$path]) {
        if (-not $byZh.ContainsKey($c.Zh)) { $byZh[$c.Zh] = @{ En = $map[$c.Zh]; Surfaces = @{} } }
        if ($byZh[$c.Zh].Surfaces.ContainsKey($s)) { $byZh[$c.Zh].Surfaces[$s] += 1 } else { $byZh[$c.Zh].Surfaces[$s] = 1 }
    }
}
$assigned = @{}          # zh -> @{Owner=..; Name=..; Id=..}
$taken = @{}             # "owner.name" -> zh
$ids = @{}
$nameless = 0
foreach ($zh in ($byZh.Keys | Sort-Object)) {
    $en = $byZh[$zh].En
    $sur = @($byZh[$zh].Surfaces.Keys | Sort-Object)
    $owner = if ($sur.Count -gt 1) { 'Common' } else { $sur[0] }
    $name = Slug-Of $en
    if (-not $name -and $NameOverride.ContainsKey($zh)) { $name = $NameOverride[$zh] }
    if (-not $name) { Write-Host ("  取不出词形，需要手工命名：[{0}] → [{1}]" -f $zh, $en) -ForegroundColor Red; $nameless++; continue }
    $base = $name; $k = 2
    while ($taken.ContainsKey("$owner.$name")) { $name = "$base$k"; $k++ }
    $id = "{0}.{1}" -f $owner.ToLower(), (Camel $name)
    $ik = 2
    while ($ids.ContainsKey($id)) { $id = "{0}.{1}.{2}" -f $owner.ToLower(), (Camel $name), $ik; $ik++ }
    $taken["$owner.$name"] = $zh; $ids[$id] = $zh
    $assigned[$zh] = @{ Owner = $owner; Name = $name; Id = $id }
}
if ($nameless) { Write-Host 'I18N-REKEY APPLY FAIL（先补 $NameOverride）' -ForegroundColor Red; exit 1 }
$shared = @($assigned.Values | Where-Object { $_.Owner -eq 'Common' }).Count
Write-Host ("文案 {0} 条，其中跨界面共用 {1} 条" -f $assigned.Count, $shared)

# ---------- 重写调用点 ----------
$rewritten = 0
foreach ($path in $perFile.Keys) {
    $txt = [System.IO.File]::ReadAllText($path)
    foreach ($c in (@($perFile[$path]) | Sort-Object Start -Descending)) {
        $a = $assigned[$c.Zh]
        $txt = $txt.Remove($c.Start, $c.End - $c.Start).Insert($c.Start, ("K.{0}.{1}" -f $a.Owner, $a.Name))
        $rewritten++
    }
    [System.IO.File]::WriteAllText($path, $txt, (New-Object System.Text.UTF8Encoding($false)))
}
Write-Host ("重写取词点 {0} 处" -f $rewritten)

# ---------- 补 K 的别名 ----------
# 有的文件把 Loc 写成别名或 Core.Loc 限定，裸 K 认不出来；这类文件补一行 using K = …。
$aliased = 0
foreach ($f in (Get-ChildItem (Join-Path $root 'src\FrameCapture') -Recurse -Include *.cs)) {
    $t = [System.IO.File]::ReadAllText($f.FullName)
    if ($t -notmatch '\bK\.[A-Z]\w*\.[A-Z]') { continue }
    if ($t -match '(?m)^using FrameCapture\.Core;') { continue }
    if ($t -match '(?m)^using K = FrameCapture\.Core\.K;') { continue }
    $u = [regex]::Matches($t, "(?m)^using (?![( ])[^\r\n]*;\r?\n")
    if ($u.Count -eq 0) { Write-Host ("  没有 using 块，需手工处理：{0}" -f $f.Name) -ForegroundColor Red; exit 1 }
    $last = $u[$u.Count - 1]
    $t = $t.Insert($last.Index + $last.Length, "using K = FrameCapture.Core.K;`r`n")
    [System.IO.File]::WriteAllText($f.FullName, $t, (New-Object System.Text.UTF8Encoding($false)))
    $aliased++
}
Write-Host ("补 K 别名 {0} 个文件" -f $aliased)

# ---------- 生成 Core/Strings/*.cs ----------
$dir = Join-Path $root 'src\FrameCapture\Core\Strings'
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$bySurface = @{}
foreach ($zh in $assigned.Keys) {
    $a = $assigned[$zh]
    if (-not $bySurface.ContainsKey($a.Owner)) { $bySurface[$a.Owner] = @() }
    $bySurface[$a.Owner] += ,@($a.Name, $a.Id, $zh, $byZh[$zh].En)
}
foreach ($s in $bySurface.Keys) {
    $L = New-Object System.Collections.Generic.List[string]
    $L.Add('namespace FrameCapture.Core;')
    $L.Add('')
    $L.Add('/// <summary>')
    $L.Add(("/// 文案 · {0}。一行一句：稳定 id、中文原文、英文译名。" -f $SurfaceDoc[$s]))
    $L.Add('/// id 定下来就不动——改文案只改后面两列，取词点不用跟着改。')
    $L.Add('/// </summary>')
    $L.Add('public static partial class K')
    $L.Add('{')
    $L.Add(("    /// <summary>{0}的文案。</summary>" -f $SurfaceDoc[$s]))
    $L.Add(("    public static partial class {0}" -f $s))
    $L.Add('    {')
    foreach ($e in ($bySurface[$s] | Sort-Object { $_[0] })) {
        $one = ('        public static readonly Str {0} = new("{1}", "{2}", "{3}");' -f $e[0], $e[1], (Esc $e[2]), (Esc $e[3]))
        if ($one.Length -le 118) { $L.Add($one) }
        else {
            $L.Add(('        public static readonly Str {0} = new(' -f $e[0]))
            $L.Add(('            "{0}",' -f $e[1]))
            $L.Add(('            "{0}",' -f (Esc $e[2])))
            $L.Add(('            "{0}");' -f (Esc $e[3])))
        }
    }
    $L.Add('    }')
    $L.Add('}')
    $L.Add('')
    [System.IO.File]::WriteAllText((Join-Path $dir ("{0}.cs" -f $s)), ($L -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  {0}.cs  {1} 条" -f $s, $bySurface[$s].Count)
}

# ---------- 删掉旧的英文表 ----------
foreach ($f in $tableFiles) { [System.IO.File]::Delete($f.FullName); Write-Host ("  删除 {0}" -f $f.Name) }
Write-Host 'I18N-REKEY APPLY DONE'
}

# ==================== 阶段三：目录里的取词键 ====================
# 动作词汇表、绘图工具目录、编辑器命令表、触发方式标签——这些函数返回「给人看的那句」，
# 中文原文写在目录里、读取处翻译。本阶段把目录改成直接携带 K 字段，读取处一行不用改。
if (-not $Catalogs) { exit 0 }

# 已生成的声明：zh -> @{Owner;Name}，让目录复用界面已经建好的字段，不重复登记同一句话。
$assigned = @{}
foreach ($f in Get-ChildItem (Join-Path $root 'src\FrameCapture\Core\Strings') -Filter *.cs) {
    foreach ($d in Parse-Decls ([System.IO.File]::ReadAllText($f.FullName))) {
        if (-not $assigned.ContainsKey($d.Zh)) { $assigned[$d.Zh] = @{ Owner = $f.BaseName; Name = $d.Name } }
    }
}

# 对照表已随阶段二删除，从版本库取回，只用来给目录里的新字段找英文。
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$old = @{}
foreach ($rel in @(git ls-tree -r --name-only HEAD -- src/FrameCapture/Core | Where-Object { $_ -match 'StringsEn' })) {
    foreach ($p in Parse-Table ((git --no-pager show "HEAD:$rel") -join "`n")) { $old[$p.Zh] = $p.En }
}
Write-Host ("版本库里的对照表 {0} 条，已登记字段 {1} 条" -f $old.Count, $assigned.Count)
if ($old.Count -lt 400) { Write-Host '取回的对照表条数不对，八成是输出编码问题' -ForegroundColor Red; exit 1 }

$pending = @{}   # 新登记的：surface -> list of @(name, id, zh, en)
function Add-Field([string]$zh, [string]$surface, [string]$name) {
    if ($assigned.ContainsKey($zh)) { return $assigned[$zh] }
    if (-not $old.ContainsKey($zh)) { throw ("对照表里没有这条中文，无法登记：[{0}]" -f $zh) }
    $en = $old[$zh]
    if (-not $name) { $name = Slug-Of $en }
    if (-not $name) { throw ("取不出词形，需要手工命名：[{0}] → [{1}]" -f $zh, $en) }
    $base = $name; $k = 2
    while (@($assigned.Values | Where-Object { $_.Owner -eq $surface -and $_.Name -eq $name }).Count) { $name = "$base$k"; $k++ }
    $e = @{ Owner = $surface; Name = $name }
    $assigned[$zh] = $e
    if (-not $pending.ContainsKey($surface)) { $pending[$surface] = @() }
    $pending[$surface] += ,@($name, ("{0}.{1}" -f $surface.ToLower(), (Camel $name)), $zh, $en)
    return $e
}

$ToolWord = @{ V = 'Select'; L = 'Line'; R = 'Rect'; O = 'Oval'; A = 'Arrow'; P = 'Pen'; H = 'Highlight'; T = 'Text'; M = 'Mosaic'; C = 'Crop'; N = 'Number' }
$CatalogSpec = @(
    @{ File = 'ToolCatalog.cs'; Surface = 'Tools'; Doc = '绘图工具的悬停提示与帮助行'; Rule = 'tool' }
    @{ File = 'EditorCommands.cs'; Surface = 'Commands'; Doc = '编辑器命令名与分组标题'; Rule = 'command' }
    @{ File = 'GestureActions.cs'; Surface = 'Actions'; Doc = '动作词汇表的短名与它做什么'; Rule = 'action' }
    @{ File = 'GestureConfig.cs'; Surface = 'Triggers'; Doc = '触发方式的后缀：按下、松手、轻点…'; Rule = 'trigger' }
)
function Pascal([string]$s) {
    (($s -split '[.\-_ ]+') | Where-Object { $_ } | ForEach-Object { $_.Substring(0, 1).ToUpper() + $_.Substring(1).ToLower() }) -join ''
}

foreach ($spec in $CatalogSpec) {
    $path = Get-ChildItem (Join-Path $root 'src\FrameCapture') -Recurse -Include $spec.File | Select-Object -First 1
    if (-not $path) { Write-Host ("  找不到 {0}" -f $spec.File) -ForegroundColor Red; exit 1 }
    $text = [System.IO.File]::ReadAllText($path.FullName)
    $lines = $text -split "`r`n", 0
    $hit = 0
    for ($li = 0; $li -lt $lines.Count; $li++) {
        $line = $lines[$li]
        if ($line -notmatch '// lockeys') { continue }
        $lits = @()
        $p = 0
        while ($p -lt $line.Length) {
            while ($p -lt $line.Length -and $line[$p] -match '\s') { $p++ }
            if ($p -ge $line.Length) { break }
            if ($line[$p] -eq '@') { $p++ }
            if ($line[$p] -ne '"') { $p++; continue }
            $st = $p
            $v = Read-Literal $line ([ref]$p)
            if ($null -eq $v) { break }
            if ($v -match '[\u4E00-\u9FFF]') { $lits += ,@($v, $st, $p) }
        }
        if ($lits.Count -eq 0) { continue }
        $names = @()
        switch ($spec.Rule) {
            'tool' {
                $km = [regex]::Match($line, '^\s*new\("(\w)",')
                if ($km.Success) { $w = $ToolWord[$km.Groups[1].Value]; $names = @("$($w)Tip", "$($w)Help") }
                else { $names = @('BrowseOrDraw') }
            }
            'command' {
                if ($line -match 'Groups =') { $names = $lits | ForEach-Object { "Group$(Pascal (Slug-Of $old[$_[0]]))" } }
                else {
                    $idm = [regex]::Match($line, 'EditorCommand\("([^"]+)"')
                    $raw = $idm.Groups[1].Value
                    $idn = Pascal $raw
                    $names = @()
                    foreach ($l in $lits) { $names += $(if ($raw -like 'tool.*') { $null } elseif ($l[0] -in @('绘制工具', '编辑', '导出', '视图', '播放', '时间轴裁剪', '窗口')) { "Group$(Pascal (Slug-Of $old[$l[0]]))" } else { "$($idn)Name" }) }
                }
            }
            'action' {
                $am = [regex]::Match($line, '^\s*new\((\w+),')
                $idn = $am.Groups[1].Value
                $names = @("$($idn)Label", "$($idn)Desc")
            }
            'trigger' {
                $tm = [regex]::Match($line, '"(\w+)"\s*=>')
                $names = @("Trigger$(Pascal $tm.Groups[1].Value)")
            }
        }
        for ($n = $lits.Count - 1; $n -ge 0; $n--) {
            $l = $lits[$n]
            $e = Add-Field $l[0] $spec.Surface $names[$n]
            $line = $line.Remove($l[1], $l[2] - $l[1]).Insert($l[1], ("K.{0}.{1}" -f $e.Owner, $e.Name))
            $hit++
        }
        $lines[$li] = $line -replace '\s*// lockeys\s*$', ''
    }
    [System.IO.File]::WriteAllText($path.FullName, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  {0}  换掉 {1} 处" -f $spec.File, $hit)
}

foreach ($s in $pending.Keys) {
    $doc = ($CatalogSpec | Where-Object { $_.Surface -eq $s }).Doc
    $L = New-Object System.Collections.Generic.List[string]
    $L.Add('namespace FrameCapture.Core;')
    $L.Add('')
    $L.Add('/// <summary>')
    $L.Add(("/// 文案 · {0}。一行一句：稳定 id、中文原文、英文译名。" -f $doc))
    $L.Add('/// id 定下来就不动——改文案只改后面两列，目录与取词点不用跟着改。')
    $L.Add('/// </summary>')
    $L.Add('public static partial class K')
    $L.Add('{')
    $L.Add(("    /// <summary>{0}。</summary>" -f $doc))
    $L.Add(("    public static partial class {0}" -f $s))
    $L.Add('    {')
    foreach ($e in ($pending[$s] | Sort-Object { $_[0] })) {
        $L.Add(('        public static readonly Str {0} = new("{1}", "{2}", "{3}");' -f $e[0], $e[1], (Esc $e[2]), (Esc $e[3])))
    }
    $L.Add('    }')
    $L.Add('}')
    $L.Add('')
    [System.IO.File]::WriteAllText((Join-Path $root ("src\FrameCapture\Core\Strings\{0}.cs" -f $s)), ($L -join "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("  新增 {0}.cs  {1} 条" -f $s, $pending[$s].Count)
}
Write-Host 'I18N-REKEY CATALOGS DONE'
