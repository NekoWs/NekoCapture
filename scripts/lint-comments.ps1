# 注释内容与 XML 文档覆盖护栏。
#
# 语义：
#   FAIL —— 违反即护栏失败（硬禁词 / 编号 / 超长 / 注释掉的代码 / #region 其他 / 缺文档 / 超长文件）
#   WARN —— 人工判定（歧义词：旧 / 不再 / 回归 / 修复 / 坑 / bug）
# 只扫描注释文本，字符串字面量不参与检查。
# 豁免：行尾加  // lint-comments:ignore <一句理由>   —— 必须带理由。
#
# 用法: pwsh -NoProfile -File scripts/lint-comments.ps1 [-Paths a,b] [-WarnOnly] [-Quiet]
param(
    [string[]]$Paths = @(),
    [switch]$RequireDocumentation,
    [switch]$WarnOnly,
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Paths.Count -eq 0) { $Paths = @((Join-Path $root "src"), (Join-Path $root "tools"), (Join-Path $root "Program.cs")) }

# ---- 词表（§3.1 / §3.2 / §8 / §6 / §7）----
$HardWords    = @('此前','之前','原来','曾经','曾把','曾让','退役','弃用','改为','修掉','教训','历史','早期','当时','过去')
$AmbigWords   = @('旧','不再','回归','修复','坑','bug')
$NarrativeRx  = '本轮|这轮|这一轮|上一轮|上轮|第\s*\d+\s*轮|改前|改后(?!面)|原先|现已|本批次'
$TagRx        = '\b[RT]\d{1,3}[a-z]?\b|\bR\d{2,3}\s[A-Z]\d\b'
$CommitRx     = '\bcommit\s+[0-9a-f]{7,40}\b'
$RegionMiscRx = '^#region\s*(其他|杂项|其它|Other|Misc|通用|工具方法|私有方法)\s*$'
$DeadCodeRx   = '^[A-Za-z_@][\w\.\[\]<>,\s\(\)\+\-\*/=!&|\?:]*[;{]\s*$'
$DeclRx       = '^\s*(public|internal)\s+(sealed\s+|static\s+|abstract\s+|readonly\s+|partial\s+)*(class|struct|enum|interface|record)\b'
$MaxCommentLen = 110
$MaxFileLines  = 700

$script:viol = [System.Collections.Generic.List[object]]::new()
$script:exemptCount = 0
function Add-V([string]$kind, [string]$sev, [string]$file, [int]$ln, [string]$text) {
    $script:viol.Add([pscustomobject]@{ Kind = $kind; Sev = $sev; File = $file; Line = $ln; Text = $text })
}

