---
name: dave-coop-setup
description: Configure or continue development of the MultiDave prototype for Windows Steam Dave the Diver using this repository, verified packages, and loading logs.
---

# MultiDave 配置与开发

仓库目标为 https://github.com/gao-xh/MultiDave。仓库根目录是此文件路径向上三级。
先读根目录 AGENTS.md 和 development/logs/DEVLOG.md，确定当前实现与验证范围。
默认发行包是 0.1.0 插件加载原型。开发源码进度以 HANDOFF 和 PLAN 为准；
本机 TCP 测试通过不能代替真实双游戏联机验收。

## 用户给 GitHub 链接并要求配置

1. 克隆用户指定仓库到可写的新目录，读取 README、AGENTS 和本 Skill。
   已有检出时检查来源和本地修改，保留别人工作。
2. 在根目录运行 `setup.ps1 -InspectOnly`，自动读取 Steam 注册表和库清单。
   找不到或找到多份时才询问游戏目录，不沿用开发者本机路径。
3. 配置 Mod 包含安装依赖和插件的授权。游戏运行时请用户保存退出并继续准备文件；
   安装脚本会拒绝覆盖运行中的插件。网络及目录写入使用环境的权限机制。
4. 运行 `setup.ps1 -LaunchGame`。默认安装 distribution 下经 SHA256 校验的
   自写插件包；框架从官方固定 URL 下载并校验，玩家不需要 .NET SDK。
   明确要求最新 GitHub Release 时才使用 `-UseLatestRelease`。
5. 首次启动会生成互操作接口，期间给用户进度。保持主菜单，运行
   `development/scripts/Check-Status.ps1`，验证当前进程日志的新鲜度与
   `DAVECOOP_BOOTSTRAP_OK`、`DAVECOOP_UPDATE_OK`、`DAVECOOP_SCENE`。
6. 报告版本、实际加载证据、日志位置和支持范围。安装文件存在不能证明加载成功。

## 继续开发

读 `development/docs/HANDOFF.md`、`PLAN.md`；研究游戏接口时读 `GAME_API.md`，
研究显示/协议/会话时读 `MULTIPLAYER.md`，研究同一海洋、鱼与互动时读 `WORLD_SYNC.md`。
研究捕获、背包、员工及返航时另读 `CREW_MODE.md`，按用户确定的每人独立背包方案开发。
源码在 `development/src/DaveCoop/`。

- C# 修改后运行 `development/scripts/Build-Plugin.ps1`。
  纯 CLR 姿态、协议或会话修改后另运行 `development/scripts/Test-Core.ps1`。
  现有脚本使用已安装 SDK 的 Roslyn 与框架 .NET 6 库；测试运行需要 .NET 6 runtime。
  支持 net6.0 的 SDK 也可使用项目文件，按当前机器的真实结果记录验证路径。
- 游戏正常退出后运行 `development/scripts/Deploy-Plugin.ps1`；
  再启动，并从本次进程日志验证。缺少编译器时玩家配置仍可使用发行包。
- 第二角色的本地回放由 F10 切换；潜水时验证显示、转向、输入、镜头及返航清理。
  用户不方便试玩时记录待验证项，继续独立开发，不把主菜单启动当成画面验收。
- 源码开发版的 F11 打开房间面板。先按 MULTIPLAYER 验证 Local test 的真实 TCP
  收发与主线程显示，再验证 Host/Join 的两个游戏实例；默认发行包不包含这个新入口。
  对照 NETWORK/LAYOUT 标记记录成功或失败，布局指纹通过不等于统一地图/实体已完成。
- 源码 0.1.11-dev 含 F7 世界只读探针，默认关闭；0.1.5-dev 已在单游戏潜水读取并传输实际鱼清单。
  新构建与历史会话的实机验证范围以 HANDOFF 为准，画面和正常断开/返航仍须确认。
  正常退出后部署、验证新版本启动，再观察地图选择、鱼 HP/捕获和返航生命周期。
  `development/scripts/Inspect-WorldApi.ps1` 可复现接口签名研究；元数据不能证明挂钩副作用。
  `development/scripts/Inspect-MapEntryApi.ps1` 读取路线、IGP 选择与实例保存边界；Init 返回枚举器不等于加载完成。
