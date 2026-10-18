$ErrorActionPreference = 'Stop'
python (Join-Path $PSScriptRoot 'monitor.py') @args
exit $LASTEXITCODE
