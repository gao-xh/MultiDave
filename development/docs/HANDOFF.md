# MultiDave 接手记录

## 已完成

已在 Windows Steam 版真实验证 BepInEx 788、207 个互操作程序集、
自定义插件加载、Unity Update 回调，以及 `DR_Start`、`DR_Logo`、`DR_Title` 场景读取。
插件日志证据见 `../logs/bootstrap-verification.log`。

开发在 `codex/player-discovery` 分支，当前源码为 `0.1.17-dev`、协议 5，Build 警告视为错误通过、Test-Core 160/160 通过；本轮未部署/启动。
当前安装及最近新鲜启动为 `0.1.12-dev`/109 项测试，加载/Update/网络入口与 4 条初始 RouteInputs 已确认，仅主菜单启动通过；新地图调用观察、Probe、潜水路线、场景切换与正常返航仍待实机。
最近完成潜水验证的是 `0.1.11-dev`。

新增`scripts/Inspect-NativeCalls.ps1`和自写`tools/NativeCallInspector.cs`，用本机解析依赖离线读取原GameAssembly/metadata。
5组真实静态报告已生成、边界和失败路径通过；原始结果只在.local，精简摘要见`logs/native-call-analysis-verification.json`。
复现及具体入口见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)：换层/附加协程直接走Addressables五参入口，当前三参观察漏该路径；
要固定工厂/MoveNext owner并显式继承子协程，再关联实际操作/Scene/controller寿命。
鱼产物、容量与水下进度同归属分流；返航包含多类别入仓，保存/加载不只是简单根赋值。
该离线工具轮保持历史0.1.15/142项记录，未部署/启动；原始报告及其证据不扩大。
随后0.1.16已接默认关闭ObserveMapOrigins：29声明前后/finalizer、固定entry/factory/MoveNext owner、Addressables精确typedoperation成功结果→实际Scene句柄→首次controller寿命。
主线程operation保留64/版本复核，队列64/消费16，进程8192/context256；无birth的真实unload也留tombstone，错误/丢失/线程/配额撤证。
MapOriginController独立于TCP，RunId隔离重开后的编号；0.1.16历史CALL/BOUND_CHOICE仅记录标量，0.1.17另接当前候选清单，始终不授权限。
0.1.16历史6组纯CLR registry夹具通过，总计148/148；Build警告视为错误通过，新挂钩原生ABI/真实时序/关闭清理仍待实机。
F11面板新增开关并按屏幕尺寸缩放，画面仍待验收；本轮未部署/启动游戏。
NativeGenerationBound/HostSelectionApplied/WorldAuthority/CargoAuthority始终false，独立员工背包与正常返航桥未接通。
详见[MAP_ORIGINS](MAP_ORIGINS.md)及[构建摘要](../logs/map-origin-build-verification.json)；框架同commit证据不当作本游戏typedreturn验收。
新版单游戏 TCP 偏移鱼群可见和原鱼移除时副本同步消失已获用户确认，关闭显示后恢复正常，操作和镜头正常。
发射/挂钩/伤害只读观察已运行；动画、完整捕获链、路线完整读取与正常返航仍待验收。
最近单鱼稳定性实测来自 `0.1.9-dev`，M1 历史证据来自 `0.1.1-dev`。
加入只读玩家、输入、动画和摄像机探针，离线元数据检查工具及 JSONL 汇总工具。
真实潜水 `A01_01_01` 已读到 `PlayerGroup(Clone)/DaveCharacter`，
`InGameManager.playerCharacter` 与该实例一致，`CameraManager` 跟随其根 Transform。
玩家位置、旋转、朝向、非零移动输入和动画状态已成功采样。
从 `A01_01_01` 切换到 `Boss_000` 后，旧玩家不再出现在采样中，新玩家与管理器、
摄像机重新绑定，跨场景探针无错误。M1 基础验收通过，证据见
`../logs/player-discovery-verification.json`。
当前默认安装包仍为 0.1.0；根入口不部署开发分支的源码构建。

