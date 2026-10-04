[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-interaction-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $gameRoot 'BepInEx\core\Mono.Cecil.dll')

# Cecil reads metadata and wrapper IL without loading or executing game types.
function AllInteractionTypes($values) {
    foreach ($type in $values) { $type; if ($type.NestedTypes.Count) { AllInteractionTypes $type.NestedTypes } }
}
function InteractionBodyFacts($method) {
    if ($null -eq $method -or !$method.HasBody) { return $null }
    $calls = @($method.Body.Instructions | Where-Object { $_.OpCode.FlowControl.ToString() -eq 'Call' } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fields = @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{
        RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match 'il2cpp_runtime_invoke' }).Count)
        NativeFieldProxy = [bool](@($fields | Where-Object { $_ -match 'NativeFieldInfoPtr_' }).Count)
        Calls = $calls; Fields = $fields
    }
}
$wanted = @(
    'DR.Save.SavePlayerData', 'DR.Save.SaveSystemPlayerDataManager', 'DR.Save.SaveSystemPlayerDataManager/InstanceInteractionData',
    'DR.Save.PlayerInstalledCargoBoxData', 'DR.Save.PlayerInstalledSensorDeviceData',
    'DR.Save.PlayerInstalledTriggerDeviceData', 'DR.Save.PlayerInstalledFunctionalDeviceData',
    'DR.Save.PlayerInteractionObjectData', 'DR.Save.PlayerCrabTrapData', 'DR.Save.PlayerRandomActivatorData',
    'DR.Save.PlayerInstalledDeviceDataBase', 'CargoSlot',
    'Il2CppSystem.Collections.Generic.List`1', 'Il2CppSystem.Collections.Generic.Dictionary`2',
    'Il2CppSystem.Collections.Generic.Dictionary`2/Entry', 'Il2CppSystem.Collections.Generic.HashSet`1',
    'Il2CppSystem.Collections.Generic.HashSet`1/Slot', 'Il2CppSystem.DateTime', 'UnityEngine.Vector2', 'UnityEngine.Vector3',
    'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase', 'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1', 'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1'
)
$inputs = @(
    @{ Name = 'Assembly-CSharp.dll'; Folder = 'interop' },
    @{ Name = 'Il2Cppmscorlib.dll'; Folder = 'interop' },
    @{ Name = 'Il2CppSystem.Core.dll'; Folder = 'interop' },
    @{ Name = 'UnityEngine.CoreModule.dll'; Folder = 'interop' },
    @{ Name = 'Il2CppInterop.Runtime.dll'; Folder = 'core' }
)
$reports = foreach ($inputAssembly in $inputs) {
    $path = Join-Path $gameRoot ('BepInEx\' + $inputAssembly.Folder + '\' + $inputAssembly.Name)
    $before = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        $types = foreach ($type in (AllInteractionTypes $assembly.MainModule.Types | Where-Object { $_.FullName -in $wanted })) {
            [pscustomobject]@{
                Name = $type.FullName; Base = $type.BaseType.FullName
                GenericParameters = @($type.GenericParameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Attributes = $_.Attributes.ToString(); Constraints = @($_.Constraints | ForEach-Object { $_.ConstraintType.FullName }) } })
                Fields = @($type.Fields | Where-Object { !$_.Name.StartsWith('Native') } | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.FieldType.FullName; Visibility = $_.Attributes.ToString(); Static = $_.IsStatic; Literal = $_.IsLiteral } })
                Properties = @($type.Properties | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.PropertyType.FullName; Getter = (InteractionBodyFacts $_.GetMethod); Setter = (InteractionBodyFacts $_.SetMethod) } })
                Methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter } | ForEach-Object { [pscustomobject]@{ Signature = $_.FullName; Visibility = $_.Attributes.ToString(); Body = (InteractionBodyFacts $_) } })
            }
        }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $before) { throw 'An input assembly changed during metadata inspection.' }
        [pscustomobject]@{ Assembly = $inputAssembly.Name; SHA256 = $before; Types = @($types) }
    } finally { $assembly.Dispose() }
}
$seen = @($reports | ForEach-Object Types | ForEach-Object Name)
$report = [pscustomobject]@{
    SchemaVersion = 1
    ScriptName = 'Inspect-GuestInteractionApi.ps1'
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    GameCodeExecuted = $false
    SavesReadOrModified = $false
    Evidence = 'Offline generated wrapper/runtime metadata and IL only. Native field proxy classification is not a native ABI, alias, synchronization or isolation test.'
    MissingTypes = @($wanted | Where-Object { $_ -notin $seen })
    Assemblies = @($reports)
}
$reportRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $reportRoot
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$reportPath = Join-Path $reportRoot $ReportName
Assert-NoPathLinks $reportPath
$temporaryPath = Join-Path $reportRoot ('interaction-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $temporaryPath -Encoding utf8
    Move-Item -LiteralPath $temporaryPath -Destination $reportPath -Force
} finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}
[pscustomobject]@{ ReportPath = $reportPath; Assemblies = @($reports).Count; Types = $seen.Count; MissingTypes = $report.MissingTypes } | ConvertTo-Json
