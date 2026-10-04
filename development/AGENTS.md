# 开发约定

- M1 玩家发现与 M2 回放基础验收通过；当前源码为 0.1.19-dev、协议 5，Build 警告视为错误通过、Test-Core 170/170 通过，范围见 [原生影子桥构建摘要](logs/guest-interaction-build-verification.json)；本轮未部署/启动，默认发行包仍为 0.1.0。当前安装及最近新鲜启动仍为 0.1.12-dev/109 项测试，仅主菜单加载/Update/网络入口和 4 条初始 RouteInputs 通过。新观察回调、Probe、潜水路线、场景切换与正常返航仍待实机。最近完成潜水验证的是 0.1.11-dev，用户确认偏移鱼群可见、捕获原鱼时副本同步消失、关闭显示后恢复正常，操作和镜头正常；动画、完整捕获链、地图及正常返航/双游戏验收仍待完成。历史证据保留，0.1.9-dev 用户确认锁定身份后不再突然消失。完成情况以 `logs/DEVLOG.md` 和真实运行证据为准。
- 继续工作前阅读 `docs/HANDOFF.md`、`docs/PLAN.md` 和当前阶段的 `docs/GAME_API.md` / `docs/MULTIPLAYER.md` / `docs/WORLD_SYNC.md`；配置别人电脑时使用仓库根目录的配置 Skill。
- 用户确定首版房主＋员工且每人独立背包，继续捕获/库存/结算开发先读 `docs/CREW_MODE.md`。房主自己的原生LootBox，员工由房主Mod持有的独立会话袋，各自容量/重量/负重；产物与前置容量检查都需正确分流，不能先入房主袋再复制。房主唯一长期进度；返航房主袋原链不重复Add、员工未入仓物料需新桥逐项确认一次。员工断线不清潜水账本，未知原生结果不重试/补奖；隔离客机全部自动持久写。同层带队、独立员工生存/装备/投射物及上述袋/结算仍待实现。
- 0.1.11-dev 含房主目标反向查询、冻结的本地指针/代次 CLR 快照、完整收到的活动观察鱼群显示，以及 8 个原生交互入口的只读前后成对观察。鱼清单仅玩家当前场景，每鱼最多 16 帧；缺显示或离镜头不释放数字身份，原生 AI/碰撞/收益保持原样。
- F11 的 Display received fish roster / Observe host harpoon and fish interactions 默认关闭，房主观察仍须启用 Transmit read-only fish observations。交互在 prefix 固定绑定，postfix 复用；bool 只是原返回，HpAtDrain 只是主线程消费时读数，不代表捕获结果或授权。只卸载自己的挂钩。
- 0.1.12-dev 新增独立操作请求/结果 FIFO、握手绑定来源、规范指纹和 guest outstanding 核对。Gate 保留房间内 RequestId 高水位，业务拒绝也消费新 ID，场景失效/缓存淘汰不允许重放；未知原生结果不重派发。6 种动作具有 schema/Gate，实际 effects 的地图权限、客机隔离、本地竞争裁定、可信玩家/装备及原生桥仍为 false，不能声称可捕鱼。
- F11 的 Check selected fish target 仅发送 ProbeTarget；房主主线程重新 native 查目标/代次，通过为 DryRunValidated、OperationId=0，不执行鱼叉/伤害/捕获。新鲜权限事实及短租约只构成候选计划，派发前还须复核。当前摘要为 `logs/fish-action-gate-build-verification.json`，本机 TCP 夹具不等于双游戏或 M5 验收。
- 合法 pause/场景切换期间旧请求或结果的发布在会话锁内返回 false，不错误关房；GUI 发布异常捕获并显示状态。目标读取前后复核 lifecycle 健康与代次，失败不保留旧身份；同 epoch 的 Transmit 开关不归零 world revision。三种 TCP 操作夹具已覆盖往返、旧协议拒绝与 take 后场景切换恢复，实际游戏切换仍待验证。
- 用户当前不方便试玩，0.1.12-dev 的手动潜水 Probe、路线和返航验证已延后；保留主菜单启动通过，不催测、不把延后算作玩法通过。
- 0.1.15-dev新增Core/Cargo纯CLR个人袋账本与默认关闭的Observe loot and return calls。账本不接NetworkController/scene清理或协议，需房主核实完整产物和新鲜操作/来源事实；CLR标记不授权原生分流/奖品/入仓。房主重量取总值不重复累加，员工容量独立；未知已进入操作不重新执行，员工返航逐产物派发/入仓/保存。四处Loot前后只读观察在Unity线程即时冻结、仅CLR排队，1024进程事件/队列64/context128/每Update16，停止可截断链且需计数。默认游戏能力全部false，原生ABI/完整产物/分流/返航/保存待实机，详见CREW_MODE及cargo-ledger-build-verification.json。
- Transmit 开启时 MapRouteObservation 独立在入海前后最多 1Hz 读取 cache/roadmap/first、候选 bSelected 层与加载场景，MAP_ROUTE_INPUTS 仅值变化记录。cache 缺失/roadmap 缺失/first 缺失/cache 太短分别报不可用并撤销旧稳定候选；候选层不是完整清单，未调用选图、加载或保存写入。
- 0.1.13-dev 默认关闭的 Observe map selection calls 独立于 TCP/Transmit，观察路线 cache/restore、IGP 原 __result、Prefab IEnumerator 工厂和 SceneLoader.LoadSceneAsync prefix 五处自然边界。当次回调只在确认 Unity 线程冻结有界 CLR，非 main 跳过 native 读取；全进程 1024、队列 64，空/截断/读取错误明确记录，不保留 native wrapper。Disconnect 关闭并卸载自己的 Observer。
- MapRouteSelection 的 ValidateRoute/CopyRoute/FingerprintRoute 严格校验并复制路线；RouteFingerprint 不是完整 manifest，缺 IGP 不升格完整。IGP factory 不证明真实请求/完成，未证明所有选择在所有加载前已完成；两处 IsInitDone 改读直接 backing field，不调用原 getter。0.1.13 历史观察构建见 `logs/map-selection-call-build-verification.json`，未实机。
- 0.1.14-dev 的 MapRouteSlice/MapIgpChoice/MapChoiceRetire 仅传输候选：host only publish、guest only receive，握手 Room 绑定；路线每片 8 场景/最多 4 片，独立 map FIFO 32 包，动作/角色/世界/地图四路公平，控制/心跳与 retire 优先。generation/revision 独立 scene epoch，WaitingForScene 可传输但不提高 Ready 或权限。
- 新 generation 首片撤旧路线，完整路线才原子提交，再接连续 revision 的 IGP；同组按新修订更新。普通场景/帧清理保留 preload 候选，显式 Retire 清理本代次、保留高水位，Close 清所有 map/source/mailbox。队列满主动控制撤销后返回 false；合法旧/已退休选择取消不关房，未来/当前冲突或伪造 fail closed。
- 0.1.14历史MapChoiceController的cache/restore 同指纹也创建新 generation，SceneLoader 同指纹去重；copy 错误/截断/丢失主动撤销，未绑定 IGP 丢弃且不缓存；已发布组再次空/unknown 撤销候选，未知新组空仍 Unbound。callbackFloor 仅排除已排队旧观察，DTO 无原生 context/controller 代次证明；新 cache 后迟到且同 scene/address 的旧 IGP 仍可附当前候选。日志 NativeGenerationBound=false，Snapshot 一律 ObservationOnly=true/HostSelectionApplied=false；补本地 origin/代次及跨机地址证据后才可采用。
- Test-Core 与测试 csproj 编译实际 MapChoiceController/MapSelectionCallObservation，仅替代 logger；4 项源适配夹具用 synthetic DTO 与实际回环 TCP，不运行 NativeHook、不调用游戏入口，不算原生或双游戏验收。
- 0.1.16-dev新增默认关闭ObserveMapOrigins与29声明的自己的前后/finalizer挂钩，详见docs/MAP_ORIGINS.md。
  entry/factory/每MoveNext固定owner；精确Addressables五参typed原返回关联operation指针/版本、成功Scene句柄及controller出生。
  主线程保留operation wrapper最多64并读直接字段，CLR队列64/消费16；8192进程事件/context256及Core有界tombstone，不把当前singleton或名称当owner。
  新entry/重复cache/Context清理/unload/destroy撤销，真实未知unload也留围栏；异常/丢失/线程/配额撤证，失败重启。
  RunId隔离重开后的life编号；0.1.16历史版本仅日志，0.1.17另接当前候选但不授权限，ScalarOriginChainMatched也不证明原生ABI/完整来源/跨机地址。
  NativeGenerationBound/HostSelectionApplied/WorldAuthority/CargoAuthority均false；148项含6组synthetic registry夹具，未运行NativeHooks。
  当前源码编译通过，新版未部署/启动，最新实机范围仍0.1.12启动/0.1.11潜水。核对实际嵌套/typedreturn/Scene值/__state/owncleanup后才接采用。
