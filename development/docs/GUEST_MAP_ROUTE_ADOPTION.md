# 客机路线加载前采用

0.1.38-dev、协议 7 新增默认关闭的实际路线消费者：
`NativeGuestMapController` 从自然入海入口取得固定加载来源，
`NativeGuestMapRoute` 在原资源加载前构造并安装六个路线根。
这轮源码已经接入实验客机初始化与真实 Guest 房间；未部署、启动游戏或执行原生验收。
本轮实际 Core/TCP 285/285、插件 Build 警告视为错误通过，见
[验证摘要](../logs/map-route-adoption-build-verification.json)。
历史安装 0.1.12-dev、最近潜水 0.1.11-dev、默认发行包 0.1.0 保持。

`Startup.ExperimentalGuestInitialization` 默认 false。开启后的加载消费者依赖
[自然初始化五根来源](GUEST_INITIALIZATION_BOOTSTRAP.md)，不依赖由调用方补成 true 的
通用权限标记。`GuestStateIsolated`、原生运行/字段/分配 ABI、完整初始场景配置、
完整 IGP 采用、世界与捕获/货袋权限均未验证或保持 false。
源码中的一次根安装与回读不能当作 M4、完整隔离或双人可玩验收。

## 真实来源与提交顺序

消费者源码有自己的 Harmony owner，注册目标是以下九处声明；本轮未执行原生 Patch。
`ResetForNewScene` 的声明类型是 `SceneLoader`；`CoLoadSceneAsync` 是 static。

| 声明 | 参数与原返回 | 用途 |
| --- | --- | --- |
| `SceneLoader.GoToInGameEntry` | instance `void(string, SceneTransitionType, bool)` | 绑定实际入海调用与原 loader |
| `SceneLoader.CoChangeSceneAsync` | instance `IEnumerator(DR.GameScene, SceneTransitionType, bool, bool, bool, bool, bool, Il2CppSystem.Action, Il2CppSystem.Action, bool, bool, bool)` | 保留原工厂返回的唯一 iterator |
| `SceneLoader._CoChangeSceneAsync_d__111.MoveNext` | instance `bool()` | 固定 iterator 首状态等待路线，后续原 Move 跨 yield 保持配对 |
| `SceneLoader.ResetForNewScene` | instance `void(bool, bool, DR.GameScene)` | 核对 loader/scene，等原方法实际返回后冻结当前 Context |
| `SceneLoader.CoLoadSceneAsync` | static `IEnumerator(string, LoadSceneMode, bool)` | 实际调用 prefix 内准备并安装六根 |
| `SceneLoader._CoLoadSceneAsync_d__108.MoveNext` | instance `bool()` | 固定原子协程首次加载前及原返回后复核 |
| `SceneContext.Clear` | instance `void()` | 退休已经绑定的 Context 来源 |
| `SceneContext.Reset` | instance `void()` | 同上 |
| `SceneContext.ClearAllCache` | instance `void()` | 同上 |

这里的 `IEnumerator` 为 `Il2CppSystem.Collections.IEnumerator`。
工厂回调只绑定协程对象，不表示资源已经开始或完成加载。

1. 同一真实 Guest peer/room 已绑定，原初始化首次 Move 已放行，五根事务仍通过
   `ValidateActive`，来源、manager 身份与持久输出围栏保持有效，才能建立入海来源。
   这只是当前实验窗口的实际前置事实，不开放通用 Native/World/Cargo 权限。
2. 自然 `GoToInGameEntry` 内的原 `CoChangeSceneAsync` 工厂返回固定 iterator。
   首次 Move 核对精确类型、原 loader、原 sceneData、state 0 与 current null。
   未收到当前完整房主路线时跳过这次原 Move、返回继续等待，下一帧再检查。
   `StartCoroutine` 可能在 GoTo 原体返回前同步执行首 Move；来源允许已配对的该嵌套关系，
   不要求祖先方法已经返回，也不让未知子 iterator 继承父身份。
3. 完整路线必须来自当前同 peer 的原子快照，未 retired，代次与指纹持续一致。
   在原 Reset 与 key 推导前完成初始 sceneData 的兼容判别；原 Move 可以跨多个 yield，
   不把全部 reset/load 限定在首 Move。
4. 只在该固定 Move 中，原 `SceneLoader.ResetForNewScene` 确实运行并配对返回后，
   才冻结实际当前 `SceneContext`。正常 Reset 内部的清理不提前退休尚未绑定的 Context；
   `ResetReturned` 后的 Clear/Reset/ClearAllCache 才退休该来源。
5. 同一固定 Move 随后调用实际 static CoLoad 工厂，key 必须与已绑定本地 scene 名称相符。
   prefix 内建立单次 lease，准备、安装及回读六根后，才让原工厂继续。
   原 loadMode、activate 保留，不以 DTO 或猜测补加载参数。
6. 保留原工厂返回的固定 `_CoLoadSceneAsync_d__108`，首次原 Move 前核对 state 0、
   current null、key/mode/activate、Context、路线代次和五根来源。
   原 Move 返回后再次复核；原调用被跳过、异常、重入或来源失效不能当作已成功加载。

