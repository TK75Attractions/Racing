param([Parameter(Mandatory=$true)][string]$Repo,
      [string]$Unity = '', [int]$ParentPid = 0, [string]$Request = '', [switch]$Check)
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path -LiteralPath $Repo).Path
if (!$Request) { $Request = Join-Path $Repo ('Logs\OperatorUpdate\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID) }
New-Item -ItemType Directory -Force -Path $Request | Out-Null
Start-Transcript -Path (Join-Path $Request 'update.log') -Append | Out-Null
$lock = Join-Path $Repo 'Logs\OperatorUpdate.lock'
$locked = $false; $checkoutCreated = $false; $sourceDir = $null
$message = '更新に失敗しました。update.logとbuild.logを確認してください。'
function Set-UpdateStatus([string]$Text) {
    $script:message = $Text
    Set-Content -LiteralPath (Join-Path $Request 'status.txt') -Value $Text -Encoding UTF8
    Write-Output $Text
}
function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git failed: $args ($LASTEXITCODE)" }
}
try {
    Set-Location -LiteralPath $Repo
    if (!$Unity) {
        $Unity = $env:RACING_UNITY_PATH
        if (!$Unity) {
            $version = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Where-Object { $_ -match '^m_EditorVersion: ' }) -replace '^m_EditorVersion: ', '').Trim()
            $Unity = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
        }
    }
    if (!(Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Unity Editorが見つかりません。RACING_UNITY_PATHを指定してください。' }
    # CreateDirectory is atomic through New-Item without -Force; do not share a build/update.
    New-Item -ItemType Directory -Force -Path (Split-Path $lock -Parent) | Out-Null
    New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null
    $locked = $true
    Set-Content -LiteralPath (Join-Path $lock 'owner.pid') -Value $PID
    Set-Content -LiteralPath (Join-Path $lock 'request.txt') -Value $Request -Encoding UTF8
    Set-UpdateStatus '最新mainを取得しています。作業中の変更は保持します。'
    Invoke-Git fetch origin main:refs/remotes/origin/main
    $commit = (Invoke-Git rev-parse --verify refs/remotes/origin/main | Out-String).Trim()
    Set-Content -LiteralPath (Join-Path $Request 'commit.txt') -Value $commit
    $assetBytes = (Get-ChildItem -LiteralPath (Join-Path $Repo 'Assets') -Recurse -File | Measure-Object Length -Sum).Sum
    $requiredBytes = $assetBytes * 3 + 64MB
    $freeBytes = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($Repo)).AvailableFreeSpace
    if ($freeBytes -lt $requiredBytes) { throw ('ビルド用の空き容量が不足しています。目安 {0:N0} MB以上、現在 {1:N0} MB。' -f ($requiredBytes/1MB), ($freeBytes/1MB)) }
    if ($Check) { Set-UpdateStatus 'OPERATOR_UPDATE_PREFLIGHT_PASS'; exit 0 }
    New-Item -ItemType File -Path (Join-Path $Request 'ready') | Out-Null
    if ($ParentPid -gt 0) {
        $parent = Get-Process -Id $ParentPid -ErrorAction SilentlyContinue
        if ($parent -and !$parent.WaitForExit(120000)) { throw 'Unityの終了を待機中にタイムアウトしました。' }
    }
    $sourceDir = Join-Path $Repo ('Builds\OperatorSource\' + (Split-Path $Request -Leaf))
    Set-UpdateStatus '取得したmainを別フォルダーで準備しています。'
    Invoke-Git worktree add --detach $sourceDir $commit
    $checkoutCreated = $true
    Set-Location -LiteralPath $sourceDir
    Set-UpdateStatus '音声プラグインをビルドしています。'
    & cmd.exe /c 'Native~\RaceAudio\build-windows.cmd'
    if ($LASTEXITCODE -ne 0) { throw '音声プラグインのビルドに失敗しました。' }
    $env:RACING_PROJECT_PATH = $Repo
    $env:RACING_OPERATOR_BUILD = Join-Path $Repo ('Builds\Operator\' + (Split-Path $Request -Leaf) + '\Race.exe')
    Set-UpdateStatus 'Unityでビルドしています。Unityを手動で起動する必要はありません。'
    $arguments = @('-batchmode','-quit','-projectPath',('"' + $sourceDir + '"'),
        '-executeMethod','OperatorPlayerBuild.Build','-logFile',('"' + (Join-Path $Request 'build.log') + '"'))
    $build = Start-Process -FilePath $Unity -ArgumentList $arguments -Wait -PassThru
    if ($build.ExitCode -ne 0 -or !(Test-Path -LiteralPath $env:RACING_OPERATOR_BUILD)) { throw 'Unityビルドに失敗しました。build.logを確認してください。' }
    Start-Process -FilePath $env:RACING_OPERATOR_BUILD -WorkingDirectory $Repo
    Set-UpdateStatus '更新したゲームを起動しました。'
    Write-Output 'OPERATOR_UPDATE_BUILD_RUN_PASS'
    New-Item -ItemType File -Path (Join-Path $Request 'succeeded') | Out-Null
} catch {
    Set-UpdateStatus $_.Exception.Message
    New-Item -ItemType File -Force -Path (Join-Path $Request 'failed') | Out-Null
    exit 1
} finally {
    Set-Location -LiteralPath $Repo
    if ($checkoutCreated) { & git worktree remove --force $sourceDir }
    if ($locked) { Remove-Item -LiteralPath $lock -Recurse -Force }
    Stop-Transcript | Out-Null
}
