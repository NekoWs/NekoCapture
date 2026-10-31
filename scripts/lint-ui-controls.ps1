# 交互控件由 Fluent.Ui 或公共组件工厂创建；布局与模板基元独立。
param([string[]]$Paths = @(), [switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')) | Out-Null
if ($Paths.Count -eq 0) { $Paths = @((Join-Path $root 'src')) }
$files = @($Paths | ForEach-Object {
    $path = if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $root $_ }
    if (Test-Path -LiteralPath $path -PathType Container) { Get-ChildItem -LiteralPath $path -Recurse -File }
    else { Get-Item -LiteralPath $path }
} | Where-Object { $_.Extension -in '.cs', '.xaml' -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName -Unique)
$native = @('Button', 'TextBox', 'PasswordBox', 'ComboBox', 'CheckBox', 'RadioButton', 'Slider', 'ProgressBar',
    'ContextMenu', 'ToggleButton', 'RepeatButton', 'ListBox', 'ListView', 'TreeView', 'DataGrid', 'DatePicker', 'Calendar')
$failures = [Collections.Generic.List[string]]::new()
$count = 0
function Short-Type($type) { if ($null -eq $type) { return '' }; return ($type.ToString() -split '\.')[-1] }
function Report($file, $node, [string]$message) {
    $line = $node.GetLocation().GetLineSpan().StartLinePosition.Line + 1
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    $failures.Add("  FAIL ${relative}:$line $message")
}
function Inferred-Type($node, $variables) {
    $parent = $node.Parent
    if ($parent.GetType().Name -eq 'EqualsValueClauseSyntax') {
        $owner = $parent.Parent
        if ($owner.GetType().Name -eq 'VariableDeclaratorSyntax') { return Short-Type $owner.Parent.Type }
        if ($owner.GetType().Name -eq 'PropertyDeclarationSyntax') { return Short-Type $owner.Type }
    }
    if ($parent.GetType().Name -eq 'AssignmentExpressionSyntax') { return $variables[($parent.Left.ToString() -split '\.')[-1]] }
    if ($parent.GetType().Name -eq 'ReturnStatementSyntax') {
        $method = $parent.Ancestors() | Where-Object { $_.GetType().Name -in 'MethodDeclarationSyntax', 'LocalFunctionStatementSyntax' } | Select-Object -First 1
        return Short-Type $method.ReturnType
    }
    if ($parent.GetType().Name -eq 'ArrowExpressionClauseSyntax') {
        if ($parent.Parent.GetType().Name -eq 'PropertyDeclarationSyntax') { return Short-Type $parent.Parent.Type }
        return Short-Type $parent.Parent.ReturnType
    }
    return ''
}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName).Replace('\', '/')
    if ($relative -match '^src/FrameCapture/Core/Fluent(?:[.][^/]+)?[.]cs$') { continue }
    $source = [IO.File]::ReadAllText($file.FullName)
    if ($file.Extension -eq '.xaml') {
        foreach ($type in $native) {
            if ($source -match "<(?:(?:\w+):)?$type(?:\s|/|>)") { $failures.Add("  FAIL $relative 业务 XAML 不直接创建 $type") }
        }
        continue
    }
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($source)
    $nodes = @($tree.GetRoot().DescendantNodes())
    $variables = @{}
    $aliases = @{}
    foreach ($node in $nodes) {
        if ($node.GetType().Name -eq 'VariableDeclaratorSyntax') { $variables[$node.Identifier.ValueText] = Short-Type $node.Parent.Type }
        elseif ($node.GetType().Name -eq 'PropertyDeclarationSyntax') { $variables[$node.Identifier.ValueText] = Short-Type $node.Type }
        elseif ($node.GetType().Name -eq 'UsingDirectiveSyntax' -and $null -ne $node.Alias) { $aliases[$node.Alias.Name.Identifier.ValueText] = Short-Type $node.Name }
    }
    foreach ($node in $nodes) {
        $kind = $node.GetType().Name
        if ($kind -eq 'InvocationExpressionSyntax' -and $node.Expression.ToString() -match '(?:^|[.])(HotkeyRow|ChromelessTemplate)$') {
            Report $file $node '请使用当前公共组件，不调用胶囊行或独立无边框模板'; continue
        }
        if ($kind -eq 'ObjectCreationExpressionSyntax') { $type = Short-Type $node.Type }
        elseif ($kind -eq 'ImplicitObjectCreationExpressionSyntax') { $type = Inferred-Type $node $variables }
        else { continue }
        if ($null -ne $type -and $aliases.ContainsKey($type)) { $type = $aliases[$type] }
        if ($type -notin $native) { continue }
        $count++
        $parent = $node.Parent
        while ($parent.GetType().Name -eq 'ParenthesizedExpressionSyntax') { $parent = $parent.Parent }
        $approved = $parent.GetType().Name -eq 'ArgumentSyntax' -and $parent.Parent.Parent.GetType().Name -eq 'InvocationExpressionSyntax' `
            -and $parent.Parent.Parent.Expression.ToString() -match '(?:^|[.])Fluent[.]Ui$'
        if (-not $approved) { Report $file $node "$type 必须通过 Fluent.Ui 创建" }
    }
}
if (-not $Quiet) { $failures | ForEach-Object { Write-Host $_ } }
Write-Host ("LINT-CONTROLS {0} FAIL={1} 控件={2} 文件={3}" -f $(if ($failures.Count) { 'FAIL' } else { 'PASS' }), $failures.Count, $count, $files.Count)
if ($failures.Count) { exit 1 }