旧 M1 会话完整日志最终为 1886 条快照，记录 Boss 返回潜水、返航大厅及主菜单，无探针错误。
M2 用户在 `A03_01_02` 确认可见且正常模仿动作，37 条回放状态中原生玩家数为 1，
摄像机均绑定本地玩家，F10 停用/重建及返航清理有日志，无警告，基础验收通过。
新增纯CLR姿态、协议/TCP、会话、资源键、布局指纹/插值、实体身份/原子快照、鱼显示缓冲及生命周期代次、房主目标查询、鱼群缓冲、路线/IGP清单、操作门禁、个人Cargo账本和固定来源候选传输；当前验证范围见[固定来源候选构建摘要](../logs/origin-map-transport-build-verification.json)。
0.1.3-dev 已部署，新进程确认 F11 网络组件、加载、Update 和主菜单标记；连接及潜水显示待验证。
启动证据见 `../logs/network-bootstrap-verification.json`。
0.1.4-dev 引入默认关闭的 F7 世界只读探针及房主鱼状态诊断通道，0.1.5-dev 加入一条鱼的 Sprite/Spine 显示验证入口。
当前源码协议版本为 5，拒绝旧协议 4；WorldSlice 保持每块最多 16 个实体，保留 FishActionRequest/FishActionResult 独立通道，新增 MapRouteSlice/MapIgpChoice/MapChoiceRetire。
F11 的 Transmit read-only fish observations / Preview one received fish 均默认关闭。
0.1.11-dev 另增默认关闭的 Display received fish roster / Observe host harpoon and fish interactions；
本机显示及房主交互观察仍依赖 Transmit read-only fish observations。
0.1.5-dev 已部署并真实潜水：F7 探针 72 条快照无错误；本机 TCP 收到 59 条鱼清单概要，数量 12..21，最大修订 582。
显示资源均可解析，59 条网络状态的单鱼组件可见，无插件警告；实际画面、动画和正常断开/返航仍待用户反馈。
这次会话进程已退出，用户确认是主动退出；未记录 Disconnect 或返航，不能据此推断正常清理通过。
地图接管、客机原生鱼 AI 隔离、互动及收益未实现；两游戏验收仍待完成。
连续更新时发送完已开始的整批快照，仅保留下一批最新状态，避免慢连接无法提交清单。
地图/鱼/互动方案及可复现接口研究见 `WORLD_SYNC.md`、`scripts/Inspect-WorldApi.ps1`。
细节见 `MULTIPLAYER.md`，最新测试证据见 `../logs/core-verification.json`。
历史 0.1.5-dev 单鱼构建的验证边界见 `../logs/fish-preview-build-verification.json`。
新生命周期和定位的原生证据分别见 `../logs/native-fish-lifecycle-verification.json` 与 `../logs/native-fish-preview-verification.json`。
真实单游戏运行证据见 `../logs/native-fish-loopback-verification.json`。
0.1.6-dev 加入观察生命周期的 9 个 HarmonyX 前缀和对象池代次处理，编译及核心测试通过，已部署并确认新进程加载。
0.1.6-dev 的生命周期挂钩已实机安装并记录回调变化，无回调错误；用户反馈未能辨认淡蓝色鱼，视觉验收未通过。
0.1.7-dev 断线日志已核对自己的挂钩注册移除；原生池重新启用的身份验证和正常返航恢复仍待完成。
0.1.7-dev 改成优先选择镜头内近鱼，并加 MultiDave Fish Preview 标签/十字及网格顶点数诊断。
已在 A04_01_02 记录镜头内、非零鱼网格与生命周期回调，用户反馈“看到了”，随后报告鱼在镜头内突然消失。日志两次记录 Local avatar part was destroyed 导致自动断开，稳定性验收不通过。
0.1.8-dev 跳过销毁的本地显示槽位并安排列表刷新，不再因这种临时部件变化终止会话；另放宽已选鱼的镜头边缘范围。
编译与核心 58/58 通过，已部署；原生恢复与用户稳定性反馈尚待实测，证据见 ../logs/avatar-recovery-build-verification.json。
0.1.8-dev 实际潜水无旧异常，但未触发过期槽位恢复标记；用户报告鱼和标签一起消失，标签会换到另一条鱼。
日志 17 条 Ready 概要均可见/镜头内，但选中编号连续改变，证据见 ../logs/native-fish-preview-stability-verification.json。
0.1.9-dev 锁定活鱼身份，镜头/距离/临时显示缺失不再重选，只有合法移除/死亡/捕获或显式按钮触发替换。
新增即时选择与显示状态日志，62/62 核心测试及编译通过；已部署，用户确认不再突然消失；原生初选/手动重选/出镜后重入已有即时日志，动画/正常返航验收仍待完成。
实机证据见 ../logs/native-fish-preview-identity-verification.json；这仍是单游戏显示验证，预览不可捕获。
0.1.9-dev 标签鱼仅显示，用户确认无法捕获；后续已准备反向实体绑定，M5 命中/房主裁定仍未接入。
该地图存在一个无法生成显示描述的鱼，数值状态仍可传输；不把一个预览推广为全部鱼型支持。
前缀不跳过原方法、只记录已观察鱼的 CLR 状态；仅开启房主鱼诊断时安装，关闭/断线只卸载本插件的挂钩。

