# 玩家与摄像机发现

当前源码为 0.1.24-dev、协议 6，插件构建警告视为错误通过，本轮实际 Core/TCP 测试 200/200 通过。[个人袋账本同步](CARGO_TRANSPORT.md)接入只读分页通道，分别保留每人的容量、重量记录和捕获物；账本重量不代表实时原生采样，房主清单非原袋全量。断线保留账本，新连接不自动继承旧成员。范围见[当前摘要](../logs/cargo-transport-build-verification.json)；新版未部署/启动，安装 0.1.12、最近潜水 0.1.11、默认发行包 0.1.0 保持。真实成员/容量及完整产物分流、返航入仓、客机隔离、房主世界采用与双端/冷配置仍待完成。

对应开发计划 M1。发布包仍为 0.1.0 加载原型；本页 M1 运行证据来自 0.1.1-dev。
当前源码与后续阶段见 HANDOFF 和 MULTIPLAYER。

## 离线证据

在已验证的 Steam Build `25315876` / Unity `6000.0.52f1` 上，使用 Mono.Cecil
读取本机 BepInEx 生成的互操作程序集，不加载或执行游戏程序集。
运行 `scripts/Inspect-GameApi.ps1` 可重复生成报告，保存在忽略的 `.local/analysis/`。

| 类型/成员 | 已知签名与用途 | 尚待确认 |
| --- | --- | --- |
| `PlayerCharacter : BaseCharacter : MonoBehaviour` | `transform`、`moveInput`、`LookDirection`、`characterAnimator` 可读取角色状态 | 不同模式中是否有多个实例及其生命周期 |
| `PlayerCharacter.Init(Vector3, bool, FlipState)` | 存在初始化入口 | 原始控制流、输入注册和任务监听副作用 |
| `InGameManager.playerCharacter` | 实例属性，类型为 `PlayerCharacter` | 船上和潜水时实际指向的实例 |
| `InGameManager.LoadCharacter / InstantiateCharacter(GameObject)` | 存在角色加载和生成入口 | 异步资源流程、单例及持久化行为 |
| `CameraManager.PrimaryTargetTransform / SecondTargetTransform` | 可读取跟随目标 | 跟随玩家根节点还是子节点，剧情时是否变化 |
| `PerspectiveCameraManager.m_Target`、`OrthographicCameraManager.Target` | 其他摄像机模式的目标 | 哪些场景使用这些管理器 |
| `CharacterController2D` | 包含刚体、碰撞体、移动与朝向属性 | 与玩家的组合方式及碰撞控制边界 |

互操作 DLL 的方法体是本机调用封装，不能当作原始游戏实现。
以上成员尚未用于创建第二角色、改变输入、设置摄像机目标或写入进度。

## 已观察到的潜水实例

用户实际进入 `A01_01_01` 潜水后，探针读到 `PlayerGroup(Clone)/DaveCharacter`。
`InGameManager.playerCharacter` 指向该 `PlayerCharacter`；
`CameraManager.PrimaryTargetTransform` 对应同一 GameObject 的根 Transform，
`SecondTargetTransform` 为 null。潜水中存在一个动画层，状态 hash 和时间随移动变化。
朝向曾读到 `(1, 0)`、`(-1, 0)` 和斜向，翻面伴随 Transform 旋转变化。
这些结果只覆盖本次基础潜水实例，其他场景和模式仍需实测。

同一次运行又进入 `Boss_000`：玩家组件和 GameObject 实例 ID 更换，旧实例在该场景
采样中消失，新玩家继续被 `InGameManager` 和 `CameraManager` 正确绑定，未出现读取错误。
该证据覆盖玩家探针的场景切换和实例替换。

## 运行探针

`src/DaveCoop/Discovery/PlayerProbe.cs` 在 Unity `Update` 主线程运行：

- 每 2 秒寻找活动对象上的玩家、游戏管理器及三类摄像机管理器。
  使用对象查找，避免调用可能创建单例的全局 `Instance` 入口。
- 每 0.5 秒读取位置、旋转、缩放、移动输入、朝向、动画状态及摄像机目标。
- 比较管理器引用与玩家组件实例 ID；通过目标 GameObject 及其父节点 ID
  判断摄像机是否跟随玩家，保留名称、路径和场景句柄供复核。
- 每次拓扑变化和每 5 秒的玩家状态写入 BepInEx 日志；F9 立即扫描并采样。
- 结构化 JSONL 位于游戏的 `BepInEx/plugins/DaveCoop/logs/discovery-*.jsonl`。
  默认每次启动最多 9000 条快照；达到上限后停止文件记录，保留实时观察。
- 各读取点独立处理托管异常并记录失败键；序列化仅接收普通 CLR 数据。
  托管辅助方法标记 `HideFromIl2Cpp`，只有 Unity 回调暴露给运行时。

