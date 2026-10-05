<#
.SYNOPSIS
Download and verify one explicitly selected development prerelease without installing it.
.PARAMETER OutputDirectory
A fresh child directory of development/artifacts; relative paths are workspace-relative.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ReleaseTag,
    [string]$VerificationPath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$taskDevelopment = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskWorkspace = [IO.Path]::GetFullPath((Join-Path $taskDevelopment '..'))
$taskArtifacts = Join-Path $taskDevelopment 'artifacts'
if ($ReleaseTag -cnotmatch '^v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)-dev$' -or $ReleaseTag.Length -gt 64) {
    throw 'ReleaseTag must be an explicit vX.Y.Z-dev tag.'
}
$taskVersion = $ReleaseTag.Substring(1)
if (!$VerificationPath) { $VerificationPath = Join-Path $taskDevelopment 'logs/core-verification.json' }
elseif (![IO.Path]::IsPathRooted($VerificationPath)) { $VerificationPath = Join-Path $taskWorkspace $VerificationPath }
$VerificationPath = [IO.Path]::GetFullPath($VerificationPath)
Assert-NoPathLinks $VerificationPath
if (!(Test-Path -LiteralPath $VerificationPath -PathType Leaf)) { throw 'The trusted Build verification record is missing.' }
$taskProof = Get-Content -LiteralPath $VerificationPath -Raw | ConvertFrom-Json
if ($taskProof.BuildPassed -isnot [bool] -or !$taskProof.BuildPassed -or
    $taskProof.BuildWarningsAsErrors -isnot [bool] -or !$taskProof.BuildWarningsAsErrors -or
    ($taskProof.BuildScriptExitCode -isnot [int] -and $taskProof.BuildScriptExitCode -isnot [long]) -or $taskProof.BuildScriptExitCode -ne 0 -or
    $taskProof.BuildValidationInputsSealedBeforeExecution -isnot [bool] -or !$taskProof.BuildValidationInputsSealedBeforeExecution -or
    $taskProof.BuildValidationInputsIdenticalAfterExecution -isnot [bool] -or !$taskProof.BuildValidationInputsIdenticalAfterExecution -or
    $taskProof.PlaytestPackageFilesVerified -isnot [bool] -or !$taskProof.PlaytestPackageFilesVerified -or
    $taskProof.PluginVersion -cne $taskVersion -or
    ($taskProof.ProtocolVersion -isnot [int] -and $taskProof.ProtocolVersion -isnot [long]) -or
    $taskProof.ProtocolVersion -lt 1 -or $taskProof.ProtocolVersion -gt [int]::MaxValue -or
    $taskProof.PluginSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
    $taskProof.PlaytestPackageSHA256 -notmatch '^[A-Fa-f0-9]{64}$') {
    throw 'The tag requires a matching passed, sealed Build and verified package proof.'
}
foreach ($taskFlag in @('UnityRuntimeVerified', 'NativeAbiVerified', 'FullGuestIsolationVerified', 'TwoRealGameSessionsVerified', 'Deployed', 'GameLaunched')) {
    $taskProperty = $taskProof.PSObject.Properties[$taskFlag]
    if ($null -eq $taskProperty -or $taskProperty.Value -isnot [bool] -or $taskProperty.Value -ne $false) {
        throw 'The trusted proof must explicitly retain the development/native-unverified scope.'
    }
}
$taskDependencies = Get-Content -LiteralPath (Join-Path $taskDevelopment 'config/dependencies.json') -Raw | ConvertFrom-Json
if ($taskDependencies.repositoryUrl -cnotmatch '^https://github\.com/([A-Za-z0-9][A-Za-z0-9_.-]*)/([A-Za-z0-9][A-Za-z0-9_.-]*?)(?:\.git)?/?$') {
    throw 'The configured repository must be a canonical public HTTPS GitHub repository.'
}
$taskRepository = $Matches[1] + '/' + $Matches[2]
if ($taskRepository -cne 'gao-xh/MultiDave') { throw 'This verifier only accepts the MultiDave repository.' }
$taskZipName = 'DaveCoop-' + $taskVersion + '.zip'
$taskChecksumName = $taskZipName + '.sha256'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $taskArtifacts ('playtest-download/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N'))
} elseif (![IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $taskWorkspace $OutputDirectory }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (!$taskOutput.StartsWith($taskArtifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be a fresh child of workspace development/artifacts.'
}
Assert-NoPathLinks $taskOutput
if (Test-Path -LiteralPath $taskOutput) { throw 'OutputDirectory already exists; nothing was overwritten.' }

# Resolve only this explicit tag; no credentials, latest-release fallback or game discovery.
$taskRelease = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $taskRepository + '/releases/tags/' + $ReleaseTag) -Headers @{ 'User-Agent' = 'MultiDave-Playtest-Package' } -TimeoutSec 30
if ($taskRelease.tag_name -cne $ReleaseTag -or $taskRelease.draft -isnot [bool] -or $taskRelease.draft -ne $false -or
    $taskRelease.prerelease -isnot [bool] -or $taskRelease.prerelease -ne $true) {
    throw 'The requested release must have the exact tag and be a published prerelease.'
}
$taskAssets = @($taskRelease.assets)
$taskZipAsset = @($taskAssets | Where-Object { $_.name -ceq $taskZipName })
$taskChecksumAsset = @($taskAssets | Where-Object { $_.name -ceq $taskChecksumName })
if ($taskAssets.Count -ne 2 -or $taskZipAsset.Count -ne 1 -or $taskChecksumAsset.Count -ne 1) {
    throw 'The prerelease must contain exactly its versioned ZIP and SHA256 sidecar.'
}
$taskAssetPrefix = 'https://github.com/' + $taskRepository + '/releases/download/' + $ReleaseTag + '/'
foreach ($taskAsset in @($taskZipAsset[0], $taskChecksumAsset[0])) {
    if ($taskAsset.browser_download_url -cne ($taskAssetPrefix + $taskAsset.name) -or
        ($taskAsset.size -isnot [int] -and $taskAsset.size -isnot [long]) -or $taskAsset.size -lt 1 -or
        ($taskAsset.name -ceq $taskZipName -and $taskAsset.size -gt 33554432) -or
        ($taskAsset.name -ceq $taskChecksumName -and $taskAsset.size -gt 512)) {
        throw 'Release asset URL or declared size is unsupported.'
    }
}

[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskZipPath = Join-Path $taskOutput $taskZipName
$taskChecksumPath = Join-Path $taskOutput $taskChecksumName
function Receive-PlaytestAsset {
    param([object]$Asset, [string]$Destination, [string]$ExpectedHash)
    Assert-NoPathLinks $Destination
    $partial = $Destination + '.download'
    if ((Test-Path -LiteralPath $Destination) -or (Test-Path -LiteralPath $partial)) { throw 'A download destination already exists.' }
    Invoke-WebRequest -Uri $Asset.browser_download_url -OutFile $partial -UseBasicParsing -Headers @{ 'User-Agent' = 'MultiDave-Playtest-Package' } -TimeoutSec 180
    Assert-NoPathLinks $partial
    if ((Get-Item -LiteralPath $partial).Length -ne $Asset.size) { throw 'The downloaded asset length does not match its declared size.' }
    if ($ExpectedHash -and (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $ExpectedHash) { throw 'Downloaded package SHA256 mismatch.' }
    # File.Move rejects an existing destination. Failed downloads remain in this unique directory.
    [IO.File]::Move($partial, $Destination)
}
Receive-PlaytestAsset $taskChecksumAsset[0] $taskChecksumPath ''
$taskChecksum = Get-Content -LiteralPath $taskChecksumPath -Raw
$taskChecksumMatch = [regex]::Match($taskChecksum, '\A([A-Fa-f0-9]{64})[ \t]+(?:\*)?' + [regex]::Escape($taskZipName) + '(?:\r?\n)?\z')
if (!$taskChecksumMatch.Success -or $taskChecksumMatch.Groups[1].Value -ine $taskProof.PlaytestPackageSHA256) {
    throw 'The sidecar does not match the exact filename and trusted package SHA256.'
}
Receive-PlaytestAsset $taskZipAsset[0] $taskZipPath $taskProof.PlaytestPackageSHA256
$taskZipHash = (Get-FileHash -LiteralPath $taskZipPath -Algorithm SHA256).Hash
if ($taskZipHash -ine $taskProof.PlaytestPackageSHA256) { throw 'Downloaded package SHA256 mismatch.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskArchive = [IO.Compression.ZipFile]::OpenRead($taskZipPath)
try {
    $taskAllowedDirectories = @('BepInEx/', 'BepInEx/plugins/', 'BepInEx/plugins/DaveCoop/')
    $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $taskFiles = @()
    foreach ($taskEntry in $taskArchive.Entries) {
        $taskName = $taskEntry.FullName
        if (!$taskSeen.Add($taskName) -or $taskName.Contains('\') -or $taskName.StartsWith('/') -or
            $taskName -match '(^|/)\.\.(/|$)|:|[\x00-\x1f]') { throw 'Unsafe or duplicate ZIP entry.' }
        # Reject symlinks and other Unix special files even though nothing is extracted.
        $taskUnixType = ($taskEntry.ExternalAttributes -shr 16) -band 0xF000
        if ($taskUnixType -notin @(0, 0x4000, 0x8000)) { throw 'Linked or special ZIP entries are unsupported.' }
        if (!$taskEntry.Name) {
            if ($taskName -cnotin $taskAllowedDirectories -or $taskEntry.Length -ne 0) { throw 'Unexpected ZIP directory.' }
        } else { $taskFiles += $taskEntry }
    }
    if ($taskFiles.Count -ne 2 -or @($taskFiles | Where-Object { $_.FullName -ceq 'manifest.json' }).Count -ne 1 -or
        @($taskFiles | Where-Object { $_.FullName -ceq 'BepInEx/plugins/DaveCoop/DaveCoop.dll' }).Count -ne 1) {
        throw 'The ZIP must contain exactly manifest.json and the project plugin DLL.'
    }
    $taskManifestEntry = $taskArchive.GetEntry('manifest.json')
    if ($taskManifestEntry.Length -lt 1 -or $taskManifestEntry.Length -gt 65536) { throw 'Manifest length is unsupported.' }
    $taskReader = [IO.StreamReader]::new($taskManifestEntry.Open(), [Text.Encoding]::UTF8, $true)
    try { $taskManifest = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
    if (($taskManifest.schemaVersion -isnot [int] -and $taskManifest.schemaVersion -isnot [long]) -or
        $taskManifest.schemaVersion -ne 1 -or $taskManifest.pluginVersion -cne $taskVersion -or
        $taskManifest.stage -cne 'development-native-unverified' -or $taskManifest.channel -cne 'development' -or
        ($taskManifest.protocolVersion -isnot [int] -and $taskManifest.protocolVersion -isnot [long]) -or
        $taskManifest.protocolVersion -ne $taskProof.ProtocolVersion -or
        $taskManifest.testedUnityVersion -cne $taskDependencies.testedUnityVersion -or
        $taskManifest.testedSteamBuildId -cne $taskDependencies.testedSteamBuildId -or
        @($taskManifest.files).Count -ne 1 -or
        $taskManifest.files[0].path -cne 'BepInEx/plugins/DaveCoop/DaveCoop.dll' -or
        $taskManifest.files[0].sha256 -ine $taskProof.PluginSHA256) {
        throw 'Manifest version, protocol, stage, game profile or plugin identity does not match the trusted proof.'
    }
    foreach ($taskFlag in @('buildPassed', 'buildWarningsAsErrors', 'buildInputsUnchanged')) {
        $taskProperty = $taskManifest.verification.PSObject.Properties[$taskFlag]
        if ($null -eq $taskProperty -or $taskProperty.Value -isnot [bool] -or $taskProperty.Value -ne $true) { throw 'Manifest Build proof is incomplete.' }
    }
    foreach ($taskFlag in @('nativeExecutionVerified', 'nativeAbiVerified', 'twoGamePlaytestVerified', 'fullGuestIsolationVerified', 'normalReturnAndSaveVerified')) {
        $taskProperty = $taskManifest.verification.PSObject.Properties[$taskFlag]
        if ($null -eq $taskProperty -or $taskProperty.Value -isnot [bool] -or $taskProperty.Value -ne $false) { throw 'Manifest must preserve every native-unverified flag.' }
    }
    $taskVerificationKeys = @('buildPassed', 'buildWarningsAsErrors', 'buildInputsUnchanged', 'recordFile',
        'nativeExecutionVerified', 'nativeAbiVerified', 'twoGamePlaytestVerified', 'fullGuestIsolationVerified', 'normalReturnAndSaveVerified')
    if (@($taskManifest.verification.PSObject.Properties | Where-Object { $_.Name -cnotin $taskVerificationKeys }).Count -ne 0) {
        throw 'Unknown manifest verification claims are unsupported.'
    }
    $taskDllEntry = $taskArchive.GetEntry('BepInEx/plugins/DaveCoop/DaveCoop.dll')
    if ($taskDllEntry.Length -lt 1 -or $taskDllEntry.Length -gt 33554432) { throw 'Plugin length is unsupported.' }
    $taskDllStream = $taskDllEntry.Open()
    $taskHasher = [Security.Cryptography.SHA256]::Create()
    try { $taskDllHash = [BitConverter]::ToString($taskHasher.ComputeHash($taskDllStream)).Replace('-', '') }
    finally { $taskHasher.Dispose(); $taskDllStream.Dispose() }
    if ($taskDllHash -ine $taskProof.PluginSHA256) { throw 'ZIP plugin bytes differ from the trusted Build.' }
} finally { $taskArchive.Dispose() }
Assert-NoPathLinks $taskZipPath
Assert-NoPathLinks $taskChecksumPath
if ((Get-FileHash -LiteralPath $taskZipPath -Algorithm SHA256).Hash -ine $taskZipHash -or
    (Get-Content -LiteralPath $taskChecksumPath -Raw) -cne $taskChecksum) { throw 'The downloaded files changed during verification.' }

[pscustomobject]@{
    Verified = $true; ReleaseTag = $ReleaseTag; Version = $taskVersion; ProtocolVersion = $taskProof.ProtocolVersion
    PackagePath = $taskZipPath; ChecksumPath = $taskChecksumPath; SHA256 = $taskZipHash; PluginSHA256 = $taskDllHash
    Stage = 'development-native-unverified'; Installed = $false; Deployed = $false; GameLaunched = $false
    NativeExecuted = $false; NativeAbiVerified = $false; TwoGamePlaytestVerified = $false
} | ConvertTo-Json
