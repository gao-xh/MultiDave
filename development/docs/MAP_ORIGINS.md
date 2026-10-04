# 地图加载来源观察（0.1.16-dev）

0.1.38当前补充见[客机路线采用](GUEST_MAP_ROUTE_ADOPTION.md)及[本轮285/285/Build摘要](../logs/map-route-adoption-build-verification.json)：protocol7/v2完整声明路线字段、30项host origin目标及固定pending-manager出生来源、9项默认关闭guest加载消费者，实际Reset后/CoLoad前六根安装源码已接；没有运行native、IGP采用或全世界/收益验收。以下0.1.37及更早数字/流程保留为历史。

本页保留0.1.16历史观察与148项构建范围。0.1.17已接当前CLR来源清单到候选通道，流程与160项验证见[ORIGIN_MAP_TRANSPORT](ORIGIN_MAP_TRANSPORT.md)和[新摘要](../logs/origin-map-transport-build-verification.json)；本页“仅日志/不接候选”不再是当前传输状态。原生ABI、完整来源、跨机地址与全部权限仍未验收。

本轮新增默认关闭的地图来源观察器，用于查明一次自然入海请求、加载协程、资源操作、实际场景和 IGP controller 之间的本机关系。它保留游戏原方法、参数和返回值，不采用房主地图，不修改生成、AI、捕获、库存或存档。源码接入和框架代码核对不能证明实机来源链已经成立；本轮未部署或启动游戏，原生验收仍待进行。

相关实现是 `Core/World/MapOriginRegistry.cs`、`Networking/MapOriginHooks.cs`、`Networking/MapOriginNativeCapture.cs` 和 `Networking/MapOriginController.cs`。协议 5 的地图候选通道仍保持原有观察边界；本机来源登记器没有新增网络身份或权限。

## 固定身份如何传递

1. 自然 `SceneLoader.GoToInGameEntry` prefix 创建新的本机 `OwnerLife`，将它压入本次调用 scope；已登记 iterator 跨 yield 保留同一 owner。新 entry 撤销先前 owner 的协程、操作、场景和选择候选。它不是已验证的潜水 `ExpeditionId`，也不等于网络 Room 或 scene epoch。
2. `ChangeSceneAsync` 与 `ChangeLevelSceneAsync` 保留当前 scope；通用 `CoChangeSceneAsync`、层加载和其他子协程工厂在 postfix 用原始返回 iterator 固定 owner。工厂被调用或返回不代表资源已经请求、完成或加载成功。
3. 对已登记 iterator，每次 `MoveNext` prefix 恢复其固定 owner，finalizer 清理该次 scope。未知、未绑定和退休 iterator 也压入明确的 unbound scope，遮住外层 owner；它们不能在下一帧从当前 singleton、相同地图指纹或同名场景获得新的身份。子工厂只继承执行当时已知 scope。
4. `SceneContext.cacheSelectedScenePath` 或 `LoadSceneMapCacheFromSave` postfix 在同一执行 scope 内复制有界路线。路线在固定 owner 中只绑定一次；再次 cache/restore 即使指纹相同，也撤销该 owner，等待新的自然 entry。缺失或不可复制的路线不保留旧候选。
5. Addressables 五参数 `LoadSceneAsync` prefix 固定调用 scope 和有界 string key，postfix 读取原 typed handle 的 operation 身份及版本。非 string key 不调用原 `ToString` 猜地址；无已知 scope 的请求保持 unbound。同 key 的并发请求不以名称或“最近一次请求”合并。
6. native capture 仅在已确认 Unity 线程保留少量 typed operation wrapper，并进行版本围栏下的只读轮询。它读直接 `m_Version`、`m_Status`、`_Result_k__BackingField`，成功结果再取 `SceneInstance.m_Scene.m_Handle`；读取前后复核 operation 指针、版本和成功状态。没有调用 operation 的 `Result`、`Status`、`IsDone` 原生计算 getter，没有安装完成 delegate。原生操作失败或版本变化撤销所属 owner。
7. `InGameManager.Start` 和 `IGPSetController.Init` 工厂从实际 component 的出生场景读取本机 Scene handle。manager iterator 必须已有确切 operation→scene owner；没有则固定 unbound，不把任意 Unity Start 归到当前 entry。controller birth 可以暂存，原 `GetRandomIGPSetInfo` 返回的选择也可暂存，等待同一实际 handle 的确切 operation 完成后解析。当前 entry 只作出生边界围栏，不替代场景归属。

