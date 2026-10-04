[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$Development,
    [string]$VerificationPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dependencies = Get-Content -LiteralPath (Join-Path $projectRoot 'config\dependencies.json') -Raw | ConvertFrom-Json
if ($VerificationPath -and !$Development) { throw '-VerificationPath requires -Development.' }
$verification = $null
if ($Development) {
    if (!$VerificationPath) { $VerificationPath = Join-Path $projectRoot 'logs\core-verification.json' }
    if (!(Test-Path -LiteralPath $VerificationPath -PathType Leaf)) { throw 'The development Build verification record is missing.' }
    $verification = Get-Content -LiteralPath $VerificationPath -Raw | ConvertFrom-Json
    if ($verification.BuildPassed -ne $true -or $verification.BuildWarningsAsErrors -ne $true -or
        $verification.BuildScriptExitCode -ne 0 -or $verification.BuildValidationInputsSealedBeforeExecution -ne $true -or
        $verification.BuildValidationInputsIdenticalAfterExecution -ne $true -or
        $verification.PluginSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        $verification.PluginVersion -notmatch '^\d+\.\d+\.\d+-[A-Za-z0-9][A-Za-z0-9.-]*$' -or
        ($verification.ProtocolVersion -isnot [int] -and $verification.ProtocolVersion -isnot [long]) -or
        $verification.ProtocolVersion -lt 1 -or $verification.ProtocolVersion -gt [int]::MaxValue) {
        throw 'A passed, sealed development Build verification record is required.'
    }
}
$environment = & (Join-Path $PSScriptRoot 'Get-Environment.ps1') -GamePath $GamePath | ConvertFrom-Json
if ($environment.UnityVersion -ne $dependencies.testedUnityVersion -or $environment.SteamBuildId -ne $dependencies.testedSteamBuildId) {
    throw 'Record validation of this game version in dependencies.json before packaging.'
}
$sourceDll = Join-Path $projectRoot 'artifacts\plugin\DaveCoop.dll'
if (!(Test-Path -LiteralPath $sourceDll)) { throw 'Build the plugin first.' }
$sourceHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
if ($Development -and $sourceHash -ne $verification.PluginSHA256) { throw 'The plugin DLL differs from the verified development Build.' }
# Read the compiled attribute without executing the plugin. A development build
# must not accidentally be packaged with the older release's manifest version.
Add-Type -Path (Join-Path $environment.GamePath 'BepInEx\core\Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($sourceDll)
try {
    $pluginAttributes = @($assembly.MainModule.Types | ForEach-Object {
        $_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
    })
    if ($pluginAttributes.Count -ne 1) { throw 'Expected exactly one BepInPlugin attribute in the compiled plugin.' }
    $compiledVersion = [string]$pluginAttributes[0].ConstructorArguments[2].Value
    if ($compiledVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?$') { throw 'The compiled plugin version is not a safe package version.' }
    if ($Development) {
        if ($compiledVersion -ne $verification.PluginVersion) { throw 'The compiled plugin version differs from the development verification record.' }
    } elseif ($compiledVersion -ne $dependencies.pluginVersion) {
        throw "Compiled plugin version $compiledVersion differs from release metadata $($dependencies.pluginVersion). Validate and update dependencies.json before packaging."
    }
} finally {
    $assembly.Dispose()
}
$packageRoot = Join-Path $projectRoot ('artifacts\packages\' + [Guid]::NewGuid().ToString('N'))
$dllRelativePath = 'BepInEx/plugins/DaveCoop/DaveCoop.dll'
$destinationDll = Join-Path $packageRoot $dllRelativePath
New-Item -ItemType Directory -Path (Split-Path -Parent $destinationDll) -Force | Out-Null
Copy-Item -LiteralPath $sourceDll -Destination $destinationDll
$destinationHash = (Get-FileHash -LiteralPath $destinationDll -Algorithm SHA256).Hash
if ($destinationHash -ne $sourceHash -or (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash -ne $sourceHash) {
    throw 'The plugin DLL changed while packaging.'
}
$manifest = [ordered]@{
    schemaVersion = 1
    pluginVersion = $compiledVersion
    stage = if ($Development) { 'development-native-unverified' } else { 'bootstrap-prototype' }
    testedUnityVersion = $dependencies.testedUnityVersion
    testedSteamBuildId = $dependencies.testedSteamBuildId
    files = @([pscustomobject]@{ path = $dllRelativePath; sha256 = $destinationHash })
}
if ($Development) {
    $manifest.channel = 'development'
    $manifest.protocolVersion = $verification.ProtocolVersion
    $manifest.verification = [ordered]@{
        buildPassed = $true
        buildWarningsAsErrors = $true
        buildInputsUnchanged = $true
        recordFile = [IO.Path]::GetFileName($VerificationPath)
        nativeExecutionVerified = $false
        nativeAbiVerified = $false
        twoGamePlaytestVerified = $false
        fullGuestIsolationVerified = $false
        normalReturnAndSaveVerified = $false
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding utf8
$releaseRoot = Join-Path $projectRoot $(if ($Development) { 'artifacts\playtest' } else { 'artifacts\release' })
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$archivePath = Join-Path $releaseRoot ('DaveCoop-' + $compiledVersion + '.zip')
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
($hash + '  ' + [IO.Path]::GetFileName($archivePath)) | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ascii
[pscustomobject]@{ PackagePath = $archivePath; SHA256 = $hash; Version = $compiledVersion; Stage = $manifest.stage; PluginSHA256 = $destinationHash } | ConvertTo-Json
