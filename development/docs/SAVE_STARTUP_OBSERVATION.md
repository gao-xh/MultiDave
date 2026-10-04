# Save startup observation

本轮新增默认关闭的自然启动诊断，用于继续研究客机临时档在首次加载前是否有可用入口。它记录已观察的调用和路径结果，不改变路径、存档、云设置、业务参数或返回值；不是冷启动档采用，也不是 Guest 隔离。

`Startup/ObserveSaveStartup=false` 为默认值。只有配置为 `true` 时，`Plugin.Load` 最前面的 CLR marker 之后才创建精确 target 清单、安装独立 Harmony owner `local.davecoop.prototype.save-startup`。需要新进程读取这个配置；同进程不允许重启观察器或重置额度。此实现尚未在游戏中安装或运行，当前安装的旧插件行为不由本文改变。

## 安装时间与线程

本机框架离线 IL 表明，`IL2CPPChainloader.OnInvokeMethod` 在方法名为 `Internal_ActiveSceneChanged` 时先执行 interop preload、chainloader 与 `Plugin.Load`，再调用原 runtime delegate。该顺序不能证明任何 `SaveUtil`、`GameBase`、UserOption 的 `Awake` 或 SaveSystem 静态初始化尚未运行。

最早记录的是实际进入 `Plugin.Load` 时的 CLR thread ID 与 `Stopwatch` 时间。`Plugin.Load` 的线程不冒充 Unity 线程。只有 `Diagnostics.Update` 的实际首次调用才登记 Unity thread candidate；在此之前，prefix/postfix 仅冻结 CLR wrapper 是否存在、参数枚举/索引、原 bool 返回及原 string 返回的有界哈希，不读取 `Pointer`、原生实例字段或 Unity 时间。安装期间已经生效的部分 detour 可以产生标量记录，`InstallationGaps` 会明确保留这个不完整区间。

精确签名检验、generated interop 类型加载与 Harmony 安装可能引发类型/原生元数据初始化。此处“只读”指观察器不主动调用游戏业务方法、不修改游戏数据；不表示安装过程零原生初始化。没有主动调用或挂接原 `.cctor` 来证明起点。`HooksInstalled` 只表示本 owner 的 36 个注册已检验存在。

首次 Update 后，仅健康记录器所固定线程上的同步 callback 可以读直接 backing proxies：SaveSystem 的三个初始化/加载 bool、四个 manager 的存在状态、`SkipCloudPullForPreset`、两个路径字段，以及具体 manager 的 `_Data` 和 Player `_InstanceData` 是否存在。读取前后均检查边界；未知线程、读取异常、重入或健康丢失会锁存故障并停止进一步字段复制。没有调用业务 getter、singleton getter、load、save 或目录 API来补证据。bool 名称和值不证明云分支、安全加载或尚未读档。

## 精确自然调用

`SaveStartupHooks` 仅接受本机离线声明一致的 instance、owner、参数与返回类型，不选 open generic manager base。清单共 36 个声明：

| 类别 | 精确边界 |
| --- | --- |
| 最早候选 | `SaveUtil.Awake`、`GameBase.Awake_Impl`、`SaveSystemUserOptionManager.Awake` |
| GameBase | `Init`、`LoadGameData`、`InitAfterSaveSystem` 的 factory 和对应 `_Init_d__32`、`_LoadGameData_d__43`、`_InitAfterSaveSystem_d__45.MoveNext`；`LoadSavedData` |
| SaveSystem | `Init(Action)`、`InitSaveSystem(Action)`、`_InitSaveSystem_d__42.MoveNext`；`LoadAllData`、`LoadGameOnInit`、`LoadGame`、`ReloadData`、`ResetAfterCloudLoad` |
| 原路径结果 | `GetSaveFolder`、`GetDemoSaveFolder`；`GetSaveFilePath`、`GetDemoSaveFilePath`、`GetFailedSaveFilePath`、`GetOldSaveFilePath` 的 `(SaveDataType,int,SaveSlotType)`；`GetSaveFileName(SaveSlotType)` 与 `(SaveDataType,int,SaveSlotType)` |
| 具体 manager | Game、Player、Photo、UserOption 的 `CreateManagedData` 和 `OnLoadData`；Player 的 `LoadData` 与 `SetLoadedData(SavePlayerData)` |

路径只在原调用已经自然返回时观察 `__result`，不新调用 getter。Demo 路径有独立目标，因为实际静态边显示它不必经过 `GetSaveFolder`。factory 返回仅记录 wrapper presence；这不等于协程完成。MoveNext 每次 prefix 固定该 callback 的实例/owner ordinal，postfix 若已具备同线程读取条件则核对两者。未在 prefix 读取的 owner 不会事后变成已绑定来源；没有 singleton fallback、跨 yield 的完整生命周期证明或 factory→iterator 采用权限。`MoveNext=false` 也不单独解释成首次加载成功。