- 同版源码的 F11 可开启 Transmit read-only fish observations 和 Preview one received fish，
  均默认关闭。正常退出后部署并验证新进程版本，再在 Local test 潜水核对 WORLD_RECEIVED，
  以及 NETWORK_STATE 的 FishPreviewEntity/Visible/InView/MeshVertices/UnknownResource、UnresolvedVisuals/FirstVisualError。
  0.1.7-dev 优先选择镜头内近鱼，并加蓝色十字和 MultiDave Fish Preview 标签。
  请用户确认标签下方的鱼可见及动画/转向正常，并核对捕获后移除、断线与返航清理、本地输入/镜头。
  组件启用、镜头内、网格顶点和标签分别是不同证据，仅有标签不能证明鱼网格可见。
  0.1.7-dev 用户确认鱼可见但会突然消失；0.1.8-dev 无旧异常但标签换鱼；0.1.9-dev 已获用户确认不再突然消失；单游戏显示不能证明客机地图/鱼群/AI 接管或合作捕获。
  当前协议为 6，双方源码/版本应匹配；原生资源只能在 Unity 线程解析。
  `development/scripts/Inspect-FishRenderApi.ps1` 可复现游戏/Spine 显示与生命周期接口签名研究。
- 0.1.6-dev 在房主开启鱼诊断且发布状态时安装自己的生命周期观察挂钩。
  核对 FISH_LIFECYCLE_READY 及 NETWORK_STATE 中 FishLifecycleHooks/Tracked/Transitions/CallbackErrors；
  捕获、鱼停用/销毁和池复用应产生代次变化，关闭诊断/Disconnect 后应卸载自己的挂钩并清理显示。
  已有原生安装/回调记录，但池重新启用和退出恢复仍待验收。
  0.1.7-dev 的 FISH_LIFECYCLE_STOPPED 和 NETWORK_DISCONNECTED 记录自己挂钩/显示清理；
  结合后续帧及玩家操作/返航验证恢复。挂钩失败或回调异常会停止鱼发布，保留错误证据。
  角色帧不包含鱼叉发射或独立投射物；第二角色不发射鱼叉属于当前实现范围，后续按 M5 接入。
- 0.1.8-dev 修复角色临时显示部件销毁导致自动断开：跳过过期槽位，下一次 Update 刷新列表。
  射击/装备变化时核对 NETWORK_PARTS_STALE 和后续 NETWORK_PARTS_CHANGED、鱼清单持续更新；
  核心测试不验证 Unity 销毁对象恢复，必须结合实机和用户反馈，不把旧异常会话当成修复通过。
- 0.1.9-dev 保留已选活鱼身份，不因镜头/距离/临时显示缺失换鱼。F11 的 Select nearest preview fish 可手动重选。
  核对 FISH_PREVIEW_SELECTION 的更换原因与 FISH_PREVIEW_TRANSITION 的即时隐藏状态；NETWORK_STATE 有 Status/SnapshotAge。
  标签鱼仅有显示组件，不能命中/捕获；不得把预览碰撞当成合作捕鱼。M5 需网络目标绑定及房主原生裁定。
- 0.1.10-dev 已编译房主反向目标查询，原生执行待验证。目标快照不代表攻击许可；操作前重查 epoch/编号和原生代次。
  部署后核对 NETWORK_STATE 的 HostFishBindableTargets；此数值不能证明命中/捕获或收益裁定完成。
  `development/scripts/Inspect-FishInteractionApi.ps1` 可复现鱼叉、伤害和收益入口签名，方法仍未调用。
