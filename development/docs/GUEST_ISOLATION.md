# 客机原生存档影子桥研究

当前源码0.1.41-dev（协议8），本轮实际Core/TCP311/311及插件Build警告视为错误通过，封存输入执行前后相同。新增[客机鱼隔离与自动观察](GUEST_FISH_QUARANTINE.md)：原鱼出生冻结真实场景来源，隔离记录与引用先于停用；握手只请求房主鱼清单，客机显示仍核实际当前来源。见[本轮验证记录](../logs/guest-fish-isolation-build-verification.json)。未部署/启动或执行native，安装.12/最近潜水.11/默认包.0保持；完整原生生命周期覆盖未证。完整生成/AI/持久隔离、每人独立袋分流/容量/负重、员工命中、远距离活跃区域、双端正常返航保存与GitHub冷配置仍待完成。

员工的游戏应在临时状态上运行，长期进度与返航收益由房主保存。每人仍有独立背包：房主使用自己的原生 `LootBox`，员工的独立容量、重量与物料由房主 Mod 账本持有；不能先加到房主袋再复制。详见 [CREW_MODE](CREW_MODE.md)。

本页给出实际原生影子桥的接口候选与验证合同。本轮仅离线读取互操作元数据、包装器 IL 和原 PE 的有界调用边；没有调用序列化、替换根、读取或写入存档、挂钩或启动游戏。签名可用于实现下一桥，尚不能授予 `GuestStateIsolated`、`WorldAuthority` 或捕获/结算权限。

## 可复现证据

从仓库根目录执行 [Inspect-GuestStateApi.ps1](../scripts/Inspect-GuestStateApi.ps1)：

```powershell
.\development\scripts\Inspect-GuestStateApi.ps1
```

脚本通过 Steam 定位游戏，也支持显式 `-GamePath`。它用已安装的 Mono.Cecil 读取七个生成程序集，不解析或执行游戏类型，不下载依赖；报告写入忽略的 `.local/analysis/guest-isolation-root-api.json`。当前输出为 42 个类型、46 条已类型化根/saveable 引用和 178 条持久输出**签名候选**。候选集合包括读取、路径、回调与备份相关声明，数量不是已证明的写入入口数，也不证明覆盖所有输出。

原生调用研究使用 [Inspect-NativeCalls.ps1](../scripts/Inspect-NativeCalls.ps1)：

```powershell
.\development\scripts\Inspect-NativeCalls.ps1 `
  -Method 'DR.Save.SaveDataBase::Serialize', `
          'DR.Save.SaveDataBase::Deserialize', `
          'SaveData::.ctor', `
          'DR.Save.SavePlayerData::.ctor', `
          'DR.Save.SaveSystemPlayerDataManager+InstanceInteractionData::.ctor', `
          'DR.Save.SaveSystemPlayerDataManager::SyncInstanceDataWithPlayerData' `
  -Depth 2 -MaxMethods 128 -ReportName 'guest-shadow-clone-native-calls'
