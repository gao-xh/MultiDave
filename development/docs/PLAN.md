# MultiDave 开发计划

当前插件源码0.1.32-dev、协议6；本轮实际Core/TCP256/256与Build警告视为错误通过（新增6项返航计划、4项提交夹具）。[员工逐项返航计划](EMPLOYEE_RETURN_PLAN.md)固定独立转换指纹并沿既有ledger一次提交；typed入仓helper已编译，无GUI/network producer，真实品质/数量政策、分流与库存增量/保存未验。证据见[构建摘要](../logs/employee-return-plan-build-verification.json)。未部署/启动，安装0.1.12、最近潜水0.1.11、默认包0.1.0保持；每人独立容量/负重、实际捕鱼/返航、客机隔离/房主世界、双端与冷配置仍待完成。

目标：Windows Steam 版双人潜水合作 MVP，先做局域网房主/客户端。
房主负责游戏世界和结算。服务器方案暂缓。
首版采用用户提出的“房主＋员工、每人独立背包”规则，权责、个人容量/负重与捕获/返航关联详见[员工模式方案](CREW_MODE.md)。
每个阶段达到可观察的验收标准后再推进；进度同步写入 DEVLOG 和 HANDOFF。

## M0 — 开发入口与可复现配置：已完成

已完成 BepInEx 安装、插件加载、场景日志、自动安装入口、仓库 Skill 和 GitHub 克隆配置验证。
新电脑的首次冷安装尚未独立验证。

## M1 — 玩家、输入与摄像机发现：基础验收通过

- 离线读取已生成程序集的类型和签名，定位玩家控制器、生成入口和摄像机管理器。
- 加入只读运行时探针，记录主菜单、船上、潜水场景的对象及生命周期。
- 明确移动、朝向、动画和场景切换应从哪里获取，区分本地玩家和其他角色。
- 验收：真实潜水场景读到本地玩家位置/朝向和摄像机绑定，跨场景读取不会持续报错。
- 证据边界：IL2CPP 互操作 DLL 的方法体是封装代码，类型签名不能证明原游戏控制流。

当前结果：离线接口工具、只读运行探针及汇总工具已实现并编译通过。
真实 `A01_01_01` 潜水已确认管理器玩家引用、位置、朝向、移动输入、动画与摄像机跟随。
继续进入 `Boss_000` 后，旧玩家不再出现在当前采样中，新实例与管理器和摄像机正确绑定，
跨场景读取无探针错误。完整旧日志后来还确认返航、大厅与主菜单，1886 条快照无探针错误。
默认发行包保持 0.1.0，0.1.22 历史源码为 0.1.22-dev、协议 5，插件 Build 警告视为错误通过；未部署/启动。0.1.22 该轮 Core 输入未改，复用 0.1.21 实际通过的 176/176 结果，没有重跑测试。
当前安装及最近新鲜启动为 0.1.12-dev/109 项测试，加载/Update/网络入口及 4 条初始 RouteInputs 已确认，仅主菜单启动通过；新地图调用观察、Probe、潜水路线、场景切换与正常返航仍待实机。
用户当前不方便试玩，手动潜水 Probe/路线/返航验证已延后；后续自主开发保持各项实机验收边界。
最近完成潜水验证的是 0.1.11-dev。
新版单游戏 TCP 偏移鱼群可见和原鱼移除时副本同步消失已获用户确认，关闭显示后恢复正常，操作和镜头正常。
发射/挂钩/伤害只读观察已运行；动画、完整捕获链、路线完整读取及正常返航仍待验收。
最近单鱼稳定性实测来自 0.1.9-dev；实际鱼传输和生命周期历史证据来自 0.1.5/0.1.6-dev。

## M2 — 本地第二角色显示：基础验收通过

- 先创建只显示位置和动画的远程角色，不绑定本地输入。
- 识别并隔离单例、摄像机、物理碰撞、任务监听和存档副作用。
- 验收：一场正常潜水中能显示第二角色；本地输入和摄像机正常；切换场景后正确清理。