- 0.1.11-dev 增加默认关闭的 Display received fish roster 和 Observe host harpoon and fish interactions。
  先正常退出、部署并核对新进程版本，再 F11 → Local test，勾选 Transmit read-only fish observations。
  分别开启完整观察鱼群显示和交互观察；完整名单仍只来自玩家当前场景活动鱼，不代表同一海洋已接管。
  核对 FISH_WORLD_STATE 的 ReceivedFishCount/AliveFishCount/RenderableCount/VisibleCount/InViewCount、
  UnknownResource/MissingVisual/SourceInvisible/RenderError/NodeCount；镜头隐藏不撤销身份，完整清单缺席才移除数值条目。
  FISH_INTERACTION_READY 安装 8 条自己的只读前后观察；真实射鱼时核对 CallId/Stage/OriginalMethodCode，
  发射→伤害→挂钩/QTE→入袋分别记录。原 bool 返回不代表成功捕获；prefix保存的epoch/编号/代次不在Drain重新猜。
  HpAtDrain 是主线程消费记录时的读数，不能当成原调用前后HP。
  NETWORK_STATE 的 FishInteractionNativeTargetsAtDrain/NativeLookupErrors验证房主原生反查是否实际执行。
  队列4096、配对上下文1024、全进程累计8192条，切换不重置限额；回调失败锁存，重启后才可重新安装。
  关闭观察、Disconnect/返航时核对 FISH_INTERACTION_STOPPED 和显示清理，随后验证本地输入/镜头/正常保存退出。
  MAP_SELECTION 只读已加载路线与IGP，两不同frame相同才报告；每个选中scene至少一组，原注册列表需一致。
  层级地址跨机稳定性、加载前房主选图、客机AI隔离、命中请求裁定及收益仍未完成；标签鱼仍不可捕获。
  新版本编译与85项CLR测试已通过；实机已见Fire/Hook及两处Damage的42条事件、21对CallId和原bool。
  用户确认成对鱼可见、捕获原鱼同步移除显示，关闭显示恢复正常，操作/镜头正常。
  Win/Pickup未见回调，MAP_SELECTION返回Selected route incomplete；完整捕获收益链和正常返航保存仍待验证。
  真实范围详见fish-world-interaction-native-verification.json，不把部分注册/ABI通过推广为全部鱼型或合作捕鱼。
- 0.1.12-dev 增加独立的鱼操作请求/结果 FIFO，协议升至4；该历史构建109项CLR/TCP测试通过。
  正常退出后部署、确认本次进程版本，再 F11 → Local test，开启 Transmit read-only fish observations
  和 Preview one received fish；点击 Check selected fish target，核对 FISH_ACTION_SENT/ADMISSION/DECISION/RECEIVED。
  ProbeTarget 的 DryRunValidated 只证明房主主线程重查了当前epoch/编号/池代次且鱼未死亡或捕获；
  OperationId=0、NativeEffectsEnabled=false，按钮不执行鱼叉、伤害、捕获或收益。
  其他动作仅有schema/Gate；地图权限、客机隔离、可信玩家/装备和原生执行桥均未接通。
  重复请求、指纹、来源及outstanding有校验；房间内ID高水位跨场景保留，未知原生结果不可重派发。
  合法旧场景发送在会话锁内取消，不因主线程旧Snapshot断房；结果显示也须检查当前场景。
  生命周期失效立即撤销查询身份，读取原生字段后再次核对代次；开关诊断不重置同epoch世界revision。
  MAP_ROUTE_INPUTS 在Transmit开启时独立1Hz观察入海前后路线字段，区分cache/roadmap/first缺失和cache过短。
  不调用选图/加载/存档写入，完整路线和双游戏世界接管仍待验证；新版原生目标检查须另有实际结果日志。
  新构建及启动证据见fish-action-gate-build-verification.json；最近完整潜水证据保持0.1.11。
- 源码0.1.13-dev新增共享MapRouteSelection与默认关闭的Observe map selection calls；112项核心测试及编译通过。
  此历史构建未部署/未启动，实机最新启动保持0.1.12、完成潜水记录保持0.1.11；先读HANDOFF确认实际版本。
  用户暂不方便试玩时继续准备代码，不把编译或旧会话当新版原生验证。
  正常退出后部署并确认新版加载，若验证此功能，应在入海前F11开启Observe map selection calls。
  该观察独立于TCP/Transmit，核对MAP_SELECTION_HOOKS_READY、MAP_SELECTION_CALL、MAP_SELECTION_OBSERVER_STATE。
  5处自然调用只观察cache/restored路线、GetRandomIGPSetInfo原__result、LoadPrefab枚举器factory及SceneLoader.LoadSceneAsync参数。
  Unity线程原回调内即时冻结值；仅CLR DTO排队，不在Drain延迟解引用native包装器；非主线程跳过原生读取。
  1024全进程调用、队列64、每Update16；丢弃/读取错误/线程计数跨开关保留，缺项/截断明示。
  RouteFingerprint只含路线，不是完整路线+IGP清单或epoch权限；枚举器factory不等于资源请求执行或加载完成。
  控制器路径仍未验证跨机稳定，不能据候选推断同一海洋或全选择先于全加载；没有地图采用或游戏/存档写入。
  Disconnect会关闭开关、卸载自己的owner并清CLR队列，核对MAP_SELECTION_HOOKS_STOPPED、后续操作及正常返航保存。
  原生ABI、实际返回项复制、加载顺序及新版画面都待实机；摘要见map-selection-call-build-verification.json。