独立非作者审查检查了 NetworkController 的实际接线：Host producer 在 `BindRoom`
设置来源 floor 前启用；临时 Guest 禁用自己的独立 origin 与旧 map-call 观察；
先绑定实际 Guest peer，再绑定地图 transport。临时模式拒绝 Host/Localtest 和换房。
Disconnect/失败不恢复个人根、卸输出围栏或释放可能仍被原消费者使用的资源。

## 初始场景判别与六根材料化

初始 key 的具体运行值未被离线证据证明。不能把每次入海初始场景直接解释为某个 A 层，
也不能把 `Game_Ingame` 一概换成房主入口层。
消费者先检查实际 `DataManager._SceneDataDic_k__BackingField` 与精确 `DR.GameScene`，
读取直接 TID、SceneName、SceneType、IsAdditive、SceneWithDiving 字段，
核对原资源与 catalog 的对应身份、结构、版本及前后值。
公开业务 getter、GetScene/FindSceneManager 不作为主动读取入口；
字典 `TryGetValue` 仍是真实原生 BCL 调用，其运行 ABI 与效果未验。

只有原 scene 在当前实际 native `sceneLayerDataList` 中按 ID/Name 唯一匹配，
且房主 EntrySceneId/Name 对应本地 catalog 与同一 native layer list 的兼容记录，
SceneType/IsAdditive/SceneWithDiving 均一致，才一次替换父 iterator 的 sceneData。
先前本 Mod 从 wire 构造的 layer list 被排除，不能拿自己的上轮输出再证明 native 分类。
缺 Context、缺 list 或没有匹配只表示未知：保留原 sceneData/key/mode/activate。
因此 `InitialSceneProfileVerified=false`，即使六根字段安装通过也不证明完整初始加载已采用。

材料化重建以下六根，不调用游戏 Build/Cache/Save 业务方法：

| `SceneContext` 根 | 实际类型 |
| --- | --- |
| `sceneLayerDataList` | `List<SceneMapLayerData>` |
| `selectedMapLayerCacheList` | `List<SceneMapLayerDataCache>` |
| `m_SceneRoadmap` | `Dictionary<int, SceneContext.SceneRoadmapData>` |
| `_firstData_k__BackingField` | `SceneRoadmapData` |
| `_lastData_k__BackingField` | `SceneRoadmapData` |
| `_TotalSceneHeight_k__BackingField` | `float` |

容器为 `Il2CppSystem.Collections.Generic` 类型。
路线 DTO 的 canonical SceneId 排序只用于规范复制/指纹；
本地层列表与 roadmap 按 EntrySceneId、Previous/Next 完整链重建，不能按该排序猜空间顺序。
新 layer 复制十一处已列字段，cache 另复制 preload 策略并本地置 `IsSceneLoaded=false`，
roadmap 复制 sceneID/offset/previous/next。房主已加载状态不进入 wire，也不借到客机。

普通记录采用精确 native class 的 `object_new`、立即额外强 GC hold、`IntPtr` wrapper 与字段复制，
不执行游戏记录构造器。容器用实际 capacity constructor 与独立精确 int comparer，
随后真实 BCL Add；不能将这些调用描述为纯 CLR 或无副作用。
构造器抛出前内部未知 allocation 的完整保留未证明，
`PartialConstructorAllocationRetentionVerified=false`。

## 协议字段与房主来源

协议 7 新增四项原生路线输入：每场景 `Priority:int`、`PreferenceWeight:int`、
`PreloadAndNotUnloadable:bool`，路线/manifest/slice 的 `TotalSceneHeight:float`。
复制、分片、组装与指纹均保留；指纹域升级为 `map-route-v2/`、`map-selection-v2/`。
Decoder 要求对应 camelCase wire 属性实际存在且唯一；0/false 是合法采样值，
缺字段或重复字段不能退回 CLR 默认值，旧协议 6 拒绝。
总高度须有限且绝对值不超过 32,000,000；不推断必须为正或等于各层高度之和。
原路线传输仍为每片八场景、最多四片、map FIFO 32；换代首片撤旧路线，收齐才原子提交。
路线完整不表示所有 IGP 已收到或已采用。

房主独立 origin observer 增至三十处实际声明，新增 manager Destroy 撤证。
manager 上限 32：在真实 Start 工厂出生时固定原 scene handle 及出生前已经观察到的
operation life 集合，保留原返回的精确 `_Start_d__111` 与原 manager 身份。
若 Addressables operation 尚未完成，iterator 与路线可暂存；
只有冻结集合中同 entry owner、同出生 scene handle 的精确成功 operation 完成，才绑定并提交。
已完成 scene 可直接验证；不会从当前 singleton、后来的 load、相同名称或当前 owner 补来源。
新 entry、unload、destroy、读错、配额或失配撤证；不同 RunId 的 life 不能比较。
这使源码具有 pending-manager 生产接线，仍不证明 Unity 的实机出生/完成先后、
原 typed-return ABI 或 bootstrap 的具体场景 handle。

## 有界保留与未完成验收