当前结果：独立 SpriteRenderer 节点、姿态缓冲/插值、F10 开关和生命周期清理已编译。
用户在 `A03_01_02` 潜水确认第二角色可见、正常回放动作；日志确认 F10 停用/重建、
原生玩家数为 1、摄像机保持本地绑定且返航清理，无回放警告。基础验收通过。
显示部件从 21 增至 25，0.1.3-dev 补上武器延迟加载的自动刷新，修订仍待实机复核。
相关说明与验收入口见 `MULTIPLAYER.md`。

## M3 — 局域网连接与移动同步

- 加入双人房间、协议/Mod/游戏版本握手、玩家身份与断线处理。
- 主线程处理 Unity 对象；网络线程只处理数据，通过队列传递。
- 移动使用快照与插值，场景事件使用确认与超时处理。
- 验收：两个客户端能连接、互见角色、同步移动和朝向，断线能回到可用状态。
- 本机双端传输测试可先执行；真实游戏联机验收需要两份运行中的 Windows 游戏实例。

独立于 M2 试玩的协议、握手、长度分帧、序号、校验和连接取消已实现，
纯 CLR 及本机 TCP 双端夹具通过。已实现线程安全会话边界、主线程数据邮箱、
场景确认/提交/暂停、心跳、时钟估算及超时。资源键生成/歧义检测已测试，
0.1.3-dev 加入 F11 房间入口、本机 TCP 测试、主线程捕获和资源解析、远程插值显示，
以及静态碰撞几何/动态节点选择的布局指纹。已部署，新进程的组件加载已验证；
0.1.5-dev 在真实潜水中确认本机 TCP Ready、布局读取、玩家/鱼资源解析与显示组件运行；
视觉反馈、断开/返航和真实双游戏运行仍待验收。
布局指纹不能代替 M4 的统一地图生成和实体同步；真实双游戏移动及跨机资源键待验收。
0.1.12-dev 协议 4 新增独立请求/结果 FIFO、绑定来源及 guest outstanding/指纹核对，拒绝协议 3。
三种本机 TCP 操作夹具（往返、旧协议拒绝、take 后场景切换恢复）通过，只证明编码/会话/传输行为，不是两份游戏联机或 M5 成功。
0.1.14-dev 协议 5 加入独立地图 FIFO：路线每片 8 场景/最多 4 片，地图队列 32 包，host only publish/guest only receive、Room 绑定；控制/心跳与撤销优先，动作/角色/世界/地图四路公平。旧协议 4 拒绝，真实 TCP 路线、连续选择和撤销夹具仍只验证网络数据。

## M4 — 同一潜水世界

- 房主统一地图选择/生成与入场流程，先验证地形、场景实例和实体一致。
- 建立会话内实体 ID，同步鱼的生成/移动/死亡和物品生成/移除。
- 验收：双方处于同一地形，鱼与物品身份一致，加入/离开场景不会留下重复实体。

