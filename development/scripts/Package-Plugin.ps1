[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dependencies = Get-Content -LiteralPath (Join-Path $projectRoot 'config\dependencies.json') -Raw | ConvertFrom-Json
$environment = & (Join-Path $PSScriptRoot 'Get-Environment.ps1') -GamePath $GamePath | ConvertFrom-Json
if ($environment.UnityVersion -ne $dependencies.testedUnityVersion -or $environment.SteamBuildId -ne $dependencies.testedSteamBuildId) {
    throw 'Record validation of this game version in dependencies.json before packaging.'
}
$sourceDll = Join-Path $projectRoot 'artifacts\plugin\DaveCoop.dll'
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the plugin first.' }
$packageRoot = Join-Path $projectRoot ('artifacts\packages\' + [Guid]::NewGuid().ToString('N'))
$dllRelativePath = 'BepInEx/plugins/DaveCoop/DaveCoop.dll'
$destinationDll = Join-Path $packageRoot $dllRelativePath
New-Item -ItemType Directory -Path (Split-Path -Parent $destinationDll) -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination $destinationDll
[pscustomobject]@{
    schemaVersion = 1
    pluginVersion = $dependencies.pluginVersion
    stage = 'bootstrap-prototype'
    testedUnityVersion = $dependencies.testedUnityVersion
    testedSteamBuildId = $dependencies.testedSteamBuildId
    files = @([pscustomobject]@{ path = $dllRelativePath; sha256 = (Get-FileHash -LiteralPath $destinationDll -Algorithm SHA256).Hash })
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
$releaseRoot = Join-Path $projectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$archivePath = Join-Path $releaseRoot ('DaveCoop-' + $dependencies.pluginVersion + '.zip')
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
($hash + '  ' + [IO.Path]::GetFileName($archivePath)) | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ascii
[pscustomobject]@{ PackagePath = $archivePath; SHA256 = $hash; Version = $dependencies.pluginVersion } | ConvertTo-Json
