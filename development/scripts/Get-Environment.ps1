[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$gameRoot = Resolve-DaveGamePath $GamePath
$gameVersion = (Get-Item -LiteralPath (Join-Path $gameRoot 'DaveTheDiver.exe')).VersionInfo.ProductVersion
$manifestPath = Join-Path (Split-Path -Parent (Split-Path -Parent $gameRoot)) 'appmanifest_1868140.acf'
$steamBuild = if (Test-Path -LiteralPath $manifestPath) { [regex]::Match((Get-Content -LiteralPath $manifestPath -Raw), '"buildid"\s+"(\d+)"').Groups[1].Value } else { '' }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
[pscustomobject]@{
    GamePath = $gameRoot
    UnityVersion = [regex]::Match($gameVersion, '(?:20\d{2}|6000)\.\d+\.\d+[abfp]\d+').Value
    SteamBuildId = $steamBuild
    Is64BitOS = [Environment]::Is64BitOperatingSystem
    GameRunning = [bool](Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue)
    DotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
    FrameworkInstalled = Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll')
    InteropReady = Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\interop\Assembly-CSharp.dll')
} | ConvertTo-Json
