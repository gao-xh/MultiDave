# 客机自然初始化的五根启动接线

0.1.37-dev（协议6）增加默认关闭的 `Startup/ExperimentalGuestInitialization`。源码已从 Plugin 启动接到实际 Guest 握手、固定原初始化 iterator、已枚举输出围栏和五根影子事务；本轮实际 Core/TCP 为 275/275，新增四组事务夹具使用合成 backend。最终插件 Build 警告视为错误通过，DLL SHA256 `FB608E8763EBC4CCE0F7BF231ED43DD550110A1D438D7FBED12622132C4E71D0`，见[本轮验证摘要](../logs/guest-initialization-build-verification.json)。没有部署、启动游戏、运行这些 native hooks、读取存档或执行克隆/换根；安装0.1.12、最近潜水0.1.11、默认包0.1.0不变。

这条源码接线解决了旧七根桥必须先有运行缓存才能 Prepare 的启动依赖。它允许原游戏在临时五根安装后自然创建缓存，仍没有证明完整存档图、缓存图、在途 writer、原生 ABI 或客机完整隔离。`GuestStateIsolated`、`NativePermission`、世界/捕获/结算权限和 `HostSelectionApplied` 均不因此改变；房主路线/IGP 的原生采用仍未接通。每人独立背包、容量、重量和自身负重惩罚仍是完整目标。

## 启动来源与固定窗口

开关在 `Plugin.Load()` 的前部读取，默认 `false` 不构造此 controller、不安装其 source hooks 或 output fence。启用后先尝试安装 NaturalInitialization 的初始输出围栏，再安装自己的五个自然边界：

| 精确实例声明 | 本轮用途 |
| --- | --- |
| `void GameBase.Awake_Impl()` | prefix 保存实际 GameBase opaque wrapper，postfix 记录原返回 |
| `void GameBase.LoadSavedData()` | prefix/postfix 配对记录本次加载发起及返回；它不是加载完成事件 |
| `void DR.Save.SaveSystem.LoadAllData()` | 保存实际 SaveSystem opaque wrapper和配对原返回 |
| `Il2CppSystem.Collections.IEnumerator GameBase.InitAfterSaveSystem()` | 配对原 factory 和唯一的实际返回 iterator |
| `bool GameBase._InitAfterSaveSystem_d__45.MoveNext()` | 在原首次推进前等待并核对固定来源；放行后继续核同 iterator |

Plugin.Load 的 CLR 线程不是 Unity 线程证明。早期 callback 只在同 installation thread 接受 opaque wrappers 和 CLR 配对标记，不在这时读 Pointer、SaveSystem/manager/cache 字段。`Diagnostics.Update()` 实际到达后才登记 Unity 线程，并要求它与安装及早期 callback 线程一致。错线程、缺配对、重复来源、原异常或另一补丁跳过原方法均锁存失败。

当前 BepInEx 加载来源只证明插件从 `Internal_ActiveSceneChanged` 回调启动，不能排除安装前的 Awake、个人档加载或类型初始化。反射目标解析和 Harmony 安装也可能触发 interop 类初始化；本模式没有首次加载完整覆盖或零原生初始化证明。安装太晚、错过必要来源时拒绝进入或保持暂缓，不补造早期记录。

## Guest 房间与首个原 MoveNext

`NetworkController` 的第一次实际 Update 在此模式下自动按 `Network/HostAddress` 与 `Network/Port` 加入房主。握手完成后把实际 `SessionPeer` 及 canonical Room 固定到启动 source；不能用配置中的地址、房间名或合成 Snapshot 代替这个 peer。此进程只支持初始 Guest 角色/房间，不能切换 Host 或 Local test。

尚未看到实际 Unity Update、配对加载返回或 Guest 握手时，原 `InitAfterSaveSystem` 的 MoveNext 被暂缓：prefix 跳过该次原推进并返回 `true` 以继续等帧，不把等待当作原初始化完成。随后核对：

1. Awake、LoadSavedData 和 factory 的实际 GameBase 指针一致，LoadAllData 的 SaveSystem 仍是当前活实例；固定原 factory 返回 iterator 与本次 MoveNext 的实例相同且 exact class 正确。
2. 该 iterator 的直接 `__1__state == 0`、`__2__current == null`、`__4__this` 是同 GameBase。每次复核使用这个来源，不用当前 singleton 给未知 iterator 重新绑定。
3. 实际 Guest peer 尚未关闭且 Room 不变；SaveSystem 和四个不同 manager、五个非空且不同的根均存在。五根为 Game、Player、PlayerInteraction、Photo、UserOption。
4. 已列冷缓存条件仍成立：没有当前 InGameManager，Mission 未标加载；Ingredients 不已加载，Interior 不已加载/持有 GameDataManager；Mission 没有已列处理 routine/sequence，已列字典、list、queue、set 要么缺失，要么处于直接 size/version/index 检查的未使用状态。前后身份复核失败就拒绝，不调用 Init/Clear 伪造冷状态。
5. 输出围栏健康且 `BlockedFileOperations == 0`，保留实际 GameBase、原返回 iterator、SaveSystem 的 wrapper和最多三个额外 strong GC handles，然后一次 SealInitialization。

这些是当前有限 source 的进入条件，不证明不存在其它 actor、delegate、缓存尾部引用、隐藏图或在途 writer。NaturalInitialization 不要求原三加载 flags 都已为 true：原流程在首次初始化前可能尚未设置它们，完成来源取自上述实际配对和未推进 iterator。旧 ExistingCaches 仍要求三 flags 并硬拒真实进入，不能把新 profile 的条件用来放开旧热切换路径。

## 输出围栏的两个阶段

