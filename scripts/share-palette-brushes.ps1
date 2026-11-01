# 把 new SolidColorBrush(Theme.令牌) 换成 Theme.令牌Brush（调色板共享画刷）。
# 共享实例让配色改动直接落到正在显示的元素上；每支新画刷都是一份凝固的快照，改完配色不重建窗口就停在旧色。
# 悬停与按下是叠加层不是表面，不在此列——它们该被 Fluent.HoverOn/PressOn 混进底色，脚本只报告还剩几处。
param([switch]$DryRun)
$map = 'WindowBgSolid','PanelBgSolid','WindowBg','PanelBg','ChromeBg','CardBg','RowHover','RowBg','NavBg','TextDim','Text','StrokeSoft','Stroke','TrackBg','Danger','Success','Warning','Accent'
$rx = [regex]('new SolidColorBrush\(Theme\.(' + ($map -join '|') + ')\)')
$hits = @{}
Get-ChildItem -Path src\NekoCapture -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' -and $_.Name -ne 'Theme.cs' } | ForEach-Object {
    $p = $_.FullName
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $ms = $rx.Matches($text)
    if ($ms.Count -gt 0) {
        "{0,3}  {1}" -f $ms.Count, $_.Name
        foreach ($m in $ms) { $k = $m.Groups[1].Value; $hits[$k] = 1 + [int]$hits[$k] }
        if (-not $DryRun) {
            $new = $rx.Replace($text, 'Theme.${1}Brush')
            $enc = New-Object System.Text.UTF8Encoding($bom)
            [System.IO.File]::WriteAllText($p, $new, $enc)
        }
    }
}
"--- 合计 {0} 处" -f (($hits.Values | Measure-Object -Sum).Sum)
$hits.GetEnumerator() | Sort-Object Value -Descending | ForEach-Object { "  {0,3} {1}" -f $_.Value, $_.Key }
"--- 仍把叠加层当底色（需手工改）:"
Select-String -Path src\NekoCapture\**\*.cs -Pattern 'new SolidColorBrush\(Theme\.(Hover|Press)\)' | ForEach-Object { "  {0}:{1}" -f $_.Filename, $_.LineNumber }