0.1.11-dev 历史构建与实机范围见 [鱼群与交互构建摘要](../logs/fish-world-interaction-build-verification.json)：

- `FishWorldBuffer` 原子接受完整数字清单，每鱼独立最多 16 帧，旧 epoch/修订拒绝，一秒失联只隐藏。
  `RemoteFishWorld` / `FishDisplayNode` 显示所有可解析的收到活鱼；缺 Visual、源不可见或离镜头保留数字身份。
  清单仅房主玩家当前场景的活动、已初始化鱼；收到完整清单不代表完整海洋、全鱼型支持或客机 AI 接管。
  日志分开收到实体/鱼总数、活鱼、可显示、组件可见、镜头内、未知资源、缺 Visual 与实际自建节点数。
- `FishInteractionHooks` 在 8 个声明方法观察原生发射、伤害、挂钩、QTE 胜利与入袋的 prefix/postfix。
  `CallId` 成对关联，prefix 固定冻结 CLR 快照中的 epoch/EntityId/代次，postfix 复用这份绑定。
  回调不访问 Unity/HP、不跳过原方法、不改返回；伤害 bool 只是原返回，`HpAtDrain` 是主线程消费时读数，
  两条成对日志不构成扣血差、捕获或奖励证据。未绑定、丢弃、线程/回调错误及卸载均须核对。
- `ObservedHostTargets` 冻结发布本地指针到房主身份的 CLR 快照，生命周期复核阻止池复用误绑；不传本机 token/指针。
  原生目标仅在 Unity 主线程重新核对，查询值不是伤害或捕获授权。
- `MapSelectionCapture` 读取加载后的路线与 IGP 清单；所有选中场景已加载、每场景至少一组，
  查找的控制器与原注册列表一致，并在两个不同 Unity 帧取得相同指纹后才接受。
  地址跨机稳定性尚未验证；没有调用随机选择、加载、存档写入或在加载前采用房主地图。

新版鱼群可见与移除同步已获用户确认；动画、完整捕获链与正常返航仍待验收，真正合作捕获、原生 AI 隔离、双游戏闭环与冷配置验收未完成。
0.1.11-dev 已部署并重新启动，实际进程启动于 2026-10-04T06:51:29Z；新鲜日志确认
BOOTSTRAP_OK（0.1.11-dev）、UPDATE_OK、NETWORK_READY 及 DR_Start/DR_Logo。
本次自写 DLL SHA256：`05C5379FF301C55D6841FA23DE00BDCC4EB80B6D404CA6122AC5A17523140E01`。
框架 Class::Init 替代实现警告仍在，启动通过不等于新原生挂钩或显示验收通过。
随后实际 A03_01_02 已记录 49 条 Loopback Ready 概要、53 条 FishWorld 状态；观察/绑定/可显示/可见最大为 16，
网格顶点 662，未知/缺 Visual/显示错误为零。用户确认出现成对的偏移鱼、捕获原鱼时两只同时消失；
关闭 Display received fish roster 后恢复正常，操作和镜头正常。Local test 保留原鱼加显示副本，不是统一世界或捕获副本。
8 个交互入口 installed/healthy，42 条事件为 21 对 CallId，HarpoonFire 28、FishHookedByProjectile 10、FishDamage 2、SpecialDamage 2；
两个原 bool 为 true，14 条事件消费时有可用原生目标，prefix 绑定固定，回调/解析/未配对/原生查询错误为零。
两个伤害声明的 __state 配对已在这条实机路径观察；未见 QTE Win 或 Pickup，不能认定完整捕获链。
地图清单失败 Selected route incomplete。已有 FISH_INTERACTION_STOPPED 与 NETWORK_DISCONNECTED；
用户确认主动退出且未返航；正常返航保存及动画仍未确认。

0.1.12-dev 已安装构建范围见 [操作门禁构建摘要](../logs/fish-action-gate-build-verification.json)，该版编译及 109/109 测试通过，已部署并通过主菜单启动验证：