已补充 [WORLD_SYNC](WORLD_SYNC.md)：房主发布实际地图选择，客机加载前采用，
通过布局及资源确认后提交世界；鱼使用会话/epoch/房主实体 ID，客机显示房主状态。
本机元数据已定位地图选择、鱼生成、HP/捕获、伤害与拾取签名，原始控制流待验证。
0.1.5-dev 已部署，实际潜水探针 72 条快照无错误，实际鱼清单经本机 TCP 接收，单鱼组件显示运行。
视觉和清理验收尚未完成；0.1.6-dev 已实测生命周期挂钩安装与回调，池重新启用仍待验证。
0.1.7-dev 用户确认带标签鱼可见，但镜头内突然消失，日志两次记录角色部件销毁导致断开。
0.1.8-dev 跳过已销毁显示部件、安排刷新并保留会话，镜头边缘保留范围也已放宽；0.1.8-dev 实机无旧异常，但用户报告标签跳到另一条鱼后又消失。
0.1.9-dev 保留当前活鱼身份，镜头/距离/临时显示缺失不换目标；只在移除/死亡/捕获或手动重选后替换，已部署并获用户确认不再突然消失；动画/正常返航仍待验收。
实体身份、完整快照分块/原子提交、旧 epoch 清理及 TCP 血量更新/清单移除已用夹具验证。
显示描述的传输/校验、插值/移除/失联隐藏、慢连接连续快照及池启停代次已用夹具验证；原生鱼可见已获用户确认，稳定性/动画/清理验收未完成。
0.1.11-dev 已编译完整收到的活动观察鱼群显示：完整数字清单原子增删更新，每个 EntityId 独立保留 16 帧，
旧 epoch/修订拒绝，失联一秒隐藏；缺显示描述和镜头移动不释放身份，原鱼 AI/碰撞/收益保持原样。
清单仅房主玩家当前场景的活动且已初始化鱼，完整接收不代表完整海洋或所有鱼型均可显示。
加载后路线/IGP 清单读取要求所有选中场景已加载、每场景至少一组、查找与原注册列表一致、两个不同 Unity 帧指纹稳定。
该读取尚未在加载前控制房主选择；层级地址跨机稳定性待验证，不是地图接管或新入海屏障。
0.1.11-dev 实际 A03_01_02 本机 TCP 已记录观察/绑定/可显示/可见最大 16、网格顶点 662，未知/缺 Visual/显示错误为零。
用户确认成对偏移鱼可见，捕获原鱼时副本同时消失，关闭显示后恢复正常，操作和镜头正常。
Local test 保留原鱼并显示副本，成对及同步移除不代表统一世界或捕获副本；动画和正常返航仍待确认。
0.1.11-dev 地图清单读取失败 Selected route incomplete。0.1.12-dev 新增独立 MapRouteObservation，Transmit 开启时入海前后最多 1Hz 观察、值变化才记录 MAP_ROUTE_INPUTS。
加载后完整清单分别诊断 cache/roadmap/first 缺失或 cache 太短并撤销旧稳定候选；bSelected 候选层不能代替完整路线。
0.1.13-dev 构建默认关闭、独立于 TCP/Transmit 的 Observe map selection calls，在 5 处自然 cache/restore/IGP 原返回/Prefab factory/SceneLoader prefix 中即时冻结 CLR。
路线与完整 IGP manifest 分开校验/复制/指纹，1024 全进程/64 队列、非 Unity 回调跳过 native 读取，空/截断/读取错误明确；不保留 native wrapper。
这是加载前时序观察准备，尚未实机；factory 不证明请求/完成，未证明所有选择先于所有加载，未共享/采用地图，Mod 不写选图/加载/存档。
0.1.14-dev 已接入候选传输与源适配：generation/revision 独立 epoch、可 WaitingForScene 发布，普通场景清理保留选择，显式 retire/关房清理；新代次首片撤销旧路线，整批原子拼装，再按连续 revision 更新 IGP。
cache/restore 每次合法自然样本都创建新 generation，SceneLoader 同指纹去重。copy 错误、截断、丢失或已发布组再次空/unknown 主动撤销；未绑定及未知新组空 IGP 不缓存。
callbackFloor 只排除已经排队的旧回调，没有原生 controller/context 代次证明；新 cache 后迟到且同 scene/address 的旧 IGP 仍可附当前候选。NativeGenerationBound=false，所有 Snapshot 为 ObservationOnly、HostSelectionApplied=false，传输不授予世界权限。
0.1.16-dev已接默认关闭的29声明加载来源观察：entry/factory/MoveNext固定owner、精确typedoperation成功Scene结果与controller出生、迟到结果/未知unload围栏。
0.1.16历史新增6组registry夹具（总计148项）验证标量关联和撤销，不运行NativeHooks；只写日志、不接网络地图generation，NativeGenerationBound/WorldAuthority仍false。
本轮未部署/启动，实际回调嵌套、typedreturn/Scene值/__state、原生出生时序和卸自己的挂钩待验收，详见[MAP_ORIGINS](MAP_ORIGINS.md)与[构建摘要](../logs/map-origin-build-verification.json)。
下一步验证本地 origin/代次与跨机地址证据，接入真正加载前房主路线/IGP 采用、客机临时进度/生成及 AI 隔离，再验证双端同地图和同实体；用户方便时再部署验证自然回调、目标检查、画面与正常返航。尚未执行原生选择采用，不能用 CLR TCP 替代实机。
布局核对和签名发现不代表 M4 完成。
历史0.1.14的134 项测试包含 4 项实际源适配夹具：Test-Core 与测试 csproj 编译实际 MapChoiceController/MapSelectionCallObservation，仅替代 logger，以 synthetic DTO 和实际回环 TCP 检查候选传输；不运行 NativeHook，也不证明原生采用。

