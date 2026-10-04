[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-comparer-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$comparerGameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $comparerGameRoot 'BepInEx\core\Mono.Cecil.dll')

# Generated wrapper declarations/IL only; no game/runtime type initialization.
function ComparerTypes($types) {
    foreach ($type in $types) { $type; if ($type.NestedTypes.Count) { ComparerTypes $type.NestedTypes } }
}
function ComparerBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    $calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{
        RuntimeInvoke=[bool](@($calls | Where-Object { $_ -match '::il2cpp_runtime_invoke\(' }).Count)
        NativeFieldProxy=[bool](@($fields | Where-Object { $_ -match '::NativeFieldInfoPtr_' }).Count)
        NativeObjectAllocation=[bool](@($calls | Where-Object { $_ -match '::il2cpp_object_new\(' }).Count)
        AssignableTypeCheck=[bool](@($calls | Where-Object { $_ -match '::il2cpp_class_is_assignable_from\(' }).Count)
        Calls=$calls;Fields=$fields
    }
}
function ComparerProperty($property) {
    [pscustomobject]@{
        Name=$property.Name;Type=$property.PropertyType.FullName;Static=[bool]$property.GetMethod.IsStatic
        Writable=$null -ne $property.SetMethod;GetterSignature=$property.GetMethod.FullName;SetterSignature=$property.SetMethod.FullName
        Getter=(ComparerBody $property.GetMethod);Setter=(ComparerBody $property.SetMethod)
    }
}
function ComparerMethod($method) {
    [pscustomobject]@{
        Signature=$method.FullName;Public=$method.IsPublic;Static=$method.IsStatic;Virtual=$method.IsVirtual;Abstract=$method.IsAbstract
        Constructor=$method.IsConstructor;PInvoke=$method.IsPInvokeImpl;ReturnType=$method.ReturnType.FullName
        Parameters=@($method.Parameters | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=$_.ParameterType.FullName;Out=$_.IsOut} })
        Body=(ComparerBody $method)
        ConstructorCallOrder=if($method.Name -eq '.ctor' -and $method.HasBody){@($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })}else{@()}
    }
}
function ComparerTypeRow($type) {
    $methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter })
    if ($type.FullName -in @('Il2CppSystem.String','Il2CppSystem.Int32','Il2CppSystem.Enum')) {
        $methods = @($methods | Where-Object { $_.Name -in @('.ctor','.cctor','Equals','GetHashCode','CompareTo','GetTypeCode') })
    }
    if ($type.FullName -eq 'Il2CppInterop.Runtime.IL2CPP') {
        $methods = @($methods | Where-Object { $_.Name -in @('il2cpp_object_new','il2cpp_object_get_class','il2cpp_class_is_assignable_from','il2cpp_gchandle_new','il2cpp_gchandle_get_target','il2cpp_gchandle_free','il2cpp_runtime_class_init') })
    }
    if ($type.FullName -eq 'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase') {
        $methods = @($methods | Where-Object { $_.Name -in @('.ctor','TryCast','Cast','CreateGCHandle','Finalize') })
    }
    [pscustomobject]@{
        Name=$type.FullName;Base=$type.BaseType.FullName;CLRValueType=$type.IsValueType;Enum=$type.IsEnum
        CLRInterface=$type.IsInterface;CLRAbstract=$type.IsAbstract;Layout=$type.Attributes.ToString()
        Interfaces=@($type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
        GenericParameters=@($type.GenericParameters | ForEach-Object { [pscustomobject]@{Name=$_.Name;Attributes=$_.Attributes.ToString();Constraints=@($_.Constraints | ForEach-Object { $_.ConstraintType.FullName })} })
        # Include generated static info caches as declarations, never their values.
        Fields=@($type.Fields | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=$_.FieldType.FullName;Visibility=$_.Attributes.ToString();Static=$_.IsStatic;Public=$_.IsPublic;Readonly=$_.IsInitOnly;Literal=$_.IsLiteral;Value=if($_.IsLiteral){$_.Constant}else{$null};GeneratedNativeInfoCache=$_.Name -match '^Native(Field|Method)InfoPtr_'} })
        Properties=@($type.Properties | ForEach-Object { ComparerProperty $_ })
        Methods=@($methods | ForEach-Object { ComparerMethod $_ })
        Evidence='All declared fields/properties; wrapper methods only. No runtime native type, constructor or equality/hash behaviour proof.'
    }
}
function ComparerClosedName($name, $mapping) {
    if ($null -eq $name) { return $null }
    foreach ($key in $mapping.Keys) { $name = [regex]::Replace($name, ('\b'+[regex]::Escape($key)+'\b'), [string]$mapping[$key]) }
    return $name
}