- `FishActions` 定义 ProbeTarget、FireHarpoon、FireGun、SubmitQteInput、RecallHarpoon、RequestPickup 的 schema、复制及规范指纹；请求不含 damage、收益、native token 或代次。
- 协议 4 的请求/结果各走独立有界 FIFO，来源来自握手绑定的玩家，guest 只接受与 outstanding 请求元数据及指纹一致的结果。
  会话容量为入站请求 16、出站动作/结果 32、入站结果 32、outstanding 32；控制/心跳优先，动作与角色/世界数据公平轮转，不覆盖旧意图。
  合法 pause/场景切换时旧请求或结果发布在会话锁内返回 false，不错误断房；GUI 发送异常捕获并显示可读状态。
- `HostFishActionGate` 保留房间/绑定玩家的 RequestId 高水位、1024 个终态缓存及最多 16 个 pending；新有效 ID 的业务拒绝也消费 ID。
  同请求同内容返回原阶段/结果，同 ID 不同内容冲突；换 epoch 和缓存淘汰不允许旧请求重新执行。16 突发/每秒 8 个请求预算独立于武器冷却。
- 请求到达最多一秒、权限 facts 与候选 lease 最多 0.25 秒；目标代次、玩家/装备版本及租约在派发前再核对，先标记 Dispatching。
  未进入可明确 NotStarted 释放；已进入但不确定为 OutcomeUnknown，不重派发、不凭超时释放资源，现阶段保留到关房。
- F11 的 Check selected fish target 只在 Guest/Local test 的 Ready 且单鱼诊断已选中目标时可用；房主主线程重新 native 查鱼/代次和终态。
  通过只返回 DryRunValidated、OperationId=0，永不调用鱼叉、伤害、捕获或收益方法；不能把 Target checked 当捕鱼成功。
  读取前后检查 lifecycle 健康与代次，不保留失效身份；同 epoch 的 Transmit 开关不归零世界 revision。
- 真实动作的可信 actor/loadout、地图权限、guest 隔离、本地原生操作竞争裁定与 native bridge 均未接入，effects 保持 false。
  纯 CLR 计划/派发防护测试不证明已经进入原生；三种本机 TCP 操作夹具（往返、旧协议拒绝、take 后场景切换恢复）不等于两游戏或 M5 完成。
- Transmit 开启后独立在入海前后最多 1Hz 读取 `MapRouteObservation`，只在值变化时记录 `MAP_ROUTE_INPUTS`。
  cache/roadmap/first 缺失、cache 太短分别失效并撤销稳定候选；候选 bSelected 层和加载名称只供诊断，不是完整地图选择，也不调用选图/加载/保存写入。

最终自写 DLL SHA256：`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`。
实际新进程于 2026-10-04T07:21:37.0010213Z 启动，日志确认 BOOTSTRAP 0.1.12-dev、UPDATE、NETWORK 及 4 条初始 RouteInputs。
这是主菜单启动与初始只读输入证据，尚无新版 Probe、潜水路线、场景切换或正常返航实机验收。
用户当前不方便试玩，手动潜水 Probe/路线/返航验证已延后，暂不催测；继续开发时不能将这些条目标为通过。

0.1.13-dev 历史构建见 [地图选择调用构建摘要](../logs/map-selection-call-build-verification.json)，Build 及 112/112 核心测试通过；当轮没有部署、启动或原生回调验证。
本次构建自写 DLL SHA256：`E5013FB314A17D618F50AF0D8FA3DFB35CCD161DF069703D2759D92FC0CFCCA3`，这不是安装/实机哈希证据。

- F11 新增默认关闭的 Observe map selection calls (read-only)，独立于 TCP、Ready 和 Transmit，可在原游戏自然选择/加载边界观察。
- `MapSelectionHooks` 五处入口：SceneContext.cacheSelectedScenePath 和 LoadSceneMapCacheFromSave 的 postfix、IGPSetController.GetRandomIGPSetInfo 的原 __result postfix、IGPSetInfo.LoadPrefab 枚举器工厂 prefix、SceneLoader.LoadSceneAsync(string,LoadSceneMode,bool) prefix。
  原方法不跳过、不修改参数/返回，不调用 MoveNext，不读取异步返回 handle；metadata 匹配和编译不证明原生 patch ABI 或调用顺序。
- callback 当次确认 Unity 主线程后复制直接字段到有界 CLR；native wrappers 仅在立即复制期间使用，不放进 queue/drain/network。
  非 main 回调跳过 native 读取并记录线程原因；全进程最多 1024 条、queue 64，空选择、截断、不可用路线和 ReadError 分开报告。