- 历史源码0.1.14-dev新增房主地图选择候选通道，协议5；该历史构建和精确测试总数见map-choice-transport-build-verification.json。
  最新安装仍0.1.12，最近完成潜水记录仍0.1.11；新源码未部署/未启动，用户试玩延后时继续独立工作。
  此为0.1.14历史流程，当前0.1.17按下文固定来源流程执行；旧Observe map selection calls仅诊断，不发候选。
  路线每片8场景、最多4片完整收齐后提交，随后IGP选择连续修订；WaitingForScene可传，不升级Ready或世界权限。
  32包FIFO与动作/移动/世界四路公平调度；队列满或观察停止/丢失/读取错误/截断时房主显式撤销。
  核对MAP_CHOICE_BOUND/ROUTE_SENT/CHOICE_SENT/RECEIVED/RETIRED及NETWORK_STATE的MapChoice统计。
  缓存/恢复即使同指纹也为新代次，普通scene切换保留选择；新代次首片撤旧路线，Disconnect清本房证据。
  已排队旧回调由callbackFloor拒绝，无路线/控制器绑定的IGP不缓冲；同组后续空结果撤销旧候选。
  客机关闭自己的本地观察不会撤销房主证据。自然回调、跨机地址与原生来源代次仍待验证；
  新路线之后迟到的旧原生controller可能同名/同地址，候选代次不能证明native origin，NativeGenerationBound=false。
  HostSelectionApplied始终false；未实现客机地图采用、原生鱼AI隔离、实际捕鱼或正常返航闭环。
  核心夹具编译实际MapChoiceController与DTO（测试仅替代logger），不模拟或验证Unity原生hook行为。
- 首版玩法为房主＋员工，每人独立背包/容量/负重；房主的原生LootBox仅本人使用，员工袋由房主Mod权威持有。
  同层潜水、房主带队换层与返航；长期图鉴/任务/材料/经济归房主，员工个人进度不接收房主收益。
  必须在员工捕获产物及前置容量判断处按操作归属分流，不能先Add房主袋再复制，不扩大房主容量替代员工袋。
  成员MemberId/潜水ExpeditionId/每袋修订关联归属；员工断线保留已确认袋，账本不随网络Disconnect或scene清空。
  房主原袋按原生链返航不补Add；员工尚未入仓物料需要新增原生桥按ReturnId/CaptureId/ProductIndex一次确认入房主仓库。
  未知原生结果不补奖/不重派发整批；跨崩溃共同事务未证，不能承诺恰好一次恢复。
  客机须隔离全部自动持久写入，并实现独立actor/装备/氧气/受伤/投射物；此模式目前为设计，未实现或实机验收。
- 源码0.1.15-dev开始实现Core/Cargo个人容量/重量/预约和逐产物返航阶段，完整构建/测试结果见cargo-ledger-build-verification.json。
  0.1.15历史协议5；当时账本尚未接游戏潜水生命周期、网络袋清单、员工原生分流/入仓或持久共同事务。
  只接受房主核实的完整产物计划与新鲜member/op/source/产品指纹，未知随机产物不能猜或再次roll。
  房主重量取原生总值，不重复加产物；员工容量独立。断线保留已确认货物与未知屏障，不把Disconnect当新潜水。
  当前来源Room绑定尚无已验证迁移，新Room不能绕过去重；按真实源码合同复核事实，不把synthetic CLR标志当权限。
  旧epoch仍有预约/未知执行时，新epoch捕获保留屏障；Host重量按当前BagRevision和采样时间核对，旧样本不能回退总重。
  快照仅tracked捕获，房主原袋非全量；返航Member袋视图用于审计，实际逐项状态看ReturnItems，不直接作为live袋/负重UI。
  默认关闭Observe loot and return calls独立于TCP/Transmit，正常捕获及返航时观察四处自然前后调用。
  核对LOOT_HOOKS_READY、LOOT_CALL、LOOT_OBSERVER_STATE及LOOT_HOOKS_STOPPED；参数/返回不改，Unity线程即时冻结。
  进程1024前后事件、queue64/context128/每Update16；丢失或停止可以截断链，OwnHooksRemoved和Discarded要分别核对。
  鱼prefix身份不能按嵌套/时间猜到Add/图鉴/仓库；slot加密字段不读，ActualBagDeltaProven/SourceOperationBound/CaptureSuccess/StorageDeltaProven始终false。
  新版未部署/启动，原生ABI、真实重量、完整产物、容量分流、正常入仓/保存均待验证。当前安装仍0.1.12，默认发行包0.1.0。
