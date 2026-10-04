[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
$assemblyPath = Join-Path $gameRoot 'BepInEx/interop/Assembly-CSharp.dll'
$inputHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
Add-Type -Path (Join-Path $gameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
try {
    # Inspect generated declarations and wrapper references only. No game
    # assembly is loaded for execution; no provider, getter or method is called.
    function Get-WrapperSummary($method) {
        $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
        $runtimeInvokes = @($instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -match 'runtime_invoke|RuntimeInvoke'
        })
        $fieldRefs = @($instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeFieldInfoPtr_*'
        } | ForEach-Object { $_.Operand.Name } | Sort-Object -Unique)
        $methodRefs = @($instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeMethodInfoPtr_*'
        } | ForEach-Object { $_.Operand.Name } | Sort-Object -Unique)
        [pscustomobject]@{
            HasBody=[bool]($method -and $method.HasBody)
            RuntimeInvokeCalls=$runtimeInvokes.Count
            NativeFieldInfoReferences=$fieldRefs
            NativeMethodInfoReferences=$methodRefs
            DirectFieldProxyOnly=($fieldRefs.Count -gt 0 -and $runtimeInvokes.Count -eq 0)
        }
    }
    function Get-ExactDeclaration($spec) {
        $type = $assembly.MainModule.GetType($spec.Type)
        if (!$type) { throw ('Yield type missing: ' + $spec.Type) }
        $matches = @($type.Methods | Where-Object {
            $_.Name -ceq $spec.Name -and $_.ReturnType.FullName -ceq $spec.Return -and
            $_.IsStatic -eq $spec.Static -and $_.IsPublic -and !$_.HasGenericParameters -and
            $_.Parameters.Count -eq $spec.Params.Count -and
            ((@($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -ceq ($spec.Params -join '|'))
        })
        if ($matches.Count -ne 1) { throw ('Yield declaration mismatch: ' + $spec.Type + '::' + $spec.Name) }
        $method = $matches[0]
        [pscustomobject]@{
            Type=$type.FullName; Method=$method.Name; Public=$method.IsPublic
            Static=$method.IsStatic; Virtual=$method.IsVirtual; Return=$method.ReturnType.FullName
            Parameters=@($method.Parameters | ForEach-Object {
                [pscustomobject]@{Name=$_.Name; Type=$_.ParameterType.FullName; ByReference=$_.ParameterType.IsByReference; Out=$_.IsOut}
            })
            MetadataSignature=$method.FullName; Wrapper=(Get-WrapperSummary $method)
        }
    }
    $lift = 'LootBox/AutoLiftedType'
    $times = 'Il2CppSystem.Collections.Generic.List`1<System.String>'
    $specs = @(
        @{Type='DR.AI.FishAISystem';Name='SuccessPickupFish';Return='System.Void';Static=$false;Params=@('System.Int32','System.Boolean')},
        @{Type='DR.AI.FishAISystem';Name='OnSuccessPickUp';Return='System.Void';Static=$false;Params=@()},
        @{Type='DR.AI.FishAISystem';Name='LootDeadFishBody';Return='System.Void';Static=$false;Params=@()},
        @{Type='DR.AI.FishAISystem';Name='AddDropItemLootBoxWithPlus';Return='System.Void';Static=$false;Params=@('System.Int32',$lift,'System.Int32')},
        @{Type='DR.AI.FishAISystem';Name='AddDropItem_Impl';Return='System.Void';Static=$false;Params=@('System.Int32',$lift,'System.Int32','System.Boolean')},
        @{Type='DR.AI.FishAISystem';Name='AddDropPlusItem_Impl';Return='System.Void';Static=$false;Params=@('System.Int32',$lift)},
        @{Type='DR.AI.FishAISystem';Name='DestroySelf';Return='System.Void';Static=$false;Params=@()},
        @{Type='DR.AI.FishAISystem';Name='WinFromProjectileinFight';Return='System.Void';Static=$false;Params=@()},
        @{Type='FishInteractionBody';Name='SuccessInteract';Return='System.Void';Static=$false;Params=@('BaseCharacter')},
        @{Type='FishInteractionBody';Name='GetPickUpGrade';Return='System.Int32';Static=$false;Params=@()},
        @{Type='DataManager';Name='GetFishData';Return='DR.FishInfoData';Static=$false;Params=@('System.Int32')},
        @{Type='DataManager';Name='GetFishDropDataByFishTID';Return='DR.FishDropPackageEntity';Static=$false;Params=@('System.Int32')},
        @{Type='DataManager';Name='GetFishDropItemID';Return='System.Int32';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='DataManager';Name='GetFishDropItemByTier';Return='System.Int32';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='DataManager';Name='GetFishPlusItemID';Return='System.Int32';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='DataManager';Name='GetFishPlusItemByGrade';Return='System.Int32';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='DataManager';Name='GetItemV2';Return='DR.IItemBase';Static=$false;Params=@('System.Int32')},
        @{Type='FishPlusItemPity';Name='RollPlusItem';Return='System.Int32';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='FishPlusItemPity';Name='SetCounter';Return='System.Void';Static=$true;Params=@('System.Int32','System.Int32')},
        @{Type='SaveData';Name='SetFishDropPityPending';Return='System.Void';Static=$false;Params=@('System.Int32','System.Int32')},
        @{Type='SaveData';Name='GetFishDropPityCommitted';Return='System.Int32';Static=$false;Params=@('System.Int32')},
        @{Type='SaveData';Name='TryGetFishDropPityPending';Return='System.Boolean';Static=$false;Params=@('System.Int32','System.Int32&')},
        @{Type='SaveData';Name='EnsureFishDropPity';Return='SaveDataFishDropPity';Static=$false;Params=@()},
        @{Type='LootBox';Name='Add';Return='System.Boolean';Static=$false;Params=@('System.Int32','System.Int32','System.Int32',$lift,$times,'System.Boolean')},
        @{Type='LootBox';Name='AddIgnoreOverloaded';Return='System.Boolean';Static=$false;Params=@('System.Int32','System.Int32','System.Int32',$lift,$times,'System.Boolean')},
        @{Type='LootBox';Name='Add_Impl';Return='System.Void';Static=$false;Params=@('DR.IItemBase','System.Int32','System.Int32',$lift,$times,'System.Boolean')},
        @{Type='LootBox';Name='SendSignal';Return='System.Void';Static=$false;Params=@('System.Int32','LootBox/ItemSignal')}
    )
    $declarations = @($specs | ForEach-Object { Get-ExactDeclaration $_ })
    $fieldSpecs = @(
        @{Type='DR.AI.FishAISystem';Name='FishDataTID';Value='System.Int32'},
        @{Type='DR.AI.FishAISystem';Name='_FishInfoData';Value='DR.FishInfoData'},
        @{Type='DR.AI.FishAISystem';Name='_fishInteractionBody';Value='FishInteractionBody'},
        @{Type='DR.AI.FishAISystem';Name='_isFishCapturedRP';Value='UniRx.ReactiveProperty`1<System.Boolean>'},
        @{Type='DR.AI.FishAISystem';Name='_CarvedCount_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishInfoData';Name='_TID_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishInfoData';Name='_CarvableCount_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishInfoData';Name='_DropItemID_k__BackingField';Value='System.Int32'},
        @{Type='FishInteractionBody';Name='_ownerFish';Value='DR.AI.SABaseFishSystem'},
        @{Type='FishInteractionBody';Name='InteractionType';Value='FishInteractionBody/FishInteractionType'},
        @{Type='FishInteractionBody';Name='SuccessPickupFish';Value='UnityEngine.Events.UnityEvent'},
        @{Type='DR.FishDropPackageEntity';Name='_TID_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishDropPackageEntity';Name='_DropType_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishDropPackageEntity';Name='FishDropList';Value='Il2CppSystem.Collections.Generic.List`1<DR.FishDropPackageEntity/FishDropNode>'},
        @{Type='DR.FishDropPackageEntity';Name='_PlusItemWeightList_k__BackingField';Value='Il2CppSystem.Collections.Generic.List`1<System.Int32>'},
        @{Type='DR.FishDropPackageEntity';Name='_PlusItemID_k__BackingField';Value='System.Int32'},
        @{Type='DR.FishDropPackageEntity/FishDropNode';Name='itemTid';Value='System.Int32'},
        @{Type='DR.FishDropPackageEntity/FishDropNode';Name='itmeTier';Value='System.Int32'},
        @{Type='DR.FishDropPackageEntity/FishDropNode';Name='itemWeight';Value='System.Int32'},
        @{Type='DataManager';Name='_FishInfoDataDic_k__BackingField';Value='Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,DR.FishInfoDataEntity>'},
        @{Type='DataManager';Name='_FishDropPackageDataDic_k__BackingField';Value='Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,DR.FishDropPackageEntity>'},
        @{Type='DataManager';Name='ItemBaseDataDic';Value='Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,DR.IItemBase>'},
        @{Type='SaveData';Name='m_FishDropPitySaveData';Value='SaveDataFishDropPity'},
        @{Type='DR.Save.SaveDataBase';Name='_IsUpdated_k__BackingField';Value='System.Boolean'},
        @{Type='SaveDataFishDropPity';Name='Committed';Value='Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,System.Int32>'},
        @{Type='SaveDataFishDropPity';Name='Pending';Value='Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,System.Int32>'},
        @{Type='LootBox';Name='m_WeightMax';Value='System.Single'},
        @{Type='LootBox';Name='_weight_k__BackingField';Value='System.Single'},
        @{Type='LootBox';Name='_WeightParameter_k__BackingField';Value='System.Single'},
        @{Type='LootBox';Name='_overloadedThreshold_k__BackingField';Value='System.Single'},
        @{Type='LootBox';Name='k_NoneBonusGrade';Value='System.Int32'},
        @{Type='LootBox';Name='k_IngredientDefaultGrade';Value='System.Int32'},
        @{Type='LootBox';Name='k_MinItemGrade';Value='System.Int32'},
        @{Type='LootBox';Name='k_MaxItemGrade';Value='System.Int32'}
    )
    foreach ($resourceType in @('DR.Items','IntegratedItem')) {
        foreach ($resourceField in @('TID','ItemDataID','ItemGrade','ItemWeight')) {
            $fieldSpecs += @{Type=$resourceType;Name=('_' + $resourceField + '_k__BackingField');Value=$(if ($resourceField -eq 'ItemWeight') {'System.Single'} else {'System.Int32'})}
        }
    }
    $fields = @(foreach ($spec in $fieldSpecs) {
        $type = $assembly.MainModule.GetType($spec.Type)
        if (!$type) { throw ('Yield field type missing: ' + $spec.Type) }
        $matched = @($type.Properties | Where-Object { $_.Name -ceq $spec.Name -and $_.PropertyType.FullName -ceq $spec.Value })
        if ($matched.Count -ne 1 -or !$matched[0].GetMethod -or !$matched[0].GetMethod.IsPublic) {
            throw ('Yield direct proxy mismatch: ' + $spec.Type + '::' + $spec.Name)
        }
        $property = $matched[0]; $getter = Get-WrapperSummary $property.GetMethod
        if (!$getter.DirectFieldProxyOnly -or $getter.NativeFieldInfoReferences.Count -ne 1) {
            throw ('Yield expected direct proxy invokes native business: ' + $spec.Type + '::' + $spec.Name)
        }
        [pscustomobject]@{Type=$type.FullName;Property=$property.Name;ValueType=$property.PropertyType.FullName
            Static=$property.GetMethod.IsStatic;Getter=$getter}
    })
    $providers = @(foreach ($provider in @(
        @{Definition='Singleton`1';Context='DataManager';Backing='_instance'},
        @{Definition='SingletonNoMono`1';Context='LootBox';Backing='_s_Instance_k__BackingField'},
        @{Definition='SingletonNoMono`1';Context='FishPlusItemPity';Backing='_s_Instance_k__BackingField'}
    )) {
        $type = $assembly.MainModule.GetType($provider.Definition)
        if (!$type) { throw ('Yield singleton definition missing: ' + $provider.Definition) }
        $method = @($type.Methods | Where-Object {
            $_.Name -ceq 'get_Instance' -and $_.IsStatic -and $_.IsPublic -and $_.Parameters.Count -eq 0 -and
            !$_.HasGenericParameters -and $_.ReturnType.IsGenericParameter
        })
        $backing = @($type.Properties | Where-Object Name -CEQ $provider.Backing)
        if ($method.Count -ne 1 -or $backing.Count -ne 1 -or !$backing[0].GetMethod.IsStatic) {
            throw ('Yield singleton declaration mismatch: ' + $provider.Definition)
        }
        $backingSummary = Get-WrapperSummary $backing[0].GetMethod
        if (!$backingSummary.DirectFieldProxyOnly) { throw 'Yield singleton backing is not a direct field proxy.' }
        [pscustomobject]@{Definition=$type.FullName;ClosedContext=$provider.Context
            MetadataSignature=$method[0].FullName;Return=$method[0].ReturnType.FullName;Static=$true;Parameters=@()
            ProviderWrapper=(Get-WrapperSummary $method[0]);BackingProperty=$provider.Backing;BackingGetter=$backingSummary
            ClosedNativeClassOrGenericAbiVerified=$false}
    })
    $itemType = $assembly.MainModule.GetType('DR.IItemBase')
    if (!$itemType -or $itemType.Properties.Count -ne 13) { throw 'Yield IItemBase property schema mismatch.' }
    $itemProperties = @($itemType.Properties | ForEach-Object {
        if (!$_.GetMethod -or !$_.GetMethod.IsPublic -or $_.GetMethod.IsStatic -or $_.GetMethod.Parameters.Count -ne 0) {
            throw 'Yield IItemBase getter declaration mismatch.'
        }
        [pscustomobject]@{Property=$_.Name;ValueType=$_.PropertyType.FullName;MetadataSignature=$_.GetMethod.FullName
            Virtual=$_.GetMethod.IsVirtual;Getter=(Get-WrapperSummary $_.GetMethod)}
    })
    $liftType = $assembly.MainModule.GetType($lift)
    if (!$liftType -or !$liftType.IsEnum) { throw 'Yield lift enum missing.' }
    $enumValues = @($liftType.Fields | Where-Object IsLiteral | ForEach-Object {
        [pscustomobject]@{Name=$_.Name;Value=$_.Constant}
    })
    $finalHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    if ($inputHash -cne $finalHash) { throw 'Yield metadata input changed during inspection.' }
    $typeNames = @(@($specs.Type) + @($fieldSpecs.Type) + @($providers.Definition) + @('DR.IItemBase',$lift) | Sort-Object -Unique)
    $report = [pscustomobject]@{
        SchemaVersion=1;GeneratedUtc=[DateTime]::UtcNow.ToString('o');AssemblyName='Assembly-CSharp.dll';AssemblySHA256=$finalHash
        Evidence='Exact generated metadata and wrapper-reference classification only; no original method execution, branch completeness or runtime ABI proof.'
        Summary=[pscustomobject]@{Assemblies=1;Types=$typeNames.Count;BusinessDeclarations=$declarations.Count
            DirectFieldProxies=$fields.Count;SingletonContexts=$providers.Count;ItemInterfaceProperties=$itemProperties.Count
            LiftEnumValues=$enumValues.Count;ByReferenceParameters=@($declarations.Parameters | Where-Object ByReference).Count;MissingTypes=@()}
        Declarations=$declarations;DirectFieldProxies=$fields;SingletonProviders=$providers
        ItemInterface=[pscustomobject]@{Type=$itemType.FullName;GeneratedClrIsInterface=$itemType.IsInterface
            GeneratedClrBaseType=$itemType.BaseType.FullName;DeclaredClrInstanceFields=@($itemType.Fields | Where-Object {!$_.IsStatic} | ForEach-Object {$_.FullName})
            Properties=$itemProperties;NativeVirtualSlotsVerified=$false}
        LiftEnum=[pscustomobject]@{Type=$liftType.FullName;UnderlyingType=($liftType.Fields | Where-Object Name -CEQ 'value__').FieldType.FullName;Values=$enumValues}
        InputHashesMatched=$true;GameCodeExecuted=$false;HooksInstalled=$false;BusinessGettersCalled=$false
        NativeAbiVerified=$false;SourceOperationBound=$false;MemberOwnershipVerified=$false;FullYield=$false
        NativeBranchCompletenessVerified=$false;SelectionIsolationVerified=$false;FinalGradeVerified=$false
        EffectiveWeightVerified=$false;CargoPermission=$false;PersonalBagRoutingVerified=$false
    }
    $reportRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.local/analysis'))
    Assert-NoPathLinks $reportRoot
    New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
    $reportPath = Join-Path $reportRoot 'fish-yield-api.json'
    Assert-NoPathLinks $reportPath
    $temporary = Join-Path $reportRoot ('fish-yield-api-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $reportPath -Force
    [pscustomobject]@{ReportPath=$reportPath;Summary=$report.Summary;AssemblySHA256=$finalHash
        GameCodeExecuted=$false;NativeAbiVerified=$false;FullYield=$false} | ConvertTo-Json -Depth 5
} finally { $assembly.Dispose() }
