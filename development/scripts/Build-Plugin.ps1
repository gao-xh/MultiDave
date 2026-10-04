[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = Resolve-DaveGamePath $GamePath
$coreRoot = Join-Path $gameRoot 'BepInEx\core'
$interopRoot = Join-Path $gameRoot 'BepInEx\interop'
$runtimeRoot = Join-Path $gameRoot 'dotnet'
$dotnetCommand = (Get-Command dotnet -ErrorAction Stop).Source
$sdkListing = & $dotnetCommand --list-sdks
if ($LASTEXITCODE -ne 0) { throw 'Cannot list installed .NET SDKs.' }
$compilerPath = $null
foreach ($sdkLine in $sdkListing) {
    if ($sdkLine -match '^(\S+) \[(.+)\]$') {
        $candidate = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
        if (Test-Path -LiteralPath $candidate) { $compilerPath = $candidate }
    }
}
if (!$compilerPath) { throw 'A .NET SDK with the Roslyn C# compiler is required.' }
# Use the loader's .NET 6 libraries so the existing SDK 5 compiler can compile offline.
$references = @(Get-ChildItem -LiteralPath $runtimeRoot -Filter 'System.*.dll' -File |
    Where-Object { $_.Name -notlike '*Native*.dll' } | Select-Object -ExpandProperty FullName)
foreach ($runtimeName in @('mscorlib.dll', 'netstandard.dll', 'Microsoft.CSharp.dll')) {
    $references += Join-Path $runtimeRoot $runtimeName
}
foreach ($coreName in @('BepInEx.Core.dll', 'BepInEx.Unity.IL2CPP.dll', 'Il2CppInterop.Runtime.dll', '0Harmony.dll')) {
    $references += Join-Path $coreRoot $coreName
}
foreach ($interopName in @('Il2Cppmscorlib.dll', 'Il2CppSystem.Core.dll', 'Assembly-CSharp.dll', 'Sirenix.Serialization.dll', 'UnityEngine.CoreModule.dll', 'UnityEngine.AnimationModule.dll', 'UnityEngine.Physics2DModule.dll', 'UnityEngine.InputLegacyModule.dll', 'UnityEngine.IMGUIModule.dll', 'Unity.ResourceManager.dll', 'Unity.Addressables.dll', 'spine-unity.dll')) {
    $references += Join-Path $interopRoot $interopName
}
foreach ($reference in $references) {
    if (!(Test-Path -LiteralPath $reference)) { throw "Missing reference: $reference. Launch BepInEx once to generate interop libraries." }
}
$outputRoot = Join-Path $projectRoot 'artifacts\plugin'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$outputDll = Join-Path $outputRoot 'DaveCoop.dll'
$responsePath = Join-Path $outputRoot 'compile.rsp'
$compilerArguments = @('-nostdlib+', '-target:library', '-langversion:9.0', '-deterministic+', '-debug:portable', '-optimize+', '-warnaserror+', ('-out:"' + $outputDll + '"'))
$compilerArguments += $references | ForEach-Object { '-reference:"' + $_ + '"' }
$compilerArguments += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\DaveCoop') -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' }
$compilerArguments | Set-Content -LiteralPath $responsePath -Encoding utf8
& $dotnetCommand $compilerPath '-noconfig' ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) { throw "Plugin compilation failed ($LASTEXITCODE)." }
Get-FileHash -LiteralPath $outputDll -Algorithm SHA256 | Select-Object Path, Hash
