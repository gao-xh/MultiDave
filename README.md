# MultiDave

当前源码0.1.44-dev（协议9），本轮实际Core/TCP347/347及插件Build警告视为错误通过，执行输入前后相同。新增[员工输入与房主移动接线](development/docs/CREW_ACTOR.md)：默认关闭、双方握手显式选择；房主独立物理身体接受输入并回读位置/速度，客机临时角色按房主状态校正。HP/O2是独立Mod规则，氧气为零仅禁止boost；真实伤害、装备、武器、命中和账本负重尚未接入。见[本轮记录](development/logs/crew-actor-build-verification.json)。原生身体创建、碰撞、校正、生存、ABI与双游戏尚未执行或验证，Guest完整隔离/World/Cargo权限仍false。未部署/启动，安装.12/最近潜水.11/默认包.0保持。完整M3—M7、每人独立袋/容量/重量/负重、完整产物前置分流、逐项返航保存与GitHub冷配置仍待完成。

《潜水员戴夫》Windows Steam 版的联机 Mod 开发项目。

**默认安装包为 0.1.0 加载原型，只验证插件加载、场景读取和状态面板。
开发分支正在实现联机功能，完整双人捕鱼与返航闭环尚未通过验收。**

## 把链接交给 Codex 配置

将以下内容发送给能够访问你本机文件和运行终端的 Codex：

> 请配置 https://github.com/gao-xh/MultiDave 。先克隆仓库，读取 AGENTS.md
> 和 .agents/skills/dave-coop-setup/SKILL.md，自动定位 Steam 游戏，运行
> setup.ps1，启动到主菜单并检查真实加载日志。遇到版本不兼容或游戏正在运行时，
> 明确说明需要处理的步骤，不要强制关闭游戏。请记录配置结果。

Codex 可以完成游戏定位、依赖下载与校验、插件安装、启动验证和日志记录。
必要的文件写入或网络访问仍受你本机 Codex 的权限设置控制。

```powershell
# 仓库根目录：只检查环境。
.\setup.ps1 -InspectOnly

# 保存并退出游戏后，安装仓库附带的原型包并启动验证。
.\setup.ps1 -LaunchGame
```

玩家安装不需要 .NET SDK。首次启动框架需要联网下载 Unity 依赖并生成接口，
可能耗时数分钟。安装后左上角出现 `DaveCoop Prototype`，F8 切换面板。

## 已验证版本

- Windows x64，Steam App ID `1868140`
- 游戏 Steam Build ID `25315876`，Unity `6000.0.52f1`
- BepInEx `6.0.0-be.788+5b766a3`

安装器会拒绝尚未验证的游戏版本或已有的其他框架版本，避免自动覆盖现有环境。
当前原型会显示尚未实现网络的状态。

## 开发与接手

- [开发目录与编译说明](development/README.md)
- [开发日志与现有证据](development/logs/DEVLOG.md)
- [阶段记录与下一步](development/docs/HANDOFF.md)
- [分阶段开发计划](development/docs/PLAN.md)
- [玩家与摄像机发现](development/docs/GAME_API.md)
- [第二角色与传输层](development/docs/MULTIPLAYER.md)
- [同一海洋、鱼与互动](development/docs/WORLD_SYNC.md)
- [房主与员工、独立背包方案](development/docs/CREW_MODE.md)
- [Codex 配置技能](.agents/skills/dave-coop-setup/SKILL.md)

源代码和重复使用的配置工具位于 `development/`；
`distribution/` 是供自动安装使用的本项目插件包。
本仓库不分发游戏、游戏接口程序集、存档或 BepInEx 运行库。