## M5 — 合作捕鱼、伤害与拾取

- 客户端发出操作请求，房主裁定伤害、捕鱼与拾取结果。
- 给请求加唯一标识并去重，处理同时操作同一对象的冲突。
- 接入独立鱼叉的发射、飞行、命中、QTE 与回收事件；角色显示本身不会生成远程投射物。
- 验收：双方可合作捕同一条鱼；重复消息或同时拾取不产生额外收益。

M5 当前准备：0.1.10-dev 加入房主 epoch/EntityId 反向查询；0.1.11-dev 编译了主线程原生复核、
冻结的本地指针/代次 CLR 快照和 8 个交互入口的只读 prefix/postfix 成对观察。
prefix 固定当时房主身份，postfix 复用同一绑定；伤害 bool 只是原方法返回，HpAtDrain 是事件消费时读数，不能推断扣血差或捕获成功。
8 个观察入口已在实机健康安装，42 条事件为 21 对 CallId，覆盖 HarpoonFire 28 条、FishHookedByProjectile 10 条、FishDamage 和 SpecialDamage 各 2 条。
两个伤害原 bool 为 true，14 条事件消费时有可用原生目标，回调/解析/未配对/查询错误为零；prefix 身份固定，两个伤害声明的 __state 配对在这条实机路径已观察。
没有 QTE 胜利或入袋样本；原 bool 与观察副本同步消失均不证明合作捕获。Mod 未发起原生命中/捕获/收益写入，标签鱼及收到的鱼群目前仍不可捕获。
0.1.12-dev 已构建 6 种动作的请求/结果模型、独立 FIFO、规范指纹、请求高水位/缓存去重、限流/新鲜事实及派发租约。
场景失效取消未进入请求，已进入但不确定的结果不能重试；缓存淘汰或新 epoch 不重置房内重放屏障。
合法场景失效的旧发布在会话锁内返回 false，不错误断房，GUI 发布异常捕获；目标读取前后复核健康/代次，同 epoch 诊断开关保持 revision 单调。
F11 的 Check selected fish target 在 Guest/Local test 发送 ProbeTarget，房主主线程重新查原生目标/代次，通过只返回 DryRunValidated、OperationId=0。
真实发射/QTE/召回/拾取缺少可信 actor/loadout、MapAuthorityReady/GuestStateIsolated/LocalActorArbitrated 与 native bridge，effects 仍 false；格式/门禁通过不等于攻击或捕获。
下一步实测只读请求往返及失效，继续原生 owner/投射物/命中/入袋证据与客机隔离，再接入实际装备/距离/冷却和房主原生裁定。
当前核心与构建范围见[启动观察摘要](../logs/save-startup-build-verification.json)，0.1.22 历史比较器见[比较器摘要](../logs/guest-comparer-build-verification.json)，0.1.20历史食材缓存见[食材缓存摘要](../logs/guest-ingredient-cache-build-verification.json)，0.1.15历史个人账本见[独立背包摘要](../logs/cargo-ledger-build-verification.json)，0.1.14历史候选传输见[地图选择传输摘要](../logs/map-choice-transport-build-verification.json)，操作门禁见[0.1.12-dev摘要](../logs/fish-action-gate-build-verification.json)；
上述历史实机见 [0.1.11-dev 鱼群与交互摘要](../logs/fish-world-interaction-build-verification.json)，均不作为 M4/M5 或真实双游戏完成证据。

## M6 — 返航、结算与进度