- 离线研究原GameAssembly调用时用`development/scripts/Inspect-NativeCalls.ps1`，先读`development/docs/NATIVE_ANALYSIS.md`。
  使用已安装LibCpp2IL/Iced、SDK与net6 runtime，按精确Type::Method选择；不启动/执行游戏或读取存档，不下载依赖。
  原报告/地址/机器码/依赖只留.local，按成功时间与原文件hash核对新鲜度；失败保留的旧报告不是本次证据。
  exact version1 chained unwind/共享别名/partial限制明确；不能凭static target或缺边开启world/cargo权限。
  已定位coLoadAdditiveScene/CoLoadSceneAsync.MoveNext直接走Addressables五参LoadSceneAsync，现三参SceneLoader观察漏该路径。
  按工厂/MoveNext固定owner、显式子协程继承后再接操作版本/真实Scene句柄/controller寿命，不用当前singleton倒推旧调用。
  鱼产物/容量/水下进度共同分流，返航按类别逐项；guest加载/SetLoadedData有持久及同步副作用，不能当纯恢复。
  该离线工具轮仅工具与文档，历史插件0.1.15/142项及0.1.12安装/0.1.11潜水证据不扩大；摘要见native-call-analysis-verification.json。
- 历史源码0.1.16-dev增加默认关闭Network.ObserveMapOrigins，继续此功能先读development/docs/MAP_ORIGINS.md。
  原生入口签名/直接字段分类可用development/scripts/Inspect-MapOriginApi.ps1复现，只输出.local报告。
  29声明自己的前后/finalizer观察：GoToInGameEntry创建固定owner，factory返回iterator固定归属，每MoveNext恢复/清scope，未知scope遮父。
  精确Addressables五参typed原返回，主线程保留operation wrapper最多64，直接version/status/result双核对后关联实际Scene句柄与controller出生。
  manager无ownedscene保持unbound，controller选择可pending等同handle确切完成；不在Drain解引用旧wrapper或用singleton补出生。
  新entry/重复cache/Context清理/真实unload/destroy撤证；未知unload无birth也留tombstone，异常退休固定controller/owner。
  原生queue64/drain16/context256/每进程8192事件及Core有界围栏，线程/读取/丢失/配额失败锁存，重启再观察。
  原方法/参数/返回不改；只卸自己owner，核对MAP_ORIGIN_HOOKS_READY/CALL/BOUND_CHOICE/OBSERVER_STATE/OBSERVER_WARNING/HOOKS_STOPPED。
  日志RunId隔离重开后的life编号，ScalarOriginChainMatched仅CLR登记关联，不是完整原生来源或权限；0.1.16历史版本不接网络mapgeneration，0.1.17另接当前清单。
  NativeTypedReturnAbiVerified/NativeGenerationBound/HostSelectionApplied/WorldAuthority/CargoAuthority始终false；框架同commit支持不算本游戏ABI通过。
  0.1.16历史Build和148项测试通过，新增6组synthetic registry夹具；未部署/启动或执行nativecallbacks。
  当前安装保持0.1.12、最近潜水0.1.11，用户试玩延后时不催测；正常保存退出后再部署验证typedreturn/Scene值/__state/加载嵌套/birth/owncleanup及画面。
  此原型不自动进入默认发行包；完整采用、guest持久/AI隔离、个人背包分流与正常返航仍待接通。
- 上一版源码0.1.17-dev、协议5、Build警告视为错误通过、Test-Core160/160通过；范围见development/docs/ORIGIN_MAP_TRANSPORT.md及origin-map-transport-build-verification.json。
  候选只从TryCaptureSource当前owned清单发布，诊断drain不消耗来源；旧Observe关闭/丢失只影响诊断，Guest停本地origin保留Host收到的候选。
  用户方便时正常保存退出后才部署，确认实际待部署版本的新进程；先建Host/Join或Local test，再启用Observe loading coroutine and scene ownership并新自然入海。
  4参BindRoom记录实际Run/owner floor；建房前entry、退休Run/owner/controller不能补旧来源。清单删除/替换退休wire代次后重发，每帧8条，超schema128整帧拒绝；64Run围栏跨Clear保留、controller历史256不淘汰。
  网络摘要核对MapChoiceOriginRunId、MapChoiceOriginOwnerLife、MapChoiceOriginPending、MapChoiceLegacySuppressed与MAP_CHOICE撤销，待发0不表示全IGP完成。新增6组Core快照和6组actualTCP测试均用synthetic标量，不运行NativeHooks/Capture/Unity/provider。
  未部署/启动；NativeGenerationBound/HostSelectionApplied/GuestStateIsolated/WorldAuthority/CargoAuthority仍false；实际采用、员工捕获/入仓及双游戏待完成。安装0.1.12/潜水0.1.11/默认包0.1.0仍为原证据范围。
