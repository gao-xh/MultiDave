$ErrorActionPreference = 'Stop'

function Resolve-DaveGamePath {
    param([string]$GamePath)
    if ($GamePath) {
        $resolved = (Resolve-Path -LiteralPath $GamePath).Path
        if (!(Test-Path -LiteralPath (Join-Path $resolved 'DaveTheDiver.exe'))) { throw 'The selected directory does not contain DaveTheDiver.exe.' }
        return $resolved
    }
    $libraryRoots = @()
    foreach ($registryPath in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        if (Test-Path -LiteralPath $registryPath) {
            $steamValues = Get-ItemProperty -LiteralPath $registryPath
            foreach ($valueName in @('SteamPath', 'InstallPath')) {
                $property = $steamValues.PSObject.Properties[$valueName]
                if ($null -ne $property -and $property.Value) { $libraryRoots += [string]$property.Value }
            }
        }
    }
    foreach ($steamRoot in @($libraryRoots | Select-Object -Unique)) {
        $libraryFile = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile) {
            $libraryText = Get-Content -LiteralPath $libraryFile -Raw
            foreach ($pathMatch in [regex]::Matches($libraryText, '"path"\s+"([^"\r\n]+)"')) {
                $libraryRoots += $pathMatch.Groups[1].Value.Replace('\\', '\')
            }
        }
    }
    $matchesFound = @()
    foreach ($libraryRoot in @($libraryRoots | Select-Object -Unique)) {
        $manifestPath = Join-Path $libraryRoot 'steamapps\appmanifest_1868140.acf'
        if (Test-Path -LiteralPath $manifestPath) {
            $manifestText = Get-Content -LiteralPath $manifestPath -Raw
            $directoryMatch = [regex]::Match($manifestText, '"installdir"\s+"([^"\r\n]+)"')
            if ($directoryMatch.Success) {
                $candidate = Join-Path $libraryRoot ('steamapps\common\' + $directoryMatch.Groups[1].Value)
                if (Test-Path -LiteralPath (Join-Path $candidate 'DaveTheDiver.exe')) { $matchesFound += (Resolve-Path -LiteralPath $candidate).Path }
            }
        }
    }
    $matchesFound = @($matchesFound | Select-Object -Unique)
    if ($matchesFound.Count -eq 0) { throw 'Steam did not report an installed copy of Dave the Diver. Supply -GamePath.' }
    if ($matchesFound.Count -gt 1) { throw 'Multiple game installations were found. Supply -GamePath to select one.' }
    return $matchesFound[0]
}

function Assert-DaveGameClosed {
    if (Get-Process -Name DaveTheDiver -ErrorAction SilentlyContinue) { throw 'Save and exit Dave the Diver, then rerun the command. The script will not terminate your game.' }
}

function Assert-NoPathLinks {
    param([string]$Path)
    $checkedPath = [IO.Path]::GetFullPath($Path)
    while ($checkedPath) {
        if ((Test-Path -LiteralPath $checkedPath) -and ((Get-Item -LiteralPath $checkedPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Linked paths are not supported for installation: $checkedPath"
        }
        $parent = [IO.Directory]::GetParent($checkedPath)
        $checkedPath = if ($null -ne $parent) { $parent.FullName } else { $null }
    }
}

function Receive-VerifiedFile {
    param([string]$Uri, [string]$Path, [string]$SHA256)
    if ((Test-Path -LiteralPath $Path) -and $SHA256 -and (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -eq $SHA256) { return }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    $temporaryPath = $Path + '.download'
    try {
        Invoke-WebRequest -Uri $Uri -OutFile $temporaryPath -UseBasicParsing -TimeoutSec 180
        if ($SHA256 -and (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash -ne $SHA256) { throw "SHA256 mismatch for $Uri" }
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally { if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath } }
}

function Write-RunEvent {
    param([string]$LogPath, [string]$Event, [object]$Details)
    [pscustomobject]@{ Timestamp = [DateTimeOffset]::Now.ToString('o'); Event = $Event; Details = $Details } |
        ConvertTo-Json -Depth 8 -Compress | Add-Content -LiteralPath $LogPath -Encoding utf8
}
