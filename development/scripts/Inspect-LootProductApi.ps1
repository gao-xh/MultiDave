[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'

# Cecil reads generated declarations and wrapper IL only. This inspector never
# loads a game type into the CLR, invokes a game method, decrypts a slot or reads
# a save. Original native implementation purity and formulas remain unknown.
$productGameRoot = Resolve-DaveGamePath $GamePath
$productAssemblyPath = Join-Path $productGameRoot 'BepInEx/interop/Assembly-CSharp.dll'
$productHashBefore = (Get-FileHash -LiteralPath $productAssemblyPath -Algorithm SHA256).Hash
Add-Type -Path (Join-Path $productGameRoot 'BepInEx/core/Mono.Cecil.dll')
$productAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($productAssemblyPath)

function Get-ProductMethodEvidence {
    param([Mono.Cecil.MethodDefinition]$Method)
    $instructions = if ($Method.HasBody) { @($Method.Body.Instructions) } else { @() }
    $methodRefs = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } |
        ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fieldRefs = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } |
        ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $runtimeInvokes = @($instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match 'runtime_invoke|RuntimeInvoke'
    })
    $valueBoxes = @($instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -ceq 'il2cpp_value_box'
    })
    $structureLoads = @($instructions | Where-Object { $_.OpCode.Code.ToString() -ceq 'Ldobj' } |
        ForEach-Object { $_.Operand.FullName })
    [pscustomobject]@{
        Signature=$Method.FullName; Name=$Method.Name; Static=$Method.IsStatic; HasBody=$Method.HasBody
        Return=$Method.ReturnType.FullName
        Parameters=@($Method.Parameters | ForEach-Object {
            [pscustomobject]@{ Name=$_.Name; Type=$_.ParameterType.FullName; ByReference=$_.ParameterType.IsByReference }
        })
        RuntimeInvokeCalls=$runtimeInvokes.Count; ValueBoxCalls=$valueBoxes.Count
        StructureLoadTypes=$structureLoads; MethodReferences=$methodRefs; FieldReferences=$fieldRefs
        OriginalImplementationPureVerified=$false; OriginalSideEffectsVerified=$false
    }
}

function Get-ProductDirectProxy {
    param([Mono.Cecil.TypeDefinition]$Type, [string]$Name, [string]$ExpectedValueType)
    $properties = @($Type.Properties | Where-Object Name -ceq $Name)
    if ($properties.Count -ne 1 -or !$properties[0].GetMethod -or !$properties[0].GetMethod.HasBody) {
        throw ('Loot product direct field proxy missing: ' + $Type.FullName + '::' + $Name)
    }
    $property = $properties[0]
    if ($property.PropertyType.FullName -cne $ExpectedValueType) {
        throw ('Loot product field type mismatch: ' + $Type.FullName + '::' + $Name)
    }
    $evidence = Get-ProductMethodEvidence $property.GetMethod
    $pointerName = 'NativeFieldInfoPtr_' + $Name
    $nativePointers = @($property.GetMethod.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -ceq $pointerName -and
        $_.Operand.DeclaringType.FullName -ceq $Type.FullName
    })
    if ($evidence.RuntimeInvokeCalls -ne 0 -or $nativePointers.Count -ne 1) {
        throw ('Loot product getter is not the expected direct field proxy: ' + $Type.FullName + '::' + $Name)
    }
    [pscustomobject]@{
        Type=$Type.FullName; Property=$property.Name; ValueType=$property.PropertyType.FullName
        ValueTypeAssemblyScope=$property.PropertyType.Scope.Name
        NativeFieldPointer=$pointerName; DirectFieldProxyOnly=$true; Getter=$evidence
        ThreadAndNativeReadSafetyVerified=$false; FinalProductValueProven=$false
    }
}