- `MapRouteSelection` / ValidateRoute / CopyRoute / FingerprintRoute 使用严格 3..32 场景全链、有限数值、唯一身份和深复制。
  RouteFingerprint 为 map-route-v1，完整 IGP manifest 仍为 map-selection-v1；路线已完整不能填补随后才选择的 IGP，旧完整指纹保持兼容。
- `MapSelectionCapture` 两处 IsInitDone 改为直接 backing field；加载后完整选择仍要求 Loaded、每场景 IGP、原注册集合一致和两帧稳定。
- factory prefix 只证明枚举器工厂调用边界，不能证明真正资源请求、MoveNext 或加载完成。尚无所有 selected-before-all-loaded 的全局时序证据，未共享/采用地图或调用选图/load/save 写入。
- Disconnect 关闭并卸载本 Observer，清理自己的有界队列；新挂钩安装/自然回调/卸载须在之后真实游戏验证，用户当前不方便试玩，不催测。

0.1.14-dev 历史范围见 [地图选择传输构建摘要](../logs/map-choice-transport-build-verification.json)。核心及真实回环 TCP 夹具验证的是候选传输，本轮未部署/启动，没有新原生 ABI、双游戏、选择采用或正常返航证据。
本轮构建自写 DLL SHA256：`AF3CB8BE38E0402ECBA173F86C3C458CC82D6248664E16A35E58FC08729766D0`；这不是安装或实机哈希。

- 协议 5 的路线每片最多 8 场景、最多 4 片，map FIFO 最多 32 包，房主发布、客机接收且绑定握手 Room。控制/心跳和 MapChoiceRetire 优先，动作/角色/世界/地图四路公平；新路线先完整复制/校验全部分片再原子入队，取消旧未发批次。
- generation/revision 独立于 scene epoch，WaitingForScene 可传输而不提升 Ready。新 generation 首片立即撤销旧路线，完整拼装后原子提交，再按连续 revision 记录 IGP；同组选择以新 revision 更新，不猜组集合已完整。
- SessionSnapshot.MapChoiceGeneration/MapChoiceRevision/MapChoiceFingerprint 供房主在会话锁内发布；合法旧或已退休回调返回 false，未来代次/当前冲突及错误方向/Room 拒绝。溢出主动发控制撤销，重复 inactive Retire 返回 false；新代次在旧 Retire 后发送。
- 普通场景/帧清理保留 preload 候选，显式 Retire 清本代次但保留房间高水位，Close 清 source/assembler/mailbox。cache/restore 的自然样本即使指纹相同也开启新 generation，SceneLoader 同指纹去重。
- MapChoiceController 用 callbackFloor 排除绑定/撤销前已经排队的旧观察；copy 错误、丢失和 Truncated 主动撤销。未绑定 IGP 不缓存；已发布组再次空/unknown 时撤销候选，未知新组空值仍为 Unbound。
- callbackFloor 无法证明原生来源：新 cache 后迟到、同 scene/address 的旧 controller 回调仍可能附当前候选。DTO 尚无 controller/context 代次，所有日志 NativeGenerationBound=false、CrossMachineAddressVerified=false；Snapshot 始终 ObservationOnly=true、HostSelectionApplied=false。
- 后续补本地 origin/代次与跨机地址证据，在安全加载前边界采用房主实际选择并隔离客机临时进度/生成/AI，再推进原生捕鱼与返航账本。当前收到候选不能充当 MapAuthorityReady 或 GuestStateIsolated。
- Test-Core 与测试 csproj 编译实际 MapChoiceController 和 MapSelectionCallObservation，仅替代 logger。134 项中包含 4 项实际源适配用例，使用 synthetic DTO 与实际回环 TCP；不运行 NativeHook、不调用游戏入口，不扩展为原生 ABI、双游戏或正常返航验收。

首次安装记录在忽略的 `artifacts/framework-install.json`；
原始 EXE、GameAssembly.dll、UnityPlayer.dll 的 SHA256 经复核未改变。

玩家入口为仓库根目录 `setup.ps1`，通过 `Install-Mod.ps1` 进行自动定位、
框架校验、发布包校验、插件备份与安装，机器日志写入 `.local/logs/*.jsonl`。
源码入口为 `src/DaveCoop/Plugin.cs`，编译脚本可使用现有 SDK 5 的 Roslyn
和游戏目录中 BepInEx 自带的 .NET 6 库，无需另行下载完整 SDK。

