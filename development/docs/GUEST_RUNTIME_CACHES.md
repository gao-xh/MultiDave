# 客机运行缓存的类型与恢复边界

当前源码0.1.38-dev（协议7），本轮实际Core/TCP285/285及插件Build警告视为错误通过，执行前后输入封存一致。新增默认关闭的[客机路线加载前采用](GUEST_MAP_ROUTE_ADOPTION.md)：固定自然入海/Reset/原load来源，在资源加载前安装六个路线根；补齐native路线输入及精确pending-manager来源。见[本轮验证记录](../logs/map-route-adoption-build-verification.json)。未部署/启动或运行native；安装0.1.12、最近潜水0.1.11、默认包0.1.0保持。完整IGP采用/客机隔离/房主世界、每人独立袋分流/容量/负重、员工命中、双端正常返航和冷配置仍待完成。

五个manager根交换之后，旧运行缓存仍可能持有原条目、数组、任务和回调。本页初版0.1.19为离线研究；历史0.1.22继续准备有限typed缓存，插件Build警告视为错误通过，见[comparer摘要](../logs/guest-comparer-build-verification.json)。0.1.22该轮Core输入未改，复用[0.1.21实际176/176及构建](../logs/guest-ingame-cache-build-verification.json)，没有重跑测试。迄今没有运行游戏、安装/清空/恢复游戏缓存或执行Init/Load/Build/克隆/存档。默认ExistingCaches七根桥仍硬拒真实进入/静止；0.1.37的[新Natural五根启动source](GUEST_INITIALIZATION_BOOTSTRAP.md)已接原初始化放行，真实静止仍false，全部权限仍不开放。

Natural profile在已列冷缓存尚未使用、固定原iterator首state0及实际Guest绑定中先安装五个save根，再放行原Init，让缓存自然读取临时根；它不调用旧Ingredients/Ingame helper构造空替代，不处理已加载/已使用的cache。有限直接字段前后检查不能证明完整cache尾部、资源、其它actor或delegate无旧引用。完整缓存隔离和所有持久writer仍待实机，`GuestStateIsolated`、`RuntimeCachesIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority`均false。

[Inspect-GuestRuntimeCacheApi.ps1](../scripts/Inspect-GuestRuntimeCacheApi.ps1) 用 Cecil 读取已生成的游戏、Il2Cppmscorlib 和 Interop.Runtime 元数据与包装器 IL。运行命令：

```powershell
.\development\scripts\Inspect-GuestRuntimeCacheApi.ps1
```

实际报告位于忽略目录 `.local/analysis/guest-runtime-cache-api.json`：25 个类型、6 个 InGameSaveData 派生类、457 条字段代理/属性引用候选和 5 个集合类型。各程序集读前后 hash 相等，`GameCodeExecuted`、`SavesReadOrModified`、`NativeHooksInstalled`、完整缓存隔离及 clone ABI 标志均 false。457 是声明数量，不能证明这些对象已存在、仍存活或共享同一原子树。

## 第一个可实施部分：IngredientsStorage

`IngredientsStorage` 继承 `SingletonNoMono<IngredientsStorage>`。两个可读写的生成 direct field proxy 为：

| 字段 | 类型 |
| --- | --- |
| `m_Storage` | ``Il2CppSystem.Collections.Generic.Dictionary`2<System.Int32,IngredientsData>`` |
| `m_IsLoaded` | `System.Boolean` |

元素 `IngredientsData` 的全部 11 个 direct 字段如下。公共 Entity、ParentTID、Price 等 getter 会走 runtime invoke，不用于只读冻结。

| 字段 | 类型和处理 |
| --- | --- |
| `ingredientsID`、`level`、`parentID`、`rank`、`placeTagMask` | Int32，按实际值复制，不由 TID 猜质量或份数 |
| `type` | IngredientsType 枚举，按值复制 |
| `isNew` | Boolean，按值复制 |
| `counts` | ``Il2CppStructArray`1<Int32>``，新建独立数组并复制所有元素；保留实际长度 |
| `lastGainTime`、`lastGainGameTime` | Il2CppSystem.DateTime，本机确认为真正 CLR struct，按值复制 |
| `_Entity_k__BackingField` | DR.IngredientsEntity，元数据资源引用；是否可作为共享只读叶仍须证明，不能默认所有 Entity 都不可变 |

