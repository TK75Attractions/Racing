$ErrorActionPreference = 'Stop'
$relay = $false
try { $info = Invoke-RestMethod 'http://127.0.0.1:8766/api/connection' -TimeoutSec 2; $relay = $info.app -eq 'racing-dashboard' } catch {}
if ($relay) {
    $result = Invoke-RestMethod 'http://127.0.0.1:8766/api/operator-update' -Method Post -TimeoutSec 15 `
        -Headers @{Origin='http://127.0.0.1:8766'} -ContentType 'application/json' -Body ('{"id":"'+[guid]::NewGuid().ToString('N')+'"}')
    Write-Output $result.message
    Start-Process 'http://127.0.0.1:8766/'
    if (!$result.ok) { exit 1 }
    exit 0
}
$game = $false
try { Invoke-RestMethod 'http://127.0.0.1:8765/api/status' -TimeoutSec 2 | Out-Null; $game = $true } catch {}
if ($game) { Write-Output 'ゲームが起動中です。Dashboardを起動し、更新ボタンを使ってください。'; exit 1 }
& "$PSScriptRoot\update-and-run.ps1" -Repo "$PSScriptRoot\..\.."
exit $LASTEXITCODE
