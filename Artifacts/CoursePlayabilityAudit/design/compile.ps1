param([string]$UnityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$refs = @(
    Get-ChildItem "$UnityData/NetStandard/ref/2.1.0" -Filter *.dll
    Get-ChildItem "$UnityData/Managed/UnityEngine" -Filter *.dll
    Get-ChildItem "$repo/Library/ScriptAssemblies" -Filter *.dll
    Get-Item "$UnityData/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll"
)
# UnityEditor.dll in Managed duplicates the modular editor references; omit it.
$lines = @('/nologo', '/target:library', '/nostdlib+', '/define:UNITY_EDITOR',
    ('/out:"' + $PSScriptRoot + '/SchoolMapCourseSetup.check.dll"'))
$lines += $refs | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$lines += ('"' + $repo + '/Assets/Editor/SchoolMapCourseSetup.cs"')
$rsp = Join-Path $PSScriptRoot 'compile.rsp'
[IO.File]::WriteAllLines($rsp, $lines)
$log = Join-Path $PSScriptRoot 'compile.log'
[IO.File]::WriteAllText($log, '')
& "$UnityData/NetCoreRuntime/dotnet.exe" "$UnityData/DotNetSdkRoslyn/csc.dll" "@$rsp" 2>&1 |
    Tee-Object -FilePath $log -Append
$compileExit = $LASTEXITCODE
Add-Content $log "csc exit code: $compileExit"
exit $compileExit
