[CmdletBinding()]
param([string]$GamePath, [string]$LogPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
if (!$LogPath) {
    $latest = Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx\plugins\DaveCoop\logs') -Filter 'discovery-*.jsonl' -File |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (!$latest) { throw 'No discovery log. Launch the development plugin with EnablePlayerProbe=true first.' }
    $LogPath = $latest.FullName
}
$stream = [IO.File]::Open($LogPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
$reader = [IO.StreamReader]::new($stream)
$players = @{}
$errorsSeen = [Collections.Generic.HashSet[string]]::new()
$timeline = [Collections.Generic.List[object]]::new()
$snapshotCount = 0
$parseFailures = 0
$incompleteTail = $false
$lastScene = $null
$lastUtc = $null
try {
    $session = $reader.ReadLine() | ConvertFrom-Json
    if ($session.Kind -ne 'session' -or $session.SchemaVersion -ne 1 -or !$session.ReadOnly) {
        throw 'Not a schema 1 read-only discovery session.'
    }
    while (!$reader.EndOfStream) {
        $line = $reader.ReadLine()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $snapshot = $line | ConvertFrom-Json } catch {
            if ($reader.EndOfStream) { $incompleteTail = $true } else { $parseFailures++ }
            continue
        }
        if ($snapshot.Kind -ne 'snapshot') { $parseFailures++; continue }
        $snapshotCount++
        $lastUtc = $snapshot.Utc
        $sceneKey = $snapshot.ActiveScene.Name + '#' + $snapshot.ActiveScene.Handle
        if ($sceneKey -ne $lastScene) {
            $timeline.Add([pscustomobject]@{ Utc = $snapshot.Utc; Scene = $snapshot.ActiveScene.Name; Handle = $snapshot.ActiveScene.Handle })
            $lastScene = $sceneKey
        }
        foreach ($probeError in $snapshot.Errors) { $errorsSeen.Add($probeError) | Out-Null }
        foreach ($player in $snapshot.Players) {
            $id = [string]$player.Object.Id
            if (!$players.ContainsKey($id)) {
                $players[$id] = [pscustomobject]@{
                    Id = $player.Object.Id
                    GameObjectId = $player.Object.GameObjectId
                    Name = $player.Object.Name
                    Path = $player.Object.Path
                    FirstSeenUtc = $snapshot.Utc
                    LastSeenUtc = $snapshot.Utc
                    Scenes = [Collections.Generic.HashSet[string]]::new()
                    Samples = 0
                    ManagerBoundSamples = 0
                    PositionSamples = 0
                    LookSamples = 0
                    NonzeroInputSamples = 0
                    AnimatorSamples = 0
                    CameraFollowSamples = 0
                    LastPosition = $null
                    LastLook = $null
                }
            }
            $observed = $players[$id]
            $observed.LastSeenUtc = $snapshot.Utc
            $observed.Scenes.Add($player.Object.Scene.Name) | Out-Null
            $observed.Samples++
            if ($player.IsManagerPlayer) { $observed.ManagerBoundSamples++ }
            if ($player.Position.Count -eq 3) { $observed.PositionSamples++; $observed.LastPosition = $player.Position }
            if ($player.Look.Count -eq 2) { $observed.LookSamples++; $observed.LastLook = $player.Look }
            if ($player.MoveInput.Count -eq 2 -and ([Math]::Abs($player.MoveInput[0]) + [Math]::Abs($player.MoveInput[1])) -gt 0.01) {
                $observed.NonzeroInputSamples++
            }
            if ($null -ne $player.Animator) { $observed.AnimatorSamples++ }
            if (@($snapshot.Cameras | Where-Object { $null -ne $_.TargetPlayerId -and $_.TargetPlayerId -eq $player.Object.Id }).Count -gt 0) {
                $observed.CameraFollowSamples++
            }
        }
    }
} finally {
    $reader.Dispose()
}
$process = Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue | Select-Object -First 1
$currentProcessSession = $false
if ($process) {
    # PowerShell 7.5+ parses ISO JSON dates as DateTime. Re-parsing its localized
    # string would discard Kind=Utc and misidentify an older game session.
    $sessionStart = if ($session.StartedUtc -is [DateTime]) {
        $session.StartedUtc.ToUniversalTime()
    } else { [DateTimeOffset]::Parse($session.StartedUtc).UtcDateTime }
    $currentProcessSession = $sessionStart -ge $process.StartTime.ToUniversalTime()
}
$playerReports = @($players.Values | Sort-Object Id | ForEach-Object {
    [pscustomobject]@{
        Id = $_.Id; GameObjectId = $_.GameObjectId; Name = $_.Name; Path = $_.Path
        Scenes = @($_.Scenes | Sort-Object)
        FirstSeenUtc = $_.FirstSeenUtc; LastSeenUtc = $_.LastSeenUtc; Samples = $_.Samples
        ManagerBoundSamples = $_.ManagerBoundSamples; PositionSamples = $_.PositionSamples
        LookSamples = $_.LookSamples; NonzeroInputSamples = $_.NonzeroInputSamples
        AnimatorSamples = $_.AnimatorSamples; CameraFollowSamples = $_.CameraFollowSamples
        LastPosition = $_.LastPosition; LastLook = $_.LastLook
    }
})
$report = [pscustomobject]@{
    SchemaVersion = 1
    SourceLog = [IO.Path]::GetFileName($LogPath)
    PluginVersion = $session.PluginVersion
    StartedUtc = $session.StartedUtc
    LastSnapshotUtc = $lastUtc
    CurrentProcessSession = $currentProcessSession
    SnapshotCount = $snapshotCount
    ParseFailures = $parseFailures
    IncompleteTail = $incompleteTail
    ProbeErrors = @($errorsSeen | Sort-Object)
    SceneTimeline = @($timeline.ToArray())
    Players = $playerReports
    ManagerBoundPlayerObserved = @($playerReports | Where-Object ManagerBoundSamples -gt 0).Count -gt 0
    CameraFollowObserved = @($playerReports | Where-Object CameraFollowSamples -gt 0).Count -gt 0
}
$reportRoot = Join-Path $PSScriptRoot '..\.local\analysis'
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$json = $report | ConvertTo-Json -Depth 8
$json | Set-Content -LiteralPath (Join-Path $reportRoot 'discovery-summary.json') -Encoding utf8
$json
