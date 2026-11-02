param([Parameter(Mandatory)] [string]$Upgrader)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$isolated = Join-Path $root ('test-out/config-upgrade/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $isolated | Out-Null
$savedTemp = $env:TEMP; $savedTmp = $env:TMP; $savedSettings = $env:NC_SETTINGS_DIR
function Chk([string]$name, [bool]$condition) { if (-not $condition) { throw "配置契约失败: $name" }; Write-Host "PASS $name" }
try {
    $env:TEMP = $isolated; $env:TMP = $isolated; $env:NC_SETTINGS_DIR = $isolated
    $exe = (Resolve-Path -LiteralPath $Upgrader).Path
    $version = [int]([regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src/NekoCapture/Core/AppSettings.cs')), 'CurrentConfigVersion = (\d+)').Groups[1].Value)
    $legacy = Join-Path $isolated 'legacy.json'
    [IO.File]::WriteAllText($legacy, '{"Fps":47,"AutoCheckUpdates":false,"Colors":{"dark.CardBg":"#112233"}}')
    $before = [IO.File]::ReadAllText($legacy)
    & $exe $legacy *> (Join-Path $isolated 'migration.log')
    Chk 'legacy-migration-exits-successfully' ($LASTEXITCODE -eq 0)
    $settings = Get-Content -LiteralPath $legacy -Raw | ConvertFrom-Json
    Chk 'version-and-user-preferences-preserved' ($settings.ConfigVersion -eq $version -and $settings.Fps -eq 47 -and $settings.AutoCheckUpdates -eq $false -and $settings.Colors.'dark.CardBg' -eq '#112233')
    $backups = @(Get-ChildItem -LiteralPath $isolated -Filter 'legacy.json.before-*.bak')
    Chk 'original-configuration-backed-up' ($backups.Count -eq 1 -and [IO.File]::ReadAllText($backups[0].FullName) -eq $before)
    $triggers = Join-Path $isolated 'triggers.json'
    [IO.File]::WriteAllText($triggers, '{"ConfigVersion":1,"Fps":47,"Gestures":{"Seeded":true,"ChainRevision":3,"GraphRevision":1,"Bindings":[{"Id":"custom","Name":"my capture","Chord":"F6","EntryId":"entry","EntryMode":"record"}],"Graph":{"Flows":[{"Id":"entry","Steps":["selection"]}],"Nodes":[{"Id":"selection","Action":"select","Branches":{"still":[],"record":[],"cancel":[],"failed":[]}}]}}}')
    & $exe $triggers *> (Join-Path $isolated 'triggers.log')
    Chk 'trigger-format-upgrade-exits-successfully' ($LASTEXITCODE -eq 0)
    $updated = Get-Content -LiteralPath $triggers -Raw | ConvertFrom-Json
    $binding = $updated.Gestures.Bindings[0]
    Chk 'entry-mode-becomes-specific-action-override' ($updated.ConfigVersion -eq $version -and $updated.Fps -eq 47 -and $binding.Name -eq 'my capture' -and $binding.Chord -eq 'F6' -and $binding.Overrides.selection.RecordMode -eq $true -and $binding.PSObject.Properties.Name -notcontains 'EntryMode')
    Chk 'tray-triggers-are-added-without-replacing-keys' ($updated.Gestures.Bindings.Count -eq 7 -and @($updated.Gestures.Bindings | Where-Object Source -eq 'tray.menu').Count -eq 4)
    $upgraded = [IO.File]::ReadAllText($triggers)
    & $exe $triggers *> (Join-Path $isolated 'triggers-repeat.log')
    Chk 'current-trigger-format-is-not-rewritten' ($LASTEXITCODE -eq 0 -and [IO.File]::ReadAllText($triggers) -eq $upgraded)
    $first = Join-Path $isolated 'pending.json'; $future = Join-Path $isolated 'future.json'
    [IO.File]::WriteAllText($first, $before)
    [IO.File]::WriteAllText($future, ('{"ConfigVersion":' + ($version + 1) + '}'))
    $futureBefore = [IO.File]::ReadAllText($future)
    & $exe $first $future *> (Join-Path $isolated 'rejected.log')
    Chk 'future-format-rejected-without-partial-migration' ($LASTEXITCODE -ne 0 -and [IO.File]::ReadAllText($first) -eq $before -and [IO.File]::ReadAllText($future) -eq $futureBefore)
} finally { $env:TEMP = $savedTemp; $env:TMP = $savedTmp; $env:NC_SETTINGS_DIR = $savedSettings }

$global:LASTEXITCODE = 0
