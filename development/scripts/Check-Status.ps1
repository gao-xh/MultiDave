[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
$bepRoot = Join-Path $gameRoot 'BepInEx'
$logPath = Join-Path $bepRoot 'LogOutput.log'
if (!(Test-Path -LiteralPath $logPath)) { $logPath = Join-Path $bepRoot 'LogOutput.txt' }
$logText = if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath -Raw } else { '' }
$assemblyPath = Join-Path $bepRoot 'plugins\DaveCoop\DaveCoop.dll'
$gameProcess = Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue | Select-Object -First 1
$currentProcessLogFresh = $false
if ($gameProcess -and (Test-Path -LiteralPath $logPath)) {
    $currentProcessLogFresh = (Get-Item -LiteralPath $logPath).LastWriteTimeUtc -ge $gameProcess.StartTime.ToUniversalTime()
}
[pscustomobject]@{
    GamePath = $gameRoot
    GameRunning = [bool]$gameProcess
    CurrentProcessLogFresh = $currentProcessLogFresh
    FrameworkInstalled = Test-Path -LiteralPath (Join-Path $gameRoot 'winhttp.dll')
    InteropAssemblies = @(Get-ChildItem -LiteralPath (Join-Path $bepRoot 'interop') -Filter '*.dll' -File -ErrorAction SilentlyContinue).Count
    PluginInstalled = Test-Path -LiteralPath $assemblyPath
    PluginLoadVerified = $currentProcessLogFresh -and $logText -match 'DAVECOOP_BOOTSTRAP_OK:'
    UnityUpdateVerified = $currentProcessLogFresh -and $logText -match 'DAVECOOP_UPDATE_OK:'
    LogPath = $logPath
} | ConvertTo-Json
