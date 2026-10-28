param([string]$UnityEditorPath = '')
$ErrorActionPreference = 'Stop'
$buildRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$versionLine = Get-Content -LiteralPath (Join-Path $buildRoot 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1
$buildVersion = ($versionLine -split ':', 2)[1].Trim()
if (!$UnityEditorPath) { $UnityEditorPath = "C:\Program Files\Unity\Hub\Editor\$buildVersion\Editor\Unity.exe" }
if (!(Test-Path -LiteralPath $UnityEditorPath)) { throw "Unity Editor not found: $UnityEditorPath" }
Write-Host 'Close this project in Unity and close Race.exe before building.'
& (Join-Path $buildRoot 'Native~/RaceAudio/build-windows.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Windows audio plugin build failed. See the message above.' }
$buildLog = Join-Path $buildRoot 'Logs/WindowsBuild.log'
New-Item -ItemType Directory -Force -Path (Split-Path $buildLog) | Out-Null
# Start-Process needs explicit quotes for paths containing spaces on Windows.
$buildArguments = @('-batchmode', '-quit', '-buildTarget', 'StandaloneWindows64',
    '-projectPath', ('"' + $buildRoot + '"'), '-executeMethod', 'WindowsPlayerBuild.Build',
    '-logFile', ('"' + $buildLog + '"'))
$buildProcess = Start-Process -FilePath $UnityEditorPath -ArgumentList $buildArguments -Wait -PassThru
if ($buildProcess.ExitCode -ne 0) { throw "Windows build failed. Read $buildLog" }
Write-Host "Build complete: $buildRoot\Builds\Windows\Race.exe"