构造候选的完整声明是 `System.Void IngredientsData::.ctor(System.Int32)`，另有 `System.Void IngredientsData::.ctor(System.IntPtr)` 包装已有对象。没有发现 copy/clone 构造器；IntPtr 构造器不复制对象。原生构造的唯一可解析业务目标为 `DataManager.GetIngredients(int)`，但静态目标不证明构造器没有其他副作用。

未来可在真实已围栏的准备边界，逐条 `new IngredientsData(id)`，再复制上述字段和独立 counts，建立新的 typed Dictionary。不能使用 `new Dictionary(original)`、只换外层 Dictionary 或继续引用原 counts 来宣称深复制。保留原 key，不能未经证明强制把 key、parentID 和 ingredientsID 视为同一身份。

对应 SaveData 路径为 `SaveData.m_IngredientsData : Dictionary<int,IngredientsSave>`。IngredientsSave 的数量、等级和时间为 Obscured 字段，缓存则使用运行整数和 DateTime；二者类型不同，不能只凭同 ID 就认定同指针 alias，或从未验证的 Obscured 布局解码。当前动态写回入口为 `IngredientsData.UpdateSaveData()`，静态直接链指向 `SaveSystem.GetGameSave()` 和 `SaveData.UpdateIngredientsSaveData(IngredientsData)`。

## Init/Build 不是纯准备或恢复

本轮通过 [Inspect-NativeCalls](NATIVE_ANALYSIS.md) 读取原 GameAssembly 和 metadata，生成 `.local/analysis/guest-runtime-cache-native-calls.json`。11 个 selectors、78 个方法记录，Depth=1，方法限额未触顶、根未遗漏、`GameCodeExecuted=false`。相关唯一目标及其限制如下：

| 原方法 | 已解析静态直接目标 |
| --- | --- |
| `IngredientsStorage.Init()` | GetGameSave、IngredientsData(int)、IngredientsSave.set_LastGainTime / set_LastGainGameTime |
| `IngredientsData.UpdateSaveData()` | GetGameSave、UpdateIngredientsSaveData |
| `MissionManager.Load()` | InitMissionList、OrderInProgressList |
| `MissionManager.InitMissionList()` | 清理集合、MissionData.Build、RaiseInProgressChanged |
| `MissionData.Build(DR.Missions)` | AutoMapper.CreateMapper、BuildInternal |
| `MissionData.BuildInternal(DR.Missions)` | GetGameSave，以及保存 Accepted/Closed/Failed/TimeProcess 时间的 setter |
| `MissionData.UpdateSave()` | GetGameSave、UpdateMissionSaveData |
| `LootBox.Load(ILootBoxEventListener)` | StatusManager.GetStatus、RefreshOverweight；还有间接调用 |
| `LootBox.get_m_Box()` | GetGameSave；随后另一地址有 SaveData.Item / GetLootBox 两个候选别名 |

`IngredientsStorage.Load()` 没有可用的 PE runtime-function 范围，主体未猜测；没有边不能推断它安全。其他已知 unwind 片段也不证明全方法可达性、分支、数据流、运行顺序或具体泛型实例。

食材 Init 会修补保存时间；任务 BuildInternal 会修补任务保存时间；InitMissionList 会清理集合并通知观察者。因此不能在 shadow 安装前调用这些方法来构造缓存，更不能调用它们来“恢复”原数据。只有原根、原缓存指针和真实静止边界经核对，才可按保存的身份逐项恢复。入海入口本身还有任务处理，详见 [GUEST_ENTRY_BOUNDARIES](GUEST_ENTRY_BOUNDARIES.md)。