加载来源最多 32 entry，最多七个额外 native 强句柄/entry、进程总额 224；
trace 64，catalog/list 检查上限 4096。材料化每份路线最多 106 个明确强句柄，
进程 retained owner 最多 32，每个方法最多 8192 guarded step。
它们与初始化五根桥的句柄独立计数；不包含原生容器内部所有分配。

准备单次、每根写入单次并回读。guard 在原生步骤前后检查真实来源，错误/重入锁存；
未知结果保留 controller、lease、CLR wrapper 与独立强句柄，不重派发、hot restore、
自动 Dispose/free/unpatch。源失效和部分安装不补成功，后续原流程是否再次修改路线仍需验证。
六根图仅覆盖已声明路线状态，不证明全部资源、actor、cache、协程及持久输出已隔离。

下一步必须验证实际首次加载与各层后续加载、路线保留、IGP 对应选择的逐帧等待和唯一
本地对象替换、跨机地址、房主实体/AI/命中裁定及客机完整隔离。
完整 M3—M7 继续包含每人独立袋/容量/重量/负重、员工捕获产物前置分流、
房主唯一长期进度、逐项返航入仓/保存、实际双端正常闭环与 GitHub 冷配置；
不能先把员工产物塞进房主袋再复制，也不能把本轮字段采用视为上述目标已完成。

## 已执行离线证据

以下是本轮实际只读 Cecil/原 PE 研究的自写摘要，均未执行游戏方法或原生 API。
原始 IL、指令文本、地址、偏移与报告只留 `development/.local/analysis/`，不发布。

| 私有报告标识 | UTC 执行区间 | 实际范围与限制 |
| --- | --- | --- |
| `guest-map-route-api` | 2026-10-04 18:03:40.7050200Z–18:03:42.0436857Z | 4 类型、56 properties、13 constructors（9 instance/4 .cctor）；含业务 properties，不是全 direct |
| `map-route-adoption-entry-api` | 2026-10-04 18:20:34.8679512Z–18:20:36.8715756Z | 6 类型、522 properties、22 selected methods；含业务 properties；助手未展开 closed generic 基类，不表示无继承字段 |
| `map-route-adoption-entry-instructions` | 2026-10-04 18:19:56.8070505Z–18:19:59.2292400Z | 8 selector roots、11 methods、2128 解码/2127 文本、9 indirect；1 Partial、1 无可用 leaf、1 invalid instruction；无 method/instruction 配额、遗漏 root、text omission 或 range overrun，仍不证明完整方法 |

两份 Cecil 输入 Assembly-CSharp SHA256 均为
`F41167D67D226866B22EB76A239B796B0D1E40F57177E62FBAAFC2284471626E`，前后一致。
原 PE binary SHA256 为
`8544E01549C9C9329C2C18283F7E35B652B174F59834CFC0279CF8732637BE59`，
metadata SHA256 为
`AF54CE1E834BD8F31E4C39200CF808D7AF63924E6B18E5FFAAB3B95209D223C4`。
报告 SHA256 依表顺序为：

- `11CC916E36CCE9FDCFEC9424D71C95148BB96349DE869FDB5CA4005141DC698A`
- `19DEDDEB98C43EFF0F66D1264093451EF16D6CE526E2A9D2F7A494C671665321`
- `6A20937FEE4D599B2D38821863A90CDB165265FFB3FB4EA3CB23731713E80709`

已知静态边支持 GoTo 原体在 transition 前触及 IngameSave/PlayerData/任务，
CoChange 固定 Move 的 Reset 早于 static CoLoad，后者首 Move 调用 Addressables 五参入口。
其中原 sceneData.SceneName 是初始 key 的来源，具体运行值未知。
Reset 存在间接/委托路径，CoChange 仅部分解码；没有完整控制流、缓存消费者或实际顺序证明。
已知 Manager.Start 片段只有 cache 检查与 BuildByCheat fallback，不能据此把普通
Build/Restore/cache 三个未见入口假设为 Manager 首 Move 中必经的采用点。

最终 Test-Core（含编译）UTC `2026-10-04T18:43:35.4098120Z` → `2026-10-04T18:43:40.5384050Z`，285/285；Build UTC `2026-10-04T18:43:41.8929187Z` → `2026-10-04T18:43:44.0024187Z`，警告视为错误通过。87份实际Core源码、104份插件源码及项目/执行脚本分别按92/109份输入核对；149份联合输入执行前封存、执行后相同，完整stdout和实际PASS名单私下保留。插件SHA256 `BAD8A38F0B96F10895EA3E716BF3CAB719997C156BE4C020A89C3C22A483105B`，详见
[验证摘要](../logs/map-route-adoption-build-verification.json)。
离线签名和静态调用边、CLR/TCP 通过及编译通过都不替代 native hook、构造/字段 ABI、
初始场景、IGP、同一海洋或完整隔离/收益验收。

后续0.1.39默认关闭的实际scene-operation/controller来源与IGP Init消费者见[GUEST_IGP_ADOPTION](GUEST_IGP_ADOPTION.md)，实际292/292和Build见新摘要。本文件早期编号/结果保留历史，不将新验证回填旧记录；全部原生权限仍未完成。
