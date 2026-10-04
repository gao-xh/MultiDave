[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
$assemblyPath = Join-Path $gameRoot 'BepInEx/interop/Assembly-CSharp.dll'
Add-Type -Path (Join-Path $gameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
try {
    # Exact declaration descriptors. Cecil inspects metadata without executing
    # constructors, getters, selected methods, hooks, game loading or saves.
    $specs = @(
        @{ Type='FishInteractionBody'; Name='SuccessInteract'; Return='System.Void'; Static=$false; Params=@('BaseCharacter') },
        @{ Type='FishInteractionBody'; Name='CheckAvailableInteraction'; Return='System.Boolean'; Static=$false; Params=@('BaseCharacter') },
        @{ Type='DR.AI.FishAISystem'; Name='SuccessPickupFish'; Return='System.Void'; Static=$false; Params=@('System.Int32','System.Boolean') },
        @{ Type='DR.AI.FishAISystem'; Name='AddDropItemLootBoxWithPlus'; Return='System.Void'; Static=$false; Params=@('System.Int32','LootBox/AutoLiftedType','System.Int32') },
        @{ Type='DR.AI.FishAISystem'; Name='AddDropItem_Impl'; Return='System.Void'; Static=$false; Params=@('System.Int32','LootBox/AutoLiftedType','System.Int32','System.Boolean') },
        @{ Type='DR.AI.FishAISystem'; Name='AddDropPlusItem_Impl'; Return='System.Void'; Static=$false; Params=@('System.Int32','LootBox/AutoLiftedType') },
        @{ Type='LootBox'; Name='Add'; Return='System.Boolean'; Static=$false; Params=@('System.Int32','System.Int32','System.Int32','LootBox/AutoLiftedType','Il2CppSystem.Collections.Generic.List`1<System.String>','System.Boolean') },
        @{ Type='LootBox'; Name='AddIgnoreOverloaded'; Return='System.Boolean'; Static=$false; Params=@('System.Int32','System.Int32','System.Int32','LootBox/AutoLiftedType','Il2CppSystem.Collections.Generic.List`1<System.String>','System.Boolean') },
        @{ Type='LootBox'; Name='Add_Impl'; Return='System.Void'; Static=$false; Params=@('DR.IItemBase','System.Int32','System.Int32','LootBox/AutoLiftedType','Il2CppSystem.Collections.Generic.List`1<System.String>','System.Boolean') },
        @{ Type='LootBox'; Name='CheckOverloadedState'; Return='System.Boolean'; Static=$false; Params=@('System.Int32') },
        @{ Type='LootBox'; Name='RefreshOverweight'; Return='System.Void'; Static=$false; Params=@('System.Single') },
        @{ Type='SaveData'; Name='AddLootingSaveData'; Return='System.Void'; Static=$false; Params=@('System.Int32','System.Boolean') },
        @{ Type='SaveDataCaughtFishRouter'; Name='AddCaughtFish'; Return='System.Void'; Static=$true; Params=@('System.Int32','System.Int32','System.Boolean') },
        @{ Type='IngredientsStorage'; Name='AddFromLootBox'; Return='System.Void'; Static=$false; Params=@('LootBoxSlot','Il2CppSystem.Func`1<System.Int32>') },
        @{ Type='FishPlusItemPity'; Name='RollPlusItem'; Return='System.Int32'; Static=$false; Params=@('System.Int32','System.Int32') },
        @{ Type='SaveData'; Name='AddLootBox'; Return='System.Void'; Static=$false; Params=@('SaveData/LootBoxType','System.String','LootBoxSlot') }
    )
    $declarations = foreach ($spec in $specs) {
        $type = $assembly.MainModule.GetType($spec.Type)
        if (!$type) { throw ('Capture type missing: ' + $spec.Type) }
        $matched = @($type.Methods | Where-Object {
            $_.Name -eq $spec.Name -and $_.ReturnType.FullName -eq $spec.Return -and $_.IsStatic -eq $spec.Static -and
            !$_.HasGenericParameters -and $_.Parameters.Count -eq $spec.Params.Count -and
            ((@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -ceq ($spec.Params -join '|'))
        })
        if ($matched.Count -ne 1) { throw ('Capture declaration mismatch: ' + $spec.Type + '::' + $spec.Name) }
        $method = $matched[0]
        [pscustomobject]@{
            Type=$type.FullName; Method=$method.Name; Static=$method.IsStatic; Virtual=$method.IsVirtual
            Return=$method.ReturnType.FullName
            Parameters=@($method.Parameters | ForEach-Object { [pscustomobject]@{Name=$_.Name; Type=$_.ParameterType.FullName} })
            MetadataSignature=$method.FullName
        }
    }
    $fields = foreach ($entry in @(
        @{ Type='LootBox'; Property='m_WeightMax' }, @{ Type='LootBox'; Property='_weight_k__BackingField' },
        @{ Type='LootBox'; Property='_WeightParameter_k__BackingField' }, @{ Type='LootBox'; Property='_overloadedThreshold_k__BackingField' },
        @{ Type='FishInteractionBody'; Property='_ownerFish' }
    )) {
        $type = $assembly.MainModule.GetType($entry.Type)
        $properties = @($type.Properties | Where-Object Name -ceq $entry.Property)
        if ($properties.Count -ne 1 -or !$properties[0].GetMethod -or !$properties[0].GetMethod.HasBody) {
            throw ('Direct field proxy missing: ' + $entry.Type + '::' + $entry.Property)
        }
        $property = $properties[0]
        $instructions = @($property.GetMethod.Body.Instructions)
        $fieldRefs = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } |
            ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
        $invokes = @($instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match 'runtime_invoke|RuntimeInvoke'
        } | ForEach-Object { $_.Operand.FullName })
        if ($invokes.Count -ne 0 -or !@($fieldRefs | Where-Object { $_.Contains('NativeFieldInfoPtr_') }).Count) {
            throw ('Expected direct field proxy requires native method invocation: ' + $entry.Type + '::' + $entry.Property)
        }
        [pscustomobject]@{ Type=$type.FullName; Property=$property.Name; ValueType=$property.PropertyType.FullName
            FieldReferences=$fieldRefs; RuntimeInvokeCalls=$invokes; DirectFieldProxyOnly=$true }
    }
    $ownerProperty = $fields | Where-Object Property -ceq '_ownerFish'
    $ownerType = $assembly.MainModule.GetType($ownerProperty.ValueType)
    $inheritance = @(); $visited = @{}
    while ($ownerType -and !$visited.ContainsKey($ownerType.FullName) -and $inheritance.Count -lt 16) {
        $visited[$ownerType.FullName] = $true; $inheritance += $ownerType.FullName
        if (!$ownerType.BaseType) { break }
        $ownerType = $assembly.MainModule.GetType($ownerType.BaseType.FullName)
    }
    if ($inheritance -cnotcontains 'DR.AI.FishAISystem') { throw 'The interaction fish owner cannot be directly upcast to FishAISystem.' }
    $report = [pscustomobject]@{
        SchemaVersion=1; GeneratedUtc=[DateTime]::UtcNow.ToString('o'); AssemblyName='Assembly-CSharp.dll'
        AssemblySHA256=(Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
        Evidence='Exact generated interop declarations and field getter IL; no original business execution or runtime ABI proof.'
        Summary=[pscustomobject]@{ Types=@($specs.Type | Sort-Object -Unique).Count; HookDeclarations=@($declarations).Count
            DirectFieldProxies=@($fields).Count; InteractionOwnerBaseDepth=$inheritance.Count; MissingTypes=@() }
        Declarations=@($declarations); FieldProxies=@($fields); InteractionOwnerInheritance=$inheritance
        GameCodeExecuted=$false; HooksInstalled=$false; NativeAbiVerified=$false; SourceOperationBound=$false; YieldComplete=$false
    }
    $reportRoot = Join-Path $PSScriptRoot '../.local/analysis'
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    $reportPath = Join-Path $reportRoot 'capture-lineage-api.json'
    $temporary = Join-Path $reportRoot ('capture-lineage-api-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $reportPath -Force
    $report.Summary | ConvertTo-Json -Depth 4
} finally { $assembly.Dispose() }
