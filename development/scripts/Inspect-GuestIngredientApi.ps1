[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-ingredient-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$ingredientGameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $ingredientGameRoot 'BepInEx\core\Mono.Cecil.dll')

# Read declarations and generated wrapper IL without loading game assemblies.
function IngredientTypes($types) {
    foreach ($type in $types) { $type; if ($type.NestedTypes.Count) { IngredientTypes $type.NestedTypes } }
}
function IngredientBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    $calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{
        RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match '::il2cpp_runtime_invoke\(' }).Count)
        NativeFieldProxy = [bool](@($fields | Where-Object { $_ -match '::NativeFieldInfoPtr_' }).Count)
        NativeObjectAllocation = [bool](@($calls | Where-Object { $_ -match '::il2cpp_object_new\(' }).Count)
        Calls = $calls; Fields = $fields
    }
}
function IngredientProperty($property) {
    [pscustomobject]@{
        Name = $property.Name; Type = $property.PropertyType.FullName
        Static = [bool]$property.GetMethod.IsStatic; Writable = $null -ne $property.SetMethod
        GetterSignature = $property.GetMethod.FullName; SetterSignature = $property.SetMethod.FullName
        Getter = (IngredientBody $property.GetMethod); Setter = (IngredientBody $property.SetMethod)
    }
}
function IngredientMethod($method) {
    [pscustomobject]@{
        Signature = $method.FullName; Visibility = $method.Attributes.ToString(); Static = $method.IsStatic
        Constructor = $method.IsConstructor; ReturnType = $method.ReturnType.FullName
        Parameters = @($method.Parameters | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Type=$_.ParameterType.FullName; Out=$_.IsOut } })
        Body = (IngredientBody $method)
    }
}
function IngredientType($type) {
    $properties = @($type.Properties)
    $methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter -and $_.Name -ne '.cctor' })
    if ($type.FullName -eq 'DataManager') {
        $properties = @($properties | Where-Object Name -eq '_IngredientsDataDic_k__BackingField')
        $methods = @($methods | Where-Object { $_.Name -in @('GetIngredients','ParsingIngredientsEntity') })
    }
    [pscustomobject]@{
        Name=$type.FullName; Base=$type.BaseType.FullName; CLRValueType=$type.IsValueType; Enum=$type.IsEnum
        Interfaces=@($type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
        GenericParameters=@($type.GenericParameters | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Constraints=@($_.Constraints | ForEach-Object { $_.ConstraintType.FullName }) } })
        Fields=@($type.Fields | Where-Object { !$_.Name.StartsWith('Native') } | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Type=$_.FieldType.FullName; Visibility=$_.Attributes.ToString(); Static=$_.IsStatic; Literal=$_.IsLiteral; Readonly=$_.IsInitOnly; Value=if($_.IsLiteral){$_.Constant}else{$null} } })
        Properties=@($properties | ForEach-Object { IngredientProperty $_ })
        Methods=@($methods | ForEach-Object { IngredientMethod $_ })
        MethodScope=if($type.FullName -eq 'DataManager'){'Only GetIngredients/ParsingIngredientsEntity and its direct ingredient dictionary.'}else{'All declared non-property methods except generated type initializer.'}
    }
}
function IngredientClosed($name, $mapping) {
    if ($null -eq $name) { return $null }
    foreach ($key in $mapping.Keys) { $name = [regex]::Replace($name, ('\b' + [regex]::Escape($key) + '\b'), [string]$mapping[$key]) }
    return $name
}