- 下一步实现实际加载前房主选择采用、客机临时进度/生成与 AI 隔离，再接房主原生捕鱼及返航收益账本；不要把 CLR TCP、布局指纹或单游戏显示当 M4/M5/M6 或双游戏完成。
- 原GameAssembly离线研究用`scripts/Inspect-NativeCalls.ps1`，先读`docs/NATIVE_ANALYSIS.md`；报告/机器码/游戏和依赖DLL只留.local。
  按精确metadata方法指针及version1 chained unwind三元组分析，静态边/别名/完整已知片段不证明运行顺序、数据流或能力。
  已发现coLoadAdditiveScene/CoLoadSceneAsync直接走Addressables五参入口，现SceneLoader三参观察不全；固定iterator owner和子协程继承后再接operation/Scene/controller寿命。
  鱼产物还触及水下进度、容量与多类返航库存，guest加载/SetLoadedData有副作用；不据单Add/最终Save开员工权限。
  该离线工具轮仅工具/文档，历史0.1.15插件/142项证据不变，摘要见`logs/native-call-analysis-verification.json`。
- 加载后的路线/IGP 清单要求每个选中场景至少一组、查找结果与原注册列表一致，并在两个不同 Unity 帧稳定。跨机地址未验证，尚未在加载前采用房主选择；M4 接管、M5 裁定及 M6 收益未实现。构建范围见 `logs/fish-world-interaction-build-verification.json`。
- 0.1.11-dev 的 A03_01_02 本机 TCP 已记录 49 条 Ready 概要、53 条 FishWorld 状态，观察/绑定/可显示/可见最大 16、网格顶点 662，未知资源/缺 Visual/显示错误为零。8 个交互挂钩健康，42 条事件组成 21 对 CallId，覆盖 HarpoonFire、FishHookedByProjectile、FishDamage 和 SpecialDamage，两个原 bool 为 true；Win/Pickup 未见。回调/解析/未配对/查询错误为零，地图读取失败 Selected route incomplete。Local test 的成对鱼是原鱼加偏移诊断副本，同步消失不等于捕获副本。自己的挂钩卸载与 Disconnect 有标记；用户确认主动退出且未返航，正常返航保存未验证。
- 每次改动记录日期、目的、修改文件、执行的验证、结果、遗留问题与下一步。
- 路径通过 Steam 注册表和库清单定位，用户提供路径时尊重该路径，不硬编码本机 F 盘。
- 更新插件前保存并正常退出游戏，脚本不终止用户游戏进程。
- 不将任何游戏数据、互操作程序集、框架二进制、账户信息或机器运行日志提交到 Git。
- 修改 C# 后运行 `scripts/Build-Plugin.ps1`；修改安装代码后验证参数检查、校验失败路径和幂等安装。
- 修改纯 CLR 姿态、协议或传输层后运行 `scripts/Test-Core.ps1`；回环传输通过不等于真实双游戏联机通过。
- 原型成功条件是新启动进程的日志出现加载、Update 和场景标记；文件存在不能证明运行成功。
- 发行包只包含自写 DLL 和 manifest，更新 `config/dependencies.json` 后重新打包及校验。

