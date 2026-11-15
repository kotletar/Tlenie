<#
  One-command local regression: pure core checks, then opt-in native player scenarios.
  -Player points to a player already containing the current scripts.
  -AI adds short navigation checks in four districts and for all ten survivors.
  No keyboard messages, mouse input, screenshots or persistent settings changes.
#>
param(
    [string]$Player = '',
    [string]$OutRoot = '',
    [switch]$AI,
    [int]$TimeoutSec = 180
)
$ErrorActionPreference = 'Stop'
$taskProject = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutRoot) { $OutRoot = Join-Path $taskProject 'TestResults\regress' }
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$OutRoot = (Resolve-Path -LiteralPath $OutRoot).Path
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskCoreExe = Join-Path $OutRoot 'CoreChecks.exe'
$taskSources = @('docs\verification\CoreChecks.cs', 'Assets\Scripts\Core\LightLedger.cs', 'Assets\Scripts\Core\GameConfig.cs', 'Assets\Scripts\Core\GameDifficulty.cs', 'Assets\Scripts\Core\RunRecords.cs', 'Assets\Scripts\World\CityMap.cs') | ForEach-Object { Join-Path $taskProject $_ }
& $taskCompiler /nologo /utf8output /codepage:65001 (('/out:' + $taskCoreExe)) @taskSources
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed' }
& $taskCoreExe | Tee-Object -FilePath (Join-Path $OutRoot 'core-checks.txt')
if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
if (-not $Player) { Write-Output 'REGRESSION: OK (core only; pass -Player for native scenarios)'; exit 0 }
$Player = (Resolve-Path -LiteralPath $Player).Path
$taskScenarios = @('gameplay')
if ($AI) { $taskScenarios += 'ai' }
foreach ($taskScenario in $taskScenarios) {
    $taskOut = Join-Path $OutRoot $taskScenario
    New-Item -ItemType Directory -Force -Path $taskOut | Out-Null
    $taskResult = Join-Path $taskOut 'regression-result.txt'
    if (Test-Path -LiteralPath $taskResult) { Remove-Item -LiteralPath $taskResult -Force }
    $taskLog = Join-Path $taskOut 'player.log'
    $taskArgs = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -pg-smoke -pg-smoke-out "' + $taskOut + '" -logFile "' + $taskLog + '"'
    if ($taskScenario -eq 'ai') { $taskArgs += ' -pg-ai' }
    $taskProcess = Start-Process -FilePath $Player -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    $taskTimer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $taskProcess.WaitForExit(1000)) {
        if ($taskTimer.Elapsed.TotalSeconds -gt $TimeoutSec) { $taskProcess.Kill(); throw "Player timeout: $taskScenario" }
    }
    if (-not (Test-Path -LiteralPath $taskResult)) { throw "Missing report: $taskScenario (see $taskLog)" }
    $taskText = [IO.File]::ReadAllText($taskResult)
    Write-Output $taskText
    if ($taskProcess.ExitCode -ne 0 -or $taskText -notmatch 'PG REGRESSION: OK') { throw "Native regression failed: $taskScenario" }
}
Write-Output 'REGRESSION: OK'