$ingredientWanted = @(
    'IngredientsStorage','IngredientsData','IngredientsType','DR.IngredientsEntity','DR.Ingredients',
    'DR.IngredientsCategoryType','DR.Items','DR.ItemCategoryType','DR.DesignSheetDataHelper`2',
    'DR.BaseSheetDataHelper','SingletonNoMono`1','DataManager','SushiBar.Place',
    'Il2CppSystem.Collections.Generic.Dictionary`2','Il2CppSystem.Collections.Generic.Dictionary`2/Entry',
    'Il2CppSystem.Collections.Generic.List`1','Il2CppSystem.Lazy`1','Il2CppSystem.DateTime','Il2CppSystem.Object',
    'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1',
    'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1'
)
$ingredientInputs = @(
    @{Name='Assembly-CSharp.dll';Folder='interop'}, @{Name='Il2Cppmscorlib.dll';Folder='interop'},
    @{Name='Il2CppSystem.Core.dll';Folder='interop'}, @{Name='Il2CppInterop.Runtime.dll';Folder='core'}
)
$ingredientAssemblies = @(); $ingredientRows = @(); $ingredientByName = @{}
foreach ($ingredientInput in $ingredientInputs) {
    $ingredientPath = Join-Path $ingredientGameRoot ('BepInEx\' + $ingredientInput.Folder + '\' + $ingredientInput.Name)
    $ingredientHash = (Get-FileHash -LiteralPath $ingredientPath -Algorithm SHA256).Hash
    $ingredientAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ingredientPath)
    try {
        foreach ($type in (IngredientTypes $ingredientAssembly.MainModule.Types | Where-Object { $_.FullName -in $ingredientWanted })) {
            if ($ingredientByName.ContainsKey($type.FullName)) { throw 'Duplicate ingredient metadata type.' }
            $row = IngredientType $type
            $ingredientRows += $row; $ingredientByName[$type.FullName] = $row
        }
    } finally { $ingredientAssembly.Dispose() }
    if ((Get-FileHash -LiteralPath $ingredientPath -Algorithm SHA256).Hash -ne $ingredientHash) { throw 'Input metadata changed during inspection.' }
    $ingredientAssemblies += [pscustomobject]@{Name=$ingredientInput.Name;SHA256=$ingredientHash}
}
$ingredientMissing = @($ingredientWanted | Where-Object { !$ingredientByName.ContainsKey($_) })
if ($ingredientMissing.Count) { throw ('Required ingredient metadata is missing: ' + ($ingredientMissing -join ', ')) }

