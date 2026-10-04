<#
.SYNOPSIS
Create separate Host/Guest movement-playtest configurations without installing them.
.PARAMETER OutputDirectory
A new directory inside development/artifacts. Relative paths are workspace-relative.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HostAddress,
    [Parameter(Mandatory = $true)][ValidateRange(1, 65535)][int]$Port,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($HostAddress -notmatch '^(?:0|[1-9]\d{0,2})(?:\.(?:0|[1-9]\d{0,2})){3}$') {
    throw 'HostAddress must be a numeric dotted IPv4 address, not a hostname or IPv6 address.'
}
$taskAddress = $null
if (![Net.IPAddress]::TryParse($HostAddress, [ref]$taskAddress) -or
    $taskAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
    $taskAddress.ToString() -ne $HostAddress) {
    throw 'HostAddress is not a valid dotted IPv4 address.'
}

$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$taskArtifacts = [IO.Path]::GetFullPath((Join-Path $taskWorkspace 'development/artifacts'))
if (!$OutputDirectory) {
    $taskStamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $OutputDirectory = Join-Path $taskArtifacts ('playtest-config/' + $taskStamp + '-' + [Guid]::NewGuid().ToString('N'))
} elseif (![IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $taskWorkspace $OutputDirectory
}
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (!$taskOutput.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a new child directory of workspace development/artifacts.'
}
if (Test-Path -LiteralPath $taskOutput) { throw 'OutputDirectory already exists; no files were changed.' }

function Assert-PlaytestOutputPath {
    param([string]$LiteralPath)
    $taskAncestor = [IO.Path]::GetFullPath($LiteralPath)
    while ($taskAncestor) {
        if ((Test-Path -LiteralPath $taskAncestor) -and
            ((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'OutputDirectory cannot use a symbolic link or directory junction.'
        }
        $taskParent = [IO.Directory]::GetParent($taskAncestor)
        $taskAncestor = if ($taskParent) { $taskParent.FullName } else { $null }
    }
}
Assert-PlaytestOutputPath $taskOutput

$taskPlugin = Get-Content -LiteralPath (Join-Path $taskWorkspace 'development/src/DaveCoop/Plugin.cs') -Raw
$taskMessages = Get-Content -LiteralPath (Join-Path $taskWorkspace 'development/src/DaveCoop/Core/Protocol/Messages.cs') -Raw
$taskVersion = [regex]::Match($taskPlugin, 'public\s+const\s+string\s+Version\s*=\s*"([^"]+)"')
$taskProtocol = [regex]::Match($taskMessages, 'ProtocolVersion\s*\{[^}]+\}\s*=\s*(\d+)')
if (!$taskVersion.Success -or !$taskProtocol.Success) { throw 'Current source version/protocol could not be read.' }

$taskPaths = @()
$taskCreatedFiles = @()
try {
    [IO.Directory]::CreateDirectory($taskOutput) | Out-Null
    foreach ($taskRole in @('Host', 'Guest')) {
        $taskTemporary = if ($taskRole -eq 'Guest') { 'true' } else { 'false' }
        $taskConfig = @"
# MultiDave movement-only playtest: $taskRole
# Source $($taskVersion.Groups[1].Value), protocol $($taskProtocol.Groups[1].Value).
# Back up the existing game cfg before manually installing this file.
# Host: F11 -> Host room, then close the panel before moving.
# Guest: startup automatically joins; restart to return to personal progress.
# Unity movement, guest isolation and two-game gameplay remain unverified.

[Startup]
ExperimentalGuestInitialization = $taskTemporary
ObserveSaveStartup = false

[Diagnostics]
ShowOverlay = true

[Preview]
Enabled = false

[Network]
ShowPanel = false
HostAddress = $HostAddress
Port = $Port
PlayerName = $taskRole
ExperimentalCrewActor = true
ExperimentalCrewHarpoon = false
ExperimentalCrewCargo = false
ExperimentalHostFishAreas = false
TransmitFishObservations = false
ShowFishPreview = false
ShowFishWorld = false
ObserveFishInteractions = false
ObserveMapSelectionCalls = false
ObserveLootCalls = false
ObserveMapOrigins = false
"@
        $taskRolePath = Join-Path $taskOutput $taskRole
        Assert-PlaytestOutputPath $taskRolePath
        [IO.Directory]::CreateDirectory($taskRolePath) | Out-Null
        $taskFile = Join-Path $taskRolePath 'local.davecoop.prototype.cfg'
        Assert-PlaytestOutputPath $taskFile
        $taskStream = [IO.FileStream]::new($taskFile, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $taskCreatedFiles += $taskFile
        try {
            $taskBytes = [Text.UTF8Encoding]::new($false).GetBytes($taskConfig + "`r`n")
            $taskStream.Write($taskBytes, 0, $taskBytes.Length)
        } finally { $taskStream.Dispose() }
        $taskPaths += [ordered]@{ Role = $taskRole; ConfigPath = $taskFile; AutomaticGuestJoin = ($taskRole -eq 'Guest') }
    }
} catch {
    # Remove only files this invocation created; never delete/move an existing tree.
    foreach ($taskFile in $taskCreatedFiles) {
        try { Assert-PlaytestOutputPath $taskFile; Remove-Item -LiteralPath $taskFile -ErrorAction Stop } catch { }
    }
    throw
}

[ordered]@{
    SourceVersion = $taskVersion.Groups[1].Value
    ProtocolVersion = [int]$taskProtocol.Groups[1].Value
    Configurations = $taskPaths
    GameConfigurationChanged = $false
    GameLaunched = $false
    PluginDeployed = $false
    ManualInstallRequired = $true
    NextStep = 'Back up each existing game cfg, then manually replace it with the matching Host/Guest file. Start Host and select F11 -> Host room first; start Guest afterward. Enter a fresh dive after connection. Restart Guest to return to personal progress; complete isolation and gameplay are not verified.'
} | ConvertTo-Json -Depth 4
