param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Test-Path './relay.env.ps1') { . './relay.env.ps1' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$relayArgs = @("--auto")
if ($NoBrowser) { $relayArgs += "--no-browser" }
dotnet run --project DashboardRelay.csproj --no-launch-profile -- @relayArgs
exit $LASTEXITCODE
