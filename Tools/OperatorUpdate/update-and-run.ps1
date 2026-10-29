param([Parameter(Mandatory=$true)][string]$Repo,
      [Parameter(Mandatory=$true)][string]$Unity,
      [Parameter(Mandatory=$true)][int]$ParentPid,
      [Parameter(Mandatory=$true)][string]$Request,
      [switch]$Check)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Request | Out-Null
Start-Transcript -Path (Join-Path $Request 'update.log') -Append | Out-Null
function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git failed: $args ($LASTEXITCODE)" }
}
function Test-Preflight {
    if (!(Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Unity Editor missing. Set RACING_UNITY_PATH.' }
    $changes = @(Invoke-Git status --porcelain)
    if ($changes.Count) { throw 'Uncommitted changes. Save and commit them before updating.' }
    $gitDir = (Invoke-Git rev-parse --absolute-git-dir | Out-String).Trim()
    foreach ($marker in @('rebase-merge','rebase-apply','MERGE_HEAD','CHERRY_PICK_HEAD')) {
        if (Test-Path -LiteralPath (Join-Path $gitDir $marker)) { throw 'Git operation in progress.' }
    }
}
$originalPlugin = $null
try {
    Set-Location -LiteralPath $Repo
    Test-Preflight
    Invoke-Git fetch origin main:refs/remotes/origin/main
    Invoke-Git merge-base --is-ancestor main origin/main
    if ($Check) { Write-Output 'OPERATOR_UPDATE_PREFLIGHT_PASS'; exit 0 }
    New-Item -ItemType File -Path (Join-Path $Request 'ready') | Out-Null
    $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
    if ($parent -and !$parent.WaitForExit(120000)) { throw 'Timed out waiting for Unity to exit.' }
    Test-Preflight
    Invoke-Git switch main
    Invoke-Git merge --ff-only origin/main
    $originalPlugin = Join-Path $Request 'original-RaceAudio.dll'
    Copy-Item -LiteralPath (Join-Path $Repo 'Assets\Plugins\RaceAudio\Windows\RaceAudio.dll') -Destination $originalPlugin
    & cmd.exe /c 'Native~\RaceAudio\build-windows.cmd' 
    if ($LASTEXITCODE -ne 0) { throw 'Native audio build failed.' }
    $version = Get-Date -Format 'yyyyMMdd-HHmmss'
    $env:RACING_OPERATOR_BUILD = Join-Path $Repo "Builds\Operator\$version-$PID\Race.exe"
    # Start-Process -Wait is needed because Unity.exe is a Windows GUI application.
    $arguments = @('-batchmode','-quit','-projectPath',('"' + $Repo + '"'),
        '-executeMethod','OperatorPlayerBuild.Build','-logFile',('"' + (Join-Path $Request 'build.log') + '"'))
    $build = Start-Process -FilePath $Unity -ArgumentList $arguments -Wait -PassThru
    if ($build.ExitCode -ne 0 -or !(Test-Path -LiteralPath $env:RACING_OPERATOR_BUILD)) { throw 'Unity build failed. See build.log.' }
    Start-Process -FilePath $env:RACING_OPERATOR_BUILD -WorkingDirectory $Repo
    Write-Output 'OPERATOR_UPDATE_BUILD_RUN_PASS'
    New-Item -ItemType File -Path (Join-Path $Request 'succeeded') | Out-Null
} catch {
    Write-Output $_
    New-Item -ItemType File -Force -Path (Join-Path $Request 'failed') | Out-Null
    exit 1
} finally {
    if ($originalPlugin -and (Test-Path -LiteralPath $originalPlugin)) {
        Copy-Item -LiteralPath $originalPlugin -Destination (Join-Path $Repo 'Assets\Plugins\RaceAudio\Windows\RaceAudio.dll') -Force
    }
    Stop-Transcript | Out-Null
}
