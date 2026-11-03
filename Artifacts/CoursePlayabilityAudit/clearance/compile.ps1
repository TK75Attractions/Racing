param([string]$UnityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$common = @('/nologo', '/target:library', '/nostdlib+', '/langversion:9.0')
$refs = @(
    Get-ChildItem "$UnityData/NetStandard/ref/2.1.0" -Filter *.dll
    Get-Item "$UnityData/NetStandard/EditorExtensions/System.IO.Ports.dll"
    Get-ChildItem "$UnityData/Managed/UnityEngine" -Filter *.dll
    Get-ChildItem "$repo/Library/ScriptAssemblies" -Filter *.dll |
        Where-Object { $_.Name -notlike 'Assembly-CSharp*' }
    Get-Item "$UnityData/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll"
)
$common += $refs | ForEach-Object { '/reference:"' + $_.FullName + '"' }
[xml]$project = Get-Content "$repo/Assembly-CSharp.csproj"
$defines = @($project.Project.PropertyGroup.DefineConstants | Where-Object { $_ })[0]
$common += '/define:' + $defines
$runtime = @(Get-ChildItem "$repo/Assets" -Recurse -Filter *.cs | Where-Object {
    $_.FullName -notmatch '\\Editor\\|\\Tests\\|\\Managers\\Multiplayer\\Core\\'
} | ForEach-Object { '"' + $_.FullName + '"' })
$rsp = Join-Path $PSScriptRoot 'runtime.rsp'
[IO.File]::WriteAllLines($rsp, ($common + @('/out:"' + $PSScriptRoot + '/Assembly-CSharp.dll"') + $runtime), $utf8)
& "$UnityData/NetCoreRuntime/dotnet.exe" "$UnityData/DotNetSdkRoslyn/csc.dll" "@$rsp" 2>&1 |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'runtime.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$editor = @(Get-ChildItem "$repo/Assets/Editor" -Recurse -Filter *.cs |
    ForEach-Object { '"' + $_.FullName + '"' })
$rsp = Join-Path $PSScriptRoot 'editor.rsp'
[IO.File]::WriteAllLines($rsp, ($common + @('/reference:"' + $PSScriptRoot + '/Assembly-CSharp.dll"',
    '/out:"' + $PSScriptRoot + '/Assembly-CSharp-Editor.dll"') + $editor), $utf8)
& "$UnityData/NetCoreRuntime/dotnet.exe" "$UnityData/DotNetSdkRoslyn/csc.dll" "@$rsp" 2>&1 |
    Tee-Object -FilePath (Join-Path $PSScriptRoot 'editor.log')
exit $LASTEXITCODE
