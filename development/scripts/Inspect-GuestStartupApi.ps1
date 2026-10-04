[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-startup-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$startupGameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $startupGameRoot 'BepInEx\core\Mono.Cecil.dll')

# Cecil reads declarations and managed wrapper/loader IL only. No generated
# game/runtime type is loaded, initialized or invoked; no profile is opened.
function StartupTypes($types) {
    foreach ($type in $types) {
        $type
        if ($type.NestedTypes.Count) { StartupTypes $type.NestedTypes }
    }
}
function StartupBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    $calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName })
    $fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $invoke = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'il2cpp_runtime_invoke' }).Count)
    $fieldProxy = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeFieldInfoPtr_*' }).Count)
    [pscustomobject]@{
        Available = [bool]($method -and $method.HasBody)
        RuntimeInvoke = $invoke; NativeFieldProxy = $fieldProxy
        Classification = if (!$method) { 'Absent' } elseif ($invoke) { 'RuntimeInvoke' } elseif ($fieldProxy) { 'NativeFieldProxy' } else { 'ManagedOrOtherWrapper' }
        CallsInOrder = $calls; Fields = $fields
        StringLiterals = @($instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
    }
}
function StartupMethod($method) {
    [pscustomobject]@{
        Name = $method.Name; Signature = $method.FullName; ReturnType = $method.ReturnType.FullName
        Static = $method.IsStatic; Constructor = $method.IsConstructor; Abstract = $method.IsAbstract
        Generic = $method.HasGenericParameters; Visibility = $method.Attributes.ToString()
        Parameters = @($method.Parameters | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Type = $_.ParameterType.FullName; Out = $_.IsOut; Optional = $_.IsOptional; Default = if ($_.HasConstant) { $_.Constant } else { $null } }
        })
        Body = (StartupBody $method)
    }
}
function StartupProperty($property) {
    $accessor = if ($property.GetMethod) { $property.GetMethod } else { $property.SetMethod }
    [pscustomobject]@{
        Name = $property.Name; Type = $property.PropertyType.FullName; Static = [bool]$accessor.IsStatic
        Writable = $null -ne $property.SetMethod
        GetterSignature = if ($property.GetMethod) { $property.GetMethod.FullName } else { $null }
        SetterSignature = if ($property.SetMethod) { $property.SetMethod.FullName } else { $null }
        Getter = (StartupBody $property.GetMethod); Setter = (StartupBody $property.SetMethod)
    }
}
$startupInputs = @(
    @{ Directory = 'interop'; Name = 'Assembly-CSharp.dll' },
    @{ Directory = 'interop'; Name = 'UnityEngine.CoreModule.dll' },
    @{ Directory = 'core'; Name = 'BepInEx.Unity.IL2CPP.dll' },
    @{ Directory = 'core'; Name = 'BepInEx.Core.dll' },
    @{ Directory = 'core'; Name = 'Il2CppInterop.Runtime.dll' }
)
$startupExactTypes = @(
    'SaveUtil', 'GameBase', 'DR.Save.SaveSystem', 'DR.Save.SaveLoadManagerBase`1',
    'DR.Save.SaveSystemGameDataManager', 'DR.Save.SaveSystemPlayerDataManager',
    'DR.Save.SaveSystemPhotoDataManager', 'DR.Save.SaveSystemUserOptionManager',
    'DR.Save.SaveDataType', 'DR.Save.SaveSlotType', 'Singleton`1',
    'UnityEngine.Application', 'UnityEngine.SceneManagement.SceneManager',
    'BepInEx.Unity.IL2CPP.BasePlugin', 'BepInEx.Unity.IL2CPP.IL2CPPChainloader',
    'BepInEx.Unity.IL2CPP.Preloader', 'BepInEx.Bootstrap.BaseChainloader`1',
    'Il2CppInterop.Runtime.IL2CPP'
)
$startupRequiredTypes = @('SaveUtil', 'GameBase', 'DR.Save.SaveSystem',
    'DR.Save.SaveSystemGameDataManager', 'DR.Save.SaveSystemPlayerDataManager',
    'DR.Save.SaveSystemPhotoDataManager', 'DR.Save.SaveSystemUserOptionManager',
    'DR.Save.SaveSystem/_InitSaveSystem_d__42', 'GameBase/_Init_d__32',
    'GameBase/_LoadGameData_d__43', 'GameBase/_InitAfterSaveSystem_d__45',
    'BepInEx.Unity.IL2CPP.IL2CPPChainloader', 'BepInEx.Unity.IL2CPP.BasePlugin',
    'BepInEx.Bootstrap.BaseChainloader`1')
