[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-runtime-cache-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$guestCacheGame = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $guestCacheGame 'BepInEx\core\Mono.Cecil.dll')
# Read generated declarations and wrapper IL; no generated game types are loaded.
function CacheTypes($types) {
    foreach ($type in $types) { $type; if ($type.NestedTypes.Count) { CacheTypes $type.NestedTypes } }
}
function CacheBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    [pscustomobject]@{
        RuntimeInvoke = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'il2cpp_runtime_invoke' }).Count)
        FieldProxy = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeFieldInfoPtr_*' }).Count)
        Calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
        Fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    }
}
function CacheProperty($property) {
    [pscustomobject]@{
        Name = $property.Name; Type = $property.PropertyType.FullName
        Writable = $null -ne $property.SetMethod; Static = [bool]$property.GetMethod.IsStatic
        Getter = (CacheBody $property.GetMethod); Setter = (CacheBody $property.SetMethod)
    }
}
function CacheMethod($method) {
    [pscustomobject]@{ Signature = $method.FullName; Visibility = $method.Attributes.ToString(); Static = $method.IsStatic; Body = (CacheBody $method) }
}
$guestCacheAssemblyPath = Join-Path $guestCacheGame 'BepInEx\interop\Assembly-CSharp.dll'
$guestCacheBeforeHash = (Get-FileHash -LiteralPath $guestCacheAssemblyPath -Algorithm SHA256).Hash
$guestCacheAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($guestCacheAssemblyPath)
try {
    $guestCacheAllTypes = @(CacheTypes $guestCacheAssembly.MainModule.Types)
    $guestCacheByName = @{}
    foreach ($type in $guestCacheAllTypes) { $guestCacheByName[$type.FullName] = $type }
    $guestCacheDescendants = foreach ($type in $guestCacheAllTypes) {
        $current = $type; $chain = [System.Collections.Generic.List[string]]::new()
        for ($i = 0; $i -lt 16 -and $current.BaseType; $i++) {
            $chain.Add($current.BaseType.FullName)
            if (!$guestCacheByName.ContainsKey($current.BaseType.FullName)) { break }
            $current = $guestCacheByName[$current.BaseType.FullName]
        }
        if ($chain.Contains('InGameSaveData')) { $type.FullName }
    }
    $guestCacheWanted = @('IngredientsStorage','IngredientsData','IngredientsSave','IngameSaveDataManager','InGameSaveData','LootBox','LootBoxSlot','MissionManager','MissionData','MissionDataSave','MissionHandlerAdaptee','MissionTaskData','MissionConditionData','MissionProcessData','MissionSequenceQueue','SaveData/SaveDataMissionManager','InGameSaveType','SushiBar.Place','DR.Save.SaveSystemPlayerDataManager/InstanceInteractionData') + @($guestCacheDescendants)
    $guestCacheTypeRows = foreach ($type in $guestCacheAllTypes | Where-Object { $_.FullName -in $guestCacheWanted }) {
        [pscustomobject]@{
            Type = $type.FullName; Base = $type.BaseType.FullName; IsValueType = $type.IsValueType
            Properties = @($type.Properties | ForEach-Object { CacheProperty $_ })
            Fields = @($type.Fields | Where-Object { !$_.IsStatic } | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Type=$_.FieldType.FullName } })
            Literals = @($type.Fields | Where-Object IsLiteral | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Value=$_.Constant } })
            Methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter -and $_.Name -ne '.cctor' } | ForEach-Object { CacheMethod $_ })
        }
    }
    $guestCacheReferences = foreach ($type in $guestCacheAllTypes) {
        foreach ($property in $type.Properties | Where-Object { $_.PropertyType.FullName -match '(^|[<,])(IngredientsData|IngredientsStorage|MissionData|MissionManager|MissionHandlerAdaptee|InGameSaveData|IngameSaveDataManager|LootBox|LootBoxSlot)([>,]|$)' }) {
            [pscustomobject]@{ Owner=$type.FullName; Name=$property.Name; Type=$property.PropertyType.FullName; Getter=(CacheBody $property.GetMethod) }
        }
    }
    $guestCacheSavePaths = foreach ($type in $guestCacheAllTypes | Where-Object { $_.FullName -eq 'SaveData' -or $_.FullName -eq 'DR.Save.SaveSystemPlayerDataManager/InstanceInteractionData' }) {
        foreach ($property in $type.Properties | Where-Object { $_.Name -match 'Ingredients|Mission|Box|InGame|Cache|Data|Dict|List' }) {
            [pscustomobject]@{ Owner=$type.FullName; Name=$property.Name; Type=$property.PropertyType.FullName; Getter=(CacheBody $property.GetMethod) }
        }
    }
} finally { $guestCacheAssembly.Dispose() }
if ((Get-FileHash -LiteralPath $guestCacheAssemblyPath -Algorithm SHA256).Hash -ne $guestCacheBeforeHash) { throw 'Interop assembly changed during metadata read.' }
$guestCacheCollections = @(); $guestCacheAssemblyHashes = @([pscustomobject]@{ Name='Assembly-CSharp.dll'; SHA256=$guestCacheBeforeHash })
foreach ($guestCacheAssemblyName in @('Il2Cppmscorlib.dll','Il2CppInterop.Runtime.dll')) {
    $guestCacheAdditionalPath = if ($guestCacheAssemblyName -eq 'Il2CppInterop.Runtime.dll') { Join-Path $guestCacheGame ('BepInEx\core\'+$guestCacheAssemblyName) } else { Join-Path $guestCacheGame ('BepInEx\interop\'+$guestCacheAssemblyName) }
    $guestCacheAdditionalHash = (Get-FileHash -LiteralPath $guestCacheAdditionalPath -Algorithm SHA256).Hash
    $guestCacheAdditionalAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($guestCacheAdditionalPath)
    try {
        foreach ($type in (CacheTypes $guestCacheAdditionalAssembly.MainModule.Types) | Where-Object { $_.FullName -in @('Il2CppSystem.DateTime','Il2CppSystem.Collections.Generic.Dictionary`2','Il2CppSystem.Collections.Generic.Dictionary`2/Entry','Il2CppSystem.Collections.Generic.List`1','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1') }) {
            $guestCacheCollections += [pscustomobject]@{
                Type=$type.FullName; Base=$type.BaseType.FullName; IsValueType=$type.IsValueType
                Properties=@($type.Properties | ForEach-Object { CacheProperty $_ })
                Fields=@($type.Fields | Where-Object { !$_.IsStatic } | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Type=$_.FieldType.FullName } })
                Constructors=@($type.Methods | Where-Object Name -eq '.ctor' | ForEach-Object { CacheMethod $_ })
            }
        }
    } finally { $guestCacheAdditionalAssembly.Dispose() }
    if ((Get-FileHash -LiteralPath $guestCacheAdditionalPath -Algorithm SHA256).Hash -ne $guestCacheAdditionalHash) { throw 'Collection metadata changed during read.' }
    $guestCacheAssemblyHashes += [pscustomobject]@{ Name=$guestCacheAssemblyName; SHA256=$guestCacheAdditionalHash }
}
$guestCacheReport = [pscustomobject]@{
    SchemaVersion=1; ScriptName='Inspect-GuestRuntimeCacheApi.ps1'; GeneratedUtc=[DateTime]::UtcNow.ToString('o')
    AssemblySHA256=$guestCacheBeforeHash; Assemblies=$guestCacheAssemblyHashes
    GameCodeExecuted=$false; SavesReadOrModified=$false; NativeHooksInstalled=$false
    FullCacheIsolationVerified=$false; NativeCloneAbiVerified=$false
    Evidence='Offline generated wrapper declarations and IL only. Direct field proxies still require native reads at runtime. Listed references are signature candidates, not proven live aliases; constructor signatures do not prove side-effect-free deep copying.'
    Types=@($guestCacheTypeRows); InGameSaveDataDescendants=@($guestCacheDescendants)
    References=@($guestCacheReferences); SaveSubtreePaths=@($guestCacheSavePaths); CollectionTypes=$guestCacheCollections
}
$guestCacheOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $guestCacheOutputRoot
New-Item -ItemType Directory -Path $guestCacheOutputRoot -Force | Out-Null
$guestCacheOutput = Join-Path $guestCacheOutputRoot $ReportName
Assert-NoPathLinks $guestCacheOutput
$guestCacheReport | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $guestCacheOutput -Encoding utf8
[pscustomobject]@{ Types=@($guestCacheTypeRows).Count; Descendants=@($guestCacheDescendants).Count; References=@($guestCacheReferences).Count; Collections=$guestCacheCollections.Count; Output=$guestCacheOutput } | ConvertTo-Json