# 取出一行中的注释文本（跳过字符串字面量）。$st 是跨行状态包（哈希表按引用传递）。
# $st.Kind: code | block | verbatim | raw | string | char
function Get-CommentText {
    param([string]$line, [hashtable]$st)
    $n = $line.Length
    $i = 0
    while ($i -lt $n) {
        $kind = $st.Kind
        if ($kind -eq 'block') {
            if ($i + 1 -lt $n -and $line.Substring($i, 2) -eq '*/') { $st.Kind = 'code'; $i += 2 } else { $i++ }
            continue
        }
        if ($kind -eq 'verbatim') {
            if ($line[$i] -eq '"') {
                if ($i + 1 -lt $n -and $line[$i + 1] -eq '"') { $i += 2 } else { $st.Kind = 'code'; $i++ }
            } else { $i++ }
            continue
        }
        if ($kind -eq 'raw') {
            if ($i + 3 -le $n -and $line.Substring($i, 3) -eq '"""') { $st.Kind = 'code'; $i += 3 } else { $i++ }
            continue
        }
        if ($kind -eq 'string') {
            if ($line[$i] -eq '\') { $i += 2; continue }
            if ($line[$i] -eq '"') { $st.Kind = 'code' }
            $i++; continue
        }
        if ($kind -eq 'char') {
            if ($line[$i] -eq '\') { $i += 2; continue }
            if ($line[$i] -eq "'") { $st.Kind = 'code' }
            $i++; continue
        }
        # code
        if ($i + 1 -lt $n -and $line.Substring($i, 2) -eq '//') { return $line.Substring($i + 2) }
        if ($i + 1 -lt $n -and $line.Substring($i, 2) -eq '/*') { $st.Kind = 'block'; $i += 2; continue }
        if ($i + 3 -le $n -and $line.Substring($i, 3) -eq '"""') { $st.Kind = 'raw'; $i += 3; continue }
        # 原始字符串四种前缀都要先认：漏了 $""" 会被当成 $" 单行串，状态一路错到文件尾，
        # 后面所有注释与代码的判定全部失真（内嵌 XAML 的 Fluent.* 就踩在这里）。
        if ($line[$i] -eq '@') {
            if ($i + 3 -le $n -and $line.Substring($i + 1, 3) -eq '"""') { $st.Kind = 'raw'; $i += 4; continue }
            if ($i + 1 -lt $n -and $line[$i + 1] -eq '$' -and $i + 4 -le $n -and $line.Substring($i + 2, 3) -eq '"""') { $st.Kind = 'raw'; $i += 5; continue }
            if ($i + 1 -lt $n -and $line[$i + 1] -eq '"') { $st.Kind = 'verbatim'; $i += 2; continue }
            if ($i + 2 -lt $n -and $line[$i + 2] -eq '"') { $st.Kind = 'verbatim'; $i += 3; continue }
            $i++; continue
        }
        if ($line[$i] -eq '$') {
            if ($i + 3 -le $n -and $line.Substring($i + 1, 3) -eq '"""') { $st.Kind = 'raw'; $i += 4; continue }
            if ($i + 1 -lt $n -and $line[$i + 1] -eq '@') {
                if ($i + 4 -le $n -and $line.Substring($i + 2, 3) -eq '"""') { $st.Kind = 'raw'; $i += 5; continue }
                $st.Kind = 'verbatim'; $i += 3; continue
            }
            if ($i + 1 -lt $n -and $line[$i + 1] -eq '"') { $st.Kind = 'string'; $i += 2; continue }
            $i++; continue
        }
        if ($line[$i] -eq '"') { $st.Kind = 'string'; $i++; continue }
        if ($line[$i] -eq "'") { $st.Kind = 'char'; $i++; continue }
        $i++
    }
    return $null
}

# 注释内容规则（§3 / §6 / §7 / §8）
function Invoke-CommentRules {
    param([string]$text, [int]$ln, [string]$rel)
    if ($text -match $NarrativeRx) { Add-V '变更叙述' 'FAIL' $rel $ln $text }
    if ($text -match 'lint-comments:ignore') {
        $script:exemptCount++
        if ($text -notmatch 'lint-comments:ignore\s+\S.{3,}') { Add-V "豁免缺理由（§12）" "FAIL" $rel $ln $text }
        return
    }
    if ($text -eq '' -or $text -match '^[-=#*]+$') { return }

    $body = ($text -replace '^/\s*', '' -replace '<[^>]+>', '').Trim()

    foreach ($w in $HardWords)  { if ($body -match [regex]::Escape($w)) { Add-V "硬禁词「$w」（§3.1）" "FAIL" $rel $ln $text; break } }
    foreach ($w in $AmbigWords) { if ($body -match [regex]::Escape($w)) { Add-V "歧义词「$w」需人工判定（§3.2）" "WARN" $rel $ln $text; break } }
    if ($body -match $TagRx)    { Add-V "需求/测试编号（§8）" "FAIL" $rel $ln $text }
    if ($body -match $CommitRx) { Add-V "commit 短码（§8）" "FAIL" $rel $ln $text }
    if ($text.Length -gt $MaxCommentLen) { Add-V "注释超 $MaxCommentLen 字符（§6）" "FAIL" $rel $ln "$($text.Length) 字符" }

    $trim = $text.TrimStart('/', ' ').Trim()
    if ($trim -match $RegionMiscRx) { Add-V "#region 分类失败名（§7）" "FAIL" $rel $ln $trim }
    if ($body -notmatch '[一-鿿]' -and $body -match $DeadCodeRx) { Add-V "注释掉的代码（§3）" "FAIL" $rel $ln $body }
}

# 解析成员声明：返回 @{ Name; Before; Args; Count }，非方法返回 $null
function Resolve-MethodDecl {
    param([string[]]$lines, [int]$idx)
    if ($lines[$idx] -match '^\s*(?:public|internal)\s+(?:(?:sealed|static|abstract|readonly|partial)\s+)*(?:class|struct|enum|interface|record)\b') { return $null }
    $decl = ''; $k = $idx; $done = $false
    while ($k -lt $lines.Count -and $k -le $idx + 8 -and -not $done) {
        $seg = $lines[$k]
        $cut = -1
        $bi = $seg.IndexOf('{'); $ai = $seg.IndexOf('=>'); $si = $seg.IndexOf(';')
        # 截到 { / => / ; 中最早者：';' 终止很关键，否则字段与事件会吞掉后续行、
        # 把下一个成员的括号当成自己的参数表
        foreach ($cand in @($bi, $ai, $si)) {
            if ($cand -ge 0 -and ($cut -lt 0 -or $cand -lt $cut)) { $cut = $cand }
        }
        if ($cut -ge 0) { $decl += $seg.Substring(0, $cut); $done = $true } else { $decl += ' ' + $seg }
        $k++
    }
    $lastOpen = -1; $lastArgs = $null; $depth = 0; $start = 0
    for ($p = 0; $p -lt $decl.Length; $p++) {
        $ch = $decl[$p]
        if ($ch -eq '(') { if ($depth -eq 0) { $start = $p }; $depth++ }
        elseif ($ch -eq ')') { $depth--; if ($depth -eq 0) { $lastOpen = $start; $lastArgs = $decl.Substring($start + 1, $p - $start - 1) } }
    }
    if ($lastOpen -le 0) { return $null }
    $nameStart = $lastOpen
    while ($nameStart -gt 0 -and $decl[$nameStart - 1] -match '[\w>]') { $nameStart-- }
    $name = $decl.Substring($nameStart, $lastOpen - $nameStart)
    # 方法名必须是裸标识符：元组返回类型 "(double A, double B) Foo" 的括号前是空白 -> 名为空，排除
    if ($name -notmatch '^[A-Za-z_]\w*$' -or $name -eq 'new') { return $null }
    $before = $decl.Substring(0, $nameStart)
    $after = $decl.Substring($lastOpen + $lastArgs.Length + 2)
    # 排除字段/属性初始化器（参数表前出现 '='）与 new(...) 表达式
    if ($before -match '=' -or $after -match '=') { return $null }
    # 排除事件与操作符：event / operator 关键字不是方法
    if ($before -match '\b(event|delegate|operator|when|get|set|init|value)\b') { return $null }
    $n = 0
    $a = $lastArgs.Trim()
    if ($a -ne '') {
        $d = 0; $n = 1
        foreach ($ch in $a.ToCharArray()) {
            if ($ch -in '(', '[', '<') { $d++ }
            elseif ($ch -in ')', ']', '>') { $d-- }
            elseif ($ch -eq ',' -and $d -eq 0) { $n++ }
        }
    }
    return @{ Name = $name; Before = $before; Args = $a; Count = $n }
}

# 嵌套在非公开类型体内的 public 成员对外不可见：CS1591 不报，扫描器也不该报。
# 靠缩进回溯找最近的外层类型声明判定——外层类型没有 public/internal 即视为私有嵌套。
function Test-NestedInPrivateType {
    param([string[]]$lines, [int]$idx)
    $ind = $lines[$idx].Length - $lines[$idx].TrimStart().Length
    for ($k = $idx - 1; $k -ge 0; $k--) {
        $l = $lines[$k]
        if ($l.Trim() -eq '') { continue }
        if (($l.Length - $l.TrimStart().Length) -ge $ind) { continue }
        if ($l -match '^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|readonly\s+|partial\s+)*(class|struct|enum|interface|record)\b') { return $false }
        if ($l -match '^\s*(?:sealed\s+|static\s+|abstract\s+|readonly\s+|partial\s+)*(class|struct|enum|interface|record)\b') { return $true }
        # 非类型行（类体的 '{'、上一个成员的 '}' 等）不终止回溯：最近的外层「类型」声明才决定可见性
    }
    return $false
}

$files = [System.Collections.Generic.List[object]]::new()
foreach ($p in $Paths) {
    if (-not (Test-Path $p)) { continue }
    if ((Get-Item $p).PSIsContainer) {
        foreach ($c in (Get-ChildItem $p -Recurse -File -Include *.cs)) { $files.Add($c) }
    } else { $files.Add((Get-Item $p)) }
}
$files = @($files | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })

foreach ($f in $files) {
    $rel = $f.FullName.Substring($root.Length + 1)
    $inSrc = $RequireDocumentation -or $rel -like 'src\*'
    $lines = @(Get-Content $f.FullName)
    $st = @{ Kind = 'code' }

    if ($inSrc -and $lines.Count -gt $MaxFileLines) {
        Add-V "文件超 $MaxFileLines 行（应拆 partial，§10）" "FAIL" $rel 1 "$($lines.Count) 行"
    }

    for ($idx = 0; $idx -lt $lines.Count; $idx++) {
        $raw = $lines[$idx]
        $ln = $idx + 1

        $cmt = Get-CommentText $raw $st
        if ($null -ne $cmt) { Invoke-CommentRules $cmt.Trim() $ln $rel }

        # #region 标题不是注释，扫描器必须单独看：同样禁编号、禁硬禁词、限长
        if ($raw -match '^\s*#region\s+(?<t>\S.*?)\s*$') {
            $t = $Matches['t']
            if ($t -match $TagRx)    { Add-V "#region 标题含编号（§8）" "FAIL" $rel $ln $t }
            if ($t.Length -gt 48)    { Add-V "#region 标题过长（§7）" "FAIL" $rel $ln "$($t.Length) 字符" }
            if ($t -match '^(其他|杂项|其它|Other|Misc|通用|工具方法|私有方法|行为)\s*$') { Add-V "#region 分类失败名（§7）" "FAIL" $rel $ln $t }
            foreach ($w in $HardWords) { if ($t -match [regex]::Escape($w)) { Add-V "#region 标题含硬禁词「$w」（§3.1）" "FAIL" $rel $ln $t; break } }
        }

        if (-not $inSrc) { continue }
        if ($raw -notmatch '^\s*(public|internal)\b' -or $raw -match '^\s*///') { continue }
        if (Test-NestedInPrivateType $lines $idx) { continue }

        # 向上跳过特性行，定位文档块
        $j = $idx - 1
        while ($j -ge 0 -and $lines[$j].Trim() -match '^\[') { $j-- }
        $doc = @()
        while ($j -ge 0 -and $lines[$j].Trim() -match '^///') { $doc = @($lines[$j]) + $doc; $j-- }

        # 类型缺文档
        if ($raw -match $DeclRx -and $doc.Count -eq 0) {
            $scope = if ($raw -match '^\s*internal') { "internal" } else { "public" }
            Add-V "缺 XML 文档（$scope 类型，§4.4）" "FAIL" $rel $ln $raw.Trim()
        }

        # 成员缺文档 / 缺 param / 缺 returns
        $m = Resolve-MethodDecl $lines $idx
        if ($null -ne $m) {
            if ($doc.Count -eq 0) {
                $scope = if ($raw -match '^\s*internal') { "internal" } else { "public" }
                Add-V "缺 XML 文档（$scope 成员，§4.4）" "FAIL" $rel $ln $raw.Trim()
            } else {
                $docTxt = $doc -join ' '
                $inherited = $docTxt -match '<inheritdoc\b[^>]*/>'
                if (-not $inherited -and $m.Count -gt 0 -and $docTxt -notmatch '<param\s') {
                    Add-V "缺 <param>（§4.4）" "FAIL" $rel $ln $raw.Trim()
                }
                $head = $m.Before -replace '\b(public|internal|static|sealed|override|async|unsafe|partial|readonly|virtual|extern|new)\b', ''
                $retTok = ($head.Trim() -split '\s+') | Where-Object { $_ -ne '' } | Select-Object -Last 1
                $voidRet = [string]::IsNullOrEmpty($retTok) -or $retTok -eq 'void'
                if (-not $inherited -and -not $voidRet -and $docTxt -notmatch '<returns>') {
                    Add-V "缺 <returns>（§4.4）" "FAIL" $rel $ln $raw.Trim()
                }
            }
        }
    }
}

$fails = @($script:viol | Where-Object Sev -eq 'FAIL')
$warns = @($script:viol | Where-Object Sev -eq 'WARN')
if (-not $Quiet) {
    foreach ($g in ($script:viol | Group-Object Kind | Sort-Object { -$_.Count })) {
        Write-Host ("`n== {0}  ({1} 处) ==" -f $g.Name, $g.Count)
        foreach ($v in ($g.Group | Select-Object -First 15)) {
            Write-Host ("  [{0}] {1}:{2}  {3}" -f $v.Sev, $v.File, $v.Line, ($v.Text -replace '\s+', ' '))
        }
        if ($g.Count -gt 15) { Write-Host ("  ...另 {0} 处" -f ($g.Count - 15)) }
    }
    Write-Host ("`n豁免 {0} 处（只增不减即技术债增长）" -f $script:exemptCount)
}
Write-Host ("LINT-COMMENTS {0}  FAIL={1} WARN={2} 文件={3}" -f $(if ($fails.Count -eq 0) { "PASS" } else { "FAIL" }), $fails.Count, $warns.Count, $files.Count)
if (-not $WarnOnly -and $fails.Count -gt 0) { exit 1 } else { exit 0 }