$startupMethodNames = @('Awake', 'Awake_Impl', 'Init', 'InitSaveSystem', 'MoveNext',
    'LoadGameData', 'LoadSavedData', 'InitAfterSaveSystem', 'LoadAllData', 'LoadGameOnInit',
    'LoadGame', 'ReloadData', 'ResetAfterCloudLoad', 'LoadData', 'LoadAllFromCloud', 'LoadFromCloud',
    'CreateManagedData', 'SetLoadedData', 'OnLoadData',
    'GetSaveFolder', 'GetDemoSaveFolder', 'GetSaveFilePath', 'GetDemoSaveFilePath',
    'GetFailedSaveFilePath', 'GetOldSaveFilePath', 'GetSaveFileName', 'GetFailedSaveFileName',
    'GetOldSaveFileName', 'GetSaveFileExtension')
$startupTypeRows = [System.Collections.Generic.List[object]]::new()
$startupAssemblies = [System.Collections.Generic.List[object]]::new()
$startupHooks = [System.Collections.Generic.List[object]]::new()
foreach ($startupInput in $startupInputs) {
    $startupPath = Join-Path $startupGameRoot ('BepInEx\' + $startupInput.Directory + '\' + $startupInput.Name)
    if (!(Test-Path -LiteralPath $startupPath -PathType Leaf)) { throw ('Required declaration input is missing: ' + $startupInput.Name) }
    $startupBefore = (Get-FileHash -LiteralPath $startupPath -Algorithm SHA256).Hash
    $startupAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($startupPath)
    try {
        foreach ($type in (StartupTypes $startupAssembly.MainModule.Types)) {
            $startupIterator = $type.FullName -match '^(GameBase/_(Init|LoadGameData|InitAfterSaveSystem)_d__|DR\.Save\.SaveSystem/_InitSaveSystem_d__)'
            if ($type.FullName -notin $startupExactTypes -and !$startupIterator) { continue }
            $startupFramework = $type.FullName -like 'BepInEx.*'
            $startupMethods = @($type.Methods | Where-Object {
                if ($_.IsGetter -or $_.IsSetter) { return $false }
                if ($startupFramework) { return $_.Name -in @('Initialize', 'OnInvokeMethod', 'LoadPlugin', 'Load', 'Execute', 'Run') }
                if ($type.FullName -eq 'Il2CppInterop.Runtime.IL2CPP') { return $_.Name -match 'il2cpp_(method_get_name|runtime_invoke|class_get_field_from_name|field_static_get_value|field_static_set_value)' }
                return $_.Name -in $startupMethodNames -or $_.IsConstructor
            } | ForEach-Object { StartupMethod $_ })
            $startupTypeRows.Add([pscustomobject]@{
                Assembly = $startupInput.Name; Type = $type.FullName; Base = $type.BaseType.FullName
                StaticType = $type.IsAbstract -and $type.IsSealed; ValueType = $type.IsValueType; Framework = $startupFramework
                Properties = @($type.Properties | ForEach-Object { StartupProperty $_ })
                Fields = @($type.Fields | Where-Object { $_.Name -notlike 'Native*Ptr*' } | ForEach-Object {
                    [pscustomobject]@{ Name = $_.Name; Type = $_.FieldType.FullName; Static = $_.IsStatic; ReadOnly = $_.IsInitOnly; Literal = $_.IsLiteral; Constant = if ($_.IsLiteral) { $_.Constant } else { $null } }
                })
                Methods = $startupMethods
            })
            if ($startupInput.Directory -eq 'interop' -and $type.FullName -notin @('UnityEngine.Application', 'UnityEngine.SceneManagement.SceneManager', 'Singleton`1')) {
                foreach ($method in $startupMethods | Where-Object { !$_.Constructor -and $_.Name -in $startupMethodNames }) {
                    $startupHooks.Add([pscustomobject]@{ Assembly = $startupInput.Name; Type = $type.FullName; OpenGenericOwner = $type.HasGenericParameters; Method = $method })
                }
            }
        }
    } finally { $startupAssembly.Dispose() }
    if ((Get-FileHash -LiteralPath $startupPath -Algorithm SHA256).Hash -ne $startupBefore) { throw 'Startup declaration input changed during the read.' }
    $startupAssemblies.Add([pscustomobject]@{ Name = $startupInput.Name; SHA256 = $startupBefore })
}
$startupMissing = @($startupRequiredTypes | Where-Object { $_ -notin $startupTypeRows.Type })
if ($startupMissing.Count) { throw ('Missing required startup declarations: ' + ($startupMissing -join ', ')) }
$startupLoaderMethods = @($startupTypeRows | Where-Object Framework | ForEach-Object Methods)
$startupOnInvoke = @($startupTypeRows | Where-Object Type -eq 'BepInEx.Unity.IL2CPP.IL2CPPChainloader' | ForEach-Object Methods | Where-Object Name -eq 'OnInvokeMethod')
$startupTriggerObserved = $startupOnInvoke.Count -eq 1 -and 'Internal_ActiveSceneChanged' -in $startupOnInvoke[0].Body.StringLiterals -and
    [bool](@($startupOnInvoke[0].Body.CallsInOrder | Where-Object { $_ -like '*BaseChainloader*::Execute()*' }).Count)
$startupAnalysisRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $startupAnalysisRoot
$startupNativeRows = [System.Collections.Generic.List[object]]::new()
foreach ($startupNativeName in @('guest-startup-native-calls.json', 'guest-save-native-calls.json')) {
    $startupNativePath = Join-Path $startupAnalysisRoot $startupNativeName
    if (!(Test-Path -LiteralPath $startupNativePath -PathType Leaf)) {
        $startupNativeRows.Add([pscustomobject]@{ Name = $startupNativeName; Available = $false })
        continue
    }
    Assert-NoPathLinks $startupNativePath
    $startupNativeHash = (Get-FileHash -LiteralPath $startupNativePath -Algorithm SHA256).Hash
    $startupNative = Get-Content -LiteralPath $startupNativePath -Raw | ConvertFrom-Json
    $startupEdges = @($startupNative.Methods | Where-Object { $_.Depth -eq 0 -or $_.IncludedBecause -like 'Compiler state-machine*' } | ForEach-Object {
        [pscustomobject]@{
            Type = $_.Type; Signature = $_.Signature; IncludedBecause = $_.IncludedBecause; BodyStatus = $_.BodyStatus
            IndirectCalls = $_.IndirectCalls; UnresolvedFlowInstructions = $_.UnresolvedFlowInstructions
            AliasTruncationPresent = [bool](@($_.Edges | Where-Object AliasesTruncated).Count)
            Callees = @($_.Edges | ForEach-Object Callees | Sort-Object -Unique)
        }
    })
    if ((Get-FileHash -LiteralPath $startupNativePath -Algorithm SHA256).Hash -ne $startupNativeHash) { throw 'Native report changed during startup summary read.' }
    $startupNativeRows.Add([pscustomobject]@{
        Name = $startupNativeName; Available = $true; SHA256 = $startupNativeHash
        BinarySHA256 = $startupNative.BinarySha256; MetadataSHA256 = $startupNative.MetadataSha256
        Roots = $startupNative.Roots.Count; Methods = $startupNative.Methods.Count
        NoUnwind = @($startupNative.Methods | Where-Object BodyStatus -like 'No containing runtime-function*').Count
        Partial = @($startupNative.Methods | Where-Object BodyStatus -like 'Partial*').Count
        MaxMethods = $startupNative.MaxMethods; MethodLimitReached = $startupNative.MethodLimitReached
        OmittedRoots = $startupNative.OmittedRootMethods
        InstructionTruncated = @($startupNative.Methods | Where-Object InstructionLimitReached).Count
        GameCodeExecuted = $false; StaticEdges = $startupEdges
        Evidence = 'Existing original-PE static edge report; no field dataflow, branch dominance, runtime order or complete method proof.'
    })
}
$startupSummary = [pscustomobject]@{
    Assemblies = $startupAssemblies.Count; Types = $startupTypeRows.Count
    SelectedHookDeclarations = $startupHooks.Count; LoaderMethodsExamined = $startupLoaderMethods.Count; MissingTypes = $startupMissing
}
$startupNativeSummary = @($startupNativeRows | Where-Object { $_.Name -eq 'guest-startup-native-calls.json' -and $_.Available } | Select-Object Roots, Methods, NoUnwind, Partial, MethodLimitReached, OmittedRoots, InstructionTruncated)
$startupReport = [pscustomobject]@{
    SchemaVersion = 1; ScriptName = 'Inspect-GuestStartupApi.ps1'; GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    Summary = $startupSummary; Assemblies = @($startupAssemblies); Types = @($startupTypeRows)
    SelectedHookDeclarations = @($startupHooks); LoaderMethods = $startupLoaderMethods; NativeEvidence = @($startupNativeRows)
    NativeStartupSummary = if ($startupNativeSummary.Count -eq 1) { $startupNativeSummary[0] } else { $null }
    PrimaryLoaderTriggerObservedInIl = [bool]$startupTriggerObserved
    PrimaryLoaderTrigger = if ($startupTriggerObserved) { 'Internal_ActiveSceneChanged' } else { $null }
    GameCodeExecuted = $false; GeneratedGameTypesLoaded = $false; NativeApiCalled = $false; NativeHooksInstalled = $false; SavesReadOrModified = $false
    PluginLoadBeforeEverySaveAwakeVerified = $false; InitialPathBindingVerified = $false; FirstLoadOrderVerified = $false
    FinalDirectoryBindingVerified = $false; SkipCloudBranchVerified = $false; PersistentOutputsComplete = $false
    GuestStateIsolated = $false; WorldAuthority = $false; CargoAuthority = $false
    Evidence = 'Offline Cecil declarations and managed loader/wrapper IL plus bounded original-PE edge summaries. Observed loader trigger does not prove plugin execution precedes every game constructor, Awake, static initializer or file access.'
}
New-Item -ItemType Directory -Path $startupAnalysisRoot -Force | Out-Null
$startupOutput = Join-Path $startupAnalysisRoot $ReportName
Assert-NoPathLinks $startupOutput
$startupTemporary = Join-Path $startupAnalysisRoot ('startup-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $startupReport | ConvertTo-Json -Depth 24 | Set-Content -LiteralPath $startupTemporary -Encoding utf8
    Move-Item -LiteralPath $startupTemporary -Destination $startupOutput -Force
} finally { if (Test-Path -LiteralPath $startupTemporary -PathType Leaf) { Remove-Item -LiteralPath $startupTemporary -Force } }
[pscustomobject]@{
    Summary = $startupSummary; PrimaryLoaderTriggerObservedInIl = $startupReport.PrimaryLoaderTriggerObservedInIl
    FirstLoadOrderVerified = $false; SkipCloudBranchVerified = $false; GameCodeExecuted = $false; SavesReadOrModified = $false; Output = $startupOutput
} | ConvertTo-Json -Depth 5
