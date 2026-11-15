<#
.SYNOPSIS
  Runs the permanent editor smoke-test harness (PGSmokeTest) in Unity without a human.

.DESCRIPTION
  1. Uses Assets/Editor/Verification/PGSmokeTest.cs without copying it.
  2. Starts Unity (-batchmode unless -Windowed), waits, prints compile errors, warnings and smoke_result.txt.
  3. Leaves the verification source in place for the next run.
  Exit code: 0 when the report says "SMOKE RESULT: OK" and errors=0, otherwise 1.

.PARAMETER Scenario      Harness scenario: layout, enemies, busfight, finalstand, finalpassive, durable, econ, reson, pizza, follow, views, ... (see README.md).
.PARAMETER Name          Label of this run; results go to <OutRoot>\shots_<Name> and the log to <OutRoot>\unity_<Name>.log.
.PARAMETER Views         Path to a views file for -Scenario views (lines: "name kind args", see README.md).
.PARAMETER Windowed      Do not pass -batchmode (IMGUI screens are only painted in a real editor window: menushot, introshot, thoughts).
.PARAMETER Project       Project folder (default: two levels above this script).
.PARAMETER UnityExe      Unity.exe (default: $env:UNITY_EXE, else the Unity Hub path for the version in ProjectSettings/ProjectVersion.txt).
.PARAMETER LicensingIpc  Optional value for -licensingIpc (some machines need e.g. LicenseClient-<name>; see Editor.log of a normal start).
.PARAMETER OutRoot       Where logs and screenshots go (default: %TEMP%\pg_smoke).
.PARAMETER TimeoutSec    Kill Unity after this many seconds (default 600; the first import of a fresh project is slow).

.EXAMPLE
  powershell -File docs\verification\run_smoke.ps1 -Scenario layout
  powershell -File docs\verification\run_smoke.ps1 -Scenario views -Views my_views.txt -Name shots1
  powershell -File docs\verification\run_smoke.ps1 -Scenario menushot -Windowed
#>
param(
    [string]$Scenario = 'layout',
    [string]$Name = 'run',
    [string]$Views = '',
    [switch]$Windowed,
    [switch]$KeepHarness,
    [string]$Project = '',
    [string]$UnityExe = '',
    [string]$LicensingIpc = '',
    [string]$OutRoot = '',
    [int]$TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Name must contain only letters, digits, underscore or hyphen' }

if (-not $Project) { $Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
$Project = (Resolve-Path -LiteralPath $Project).Path
if (-not $OutRoot) { $OutRoot = Join-Path ([IO.Path]::GetTempPath()) 'pg_smoke' }
if ($Views) { $Views = (Resolve-Path -LiteralPath $Views).Path }

if (-not $UnityExe) { $UnityExe = $env:UNITY_EXE }
if (-not $UnityExe) {
    $verLine = Get-Content -LiteralPath (Join-Path $Project 'ProjectSettings\ProjectVersion.txt') |
        Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    $ver = ($verLine -replace '^m_EditorVersion:\s*', '').Trim()
    $UnityExe = "C:\Program Files\Unity\Hub\Editor\$ver\Editor\Unity.exe"
}
if (-not (Test-Path -LiteralPath $UnityExe)) {
    throw "Unity not found: $UnityExe (pass -UnityExe or set the UNITY_EXE environment variable)"
}

New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$log = Join-Path $OutRoot "unity_$Name.log"
$OutRoot = [IO.Path]::GetFullPath($OutRoot)
$outDir = [IO.Path]::GetFullPath((Join-Path $OutRoot "shots_$Name"))
if (-not $outDir.StartsWith($OutRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Output path escapes OutRoot' }
if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }

$harnessPath = Join-Path $Project 'Assets\Editor\Verification\PGSmokeTest.cs'
if (-not (Test-Path -LiteralPath $harnessPath)) { throw "Permanent harness not found: $harnessPath" }
$taskPreviousOut = $env:PG_SMOKE_OUT
$taskPreviousScenario = $env:PG_SMOKE_SCENARIO
$taskPreviousViews = $env:PG_SMOKE_VIEWS

$exitCode = 1
try {
    $env:PG_SMOKE_OUT = $outDir
    $env:PG_SMOKE_SCENARIO = $Scenario
    $env:PG_SMOKE_VIEWS = $Views

    $argLine = ''
    if (-not $Windowed) { $argLine += '-batchmode ' }
    $argLine += "-projectPath `"$Project`" -executeMethod PokaGoryu.EditorTools.PGSmokeTest.Run -logFile `"$log`""
    if ($LicensingIpc) { $argLine += " -licensingIpc $LicensingIpc" }

    $sw = [Diagnostics.Stopwatch]::StartNew()
    if ($Windowed) { $proc = Start-Process -FilePath $UnityExe -ArgumentList $argLine -PassThru }
    else { $proc = Start-Process -FilePath $UnityExe -ArgumentList $argLine -WindowStyle Hidden -PassThru }
    $done = $proc.WaitForExit($TimeoutSec * 1000)
    if (-not $done) {
        'TIMEOUT: killing Unity'
        try { $proc.Kill() } catch { }
    }
    $sw.Stop()
    'Unity exit: {0}, took {1:N0} s' -f $(if ($done) { $proc.ExitCode } else { 'killed' }), $sw.Elapsed.TotalSeconds

    '--- compile errors / shader errors ---'
    $errs = @()
    if (Test-Path -LiteralPath $log) {
        $errs = @(Select-String -LiteralPath $log -Pattern 'error CS\d+|Shader error|Compilation failed' |
            ForEach-Object { $_.Line.Trim() } | Select-Object -Unique | Select-Object -First 30)
    }
    if ($errs.Count -gt 0) { $errs } else { 'none' }

    '--- warnings (CS) ---'
    $warns = @()
    if (Test-Path -LiteralPath $log) {
        $warns = @(Select-String -LiteralPath $log -Pattern 'warning CS\d+' |
            ForEach-Object { $_.Line.Trim() } | Select-Object -Unique | Select-Object -First 15)
    }
    if ($warns.Count -gt 0) { $warns } else { 'none' }

    '--- result ---'
    $resultFile = Join-Path $outDir 'smoke_result.txt'
    if (Test-Path -LiteralPath $resultFile) {
        $text = Get-Content -LiteralPath $resultFile -Encoding UTF8
        $text
        # success = the report says OK AND no errors were logged (errors=0)
        $m = [regex]::Match(($text -join "`n"), 'errors=(\d+)')
        if (($text -match 'SMOKE RESULT: OK') -and $m.Success -and ([int]$m.Groups[1].Value -eq 0)) { $exitCode = 0 }
    } else {
        'no smoke_result.txt (see the log: ' + $log + ')'
    }

    '--- files ---'
    Get-ChildItem -LiteralPath $outDir -Filter *.png -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
}
finally {
    $env:PG_SMOKE_OUT = $taskPreviousOut
    $env:PG_SMOKE_SCENARIO = $taskPreviousScenario
    $env:PG_SMOKE_VIEWS = $taskPreviousViews
}
exit $exitCode