- 客机影子桥继续先读development/docs/GUEST_ISOLATION.md；Inspect-GuestStateApi.ps1离线只读wrapper/IL，报告仅.local，不调用游戏或读取存档。
  Serialize<T>/Deserialize<T>是实际native clone候选，SaveData(string ver)不是JSON构造器，SetLoadedData/Load有副作用不能作纯恢复。
  双Data/Interaction直接字段交换仍需detached子树、旧协程/缓存及全部输出围栏验证，Photo/UserOption也需明确隔离。178签名候选/static边不是所有writer覆盖或GuestStateIsolated证明。
  Prepare/Activate/Validate/Restore代码与实机待完成，未知恢复不放开写入；每人独立袋/容量/负重规则保持，房主原袋不重复Add，员工逐产物入仓另证。
- 上一版源码0.1.18-dev、协议5、Build警告视为错误通过、167/167测试通过；见development/docs/GUEST_SHADOW_BRIDGE.md、GUEST_OUTPUT_FENCE.md及guest-shadow-build-verification.json。
  actual typed bridge具备4Data native roundtrip、5direct根/回读/一次恢复及15 explicit IntPtr强handles；temp Interaction(false)未Sync，不证明private子树/cache/旧引用隔离。
  Native CanEnterBoundary/HasQuiescentBoundary恒false，Core在InstallFence前拒绝且primitive自身fresh核对；没有Network/GUI入口，不为了试玩改true/加入开关/伪造几个callerbool，也不从Room或loaded/writer0授native权限。
  frozen output manifest194严格declared/static/params/return匹配；8closed typedout失败置null/false，Injected输入ref不乱改。Steam流invalidMaxValue/异步0、Toolbox Save/Delete失败2/1，不能default伪报成功。
  sharedgeneric/typedABI/在途输出/具体service实现及全writer仍未证；检查自己owner并且失败保持阻断，不Patch任意System.IO全局。新脚本Inspect-GuestOutputApi.ps1只读取Cecil元数据/框架IL，报告仅.local，不运行native或读存档。
  7组新事务测试合成backend不执行native bridge/fence；RootShadowInstalled也不升级GuestStateIsolated。原根/manager、强handle及真实静止边界确认前不卸围栏/free；未知恢复/free不重复，尚未有实际原生恢复验证。
  未部署/启动，用户手动验证继续延后；安装0.1.12/潜水0.1.11/默认包0.1.0保持原范围。新source初次调用须先可信native边界/cache与全输出隔离，不能锁住正常保存冒充诊断。
- 上一版源码0.1.19-dev、协议5、Build警告视为错误通过、170/170核心测试通过，见development/docs/GUEST_INTERACTION_SHADOW.md、GUEST_ENTRY_BOUNDARIES.md、GUEST_RUNTIME_CACHES.md及guest-interaction-build-verification.json。
  typed Interaction从detached Player十组容器绑定并新IGP hash，拒原dirty/已知baseline不同或共享mutable引用；不调用manager Sync/SetLoadedData/Load，已接根桥准备/安装/验证。
  KnownReferencesDisjoint仅已读图；0长数组可不含可变元素，非空arrays/records仍须查。comparer/其它Player/旧UI和coroutine、native泛型array/Entry布局、完整baseline/cache/output均未证，不授GuestStateIsolated或收益权限。
  原生进入/静止仍恒false，helper未执行，不能拿170项CLR/TCP（新增3引用审计）当native或双游戏验收。GameAssembly唯一staticedge不证运行顺序；GoToInGameEntry自身可改Player/Mission，下游ChangeSceneAsync不能覆盖它前部，Init/Build也不作无副作用clone。
  新Inspect-GuestInteractionApi/Inspect-GuestRuntimeCacheApi仅Cecil离线读取元数据/IL，原报告仅.local；不读存档/调用native。Build新增Il2CppSystem.Core引用，保持SDK/依赖版本核对，不发布interop DLL。
  未部署/启动，用户手动测试延后时不催测；安装0.1.12、潜水0.1.11、默认包0.1.0证据范围保持。继续真实缓存/输出/旧引用及normal restore，个人员工捕获/逐产物入仓和双游戏/冷安装仍待完整验收。
