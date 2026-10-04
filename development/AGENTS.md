# 开发约定

当前源码0.1.44-dev（协议9），本轮实际Core/TCP347/347及插件Build警告视为错误通过，执行输入前后相同。新增[员工输入与房主移动接线](docs/CREW_ACTOR.md)：默认关闭、双方握手显式选择；房主独立物理身体接受输入并回读位置/速度，客机临时角色按房主状态校正。HP/O2是独立Mod规则，氧气为零仅禁止boost；真实伤害、装备、武器、命中和账本负重尚未接入。见[本轮记录](logs/crew-actor-build-verification.json)。原生身体创建、碰撞、校正、生存、ABI与双游戏尚未执行或验证，Guest完整隔离/World/Cargo权限仍false。未部署/启动，安装.12/最近潜水.11/默认包.0保持。完整M3—M7、每人独立袋/容量/重量/负重、完整产物前置分流、逐项返航保存与GitHub冷配置仍待完成。

- M1 玩家发现与 M2 回放基础验收通过；0.1.22 历史源码为 0.1.22-dev、协议 5，插件 Build 警告视为错误通过，范围见 [comparer 摘要](logs/guest-comparer-build-verification.json)；0.1.22 该轮 Core 输入未改，复用 [0.1.21 实际 176/176 结果](logs/guest-ingame-cache-build-verification.json)，没有重跑测试。未部署/启动，默认发行包仍为 0.1.0。当前安装及最近新鲜启动仍为 0.1.12-dev/109 项测试，仅主菜单加载/Update/网络入口和 4 条初始 RouteInputs 通过。新观察回调、Probe、潜水路线、场景切换与正常返航仍待实机。最近完成潜水验证的是 0.1.11-dev，用户确认偏移鱼群可见、捕获原鱼时副本同步消失、关闭显示后恢复正常，操作和镜头正常；动画、完整捕获链、地图及正常返航/双游戏验收仍待完成。历史证据保留，0.1.9-dev 用户确认锁定身份后不再突然消失。完成情况以 `logs/DEVLOG.md` 和真实运行证据为准。
- 继续工作前阅读 `docs/HANDOFF.md`、`docs/PLAN.md` 和当前阶段的 `docs/GAME_API.md` / `docs/MULTIPLAYER.md` / `docs/WORLD_SYNC.md`；配置别人电脑时使用仓库根目录的配置 Skill。
- 用户确定首版房主＋员工且每人独立背包，继续捕获/库存/结算开发先读 `docs/CREW_MODE.md`。房主自己的原生LootBox，员工由房主Mod持有的独立会话袋，各自容量/重量/负重；产物与前置容量检查都需正确分流，不能先入房主袋再复制。房主唯一长期进度；返航房主袋原链不重复Add、员工未入仓物料需新桥逐项确认一次。员工断线不清潜水账本，未知原生结果不重试/补奖；隔离客机全部自动持久写。同层带队、独立员工生存/装备/投射物及上述袋/结算仍待实现。
- 0.1.11-dev 含房主目标反向查询、冻结的本地指针/代次 CLR 快照、完整收到的活动观察鱼群显示，以及 8 个原生交互入口的只读前后成对观察。鱼清单仅玩家当前场景，每鱼最多 16 帧；缺显示或离镜头不释放数字身份，原生 AI/碰撞/收益保持原样。
- F11 的 Display received fish roster / Observe host harpoon and fish interactions 默认关闭，房主观察仍须启用 Transmit read-only fish observations。交互在 prefix 固定绑定，postfix 复用；bool 只是原返回，HpAtDrain 只是主线程消费时读数，不代表捕获结果或授权。只卸载自己的挂钩。
- 0.1.12-dev 新增独立操作请求/结果 FIFO、握手绑定来源、规范指纹和 guest outstanding 核对。Gate 保留房间内 RequestId 高水位，业务拒绝也消费新 ID，场景失效/缓存淘汰不允许重放；未知原生结果不重派发。6 种动作具有 schema/Gate，实际 effects 的地图权限、客机隔离、本地竞争裁定、可信玩家/装备及原生桥仍为 false，不能声称可捕鱼。
- F11 的 Check selected fish target 仅发送 ProbeTarget；房主主线程重新 native 查目标/代次，通过为 DryRunValidated、OperationId=0，不执行鱼叉/伤害/捕获。新鲜权限事实及短租约只构成候选计划，派发前还须复核。当前摘要为 `logs/fish-action-gate-build-verification.json`，本机 TCP 夹具不等于双游戏或 M5 验收。
- 合法 pause/场景切换期间旧请求或结果的发布在会话锁内返回 false，不错误关房；GUI 发布异常捕获并显示状态。目标读取前后复核 lifecycle 健康与代次，失败不保留旧身份；同 epoch 的 Transmit 开关不归零 world revision。三种 TCP 操作夹具已覆盖往返、旧协议拒绝与 take 后场景切换恢复，实际游戏切换仍待验证。
- 用户当前不方便试玩，0.1.12-dev 的手动潜水 Probe、路线和返航验证已延后；保留主菜单启动通过，不催测、不把延后算作玩法通过。
- 0.1.15-dev新增Core/Cargo纯CLR个人袋账本与默认关闭的Observe loot and return calls。0.1.15历史账本尚不接网络；0.1.24增加只读投影通道，仍不接原生潜水生命周期，需房主核实完整产物和新鲜操作/来源事实；CLR标记不授权原生分流/奖品/入仓。房主重量取总值不重复累加，员工容量独立；未知已进入操作不重新执行，员工返航逐产物派发/入仓/保存。0.1.15历史四处Loot观察在0.1.25扩至16默认关闭入口，prefix固定来源候选，postfix保留原结果、void finalizer退出同步范围；512队列/128context/32depth/8192进程事件，每Update16。重入/线程/队列/配额/原异常立即撤证，未知卸钩不重试；详见CAPTURE_LINEAGE。默认游戏能力全部false，原生ABI/完整产物/分流/返航/保存待实机，详见CREW_MODE及cargo-ledger-build-verification.json。
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
  0.1.16历史源码编译通过，新版未部署/启动，最新实机范围仍0.1.12启动/0.1.11潜水。核对实际嵌套/typedreturn/Scene值/__state/owncleanup后才接采用。
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
- 0.1.18历史进入/静止边界恒false，默认ExistingCaches七根至今硬拒；不得为了试玩直接改true、伪造caller flags或只用Room/loaded/writer0。0.1.37默认关闭的Natural五根source已实际接Plugin/Guest房间/固定首次初始化窗口，见docs/GUEST_INITIALIZATION_BOOTSTRAP.md；只是源码接通，未执行native。quiet/全部原生权限仍false，Disconnect不Restore/unpatch/free，真实旧引用/深树/Interaction/cache/writer覆盖待证。
- GuestOutputTargetManifest冻结194精确声明（130declared除22open-base/2service-interface再加88closed），8typed out设null/false，Injected输入ref保持；Steam stream invalid=MaxValue、async=0，Toolbox Save/Delete失败2/1，不以default成功。只卸自己owner，unknown/其它线程/partial安装失健康仍阻断；194不是唯一原生地址或完整覆盖。
- 0.1.18的167项含7组instrumented synthetic backend，用write前/后错误与未知恢复验证一次补偿/释放；不执行nativebackend/hooks。强managed backend在首次真实fence attempt时保留，precheck拒绝不占global slot。先原根/manager/readback＋真实静止边界确认再卸fence/free，未知不重试。JSON限额是UTF16代码单元，native返回后检查，不约束内部初始分配。