- 0.1.17当前来源清单见docs/ORIGIN_MAP_TRANSPORT.md。TryCaptureSource owned复制且不消费诊断队列；4参BindRoom固定实际Run/owner floor，建房前entry不得晚补路线绕过。64个Run围栏跨Clear保留，256 controller历史不淘汰；删除/替换重建wire代次，每帧8条、FIFO32、schema128超限整帧拒绝。旧Observe只诊断，Guest停本地origin不撤Host候选。160项含新增6组快照和6组originTCP，不运行Unity/native；全部原生权限false。
- 继续客机影子桥先读docs/GUEST_ISOLATION.md，Inspect-GuestStateApi.ps1只读wrapper/IL，报告只留.local。真实Serialize/Deserialize为clone候选，string ver不是JSON构造器，SetLoadedData/Load不是纯恢复。直接根交换不消除旧缓存/协程引用；全部输出、Interaction/Photo/UserOption隔离与恢复需另证，静态边或178签名候选不开放GuestStateIsolated。

- 0.1.18历史根桥/事务/已枚举fence见docs/GUEST_SHADOW_BRIDGE.md与GUEST_OUTPUT_FENCE.md。NativeGuestShadowBridge使用四Data native Serialize/Deserialize、temp Interaction(false)、五根direct读写/readback与15独立IntPtr强handles；不调用SetLoadedData/Load/Sync，不复制原cached指针作shadow。
- 进入/静止边界当前恒false，Core先检查再装fence，startup primitive也重查；没有Network/GUI接线。不得为了试玩直接改true、伪造caller flags或只用Room/loaded/writer0。全原生权限false；当前代码不运行clone/field mutation/fence安装，真实旧引用/深树/Interaction/writer覆盖待接。
- GuestOutputTargetManifest冻结194精确声明（130declared除22open-base/2service-interface再加88closed），8typed out设null/false，Injected输入ref保持；Steam stream invalid=MaxValue、async=0，Toolbox Save/Delete失败2/1，不以default成功。只卸自己owner，unknown/其它线程/partial安装失健康仍阻断；194不是唯一原生地址或完整覆盖。
- 0.1.18的167项含7组instrumented synthetic backend，用write前/后错误与未知恢复验证一次补偿/释放；不执行nativebackend/hooks。强managed backend在首次真实fence attempt时保留，precheck拒绝不占global slot。先原根/manager/readback＋真实静止边界确认再卸fence/free，未知不重试。JSON限额是UTF16代码单元，native返回后检查，不约束内部初始分配。

- 当前0.1.19继续先读docs/GUEST_INTERACTION_SHADOW.md、GUEST_ENTRY_BOUNDARIES.md与GUEST_RUNTIME_CACHES.md。typed十组绑定+new IGP hash已接PrepareDetached，single-use/source/thread窗口核对不变，禁止把Invoke Init/Load/Build当纯clone。
- 原10dirty拒绝、已知baseline逐值/版本及mutable graph审计；4096每侧含重复计数，两侧8192，0长数组不含可变元素可共享，非空数组/7record/嵌套货槽另查。KnownReferencesDisjoint只针对已读图；comparer/cache/其它Player字段与native array/Entry ABI等仍未知，SourceBaseline/CompleteGraph及全部权限false。
- 新增Il2CppSystem.Core build引用供native HashSet，未复制依赖DLL。170项只新增3组实际CLR引用审计，不执行nativehelper；新两个Cecil工具及Depth1 cache native分析只留.local原报告。不自动部署或催测试，完整实际边界/缓存/输出恢复及两袋玩法仍必须完成。
