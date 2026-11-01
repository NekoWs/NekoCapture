# 把新文案按字段名字典序插进 Core\Strings 的译表：表本身按字段名排序，追加在尾部会打断这个次序。
param(
    [string]$Surface = 'Settings',
    [string[]]$Decl = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$file = Join-Path $root "src\NekoCapture\Core\Strings\$Surface.cs"
$lines = [System.Collections.Generic.List[string]](Get-Content $file -Encoding UTF8)
$reStart = [regex]'^\s*public static readonly Str (\w+) = new\('

foreach ($d in $Decl) {
    $name = [regex]::Match($d, 'public static readonly Str (\w+)').Groups[1].Value
    if (-not $name) { throw "不是文案声明：$d" }
    if ($lines -match [regex]::Escape("Str $name = new(")) { Write-Host "skip $name"; continue }
    $at = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = $reStart.Match($lines[$i])
        if (-not $m.Success) { continue }
        if ([string]::CompareOrdinal($m.Groups[1].Value, $name) -gt 0) { $at = $i; break }
    }
    if ($at -lt 0) { throw "$Surface.cs 里没找到 $name 的插入点（最后一个字段也排在它前面？）" }
    $lines.Insert($at, "        $d")
    Write-Host "insert $name @$($at + 1)"
}
Set-Content $file $lines -Encoding UTF8