0.1.15-dev开始实现纯CLR个人账本和默认关闭的原生Loot只读观察，范围见[独立背包构建摘要](../logs/cargo-ledger-build-verification.json)。
账本暂不接游戏生命周期或网络房间，不能作为员工背包已可玩的证据；员工产物分流、真实容量路由、入仓桥与保存仍未接通。
原GameAssembly离线调用工具已定位实际鱼掉落/容量、六参数入仓、多类别返航与保存/加载副作用目标，
并发现换层协程绕过现有加载观察。下一适配顺序为固定iterator owner/操作/场景寿命、客机临时状态及集中输出隔离、
个人产物/容量分流和按类别逐项入仓。静态边与源码工具通过不算M4/M5/M6验收，见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。

- 每人独立背包、容量与负重：房主自己的原生LootBox，员工由房主持有的Mod会话工作袋；两人的实际收获返航归房主仓库。
- 用ExpeditionId关联跨场景/断线潜水、MemberId关联员工、CaptureId关联真实来源与品质/数量/重量；请求通过、鱼移除或QTE不等于入袋。
- 房主本地操作与员工竞争共用一次裁定；员工产物需完整分流到员工袋，更早的容量检查也按员工计算。证据不全保持未决，不补奖、不重派发未知原生操作。
- 房主统一返航、捕获清单与收益，先只推进房主进度。
- 房主原袋沿原生入仓链不补Add；员工此前未入仓的独立物料须新结算桥按条目确认一次入房主仓库。员工断线保留已确认袋，潜水账本不随网络Disconnect清空。
- 明确客机临时状态和本地存档的恢复流程；改动存档前备份并验证退出路径。
- 隔离客机任务、图鉴、库存、经济及全部自动持久写；首版同层潜水，由房主带队换层和返航，不承诺自动重连或跨崩溃恰好一次结算。
- 验收：完成一次连接、入海、合作捕获、返航、结算的完整闭环。

## M7 — 可供朋友测试的发布

- 用两台机器验证真实连接及不同延迟、断线和场景切换。
- 验证完全未安装框架的 Windows 环境，通过 GitHub 链接由 Codex 配置。
- 更新发行包、依赖清单、README、Skill、日志及兼容版本。
- 验收：公开的安装说明与实际支持范围一致，失败时有可读日志和停用办法。

## 后续扩展

寿司店、完整剧情、Boss、DLC、重连恢复和公网方案在基础潜水闭环稳定后另行规划。
两袋转移、员工持久报酬与自由跨层在独立背包/房主统一入仓闭环之后推进。
不预先承诺工期；主要不确定项是第二角色生命周期、地图一致性和游戏实例测试条件。

## 0.1.17 当前接线与下一桥

0.1.17 将当前固定来源 CLR 清单接入候选发送：建房时记录实际 Run/owner floor，建房前 entry、退休 Run/owner/controller 和旧回调不能提供新来源。集合删除或替换先退休旧 wire 代次再重发，每帧最多 8 条选择；诊断队列消费不影响当前清单。旧 Observe map selection calls 仅诊断，其关闭或丢失不发送/撤销来源。

新增 6 组 Core 快照和 6 组实际回环 TCP 适配测试，原 4 项源适配已迁移，总计 160/160 通过；Build 警告视为错误通过。测试使用合成标量，不运行 NativeHooks、NativeCapture、Unity provider 或两个游戏。NativeGenerationBound、HostSelectionApplied、GuestStateIsolated、WorldAuthority、CargoAuthority 仍为 false；未部署或启动。

当前行为见[固定来源候选传输](ORIGIN_MAP_TRANSPORT.md)和[0.1.17 构建摘要](../logs/origin-map-transport-build-verification.json)。0.1.14 的 callbackFloor/cache 来源与 0.1.16 的“仅日志”是历史范围，当前发送流程按新文档执行。

客机的原生 Serialize/Deserialize、双 Data/Interaction 根及直接恢复候选已离线定位，见[客机影子桥研究](GUEST_ISOLATION.md)。SaveData(string ver) 不是 JSON 构造器，SetLoadedData/Load 不是纯交换；旧协程、缓存、可变子树及全部持久输出仍需隔离与恢复验证。尚未执行原生克隆/根替换或证明 GuestStateIsolated。每人的独立容量和负重规则保持不变。