最终候选关系为 `OwnerLife → IteratorLife → OperationLife → SceneLife → ControllerLife → 原始选择`；路线绑定另记录本机 Context 与路线指纹。这描述登记器的关联要求，不证明游戏所有加载路径都经过这些入口，也不证明图中各阶段在所有游戏分支的运行顺序。

## 观察的 29 个声明方法

每处均按声明类型、完整参数类型、static/instance 和返回类型精确注册。前后观察及 finalizer 都使用本插件自己的 Harmony owner；只卸载自己的注册，并复核 owner 已移除。

| 组 | 方法与返回边界 | 数量 |
| --- | --- | ---: |
| 请求 scope | `SceneLoader.GoToInGameEntry`、13 参数 `ChangeSceneAsync`、`ChangeLevelSceneAsync`；均 `void` | 3 |
| iterator 工厂 | `SceneLoader.CoChangeSceneAsync`（12 参数）、`CoChangeLevelSceneAsync`、`LoadAdditiveScene`、`coLoadAdditiveScene`、static `CoLoadSceneAsync`；`InGameManager.Start`；`IGPSetController.Init`；均返回原 `IEnumerator` | 7 |
| iterator 执行 | 上述工厂对应 nested wrapper 的 `MoveNext()`；原 `bool` 只表示该次 iterator 继续或结束 | 7 |
| 资源请求 | static `Addressables.LoadSceneAsync(Object, LoadSceneMode, bool, int, SceneReleaseMode)`，返回原 `AsyncOperationHandle<SceneInstance>` | 1 |
| 路线与 Context 边界 | `cacheSelectedScenePath`、`LoadSceneMapCacheFromSave`、`Clear`、`Reset`、`ClearAllCache`、`OnCreated` | 6 |
| 自然场景通知 | `SceneLoader.OnSceneLoaded`、`OnSceneLoadedCustom`、`OnSceneUnloadedCustom` | 3 |
| controller 终止与选择 | `IGPSetController.OnDestroy`、`GetRandomIGPSetInfo`（原 `IGPSetInfo` 返回值） | 2 |

这组入口补上了原先仅观察 `SceneLoader.LoadSceneAsync(string, LoadSceneMode, bool)` 的不足：离线原生报告发现部分子协程直接调用 Addressables 五参数入口。仍有其他重载、特殊入口和未观察分支，不能称完整加载覆盖。无 `GoToInGameRoutine` 包装器；实现使用已确认的通用 `CoChangeSceneAsync` 父协程。

## 复制、限额与失效

回调先确认 CLR 线程与已记录 Unity 线程一致，再读取 Pointer、Unity frame、场景和原生直接字段。临时 callback 可以带 wrapper 供同步复制；排队的 observation 和 Core registry 只保存 owned CLR 值，不把 wrapper 交给日志、网络或其他线程。typed operation wrapper 的保留是 native capture 内的明确例外，仅供主线程有界、版本核对的读取。

| 部分 | 上限 |
| --- | --- |
| Hooks | 每进程 8192 条接受事件；最多 256 个前后待配对调用 |
| Native capture | observation queue 64；每 Update 最多消费 16；最多保留 64 个 native operation |
| Core registry | 32 个 owner；256 个 iterator；128 个 operation；128 个不同 scene handle（含 tombstone）；256 个 controller；128 条 pending/ready choice；32 层 scope |
| 文本与额外记录 | load key 最多 512 字符；Context owner 记录最多 32；MoveNext 首次诊断记录最多 256；路线、地址和 prefab 使用既有 schema 限额 |