$ingredientInheritance = @(); $ingredientCurrent = 'DR.IngredientsEntity'; $ingredientSeen = @{}
for ($ingredientDepth = 0; $ingredientDepth -lt 16; $ingredientDepth++) {
    if ($ingredientSeen.ContainsKey($ingredientCurrent) -or !$ingredientByName.ContainsKey($ingredientCurrent)) { throw 'Ingredient inheritance could not be resolved without guessing.' }
    $ingredientSeen[$ingredientCurrent] = $true; $row = $ingredientByName[$ingredientCurrent]
    $ingredientInheritance += [pscustomobject]@{
        Name=$row.Name; Base=$row.Base
        DirectInstanceFields=@($row.Properties | Where-Object { !$_.Static -and $_.Getter.NativeFieldProxy })
        DirectStaticFields=@($row.Properties | Where-Object { $_.Static -and $_.Getter.NativeFieldProxy })
        CLRInstanceFields=@($row.Fields | Where-Object { !$_.Static })
    }
    if ($ingredientCurrent -eq 'Il2CppSystem.Object') { break }
    $ingredientCurrent = ($row.Base -split '<')[0]
}
if ($ingredientCurrent -ne 'Il2CppSystem.Object') { throw 'Ingredient inheritance depth limit reached.' }
$ingredientClosedContexts = @(
    @{Definition='SingletonNoMono`1';Context='SingletonNoMono<IngredientsStorage>';Map=@{T='IngredientsStorage'}},
    @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<int,IngredientsData>';Map=@{TKey='System.Int32';TValue='IngredientsData'}},
    @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2/Entry';Context='Dictionary<int,IngredientsData>.Entry';Map=@{TKey='System.Int32';TValue='IngredientsData'}},
    @{Definition='Il2CppSystem.Collections.Generic.List`1';Context='List<string>';Map=@{T='System.String'}},
    @{Definition='Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1';Context='Il2CppStructArray<int>';Map=@{T='System.Int32'}}
)
$ingredientClosedRows = foreach ($context in $ingredientClosedContexts) {
    $row = $ingredientByName[$context.Definition]
    [pscustomobject]@{
        Context=$context.Context; Definition=$row.Name; Base=(IngredientClosed $row.Base $context.Map)
        Evidence='Metadata generic substitution only; no closed generic was instantiated or invoked.'
        Fields=@($row.Fields | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=(IngredientClosed $_.Type $context.Map);Static=$_.Static;Readonly=$_.Readonly} })
        Properties=@($row.Properties | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=(IngredientClosed $_.Type $context.Map);Static=$_.Static;Writable=$_.Writable;Getter=(IngredientClosed $_.GetterSignature $context.Map);GetterRuntimeInvoke=$_.Getter.RuntimeInvoke;GetterFieldProxy=$_.Getter.NativeFieldProxy;Setter=(IngredientClosed $_.SetterSignature $context.Map);SetterRuntimeInvoke=$_.Setter.RuntimeInvoke;SetterFieldProxy=$_.Setter.NativeFieldProxy} })
        Methods=@($row.Methods | Where-Object { $_.Constructor -or $_.Signature -match '::Add\(' } | ForEach-Object { IngredientClosed $_.Signature $context.Map })
    }
}
$ingredientReport = [pscustomobject]@{
    SchemaVersion=1;ScriptName='Inspect-GuestIngredientApi.ps1';GeneratedUtc=[DateTime]::UtcNow.ToString('o')
    GameCodeExecuted=$false;SavesReadOrModified=$false;NativeHooksInstalled=$false
    NativeConstructorAbiVerified=$false;ResourceGraphIsolated=$false;FullCacheIsolationVerified=$false
    Evidence='Offline generated metadata and wrapper IL classification only. No business getters, constructors, collections, native field reads or saves executed. Not original method bodies, runtime aliases, immutable resource or complete graph proof.'
    Assemblies=$ingredientAssemblies;MissingTypes=$ingredientMissing;Types=$ingredientRows
    EntityInheritance=$ingredientInheritance;ClosedMetadataContexts=@($ingredientClosedRows)
    EntityDirectInstanceFieldCount=@($ingredientInheritance | ForEach-Object DirectInstanceFields).Count
    EntityStaticResourceFields=@($ingredientInheritance | ForEach-Object DirectStaticFields)
    ParentResourceCandidates=@($ingredientByName['DR.Items'].Properties | Where-Object { !$_.Static -and $_.Getter.NativeFieldProxy -and $_.Type -match 'List|Dictionary|Array' })
}
$ingredientOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $ingredientOutputRoot
New-Item -ItemType Directory -Path $ingredientOutputRoot -Force | Out-Null
$ingredientOutput = Join-Path $ingredientOutputRoot $ReportName
Assert-NoPathLinks $ingredientOutput
$ingredientTemporary = Join-Path $ingredientOutputRoot ('ingredient-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $ingredientReport | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $ingredientTemporary -Encoding utf8
    Move-Item -LiteralPath $ingredientTemporary -Destination $ingredientOutput -Force
} finally {
    if (Test-Path -LiteralPath $ingredientTemporary) { Remove-Item -LiteralPath $ingredientTemporary }
}
[pscustomobject]@{
    ReportPath=$ingredientOutput;Assemblies=$ingredientAssemblies.Count;Types=$ingredientRows.Count
    EntityInheritanceTypes=$ingredientInheritance.Count;EntityDirectInstanceFields=$ingredientReport.EntityDirectInstanceFieldCount
    EntityStaticResourceFields=$ingredientReport.EntityStaticResourceFields.Count
    ParentMutableCandidates=$ingredientReport.ParentResourceCandidates.Count;ClosedMetadataContexts=@($ingredientClosedRows).Count
    MissingTypes=$ingredientMissing;GameCodeExecuted=$false;SavesReadOrModified=$false
} | ConvertTo-Json