## 0.1.18 原生根桥与输出围栏源码

0.1.18新增实际typed原生影子桥、单次事务及已枚举输出围栏源码。四类Data原生JSON round trip、五根直接交换/回读/恢复和15个独立强handle已编译；7组新增事务夹具以合成backend验证partial/unknown补偿、fence/refs保留和一次清理，总167/167通过。

当前生产进入与静止边界恒false，事务在围栏安装前拒绝；startup primitive自身再查边界，未接Network/GUI，未运行克隆、根交换、阻断或恢复。194条精确声明不是所有writer、独立native地址或ABI证明；Interaction未Sync、完整子树/旧缓存/协程隔离仍待完成。全部GuestStateIsolated/NativePermission/WorldAuthority/CargoAuthority保持false，未部署或启动。

实现与下一步见[原生根桥](GUEST_SHADOW_BRIDGE.md)、[输出围栏](GUEST_OUTPUT_FENCE.md)及[0.1.18构建摘要](../logs/guest-shadow-build-verification.json)。下一步必须实现可信原生进入/静止边界与缓存/Interaction切换，再进行受控实机验证；个人袋分流、真实地图采用及双游戏闭环仍按原计划推进。

## 0.1.19 当前进展与下一步

已将[typed交互缓存准备](GUEST_INTERACTION_SHADOW.md)接入原生根桥源码，含已知baseline/共享引用检查；Build及170项测试通过，未部署或执行native。当前进入/静止仍false，不把十组绑定或已读节点不相交当完整GuestStateIsolated。

下一步在[更早的entry候选](GUEST_ENTRY_BOUNDARIES.md)补真实自然生命周期证据，接[其它运行缓存的typed准备/恢复](GUEST_RUNTIME_CACHES.md)，证明旧引用和全输出隔离，再推进房主地图采用、独立员工捕获/容量分流与逐产物返航结算。原M3—M7和冷配置/真实双端闭环验收保持完整范围。

0.1.20 已将[独立食材缓存准备/恢复](GUEST_INGREDIENT_CACHE.md)接入第六步源码，174项测试仅覆盖CLR控制及此前范围；[当前摘要](../logs/guest-ingredient-cache-build-verification.json)不证明原生运行。每人独立容量/负重及房主唯一长期收益规则保持；完整资源、缓存、真实地图、捕鱼分流、返航和双游戏仍待验收。

0.1.21 新增[游戏内临时缓存](GUEST_INGAME_CACHE.md)第七步与[精确接口工具](GUEST_INGAME_API.md)，176项Core测试通过、插件Build警告视为错误通过，见[本轮摘要](../logs/guest-ingame-cache-build-verification.json)。六record schema仅覆盖支持子图；non-null助手资源/live设备队列拒绝，不能清空或共享原状态代替。三known baselines在Serialize前捕获闭合，准备后strict复查；逆序恢复7→6→五Save根，最多21explicit handles/4Data stamps，第七singlefield无Mixed。

进入/静止和native/guest/world/bag权限仍false，ordinary record exactclass/object_new候选未运行。后续M3—M7验收必须覆盖剩余资源/actor/cache/输出与旧引用、房主加载前地图采用、每人个人捕获/容量分流/返航、真实双端移动与退出恢复、冷配置发行；当前缓存源码与CLR测试不能缩减这些目标。

## 0.1.22 comparer 支持候选与完整后续目标

[独立comparer源码合同](GUEST_DICTIONARY_COMPARERS.md)与[离线API](GUEST_COMPARER_API.md)只补有限字典准备：int/string/InGameSaveType(int32)的精确Generic/Object和该enum专用Enum采用同class独立对象，source pointer/class/kind与aux参与审计，Ingredients同规则。null原可Capture但Prepare拒，不猜Default/CreateComparer/getter、不共享或清空；custom/文化/hash-salt未知拒。显式(capacity,comparer)后才Add，普通constructor抛时assignment未发生，PartialConstructorAllocationRetentionVerified=false。