$comparerRequired = @(
    'Il2CppSystem.Collections.Generic.IEqualityComparer`1','Il2CppSystem.Collections.Generic.EqualityComparer`1',
    'Il2CppSystem.Collections.Generic.GenericEqualityComparer`1','Il2CppSystem.Collections.Generic.ObjectEqualityComparer`1',
    'Il2CppSystem.Collections.Generic.EnumEqualityComparer`1','Il2CppSystem.Collections.Generic.SByteEnumEqualityComparer`1',
    'Il2CppSystem.Collections.Generic.ShortEnumEqualityComparer`1','Il2CppSystem.Collections.Generic.LongEnumEqualityComparer`1',
    'Il2CppSystem.Collections.Generic.NullableEqualityComparer`1','Il2CppSystem.Collections.Generic.ByteEqualityComparer',
    'Il2CppSystem.Collections.IEqualityComparer','Il2CppSystem.IEquatable`1','Il2CppSystem.Collections.Generic.Dictionary`2',
    'Il2CppSystem.StringComparer','Il2CppSystem.OrdinalComparer','Il2CppSystem.OrdinalCaseSensitiveComparer',
    'Il2CppSystem.OrdinalIgnoreCaseComparer','Il2CppSystem.CultureAwareComparer',
    'Il2CppSystem.Int32','Il2CppSystem.String','Il2CppSystem.Enum','Il2CppSystem.Object','Il2CppSystem.ValueType',
    'InGameSaveType','Il2CppInterop.Runtime.IL2CPP','Il2CppInterop.Runtime.Il2CppClassPointerStore`1','Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase'
)
$comparerInputs = @(@{Name='Il2Cppmscorlib.dll';Folder='interop'},@{Name='Assembly-CSharp.dll';Folder='interop'},@{Name='Il2CppInterop.Runtime.dll';Folder='core'})
$comparerAssemblies = @(); $comparerHashes = @(); $comparerByName = @{}
try {
    foreach ($inputAssembly in $comparerInputs) {
        $path = Join-Path $comparerGameRoot ('BepInEx\'+$inputAssembly.Folder+'\'+$inputAssembly.Name)
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
        $comparerAssemblies += $assembly; $comparerHashes += [pscustomobject]@{Name=$inputAssembly.Name;SHA256=$hash;Path=$path}
        foreach ($type in (ComparerTypes $assembly.MainModule.Types | Where-Object { $_.FullName -in $comparerRequired })) {
            if ($comparerByName.ContainsKey($type.FullName)) { throw ('Required comparer declaration is ambiguous: '+$type.FullName) }
            $comparerByName[$type.FullName] = ComparerTypeRow $type
        }
    }
    $comparerMissing = @($comparerRequired | Where-Object { !$comparerByName.ContainsKey($_) })
    if ($comparerMissing.Count) { throw ('Required comparer metadata is missing: '+($comparerMissing -join ', ')) }
    $comparerFamilies = @('GenericEqualityComparer','ObjectEqualityComparer','EnumEqualityComparer','SByteEnumEqualityComparer','ShortEnumEqualityComparer','LongEnumEqualityComparer','NullableEqualityComparer')
    $comparerInheritance = foreach ($family in $comparerFamilies) {
        $current = 'Il2CppSystem.Collections.Generic.'+$family+'`1'; $seen = @{}; $chain = @()
        for ($depth = 0; $depth -lt 16; $depth++) {
            if ($seen.ContainsKey($current) -or !$comparerByName.ContainsKey($current)) { throw 'Comparer inheritance is unresolved or cyclic.' }
            $seen[$current] = $true; $row = $comparerByName[$current]
            $chain += [pscustomobject]@{
                Name=$row.Name;Base=$row.Base
                CLRInstanceFields=@($row.Fields | Where-Object { !$_.Static })
                CLRStaticFields=@($row.Fields | Where-Object { $_.Static -and !$_.GeneratedNativeInfoCache })
                NativeInstanceFieldProxies=@($row.Properties | Where-Object { !$_.Static -and $_.Getter.NativeFieldProxy })
                NativeStaticFieldProxies=@($row.Properties | Where-Object { $_.Static -and $_.Getter.NativeFieldProxy })
            }
            if ($current -eq 'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase') { break }
            $current = ($row.Base -split '<')[0]
        }
        if ($current -ne 'Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase') { throw 'Comparer inheritance depth limit reached.' }
        [pscustomobject]@{Family=$family;Chain=$chain;Evidence='Generated declared inheritance only; absence of native instance field proxies does not prove immutable/stateless behaviour.'}
    }
    $comparerClosedContexts = @()
    foreach ($key in @(@{Label='int';Type='System.Int32'},@{Label='string';Type='System.String'},@{Label='InGameSaveType';Type='InGameSaveType'})) {
        foreach ($family in @('IEqualityComparer','EqualityComparer','GenericEqualityComparer','ObjectEqualityComparer','EnumEqualityComparer')) {
            $comparerClosedContexts += @{Definition=('Il2CppSystem.Collections.Generic.'+$family+'`1');Context=($family+'<'+$key.Label+'>');Map=@{T=$key.Type}}
        }
    }
    $comparerClosedContexts += @(
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<int,IngredientsData>';Map=@{TKey='System.Int32';TValue='IngredientsData'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<InGameSaveType,InGameSaveData>';Map=@{TKey='InGameSaveType';TValue='InGameSaveData'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<string,bool>';Map=@{TKey='System.String';TValue='System.Boolean'}},
        @{Definition='Il2CppSystem.Collections.Generic.Dictionary`2';Context='Dictionary<string,InGameObjectSaveData.Data>';Map=@{TKey='System.String';TValue='InGameObjectSaveData/Data'}}
    )
    $comparerClosedRows = foreach ($context in $comparerClosedContexts) {
        $row = $comparerByName[$context.Definition]
        [pscustomobject]@{
            Context=$context.Context;Definition=$row.Name;Base=(ComparerClosedName $row.Base $context.Map)
            GenericConstraints=$row.GenericParameters
            Evidence='Metadata substitution only; not a supported instantiation, native AOT presence, construction, exact runtime type or default-selection proof.'
            Fields=@($row.Fields | Where-Object { !$_.GeneratedNativeInfoCache } | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=(ComparerClosedName $_.Type $context.Map);Static=$_.Static;Readonly=$_.Readonly} })
            Properties=@($row.Properties | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=(ComparerClosedName $_.Type $context.Map);Static=$_.Static;Writable=$_.Writable;GetterFieldProxy=$_.Getter.NativeFieldProxy;GetterRuntimeInvoke=$_.Getter.RuntimeInvoke;SetterFieldProxy=$_.Setter.NativeFieldProxy;SetterRuntimeInvoke=$_.Setter.RuntimeInvoke} })
            Methods=@($row.Methods | Where-Object { $_.Constructor -or $_.Signature -match '::(CreateComparer|Equals|GetHashCode)\(' } | ForEach-Object { ComparerClosedName $_.Signature $context.Map })
        }
    }
    foreach ($inputHash in $comparerHashes) {
        if ((Get-FileHash -LiteralPath $inputHash.Path -Algorithm SHA256).Hash -ne $inputHash.SHA256) { throw 'Input metadata changed during comparer inspection.' }
    }
    $comparerReport = [pscustomobject]@{
        SchemaVersion=1;ScriptName='Inspect-GuestComparerApi.ps1';GeneratedUtc=[DateTime]::UtcNow.ToString('o')
        GameCodeExecuted=$false;SavesReadOrModified=$false;NativeHooksInstalled=$false;NativeConstructorAbiVerified=$false
        DefaultSelectionVerified=$false;ReadonlyComparerVerified=$false;CompleteGraphVerified=$false;GuestStateIsolated=$false
        Evidence='Offline generated declarations and wrapper IL classification. Default/CreateComparer are native method candidates; original branch selection, actual native subtype, hash behaviour and deep isolation are unknown.'
        Assemblies=@($comparerHashes | Select-Object Name,SHA256);MissingTypes=$comparerMissing
        Types=@($comparerByName.Values | Sort-Object Name);ComparerInheritance=@($comparerInheritance);ClosedMetadataContexts=@($comparerClosedRows)
        SelectionCandidates=@($comparerByName['Il2CppSystem.Collections.Generic.EqualityComparer`1'].Methods | Where-Object { $_.Signature -match '::CreateComparer\(' })
        DefaultGetter=@($comparerByName['Il2CppSystem.Collections.Generic.EqualityComparer`1'].Properties | Where-Object Name -eq 'Default')
        DefaultNativeField=@($comparerByName['Il2CppSystem.Collections.Generic.EqualityComparer`1'].Properties | Where-Object Name -eq 'defaultComparer')
        DictionaryConstructors=@($comparerByName['Il2CppSystem.Collections.Generic.Dictionary`2'].Methods | Where-Object { $_.Constructor -and !$_.Static })
    }
    $comparerOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
    Assert-NoPathLinks $comparerOutputRoot
    New-Item -ItemType Directory -Path $comparerOutputRoot -Force | Out-Null
    $comparerOutput = Join-Path $comparerOutputRoot $ReportName
    Assert-NoPathLinks $comparerOutput
    $comparerTemporary = Join-Path $comparerOutputRoot ('comparer-'+[Guid]::NewGuid().ToString('N')+'.tmp')
    try {
        $comparerReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $comparerTemporary -Encoding utf8
        Move-Item -LiteralPath $comparerTemporary -Destination $comparerOutput -Force
    } finally {
        if (Test-Path -LiteralPath $comparerTemporary) { Remove-Item -LiteralPath $comparerTemporary }
    }
    [pscustomobject]@{ReportPath=$comparerOutput;Assemblies=$comparerHashes.Count;Types=$comparerByName.Count;InheritanceFamilies=@($comparerInheritance).Count;ClosedMetadataContexts=@($comparerClosedRows).Count;DictionaryConstructors=$comparerReport.DictionaryConstructors.Count;MissingTypes=$comparerMissing;GameCodeExecuted=$false;SavesReadOrModified=$false;DefaultSelectionVerified=$false} | ConvertTo-Json
} finally {
    foreach ($assembly in $comparerAssemblies) { $assembly.Dispose() }
}