- 0.1.19版继续先读docs/GUEST_INTERACTION_SHADOW.md、GUEST_ENTRY_BOUNDARIES.md与GUEST_RUNTIME_CACHES.md。typed十组绑定+new IGP hash已接PrepareDetached，single-use/source/thread窗口核对不变，禁止把Invoke Init/Load/Build当纯clone。
- 原10dirty拒绝、已知baseline逐值/版本及mutable graph审计；4096每侧含重复计数，两侧8192，0长数组不含可变元素可共享，非空数组/7record/嵌套货槽另查。KnownReferencesDisjoint只针对已读图；comparer/cache/其它Player字段与native array/Entry ABI等仍未知，SourceBaseline/CompleteGraph及全部权限false。
- 新增Il2CppSystem.Core build引用供native HashSet，未复制依赖DLL。170项只新增3组实际CLR引用审计，不执行nativehelper；新两个Cecil工具及Depth1 cache native分析只留.local原报告。不自动部署或催测试，完整实际边界/缓存/输出恢复及两袋玩法仍必须完成。

- 0.1.20历史范围先读docs/GUEST_INGREDIENT_CACHE.md和GUEST_INGREDIENT_API.md；缓存singleton只读实际SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField，不用不存在的_instance或runtime getter补实例。Capture两份原known baseline均先于serializer。
- GuestShadowTransaction新增第六IngredientsCache与仅此step合法OwnedMixed；五SaveRoots准备窗口与六步最终核对分开，cacheguard不能递归自身graph。最多18explicit strong handles，DataStamps仍4；全部原/native资源与玩法权限false，无Network/GUI调用。
- 174项包含4组双字段CLR cache夹具，不执行nativehelper；相同loaded按storage身份区分，原storage null拒绝Prepare，unknown/foreign不盲写或retry。Entity仅复制13已知instance fields，Parent/static及任务/其它cache仍未知，不调用Init/Load/Reset修复。