`codex/player-discovery` 分支0.1.22 历史源码为 0.1.22-dev、协议 5；插件 Build 警告视为错误通过，未部署或启动。0.1.22 该轮 Core 输入未改，复用 0.1.21 实际通过的 176/176 结果，没有重新运行这批测试。
当前安装及最近新鲜启动为 0.1.12-dev/109 项测试；该进程已确认插件加载、Unity Update、网络入口及 4 条初始路线输入日志，仅主菜单启动通过。
目标检查、潜水路线、场景切换与正常返航仍待实机，用户当前不方便试玩，手动验证已延后。最近完成潜水验证的是 0.1.11-dev，单游戏 TCP 鱼群显示与发射/挂钩/伤害只读观察已运行。
用户确认偏移鱼群可见、捕获原鱼时对应副本也消失，关闭鱼群显示后恢复正常，操作和镜头正常。
动画、完整捕获链、统一地图和正常返航仍待验收。
最近的单鱼稳定性实测来自 0.1.9-dev；玩家探针与第二角色回放基础验收通过。
实际潜水已确认鱼探针、本机 TCP 收发、显示组件和生命周期回调运行，
0.1.7-dev 用户已确认预览鱼可见，但镜头内突然消失；日志记录临时角色部件销毁导致自动断开。
0.1.8-dev 无旧断线异常，但用户仍报告标签换鱼及消失；0.1.9-dev 锁定已选身份并记录隐藏原因，已部署且用户确认不再突然消失。
0.1.10-dev 准备房主鱼编号反向查询；0.1.11-dev 增加完整收到的活动鱼观察清单显示、
8 个原生交互入口的只读前后成对观察、冻结的本地身份查询和加载后的路线/IGP 清单读取。
鱼清单只覆盖玩家当前场景；每鱼保留 16 帧，镜头与暂缺显示资源不改变数字身份。
F11 新增默认关闭的 Display received fish roster 和 Observe host harpoon and fish interactions；
本机测试仍须开启 Transmit read-only fish observations。标签和鱼群显示均不可捕获，原生 AI、碰撞与收益未接管。
交互 bool 只记录原方法返回；HpAtDrain 是消费事件时的读数，不能据此认定命中或捕获结果。
0.1.11-dev 地图清单读取停在 Selected route incomplete；加载后清单不能代替加载前采用房主地图，跨机地址稳定性尚未验证。
该历史实机的 8 个观察入口健康，42 条事件组成 21 对调用，覆盖发射、挂钩和两种伤害声明，两个原 bool 返回为 true；未见 QTE 胜利或入袋观察。
本机 Local test 保留原鱼并偏移显示副本，所以会出现成对鱼；同时消失是观察清单移除同步，不是捕获副本或统一世界完成。
动画、完整捕获链、正常返航、真正合作捕获及双游戏验收仍待完成；用户确认主动退出且未返航，正常返航保存仍未验证。
0.1.12-dev 的 F11 新增 Check selected fish target：Guest 或 Local test 发送目标检查请求，
房主主线程重新查找原生鱼与代次；通过只返回 DryRunValidated，OperationId 为 0，不发射鱼叉、不扣血或捕获。
请求/结果使用独立 FIFO、来源与指纹核对、请求高水位和缓存去重；合法场景切换期间的旧发布在会话锁内返回 false，保留连接，GUI 发送异常有捕获。
真实游戏意图缺少可信玩家/装备、地图接管、客机隔离与原生执行桥，保持拒绝执行。
Transmit 开启后独立在入海前后读取路线输入，每秒最多一次、值变化才记录；候选选中层不是完整地图选择。
三种本机 TCP 操作夹具（往返、旧协议拒绝、取出后场景切换恢复）通过不等于两游戏联机或合作捕鱼通过。
0.1.13-dev 新增默认关闭的 Observe map selection calls，独立于 TCP/Transmit，在原游戏 5 处自然调用中即时冻结有界 CLR 路线/IGP/加载参数。
RouteFingerprint 只代表路线候选；IGP 枚举器工厂不证明加载请求或完成，未证明所有选择均先于所有加载，也未共享或采用房主地图。
Mod 不持有原生包装器、不调用或改写选图/加载/存档入口；Disconnect 关闭并卸载自己的观察挂钩。新观察仅通过构建，尚无实机回调证据。
0.1.14-dev 将房主观察到的路线和 IGP 选择候选通过独立通道发送给客机：路线每片最多 8 场景、最多 4 片，地图 FIFO 最多 32 包；动作、角色、世界和地图四路公平发送，控制/心跳与撤销优先。
地图 generation/revision 独立于场景 epoch，可在等待场景时传输；新 generation 首片撤销旧路线，整批完成才原子提供路线。普通场景切换保留候选，显式撤销或关房清理，队列溢出主动撤销。
0.1.14历史适配依靠cache/restore与callbackFloor，无法排除新cache后同地址旧控制器的迟到选择；0.1.17已替换为固定来源清单，旧Observe map selection calls只诊断。当前原生ABI与跨机地址仍未验证，NativeGenerationBound=false，全部快照为证据，HostSelectionApplied=false。
下一步需验证原生来源代次、跨机地址和实际加载前选择采用，再实现客机临时进度及原生生成/AI 隔离。当前地图传输不等于统一海洋、双游戏或合作捕鱼验收。

0.1.15-dev开始实现每人独立背包的内存账本：容量/重量按人记账、捕获来源去重、未知结果保留，返航按产物分别确认。
新增默认关闭的F11 Loot/返航只读观察，帮助核对原游戏链；账本未接原生员工分流、入仓或网络清单，独立背包玩法仍待接通。
范围见[员工模式方案](development/docs/CREW_MODE.md)及[独立背包构建摘要](development/logs/cargo-ledger-build-verification.json)。
0.1.16-dev另增默认关闭的加载来源观察：固定入海/协程身份，关联精确资源操作、实际Scene句柄及controller出生，防止旧调用归到新地图。
这是来源证据准备，仍未采用房主地图或接通个人背包玩法；原生回调及画面未验证。
实现与限额见[地图加载来源](development/docs/MAP_ORIGINS.md)，0.1.16历史编译/148项测试范围见[加载来源构建摘要](development/logs/map-origin-build-verification.json)。
测试直接编译实际 MapChoiceController 与 MapSelectionCallObservation，仅替代 logger；4 项源适配用例用 synthetic DTO 和实际回环 TCP 验证候选，未运行 NativeHook。
0.1.14历史范围见 [地图选择传输构建摘要](development/logs/map-choice-transport-build-verification.json)；0.1.13 历史构建见 [地图选择调用摘要](development/logs/map-selection-call-build-verification.json)；已安装版本见 [0.1.12-dev 操作门禁摘要](development/logs/fish-action-gate-build-verification.json)，历史鱼群与交互证据见 [0.1.11-dev 摘要](development/logs/fish-world-interaction-build-verification.json)。
同一海洋、鱼与互动的实现范围见 [WORLD_SYNC](development/docs/WORLD_SYNC.md)。
开发时使用编译和部署脚本；默认玩家安装包保持 0.1.0。

