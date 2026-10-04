[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-ingame-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$ingameGameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $ingameGameRoot 'BepInEx\core\Mono.Cecil.dll')

# Cecil reads declarations and generated wrapper IL, never loads game types.
function IngameTypes($types) {
    foreach ($type in $types) { $type; if ($type.NestedTypes.Count) { IngameTypes $type.NestedTypes } }
}
function IngameBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    $calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{
        RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match '::il2cpp_runtime_invoke\(' }).Count)
        NativeFieldProxy = [bool](@($fields | Where-Object { $_ -match '::NativeFieldInfoPtr_' }).Count)
        NativeObjectAllocation = [bool](@($calls | Where-Object { $_ -match '::il2cpp_object_new\(' }).Count)
        AssignableTypeCheck = [bool](@($calls | Where-Object { $_ -match '::il2cpp_class_is_assignable_from\(' }).Count)
        Calls = $calls; Fields = $fields
    }
}
function IngameProperty($property) {
    [pscustomobject]@{
        Name=$property.Name; Type=$property.PropertyType.FullName; Static=[bool]$property.GetMethod.IsStatic
        Writable=$null -ne $property.SetMethod; GetterSignature=$property.GetMethod.FullName; SetterSignature=$property.SetMethod.FullName
        Getter=(IngameBody $property.GetMethod); Setter=(IngameBody $property.SetMethod)
    }
}
function IngameMethod($method) {
    [pscustomobject]@{
        Signature=$method.FullName; Public=$method.IsPublic; Static=$method.IsStatic; Constructor=$method.IsConstructor
        PInvoke=$method.IsPInvokeImpl; ReturnType=$method.ReturnType.FullName
        Parameters=@($method.Parameters | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=$_.ParameterType.FullName;Out=$_.IsOut} })
        Body=(IngameBody $method)
    }
}
function IngameTypeRow($type) {
    $methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter })
    if ($type.FullName -eq 'Il2CppInterop.Runtime.IL2CPP') {
        $methods = @($methods | Where-Object { $_.Name -in @('il2cpp_object_get_class','il2cpp_object_new','il2cpp_class_is_assignable_from','il2cpp_class_is_valuetype','il2cpp_class_get_parent','il2cpp_gchandle_new','il2cpp_gchandle_get_target','il2cpp_gchandle_free') })
    }
    if ($type.FullName -eq 'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase') {
        $methods = @($methods | Where-Object { $_.Name -in @('TryCast','Cast','.ctor') })
    }
    [pscustomobject]@{
        Name=$type.FullName; Base=$type.BaseType.FullName; CLRValueType=$type.IsValueType; Enum=$type.IsEnum
        CLRInterface=$type.IsInterface; Abstract=$type.IsAbstract; Layout=$type.Attributes.ToString()
        GenericParameters=@($type.GenericParameters | ForEach-Object { $_.Name })
        Fields=@($type.Fields | Where-Object { $_.Name -notmatch '^Native(Field|Method)InfoPtr_' } | ForEach-Object {
            [pscustomobject]@{Name=$_.Name;Type=$_.FieldType.FullName;Static=$_.IsStatic;Public=$_.IsPublic;Readonly=$_.IsInitOnly;Literal=$_.IsLiteral;Value=if($_.IsLiteral){$_.Constant}else{$null};Offset=$_.Offset}
        })
        Properties=@($type.Properties | ForEach-Object { IngameProperty $_ })
        Methods=@($methods | ForEach-Object { IngameMethod $_ })
    }
}
function IngameReferenceNames($reference) {
    if ($null -eq $reference -or $reference.IsGenericParameter) { return }
    if ($reference -is [Mono.Cecil.GenericInstanceType]) {
        $reference.ElementType.FullName
        foreach ($argument in $reference.GenericArguments) { IngameReferenceNames $argument }
    } elseif ($reference -is [Mono.Cecil.TypeSpecification]) { IngameReferenceNames $reference.ElementType }
    else { $reference.FullName }
}
function IngameClosedName($name, $mapping) {
    if ($null -eq $name) { return $null }
    foreach ($key in $mapping.Keys) { $name = [regex]::Replace($name, ('\b'+[regex]::Escape($key)+'\b'), [string]$mapping[$key]) }
    return $name
}