0.1.10-dev 已编译本地反向目标查询 HostEntityRegistry.TryResolve(epoch,id) 和 Unity 线程 TryResolveNativeFish。
查询复核当前实例/指针、种类、场景和启用代次；失活/回收后的旧绑定不提供可操作原生引用。
同 epoch 清理保留递增计数，断房间才 ResetRoom；这些只建立目标身份，不构成攻击/捕获授权。
0.1.10-dev 历史构建新增 6 项核心用例，68/68 通过；当时未部署/执行新原生适配。构建摘要见 ../logs/host-target-build-verification.json。
该历史构建 SHA256：`5BF9494E3E6C7782C9A3F4B077C7C1075EEC6CF92892B39B76734A993232204D`。M5 可复现签名研究见 scripts/Inspect-FishInteractionApi.ps1 与 WORLD_SYNC。

## 尚未完成

首版玩法已按用户选择收敛为[房主＋员工、每人独立背包](CREW_MODE.md)：房主自己的原生LootBox，员工由房主持有的Mod会话工作袋，
各自容量/负重独立；长期任务/图鉴/材料/经济归房主。双方计划独立操作及生存状态，先同层潜水由房主带队；已准备CLR账本，实际员工玩法尚未接通。
捕获账本必须跨场景/员工断线保留；员工产物/容量判断需分流且不污染房主原袋。返航房主袋原链不补Add，员工袋需新结算桥逐条确认一次入仓；
客机全部自动写入隔离、原生提交证据、员工actor/loadout/vitals和唯一结算仍是必做项。后续开发先读CREW_MODE。

M3 游戏适配实机验证和真实双游戏验收尚未完成。
真实双游戏连接验收、客机原生鱼群/拾取接管与临时进度恢复均未完成。
0.1.11-dev 的活动鱼群可见与移除同步已获用户确认，Fire/Hook/Damage 观察已有单游戏日志；已安装 0.1.12-dev 仅主菜单启动及初始路线输入通过，新源码 0.1.15-dev 仅构建。自然地图调用、Probe/潜水路线/场景切换、动画、完整捕获链和路线完整读取仍待验证，不能据此标记 M4/M5 完成。
服务器方案暂时搁置。当前包是验证开发入口的原型。

## 下一步

0.1.15-dev已准备独立的纯CLR个人Cargo账本及四处Loot/返航只读观察，详情见[员工模式](CREW_MODE.md)和[构建摘要](../logs/cargo-ledger-build-verification.json)。
账本寿命不绑定NetworkController.Disconnect；尚未创建真实潜水账本或发布网络袋清单，不能把CLR候选测试当原生分流/入仓通过。
F11的Observe loot and return calls默认关闭、独立TCP；每次正常捕获及返航时自然观察，不提供模拟Add/补奖按钮。
新版未部署/启动，原生ABI、现场重量读取、来源/产物强关联与正常入仓/保存均待用户方便后验证。