复现本轮原生静态报告：

```powershell
.\development\scripts\Inspect-NativeCalls.ps1 `
  -Method 'IngredientsStorage::Init','IngredientsStorage::Load',`
    'IngredientsData::.ctor','IngredientsData::UpdateSaveData',`
    'LootBox::Load','LootBox::get_m_Box',`
    'IngameSaveDataManager::SaveInGameData',`
    'MissionManager::Load','MissionManager::InitMissionList',`
    'MissionData::Build','MissionData::UpdateSave' `
  -Depth 1 -MaxMethods 128 -ReportName 'guest-runtime-cache-native-calls'
```

## IngameSaveDataManager 的异构临时表

唯一 direct 字段为 `ingameSaveDatas : Dictionary<InGameSaveType,InGameSaveData>`。SaveInGameData、GetInGameData、Clear、Reset 是 runtime-invoke 方法，不是 direct 字段复制。已找到六个派生类型：

| 枚举及值 | 类型与 mutable 子结构 | 已声明构造候选 |
| --- | --- | --- |
| CharacterHealth=0 | CharacterHealthData.HP : float | `.ctor(float)` |
| CharcterEquip=1 | CharacterEquipData._currentEquipInInventory_k__BackingField : List<int>；gunAmmo : int | `.ctor(Dictionary<EquipmentType,SpecDataBase>,int)` |
| CharacterSubHelper=2 | CharacterSubHelperData._subHelperSlots_k__BackingField : ReferenceArray<SubHelperSlotData> | `.ctor(ReferenceArray<SubHelperSlotData>)` |
| CharacterInstallDevice=3 | CharacterInstallDeviceData._currentInstalledDevices_k__BackingField : List<InstallDeviceSaveSlot> | `.ctor()` |
| PuzzleState=4 | PuzzleStateSaveData._keyToSolves : Dictionary<string,bool> | `.ctor()` |
| InGameObject=5 | InGameObjectSaveData._keyToDatas : Dictionary<string,InGameObjectSaveData.Data> | `.ctor()` |

枚举 `CharcterEquip` 拼写来自原声明，不能擅自改成 CharacterEquip。所有派生类另有 IntPtr wrapper 构造，它们不深复制底层对象。

非空表需要按实际 native 类型处理；只复制 base InGameSaveData 或外层 Dictionary 会遗留装备列表、助手 slot、装置 slot 和对象保存 Data。未知派生类型、key/type 不符、重复对象或未知子结构都应拒绝准备，而非略过。0.1.21的[精确子字段](GUEST_INGAME_API.md)与[有限typed合同](GUEST_INGAME_CACHE.md)支持已覆盖内容；非空SubHelperSpecData/live设备子图仍拒绝，不能把原状态改空替代。

原入海入口有 `IngameSaveDataManager.Clear()` 静态目标。它可以帮助定位新潜水临时表的边界，但不证明每条入口分支都必然清空。只有真实入口意图和该清理阶段已证明，才能准备 owned 空表并保留原表待恢复；不能在任意连接时丢弃原非空表或将空表称为完整 clone。

## MissionManager 不能只交换一个字典

MissionManager 的 direct 状态包括 `handlerMap : Dictionary<ZoneType,MissionHandlerAdaptee>`、`Handler`、`_IsLoaded_k__BackingField`、`m_MissionList : Dictionary<int,MissionData>`、InProgress/New/NewVIP 三个 backing List，以及 Clear/Processed/Deferred/UpdatedUnlock 集合、过程 Queue、MissionSequenceQueue、CoroutineHelper、ReactiveProperty/Subject 和事件监听列表。

MissionData 又持有 `m_TaskList : LinkedList<MissionTaskData>`、`m_CurrentTaskNode`、报警列表和时间 verifier；TaskData 持有 ConditionGroup，ConditionData 带 SavedCount/NowCount 与 dependency mapping。MissionHandlerAdaptee 固定引用 `missionManager`，还保存当前 MissionData/MissionTaskData 的 ReactiveProperty。对应保存路径为 `SaveData.m_MissionData : Dictionary<int,MissionDataSave>` 和 `m_MissionManagerData : SaveData.SaveDataMissionManager`。

这些声明证明存在需要纳入检查的路径，尚不能证明哪些条件列表、保存记录和旧任务在运行时同指针。新字典沿用旧 MissionData、旧 Handler、subject 或队列仍会保留原运行状态；直接调 Load/Build 还会发事件或修补当前存档。首个食材缓存步骤不应顺带伪造完整任务切换。任务 graph、listener/delegate、队列和 coroutine 没有完成隔离前，完整缓存权限保持 false。

## LootBox 是保存根依赖与会话状态的组合

`LootBox.m_Box` 是 runtime-invoke getter，返回 IReadOnlyDictionary；它并非可直接赋值的袋字段。静态路径读取当前 GetGameSave，实际可写保存容器是 `SaveData.m_Box` / `m_LobbyBox : Dictionary<string,LootBoxSlot>`，已经属于 GameData 子树。不得为不存在的 LootBox.m_Box backing field 做缓存交换。

LootBox 的 direct 会话状态另有 `m_CharacterStatus`、`m_LootBoxEventListener`、`m_WeightMax`、weight/Price/CargoPrice/Weight/escapePod/overloaded 的 backing floats、`currentDebuffTid`、`AppliedWeightBuffs : List<int>` 和 `SingalRP : ReactiveProperty<ValueTuple<int,ItemSignal>>`。Load 会取得 Status 并更新负重；换保存容器不能自动隔离旧角色状态、监听者和 subject 订阅。LootBoxSlot 还带 Obscured 字段和 GetTimes 列表，CopyFrom 声明不证明列表独立或完整克隆。

这项缓存研究不完成任何个人袋：房主 native 袋仍归房主，员工独立临时袋、容量分流、可信产物及一次入仓桥仍按 [CREW_MODE](CREW_MODE.md) 实现。不能以 shadow 的 LootBox getter、复制袋内容或初始化 bool 作为员工捕获/结算 receipt。

## 后续 typed capture / prepare / validate / restore 合同

以下为0.1.19提出的IngredientCache有限合同，独立于五个manager根；0.1.20已实现的范围见文末专页，不能把原建议当作完整资源验证。

1. Capture 固定本租约、Unity 线程、实际 SingletonNoMono 实例、原 m_Storage 指针和 m_IsLoaded，强持有原容器及原状态。读取有界条目/数组前后核对容器版本和身份；超限、未知字段、读取错误或实例变化拒绝，不截断后报告完整。
2. Prepare 只创建新 dictionary、每条新 IngredientsData 和每条新 counts。原字典及所有原数组均不写；不要调用 Storage.Init/Load/Reset。候选实现可限制 4096 条、每条 16 个 count，这只是待审查的 Mod 配额，超过便失败，并非游戏容量事实。
3. Validate 检查 key/字段值/数组内容一致、容器/元素/数组与原 mutable graph 不相交；资源 Entity 仅在已证实只读 leaf 后才可共享。顶层指针不同和数组等值不能证明所有旧 UI、任务及 observer 已脱离。
4. Install 是一次派发的 direct 字段交换，每项前后核对 fresh 来源、实际实例、围栏和 readback。字典与 loaded flag 是两个字段，不能把部分写入误报成原子操作；须保存每字段 attempt 和 Mixed/Unknown 状态。相同 bool 也不能独立充当身份或代次。
5. Restore 逆序核对每字段当前仍是本租约 owned 状态，再恢复保存的原指针/标量。foreign/unknown 不覆盖；已派发但抛异常只再读，不重写。最后证明原实例、原根和原 cache graph/标量仍一致，并满足真实静止与输出围栏条件后才释放引用。

整体安装应先捕获原五根及 cache，再准备独立副本，随后在同一真实边界安装并验证；不能在原根已切换后才捕获原缓存，或在退出时通过重新 Init 修补原进度。强引用只能保证 GC 存活，不消除消费者 alias 或阻止对象被替换。

已有元数据还包含 IngredientsDetailPanel.m_NowData、IngredientsSellPanel.m_Data、ManagementIngredientDetailPanel 闭包 data、Mission UI/闭包等条目引用；PlayerCharacter 的 charm closure 持有 SavePlayerData，InitSunangEmitterSystem iterator 持有 SaveData，InteriorStorage 持有 GameDataManager。这些旧引用需要实际生命周期/回调边界隔离，不能凭改 current singleton 抹掉。

Dictionary.Entry 包装器继承 Il2CppSystem.ValueType，但本机 CLR `IsValueType=false`；与真正 CLR struct DateTime 不同。别名检查必须展开 Entry 的 key/value 等可变引用，不能将所有 IL2CPP 值类型 wrapper 一律视为无引用 leaf。direct proxy 仍属于原生内存访问，只读元数据和可编译 typed 签名不能代替实际 ABI、深复制、线程、完整 writer 或静止验证。边界未证明之前不连接游戏入口，也不授予客机世界和收益权限。

0.1.20 已实现首个[typed食材缓存合同](GUEST_INGREDIENT_CACHE.md)并接入六步源码，精确字段补充见[GUEST_INGREDIENT_API](GUEST_INGREDIENT_API.md)。原Entry/Entity已知实例图准备独立副本，不把Parent/static目录当只读；实际native、完整缓存和消费者旧引用隔离仍未证。

0.1.21 增加第七[Ingame缓存](GUEST_INGAME_CACHE.md)，[API](GUEST_INGAME_API.md)包含六record、mutable slots/Data、集合声明及exactclass候选。ordinary record object_new+IntPtr未执行；非空助手资源/live gearQueue拒绝，六kind schema不等于六种完整深复制。三known基线在Serialize前捕获闭合，Prepare后strict复查；恢复7→6→Save5，21explicit handles/4Data stamps，第七singlefield无Mixed。

Mission、资源、旧UI/closures、独立actor和其它运行缓存/所有输出仍需实际隔离；0.1.21的176项只验证CLR控制，该版插件Build警告视为错误通过，entry/quiet/native/guest/world/bag权限false。没有个人袋、地图采用或返航收益权限；每人独立容量/负重和完整M3—M7/双端/冷配置目标保持。

## 0.1.22 dictionary comparer 的有限已知图

[独立comparer合同](GUEST_DICTIONARY_COMPARERS.md)及[API](GUEST_COMPARER_API.md)补int/string/InGameSaveType(int32)三key：只核exact Generic/Object与该enum专用Enum，同class独立对象，source pointer/class/kind、aux及原/新引用进入已知审计。Ingredients同样捕获comparer并核对，不把它当资源只读leaf；null原可Capture但Prepare拒绝。禁止以Default/CreateComparer/getter猜原状态、共享或清空；custom/文化/hash-salt未知拒绝，非空views/syncRoot不能顺带略掉。

新dictionary显式(capacity,comparer)先于Add并核对构造后的绑定。普通constructor抛时assignment未发生，PartialConstructorAllocationRetentionVerified=false，不能保证全部未知allocation已持有。七步/21explicit handles/4Data stamps不扩，三known原图仍先于Serialize闭合、Prepare后strict复查；这不证明完整graph/资源、native ABI或实际进入/静止。[0.1.22 历史摘要](../logs/guest-comparer-build-verification.json)的Build警告视为错误通过，0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176，未重跑。全部guest/world/bag权限false，无GUI/Network自动native入口。

[冷档候选](GUEST_COLD_PROFILE.md)只研究首次load/slot/output，未采用，不能借空profile假定Mission/actor/旧引用/其余缓存已隔离。继续真实资源/actor/cache/output、房主地图采用、每人独立容量/负重下的个人捕获和逐产物返航、实际双端及冷配置；完整M3—M7目标保持。
