[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$PackagePath,
    [switch]$LaunchGame,
    [switch]$InspectOnly,
    [switch]$UseLatestRelease
)
$ErrorActionPreference = 'Stop'
$developmentRoot = Join-Path $PSScriptRoot 'development'
$dependencies = Get-Content -LiteralPath (Join-Path $developmentRoot 'config\dependencies.json') -Raw | ConvertFrom-Json
if (!$PackagePath -and !$UseLatestRelease -and !$InspectOnly) {
    $PackagePath = Join-Path $PSScriptRoot ('distribution\DaveCoop-' + $dependencies.pluginVersion + '.zip')
}
if ($PackagePath -and !$InspectOnly) {
    $checksumPath = $PackagePath + '.sha256'
    if (!(Test-Path -LiteralPath $PackagePath) -or !(Test-Path -LiteralPath $checksumPath)) { throw 'The repository package is missing. Use -UseLatestRelease or supply -PackagePath.' }
    $hashMatch = [regex]::Match((Get-Content -LiteralPath $checksumPath -Raw), '^\s*([A-Fa-f0-9]{64})\s+')
    if (!$hashMatch.Success -or (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash -ne $hashMatch.Groups[1].Value) { throw 'Repository package checksum mismatch.' }
}
& (Join-Path $developmentRoot 'scripts\Install-Mod.ps1') -GamePath $GamePath -RepositoryUrl $dependencies.repositoryUrl -PackagePath $PackagePath -LaunchGame:$LaunchGame -InspectOnly:$InspectOnly
