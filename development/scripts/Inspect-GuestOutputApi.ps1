[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-output-fence-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$guestOutputGame = Resolve-DaveGamePath $GamePath
$guestOutputInterop = Join-Path $guestOutputGame 'BepInEx\interop'
Add-Type -Path (Join-Path $guestOutputGame 'BepInEx\core\Mono.Cecil.dll')

# Exact declared names are selected. This never loads generated game types,
# invokes original getters, initializes generic natives, or touches a save.
function OutputTypes($types) {
    foreach ($type in $types) { $type; if ($type.NestedTypes.Count) { OutputTypes $type.NestedTypes } }
}
function OutputTypeName($type, [string]$argument) {
    if ($type -is [Mono.Cecil.GenericParameter] -and $type.Type.ToString() -eq 'Type') { return $argument }
    if ($type -is [Mono.Cecil.ByReferenceType]) { return (OutputTypeName $type.ElementType $argument) + '&' }
    if ($type -is [Mono.Cecil.GenericInstanceType]) {
        $args = @($type.GenericArguments | ForEach-Object { OutputTypeName $_ $argument })
        return $type.ElementType.FullName + '<' + ($args -join ',') + '>'
    }
    return $type.FullName
}
function OutputBody($method) {
    $instructions = if ($method.HasBody) { @($method.Body.Instructions) } else { @() }
    $calls = @($instructions | Where-Object { $_.OpCode.FlowControl.ToString() -eq 'Call' } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{
        RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match 'il2cpp_runtime_invoke' }).Count)
        Calls = $calls
        NativeMethodInfoFields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeMethodInfoPtr_*' } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
        Instructions = @($instructions | ForEach-Object ToString)
    }
}
function OutputPolicy($method) {
    $returnName = $method.ReturnType.FullName
    if ($method.Parameters | Where-Object { $_.ParameterType.IsByReference }) {
        if ($method.Name -in @('TryLoadFromSlot','TryLoadFromJson')) { return 'Typed closed out-data=null and __result=false; no original load.' }
        if ($method.DeclaringType.FullName -eq 'UnityEngine.PlayerPrefs' -and $method.Name -like '*_Injected') {
            if ($returnName -eq 'System.Boolean') { return 'Input ref spans untouched; skip original and __result=false.' }
            if ($returnName -eq 'System.Void') { return 'Input ref spans untouched; skip original without synthesized success.' }
        }
        return 'Unknown ref/out policy: fail installation, do not mutate arguments or guess a result.'
    }
    if ($returnName -eq 'System.Void') { return 'Skip original; no success callback or result synthesized.' }
    if ($returnName -eq 'System.Boolean') { return 'Skip original; __result=false.' }
    if ($returnName -eq 'System.UInt64' -and $method.Name -like '*FileWriteStreamOpen') { return 'Skip original; __result=UInt64.MaxValue (stream invalid).' }
    if ($returnName -eq 'System.UInt64') { return 'Skip original; __result=0 (API call invalid).' }
    if ($returnName -eq 'Steamworks.SteamAPICall_t') { return 'Skip original; default struct has m_SteamAPICall=0.' }
    if ($returnName -eq 'Steamworks.UGCFileWriteStreamHandle_t') { return 'Skip original; default struct then direct m_UGCFileWriteStreamHandle=UInt64.MaxValue; do not call ulong constructor.' }
    if ($returnName -eq 'Toolbox.SaveSystem.SaveResult') { return 'Skip original; SaveResult.Failed=2, never default Succeed=0.' }
    if ($returnName -eq 'Toolbox.SaveSystem.DeleteResult') { return 'Skip original; DeleteResult.Failed=1, never default Succeed=0.' }
    return 'Unsupported result policy: fail installation rather than guessing a success/default.'
}
$saveSystem = @('SaveAllData','SaveGameDataInSlot','LoadGameDataFromSlot','LoadGameDataFromJson','TrySaveGameData','SaveGameData','SavePhotoData','DeleteGameData','LoadGame','ResetAfterCloudLoad','ReloadData','LoadGameOnInit','CheckSaveVersion','LoadAllData','TestSaveGameData','TestLoadGameData','TestSavePhotoData')
$base = @('SaveData','CopyFileToCloud','WriteOnCloud','WriteAllAutoSaveOnCloud','SaveBackupData','SaveSlotWithJson','SaveOnSelectedSlot','LoadData','LoadFromCloud','LoadAllFromCloud','WriteOldFileOnConvert','CreateNew','CreateNewAndSave','DeleteSaveFile','SetLoadedData','CreateManagedData','OnLoadData','Reset','ReportProgressMissionState')
$steamWrites = @('FileWrite','FileWriteAsync','FileForget','FileDelete','FileShare','SetSyncPlatforms','FileWriteStreamOpen','FileWriteStreamWriteChunk','FileWriteStreamClose','FileWriteStreamCancel','BeginFileWriteBatch','EndFileWriteBatch')
$stats = @('SetStat','SetAchievement','ClearAchievement','StoreStats','ResetAllStats','IndicateAchievementProgress','UpdateAvgRateStat')
$native = @($steamWrites | ForEach-Object { 'ISteamRemoteStorage_' + $_ }) + @('ISteamUserStats_SetStatInt32','ISteamUserStats_SetStatFloat','ISteamUserStats_SetAchievement','ISteamUserStats_ClearAchievement','ISteamUserStats_StoreStats','ISteamUserStats_ResetAllStats','ISteamUserStats_IndicateAchievementProgress','ISteamUserStats_UpdateAvgRateStat')
$selection = @{
    'DR.Save.SaveSystem' = $saveSystem
    'DR.Save.SaveLoadManagerBase`1' = $base + @('TryLoadFromSlot','TryLoadFromJson')
    'DR.Save.SaveSystemGameDataManager' = @('SaveData','DeleteSaveFile','CopyDemoSaveFiles','CheckSaveVersion','OnLoadData','CreateManagedData','Reset')
    'DR.Save.SaveSystemPhotoDataManager' = @('SaveData','DeleteSaveFile','OnLoadData','CreateManagedData')
    'DR.Save.SaveSystemPlayerDataManager' = @('LoadData','SetLoadedData','OnLoadData','CreateManagedData','ClearData')
    'DR.Save.SaveSystemUserOptionManager' = @('OnLoadData','CreateManagedData','SetMakeBackupEndingSlot')
    'SteamAchievements' = @('SetStatById','UnlockAchievement','UnlockAchievementWithStat','UpdateAchievementValueWithStat','LockAchievement','ResetAll','UnlockProgressSyncFromSave','OnDREvent','CompletedMission','UpdateMissionCondition')
    'Steamworks.SteamRemoteStorage' = $steamWrites
    'Steamworks.SteamUserStats' = $stats
    'Steamworks.NativeMethods' = $native
    'UnityEngine.PlayerPrefs' = @('SetInt','SetFloat','SetString','TrySetInt','TrySetFloat','TrySetSetString','DeleteKey','DeleteAll','Save','TrySetInt_Injected','TrySetFloat_Injected','TrySetSetString_Injected','DeleteKey_Injected')
    'GDKSaveLoadModule' = @('SaveData','DeleteData','DeleteFiles')
    'TKoU.UniversalSaveSystem.ISaveSystemService' = @('FileWriteBytes','FileDelete')
    'Toolbox.SaveSystem.SaveManager' = @('Save','Delete')
}
$assemblyNames = @('Assembly-CSharp.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.CoreModule.dll','TKoU.UniversalSaveSystem.Core.dll','SaveSystem.dll')
$targetRows = @(); $assemblyRows = @(); $closedRows = @(); $declaredTypes = @{}; $valueRows = @()
foreach ($assemblyName in $assemblyNames) {
    $path = Join-Path $guestOutputInterop $assemblyName
    $beforeHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        $all = @(OutputTypes $assembly.MainModule.Types)
        foreach ($type in $all) {
            if ($selection.ContainsKey($type.FullName)) {
                $declaredTypes[$type.FullName] = @($type.Methods | Where-Object { !$_.IsConstructor -and $_.Name -in $selection[$type.FullName] } | ForEach-Object Name | Sort-Object -Unique)
                foreach ($method in $type.Methods | Where-Object { !$_.IsConstructor -and $_.Name -in $selection[$type.FullName] }) {
                    $row = [pscustomobject]@{
                        Assembly = $assemblyName; Owner = $type.FullName; ReflectionOwner = $type.FullName.Replace('/','+')
                        Name = $method.Name; Static = $method.IsStatic; Virtual = $method.IsVirtual; Abstract = $method.IsAbstract
                        Return = $method.ReturnType.FullName; Parameters = @($method.Parameters | ForEach-Object { $_.ParameterType.FullName })
                        ParameterRoles = @($method.Parameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.ParameterType.FullName; ByRef = $_.ParameterType.IsByReference; IsOut = $_.IsOut; IsIn = $_.IsIn } })
                        DeclaredSignature = $method.FullName; Policy = (OutputPolicy $method); Body = (OutputBody $method)
                    }
                    $targetRows += $row
                    if ($type.FullName -eq 'DR.Save.SaveLoadManagerBase`1') {
                        foreach ($argument in @('SaveData','DR.Save.SavePlayerData','SavePhotoData','DR.Save.SaveUserOptions')) {
                            $closedRows += [pscustomobject]@{
                                Owner = $type.FullName + '<' + $argument + '>'; GenericArgument = $argument
                                Name = $method.Name; Static = $method.IsStatic; Virtual = $method.IsVirtual
                                Return = (OutputTypeName $method.ReturnType $argument)
                                Parameters = @($method.Parameters | ForEach-Object { OutputTypeName $_.ParameterType $argument })
                                Policy = (OutputPolicy $method)
                                NativeClosedInstanceAddressVerified = $false
                            }
                        }
                    }
                }
            }
            if ($type.FullName -in @('Steamworks.SteamAPICall_t','Steamworks.UGCFileWriteStreamHandle_t','Toolbox.SaveSystem.SaveResult','Toolbox.SaveSystem.DeleteResult')) {
                $valueRows += [pscustomobject]@{
                    Owner = $type.FullName; IsValueType = $type.IsValueType; IsEnum = $type.IsEnum
                    LiteralFields = @($type.Fields | Where-Object IsLiteral | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Value = $_.Constant } })
                    InstanceFields = @($type.Fields | Where-Object { !$_.IsStatic } | ForEach-Object FullName)
                    Constructors = @($type.Methods | Where-Object { $_.Name -eq '.ctor' } | ForEach-Object { [pscustomobject]@{ Signature = $_.FullName; Body = (OutputBody $_) } })
                }
            }
        }
    } finally { $assembly.Dispose() }
    $afterHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($beforeHash -ne $afterHash) { throw 'Generated metadata changed while reading; report rejected.' }
    $assemblyRows += [pscustomobject]@{ Name = $assemblyName; SHA256 = $beforeHash }
}
$missing = @()
foreach ($owner in $selection.Keys) {
    foreach ($name in $selection[$owner]) {
        if (!$declaredTypes.ContainsKey($owner) -or $name -notin $declaredTypes[$owner]) { $missing += $owner + '::' + $name }
    }
}
$runtimePath = Join-Path $guestOutputGame 'BepInEx\core\Il2CppInterop.Runtime.dll'
$runtime = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($runtimePath)
try {
    $runtimeVersion = @($runtime.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Reflection.AssemblyInformationalVersionAttribute' } | ForEach-Object { $_.ConstructorArguments[0].Value })
    $gcRows = foreach ($type in $runtime.MainModule.Types | Where-Object { $_.FullName -in @('Il2CppInterop.Runtime.IL2CPP','Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase') }) {
        foreach ($method in $type.Methods | Where-Object { $_.Name -match '^il2cpp_gchandle_|^CreateGCHandle$|^Finalize$|^get_Pointer$' }) {
            [pscustomobject]@{ Signature = $method.FullName; Visibility = $method.Attributes.ToString(); Body = (OutputBody $method) }
        }
    }
} finally { $runtime.Dispose() }
$nativeEvidence = @()
foreach ($name in @('guest-save-native-calls.json','guest-shadow-clone-native-calls.json')) {
    $path = Join-Path $PSScriptRoot ('..\.local\analysis\' + $name)
    if (!(Test-Path -LiteralPath $path)) { continue }
    $nativeReport = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    $edges = foreach ($method in $nativeReport.Methods) {
        foreach ($edge in $method.Edges) {
            $matches = @($edge.Callees | Where-Object { $_ -match '^System\.IO\.(File|Directory)::|^DR\.Save\.SaveLoadManagerBase|^Steamworks\.' })
            if ($matches.Count) { [pscustomobject]@{ CallerType = $method.Type; CallerSignature = $method.Signature; BodyStatus = $method.BodyStatus; AliasesTruncated = $edge.AliasesTruncated; Matches = $matches } }
        }
    }
    $nativeEvidence += [pscustomobject]@{ Name = $name; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; OriginalBinarySHA256 = $nativeReport.BinarySha256; OriginalMetadataSHA256 = $nativeReport.MetadataSha256; GameCodeExecuted = $nativeReport.GameCodeExecuted; SelectedStaticEdges = @($edges); CurrentOriginalFilesRevalidated = $false }
}
$report = [pscustomobject]@{
    SchemaVersion = 1; ScriptName = 'Inspect-GuestOutputApi.ps1'; GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    GameCodeExecuted = $false; SavesReadOrModified = $false; NativeHooksInstalled = $false; FullWriterCoverageVerified = $false
    Evidence = 'Offline exact generated declarations/wrapper IL plus selected previously generated static edges. Generic closed native addresses, native ABI, callback coverage and complete writers remain unverified.'
    Assemblies = $assemblyRows; DeclaredTargets = $targetRows; ClosedBaseTargets = $closedRows; MissingNames = $missing
    ValueReturnTypes = $valueRows; RuntimeSHA256 = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
    RuntimeInformationalVersion = $runtimeVersion; GcApiAndWrapperSemantics = @($gcRows); PriorStaticNativeEvidence = $nativeEvidence
}
$reportRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $reportRoot
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$reportPath = Join-Path $reportRoot $ReportName
Assert-NoPathLinks $reportPath
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $reportPath -Encoding utf8
[pscustomobject]@{ ReportPath = $reportPath; DeclaredTargets = $targetRows.Count; ClosedBaseTargets = $closedRows.Count; MissingNames = $missing; FullWriterCoverageVerified = $false } | ConvertTo-Json -Depth 5