- 0.1.21历史范围先读docs/GUEST_INGAME_API.md与GUEST_INGAME_CACHE.md，第七IngameCache用实际SingletonNoMono backing及ingameSaveDatas。六kind schema只覆盖支持的known childgraph；未知class/keytype、非空SubHelperSpecData或live gearQueue拒绝，不改空、不共享原资源替代。
- 三known baselines（Interaction/Ingredients/Ingame）在任何Serialize前捕获并闭合核对，全部Prepare完成再strict复查。五SaveRoots窗口与七步最终核对分开，轻量cache read guard不递归自身图；逆序恢复7→6→Save5，第七单field不允许OwnedMixed，只有第六两field合同可用Mixed。
- 最多21explicit strong handles，4DataStamps保留；object_get_class exact比较与object_new+IntPtr普通record准备均未运行，class store初始化也是原生候选。0.1.21的176项测试只覆盖CLR控制/此前TCP，该版插件Build警告视为错误通过；进入/静止及所有native/guest/world/bag权限false，无自动GUI/Network入口。完整资源/角色/缓存/输出、房主地图采用、个人产物分流和返航、M3—M7/实际双端/冷配置仍必需。

- 历史0.1.22继续先读docs/GUEST_DICTIONARY_COMPARERS.md与GUEST_COMPARER_API.md；三key int/string/InGameSaveType(int32)只接受已核exact Generic/Object与该enum专用Enum的独立同class候选。source pointer/class/kind及aux审计必须保留，Ingredients同规则；null原可Capture但Prepare拒绝，不以Default/CreateComparer/getter、共享或清空补状态，custom/文化/hash-salt未知拒绝。
- 新dictionary显式(capacity,comparer)先于Add；普通constructor抛时assignment未完成，PartialConstructorAllocationRetentionVerified=false，不声称所有未知allocation已Hold。七步/21explicit handles/4Data stamps不扩，ABI/fullisolation/entry/quiet/native/guest/world/bag全false，无GUI/Network自动入口。0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176而未重跑；插件Build警告视为错误通过，记录见logs/guest-comparer-build-verification.json。
- docs/GUEST_COLD_PROFILE.md保留独立首load path/slot/output研究；0.1.37采用existing-save自然加载后、缓存初始化前的五根源码接线，不改目录/槽位/云设置，不证明首次读取隔离。继续完整资源/actor/cache/output与真实边界、房主地图采用、每人独立袋/容量/负重下的捕获分流及逐产物返航、实际双端和GitHub冷配置，不缩减M3—M7。