按 `PLAN.md`、`GAME_API.md`、`MULTIPLAYER.md` 和 `WORLD_SYNC.md` 继续。
M2 历史验证构建 SHA256 为 `E2A7DEC29ACB894F72A2D8528C099E82AB867DDE00F82DC739707BAA8EBB5AB9`。
用户保存退出后部署并启动 0.1.3-dev，SHA256 为 `1E2264723B799150033C55F0754E73DA9F33AFEF5C61FEE5A5030A7DECB8064D`。
随后部署 0.1.5-dev，实机 DLL SHA256 为 `F8063FA1E1C025A8B17575A5C763AF15B662B44D84377D29787886A17C5BEA60`。
实际新进程启动于 2026-10-04T05:45:30Z，已通过只读探针及本机 TCP 读取/传输的日志验证。
原始 0.1.5-dev 会话已归档到忽略的 .local/verification/fish-preview-0.1.5；用户允许再次打开测试。
0.1.6-dev 实际新进程启动于 2026-10-04T05:56:35Z，目前已确认加载/Update 和世界探针，已请用户执行 F11 本机测试。
0.1.6-dev 历史编译/部署 SHA256 为 `8563E3C2835178CF03299851C904F471CE5873A094B57A2432BDA44B2AFE1A24`。
用户主动关闭后已归档该轮日志并部署 0.1.7-dev，SHA256 为 `53336298F2C47D3BCC3F300E21B5629938C8ED4A2C0A167C95E2B10CAEA31849`。
0.1.7-dev 已退出并归档；0.1.8-dev 已部署，SHA256 为 `7950EF93D20CF6E9912C14390D8649CB1C501ADB73A61495DEF9899777C875B5`。
新进程启动于 2026-10-04T06:16:09Z，加载/Update 已确认。请用户潜水、射击并观察连接与预览稳定性，再 Disconnect/正常返航。
随后确认该进程退出并归档，部署 0.1.9-dev：SHA256 为 `1F2D0C3B8B42B9439BAE1ADB6339238DA792D98A7DC50FC055317908B9811D86`。
实际进程启动于 2026-10-04T06:26:47Z，已在真实潜水记录固定编号、出镜/重入和手动重选，用户确认“不再消失”。
这轮已记录自己的挂钩卸载/Disconnect，随后再次本机连接；正常返航/保存与动画反馈仍待确认。
后续部署仍需正常保存退出，先核对新进程版本/加载，F7 开启观察入海/捕鱼/返航生命周期。
不要把新探针的编译证据或旧版本启动证据当成它已运行。
对照 NETWORK_STATE 的 Ready/角色帧/资源及 LAYOUT_READY 或 WARNING，修复实际问题。
本机测试仅是单游戏中的两个 TCP 会话；不能标记 M3 双游戏或 M4 同一地图完成。
之后做真实双游戏测试，并验证资源键、地图布局指纹在两机上的稳定性。
Sprite 回放仅验证显示路径，网络消息必须解析资源键，不可跨线程使用 Native Sprite 引用。
后续游戏交互仍需研究真实控制流和原角色组件副作用。
已经读到地图节点/IGP 选择、FishAllocator 生成、FishAISystem 的种类/HP/捕获状态、
Damageable.TakeDamage 与鱼/物品 SuccessInteract 等签名；尚未执行这些写入入口。
探针记录本机 ID 仅用于观察；网络数值实体已使用房主分配的 RoomId/epoch/EntityId。
当前 0.1.12-dev 已确认主菜单启动；用户方便时正常退出后部署当前开发源码，核对 0.1.17-dev 新进程版本与加载，不以旧版启动证明新观察或传输适配运行。
当前候选测试先建Host/Join或Local test，再开启Observe loading coroutine and scene ownership并进行新自然入海。旧Observe map selection calls仅可选诊断，不能发送候选；可核对 MAP_SELECTION_HOOKS_READY、MAP_SELECTION_CALL、MAP_SELECTION_OBSERVER_STATE、MAP_SELECTION_HOOKS_STOPPED 的五处自然边界、线程、路线候选、空/截断/读取错误及自己的卸载。
随后按目标检查范围在 F11 / Local test 开启 Transmit read-only fish observations 后进入潜水。
开启单鱼预览取得选中身份，点 Check selected fish target，核对 FISH_ACTION_SENT / ADMISSION / DECISION / RECEIVED 的请求元数据、指纹及 DryRunValidated/op0。
测试源鱼移除、场景失效、Disconnect 与正常返航保存，不把拒绝或目标检查当捕获成功；再按需要开启 Display received fish roster 与只读交互观察。
核对 WORLD_RECEIVED / NETWORK_STATE 及 FISH_WORLD_STATE / TRANSITION 的数字总数、可显示/未知资源/镜头内/节点数，
并确认画面、转向、镜头外重入、鱼终态和 Disconnect/正常返航清理。本机偏移后的显示鱼不可捕获；应操作原生鱼观察调用链。
核对 FISH_INTERACTION_READY / INTERACTION / STOPPED 的前后成对 CallId、prefix 绑定、原 bool、消费时 HP 及错误/丢弃统计；
地图独立观察核对 MAP_ROUTE_INPUTS / WARNING 的入海前后输入变化、截断/上限标记与缺失原因；MAP_SELECTION 仍须完整条件与 PostLoadObservationOnly，不把候选层当路线清单或加载前接管。
显示仅创建自己的 SpriteRenderer 或 SkeletonAnimation；原鱼及其 AI 保持原样，不能当成共享鱼群。
对照 F7 探针核实实际鱼值和生命周期。地图传输核对 MAP_CHOICE_* 的路线分批/修订/撤销与明确不可用，补 controller/context 原生 origin/代次及两机地址证据；随后实现加载前实际采用、客机临时状态/生成与 AI 隔离和 M5 裁定，而非仅继续显示候选。
当前数值通道只覆盖玩家所在场景的已初始化鱼；新生命周期挂钩需验证实际启停/销毁与池复用，再扩大至完整世界事件。
Spine 引用来自现有生成的 spine-unity.dll；资源键为名字/缩放/图集描述哈希，跨机稳定性待验证。
Spine 暂只表达一条主动画/皮肤/颜色/缩放，混合、多轨、槽位材质和约束等未覆盖；非 Spine Mesh 鱼待研究。
用户观察第二个戴夫不发射鱼叉：当前只捕获角色显示，不包含独立投射物或发射事件。
M5 需接入鱼叉发射/飞行/命中/回收及房主裁定，不能给显示副本直接启用原生武器逻辑。