0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176，未重跑；[0.1.22 历史摘要](../logs/guest-comparer-build-verification.json)的插件Build警告视为错误通过。七步/21explicit handles/4Data stamps不扩，所有native ABI/fullisolation/entry/quiet/native/guest/world/bag权限false，无GUI/Network自动入口。[冷档候选](GUEST_COLD_PROFILE.md)仅研究首load/slot/output，未采用。继续真实资源/actor/cache/output与边界、房主地图采用、每人独立袋/容量/负重的个人捕获和逐产物返航、实际双端与GitHub冷配置；完整M3—M7不因有限comparer候选缩减。

## 0.1.24 个人袋账本传输

已编译并实际通过 200/200 项 Core/TCP 验证；新增 18 项分页、会话与真实生产适配器夹具。详见 [CARGO_TRANSPORT](CARGO_TRANSPORT.md) 和 [构建摘要](../logs/cargo-transport-build-verification.json)。协议 6 在房间内传两袋的只读账本，整批分页原子提交、五路公平发送及慢连接保留已开始批次；场景变化不清袋。Disconnected 保留确认与未知记录，新 Room/peer 不自动恢复旧成员；重量及 Returned 阶段均仅历史记录。没有游戏原生 producer 接入，没有虚构账本或 GUI 权限开关；实际容量、捕获归属、员工分流、逐项返航与隔离、世界、双端/冷配置继续必需。

## 0.1.25 捕获来源与个人容量边界

本轮 Build 及实际 Core/TCP 209/209 通过，新增9组生产 CLR 来源栈夹具。已有默认关闭的 Loot 观察扩至16精确入口，区分主/追加产物、原随机返回、容量检查、入袋与水下进度；prefix冻结候选、postfix保留scope、void finalizer退出，未知鱼边界遮父，异常/丢失/错线程锁存。详见[CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)与[本轮摘要](../logs/capture-lineage-build-verification.json)。同步包含不是直接caller或员工归属，原bool/int、鱼消失和袋重量都不单独确认捕获；原生回调/ABI未运行。

下一步接独立的潜水级操作编号与来源租约，再在原游戏已经选定整批主/追加产物后的受控阶段进行个人容量检查和分流。不提前重掷保底随机、不把事后Add参数冒充入口前完整计划，不先入房主袋再复制。需要覆盖现有槽合并与任务/成就/解锁副作用；正常返航不能清未知结果，逐产物入仓与客机完整隔离仍待接。

## 0.1.26 来源预约与延后完整产物

Build及实际Core/TCP222/222通过；新增11组生产账本夹具、2组实际清单适配器/TCP。详见[CAPTURE_SELECTION](CAPTURE_SELECTION.md)与[本轮摘要](../logs/capture-selection-build-verification.json)。SourceReserve共享潜水账本捕获编号与来源围栏，EnterSelection在native业务前须已有整批隔离能力，LateSeal仅绑定已选完整且仍未写袋的产物和个人容量。未选Request=null、Intent保留；EnteredUnknown不重新执行/取消，return或断线不把它当零产物完成；晚绑定只补原capture的返航条目。第一份完整已核选择在容量检查之前固定，容量拒绝也不能改成较轻产物；同批次可以新鲜容量再核，不重Roll。旧Reserve仍需已核完整计划，Gate局部编号与Unity实例编号不能当潜水operation或会话player身份。真实native证明全部false，无producer attach、选择/分流/入仓/save执行；下一步显式原选择与提交桥、最终grade/effectiveweight/slot/任务与鱼终态、客机隔离及双端/冷配置。

## 0.1.27 原参数基础资源观察

既有默认关闭16入口在Add_Impl Before同步读取两种精确类的四个资源direct backing fields，After/Finalizer复用owned CLR候选。未知类/null明示不可用，不调用业务getter、主动抽随机或解码货槽。TID/ItemDataID、basegrade/baseweight不代替最终产品映射/品质/有效重量；原生调用、ABI、整批产物与个人分流权限仍false。Build通过；纯CLR/TCP输入与前commit逐文件一致，复用0.1.26实际222/222，本轮未重跑。详见[资源观察](LOOT_PRODUCT_OBSERVATION.md)与[本轮摘要](../logs/loot-product-build-verification.json)。未部署/启动，继续真实选择/提交桥、个人容量/捕获/逐项返航、客机隔离/房主世界及双端冷配置。