## CLR 记录与隐私

Core `SaveStartupTrace` 提供不可变的 Run/Call/ParentCall/Depth、阶段、CLR 线程和时间配对。Call ID 使用进程级单调高水位，旧 `__state` 不能碰撞新 Run。正常 postfix 后的空异常 finalizer 幂等；原异常 finalizer 保留原异常行为并锁存不完整证据。prefix/postfix 均为 void，void finalizer 不替换异常；没有阻挡原调用的 prefix。

`SaveStartupCapture` 在 callback 内同步复制 `SaveStartupObservation`，队列不保存 native wrapper、参数数组、原路径或指针。读取失败撤掉该 DTO 中部分 native 字段；原返回 CLR string 的哈希仍属于标量候选。实际指针只在私有、有界字典中映射到本次观察的 local ordinal，不进入日志，也不代表 native life 已验证。Drain 只序列化 owned CLR 数据，不延后解引用。

输出只进入本机 BepInEx 私有日志：`DAVECOOP_SAVE_STARTUP_SETUP_BEGIN`、`..._CALL`、`..._STATE`、固定失败/清理类别。路径只包含类别、是否存在、UTF16 长度与 SHA256；哈希输入为未规范化的 UTF16 little-endian code units，包括未配对 surrogate。单路径超预算不截取成可误认的完整哈希。异常仅固定类别或 `GetType().Name`，不打印 `Message`、`ToString()`、个人路径、账户或存档内容。公开验证摘要只应使用计数、输入/构建哈希与明确 false 的能力字段，不复制本机路径证据日志。

字符串限额在原返回值或 direct field 的 interop 转换为 CLR string 后检查；它约束本观察器的哈希工作与保留内容，不约束游戏/interop 先前的字符串分配。生成 wrapper 的临时引用/句柄和 detour marshalling 也未由本轮验证为零原生分配或安全 ABI。

| 有界项目 | 实际限额 |
| --- | --- |
| Core queue / native copied CLR queue | 各 512；callback 同步转移后 copied queue 仍限制首个 Update 前积累 |
| pending call contexts | 128 |
| Run / 本观察器进程事件 | 4096；进程 CAS 单次启动，不通过新 Run 重置 |
| method key | 512 UTF16 code units |
| native identity ordinals | 256 |
| 单路径 / 本进程路径哈希工作 | 4096 / 262144 UTF16 code units，重复观察也计费 |
| 每 Update drain | 最多 16 条；状态日志最多每 5 秒一次 |
| 状态日志 | 进程单次 observer 共 128 条，保留最后一条给一次终态；停止排空后不周期重复 |

4096 callback 事件额度不等于全部日志额度。`CALL` 受事件/copy queue 限额约束，`STATE` 单独受 128 条预算约束；安装开始、固定安装失败和固定卸钩失败提示也各为单次。正常停止排空后只输出一次终态；应用退出可在仍有未打印队列时输出终态，其 `CopyQueued` 明确保留尚未打印数，不补造遗漏调用。

Core/copy queue overflow、run quota、path quota、未知配对、异线程、重入、字段读取异常及 own patch 丢失均明确计数/锁存。Drain 不恢复健康；没有用缺失事件推断原调用没发生。失效后停止接受新 callbacks，随后只卸本 owner。卸钩只派发一次；失败或结果未知保留 `_active`/capture 引用，记录 `CleanupVerified=false`，后续不重派发、不声称已清理。应用退出也只处置这一 owner。

## 尚未证明

`ObservationOnly=true`。`StartupCompleteness`、`StartEarlyCoverageVerified`、`FirstLoadOrderVerified`、`NativeIdentityLifetimeVerified`、`NativeHookAbiVerified`、`PathIsolationVerified`、`CloudIsolationVerified`、`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority` 始终 false。Core 的 `NativeOrderVerified` 与 `FirstLoadSafe` 同样 false。

本机 [startup API 研究](GUEST_STARTUP_API.md) 与私有原 PE 报告支持声明及有限静态边，不证明共享泛型地址、detour ABI、安装前活动、完整 writer 覆盖、实际路径选择、云支路、缓存隔离或 first load 的安全性。真实观察也不能仅靠初始化 bool 升格权限。后续仍需真实首轮边界/路径证据、完整客机临时状态与输出隔离、房主地图采用、每人独立袋/容量/负重、实际捕获分流与逐产物返航闭环；本诊断不接 Network/GUI 或 native shadow 安装。
