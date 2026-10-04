[CmdletBinding()]
param(
    [string]$GamePath,
    [string]$RepositoryUrl,
    [string]$PackagePath,
    [switch]$LaunchGame,
    [switch]$InspectOnly
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dependencies = Get-Content -LiteralPath (Join-Path $projectRoot 'config\dependencies.json') -Raw | ConvertFrom-Json
$runRoot = Join-Path $projectRoot '.local\logs'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$runLog = Join-Path $runRoot ('install-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6) + '.jsonl')
try {
    $environment = & (Join-Path $PSScriptRoot 'Get-Environment.ps1') -GamePath $GamePath | ConvertFrom-Json
    $gameRoot = $environment.GamePath
    Write-RunEvent $runLog 'environment' $environment
    if ($InspectOnly) { $environment | ConvertTo-Json; return }
    Assert-DaveGameClosed
    Assert-NoPathLinks $gameRoot
    if (!$environment.Is64BitOS) { throw 'Windows x64 is required.' }
    $downloadRoot = Join-Path $projectRoot '.local\downloads'
    New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
    if (!$PackagePath) {
        if (!$RepositoryUrl) {
            $gitCommand = Get-Command git -ErrorAction SilentlyContinue
            if ($gitCommand) { $RepositoryUrl = & $gitCommand.Source -C $projectRoot remote get-url origin 2>$null }
        }
        if (!$RepositoryUrl -or $RepositoryUrl -notmatch '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
            throw 'Supply -RepositoryUrl https://github.com/OWNER/REPOSITORY, or -PackagePath for a local release ZIP.'
        }
        $repositoryName = $Matches[1] + '/' + $Matches[2]
        $release = Invoke-RestMethod -Uri ('https://api.github.com/repos/' + $repositoryName + '/releases/latest') -Headers @{ 'User-Agent' = 'DaveCoop-Setup' } -TimeoutSec 30
        $asset = @($release.assets | Where-Object { $_.name -match '^DaveCoop-[0-9]+\.[0-9]+\.[0-9]+\.zip$' })
        if ($asset.Count -ne 1) { throw 'The latest release must contain one DaveCoop-VERSION.zip asset.' }
        $checksumAsset = @($release.assets | Where-Object { $_.name -eq ($asset[0].name + '.sha256') })
        if ($checksumAsset.Count -ne 1) { throw 'The release is missing its SHA256 sidecar file.' }
        $checksumPath = Join-Path $downloadRoot $checksumAsset[0].name
        Receive-VerifiedFile $checksumAsset[0].browser_download_url $checksumPath ''
        $hashMatch = [regex]::Match((Get-Content -LiteralPath $checksumPath -Raw), '^\s*([0-9A-Fa-f]{64})\s+')
        if (!$hashMatch.Success) { throw 'Invalid release checksum file.' }
        $PackagePath = Join-Path $downloadRoot $asset[0].name
        Receive-VerifiedFile $asset[0].browser_download_url $PackagePath $hashMatch.Groups[1].Value
        Write-RunEvent $runLog 'release-downloaded' @{ Repository = $repositoryName; Tag = $release.tag_name; SHA256 = $hashMatch.Groups[1].Value }
    }
    $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    $packageStage = Join-Path $projectRoot ('.local\packages\' + [Guid]::NewGuid().ToString('N'))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $packageEntries = @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
        if ($packageEntries.Count -ne 2 -or @($packageEntries | Select-Object -Unique).Count -ne 2 -or
            'manifest.json' -notin $packageEntries -or 'BepInEx/plugins/DaveCoop/DaveCoop.dll' -notin $packageEntries) {
            throw 'The release ZIP must contain only manifest.json and the DaveCoop plugin DLL.'
        }
        foreach ($archiveEntry in $archive.Entries) {
            $entryName = $archiveEntry.FullName.Replace('\', '/')
            if ($entryName.StartsWith('/') -or $entryName -match '(^|/)\.\.(/|$)|:') { throw 'Unsafe ZIP entry.' }
        }
    } finally { $archive.Dispose() }
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $packageStage
    $manifest = Get-Content -LiteralPath (Join-Path $packageStage 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or @($manifest.files).Count -ne 1 -or $manifest.files[0].path -ne 'BepInEx/plugins/DaveCoop/DaveCoop.dll') { throw 'Unsupported release manifest.' }
    if ($environment.UnityVersion -ne $manifest.testedUnityVersion -or $environment.SteamBuildId -ne $manifest.testedSteamBuildId) {
        throw "This package was tested on Unity $($manifest.testedUnityVersion), Steam build $($manifest.testedSteamBuildId). Your game version requires validation."
    }
    $sourceDll = Join-Path $packageStage $manifest.files[0].path
    $pluginHash = (Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash
    if ($pluginHash -ne $manifest.files[0].sha256) { throw 'Plugin hash does not match the release manifest.' }
    $frameworkDll = Join-Path $gameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
    if (!(Test-Path -LiteralPath $frameworkDll)) {
        $frameworkArchive = Join-Path $downloadRoot $dependencies.bepinex.archive
        Receive-VerifiedFile $dependencies.bepinex.url $frameworkArchive $dependencies.bepinex.sha256
        $frameworkStage = Join-Path $projectRoot ('.local\frameworks\' + [Guid]::NewGuid().ToString('N'))
        Expand-Archive -LiteralPath $frameworkArchive -DestinationPath $frameworkStage
        & (Join-Path $PSScriptRoot 'Install-Framework.ps1') -GamePath $gameRoot -StagePath $frameworkStage
        Write-RunEvent $runLog 'framework-installed' @{ Version = $dependencies.bepinex.version }
    } else {
        $actualFrameworkHash = (Get-FileHash -LiteralPath $frameworkDll -Algorithm SHA256).Hash
        if ($actualFrameworkHash -ne $dependencies.bepinex.coreSha256) { throw 'An unvalidated BepInEx build is already installed. Inspect it before replacing or reusing it.' }
        if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'winhttp.dll'))) { throw 'BepInEx is installed but its loader is missing or disabled.' }
        Write-RunEvent $runLog 'framework-reused' @{ Version = $dependencies.bepinex.version }
    }
    Assert-DaveGameClosed
    $pluginRoot = Join-Path $gameRoot 'BepInEx\plugins\DaveCoop'
    Assert-NoPathLinks $pluginRoot
    New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
    $targetDll = Join-Path $pluginRoot 'DaveCoop.dll'
    $alreadyInstalled = (Test-Path -LiteralPath $targetDll) -and ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -eq $pluginHash)
    if (!$alreadyInstalled) {
        if (Test-Path -LiteralPath $targetDll) {
            $backupRoot = Join-Path $projectRoot ('.local\backups\' + [Guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
            Copy-Item -LiteralPath $targetDll -Destination (Join-Path $backupRoot 'DaveCoop.dll')
            Write-RunEvent $runLog 'plugin-backup' @{ Path = $backupRoot }
        }
        Copy-Item -LiteralPath $sourceDll -Destination $targetDll -Force
        if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ne $pluginHash) { throw 'Deployed plugin hash mismatch.' }
    }
    Write-RunEvent $runLog 'plugin-installed' @{ Version = $manifest.pluginVersion; SHA256 = $pluginHash; AlreadyInstalled = $alreadyInstalled }
    $result = [pscustomobject]@{ GamePath = $gameRoot; PluginVersion = $manifest.pluginVersion; AlreadyInstalled = $alreadyInstalled; LogPath = $runLog }
    if ($LaunchGame) {
        Start-Process -FilePath (Join-Path $gameRoot 'DaveTheDiver.exe') -WorkingDirectory $gameRoot -WindowStyle Normal
        Write-RunEvent $runLog 'game-launched' @{ Verification = 'Run Check-Status.ps1 after startup; stay at the title screen.' }
    }
    $result | ConvertTo-Json
} catch {
    Write-RunEvent $runLog 'failed' @{ Message = $_.Exception.Message }
    throw
}