try {
    $resourceNames = @('DR.Items','IntegratedItem')
    $obscuredName = 'CodeStage.AntiCheat.ObscuredTypes.ObscuredInt'
    $requiredNames = @($resourceNames) + @('LootBoxSlot', $obscuredName)
    $types = @{}
    foreach ($name in $requiredNames) {
        $type = $productAssembly.MainModule.GetType($name)
        if (!$type) { throw ('Loot product type missing from Assembly-CSharp.dll: ' + $name) }
        $types[$name] = $type
    }

    $resourceSpecs = @(
        @{ Field='_TID_k__BackingField'; Public='TID'; Value='System.Int32' },
        @{ Field='_ItemDataID_k__BackingField'; Public='ItemDataID'; Value='System.Int32' },
        @{ Field='_ItemGrade_k__BackingField'; Public='ItemGrade'; Value='System.Int32' },
        @{ Field='_ItemWeight_k__BackingField'; Public='ItemWeight'; Value='System.Single' }
    )
    $resources = @(foreach ($name in $resourceNames) {
        $type = $types[$name]
        $proxies = @(foreach ($spec in $resourceSpecs) { Get-ProductDirectProxy $type $spec.Field $spec.Value })
        $publicGetters = @(foreach ($spec in $resourceSpecs) {
            $property = @($type.Properties | Where-Object Name -ceq $spec.Public)
            if ($property.Count -ne 1 -or !$property[0].GetMethod) {
                throw ('Loot product public getter declaration missing: ' + $name + '::' + $spec.Public)
            }
            Get-ProductMethodEvidence $property[0].GetMethod
        })
        $pointerConstructors = @($type.Methods | Where-Object {
            $_.IsConstructor -and !$_.IsStatic -and $_.Parameters.Count -eq 1 -and
            $_.Parameters[0].ParameterType.FullName -ceq 'System.IntPtr'
        })
        if ($pointerConstructors.Count -ne 1) { throw ('Loot product IntPtr wrapper constructor mismatch: ' + $name) }
        [pscustomobject]@{
            Type=$type.FullName; Assembly=$productAssembly.Name.Name; BaseType=$type.BaseType.FullName
            IsClrValueType=$type.IsValueType; IsClrInterface=$type.IsInterface
            DirectFields=$proxies; PublicGetters=$publicGetters
            PointerWrapperConstructor=$pointerConstructors[0].FullName
            ExactNativeClassVerified=$false; BaseValuesAreFinalSlotValues=$false
        }
    })

    $slot = $types['LootBoxSlot']
    $slotFields = @(foreach ($name in @('m_ItemID','m_Grade','m_FinalGrade','m_TotalCount')) {
        Get-ProductDirectProxy $slot $name $obscuredName
    })
    $obscured = $types[$obscuredName]
    $obscuredFields = @($obscured.Fields | Where-Object { !$_.IsStatic } | ForEach-Object {
        [pscustomobject]@{ Name=$_.Name; Type=$_.FieldType.FullName; AssemblyScope=$_.FieldType.Scope.Name
            Public=$_.IsPublic; InitOnly=$_.IsInitOnly; GeneratedClrFieldOffset=$_.Offset }
    })
    $obscuredMethods = @($obscured.Methods | Where-Object {
        $_.Name -match '^(Encrypt|Decrypt|GetEncrypted|SetEncrypted|GetDecrypted|InternalDecrypt|op_Implicit)$' -or
        ($_.IsConstructor -and !$_.IsStatic)
    } | ForEach-Object { Get-ProductMethodEvidence $_ })
    $cryptoKey = @($obscured.Properties | Where-Object Name -ceq 'cryptoKey')
    if ($cryptoKey.Count -ne 1 -or !$cryptoKey[0].GetMethod) { throw 'ObscuredInt cryptoKey declaration missing.' }
    $cryptoKeyGetter = Get-ProductMethodEvidence $cryptoKey[0].GetMethod
    $intConversions = @($obscuredMethods | Where-Object {
        $_.Name -ceq 'op_Implicit' -and
        (($_.Return -ceq 'System.Int32' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].Type -ceq $obscuredName) -or
         ($_.Return -ceq $obscuredName -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].Type -ceq 'System.Int32'))
    })
    if ($intConversions.Count -ne 2 -or @($intConversions | Where-Object RuntimeInvokeCalls -ne 1).Count -ne 0) {
        throw 'ObscuredInt integer conversion declaration or native invocation mismatch.'
    }

    $productHashAfter = (Get-FileHash -LiteralPath $productAssemblyPath -Algorithm SHA256).Hash
    if ($productHashAfter -cne $productHashBefore) { throw 'Generated interop input changed during loot product inspection.' }
    $report = [pscustomobject]@{
        SchemaVersion=1; GeneratedUtc=[DateTime]::UtcNow.ToString('o'); AssemblyName='Assembly-CSharp.dll'
        AssemblySHA256=$productHashAfter; InputHashFresh=$true
        Evidence='Offline generated interop metadata and wrapper call classification; no original native behavior, decryption or formula proof.'
        Summary=[pscustomobject]@{
            Assemblies=1; Types=$requiredNames.Count; Resources=$resources.Count
            ResourceDirectFields=@($resources | ForEach-Object { $_.DirectFields }).Count
            ResourcePublicGetterDeclarations=@($resources | ForEach-Object { $_.PublicGetters }).Count
            SlotDirectFields=$slotFields.Count; SlotGetterValueBoxCalls=($slotFields | ForEach-Object { $_.Getter.ValueBoxCalls } | Measure-Object -Sum).Sum
            ObscuredIntIsValueType=$obscured.IsValueType; ObscuredIntInstanceFields=$obscuredFields.Count
            ObscuredMethodDeclarations=$obscuredMethods.Count; NativeIntConversions=$intConversions.Count
            NativeObscuredMethodDeclarations=@($obscuredMethods | Where-Object RuntimeInvokeCalls -gt 0).Count
            MissingTypes=@()
        }
        Resources=$resources
        LootBoxSlot=[pscustomobject]@{ Type=$slot.FullName; BaseType=$slot.BaseType.FullName; DirectFields=$slotFields }
        ObscuredInt=[pscustomobject]@{
            Type=$obscured.FullName; Assembly=$productAssembly.Name.Name; BaseType=$obscured.BaseType.FullName
            IsClrValueType=$obscured.IsValueType; Layout=$obscured.Attributes.ToString()
            GeneratedClrClassSize=$obscured.ClassSize; GeneratedClrPackingSize=$obscured.PackingSize
            NativeLayoutAbiVerified=$false
            InstanceFields=$obscuredFields; Methods=$obscuredMethods; CryptoKeyGetter=$cryptoKeyGetter
            DecryptionExecuted=$false; DecryptionPureVerified=$false; AntiCheatSideEffectsKnown=$false
        }
        GameCodeExecuted=$false; HooksInstalled=$false; NativeAbiVerified=$false; FinalGradeProven=$false
        EffectiveWeightProven=$false; YieldComplete=$false; SourceOperationBound=$false; CaptureReceiptProven=$false
    }
    $reportRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.local/analysis'))
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    $reportPath = Join-Path $reportRoot 'loot-product-api.json'
    $temporary = Join-Path $reportRoot ('loot-product-api-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $temporary -Encoding utf8
        Move-Item -LiteralPath $temporary -Destination $reportPath -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
    }
    [pscustomobject]@{ Summary=$report.Summary; AssemblySHA256=$report.AssemblySHA256; ReportPath=$reportPath; GameCodeExecuted=$false } |
        ConvertTo-Json -Depth 5
} finally { $productAssembly.Dispose() }