- 网络线程只处理纯 CLR 数据；Unity 对象和资源键解析放在主线程。
  真实双实例、同一地图及捕鱼/结算验收按 PLAN 的阶段条件执行。

每次有意义的修改追加 `development/logs/DEVLOG.md`，同步接手文档和真实验证摘要。
机器运行日志放在忽略的 `development/.local/`，历史会话与新构建的证据分别记录。

## 版本与发行

未验证的游戏或框架版本先研究兼容性；下载及插件失败保留本次错误记录。
停用办法见根 README，不删除游戏或存档。发行前更新依赖清单、重新验证并打包；
打包器会检查 DLL 与发布版本一致。开发构建不自动替换默认发行包。

仓库只发布本项目源码、自写 DLL 和元数据，不发布游戏、互操作程序集或存档。

- 上一版源码0.1.20-dev、协议5，Build警告视为错误通过、174/174核心测试通过，见development/docs/GUEST_INGREDIENT_CACHE.md、GUEST_INGREDIENT_API.md及guest-ingredient-cache-build-verification.json。独立Ingredients dictionary/records/counts/13 Entity实例字段候选已接六步源码，原缓存与Interaction均在serializer前捕获；最多18显式strong handle不是框架/临时box总数。
- 第六cache OwnedMixed仅表示每field已证original/detached组合，单个Save根不得接受；foreign/unknown不覆盖，进入后未知不重新派发。原loaded保真，不强制true；null原storage可捕获但准备拒绝。SingletonNoMono真实字段为_s_Instance_k__BackingField；不要调用Storage.Init/Load/Reset/Entity.Parent重建或补实例。
- Entity Parent/static目录、旧UI/closures、Mission/Ingame/LootBox、全输出及native静止/ABI仍未证；typed副本/174 CLR测试不授GuestStateIsolated或个人捕鱼/入仓权限。用户测试继续延后，不自动部署/启动；完成房主世界、每人独立容量/捕获分流、逐产物返航与真实双端/冷配置后才算完整目标。

- 上一版源码0.1.21-dev、协议5，Build警告视为错误通过、176/176核心测试通过；继续临时状态隔离先读development/docs/GUEST_INGAME_CACHE.md、GUEST_INGAME_API.md及guest-ingame-cache-build-verification.json。第七IngameCache是单字段步骤，不能接受OwnedMixed；七步逆序补偿、最多21显式strong handles，四Data标量仍4份。
- 三份known原图在serializer前闭合，三份prepared图在安装前再次strict核对；这是顺序已知图核对，不证明全图或静止。六种record声明只覆盖有限可独立构造子图；non-null SubHelperSpecData/live gearQueue、未证comparer/views/sync引用和重复mutable record alias须明确拒绝，不能分享/改空或拆alias代替；默认comparer也可能使准备拒绝，继续实际资源/设备/容器隔离。Exact class加object_new/IntPtr仅普通record候选，不用于Unity资产，全部native分配/ABI/隔离权限false。
- 两组新增第七步CLR夹具覆盖未知恢复保留、晚到original读数和foreign/unknown/mixed/换单例/null拒绝，不运行helper。未部署/启动，用户试玩延后；每人独立袋/容量/负重、房主唯一长期进度规则保持，完整资源/actor/cache/output、房主世界、个人捕获/逐产物返航、真实双端及冷配置仍须完成。

- 上一版源码0.1.22-dev/协议5，Build警告视为错误通过；Core/Test/选定adapter输入逐文件与前commit一致，本轮复用0.1.21实际176/176，不重复运行，不当native测试。比较器继续先读GUEST_DICTIONARY_COMPARERS.md、GUEST_COMPARER_API.md及guest-comparer-build-verification.json。
- 三种key/int32 enum的七个精确Generic/Object/Enum闭型采用同class独立普通instance候选，在(capacity,freshComparer)后核对实际指针/class再Add；Ingredients也捕获comparer/aux。原null可capture但Prepare拒，未知custom/文化/salt/aux不猜默认、不共享/清空，不调用Default/CreateComparer。无declared fields不证明全部无状态、hash/equality或ABI；publicctor抛在assignment前的未知分配无法保证已Hold，PartialConstructorAllocationRetentionVerified仍false。
- 更直接的冷启动员工临时档候选见GUEST_COLD_PROFILE.md及Inspect-GuestProfileApi.ps1；DefaultSaveFolder/instance path/SkipCloudPullForPreset需在实际首load前绑定所有manager、云/prefs/achievement输出，单换slot/目录不等于隔离。候选未应用，全部native/world/bag/entry/quiet权限false。完整M3—M7、每人独立袋/负重/捕获和逐产物返航、真实双端与冷配置继续要求；用户测试延后，不自动启动或部署。

