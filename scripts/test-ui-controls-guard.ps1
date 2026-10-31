# 控件护栏识别显式构造、目标类型构造、别名和业务 XAML；字符串内容不作为 C# 构造。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'test-out/ui-controls-guard'
$good = Join-Path $folder 'allowed'
$bad = Join-Path $folder 'rejected'
New-Item -ItemType Directory -Force -Path $good, $bad | Out-Null
[IO.File]::WriteAllText((Join-Path $good 'Controls.cs'), @'
class Allowed {
    object Button() => Fluent.Ui(new Button { Content = "new TextBox()" });
    object Nested() => Fluent.Ui(new Button { Content = Fluent.Ui(new TextBox()) });
    object Layout() => new Grid();
}
'@)
[IO.File]::WriteAllText((Join-Path $bad 'Controls.cs'), @'
using Native = System.Windows.Controls.ComboBox;
class Rejected {
    TextBox input = new();
    Button Create() => new();
    object Plain() => new System.Windows.Controls.Button();
    object Alias() => new Native();
    object Nested() => Fluent.Ui(new Button { Content = new TextBox() });
    void Component() { HotkeyRow(); ChromelessTemplate(); }
}
'@)
[IO.File]::WriteAllText((Join-Path $bad 'View.xaml'), '<Grid><Button/></Grid>')
$lint = Join-Path $PSScriptRoot 'lint-ui-controls.ps1'
$allowedOutput = & pwsh -NoProfile -File $lint -Paths $good 2>&1 | Out-String
$allowedCode = $LASTEXITCODE
$rejectedOutput = & pwsh -NoProfile -File $lint -Paths $bad 2>&1 | Out-String
$rejectedCode = $LASTEXITCODE
$failed = 0
if ($allowedCode -ne 0 -or $allowedOutput -notmatch 'LINT-CONTROLS PASS FAIL=0') { $failed++; if (-not $Quiet) { Write-Host $allowedOutput } }
if ($rejectedCode -eq 0 -or $rejectedOutput -notmatch 'LINT-CONTROLS FAIL FAIL=8(?:\D|$)') { $failed++; if (-not $Quiet) { Write-Host $rejectedOutput } }
Write-Host ("UI-CONTROL-GUARD {0} FAIL={1} CASES=2" -f $(if ($failed) { 'FAIL' } else { 'PASS' }), $failed)
if ($failed) { exit 1 }
