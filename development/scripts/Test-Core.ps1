[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Resolve-DaveGamePath $GamePath
$runtimeRoot = Join-Path $gameRoot 'dotnet'
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$compilerPath = $null
foreach ($line in (& $dotnetCommand --list-sdks)) {
    if ($line -match '^(\S+) \[(.+)\]$') {
        $candidate = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
        if (Test-Path -LiteralPath $candidate) { $compilerPath = $candidate }
    }
}
if (!$compilerPath) { throw 'A .NET SDK with Roslyn is required for core tests.' }
$outputRoot = Join-Path $projectRoot 'artifacts\tests'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputDll = Join-Path $outputRoot 'DaveCoop.Core.Tests.dll'
$references = @(Get-ChildItem -LiteralPath $runtimeRoot -Filter 'System.*.dll' -File |
    Where-Object Name -notlike '*Native*.dll' | Select-Object -ExpandProperty FullName)
$references += @('mscorlib.dll', 'netstandard.dll') | ForEach-Object { Join-Path $runtimeRoot $_ }
$arguments = @('-nostdlib+', '-target:exe', '-langversion:9.0', '-deterministic+', '-warnaserror+', ('-out:"' + $outputDll + '"'))
$arguments += $references | ForEach-Object { '-reference:"' + $_ + '"' }
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\DaveCoop\Core') -Filter '*.cs' -File -Recurse |
    Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' }
$arguments += @('MapChoiceController.cs', 'MapSelectionCallObservation.cs', 'MapOriginSourceFrame.cs', 'CargoInventoryController.cs') | ForEach-Object {
    '"' + (Join-Path $projectRoot ('src\DaveCoop\Networking\' + $_)) + '"'
}
$arguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tests\DaveCoop.Core.Tests') -Filter '*.cs' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' }
$responsePath = Join-Path $outputRoot 'compile.rsp'
$arguments | Set-Content -LiteralPath $responsePath -Encoding utf8
& $dotnetCommand $compilerPath '-noconfig' ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Core test compilation failed ($LASTEXITCODE)." }
@{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = '6.0.0' } } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'DaveCoop.Core.Tests.runtimeconfig.json') -Encoding utf8
& $dotnetCommand $outputDll
if ($LASTEXITCODE -ne 0) { throw "Core tests failed ($LASTEXITCODE)." }
