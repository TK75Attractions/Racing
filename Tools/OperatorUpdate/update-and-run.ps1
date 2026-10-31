param([Parameter(Mandatory=$true)][string]$Repo,
      [string]$Unity = '', [int]$ParentPid = 0, [string]$Request = '', [switch]$Check)
$ErrorActionPreference = 'Stop'
$Repo = (Resolve-Path -LiteralPath $Repo).Path
if (!$Request) { $Request = Join-Path $Repo ('Logs\OperatorUpdate\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID) }
New-Item -ItemType Directory -Force -Path $Request | Out-Null
Start-Transcript -Path (Join-Path $Request 'update.log') -Append | Out-Null
$lock = Join-Path $Repo 'Logs\OperatorUpdate.lock'
$locked = $false; $checkoutCreated = $false; $sourceDir = $null; $child = $null; $cancelled = $false
$message = '更新に失敗しました。update.logとbuild.logを確認してください。'
function Set-UpdateStatus([string]$Text, [string]$Phase = '') {
    $script:message = $Text
    Set-Content -LiteralPath (Join-Path $Request 'status.txt') -Value $Text -Encoding UTF8
    if ($Phase) { Set-Content -LiteralPath (Join-Path $Request 'phase.txt') -Value $Phase -Encoding UTF8 }
    Write-Output $Text
}
function Check-Cancel {
    $decision = Join-Path $Request 'decision'
    if ((Test-Path -LiteralPath $decision) -and ([string](Get-Content -LiteralPath $decision -Raw)).Trim() -ne 'launch') {
        $script:cancelled = $true
        throw [OperationCanceledException]::new('更新をキャンセルしました。既存のRaceはそのまま使用できます。')
    }
}
function Stop-OwnedProcess([int]$ProcessId) {
    foreach ($item in @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$ProcessId" -ErrorAction SilentlyContinue)) {
        Stop-OwnedProcess $item.ProcessId
    }
    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}
function Run-Command([string]$Executable, [string[]]$Arguments) {
    Check-Cancel
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '[\s"]' -or $_ -eq '') { '"' + $_.Replace('"','\"') + '"' } else { $_ } })
    $script:child = Start-Process -FilePath $Executable -ArgumentList $quoted -NoNewWindow -PassThru
    try {
        while (!$child.HasExited) { Check-Cancel; Start-Sleep -Milliseconds 200; $child.Refresh() }
        $child.WaitForExit()
        if ($child.ExitCode -ne 0) { throw "$Executable failed ($($child.ExitCode)). update.log と build.log を確認してください。" }
        Check-Cancel
    } finally {
        if ($child -and !$child.HasExited) { Stop-OwnedProcess $child.Id; $child.WaitForExit() }
        if ($child) { $child.Dispose(); $script:child = $null }
    }
}
function Invoke-Git {
    & git @args
    if ($LASTEXITCODE -ne 0) { throw "git failed: $args ($LASTEXITCODE)" }
}
try {
    Set-Content -LiteralPath (Join-Path $Request 'control-version') -Value '2' -Encoding UTF8
    Set-Location -LiteralPath $Repo
    if (!$Unity) {
        $Unity = $env:RACING_UNITY_PATH
        if (!$Unity) {
            $version = ((Get-Content 'ProjectSettings\ProjectVersion.txt' | Where-Object { $_ -match '^m_EditorVersion: ' }) -replace '^m_EditorVersion: ', '').Trim()
            $Unity = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
        }
    }
    if (!(Test-Path -LiteralPath $Unity -PathType Leaf)) { throw 'Unity Editorが見つかりません。RACING_UNITY_PATHを指定してください。' }
    New-Item -ItemType Directory -Force -Path (Split-Path $lock -Parent) | Out-Null
    New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null
    $locked = $true
    Set-Content -LiteralPath (Join-Path $lock 'owner.pid') -Value $PID
    Set-Content -LiteralPath (Join-Path $lock 'request.txt') -Value $Request -Encoding UTF8
    Set-UpdateStatus '最新mainを取得しています。既存のRaceはビルド完了まで使用できます。' 'fetching'
    Run-Command 'git' @('fetch','origin','main:refs/remotes/origin/main')
    $commit = (Invoke-Git rev-parse --verify refs/remotes/origin/main | Out-String).Trim()
    Set-Content -LiteralPath (Join-Path $Request 'commit.txt') -Value $commit
    $assetBytes = (Get-ChildItem -LiteralPath (Join-Path $Repo 'Assets') -Recurse -File | Measure-Object Length -Sum).Sum
    $requiredBytes = $assetBytes * 3 + 64MB
    $freeBytes = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($Repo)).AvailableFreeSpace
    if ($freeBytes -lt $requiredBytes) { throw ('ビルド用の空き容量が不足しています。目安 {0:N0} MB以上、現在 {1:N0} MB。' -f ($requiredBytes/1MB), ($freeBytes/1MB)) }
    if ($Check) { Set-UpdateStatus 'OPERATOR_UPDATE_PREFLIGHT_PASS'; exit 0 }
    $sourceDir = Join-Path $Repo ('Builds\OperatorSource\' + (Split-Path $Request -Leaf))
    if (Test-Path -LiteralPath $sourceDir) { throw '更新用フォルダーが既に存在します。別の要求で再実行してください。' }
    Set-UpdateStatus '取得したmainを別フォルダーで準備しています。' 'preparing'
    $checkoutCreated = $true
    Run-Command 'git' @('worktree','add','--detach',$sourceDir,$commit)
    Set-Location -LiteralPath $sourceDir
    Set-UpdateStatus '音声プラグインをビルドしています。' 'native'
    Run-Command 'cmd.exe' @('/c','Native~\RaceAudio\build-windows.cmd')
    $env:RACING_PROJECT_PATH = $Repo
    $env:RACING_OPERATOR_BUILD = Join-Path $Repo ('Builds\Operator\' + (Split-Path $Request -Leaf) + '\Race.exe')
    Set-UpdateStatus 'Unityでビルドしています。完了まで既存のRaceは終了しません。' 'building'
    Run-Command $Unity @('-batchmode','-quit','-projectPath',$sourceDir,'-executeMethod','OperatorPlayerBuild.Build','-logFile',(Join-Path $Request 'build.log'))
    if (!(Test-Path -LiteralPath $env:RACING_OPERATOR_BUILD -PathType Leaf)) { throw 'Unityビルドの出力がありません。build.logを確認してください。' }
    Check-Cancel
    if (Test-Path -LiteralPath (Join-Path $Request 'handoff.wait')) {
        Set-UpdateStatus 'ビルドが完了しました。現在の試合終了を待っています。待機中もキャンセルできます。' 'waiting-race'
        New-Item -ItemType File -Path (Join-Path $Request 'built') | Out-Null
        while (!(Test-Path -LiteralPath (Join-Path $Request 'launch.allowed'))) {
            Check-Cancel
            if (Test-Path -LiteralPath (Join-Path $Request 'handoff.failed')) { throw (Get-Content -LiteralPath (Join-Path $Request 'handoff.failed') -Raw) }
            Start-Sleep -Milliseconds 100
        }
    }
    Check-Cancel
    try {
        $decision = [IO.File]::Open((Join-Path $Request 'decision'),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        $writer = [IO.StreamWriter]::new($decision)
        try { $writer.WriteLine('launch') } finally { $writer.Dispose() }
    } catch [IO.IOException] { Check-Cancel; throw '起動への切り替えを確定できません。' }
    Set-UpdateStatus 'ビルドが完了しました。既存のRaceを終了して新しいアプリへ切り替えます。' 'switching'
    New-Item -ItemType File -Path (Join-Path $Request 'ready') | Out-Null
    $handoff = Test-Path -LiteralPath (Join-Path $Request 'handoff.wait')
    if ($handoff) {
        for ($i=0; $i -lt 300; $i++) {
            if (Test-Path -LiteralPath (Join-Path $Request 'handoff.failed')) { throw (Get-Content -LiteralPath (Join-Path $Request 'handoff.failed') -Raw) }
            if (Test-Path -LiteralPath (Join-Path $Request 'released')) { break }
            Start-Sleep -Milliseconds 100
        }
        if (!(Test-Path -LiteralPath (Join-Path $Request 'released'))) { throw '起動の引き継ぎを確認できません。管理画面の常駐プログラムを確認してください。' }
    }
    $sessionId = (Get-Process -Id $PID).SessionId
    foreach ($player in @(Get-Process -Name Race -ErrorAction SilentlyContinue)) {
        if ($player.SessionId -ne $sessionId -or !$player.Path -or [IO.Path]::GetFileName($player.Path) -ne 'Race.exe') { continue }
        # Never close Unity.exe or an Editor with unsaved scenes.
        $null = $player.CloseMainWindow()
        if (!$player.WaitForExit(10000)) { $player.Kill(); $player.WaitForExit() }
    }
    if ($handoff) {
        $running = $false
        for ($i=0; $i -lt 100; $i++) {
            try { $null = Invoke-WebRequest 'http://127.0.0.1:8765/api/status' -UseBasicParsing -TimeoutSec 1; $running = $true }
            catch { $running = $false; break }
            Start-Sleep -Milliseconds 100
        }
        if ($running) { throw 'UnityのPlayが機器を使用しています。EditorのPlayを停止してから再実行してください。ビルド済みアプリは保存されています。' }
    }
    Set-UpdateStatus 'ビルド済みのRaceを起動しています。' 'launching'
    Start-Process -FilePath $env:RACING_OPERATOR_BUILD -WorkingDirectory $Repo
    Set-UpdateStatus '更新したゲームを起動しました。' 'completed'
    Write-Output 'OPERATOR_UPDATE_BUILD_RUN_PASS'
    New-Item -ItemType File -Path (Join-Path $Request 'succeeded') | Out-Null
} catch {
    if ($cancelled) {
        Set-UpdateStatus '更新をキャンセルしました。既存のRaceはそのまま使用できます。' 'cancelled'
        New-Item -ItemType File -Force -Path (Join-Path $Request 'cancelled') | Out-Null
        exit 130
    }
    Set-UpdateStatus $_.Exception.Message 'failed'
    New-Item -ItemType File -Force -Path (Join-Path $Request 'failed') | Out-Null
    exit 1
} finally {
    if ($child -and !$child.HasExited) { Stop-OwnedProcess $child.Id }
    Set-Location -LiteralPath $Repo
    if ($checkoutCreated) {
        try { & git worktree remove --force $sourceDir } catch { Write-Warning $_.Exception.Message }
        # Only this request's newly created checkout can be removed here.
        for ($attempt=0; $attempt -lt 3 -and (Test-Path -LiteralPath $sourceDir); $attempt++) {
            try { Remove-Item -LiteralPath $sourceDir -Recurse -Force } catch { Start-Sleep -Seconds 1 }
        }
        if (Test-Path -LiteralPath $sourceDir) { Write-Warning "一時フォルダーが残っています: $sourceDir" }
        else { try { & git worktree remove --force $sourceDir 2>$null } catch {} }
    }
    if ($locked) { Remove-Item -LiteralPath $lock -Recurse -Force }
    Stop-Transcript | Out-Null
}
exit 0