```

本次六个选择器匹配八个根声明，输出 90 条方法记录；方法上限 128、每方法指令上限 8192，根遗漏和指令截断均为零，方法配额未触顶。54 条记录解码已知 unwind 族，36 条没有包含该入口的 runtime-function 范围，工具没有猜测 leaf 主体。存在共享地址别名截断与未解析间接调用；全 PE 的三个不支持 unwind 项也保留在报告中。所有八个根都有已知范围输出，`GameCodeExecuted=false`。

这不是具体泛型实例的完整控制流分析。唯一直接目标、完整已知 unwind 片段和没有看到文件写入边，均不能证明分支可达性、回调副作用、深复制或输出覆盖。证据限制见 [NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。原始报告与本机路径、地址、第三方 DLL 不提交 Git。

## 内存根与生成字段边界

以下声明来自 `Assembly-CSharp.dll` 的生成包装器。名称带 backing field 的 C# 属性是原生字段代理：getter/setter 读写 native 字段，引用写入还调用 GC write barrier；没有调用原游戏的 `il2cpp_runtime_invoke`。它们不是普通 CLR 字段，仍必须在已确认的 Unity 主线程操作，保存真实对象引用并核对所属实例。

| 所属类型 | 可直接读取和恢复的根 |
| --- | --- |
| `Singleton<DR.Save.SaveSystem>` | 静态 `_instance : SaveSystem`；公共 `Instance` 是原生 getter |
| `DR.Save.SaveSystem` | `_GameDataManager`、`_PlayerDataManager`、`_PhotoDataManager`、`_UserOptionManager`；公共 manager getter 会 RuntimeInvoke |
| `DR.Save.SaveSystemGameDataManager` | `_Data_k__BackingField : SaveData`、`_LastSavedTurnInfo_k__BackingField : ValueTuple<long,int>`、`m_SaveLocks : HashSet<uint>` |
| `DR.Save.SaveSystemPlayerDataManager` | `_Data_k__BackingField : SavePlayerData`、`_InstanceData_k__BackingField : InstanceInteractionData` |
| `DR.Save.SaveSystemPhotoDataManager` | `_Data_k__BackingField : SavePhotoData` |
| `DR.Save.SaveSystemUserOptionManager` | `_Data_k__BackingField : SaveUserOptions` |
| `DR.Save.SaveLoadManagerBase<T>` | `_IsNewData_k__BackingField : bool`；`Data` 是原生虚属性，没有基类通用的直接 Data 根 |
| `DR.Save.SaveDataBase` | `Version`、`BuildVersion`、`lastUpdateLocalTime`、`isPrevDataCorrupted`、`_IsUpdated_k__BackingField`；同名公共状态 getter/setter 的边界另行核对 |

`GameSave`、`Data`、`GetGameSave()`、`CurrentSceneNameForPlayerSave` 及 `InstanceData` 公共入口会调用原游戏方法。桥应保留现有 manager 实例并替换其 Data 字段，避免新建 MonoBehaviour manager。直接根交换只是具体可实现的安装点，不证明原数据的所有引用已经切换。

不能遗漏的状态包括：

- `SaveData` 的 `m_Box`、`m_LobbyBox`、`m_IngredientsData`、`m_MissionData`、`m_CaughtFishData`、`m_FishDropPitySaveData`、`m_SceneMapLayerCacheList` 等完整子树；CLR 摘要或只复制金币/鱼库不足以隔离。
- Player 的 `InstanceInteractionData` 内设备列表、使用过的交互/可破坏物/蟹笼/随机器、独占物、IGP 列表、`_usedIGPSetRuntimeSet_k__BackingField` 和各更新标记。`new InstanceInteractionData(bool needUpdate)` 的参数不是克隆数据。
- `IngameSaveDataManager.ingameSaveDatas`、`IngredientsStorage.m_Storage/m_IsLoaded`、任务运行缓存、LootBox 状态；这些运行对象不因换一个 SaveData 引用自动冻结或恢复。
- `IGPSetController._cachedSaveable : ISaveableInstanceData`，以及随机器、Jungle 对象缓存的 saveable/`InstanceDataSaveBehaviour`。Jungle 的 `m_GameSave` 反向引用与日常/RPG 交互状态也需归属核对。
- 已存在的 delegate/iterator 引用：`PlayerCharacter.__c__DisplayClass464_0/464_1.playerData`、`InGameManager._InitSunangEmitterSystem_d__177._save_5__2` 等。入海后仅换全局根不能改写它们已捕获的原对象；类型化扫描也不能排除经 `Object`、容器或更深子对象保留的引用。

## 两条实际原生克隆候选

已核实静态声明：

```csharp
string DR.Save.SaveDataBase.Serialize<T>(T data) where T : DR.Save.SaveDataBase;
T DR.Save.SaveDataBase.Deserialize<T>(DR.Save.SaveDataType type, string jsonVal)
    where T : DR.Save.SaveDataBase;
```

下一桥可用以下两个真实包装器表达式准备 detached shadow；本轮没有执行它们：

```csharp
var gameShadow = DR.Save.SaveDataBase.Deserialize<SaveData>(
    DR.Save.SaveDataType.GameData,
    DR.Save.SaveDataBase.Serialize<SaveData>(originalGame));

var playerShadow = DR.Save.SaveDataBase.Deserialize<DR.Save.SavePlayerData>(
    DR.Save.SaveDataType.PlayerData,
    DR.Save.SaveDataBase.Serialize<DR.Save.SavePlayerData>(originalPlayer));