$ingameInputs = @(
    @{Name='Assembly-CSharp.dll';Folder='interop'}, @{Name='Il2Cppmscorlib.dll';Folder='interop'},
    @{Name='UnityEngine.CoreModule.dll';Folder='interop'}, @{Name='Sirenix.Serialization.dll';Folder='interop'},
    @{Name='Il2CppInterop.Runtime.dll';Folder='core'}
)
$ingameAssemblies = @(); $ingameHashes = @(); $ingameTypeIndex = @{}; $ingameAmbiguous = @{}
try {
    foreach ($inputAssembly in $ingameInputs) {
        $path = Join-Path $ingameGameRoot ('BepInEx\'+$inputAssembly.Folder+'\'+$inputAssembly.Name)
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
        $ingameAssemblies += $assembly
        $ingameHashes += [pscustomobject]@{Name=$inputAssembly.Name;SHA256=$hash;Path=$path}
        foreach ($type in (IngameTypes $assembly.MainModule.Types)) {
            if ($type.Name -eq '<Module>' -or $type.FullName -match '^_PrivateImplementationDetails_($|/)') { continue }
            if ($ingameTypeIndex.ContainsKey($type.FullName)) { $ingameAmbiguous[$type.FullName] = $true; continue }
            $ingameTypeIndex[$type.FullName] = $type
        }
    }
    $ingameDerived = @(foreach ($candidate in $ingameTypeIndex.Values) {
        $current = $candidate
        for ($depth = 0; $depth -lt 16 -and $current.BaseType; $depth++) {
            if ($current.BaseType.FullName -eq 'InGameSaveData') { $candidate.FullName; break }
            if (!$ingameTypeIndex.ContainsKey($current.BaseType.FullName)) { break }
            $current = $ingameTypeIndex[$current.BaseType.FullName]
        }
    }) | Sort-Object
    $ingameSix = @('CharacterHealthData','CharacterEquipData','CharacterSubHelperData','CharacterInstallDeviceData','PuzzleStateSaveData','InGameObjectSaveData')
    if (@(Compare-Object $ingameSix $ingameDerived).Count) { throw 'InGameSaveData declarations differ from the six-type implementation contract.' }
    $ingameRequired = @('IngameSaveDataManager','SingletonNoMono`1','InGameSaveData','InGameSaveType','InGameSaveInstallDeviceType',
        'SubHelperSlotData','InstallDeviceSaveSlot','InGameObjectSaveData/Data','SubHelperSpecData','IInstalledDevice','SpecDataContainerBase',
        'Sirenix.OdinInspector.SerializedScriptableObject','Sirenix.Serialization.SerializationData','UnityEngine.ScriptableObject','UnityEngine.Object','UnityEngine.Vector3',
        'CodeStage.AntiCheat.ObscuredTypes.ObscuredInt','CodeStage.AntiCheat.ObscuredTypes.ObscuredFloat','CodeStage.AntiCheat.ObscuredTypes.ObscuredBool',
        'CodeStage.AntiCheat.ObscuredTypes.ObscuredVector3','CodeStage.AntiCheat.ObscuredTypes.ObscuredVector3/RawEncryptedVector3','CodeStage.AntiCheat.Common.ACTkByte4',
        'Il2CppSystem.Collections.Generic.Dictionary`2','Il2CppSystem.Collections.Generic.Dictionary`2/Entry','Il2CppSystem.Collections.Generic.List`1','Il2CppSystem.Collections.Generic.Queue`1',
        'Il2CppInterop.Runtime.IL2CPP','Il2CppInterop.Runtime.Il2CppClassPointerStore`1','Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase',
        'Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1','Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1') + $ingameSix
    $ingameMissing = @($ingameRequired | Where-Object { !$ingameTypeIndex.ContainsKey($_) })
    if ($ingameMissing.Count) { throw ('Required ingame metadata is missing: '+($ingameMissing -join ', ')) }
    # Include the declared polymorphic catalogue; unknown runtime subclasses are
    # still rejected. Follow ordinary child declarations, stop at Unity owners.
    $containerTypes = @($ingameTypeIndex.Values | Where-Object { $_.BaseType.FullName -eq 'SpecDataContainerBase' } | ForEach-Object FullName | Sort-Object)
    $selected = @{}; $pending = [System.Collections.Generic.Queue[string]]::new()
    foreach ($name in ($ingameRequired + $containerTypes)) { $pending.Enqueue($name) }
    $ingameFrontier = @(); $ingameEdges = @()
    while ($pending.Count) {
        $name = $pending.Dequeue()
        if ($selected.ContainsKey($name)) { continue }
        if ($ingameAmbiguous.ContainsKey($name)) { throw ('Selected ingame declaration is ambiguous across assemblies: '+$name) }
        if ($selected.Count -ge 192) { throw 'Ingame metadata closure exceeds its type limit.' }
        $type = $ingameTypeIndex[$name]; $row = IngameTypeRow $type; $selected[$name] = $row
        foreach ($property in $type.Properties) {
            $body = IngameBody $property.GetMethod
            if ($property.GetMethod.IsStatic -or !$body.NativeFieldProxy) { continue }
            $ingameEdges += [pscustomobject]@{Owner=$name;Field=$property.Name;Type=$property.PropertyType.FullName}
            foreach ($child in (IngameReferenceNames $property.PropertyType | Sort-Object -Unique)) {
                if ($child.StartsWith('System.') -or $child -eq 'Il2CppSystem.Object' -or $child -eq 'Il2CppSystem.ValueType') { continue }
                if (!$ingameTypeIndex.ContainsKey($child)) { $ingameFrontier += [pscustomobject]@{Owner=$name;Field=$property.Name;Type=$child;Reason='Declaration outside inspected assemblies.'}; continue }
                # Unity handles and resources require a separate engine bridge;
                # their presence does not authorize object_new or sharing.
                if ($child.StartsWith('UnityEngine.') -and $child -notin @('UnityEngine.Vector2','UnityEngine.Vector3','UnityEngine.Object')) {
                    $ingameFrontier += [pscustomobject]@{Owner=$name;Field=$property.Name;Type=$child;Reason='Engine/resource boundary; not an ordinary record clone.'}; continue
                }
                if ($child -match '^(GunSpecData|Command_SO|SpecDataBase|BuffDebuffEffectData)$') {
                    $ingameFrontier += [pscustomobject]@{Owner=$name;Field=$property.Name;Type=$child;Reason='Resource/behaviour subtree requires separate typed coverage.'}; continue
                }
                $pending.Enqueue($child)
            }
        }
    }
    $ingameClosed = @(
        @{Definition='SingletonNoMono`1';Context='SingletonNoMono<IngameSaveDataManager>';Map=@{T='IngameSaveDataManager'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<InGameSaveType,InGameSaveData>';Map=@{TKey='InGameSaveType';TValue='InGameSaveData'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<string,bool>';Map=@{TKey='System.String';TValue='System.Boolean'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<string,InGameObjectSaveData.Data>';Map=@{TKey='System.String';TValue='InGameObjectSaveData/Data'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2/Entry';Context='Dictionary<InGameSaveType,InGameSaveData>.Entry';Map=@{TKey='InGameSaveType';TValue='InGameSaveData'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2/Entry';Context='Dictionary<string,bool>.Entry';Map=@{TKey='System.String';TValue='System.Boolean'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2/Entry';Context='Dictionary<string,InGameObjectSaveData.Data>.Entry';Map=@{TKey='System.String';TValue='InGameObjectSaveData/Data'}},
        @{Definition='Il2CppSystem.Collections.Generic.List`1';Context='List<int>';Map=@{T='System.Int32'}},
        @{Definition='Il2CppSystem.Collections.Generic.List`1';Context='List<InstallDeviceSaveSlot>';Map=@{T='InstallDeviceSaveSlot'}},
        @{Definition='Il2CppSystem.Collections.Generic.Queue`1';Context='Queue<IInstalledDevice>';Map=@{T='IInstalledDevice'}},
        @{Definition='Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1';Context='Il2CppReferenceArray<SubHelperSlotData>';Map=@{T='SubHelperSlotData'}}
    )
    $ingameClosedRows = foreach ($context in $ingameClosed) {
        $row = $selected[$context.Definition]
        [pscustomobject]@{
            Context=$context.Context;Definition=$row.Name;Evidence='Generic metadata substitution only; no closed instance or native method executed.'
            Properties=@($row.Properties | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=(IngameClosedName $_.Type $context.Map);Static=$_.Static;Writable=$_.Writable;GetterFieldProxy=$_.Getter.NativeFieldProxy;GetterRuntimeInvoke=$_.Getter.RuntimeInvoke;SetterFieldProxy=$_.Setter.NativeFieldProxy;SetterRuntimeInvoke=$_.Setter.RuntimeInvoke} })
            Methods=@($row.Methods | Where-Object { $_.Constructor -or $_.Signature -match '::(Add|Enqueue)\(' } | ForEach-Object { IngameClosedName $_.Signature $context.Map })
        }
    }
    foreach ($inputHash in $ingameHashes) {
        if ((Get-FileHash -LiteralPath $inputHash.Path -Algorithm SHA256).Hash -ne $inputHash.SHA256) { throw 'Input metadata changed during inspection.' }
    }
    $ingameReport = [pscustomobject]@{
        SchemaVersion=1;ScriptName='Inspect-GuestIngameApi.ps1';GeneratedUtc=[DateTime]::UtcNow.ToString('o')
        GameCodeExecuted=$false;SavesReadOrModified=$false;NativeHooksInstalled=$false;NativeAllocationAbiVerified=$false
        ResourceGraphIsolated=$false;FullCacheIsolationVerified=$false;GuestStateIsolated=$false
        Evidence='Offline generated declarations and wrapper IL classification only; not original native control flow, live aliases, allocation validity or complete graph proof.'
        Assemblies=@($ingameHashes | Select-Object Name,SHA256);MissingTypes=$ingameMissing
        Types=@($selected.Values | Sort-Object Name);InGameSaveDataDescendants=$ingameDerived;SpecContainerDeclarations=$containerTypes
        DirectChildEdges=$ingameEdges;UnresolvedOrResourceFrontier=$ingameFrontier;ClosedMetadataContexts=@($ingameClosedRows)
    }
    $ingameOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
    Assert-NoPathLinks $ingameOutputRoot
    New-Item -ItemType Directory -Path $ingameOutputRoot -Force | Out-Null
    $ingameOutput = Join-Path $ingameOutputRoot $ReportName
    Assert-NoPathLinks $ingameOutput
    $ingameTemporary = Join-Path $ingameOutputRoot ('ingame-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try {
        $ingameReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $ingameTemporary -Encoding utf8
        Move-Item -LiteralPath $ingameTemporary -Destination $ingameOutput -Force
    } finally {
        if (Test-Path -LiteralPath $ingameTemporary) { Remove-Item -LiteralPath $ingameTemporary }
    }
    [pscustomobject]@{ReportPath=$ingameOutput;Assemblies=$ingameHashes.Count;Types=$selected.Count;Derived=$ingameDerived.Count;SpecContainers=$containerTypes.Count;DirectChildEdges=$ingameEdges.Count;Frontier=$ingameFrontier.Count;ClosedMetadataContexts=@($ingameClosedRows).Count;MissingTypes=$ingameMissing;GameCodeExecuted=$false;SavesReadOrModified=$false} | ConvertTo-Json
} finally {
    foreach ($assembly in $ingameAssemblies) { $assembly.Dispose() }
}