- 历史源码0.1.23-dev/协议5，Build警告视为错误通过，本轮实际Core/TCP 182/182（新增6组生产startup trace夹具）通过，不执行nativehook。继续首load/临时档先读development/docs/SAVE_STARTUP_OBSERVATION.md、GUEST_STARTUP_API.md与save-startup-build-verification.json。
- Startup/ObserveSaveStartup默认false，须未来正常保存退出/部署后、启动前配置，不能F11/Join后补装当早覆盖。Plugin.Load marker只有CLR来源；第一次实际Diagnostics.Update登记Unity线程后才读native字段，早期callbacks仅标量和有界pathhash。不重定向路径、不调用原业务或保存、不改云；安装可能native初始化且安装前读取无法排除。己方owner卸除、异常/丢失/线程/配额明示；不以factory/postfix或初始化false推断never-loaded。
- 本机loader IL在Internal_ActiveSceneChanged detour中Execute/Load plugins，然后原Invoke；这不证早于SaveUtil/GameBase/UserOption Awake/.cctor。完整首次load/路径/云/prefs/成就输出与隔离仍未证，native/world/bag/entry/quiet权限false。用户测试延后，不自动部署/启动；完整M3—M7、个人容量/捕获/逐产物返航、真实双端与GitHub冷配置继续要求。

- 历史源码0.1.24-dev/协议6，Build与实际Core/TCP 200/200通过；继续个人背包先读development/docs/CARGO_TRANSPORT.md与cargo-transport-build-verification.json。
  两袋各自容量/重量/预约和确认产物仅只读账本投影；host inventory tracked-only，重量无新鲜原生证明，Returned也不能按空inventory归零。
  新整体revision覆盖Connected/phase/未知，即使BagRevision不变；32products/64pages整批提交、control优先五路公平，begun batch完成后才nextlatest。
  Disconnect保留host ledger和unknown屏障；newpeer/Room不自动迁移，重连及后续expedition接替仍待真实生命周期协议。
  游戏没有verified producer attach，不创建fakeledger或GUI权限入口；tests仅synthetic CLR与回环TCP，不是原生捕获/容量/分流/返航或双游戏。
  本轮未部署/启动，安装0.1.12/潜水0.1.11/default0.1.0保持；完整guest隔离、房主世界采用、独立actor/装备/氧气、个人袋与正常逐项入仓、M3—M7双端/冷配置继续推进。

- 当前源码0.1.25-dev/协议6，Build与实际Core/TCP 209/209通过；继续捕获分流先读development/docs/CAPTURE_LINEAGE.md及capture-lineage-build-verification.json。
  既有ObserveLootCalls默认关闭，16声明只观察自然拾取/主与追加产物/随机原返回/容量/入袋/进度；prefix固定候选、postfix不pop、void finalizer退出。
  Parent只是同步包含、ordinal仅本地run；未知鱼边界遮父、不从drain/最近鱼补来源。回调冻结重入、错线程/配对/queue/预算/原异常立即撤证；日志另看CurrentLineageHealthy。
  Core/copy队列512、context128、depth32、fishordinal256、Update16、run和process8192events；key512UTF16、process65536，state/lifecycle各128。只有已确认卸钩才newinstance，未知cleanup不retry，全局预算不reset。
  SourceOperationBound/MemberOwnership/FullYield/CaptureSuccess/BagDelta/ABI等全false，没有CargoFacts producer；9fixture只运行synthetic CLR，不等于native调用/员工归属。
  下一桥需潜水级operation/source lease与原游戏已经选产物后的受控阶段，不能提前Roll/retry、把事后Add当完整预计划或只跳Add_Impl/新槽写入。保留每人独立袋/容量/负重及逐产物返航；本轮未部署/启动，完整M3—M7/双端/冷配置继续。