每次正常关闭或重开观察器会产生新的登记器实例。日志必须按 `RunId` 连同 `OwnerLife` 等编号解释；不同 RunId 的 life 即使数字相同，也不是同一身份，不能跨 run 比较大小、合并或补来源。operation 指针、Context 指针和 Scene handle 均为本机寿命 token，不作网络 ID，不以地址相同认定生命周期相同。

退休 iterator/controller 指针、operation 指针＋版本和 scene handle 保留围栏，不为扩容淘汰旧记录。实际 unload 即使先于 scene 完成或 controller birth，也为非零 handle 留 tombstone，拒绝迟到 completion；销毁 controller、新 entry、相关 Context 清理和重复 cache 边界同样撤销旧候选。停止观察清理队列、scope、待配对调用和保留的 native operation，旧候选不带入新 run。

错误、非 Unity 回调、非法身份、scope 非 LIFO、复制丢失和超限使来源登记器失效，撤销全部候选。callback 的自身错误被吞入诊断并锁存，随后停止接受并卸载自己的挂钩；错误和进程事件额度跨开关保留，失败后需要进程重启。停止、溢出和配额耗尽都可能截断轨迹，不能解释为观察到完整链。已接受 prefix 的 finalizer 仍尝试清理；额度耗尽后的同步失效路径会撤销 registry 并清空 scope，不继续保留来源。

异常撤销使用该次 prefix 固定的 controller 和 owner。controller 的 Init iterator 另保存有界（最多256）的纯 CLR iteratorLife→ControllerLife，用于其 MoveNext 原方法异常时退休选择；manager/Init 的 owner 为0时也不留下已排队的 controller 选择。这个关系不改变 iterator owner。旧 route postfix 只有 BindRoute 返回 Accepted 才登记 Context owner，不能覆盖新 entry 的有效映射。

## typed return 与 finalizer 的框架证据

本机 `Il2CppInterop.HarmonySupport` / Runtime 版本为 1.5.3，InformationalVersion 对应 commit `dbda1cb353b0f4253345dc45136d170b9e50a5a0`；HarmonyX 为 2.10.2。已离线读取本机框架 IL，并与同版本官方代码核对，不调用游戏或真实挂钩。

