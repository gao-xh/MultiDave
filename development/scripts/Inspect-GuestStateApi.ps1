[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-isolation-root-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
$interopRoot = Join-Path $gameRoot 'BepInEx\interop'
Add-Type -Path (Join-Path $gameRoot 'BepInEx\core\Mono.Cecil.dll')
# Cecil reads generated wrapper metadata and IL; it never resolves or executes game types.
function AllGuestTypes($sourceTypes) {
    foreach ($type in $sourceTypes) { $type; if ($type.NestedTypes.Count) { AllGuestTypes $type.NestedTypes } }
}
function BodyFacts($method) {
    $instructions = @()
    if ($null -ne $method -and $method.HasBody) { $instructions = @($method.Body.Instructions) }
    $calls = @($instructions | Where-Object { $_.OpCode.FlowControl.ToString() -eq 'Call' } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    $fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    [pscustomobject]@{ RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match 'il2cpp_runtime_invoke' }).Count); NativeFieldProxy = [bool](@($fields | Where-Object { $_ -match 'NativeFieldInfoPtr_' }).Count); Calls = $calls; Fields = $fields }
}
function MethodFacts($method) {
    [pscustomobject]@{
        Signature = $method.FullName; Name = $method.Name; Static = $method.IsStatic; Virtual = $method.IsVirtual
        Constructor = $method.IsConstructor; Visibility = $method.Attributes.ToString()
        ReturnType = $method.ReturnType.FullName
        GenericParameters = @($method.GenericParameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Attributes = $_.Attributes.ToString(); Constraints = @($_.Constraints | ForEach-Object { $_.ConstraintType.FullName }) } })
        Parameters = @($method.Parameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.ParameterType.FullName; Optional = $_.IsOptional; Default = $_.Constant } })
        Body = (BodyFacts $method)
    }
}
$wanted = @('SaveData','DR.Save.SavePlayerData','DR.Save.SaveDataBase','DR.Save.SaveLoadManagerBase`1','DR.Save.SaveSystem','DR.Save.SaveSystemGameDataManager','DR.Save.SaveSystemPlayerDataManager','DR.Save.SaveSystemPlayerDataManager/InstanceInteractionData','DR.Save.SaveSystemPhotoDataManager','DR.Save.SaveSystemUserOptionManager','SavePhotoData','DR.Save.SaveUserOptions','DR.Save.SaveDataType','DR.Save.SaveSlotType','DR.Save.InstanceDataSaveType','DR.Save.ISaveableInstanceData','IngameSaveDataManager','InGameSaveData','InGameSaveObject','JDLC.InstanceDataSaveBehaviour','JDLC.SaveDataJungle','DR.InteriorStorage','IngredientsStorage','LootBox','MissionManager','Singleton`1','SingletonNoMono`1','SteamAchievements','SaveUtil','GDKSaveLoadModule','IGPSetController','SavedRandomActivator','JDLC.JungleLootable','JDLC.JungleSpawner','JDLC.JungleGatherable','JDLC.JVillageSpawnManager','Steamworks.SteamRemoteStorage','Steamworks.SteamUserStats','UnityEngine.PlayerPrefs','UnityEngine.JsonUtility','Newtonsoft.Json.JsonConvert','TKoU.UniversalSaveSystem.ISaveSystemService')
$assemblyNames = @('Assembly-CSharp.dll','SaveSystem.dll','TKoU.UniversalSaveSystem.Core.dll','com.rlabrecque.steamworks.net.dll','UnityEngine.CoreModule.dll','UnityEngine.JSONSerializeModule.dll','Newtonsoft.Json.dll')
$reports = @()
foreach ($assemblyName in $assemblyNames) {
    $assemblyPath = Join-Path $interopRoot $assemblyName
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($assemblyPath)
    try {
        $all = @(AllGuestTypes $assembly.MainModule.Types)
        $selected = @($all | Where-Object { $_.FullName -in $wanted })
        $types = foreach ($type in $selected) {
            [pscustomobject]@{
                Name = $type.FullName; Base = $type.BaseType.FullName; Interfaces = @($type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
                GenericParameters = @($type.GenericParameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Attributes = $_.Attributes.ToString(); Constraints = @($_.Constraints | ForEach-Object { $_.ConstraintType.FullName }) } })
                Properties = @($type.Properties | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.PropertyType.FullName; Static = [bool]$_.GetMethod.IsStatic; Writable = $null -ne $_.SetMethod; Getter = (BodyFacts $_.GetMethod); Setter = (BodyFacts $_.SetMethod) } })
                LiteralFields = @($type.Fields | Where-Object IsLiteral | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Value = $_.Constant } })
                Methods = @($type.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter -and $_.Name -ne '.cctor' } | ForEach-Object { MethodFacts $_ })
            }
        }
        $rootReferences = foreach ($type in $all) {
            foreach ($property in $type.Properties | Where-Object { $_.PropertyType.FullName -match '^SaveData$|^DR\.Save\.SavePlayerData$|^SavePhotoData$|^DR\.Save\.SaveUserOptions$|SaveSystem(GameData|PlayerData|PhotoData|UserOption)Manager|InstanceInteractionData|^JDLC\.InstanceDataSaveBehaviour$|^DR\.Save\.ISaveableInstanceData$' }) {
                [pscustomobject]@{ Owner = $type.FullName; Name = $property.Name; Type = $property.PropertyType.FullName; Static = [bool]$property.GetMethod.IsStatic; Getter = (BodyFacts $property.GetMethod); Setter = (BodyFacts $property.SetMethod) }
            }
        }
        $outputCandidates = foreach ($type in $all) {
            $matches = @($type.Methods | Where-Object {
                !$_.IsConstructor -and !$_.IsGetter -and !$_.IsSetter -and (
                    ($type.FullName -match '^DR\.Save\.(SaveSystem|SaveLoadManagerBase)' -and $_.Name -match 'Save|Write|Cloud|Delete|Copy|Load|SetLoaded|Json') -or
                    ($type.FullName -match '^Steamworks\.(SteamRemoteStorage|SteamUserStats|NativeMethods)$' -and $_.Name -match 'FileWrite|FileDelete|FileForget|FileShare|SetSyncPlatforms|StoreStats|SetAchievement|ClearAchievement|SetStat|SetUserPublishedFile') -or
                    ($assemblyName -in @('SaveSystem.dll','TKoU.UniversalSaveSystem.Core.dll') -and $_.Name -match 'Write|Save|Delete|SetUserData|ToJson|FromJson') -or
                    ($type.FullName -in @('SteamAchievements','GDKSaveLoadModule') -and $_.Name -match 'Unlock|Lock|Sync|Save|Write|Delete') -or
                    ($type.FullName -eq 'UnityEngine.PlayerPrefs' -and $_.Name -match 'Set|Delete|Save') -or
                    ($type.FullName -eq 'SaveData' -and $_.Name -match 'SerializedUserData|ToJson|FromJson'))
            })
            foreach ($method in $matches) { [pscustomobject]@{ Owner = $type.FullName; Facts = (MethodFacts $method); Classification = 'Signature candidate only: name does not prove actual persistent effect or full coverage.' } }
        }
        $reports += [pscustomobject]@{ Name = $assemblyName; SHA256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash; Types = @($types); DirectRootReferences = @($rootReferences); PersistentOutputCandidates = @($outputCandidates) }
    } finally { $assembly.Dispose() }
}
$report = [pscustomobject]@{
    SchemaVersion = 1
    ScriptName = 'Inspect-GuestStateApi.ps1'
    GameCodeExecuted = $false
    SavesReadOrModified = $false
    GeneratedUtc = [DateTime]::UtcNow.ToString('o')
    Evidence = 'Offline generated interop metadata and wrapper IL only. No original native bodies in this report, no game or save APIs executed, no saves read or modified.'
    Scope = 'Known native roots, clone/restore signatures, generated native field getter/setter proxies, root aliases and persistent-output signature candidates. Not an exhaustive proof of all writers.'
    Assemblies = $reports
}
$reportRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $reportRoot
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$reportPath = Join-Path $reportRoot $ReportName
Assert-NoPathLinks $reportPath
$report | ConvertTo-Json -Depth 18 | Set-Content -LiteralPath $reportPath -Encoding utf8
[pscustomobject]@{ ReportPath = $reportPath; Assemblies = $reports.Count; Types = @($reports | ForEach-Object Types).Count; RootReferences = @($reports | ForEach-Object DirectRootReferences).Count; PersistentCandidates = @($reports | ForEach-Object PersistentOutputCandidates).Count } | ConvertTo-Json
