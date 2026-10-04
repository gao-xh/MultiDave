[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$StagePath = (Join-Path $PSScriptRoot '..\.local\bepinex-788')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Resolve-DaveGamePath $GamePath
$stageRoot = (Resolve-Path -LiteralPath $StagePath).Path
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'DaveTheDiver.exe'))) { throw 'Not a Dave the Diver installation.' }
if ((Get-Item -LiteralPath $gameRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Game directory must not be a link.' }
if (Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue) { throw 'Save and exit the game before installing.' }
Assert-NoPathLinks $gameRoot
$entries = @('BepInEx', 'dotnet', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')
foreach ($entry in $entries) {
    if (!(Test-Path -LiteralPath (Join-Path $stageRoot $entry))) { throw "Missing framework entry: $entry" }
    if (Test-Path -LiteralPath (Join-Path $gameRoot $entry)) { throw "Existing entry would be overwritten: $entry" }
}
$artifactRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$originalFiles = @('DaveTheDiver.exe', 'GameAssembly.dll', 'UnityPlayer.dll') | ForEach-Object {
    Get-FileHash -LiteralPath (Join-Path $gameRoot $_) -Algorithm SHA256 | Select-Object Path, Hash
}
$originalFiles | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot 'original-game-hashes.json') -Encoding utf8
foreach ($entry in $entries) {
    Copy-Item -LiteralPath (Join-Path $stageRoot $entry) -Destination (Join-Path $gameRoot $entry) -Recurse
}
[pscustomobject]@{
    GamePath = $gameRoot
    FrameworkBuild = '6.0.0-be.788+5b766a3'
    InstalledEntries = $entries
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot 'framework-install.json') -Encoding utf8
Write-Output "Installed BepInEx in $gameRoot"
