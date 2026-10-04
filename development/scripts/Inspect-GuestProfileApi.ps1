[CmdletBinding()]
param(
    [string]$GamePath,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]{0,63}\.json$')]
    [string]$ReportName = 'guest-profile-api.json'
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$profileGameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $profileGameRoot 'BepInEx\core\Mono.Cecil.dll')

# Only Cecil reads generated declarations/IL. Do not load generated game types,
# invoke getters, read profile files, or instantiate any game/platform service.
function ProfileTypes($types) {
    foreach ($type in $types) {
        $type
        if ($type.NestedTypes.Count) { ProfileTypes $type.NestedTypes }
    }
}
function ProfileBody($method) {
    $instructions = if ($method -and $method.HasBody) { @($method.Body.Instructions) } else { @() }
    $invoke = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'il2cpp_runtime_invoke' }).Count)
    $field = [bool](@($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -like 'NativeFieldInfoPtr_*' }).Count)
    [pscustomobject]@{
        RuntimeInvoke = $invoke; NativeFieldProxy = $field
        Classification = if (!$method) { 'Absent' } elseif ($invoke) { 'RuntimeInvoke' } elseif ($field) { 'NativeFieldProxy' } else { 'OtherOrUnavailable' }
        Calls = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
        Fields = @($instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } | ForEach-Object { $_.Operand.FullName } | Sort-Object -Unique)
    }
}
function ProfileMethod($method) {
    [pscustomobject]@{
        Name = $method.Name; Signature = $method.FullName; ReturnType = $method.ReturnType.FullName
        Static = $method.IsStatic; Abstract = $method.IsAbstract; PInvoke = $method.IsPInvokeImpl
        Generic = $method.HasGenericParameters; Visibility = $method.Attributes.ToString()
        Parameters = @($method.Parameters | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Type=$_.ParameterType.FullName; Out=$_.IsOut; Optional=$_.IsOptional } })
        Body = (ProfileBody $method)
    }
}
function ProfileProperty($property) {
    $accessor = if ($property.GetMethod) { $property.GetMethod } else { $property.SetMethod }
    [pscustomobject]@{
        Name = $property.Name; Type = $property.PropertyType.FullName
        Static = [bool]$accessor.IsStatic; Writable = $null -ne $property.SetMethod
        GetterSignature = if ($property.GetMethod) { $property.GetMethod.FullName } else { $null }
        SetterSignature = if ($property.SetMethod) { $property.SetMethod.FullName } else { $null }
        Getter = (ProfileBody $property.GetMethod); Setter = (ProfileBody $property.SetMethod)
    }
}
function ProfileSelectedMethod($type, $method) {
    if ($method.IsGetter -or $method.IsSetter -or $method.Name -eq '.cctor') { return $false }
    if ($type.FullName -eq 'Steamworks.NativeMethods') { return $method.Name -match 'ISteamRemoteStorage|ISteamUserStats' }
    if ($type.FullName -in @('GameBase','GameManager','PlayerManager')) { return $method.Name -match 'Init|Awake|Start|Save|Load|Restart|Destroy|\.ctor' }
    return $true
}
$profileAssemblyNames = @(
    'Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll','SaveSystem.dll','SaveConverter.dll','Toolbox.dll',
    'TKoU.UniversalSaveSystem.Core.dll','TKoU.UniversalPlatformSystem.Core.dll','TKoU.UniversalAchievementSystem.Core.dll',
    'com.rlabrecque.steamworks.net.dll','UnityEngine.CoreModule.dll','Il2Cppmscorlib.dll','Unity.Microsoft.GDK.Tools.dll'
)
$profileExactTypes = @(
    'GameBase','GameManager','PlayerManager','SaveUtil','GDKSaveLoadModule','SteamAchievements',
    'DR.Save.SaveSystem','DR.Save.SaveLoadManagerBase`1','DR.Save.SaveSystemGameDataManager',
    'DR.Save.SaveSystemPlayerDataManager','DR.Save.SaveSystemPhotoDataManager','DR.Save.SaveSystemUserOptionManager',
    'DR.Save.SaveDataType','DR.Save.SaveSlotType','DR.TCS.FileDataVer0','DR.TCS.FileDataVer1','Toolbox.SaveSystem.SaveManager',
    'Toolbox.SaveSystem.StateSavingManager','Toolbox.SaveSystem.SaveResult','Toolbox.SaveSystem.LoadResult','Toolbox.SaveSystem.DeleteResult',
    'TKoU.UniversalSaveSystem.ISaveSystemService','TKoU.UniversalSaveSystem.Utils.RelativePath','TKoU.UniversalSaveSystem.Utils.Utilities',
    'TKoU.UniversalPlatformSystem.PlatformManager','TKoU.UniversalPlatformSystem.ServiceRepository',
    'TKoU.UniversalPlatformSystem.SteamServiceRepository','TKoU.UniversalPlatformSystem.GDKServiceRepository',
    'TKoU.UniversalAchievementSystem.AchievementManager','TKoU.UniversalAchievementSystem.DefaultAchievementService',
    'TKoU.UniversalAchievementSystem.IAchievementService','Steamworks.SteamRemoteStorage','Steamworks.SteamUserStats',
    'Steamworks.NativeMethods','UnityEngine.PlayerPrefs','UnityEngine.Application',
    'PixelCrushers.SaveSystem','PixelCrushers.SavedGameDataStorer','PixelCrushers.DiskSavedGameDataStorer',
    'PixelCrushers.PlayerPrefsSavedGameDataStorer','SaveConverterTool.SaveConvertSystem'
)
$profileTypes = [System.Collections.Generic.List[object]]::new()
$profileAssemblies = [System.Collections.Generic.List[object]]::new()
$profileServices = [System.Collections.Generic.List[object]]::new()
$profilePathReferences = [System.Collections.Generic.List[object]]::new()
$profileMissingAssemblies = [System.Collections.Generic.List[string]]::new()
foreach ($profileAssemblyName in $profileAssemblyNames) {
    $profileAssemblyPath = Join-Path $profileGameRoot ('BepInEx\interop\'+$profileAssemblyName)
    if (!(Test-Path -LiteralPath $profileAssemblyPath -PathType Leaf)) {
        $profileMissingAssemblies.Add($profileAssemblyName)
        continue
    }
    $profileBeforeHash = (Get-FileHash -LiteralPath $profileAssemblyPath -Algorithm SHA256).Hash
    $profileAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($profileAssemblyPath)
    try {
        $profileAll = @(ProfileTypes $profileAssembly.MainModule.Types)
        foreach ($type in $profileAll) {
            $profileService = $type.FullName -match 'NativeStorageService|StorageService|Save.*Config|Save.*Settings' -or
                [bool](@($type.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'TKoU.UniversalSaveSystem.ISaveSystemService' }).Count)
            if ($profileService) {
                $profileServices.Add([pscustomobject]@{
                    Assembly=$profileAssemblyName; Type=$type.FullName; Base=$type.BaseType.FullName
                    Interfaces=@($type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
                })
            }
            $profileStartupIterator = $type.FullName -match '^(GameBase/_(Init|StartGame|LoadGameData|InitAfterSaveSystem)_d__|DR\.Save\.SaveSystem/_InitSaveSystem_d__|GDKSaveLoadModule/_InitializeSaveSystemService_d__|SteamAchievements/_Start_d__)'
            if ($type.FullName -in $profileExactTypes -or $profileService -or $profileStartupIterator) {
                $profileTypes.Add([pscustomobject]@{
                    Assembly=$profileAssemblyName; Type=$type.FullName; Base=$type.BaseType.FullName
                    Abstract=$type.IsAbstract; Interface=$type.IsInterface; ValueType=$type.IsValueType
                    Interfaces=@($type.Interfaces | ForEach-Object { $_.InterfaceType.FullName })
                    Properties=@($type.Properties | ForEach-Object { ProfileProperty $_ })
                    Fields=@($type.Fields | Where-Object { $_.Name -notlike 'Native*Ptr*' } | ForEach-Object { [pscustomobject]@{Name=$_.Name;Type=$_.FieldType.FullName;Static=$_.IsStatic;Literal=$_.IsLiteral;Constant=if($_.IsLiteral){$_.Constant}else{$null}} })
                    Methods=@($type.Methods | Where-Object { ProfileSelectedMethod $type $_ } | ForEach-Object { ProfileMethod $_ })
                })
            }
            # Declaration references locate other path owners without claiming
            # they are instantiated or part of the actual Steam save pipeline.
            foreach ($property in $type.Properties | Where-Object { $_.Name -match 'SaveFolder|SaveFilePath|SaveDirectory|StorageService|saveSystemService|persistentDataPath|SkipCloud' }) {
                $profilePathReferences.Add([pscustomobject]@{Assembly=$profileAssemblyName;Owner=$type.FullName;Property=(ProfileProperty $property)})
            }
        }
    } finally { $profileAssembly.Dispose() }
    if ((Get-FileHash -LiteralPath $profileAssemblyPath -Algorithm SHA256).Hash -ne $profileBeforeHash) { throw 'Profile declaration input changed during the metadata read.' }
    $profileAssemblies.Add([pscustomobject]@{Name=$profileAssemblyName;SHA256=$profileBeforeHash})
}
$profileRequiredTypes = @('DR.Save.SaveSystem','DR.Save.SaveLoadManagerBase`1','DR.Save.SaveSystemGameDataManager','DR.Save.SaveSystemPlayerDataManager','DR.Save.SaveSystemPhotoDataManager','DR.Save.SaveSystemUserOptionManager','UnityEngine.PlayerPrefs','Steamworks.SteamRemoteStorage')
$profileMissingRequired = @($profileRequiredTypes | Where-Object { $_ -notin $profileTypes.Type })
if ($profileMissingRequired.Count) { throw ('Missing required profile declarations: '+($profileMissingRequired -join ', ')) }
$profilePathMethodCount = @($profileTypes | Where-Object Type -eq 'DR.Save.SaveSystem' | ForEach-Object Methods | Where-Object {
    $_.Name -in @('GetSaveFilePath','GetDemoSaveFilePath','GetFailedSaveFilePath','GetOldSaveFilePath','GetSaveFolder','GetDemoSaveFolder','GetSaveFileName','GetFailedSaveFileName','GetOldSaveFileName','GetSaveFileExtension')
}).Count
$profileLoadMethodCount = @($profileTypes | ForEach-Object Methods | Where-Object { $_.Name -match '^(Load|TryLoad|Reload|ResetAfterCloudLoad|OnLoad)' }).Count

$profileAnalysisRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis'))
Assert-NoPathLinks $profileAnalysisRoot
$profilePriorEvidence = [System.Collections.Generic.List[object]]::new()
foreach ($profilePriorName in @('guest-save-native-calls.json','map-entry-owner-native-calls.json','guest-runtime-cache-native-calls.json')) {
    $profilePriorPath = Join-Path $profileAnalysisRoot $profilePriorName
    if (!(Test-Path -LiteralPath $profilePriorPath -PathType Leaf)) {
        $profilePriorEvidence.Add([pscustomobject]@{Name=$profilePriorName;Available=$false})
        continue
    }
    Assert-NoPathLinks $profilePriorPath
    $profilePriorHash = (Get-FileHash -LiteralPath $profilePriorPath -Algorithm SHA256).Hash
    $profilePrior = Get-Content -LiteralPath $profilePriorPath -Raw | ConvertFrom-Json
    $profileEdges = foreach ($method in $profilePrior.Methods | Where-Object { $_.Type -match 'SaveSystem|SaveLoadManager|GameBase|SceneLoader|IngredientsStorage|MissionManager' }) {
        $profileRelevant = @($method.Edges | ForEach-Object Callees | Where-Object { $_ -match 'DR\.Save\.|System\.IO\.|Steam|Cloud|IngameSaveDataManager|IngredientsStorage|MissionManager|ChangeSceneAsync' } | Sort-Object -Unique)
        if ($profileRelevant.Count) {
            [pscustomobject]@{Caller=$method.Type+'::'+$method.Signature;BodyStatus=$method.BodyStatus;Callees=$profileRelevant;AliasTruncationPresent=[bool](@($method.Edges | Where-Object AliasesTruncated).Count)}
        }
    }
    if ((Get-FileHash -LiteralPath $profilePriorPath -Algorithm SHA256).Hash -ne $profilePriorHash) { throw 'Prior native-call report changed during read.' }
    $profilePriorEvidence.Add([pscustomobject]@{
        Name=$profilePriorName;Available=$true;SHA256=$profilePriorHash
        Evidence='Existing original-PE static direct-call candidates; not runtime order, dataflow, coverage, or a new native analysis.'
        GameCodeExecuted=$false;StaticEdges=@($profileEdges)
    })
}
$profileReport = [pscustomobject]@{
    SchemaVersion=1;ScriptName='Inspect-GuestProfileApi.ps1';GeneratedUtc=[DateTime]::UtcNow.ToString('o')
    Assemblies=@($profileAssemblies);MissingAssemblies=@($profileMissingAssemblies);MissingTypes=$profileMissingRequired
    MissingTypesScope='Required SaveSystem, four managers, open generic base, PlayerPrefs and SteamRemoteStorage declarations only.'
    Types=@($profileTypes);SaveSystemPathMethodDeclarations=$profilePathMethodCount;SelectedLoadMethodDeclarations=$profileLoadMethodCount
    PathReferences=@($profilePathReferences);ServiceDeclarations=@($profileServices)
    NamedNativeStorageServicePresent=[bool](@($profileTypes | Where-Object { $_.Type -match '(^|[./])NativeStorageService$' }).Count)
    ServiceDiscoveryScope='Selected installed generated assemblies only; absent named types do not prove absence of native, platform, generic, or plugin services.'
    ExistingNativeEvidence=@($profilePriorEvidence)
    GameCodeExecuted=$false;GeneratedGameTypesLoaded=$false;NativeApiCalled=$false;NativeHooksInstalled=$false;SavesReadOrModified=$false
    ColdProfileImplemented=$false;InitialPathBindingVerified=$false;FirstLoadOrderVerified=$false;AllCloudOutputsFenced=$false
    PersistentOutputsComplete=$false;GuestStateIsolated=$false;WorldAuthority=$false;CargoAuthority=$false
    Evidence='Cecil reads declarations and generated wrapper IL only. Native field proxies need native execution at runtime; RuntimeInvoke getters are not pure metadata reads. A path or slot setter does not establish complete profile isolation.'
}
New-Item -ItemType Directory -Path $profileAnalysisRoot -Force | Out-Null
$profileOutput = Join-Path $profileAnalysisRoot $ReportName
Assert-NoPathLinks $profileOutput
$profileTemporary = Join-Path $profileAnalysisRoot ('profile-'+[Guid]::NewGuid().ToString('N')+'.tmp')
try {
    $profileReport | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $profileTemporary -Encoding utf8
    Move-Item -LiteralPath $profileTemporary -Destination $profileOutput -Force
} finally { if (Test-Path -LiteralPath $profileTemporary -PathType Leaf) { Remove-Item -LiteralPath $profileTemporary -Force } }
[pscustomobject]@{
    Assemblies=$profileAssemblies.Count;Types=$profileTypes.Count;PathReferences=$profilePathReferences.Count
    SaveSystemPathMethods=$profilePathMethodCount;SelectedLoadMethods=$profileLoadMethodCount;MissingTypes=$profileMissingRequired
    ServiceDeclarations=$profileServices.Count;NamedNativeStorageServicePresent=$profileReport.NamedNativeStorageServicePresent
    ExistingNativeReports=@($profilePriorEvidence | Where-Object Available).Count
    GameCodeExecuted=$false;SavesReadOrModified=$false;ColdProfileImplemented=$false;Output=$profileOutput
} | ConvertTo-Json