`AsyncOperationHandle` 的生成 wrapper 是 CLR class，继承 `Il2CppSystem.ValueType`。HarmonySupport 根据这个继承关系计算原生值大小并生成 Windows return buffer 转换，typed `__result` 并未被明确拒绝。观察器按值读取原精确类型，不替换它；去掉 `__result` 也不会免除整个 detour 的返回 ABI 转换。[同 commit 的 Il2CppDetourMethodPatcher](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.HarmonySupport/Il2CppDetourMethodPatcher.cs#L189)

HarmonySupport 将 copied wrapper 交给 HarmonyManipulator，支持正常尾部与托管 `Exception` 路径的 finalizer。当前 void finalizer 清理 CallId/scope，按值只读原 exception，不替换异常或结果；自身错误不进入游戏回调。最外 trampoline 还有框架自己的异常处理，finalizer 不能保证原生崩溃或 SEH 后执行，也不能据此声称所有原生异常都透明传播。[HarmonyX 2.10.2 的 finalizer 实现](https://github.com/BepInEx/HarmonyX/blob/v2.10.2/Harmony/Public/Patching/HarmonyManipulator.cs#L447)

这些证据支持实现所选签名，尚未证明本游戏五参数入口的 typed return、Scene 值入参、原 IEnumerator 返回、`__state` 配对或卸载在实机正常。`NativeTypedReturnAbiVerified=false` 保持不变；安装注册存在也不是 ABI 验收。

## 如何读取结果

配置 `Network.ObserveMapOrigins=false` 是默认值；F11 对应 `Observe loading coroutine and scene ownership (read-only)`。启用是诊断选择，不要求 TCP Ready，也不会提升会话阶段或权限。观察器在加载中途打开时无法补出已错过的出生身份；缺失路径保留 unbound，需新的自然入海轨迹继续验证。

日志入口是 `DAVECOOP_MAP_ORIGIN_HOOKS_READY`、`CALL`、`BOUND_CHOICE`、`OBSERVER_STATE`、`OBSERVER_WARNING` 和 `HOOKS_STOPPED`，各项使用完整 `DAVECOOP_MAP_ORIGIN_` 前缀。READY 只表示观察注册及本地健康检查；BOUND_CHOICE 的 `ScalarOriginChainMatched=true` 只表示登记器标量关系匹配。CALL 的 `TraceHealthyAtDrain`、错误/丢失/配额计数和 STOPPED 的 `OwnHooksRemoved/EvidenceRevoked` 必须共同查看；以前输出的候选不能因日志已写入而在停止后继续生效。

`CopiedObservation` 和 `ObservationOnly` 只说明值已复制、用途是观察。Core `MapOriginChoiceEvidence` 即使状态为 Accepted，也保持 `NativeGenerationBound=false`、`HostSelectionApplied=false`、`NativePermission=false`、`CrossMachineAddressVerified=false`；native observation 同样保持 WorldAuthority/CargoAuthority 为 false。Accepted 是登记器接受调用者提供的本机关系，不是实机证明、房主完整 manifest、跨机地址验证或可执行凭证。

来源链还未与已传输路线 generation 建立经实机核实的完整绑定，未完成 guest 采用路线/IGP、临时进度隔离、自主生成/AI 隔离或真实双游戏闭环。这一版不授予 WorldAdopt、guest 原生操作或 Cargo 权限，不作为 M4 完成。

## 具体待验收范围

- 新鲜普通入海中，核对 entry、通用父工厂、各次 MoveNext、cache、子工厂、五参数资源请求和确切 scene result 的实际关系；没有作用域泄漏、回调错误或丢失。重复 MoveNext 与 yield 不新建 owner。
- 核对 typed return、原 Scene 值、`__state`、finalizer 和主线程直接字段读取；确认运行正常，再核对关闭、Disconnect 与自己的挂钩卸载，不只看注册成功。
- 确认 operation 成功结果与 controller 出生是同一实际 Scene handle；GetRandom 早于 operation 完成时，只在原 owner 尚活的确切结果到达后解析冻结选择。manager 尚无 owned scene 时保持 unbound，不事后改 iterator owner。
- 新 entry、重复相同指纹 cache、Context 清理、scene unload（包括无 controller birth）、controller destroy、旧 iterator/operation 晚到均不能恢复旧候选。观察器重开或指针/handle 复用时，RunId 与寿命围栏按预期生效。
- 覆盖未知 key、非主线程、读取失败、队列/配额和原方法异常；错误停止后没有残留来源能力。剩余路径缺证据时明确 unbound，不用 singleton、名称或旧 cache 补票。
- 明确普通海域、换层、返航再次入海、restore/custom 与 DLC 的支持范围，再证明全部实际 controller inventory 与跨机匹配；随后才实现采用和 guest 持久副作用隔离。

构建、测试数量及产物哈希以本轮验证摘要和 [HANDOFF](HANDOFF.md) 为准；CLR 夹具与框架源核对不替代上述实机验证。离线工具范围见 [NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)，地图候选传输和采用前置条件见 [WORLD_SYNC](WORLD_SYNC.md)，潜水账本边界见 [CREW_MODE](CREW_MODE.md)。

后续0.1.39默认关闭的实际scene-operation/controller来源与IGP Init消费者见[GUEST_IGP_ADOPTION](GUEST_IGP_ADOPTION.md)，实际292/292和Build见新摘要。本文件早期编号/结果保留历史，不将新验证回填旧记录；全部原生权限仍未完成。
