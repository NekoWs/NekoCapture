# 文案护栏：用户可见文案里的括号补充说明（括注）。
# 语义：标签只说名字，补充信息并进句子或交给界面上别处——键位交给键帽，选项就是那一行的按钮。
# 括号只留在代码符号与记法里（rgb(r, g, b)、*.ogg;*.wav 过滤器、Program Files (x86)）。
# 用法: pwsh -NoProfile -File scripts/lint-copy-parens.ps1 [-Quiet]
# 逐行豁免：行尾加 // copyok（日志串、坐标读数等确属记法的场景）。
param([switch]$Quiet)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 记法白名单：括号内容是这些，或纯数字/坐标插值，不算括注
$allowExact = @('x86', '*.ogg;*.wav', '*.*', 'r, g, b', 'h, s%, l%', 'NC_TEST_ENCODE_FAIL=1')
$reKeyHint = [regex]'^(?:Ctrl|Shift|Alt|Win|Esc|Enter|Space|Tab|Del|Delete|Back|Ins|F\d|←|→|↑|↓|[A-Z0-9])(?:\s*[+/]\s*(?:Ctrl|Shift|Alt|Win|Esc|Enter|Space|Tab|Del|F\d|←|→|↑|↓|[A-Z0-9]))*$'
$reCjk = [regex]'[\u4e00-\u9fff]'
$reParen = [regex]'[（(]([^（()）]{1,60})[）)]'
$reLit = [regex]'"(?:\\.|[^"\\])*"'

function Test-Violation([string]$content) {
    if ($allowExact -contains $content) { return $false }
    $stripped = [regex]::Replace($content, '\{[^}]*\}', '')
    if ($stripped -match '^[\d\s,\.xX%:*/=+-]*$') { return $false }   # 纯数值/坐标读数
    if ($reKeyHint.IsMatch($content.Trim())) { return $true }          # (Ctrl+S) (V) (Shift+←)
    return $reCjk.IsMatch($content)                                    # 任何带中文的括注
}

$findings = [System.Collections.Generic.List[string]]::new()
$files = 0

foreach ($f in Get-ChildItem (Join-Path $root "src") -Recurse -Include *.cs, *.xaml -File) {
    $files++
    $rel = $f.FullName.Substring($root.Length + 1)
    $lineNo = 0
    foreach ($line in Get-Content $f.FullName -Encoding UTF8) {
        $lineNo++
        if ($line -match '^\s*(///|//|\*)') { continue }               # 注释与文档不参与
        if ($line -match 'Trace\.(Log|Fail)|Debug\.Write|Console\.Write') { continue }
        if ($line -match '//\s*copyok') { continue }
        foreach ($lit in $reLit.Matches($line)) {
            foreach ($m in $reParen.Matches($lit.Value)) {
                if (Test-Violation $m.Groups[1].Value) {
                    $findings.Add("$rel`:$($lineNo)  $($m.Value)  ← 并进句子或删掉")
                }
            }
        }
    }
}

foreach ($f in Get-ChildItem (Join-Path $root "site") -Recurse -Include *.html -File) {
    $files++
    $rel = $f.FullName.Substring($root.Length + 1)
    $html = Get-Content $f.FullName -Raw -Encoding UTF8
    $html = [regex]::Replace($html, '(?is)<script.*?</script>', ' ')
    $html = [regex]::Replace($html, '(?is)<style.*?</style>', ' ')
    $lineNo = 0
    foreach ($line in ($html -split "`r?`n")) {
        $lineNo++
        $text = [regex]::Replace($line, '<[^>]*>', ' ')
        if ($text -match '<!--') { continue }
        foreach ($m in $reParen.Matches($text)) {
            if (Test-Violation $m.Groups[1].Value) {
                $findings.Add("$rel`:$lineNo  $($m.Value)  ← 并进句子或删掉")
            }
        }
    }
}

$fail = $findings.Count
if ($fail -eq 0) { Write-Host ("LINT-COPY PASS FAIL=0 文件=$files") }
else {
    Write-Host ("LINT-COPY FAIL FAIL=$fail")
    if (-not $Quiet) { $findings | ForEach-Object { Write-Host "  $_" } }
}
exit $(if ($fail -eq 0) { 0 } else { 1 })