## 已遇到的问题

- 沙箱内网络请求曾被拒绝；使用授权的工具提权联网完成官方框架下载。
- 完整 SDK 下载遇到 C 盘空间不足；删除此次未完成下载，改用已有编译器。
- 框架的 dotnet 目录包含 `System.IO.Compression.Native.dll`；
  它不能作为 C# 托管引用，编译脚本已排除 Native DLL。
- 首次互操作生成有若干方法恢复失败，框架日志有 Class::Init 替代实现警告；
  基础插件仍通过加载和 Update 验证。这不代表所有游戏函数均可挂钩。
- 当前沙箱内窗口枚举无法取得用户桌面窗口，视觉结果需另行确认；
  加载验证以当前游戏日志为证据。
- 首轮玩家探针的托管辅助方法被 IL2CPP 注册器尝试导出，产生不支持类型警告；
  已加 `HideFromIl2Cpp` 并真实重启验证，修正版不再出现这些警告。
- 新代码用现有编译器/框架库时，Task.Run 异步泛型 lambda 出现推断及 NullableAttribute 编译错误；
  改为独立 async 函数与 ConfigureAwait(false) 异步 I/O 后编译通过，连接任务不接触 Unity。
- Il2CppStructArray 不实现 System.IDisposable；不能使用 using 声明，使用互操作包装器生命周期。

## 发行边界

仓库提供自写插件安装包，BepInEx 从官方站按固定 SHA256 下载。
游戏版本改变时先重新验证，再更新依赖和发布包元数据。
禁止把编译通过解释为全流程联机已完成。

## GitHub 配置验证

已推送到 https://github.com/gao-xh/MultiDave 。从链接重新克隆后的自动定位、
安装与启动流程已执行，结果见 ../logs/github-verification.json。
测试机器已有框架，因此尚未独立验证根入口在完全未装框架的新机器上的冷安装。
框架本身的首次安装及生成接口在本次开发中已通过。

M1 源码与计划已推送至 `codex/player-discovery` 分支。自动审批首次拒绝公开推送
含真实运行摘要的文件；用户明确授权公开自写源码、计划、日志及验证摘要后，重新推送成功。

## 0.1.17 当前接线与下一桥

0.1.17 将当前固定来源 CLR 清单接入候选发送：建房时记录实际 Run/owner floor，建房前 entry、退休 Run/owner/controller 和旧回调不能提供新来源。集合删除或替换先退休旧 wire 代次再重发，每帧最多 8 条选择；诊断队列消费不影响当前清单。旧 Observe map selection calls 仅诊断，其关闭或丢失不发送/撤销来源。

新增 6 组 Core 快照和 6 组实际回环 TCP 适配测试，原 4 项源适配已迁移，总计 160/160 通过；Build 警告视为错误通过。测试使用合成标量，不运行 NativeHooks、NativeCapture、Unity provider 或两个游戏。NativeGenerationBound、HostSelectionApplied、GuestStateIsolated、WorldAuthority、CargoAuthority 仍为 false；未部署或启动。

当前行为见[固定来源候选传输](ORIGIN_MAP_TRANSPORT.md)和[0.1.17 构建摘要](../logs/origin-map-transport-build-verification.json)。0.1.14 的 callbackFloor/cache 来源与 0.1.16 的“仅日志”是历史范围，当前发送流程按新文档执行。

客机的原生 Serialize/Deserialize、双 Data/Interaction 根及直接恢复候选已离线定位，见[客机影子桥研究](GUEST_ISOLATION.md)。SaveData(string ver) 不是 JSON 构造器，SetLoadedData/Load 不是纯交换；旧协程、缓存、可变子树及全部持久输出仍需隔离与恢复验证。尚未执行原生克隆/根替换或证明 GuestStateIsolated。每人的独立容量和负重规则保持不变。