## 0.1.28 受限货槽候选

Build及本轮实际Core/TCP226/226通过，新增4组已初始化/非零key的CLR标量解码夹具；原生slot仍仅编译未执行，16默认关闭入口保持。Before对exact槽四struct字段两次守卫采样，prefix仅存候选，After/Finalizer复用且不读原生槽；密钥/隐藏值不入队/日志。未init/key0/fake校验不符明确Unavailable，不补初始化/查静态key或检测器。离线新工具文本默认关闭，三模式原decode/edges不变，4无范围leaf不补猜。最终品质时刻、接口重量、肉量转换与完整产物/个人分流/入仓仍待实际桥。详见[货槽观察](LOOT_SLOT_OBSERVATION.md)及[本轮摘要](../logs/loot-slot-build-verification.json)。

## 0.1.29 已有货槽与品质更新观察

新增三处默认关闭的自然setter观察，覆盖新槽入口之外的总数量合并和品质更新候选。Before/After各自复制四字段，After固定同次prefix身份；Finalizer复用After（含Unavailable），没有After才复用Before。RunId+CallId仅标识本次样本，不能跨调用关联货槽或证明槽寿命/袋身份。数量setter传入的是新总数，不是捕获增量；FinalGrade setter不证明整批或捕获终局。Build和实际226/226 Core/TCP通过，扩展两个既有CLR来源栈夹具；原生回调/ABI未执行，个人产物分流仍待接通。详见[货槽观察](LOOT_SLOT_OBSERVATION.md)及[本轮摘要](../logs/loot-slot-mutation-build-verification.json)。

## 0.1.29捕获选择与提交的历史研究

该研究阶段插件保持0.1.29；该轮只执行新离线研究工具，不重复原226测试或插件Build。普通拾取按CarvableCount逐tier，死鱼身体为另一个tier1配方；主选择也会随机，追加只一次并改保底/dirty。原Add的成功返回在实际Add_Impl之后，槽前已经有负重效果，原容量并非整批预期重量检查。SuccessInteract的UnityEvent转发不提供已证actor归属，回收/尸体状态也非捕获凭证。按[员工选择与提交桥](FISH_YIELD_BRIDGE.md)实现明确合作批次规则；不再增加镜像Core Gate，先接实际成员、选择/分流producer与现账本。完整M3—M7和实机闭环继续必需。

0.1.30已落地一次性员工选择编排与typed原生原语，详见[EMPLOYEE_FISH_SELECTION](EMPLOYEE_FISH_SELECTION.md)。236项Core/TCP与Build实际通过；先登记既有租约，再一次品质、多tier主与一次Plus，保留未知及强资源。下一步建立真实host-owned员工actor事实与完整grade/weight/product映射，接LateSeal、单次分流/共享进度/终态receipt及逐项返航；typed helper未接网络，不放开玩法。完整M3—M7继续。

0.1.31已固定捕获产品并提供同批LateSeal入口，详见[EMPLOYEE_FISH_PRODUCTS](EMPLOYEE_FISH_PRODUCTS.md)。下一步接真实host-owned员工actor与实际分流/共享进度/鱼终态凭证；返航另固定原品质兑换数量与FinalGrade计划。完整M3—M7继续，不把字段完成当玩法授权。

0.1.32已沿原ReturnItem固定员工独立转换计划，并实现同ledger Enter前后的一次typed入仓原语；详见[EMPLOYEE_RETURN_PLAN](EMPLOYEE_RETURN_PLAN.md)。下一步冻结真实资源映射与一次原品质兑换/FinalGrade政策，再接实际仓库桶增量与保存。可信员工actor/捕鱼分流、guest隔离/房主世界、双实例正常返航与冷配置继续完整M3—M7。
