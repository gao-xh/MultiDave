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
  当前协议为 5，双方源码/版本应匹配；原生资源只能在 Unity 线程解析。
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
- 源码0.1.14-dev新增房主地图选择候选通道，协议5；当前构建和精确测试总数见map-choice-transport-build-verification.json。
  最新安装仍0.1.12，最近完成潜水记录仍0.1.11；新源码未部署/未启动，用户试玩延后时继续独立工作。
  入海前先建Host/Join或Local test，再开启Observe map selection calls；路线cache/restore自然回调开启新代次。
  路线每片8场景、最多4片完整收齐后提交，随后IGP选择连续修订；WaitingForScene可传，不升级Ready或世界权限。
  32包FIFO与动作/移动/世界四路公平调度；队列满或观察停止/丢失/读取错误/截断时房主显式撤销。
  核对MAP_CHOICE_BOUND/ROUTE_SENT/CHOICE_SENT/RECEIVED/RETIRED及NETWORK_STATE的MapChoice统计。
  缓存/恢复即使同指纹也为新代次，普通scene切换保留选择；新代次首片撤旧路线，Disconnect清本房证据。
  已排队旧回调由callbackFloor拒绝，无路线/控制器绑定的IGP不缓冲；同组后续空结果撤销旧候选。
  客机关闭自己的本地观察不会撤销房主证据。自然回调、跨机地址与原生来源代次仍待验证；
  新路线之后迟到的旧原生controller可能同名/同地址，候选代次不能证明native origin，NativeGenerationBound=false。
  HostSelectionApplied始终false；未实现客机地图采用、原生鱼AI隔离、实际捕鱼或正常返航闭环。
  核心夹具编译实际MapChoiceController与DTO（测试仅替代logger），不模拟或验证Unity原生hook行为。
- 网络线程只处理纯 CLR 数据；Unity 对象和资源键解析放在主线程。
  真实双实例、同一地图及捕鱼/结算验收按 PLAN 的阶段条件执行。

每次有意义的修改追加 `development/logs/DEVLOG.md`，同步接手文档和真实验证摘要。
机器运行日志放在忽略的 `development/.local/`，历史会话与新构建的证据分别记录。

## 版本与发行

未验证的游戏或框架版本先研究兼容性；下载及插件失败保留本次错误记录。
停用办法见根 README，不删除游戏或存档。发行前更新依赖清单、重新验证并打包；
打包器会检查 DLL 与发布版本一致。开发构建不自动替换默认发行包。

仓库只发布本项目源码、自写 DLL 和元数据，不发布游戏、互操作程序集或存档。