## 暂时停用

保存并退出游戏后，将游戏目录的 `winhttp.dll` 改名为
`winhttp.dll.disabled` 可停用框架；改回原名即可恢复。
只停用 MultiDave 时，将 `BepInEx/plugins/DaveCoop/DaveCoop.dll`
移到 `plugins` 目录之外。

0.1.17 已接固定来源清单到候选发送，旧地图调用观察只作诊断；每人的独立容量和负重保持不变。当前范围见[固定来源候选传输](development/docs/ORIGIN_MAP_TRANSPORT.md)和[160项构建摘要](development/logs/origin-map-transport-build-verification.json)。客机克隆/根恢复接口见[影子桥研究](development/docs/GUEST_ISOLATION.md)，实际地图采用、员工捕获/入仓及客机隔离仍待完成。0.1.14旧适配与0.1.16仅日志均为历史行为。

0.1.18新增客机原生状态复制/五根恢复和已枚举输出围栏源码，167项测试通过，见[根桥](development/docs/GUEST_SHADOW_BRIDGE.md)与[当前摘要](development/logs/guest-shadow-build-verification.json)。真实进入边界尚未接通，源码会在安装围栏前拒绝进入；没有自动调用、部署或试玩验证，不开放员工或存档隔离权限。

0.1.19版进一步实现临时玩家交互缓存准备与共享引用检查，编译和170项测试通过。见[交互缓存](development/docs/GUEST_INTERACTION_SHADOW.md)与[该版构建摘要](development/logs/guest-interaction-build-verification.json)。原生进入仍关闭；未部署或验证实际员工捕获和返航入仓。

0.1.20 继续实现[客机食材缓存](development/docs/GUEST_INGREDIENT_CACHE.md)：独立条目、数量数组和资源实例字段副本，并接入六步准备/恢复源码。编译及174项测试通过，见[该版摘要](development/logs/guest-ingredient-cache-build-verification.json)。未部署或执行原生切换；完整缓存隔离、个人捕获和返航仍待接通。

0.1.21 新增[游戏内临时保存缓存](development/docs/GUEST_INGAME_CACHE.md)第七步，接口见[精确 API](development/docs/GUEST_INGAME_API.md)。六种记录具有有限字段复制合同；非空助手资源和正在使用的设备队列明确拒绝，不能清空或共享原状态代替复制。三份已知原图在 Serialize 前捕获并再次核对，准备结束再严格复查；恢复顺序为游戏内缓存→食材缓存→五个保存根。显式强 handle 上限为21，四份 Data 标量检查保留。

176项测试通过仅证明CLR控制和此前TCP范围；[该版记录](development/logs/guest-ingame-cache-build-verification.json)的插件构建警告视为错误通过。原生类型/分配和七步切换未执行，进入/静止及客机、世界、背包权限均false。每人独立容量和负重保持；完整M3—M7仍需资源/角色/余下缓存/全输出隔离、房主地图采用、个人捕获与返航、真实双端及冷配置验收。

0.1.22 补充[独立字典 comparer 候选](development/docs/GUEST_DICTIONARY_COMPARERS.md)与[精确接口](development/docs/GUEST_COMPARER_API.md)：对 int、string 和 int32 底型 InGameSaveType，只研究已核对 exact class 的 Generic/Object family，以及该 enum 的专用 Enum family；原 comparer 的指针、class、kind 和辅助引用纳入检查。食材缓存使用相同规则；原 null 可捕获但拒绝准备，不猜 Default、不共享或清空原 comparer。新 dictionary 显式 `(capacity, comparer)` 后才 Add；未知自定义、文化或 hash-salt 语义拒绝。

普通 constructor 抛出时 helper 赋值尚未完成，`PartialConstructorAllocationRetentionVerified=false`，不能声称所有未知分配都已持有。[0.1.22 历史摘要](development/logs/guest-comparer-build-verification.json)的插件 Build 警告视为错误通过，Core 复用前轮 176 项结果。七步、21 explicit handles 和四 Data 检查未扩；原生 ABI、完整隔离、进入/静止、世界和背包权限仍 false，无 GUI/Network 自动入口。[冷档方案](development/docs/GUEST_COLD_PROFILE.md)仅研究首次加载、slot 与输出路径，尚未采用；完整双端与每人独立捕获、容量分流及逐产物返航目标保持。
