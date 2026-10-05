[CmdletBinding()]
param(
    [string]$UserProfilePath,
    [string[]]$SteamRoot,
    [string]$GamePath,
    [string]$BackupRoot,
    [string]$VerifyBackup,
    [string]$ExpectedManifestSHA256
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Test-BackupPathWithin {
    param([string]$Path, [string]$Root)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    $candidate = [IO.Path]::GetFullPath($Path)
    return $candidate.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Read-BackupTree {
    param([string]$Root, [string]$Filter = '*')
    Assert-NoPathLinks $Root
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([char[]]'\/')
    if (!(Test-Path -LiteralPath $rootPath -PathType Container)) { throw 'A source or backup directory is missing.' }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($rootPath)
    $files = [Collections.Generic.List[object]]::new()
    $directories = [Collections.Generic.List[string]]::new()
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        Assert-NoPathLinks $directory
        if (!(Test-BackupPathWithin $directory $rootPath)) { throw 'Directory escaped its fixed source root.' }
        $directories.Add($directory.Substring($rootPath.Length).TrimStart([char[]]'\/').Replace('\', '/'))
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points are not supported for save backups.' }
            if (!(Test-BackupPathWithin $entry.FullName $rootPath)) { throw 'File escaped its fixed source root.' }
            if ($entry.PSIsContainer) { $pending.Push($entry.FullName); continue }
            if ($entry.Name -notlike $Filter) { continue }
            Assert-NoPathLinks $entry.FullName
            $length = $entry.Length
            $hash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
            Assert-NoPathLinks $entry.FullName
            if ((Get-Item -LiteralPath $entry.FullName -Force).Length -ne $length) { throw 'A file changed while hashing.' }
            $files.Add([pscustomobject]@{
                RelativePath = $entry.FullName.Substring($rootPath.Length).TrimStart([char[]]'\/').Replace('\', '/')
                Length = $length
                SHA256 = $hash
            })
        }
    }
    return [pscustomobject]@{
        Files = @($files.ToArray() | Sort-Object RelativePath)
        Directories = @($directories.ToArray() | Sort-Object)
    }
}

function Assert-BackupContents {
    param([string]$Directory, [object]$Manifest)
    if (($Manifest.SchemaVersion -isnot [int] -and $Manifest.SchemaVersion -isnot [long]) -or
        $Manifest.SchemaVersion -ne 1 -or $Manifest.Verified -isnot [bool] -or $Manifest.Verified -ne $true -or
        $Manifest.SourceFilesStable -isnot [bool] -or $Manifest.SourceFilesStable -ne $true -or
        $Manifest.GameClosedBeforeAndAfter -isnot [bool] -or $Manifest.GameClosedBeforeAndAfter -ne $true -or
        @($Manifest.Files).Count -eq 0) {
        throw 'The backup does not have a successful verification manifest.'
    }
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($Manifest.Files)) {
        $relative = [string]$file.BackupPath
        if (!$relative.StartsWith('sources/', [StringComparison]::Ordinal) -or
            $relative -match '\\|:|(^|/)\.\.(/|$)|(^|/)\.(/|$)' -or !$expected.Add($relative) -or
            $file.SourceSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or $file.BackupSHA256 -ne $file.SourceSHA256 -or
            ($file.Length -isnot [long] -and $file.Length -isnot [int]) -or $file.Length -lt 0) {
            throw 'The backup manifest contains an invalid file entry.'
        }
        $target = [IO.Path]::GetFullPath((Join-Path $Directory $relative))
        if (!(Test-BackupPathWithin $target $Directory)) { throw 'Backup file escaped its directory.' }
        Assert-NoPathLinks $target
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or
            (Get-Item -LiteralPath $target -Force).Length -ne $file.Length -or
            (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.BackupSHA256) {
            throw 'Backup file length or SHA256 does not match the manifest.'
        }
    }
    $tree = Read-BackupTree $Directory
    $actualFiles = @($tree.Files | Where-Object { $_.RelativePath -ne 'manifest.json' })
    if ($actualFiles.Count -ne $expected.Count) { throw 'The backup file set differs from the manifest.' }
    foreach ($file in $actualFiles) {
        if (!$expected.Contains($file.RelativePath)) { throw 'The backup contains an unrecorded file.' }
    }
    if (($tree.Directories | ConvertTo-Json -Compress) -ne (@($Manifest.BackupDirectories) | ConvertTo-Json -Compress)) {
        throw 'The backup directory set differs from the manifest.'
    }
}

if ($PSBoundParameters.ContainsKey('ExpectedManifestSHA256')) {
    if (!$VerifyBackup -or $ExpectedManifestSHA256 -notmatch '\A[A-Fa-f0-9]{64}\z') {
        throw '-ExpectedManifestSHA256 requires -VerifyBackup and a 64-digit SHA256 from a separate trusted record.'
    }
}

if ($VerifyBackup) {
    if ($PSBoundParameters.ContainsKey('UserProfilePath') -or $PSBoundParameters.ContainsKey('SteamRoot') -or
        $PSBoundParameters.ContainsKey('GamePath') -or $PSBoundParameters.ContainsKey('BackupRoot')) {
        throw '-VerifyBackup cannot be combined with backup source or destination parameters.'
    }
    $verifyRoot = [IO.Path]::GetFullPath($VerifyBackup)
    Assert-NoPathLinks $verifyRoot
    $manifestPath = Join-Path $verifyRoot 'manifest.json'
    Assert-NoPathLinks $manifestPath
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Backup manifest is missing.' }
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    if ($ExpectedManifestSHA256 -and $manifestHash -ne $ExpectedManifestSHA256) {
        throw 'Backup manifest SHA256 does not match the separately recorded value.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-BackupContents $verifyRoot $manifest
    if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash -ne $manifestHash) {
        throw 'The backup manifest changed during verification.'
    }
    [pscustomobject]@{ BackupPath = $verifyRoot; Verified = $true; Files = @($manifest.Files).Count;
        CoverageComplete = $manifest.CoverageComplete; Missing = @($manifest.Missing);
        ManifestSHA256 = $manifestHash; ManifestHashPinned = [bool]$ExpectedManifestSHA256;
        Verification = 'Recorded backup bytes only; current live saves are not compared.' } | ConvertTo-Json -Depth 6
    return
}

Assert-DaveGameClosed
if (!$UserProfilePath) { $UserProfilePath = [Environment]::GetFolderPath('UserProfile') }
if (!$UserProfilePath) { throw 'The user profile is unavailable. Supply -UserProfilePath.' }
$saveRoot = [IO.Path]::GetFullPath((Join-Path $UserProfilePath 'AppData\LocalLow\nexon\DAVE THE DIVER'))
Assert-NoPathLinks $saveRoot
if (!(Test-Path -LiteralPath $saveRoot -PathType Container)) {
    throw 'Dave LocalLow saves are missing under the selected profile. Supply the actual -UserProfilePath.'
}
if (!$BackupRoot) { $BackupRoot = Join-Path $projectRoot '.local\save-backups' }
$backupParent = [IO.Path]::GetFullPath($BackupRoot)
Assert-NoPathLinks $backupParent
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
if ((Test-BackupPathWithin $backupParent $repositoryRoot) -and
    !(Test-BackupPathWithin $backupParent (Join-Path $projectRoot '.local'))) {
    throw 'A backup inside this repository must be under development/.local; save files must not enter Git.'
}
$sources = [Collections.Generic.List[object]]::new()
$missing = [Collections.Generic.List[object]]::new()
$sources.Add([pscustomobject]@{ Id = 'locallow'; Kind = 'DaveLocalLow'; Path = $saveRoot; Filter = '*' })
if (!$PSBoundParameters.ContainsKey('SteamRoot')) {
    $foundSteamRoots = [Collections.Generic.List[string]]::new()
    foreach ($registryPath in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        if (!(Test-Path -LiteralPath $registryPath)) { continue }
        $values = Get-ItemProperty -LiteralPath $registryPath
        foreach ($name in @('SteamPath', 'InstallPath')) {
            $property = $values.PSObject.Properties[$name]
            if ($null -ne $property -and $property.Value) { $foundSteamRoots.Add([string]$property.Value) }
        }
    }
    $SteamRoot = $foundSteamRoots.ToArray()
}
$steamRoots = @($SteamRoot | Where-Object { $_ } | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
if ($steamRoots.Count -eq 0) { $missing.Add([pscustomobject]@{ Kind = 'SteamUserdata'; Reason = 'Steam installation roots were not found.' }) }
$steamIndex = 0
foreach ($steamPath in $steamRoots) {
    $steamIndex++
    Assert-NoPathLinks $steamPath
    $userdata = Join-Path $steamPath 'userdata'
    Assert-NoPathLinks $userdata
    if (!(Test-Path -LiteralPath $userdata -PathType Container)) {
        $missing.Add([pscustomobject]@{ Kind = 'SteamUserdata'; Path = $userdata; Reason = 'Missing' }); continue
    }
    $userIndex = 0
    $appRootsFound = 0
    foreach ($user in @(Get-ChildItem -LiteralPath $userdata -Directory -Force | Sort-Object Name)) {
        $userIndex++
        Assert-NoPathLinks $user.FullName
        $appRoot = Join-Path $user.FullName '1868140'
        Assert-NoPathLinks $appRoot
        if (!(Test-Path -LiteralPath $appRoot -PathType Container)) {
            $missing.Add([pscustomobject]@{ Kind = 'SteamAppCache'; Path = $appRoot; Reason = 'Missing' }); continue
        }
        $appRootsFound++
        $sources.Add([pscustomobject]@{ Id = ('steam-{0:D3}-user-{1:D4}' -f $steamIndex, $userIndex);
            Kind = 'SteamAppCache'; Path = [IO.Path]::GetFullPath($appRoot); Filter = '*' })
    }
    if ($appRootsFound -eq 0) { $missing.Add([pscustomobject]@{ Kind = 'SteamAppCache'; Path = $userdata; Reason = 'No 1868140 app directory found.' }) }
}
$gameRoot = $null
try { $gameRoot = Resolve-DaveGamePath $GamePath } catch {
    if ($GamePath) { throw }
    $missing.Add([pscustomobject]@{ Kind = 'BepInExConfig'; Reason = 'Game installation was not found.' })
}
if ($gameRoot) {
    $configRoot = Join-Path $gameRoot 'BepInEx\config'
    Assert-NoPathLinks $configRoot
    if (Test-Path -LiteralPath $configRoot -PathType Container) {
        $sources.Add([pscustomobject]@{ Id = 'bepinex-config'; Kind = 'BepInExConfig'; Path = [IO.Path]::GetFullPath($configRoot); Filter = '*.cfg' })
    } else { $missing.Add([pscustomobject]@{ Kind = 'BepInExConfig'; Path = $configRoot; Reason = 'Missing' }) }
}
foreach ($source in $sources) {
    if (Test-BackupPathWithin $backupParent $source.Path) { throw 'The backup destination must not be nested in a source directory.' }
}
$backupPath = Join-Path $backupParent ([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [Guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $backupPath) { throw 'The unique backup directory already exists.' }
New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
$manifestPath = Join-Path $backupPath 'manifest.json'
$manifest = [ordered]@{ SchemaVersion = 1; StartedUtc = [DateTimeOffset]::UtcNow.ToString('o'); FinishedUtc = $null;
    Verified = $false; SourceFilesStable = $false; GameClosedBeforeAndAfter = $false;
    CoverageComplete = $missing.Count -eq 0; Missing = @($missing.ToArray()); Sources = @($sources.ToArray());
    Files = @(); BackupDirectories = @(); Failure = $null }
try {
    $firstTrees = @{}
    $copiedFiles = [Collections.Generic.List[object]]::new()
    foreach ($source in $sources) {
        $tree = Read-BackupTree $source.Path $source.Filter
        $firstTrees[$source.Id] = $tree
        if ($source.Id -eq 'locallow' -and $tree.Files.Count -eq 0) { throw 'The selected Dave LocalLow directory contains no files.' }
        foreach ($relative in $tree.Directories) {
            $destination = Join-Path $backupPath ('sources/' + $source.Id + '/' + $relative)
            if (!(Test-BackupPathWithin $destination $backupPath)) { throw 'Backup directory escaped its fixed destination.' }
            Assert-NoPathLinks $destination
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
        }
        foreach ($file in $tree.Files) {
            Assert-DaveGameClosed
            $original = Join-Path $source.Path $file.RelativePath
            $relativeDestination = 'sources/' + $source.Id + '/' + $file.RelativePath
            $destination = Join-Path $backupPath $relativeDestination
            if (!(Test-BackupPathWithin $original $source.Path) -or !(Test-BackupPathWithin $destination $backupPath)) { throw 'Copy path escaped a fixed boundary.' }
            Assert-NoPathLinks $original
            Assert-NoPathLinks $destination
            if (Test-Path -LiteralPath $destination) { throw 'A backup file already exists; it will not be overwritten.' }
            Copy-Item -LiteralPath $original -Destination $destination
            Assert-NoPathLinks $original
            Assert-NoPathLinks $destination
            $backupHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if ((Get-Item -LiteralPath $destination).Length -ne $file.Length -or $backupHash -ne $file.SHA256) { throw 'A copied file does not match its source snapshot.' }
            $copiedFiles.Add([pscustomobject]@{ SourceId = $source.Id; RelativePath = $file.RelativePath;
                BackupPath = $relativeDestination; Length = $file.Length; SourceSHA256 = $file.SHA256; BackupSHA256 = $backupHash })
        }
    }
    $manifest.Files = @($copiedFiles.ToArray())
    foreach ($source in $sources) {
        $lastTree = Read-BackupTree $source.Path $source.Filter
        if (($firstTrees[$source.Id] | ConvertTo-Json -Depth 6 -Compress) -ne ($lastTree | ConvertTo-Json -Depth 6 -Compress)) { throw 'The source file or directory set changed during backup.' }
    }
    $lastSteamApps = [Collections.Generic.List[string]]::new()
    foreach ($steamPath in $steamRoots) {
        $userdata = Join-Path $steamPath 'userdata'
        Assert-NoPathLinks $userdata
        if (!(Test-Path -LiteralPath $userdata -PathType Container)) { continue }
        foreach ($user in @(Get-ChildItem -LiteralPath $userdata -Directory -Force)) {
            Assert-NoPathLinks $user.FullName
            $appRoot = Join-Path $user.FullName '1868140'
            Assert-NoPathLinks $appRoot
            if (Test-Path -LiteralPath $appRoot -PathType Container) { $lastSteamApps.Add([IO.Path]::GetFullPath($appRoot)) }
        }
    }
    $firstSteamApps = @($sources | Where-Object { $_.Kind -eq 'SteamAppCache' } | ForEach-Object { $_.Path } | Sort-Object)
    if (($firstSteamApps | ConvertTo-Json -Compress) -ne (@($lastSteamApps.ToArray() | Sort-Object) | ConvertTo-Json -Compress)) {
        throw 'The Steam application source directory set changed during backup.'
    }
    Assert-DaveGameClosed
    $manifest.SourceFilesStable = $true
    $manifest.GameClosedBeforeAndAfter = $true
    $manifest.BackupDirectories = @((Read-BackupTree $backupPath).Directories)
    $manifest.Verified = $true
    Assert-BackupContents $backupPath ([pscustomobject]$manifest)
    Assert-DaveGameClosed
    $manifest.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    [pscustomobject]@{ BackupPath = $backupPath; Verified = $true; Files = $manifest.Files.Count;
        ManifestSHA256 = $manifestHash;
        CoverageComplete = $manifest.CoverageComplete; Missing = $manifest.Missing } | ConvertTo-Json -Depth 6
} catch {
    $manifest.Verified = $false
    $manifest.FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    $manifest.Failure = $_.Exception.Message
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    throw ('Save backup failed; retained at ' + $backupPath + '. ' + $_.Exception.Message)
}
