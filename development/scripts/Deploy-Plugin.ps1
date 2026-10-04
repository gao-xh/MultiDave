[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Resolve-DaveGamePath $GamePath
Assert-NoPathLinks (Join-Path $gameRoot 'BepInEx\plugins\DaveCoop')
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'DaveTheDiver.exe'))) { throw 'Not a Dave the Diver installation.' }
if (Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue) { throw 'Exit the game before deploying.' }
$sourceDll = Join-Path $projectRoot 'artifacts\plugin\DaveCoop.dll'
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the plugin first.' }
$pluginRoot = Join-Path $gameRoot 'BepInEx\plugins\DaveCoop'
New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
if ((Get-Item -LiteralPath $pluginRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Plugin directory must not be a link.' }
foreach ($name in @('DaveCoop.dll', 'DaveCoop.pdb')) {
    $sourceFile = Join-Path $projectRoot ('artifacts\plugin\' + $name)
    $targetFile = Join-Path $pluginRoot $name
    if (Test-Path -LiteralPath $targetFile) {
        $backupRoot = Join-Path $projectRoot ('artifacts\plugin-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Copy-Item -LiteralPath $targetFile -Destination (Join-Path $backupRoot $name)
    }
    if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force }
}
Write-Output "Deployed plugin to $pluginRoot"