`GuestOutputFenceProfile.ExistingCaches` 保持原194项立即阻断。NaturalInitialization 精确 inventory 为197项：初始156项阻断，41项内存建根/加载声明暂不安装；原加载返回后、任何 serializer 或换根前，一次安装这41项并读回全部197项自己的 Harmony owner。不是卸除持久 hooks 后再装回。

| 暂未安装的精确声明组 | 数量 |
| --- | --- |
| SaveSystem `LoadGame()`、`LoadGameOnInit()`、`LoadAllData()` | 3 |
| 四个 Game/Player/Photo/UserOption derived manager 的 `CreateManagedData()`、`OnLoadData()` | 8 |
| Player derived manager 的 `LoadData()`、`SetLoadedData(SavePlayerData)` | 2 |
| 四个 closed `SaveLoadManagerBase<T>` 各自的 `TryLoadFromSlot(int,SaveSlotType,out T)`、`TryLoadFromJson(string,out T)`、`SetLoadedData(T)`、`LoadData()`、`CreateNew(bool)`、`CreateManagedData()`、`OnLoadData()` | 28 |

四个 T 是 `SaveData`、`SavePlayerData`、`SavePhotoData`、`SaveUserOptions`。完整声明逐条匹配，不按名称 pattern 广泛放行。`CreateNewAndSave(bool)` 四处仍阻断；没有证明其底层只走已挡写入，不能为缺档流程宣称可用。

新增的三个初始阻断入口只属于 NaturalInitialization：`Il2CppSystem.IO.File.Delete(string)`、`Copy(string,string)`、`Copy(string,string,bool)`。离线 Cecil 已核三条 public static void wrapper，声明缺失会拒绝安装该 profile。任何匹配调用递增 `BlockedFileOperations` 并锁存围栏失败；跳过 void 不是复制/删除成功，source 不得继续进入克隆。未知方法、其它线程、partial patch/seal 或健康丢失保留已有阻断与 owner，不重复安装、不自动 unpatch。

这197条只是已枚举声明，不是197个唯一原生地址、所有文件/云/成就/PlayerPrefs writer覆盖或 ABI 通过。原加载仍可能读个人档；本模式没有改目录、槽位、云设置，也不是从第一次读取就使用新临时档的方案。[原围栏清单](GUEST_OUTPUT_FENCE.md)和[冷档研究](GUEST_COLD_PROFILE.md)保留这些范围限制。

## 五根事务与原缓存自然出生

Seal 成功后，controller 创建绑定自身、真实线程和实际 Room 的 opaque lease，用同一个已封闭 fence 构造 `NativeGuestShadowBridge`，再调用既有 `GuestShadowTransaction.Install()`。新的 `GuestShadowProfile.NaturalInitialization` 固定五步：GameData → PlayerData → PlayerInteraction → PhotoData → UserOption。调用者不能通过几个 bool 自行构造此进入窗口。

桥捕获四 manager、原五根及原 Interaction 的已知 baseline，再以原 typed Serialize/Deserialize 准备四 Data；新 Interaction 将已覆盖容器绑定到 detached Player。prepare、每次 root dispatch、回读和最终 validation 都继续核 source、manager、original scalar、引用及 fence。只交换精确 backing fields，不以 Load/SetLoadedData/Sync 作为恢复。JSON只在内存，不进日志、TCP或文件；原序列化、深图、构造和 typed ABI 均未实机。

五根回读成功仅标记 `RootShadowInstalled`。随后在原 MoveNext 放行之前标记消费者已可能开始使用临时根，再继续原初始化，让 Ingredients、Mission、Interior 等已知原 Init 路径自然读取当前临时根。Natural profile 不调用旧 Ingredients/Ingame cache clone helpers补空表，也不把两个缓存根加入本次事务；原已加载或已使用的缓存由前置拒绝阻止。自然消费者可能写内存临时进度，完整图与所有消费者仍待验证。

桥拥有原五根、detached五根、SaveSystem及四 manager的最多15个额外 strong handles；source另外最多3个。临时 wrapper/子图和序列化内部未知分配不因这些数目成为完整持有或深隔离证明。已知 Interaction 图审计不等于完整 SaveGraph/缓存覆盖；准备或派发未知结果保留全部 owner、fence和引用，不猜重试。

## 进程生命周期与验证边界

本 source 的 `HasQuiescentBoundary()` 仍为 false。Core补偿在任何 RestoreRoot前先核真实 quiet，再重新核 binding和该根身份；quiet false 时不恢复已派发根。安装失败、active validation失败、网络断开或原初始化异常都不热恢复个人根、不卸 fence、不释放 native handles。Disconnect保留临时状态；切回个人角色需要退出并以关闭该开关的新进程启动。未知结束不记正常返航或保存成功。

本轮275项只覆盖实际CLR/TCP代码，新增四个 synthetic backend夹具验证五/七步profile固定、quiet-before-restore及恢复期间撤销；没有运行新五个自然 hook、197 fence或任何 native clone。测试实际执行证据是退出码0、`275/275 tests passed.` 尾部输出与4.55秒工具时长；完整stdout与确切测试起止UTC未保留。86份Core输入hash在成功测试之后采集，没有执行前封存或测试前后字节相同的证据。插件Build单独在 `2026-10-04T17:54:04.5287763Z` 至 `17:54:05.9503523Z` 通过，147份插件执行前封存输入在Build前后字节一致；这些构建事实不补成原生运行证据。

M4房主地图采用没有因五根启动接线完成；可信员工actor/装备/生存与命中、每人独立袋产物分流与容量/负重、真实返航仓库增量/save、双端正常闭环及GitHub冷配置仍未验收。0.1.36地图候选读取与边界研究的历史证据见[MAP_ADOPTION_ENTRY](MAP_ADOPTION_ENTRY.md)及[历史摘要](../logs/map-candidate-build-verification.json)。