配置文件：`BepInEx/config/local.davecoop.prototype.cfg`。
`Discovery.EnablePlayerProbe` 控制探针，`Discovery.MaxSnapshots` 范围 1..18000。
F8 显示或隐藏面板。该探针没有网络功能。

## 复现与验收

从仓库根目录执行：

```powershell
.\development\scripts\Inspect-GameApi.ps1
.\development\scripts\Build-Plugin.ps1
# 保存并退出游戏后部署，然后从 Steam 正常启动游戏。
.\development\scripts\Deploy-Plugin.ps1
.\development\scripts\Check-Status.ps1
```

开发版使用 Build/Deploy。根入口 `setup.ps1` 安装的是 `distribution/` 中的发布包。

进入存档、船上及一次潜水，移动与转向约 20 秒，按 F9，然后正常返航、保存退出。
在实际会话期间或退出后运行：

```powershell
.\development\scripts\Get-DiscoverySummary.ps1
```

汇总保存为 `.local/analysis/discovery-summary.json`。检查场景时间线、玩家 ID、
管理器绑定、位置/朝向采样、非零输入、动画和摄像机跟随次数，以及 `ProbeErrors`。
`CurrentProcessSession` 仅在对应游戏进程仍运行时为真；退出后的报告属于历史证据。
汇总中的玩家观察不自动等同于潜水验收，需结合实际试玩与场景时间线确认。

## 后续设计约束

新增原GameAssembly离线调用分析工具：`scripts/Inspect-NativeCalls.ps1`。
精确选择器、unwind代码片段、复现命令和实际产物/加载/保存目标见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。
它不执行游戏、挂钩或读存档；静态direct target及共享别名不证明运行顺序或原生能力。
该离线工具轮没有修改历史0.1.15插件或扩大其142项/实机范围。
随后0.1.16新增`scripts/Inspect-MapOriginApi.ps1`，可复现精确入口/参数/nestediterator/字段代理与runtimeinvoke区分，仅写.local元数据报告。
默认关闭的29处MapOrigin挂钩已按这些签名编译；固定entry与iterator、操作指针/版本和实际Scene句柄，再记录controller出生。
仅主线程保留有限operation wrapper并读取直接字段，排队/registry仍纯CLR；框架同commit核对不证明本游戏typedreturn/Scene值ABI。
6组synthetic registry测试通过（总148），未部署/启动或执行挂钩，全部权限保持false；范围见[MAP_ORIGINS](MAP_ORIGINS.md)。

M2 先研究只复制显示组件/姿态的方式。直接克隆完整 `PlayerCharacter` GameObject
可能运行 Awake、注册输入和其他监听；在明确这些行为之前，不尝试完整角色克隆。
远程显示对象不作为游戏管理器的本地玩家，也不绑定本地摄像机。

## 0.1.17 当前接线与下一桥

0.1.17 将当前固定来源 CLR 清单接入候选发送：建房时记录实际 Run/owner floor，建房前 entry、退休 Run/owner/controller 和旧回调不能提供新来源。集合删除或替换先退休旧 wire 代次再重发，每帧最多 8 条选择；诊断队列消费不影响当前清单。旧 Observe map selection calls 仅诊断，其关闭或丢失不发送/撤销来源。

新增 6 组 Core 快照和 6 组实际回环 TCP 适配测试，原 4 项源适配已迁移，总计 160/160 通过；Build 警告视为错误通过。测试使用合成标量，不运行 NativeHooks、NativeCapture、Unity provider 或两个游戏。NativeGenerationBound、HostSelectionApplied、GuestStateIsolated、WorldAuthority、CargoAuthority 仍为 false；未部署或启动。

当前行为见[固定来源候选传输](ORIGIN_MAP_TRANSPORT.md)和[0.1.17 构建摘要](../logs/origin-map-transport-build-verification.json)。0.1.14 的 callbackFloor/cache 来源与 0.1.16 的“仅日志”是历史范围，当前发送流程按新文档执行。

客机的原生 Serialize/Deserialize、双 Data/Interaction 根及直接恢复候选已离线定位，见[客机影子桥研究](GUEST_ISOLATION.md)。SaveData(string ver) 不是 JSON 构造器，SetLoadedData/Load 不是纯交换；旧协程、缓存、可变子树及全部持久输出仍需隔离与恢复验证。尚未执行原生克隆/根替换或证明 GuestStateIsolated。每人的独立容量和负重规则保持不变。

## 0.1.18 原生根桥与输出围栏源码

0.1.18新增实际typed原生影子桥、单次事务及已枚举输出围栏源码。四类Data原生JSON round trip、五根直接交换/回读/恢复和15个独立强handle已编译；7组新增事务夹具以合成backend验证partial/unknown补偿、fence/refs保留和一次清理，总167/167通过。