```

`SaveDataType` 明确为 `UserOption=1`、`PlayerData=2`、`GameData=3`、`PhotoData=4`。静态原生边确认 Serialize 包含原生 Newtonsoft `SerializeObject(Object,Formatting)` 和 Unity `JsonUtility.ToJson(Object)` 目标，Deserialize 包含原生 Newtonsoft `DeserializeObject<T>(string)` 和 Unity `JsonUtility.FromJson<T>(string)` 目标。canonical 泛型主体有地址与范围，具体 `T` 的分支、默认 settings/delegate、私有 Obscured 状态覆盖和原生序列化回调仍未验证。

因此应先安装持久输出围栏，再执行有界克隆，并核对新对象及可变子树没有指向原根、版本与必要数据有效、双向交互缓存和反向引用完整。临时 JSON 仅留内存，不写日志或协议。普通 CLR JSON 序列化生成 wrapper 无法据此复制原生 Obscured 私有状态；native JSON round trip 也不是任意运行缓存的内存快照。

`SaveData(string ver)` 与 `SavePlayerData(string ver)` 的参数名明确为 `ver`。原生构造器包括初始化子对象，SaveData 的版本构造器还有时间戳相关目标，不能把 JSON 传进此构造器冒充克隆。`IntPtr` 构造器只包装现有对象，也不会克隆它。

`SaveSystemPlayerDataManager.SetLoadedData(SavePlayerData)` 的实际直接目标包括基类 SetLoadedData、创建 InstanceInteractionData 和 Sync。`SyncInstanceDataWithPlayerData()` 又触及 `LoadRuntimeIGPHashData()`，并有十次未解析间接调用。`LoadData()` 包含云加载、转换和文件复制/删除路径。这些入口不能当作纯字段交换或恢复；重建交互缓存若选用原 Sync，必须另行验证其完整副作用并在 shadow/输出围栏内执行。

## 必须覆盖的持久输出声明

下面是可用于下一桥定位的精确入口组；除已记录的静态调用边外，不声称每条都会在 Steam 客机路径执行或已被拦截。返回值为 bool 的阻断应明确失败，不能伪报保存成功。泛型共享原生地址和 struct 返回 ABI 也需验证自己的挂钩覆盖。

| 类型/程序集 | 声明 |
| --- | --- |
| `DR.Save.SaveSystem` / Assembly-CSharp | `void SaveAllData()`、`bool TrySaveGameData()`、`bool SaveGameData()`、`bool SaveGameDataInSlot(int,bool)`、`bool SaveGameDataInSlot(int,bool,SaveSlotType)`、`void SavePhotoData()`、`void DeleteGameData()` |
| `DR.Save.SaveLoadManagerBase<T>` / Assembly-CSharp | `void SaveData(bool)`、`bool SaveOnSelectedSlot(int,SaveSlotType)`、`bool SaveSlotWithJson(string,int,SaveSlotType)`、`void SaveBackupData()`、`void CreateNewAndSave(bool)`、`void DeleteSaveFile()`、`void DeleteSaveFile(string)`、`void WriteOldFileOnConvert(string,string)` |
| 同一基类 / Assembly-CSharp | `void CopyFileToCloud(int,bool)`、`void WriteOnCloud(string,int,SaveSlotType)`、`void WriteAllAutoSaveOnCloud(string)`、`void LoadFromCloud(int,SaveSlotType)`、`void LoadAllFromCloud()`；拉取/转换也可能改本地文件 |
| Game/Photo manager / Assembly-CSharp | 各自 `void SaveData(bool)`、`void DeleteSaveFile()`；Game 另有 `void CopyDemoSaveFiles()`；继承的各 closed generic 实例不能仅凭源码名称假定都受同一 patch 保护 |
| `GDKSaveLoadModule` / Assembly-CSharp | `void SaveData(string,Il2CppStructArray<byte>)`、`void DeleteData(string)`、`void DeleteFiles(Il2CppStringArray)`；在 Steam 上是否使用尚未证明 |
| `TKoU.UniversalSaveSystem.ISaveSystemService` / TKoU.UniversalSaveSystem.Core | `void FileWriteBytes(RelativePath,Il2CppStructArray<byte>)`、`void FileDelete(RelativePath)`；接口声明不是所有具体实现的拦截点 |
| `Toolbox.SaveSystem.SaveManager` / SaveSystem | `SaveResult Save(Object,int,bool)`、`DeleteResult Delete(int)`；这是额外系统的候选，不能默认与 DR.Save 是同一流程 |
| `Steamworks.SteamRemoteStorage` / com.rlabrecque.steamworks.net | `bool FileWrite(string,Il2CppStructArray<byte>,int)`、`SteamAPICall_t FileWriteAsync(string,Il2CppStructArray<byte>,uint)`、`bool FileDelete(string)`、`bool FileForget(string)`、`SteamAPICall_t FileShare(string)`、`bool SetSyncPlatforms(string,ERemoteStoragePlatform)`，另有 stream open/write/close/cancel 与 batch 声明 |
| `SteamAchievements` / Assembly-CSharp；`Steamworks.SteamUserStats` / steamworks | 进度同步/成就与 stat 写入候选包括 `UnlockProgressSyncFromSave()`、`UnlockAchievement(string)`、`UnlockAchievementWithStat(string,string,int)`、`SetStat(string,int/float)`、`SetAchievement(string)`、`ClearAchievement(string)`、`StoreStats()` |
| `UnityEngine.PlayerPrefs` / UnityEngine.CoreModule | `void SetInt(string,int)`、`void SetFloat(string,float)`、`void SetString(string,string)`、`void DeleteKey(string)`、`void DeleteAll()`、`void Save()`；尚未证明哪些键属于游戏进度，不能泛化为全局禁用偏好设置 |

已观察的原生基类 SaveData 静态路径包括 Serialize、加密、目录创建、`System.IO.File.WriteAllText(string,string)`；LoadData 包括读、转换、Copy/Delete。只拦最终 SaveGameData 或只改保存目录均不足以覆盖槽位、备份、云端及成就，也不能阻止仍指向原对象的内存进度被修改。`SaveData.DeleteSerializedUserData<T>(ref T)` 是另一个内存用户数据声明，方法名不能直接当作文件删除证据。

## 下一桥的最小安装与恢复合同

以下为0.1.17研究时的安装/恢复建议，当前七步源码范围见文末；原生实测仍未进行。桥要保存 manager/root/cache 的实际 native 引用及生命周期身份，使用强存活保证，不能只存可复用的数字指针。

1. 在确认本地加载完成、尚未创建员工本次世界对象的边界，先接管持久输出并排除在途保存、load、delegate 和 coroutine。房间状态或一个 Scene 名称不证明该边界。已有场景/旧 saveable 无法确认退休时拒绝进入。
2. 同时准备 Game、Player 和 Interaction shadow。Photo/UserOption 的可写进度必须另有 shadow 或明确阻断；运行缓存逐项准备与核对。先完整准备、验证 detached 数据，再交换直接根，不能在半安装期间放行 guest 场景或动作。
3. 保持 manager 实例，使用 `_Data_k__BackingField`、`_InstanceData_k__BackingField` 等直接字段代理交换；逐项读回确认。`ShadowRootsInstalled` 仅说明根交换匹配，仍不等于 `GuestStateIsolated` 或持久输出覆盖已验收。缺缓存、旧别名、克隆失败、异常或不可覆盖 writer 均保持 native 权限关闭。
4. 恢复时仍保持输出围栏，停用员工世界对象并排除在途操作；确认当前 manager/root 正是本 lease 安装的 shadow，再按原引用恢复所有根、脏标记和缓存。原指针恢复并不能证明嵌套原数据从未被旧引用修改。发生未知替换、恢复不完整或仍有 guest callback 时，不能卸载围栏后恢复普通存档写入；应进入需要重启的失败状态。

员工断线不清房主潜水账本，客机 shadow 也不回写为自己的进度。两袋结算由房主真实产物/捕获/返航证据驱动：房主原袋不重复 Add，员工未入仓条目由新的原生 bridge 逐项确认一次。shadow 安装、bool 保存返回或副本消失不能作为捕获/入仓 receipt；未知结果不重放。完整采用、个人袋分流和双游戏验证仍待完成。

## 0.1.18 原生根桥与输出围栏源码

0.1.18新增实际typed原生影子桥、单次事务及已枚举输出围栏源码。四类Data原生JSON round trip、五根直接交换/回读/恢复和15个独立强handle已编译；7组新增事务夹具以合成backend验证partial/unknown补偿、fence/refs保留和一次清理，总167/167通过。

0.1.18该历史阶段的生产进入与静止边界恒false，事务在围栏安装前拒绝；当时startup primitive自身再查边界，未接Network/GUI，未运行克隆、根交换、阻断或恢复。194条精确声明不是所有writer、独立native地址或ABI证明；Interaction未Sync、完整子树/旧缓存/协程隔离仍待完成。0.1.37新Natural五根启动接线见GUEST_INITIALIZATION_BOOTSTRAP；旧ExistingCaches七根仍硬拒，GuestStateIsolated/NativePermission/WorldAuthority/CargoAuthority仍false，迄今未部署或启动新版。

实现与下一步见[原生根桥](GUEST_SHADOW_BRIDGE.md)、[输出围栏](GUEST_OUTPUT_FENCE.md)及[0.1.18构建摘要](../logs/guest-shadow-build-verification.json)。下一步必须实现可信原生进入/静止边界与缓存/Interaction切换，再进行受控实机验证；个人袋分流、真实地图采用及双游戏闭环仍按原计划推进。

0.1.19已实现[十组typed交互绑定](GUEST_INTERACTION_SHADOW.md)并接入根桥源码；实际进入仍关闭。其它缓存与副作用见[GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)，原入口保护时机见[GUEST_ENTRY_BOUNDARIES](GUEST_ENTRY_BOUNDARIES.md)。已知字段等值和别名检查是必要条件，完整基线/深复制/所有旧引用仍未证。

0.1.20 将[typed食材缓存](GUEST_INGREDIENT_CACHE.md)接入六步捕获/准备/恢复。实际SingletonNoMono字段与Entity实例/共享资源边界见[精确API](GUEST_INGREDIENT_API.md)；已编译、174项CLR/TCP测试通过，未执行native，GuestStateIsolated仍false。原五个Save根、Interaction基线及Data标量不由第六缓存替代。

## 0.1.21 第七临时缓存候选

0.1.21版源码/协议5的176项Core测试通过、插件Build警告视为错误通过，见[该版摘要](../logs/guest-ingame-cache-build-verification.json)。[Ingame精确接口](GUEST_INGAME_API.md)与[typed缓存](GUEST_INGAME_CACHE.md)增加第七单字段步骤；六kind schema仅支持已覆盖子图，不是六种完整deep clone。non-null助手ScriptableObject资源和live设备队列明确拒绝，不能用共享指针或空状态代替。

三known原图（Interaction/Ingredients/Ingame）在Serialize之前捕获并闭合核对，Prepare全部完成后严格重查。五Save根、两个cache的最终安装/确认共七步，恢复7→6→Save5；第七singlefield拒OwnedMixed，显式handles上限21、四Data scalar stamps仍4。普通record exactclass/object_new+IntPtr候选未运行；完整原树、资源、旧引用/actor/其它cache/输出与真实静止未证明。

entry/quiet/native/guest/world/bag权限仍false，未接自动入口、未部署或启动。当前安装0.1.12/最近潜水0.1.11/default0.1.0及用户试玩延后保持；每人独立容量/重量/负重、房主地图采用、个人真实捕获/返航和完整M3—M7/双端/冷配置验收仍必需。

## 0.1.22 有限 comparer 与冷档研究

0.1.22历史源码0.1.22-dev/协议5，[0.1.22 历史摘要](../logs/guest-comparer-build-verification.json)的插件Build警告视为错误通过。0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176而未重跑；未部署、启动或执行native。七步/21explicit handles/4Data stamps维持，所有ABI、完整graph/cache/isolation、entry/quiet/native/guest/world/bag权限false，无GUI/Network自动入口。

[独立comparer合同](GUEST_DICTIONARY_COMPARERS.md)和[API](GUEST_COMPARER_API.md)仅支持候选白名单：int/string/InGameSaveType(int32)精确Generic/Object，以及仅该enum的Enum。原comparer pointer/class/kind与aux参与审计；Ingredients同规则。null原可Capture但Prepare拒绝，不调用Default/CreateComparer/getter猜当前默认、不共享或清空；custom/文化/hash-salt未知拒绝。准备新表先显式(capacity,comparer)再Add并核对，不能凭没有已声明native实例字段证明行为只读或全图独立。普通constructor抛时assignment未完成，PartialConstructorAllocationRetentionVerified=false，不能保证其内部所有未知allocation已Hold。

[GUEST_COLD_PROFILE](GUEST_COLD_PROFILE.md)只提出更直接的首次加载路径、slot与输出研究，尚未采用，不意味着临时目录已隔离缓存、原资源、actor或全持久输出。实际边界与其余资源/actor/cache/output、房主地图采用、每人独立袋/容量/负重的个人捕获和逐产物返航、实际双端及GitHub冷配置验收仍按完整M3—M7推进。
