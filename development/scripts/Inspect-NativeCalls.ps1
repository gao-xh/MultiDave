[CmdletBinding()]
param(
    [string]$GamePath,
    [Parameter(Mandatory = $true)]
    [ValidateCount(1, 16)]
    [string[]]$Method,
    [ValidateRange(0, 3)]
    [int]$Depth = 1,
    [ValidateRange(1, 256)]
    [int]$MaxMethods = 128,
    [ValidateRange(64, 32768)]
    [int]$MaxInstructions = 8192,
    [switch]$IncludeInstructions,
    [ValidateRange(1, 8192)]
    [int]$MaxInstructionTextPerMethod = 2048,
    [ValidateRange(1, 16384)]
    [int]$MaxInstructionTextTotal = 8192,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$')]
    [string]$ReportName = 'native-calls'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
foreach ($selector in $Method) {
    if ($selector -notmatch '^\S+::[^\s:]+$') { throw 'Use exact selectors in the form Namespace.Type::MethodName.' }
}
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Resolve-DaveGamePath $GamePath
$environment = & (Join-Path $PSScriptRoot 'Get-Environment.ps1') -GamePath $gameRoot | ConvertFrom-Json
if (!$environment.UnityVersion) { throw 'Cannot determine the installed Unity version from DaveTheDiver.exe.' }
$coreRoot = Join-Path $gameRoot 'BepInEx\core'
$runtimeRoot = Join-Path $gameRoot 'dotnet'
$binaryPath = Join-Path $gameRoot 'GameAssembly.dll'
$metadataPath = Join-Path $gameRoot 'DaveTheDiver_Data\il2cpp_data\Metadata\global-metadata.dat'
foreach ($required in @($binaryPath, $metadataPath, (Join-Path $runtimeRoot 'System.Runtime.dll'))) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required local file is missing: $required" }
}
$parserNames = @('AssetRipper.Primitives.dll', 'WasmDisassembler.dll', 'Iced.dll', 'LibCpp2IL.dll')
foreach ($name in $parserNames) {
    if (!(Test-Path -LiteralPath (Join-Path $coreRoot $name) -PathType Leaf)) { throw "The installed loader lacks $name. This tool does not download dependencies." }
}
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$compilerPath = $null
$sdkListing = & $dotnetCommand --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Cannot list installed .NET SDKs.' }
foreach ($line in $sdkListing) {
    if ($line -match '^(\S+) \[(.+)\]$') {
        $candidate = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $compilerPath = $candidate }
    }
}
if (!$compilerPath) { throw 'A .NET SDK with Roslyn is required for offline native analysis.' }
$outputRoot = Join-Path $projectRoot ('.local\analysis\native-tool\' + [Guid]::NewGuid().ToString('N'))
Assert-NoPathLinks $outputRoot
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$references = @(Get-ChildItem -LiteralPath $runtimeRoot -Filter 'System.*.dll' -File |
    Where-Object Name -notlike '*Native*.dll' | Select-Object -ExpandProperty FullName)
$references += @('mscorlib.dll', 'netstandard.dll') | ForEach-Object { Join-Path $runtimeRoot $_ }
$references += $parserNames | ForEach-Object { Join-Path $coreRoot $_ }
$outputDll = Join-Path $outputRoot 'MultiDave.NativeCallInspector.dll'
$arguments = @('-nostdlib+', '-target:exe', '-langversion:9.0', '-deterministic+', '-warnaserror+', ('-out:"' + $outputDll + '"'))
$arguments += $references | ForEach-Object { '-reference:"' + $_ + '"' }
$arguments += '"' + (Join-Path $projectRoot 'tools\NativeCallInspector.cs') + '"'
$responsePath = Join-Path $outputRoot 'compile.rsp'
$arguments | Set-Content -LiteralPath $responsePath -Encoding utf8
& $dotnetCommand $compilerPath '-noconfig' ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Native analysis tool compilation failed ($LASTEXITCODE)." }
@{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = '6.0.0' } } } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'MultiDave.NativeCallInspector.runtimeconfig.json') -Encoding utf8
$reportPath = Join-Path $projectRoot ('.local\analysis\' + $ReportName + '.json')
Assert-NoPathLinks $reportPath
$requestPath = Join-Path $outputRoot 'request.json'
@{
    CorePath = $coreRoot; BinaryPath = $binaryPath; MetadataPath = $metadataPath
    UnityVersion = $environment.UnityVersion; Selectors = @($Method); OutputPath = $reportPath
    Depth = $Depth; MaxMethods = $MaxMethods; MaxInstructions = $MaxInstructions
    IncludeInstructions = [bool]$IncludeInstructions
    MaxInstructionTextPerMethod = $MaxInstructionTextPerMethod; MaxInstructionTextTotal = $MaxInstructionTextTotal
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $requestPath -Encoding utf8
& $dotnetCommand $outputDll $requestPath
if ($LASTEXITCODE -ne 0) { throw "Native call inspection failed ($LASTEXITCODE). Existing reports must not be treated as fresh evidence." }
Write-Output "Local report: $reportPath"
