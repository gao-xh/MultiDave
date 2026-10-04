[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9_.-]+\.dll$')]
    [string]$AssemblyName = 'Assembly-CSharp.dll',
    [ValidatePattern('^[A-Za-z0-9_.-]+\.json$')]
    [string]$ReportName,
    [string[]]$TypeName = @('PlayerCharacter', 'BaseCharacter', 'InGameManager',
        'CameraManager', 'PerspectiveCameraManager', 'OrthographicCameraManager',
        'CharacterController2D', 'UserInput')
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
$assemblyPath = Join-Path $gameRoot ('BepInEx\interop\' + $AssemblyName)
if (!(Test-Path -LiteralPath $assemblyPath)) {
    throw "Interop assembly missing: $assemblyPath. Launch the configured game once first."
}
# Cecil reads metadata without executing game code or resolving its dependencies.
Add-Type -Path (Join-Path $gameRoot 'BepInEx\core\Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
try {
    $types = foreach ($name in $TypeName) {
        $type = $assembly.MainModule.Types | Where-Object FullName -eq $name
        if (!$type) { throw "Top-level type not found: $name in $AssemblyName" }
        [pscustomobject]@{
            Name = $type.FullName
            BaseType = $type.BaseType.FullName
            Properties = @($type.Properties | ForEach-Object {
                [pscustomobject]@{
                    Name = $_.Name
                    Type = $_.PropertyType.FullName
                    Static = [bool]$_.GetMethod.IsStatic
                    Readable = $null -ne $_.GetMethod
                    Writable = $null -ne $_.SetMethod
                }
            })
            Methods = @($type.Methods | Where-Object {
                !$_.IsGetter -and !$_.IsSetter -and !$_.IsConstructor
            } | ForEach-Object { $_.FullName })
        }
    }
    $report = [pscustomobject]@{
        SchemaVersion = 1
        GeneratedUtc = [DateTime]::UtcNow.ToString('o')
        AssemblyName = $AssemblyName
        AssemblySHA256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
        Evidence = 'Generated interop metadata; not original game method bodies.'
        Types = @($types)
    }
    $reportRoot = Join-Path $PSScriptRoot '..\.local\analysis'
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    if (!$ReportName) { $ReportName = $AssemblyName + '.api.json' }
    $reportPath = [IO.Path]::GetFullPath((Join-Path $reportRoot $ReportName))
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
    [pscustomobject]@{ ReportPath = $reportPath; TypeCount = @($types).Count } | ConvertTo-Json
} finally {
    $assembly.Dispose()
}