当前生产进入与静止边界恒false，事务在围栏安装前拒绝；startup primitive自身再查边界，未接Network/GUI，未运行克隆、根交换、阻断或恢复。194条精确声明不是所有writer、独立native地址或ABI证明；Interaction未Sync、完整子树/旧缓存/协程隔离仍待完成。全部GuestStateIsolated/NativePermission/WorldAuthority/CargoAuthority保持false，未部署或启动。

实现与下一步见[原生根桥](GUEST_SHADOW_BRIDGE.md)、[输出围栏](GUEST_OUTPUT_FENCE.md)及[0.1.18构建摘要](../logs/guest-shadow-build-verification.json)。下一步必须实现可信原生进入/静止边界与缓存/Interaction切换，再进行受控实机验证；个人袋分流、真实地图采用及双游戏闭环仍按原计划推进。

交互缓存的实际typed准备、字段映射和已知图验证见[GUEST_INTERACTION_SHADOW](GUEST_INTERACTION_SHADOW.md)；新增离线Cecil工具可复现对应元数据。其它运行缓存及entry签名见[GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)、[GUEST_ENTRY_BOUNDARIES](GUEST_ENTRY_BOUNDARIES.md)。这些声明和可编译源码不代替native ABI、输出或运行隔离验证。

食材缓存的SingletonNoMono真实direct backing、11条目字段、13资源实例字段和Parent/static未知图见[GUEST_INGREDIENT_API](GUEST_INGREDIENT_API.md)；typed准备与逐字段恢复源码见[GUEST_INGREDIENT_CACHE](GUEST_INGREDIENT_CACHE.md)。离线元数据和可编译字段代理不证明native构造/数组/Entry ABI或整个资源无共享。

## 0.1.21 游戏内缓存精确接口

[Inspect-GuestIngameApi.ps1](../scripts/Inspect-GuestIngameApi.ps1)已离线实际输出5程序集/89类型/6保存记录/34container声明/181direct child edges/8明确frontier/11closed contexts，MissingTypes=[]，hashfresh；不执行native/save。准确singleton、六record/slot/Data字段、Obscured真CLRstruct与ValueType wrapper区分、TryCast的assignable边界和exactclass/object_new候选见[GUEST_INGAME_API](GUEST_INGAME_API.md)。非空助手ScriptableObject资源或live设备队列未覆盖时拒绝，不清空/共享原指针替代。

[GUEST_INGAME_CACHE](GUEST_INGAME_CACHE.md)接第七singlefield源码合同；Interaction/Ingredients/Ingame三known原图先于Serialize捕获闭合，准备全部完成再strict核对，逆序7→6→Save5；21explicit handles/4Data stamps，第七不接受Mixed。0.1.21-dev/协议5的176项Core通过，插件Build警告视为错误通过，见[该版摘要](../logs/guest-ingame-cache-build-verification.json)。类型/分配/字段ABI均未实测，entry/quiet/native/guest/world/bag权限false，未部署/启动；完整缓存/资源/actor/输出、实际地图与个人捕获/返航、双端和冷配置仍待完成。

## 0.1.22 comparer 接口与原生候选

[Inspect-GuestComparerApi.ps1](../scripts/Inspect-GuestComparerApi.ps1)已实际离线读取3程序集/27类型/7继承family/19metadata contexts/8dictionary实例constructor，MissingTypes=[]、输入hashfresh，未运行native/save。19contexts仅参数替换，不授Enum<string/int>原生支持；InGameSaveType底型int32已核。Generic/Object/Enum→EqualityComparer→Il2CppSystem.Object没有已声明native实例代理，但框架Il2CppObjectBase含CLR wrapper/GC字段，不能复制其privatehandle或据此称全继承无状态。精确签名及Default/CreateComparer的RuntimeInvoke边界见[GUEST_COMPARER_API](GUEST_COMPARER_API.md)。

[有限comparer候选](GUEST_DICTIONARY_COMPARERS.md)只接受int/string/InGameSaveType(int32)精确Generic/Object与该enum专用Enum同class独立副本，source pointer/class/kind和aux纳入审计，Ingredients同规则。null原可Capture但Prepare拒，不调用Default/CreateComparer/getter、不share/清空，custom/文化/hash-salt未知拒。explicit(capacity,comparer)先于Add；普通constructor抛时assignment未发生，PartialConstructorAllocationRetentionVerified=false，不证明全部未知allocation已Hold。

历史0.1.22-dev/协议5，[0.1.22 历史摘要](../logs/guest-comparer-build-verification.json)的插件Build警告视为错误通过；0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176，未重跑。七步/21explicit handles/4Data stamps不扩，ABI/fullisolation/entry/quiet/native/guest/world/bag权限false，无GUI/Network自动入口。[冷档候选](GUEST_COLD_PROFILE.md)只研究首load/slot/output而未采用；剩余资源/actor/cache/output、房主地图、每人独立袋/容量/负重的真实捕获与逐产物返航、实际双端/冷配置及完整M3—M7继续必需。
