param(
    [string]$UnityEditorPath = ""
)

$ErrorActionPreference = "Stop"
$validationProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectVersionPath = Join-Path $validationProjectRoot "ProjectSettings\ProjectVersion.txt"
$versionLine = Get-Content -Encoding UTF8 $projectVersionPath | Select-Object -First 1
$unityVersion = ($versionLine -split ":", 2)[1].Trim()

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\$unityVersion\Editor\Unity.exe"
}

if (-not (Test-Path -LiteralPath $UnityEditorPath)) {
    throw "Unity Editor was not found: $UnityEditorPath"
}

$validationOutput = Join-Path $validationProjectRoot "Logs\MultiplayerValidation"
New-Item -ItemType Directory -Force -Path $validationOutput | Out-Null

function Invoke-UnityValidationStep {
    param(
        [string]$Name,
        [string[]]$Arguments
    )

    Write-Host "[$Name] Starting..."
    $process = Start-Process `
        -FilePath $UnityEditorPath `
        -ArgumentList $Arguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden

    if ($process.ExitCode -ne 0) {
        throw "[$Name] Unity exited with code $($process.ExitCode)."
    }

    Write-Host "[$Name] Passed."
}

$compileLog = Join-Path $validationOutput "compile.log"
$editModeLog = Join-Path $validationOutput "editmode.log"
$editModeResults = Join-Path $validationOutput "editmode-results.xml"
$playModeLog = Join-Path $validationOutput "playmode.log"

Invoke-UnityValidationStep -Name "Compile" -Arguments @(
    "-quit",
    "-batchmode",
    "-projectPath", $validationProjectRoot,
    "-logFile", $compileLog
)

Invoke-UnityValidationStep -Name "EditMode tests" -Arguments @(
    "-batchmode",
    "-projectPath", $validationProjectRoot,
    "-runTests",
    "-testPlatform", "EditMode",
    "-testResults", $editModeResults,
    "-logFile", $editModeLog
)

if (-not (Test-Path -LiteralPath $editModeResults)) {
    throw "EditMode result XML was not generated."
}

[xml]$testResult = Get-Content -Raw -Encoding UTF8 $editModeResults
$testRun = $testResult."test-run"
if ($testRun.result -ne "Passed" -or [int]$testRun.failed -ne 0 -or [int]$testRun.total -le 0) {
    throw "EditMode tests failed: total=$($testRun.total), passed=$($testRun.passed), failed=$($testRun.failed)"
}

Invoke-UnityValidationStep -Name "PlayMode multiplayer smoke" -Arguments @(
    "-batchmode",
    "-nographics",
    "-projectPath", $validationProjectRoot,
    "-executeMethod", "MultiplayerPlayModeValidation.RunBatch",
    "-logFile", $playModeLog
)

if (-not (Select-String -LiteralPath $playModeLog -SimpleMatch "MULTIPLAYER_PLAYMODE_VALIDATION_PASS" -Quiet)) {
    throw "PlayMode validation did not emit its success marker."
}

Write-Host "Multiplayer validation passed: $($testRun.passed)/$($testRun.total) EditMode tests and PlayMode smoke test."
Write-Host "Logs: $validationOutput"
