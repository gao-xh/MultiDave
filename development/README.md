# 潜水员戴夫联机原型

当前源码0.1.42-dev（协议8），本轮实际Core/TCP323/323及插件Build警告视为错误通过，执行输入前后相同。新增[房主双成员鱼区域](docs/HOST_FISH_INTEREST.md)：真实接收来源绑定到默认关闭的普通allocator距离与原LOD结果消费者，保原生成/生命周期、独立计算两区域后合并。见[本轮记录](logs/host-fish-interest-build-verification.json)。原生ABI、普通与group鱼完整覆盖、远处避让及双游戏未验证；不能称远距离探索完成。未部署/启动，安装.12/最近潜水.11/默认包.0保持。完整M3—M7、Guest隔离、可信员工命中、每人独立袋/容量/负重、逐项返航保存与GitHub冷配置仍待完成。

当前进度：M1 已通过潜水、场景切换与返航读取验证，M2 回放基础验收通过。
0.1.22 历史源码为 0.1.22-dev、协议 5，插件 Build 警告视为错误通过；未部署或启动新版本。0.1.22 该轮 Core 输入未改，复用 0.1.21 实际通过的 176/176 结果，没有重跑测试。
当前安装仍为 0.1.12-dev，仅主菜单加载/Update/网络入口及初始路线输入通过；最近完成潜水验证的是 0.1.11-dev。
用户目前不方便试玩，Probe、自然地图回调、路线/场景切换及正常返航验证延后。
0.1.5/0.1.6-dev 已在真实潜水确认鱼探针、本机 TCP 收发、显示组件及生命周期回调运行。
0.1.7-dev 用户确认预览鱼可见，但日志记录角色部件销毁导致自动断开，画面稳定性不通过。
0.1.8-dev 没有旧异常但仍出现自动换鱼；0.1.9-dev 已锁定身份并记录隐藏原因、加入手动重选，已部署，用户确认不再突然消失。
预览鱼目前不能命中/捕获，已确认不再突然消失；动画、池重用/清理及双游戏验收待完成。
完整计划见 [PLAN](docs/PLAN.md)，移动实现见 [MULTIPLAYER](docs/MULTIPLAYER.md)，
地图、鱼与互动方案见 [WORLD_SYNC](docs/WORLD_SYNC.md)。
尚未完成真实双游戏移动验收、同一地图生成、捕鱼同步或存档同步。
0.1.22 历史范围见 [comparer 摘要](logs/guest-comparer-build-verification.json)，插件 Build 警告视为错误通过；0.1.21 的构建与 176 项测试证据见 [游戏内缓存摘要](logs/guest-ingame-cache-build-verification.json)；
0.1.13 历史观察构建见 [地图选择调用摘要](logs/map-selection-call-build-verification.json)。

## 本机环境

- 游戏目录：`F:\steam\steamapps\common\Dave the Diver`
- Steam App ID：`1868140`
- 检查时的 Steam Build ID：`25315876`
- Unity：`6000.0.52f1`，Windows x64 IL2CPP
- BepInEx：官方 `6.0.0-be.788+5b766a3`
- 插件：`local.davecoop.prototype`，源码 `0.1.23-dev`，安装/最近启动 `0.1.12-dev`，最近潜水 `0.1.11-dev`，发布包 `0.1.0`

## 编译与安装

所有命令在本项目目录运行。安装和更新 DLL 前，请先保存并正常退出游戏。

```powershell
# 使用已有 SDK 的 C# 编译器及 BepInEx 自带的 .NET 6 库，离线编译。
.\scripts\Build-Plugin.ps1

# 纯 CLR 姿态、协议及真实回环 TCP 端点测试（需系统 .NET 6 Runtime）。
.\scripts\Test-Core.ps1

# 仅将自己的插件 DLL/PDB 复制到 BepInEx/plugins/DaveCoop。
.\scripts\Deploy-Plugin.ps1

# 从 Steam 启动游戏后，检查加载标记。
.\scripts\Check-Status.ps1
```

`src/DaveCoop/DaveCoop.csproj` 也可供支持 .NET 6 的 SDK/IDE 使用。
本机现有 SDK 5 无法直接构建 net6.0 项目，因此使用上述离线编译脚本。
编译输出保存在 `artifacts/plugin/`，不复制框架或游戏的 DLL。

## 运行验证

当前已安装游戏显示 `DaveCoop Prototype 0.1.12-dev`；0.1.23-dev 尚未部署或启动，不能沿用旧进程日志验证新版本。
F7 世界探针已有真实潜水读取证据；生命周期回调在 0.1.6-dev 实际触发，池复用与卸载恢复仍待验证。
按 F8 显示或隐藏面板，设置保存在
`BepInEx/config/local.davecoop.prototype.cfg`。

日志中的成功标记：

- `DAVECOOP_BOOTSTRAP_OK`：插件加载成功。
- `DAVECOOP_UPDATE_OK`：Unity 正在调用自定义组件。
- `DAVECOOP_SCENE`：已读到活动场景。
- `DAVECOOP_PROBE_READY`：只读玩家/摄像机探针开始运行。
- `DAVECOOP_OBJECTS` / `DAVECOOP_PLAYER_SNAPSHOT`：对象关系变化与玩家采样。

F9 立即采样。离线接口读取、潜水验收和日志汇总流程见
[玩家与摄像机发现](docs/GAME_API.md)。
F10 切换第二角色延迟回放；0.1.2-dev 的基础潜水已验收。
F11 打开源码开发版的房间与本机测试入口，本机 TCP 已运行，真实双游戏验收尚待完成，流程见 MULTIPLAYER。
新源码的 F11 另有鱼状态传输和一条鱼显示的诊断开关，均默认关闭，流程与范围见 WORLD_SYNC。
当前候选来源是默认关闭的 Observe loading coroutine and scene ownership：先建Host/Join或Local test，再开启并进行新的自然入海。建房前的entry不能为当前房间提供来源，旧Observe map selection calls仅诊断，不发布或撤销候选。
路线每片 8 场景、最多 4 片，独立 32 包 FIFO；控制/心跳与 MapChoiceRetire 优先，动作/角色/世界/地图四路公平。
generation/revision 独立于 epoch；新代次首片撤旧路线，完整拼装才提交。固定来源清单删除/替换controller先撤旧代次再重发；退休Run/owner/controller不能复活，每帧最多8条选择，超128整帧拒绝，队列取消/溢出封owner不重播。Guest停本地来源保留Host候选。
0.1.14历史callbackFloor/cache适配已替换；原生ABI、完整来源与跨机地址仍待验收，NativeGenerationBound=false，全部Snapshot为evidence only、HostSelectionApplied=false。
Test-Core直接编译实际Core登记器、MapChoiceController与DTO，仅替代logger；原4适配已迁移，另6组快照和6组origin TCP用合成标量，不执行NativeHook/Capture/Unity provider。详见[当前传输](docs/ORIGIN_MAP_TRANSPORT.md)。
每人的独立容量/重量与个人Cargo账本保持；原生员工分流/入仓尚未接通。客机原生clone/双Data与Interaction根及恢复候选见[影子桥研究](docs/GUEST_ISOLATION.md)，静态签名与调用边不证明实际隔离。

## 框架安装记录

`scripts/Install-Framework.ps1` 只接受包含 `DaveTheDiver.exe` 的目录，
拒绝覆盖已有框架文件，并将原始游戏文件 SHA256 记录在
`artifacts/original-game-hashes.json`。
框架下载与暂存目录为 `.local/`，均不提交到 Git。

临时停用全部插件：退出游戏后，把游戏目录的 `winhttp.dll` 改名为
`winhttp.dll.disabled`；恢复时改回原名。
仅停用本插件：把 `BepInEx/plugins/DaveCoop/DaveCoop.dll` 移到 plugins 目录之外。

## 后续验证顺序

1. 用户方便时正常保存退出后部署，验证固定来源自然入海链及当前候选发送/退休、目标检查，补充typed返回/真实Scene/controller时序及跨机地址证据。旧五处观察只作可选诊断。
2. 接入实际加载前房主路线/IGP 选择采用、客机临时进度恢复及原生生成/AI 隔离，再用两份游戏核对地图与实体。
3. 接入可信玩家/装备和鱼叉、命中、QTE、拾取的房主原生裁定，覆盖同时操作与重复请求。
4. 完成正常返航、唯一收益账本及客机恢复，最后验证冷配置并更新发行包。

具体阶段与验收条件按 [开发计划](docs/PLAN.md) 执行。
原游戏产物、返航、保存及加载协程研究可用离线[NATIVE_ANALYSIS](docs/NATIVE_ANALYSIS.md)工具复现；
静态调用分析不执行游戏，也不代表原生捕鱼或双人玩法已完成。

## 官方资料

- [BepInEx IL2CPP 安装](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
- [BepInEx 官方构建](https://builds.bepinex.dev/projects/bepinex_be)
- [插件开发](https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html)

0.1.18历史根桥与已枚举围栏仅源码准备；默认ExistingCaches七根至今硬拒进入/静止，源事务拒绝后不安装围栏或捕获根。0.1.37另接默认关闭的Natural五根启动source，见[自然初始化](docs/GUEST_INITIALIZATION_BOOTSTRAP.md)；quiet与完整隔离仍未证，不能据历史167项或当前275项CLR/TCP测试描述实际存档隔离完成。[旧根桥摘要](logs/guest-shadow-build-verification.json)保持历史。

0.1.19把typed交互准备接入根桥，原玩家缓存尚未同步或已知可变子引用仍共享则拒绝；完整baseline/graph和native ABI仍未证。见[交互缓存](docs/GUEST_INTERACTION_SHADOW.md)、[更早的进入时机](docs/GUEST_ENTRY_BOUNDARIES.md)、[其它运行缓存](docs/GUEST_RUNTIME_CACHES.md)及[当前摘要](logs/guest-interaction-build-verification.json)。

0.1.20 将[typed食材缓存](docs/GUEST_INGREDIENT_CACHE.md)接入原生根桥第六步，原图在serializer前捕获；[精确API](docs/GUEST_INGREDIENT_API.md)核对真实SingletonNoMono backing及Entity字段。Build及174项测试通过，原生进入/静止仍关闭，见[该版摘要](logs/guest-ingredient-cache-build-verification.json)。

0.1.21 将[游戏内临时缓存](docs/GUEST_INGAME_CACHE.md)接入第七步，声明与类型检查候选见[GUEST_INGAME_API](docs/GUEST_INGAME_API.md)。三份已知原图均先于Serialize捕获并闭合核对，准备结束再严格复查；恢复按7→6→五Save根。显式handle上限21、DataStamps仍4，第七步单字段不允许OwnedMixed。

六种记录仅支持已覆盖子图；非空SubHelperSpecData或live gearQueue拒绝，不能分享或改空替代。普通record exact class检查及object_new+IntPtr包装仍未执行，进入/静止和全部客机/世界/袋权限false。[该版摘要](logs/guest-ingame-cache-build-verification.json)记录176项Core测试通过、插件Build警告视为错误通过；测试不执行native helper。每人独立容量/负重、剩余M3—M7及真实双端/冷配置要求保持。

## 0.1.22 字典 comparer 准备

[独立 comparer 合同](docs/GUEST_DICTIONARY_COMPARERS.md)与[离线精确 API](docs/GUEST_COMPARER_API.md)增加有限候选：int/string/InGameSaveType(int32) 的 Generic/Object，以及仅该 enum 的 Enum family，均需原/新 exact class 相同且不同 pointer。源 comparer 的 pointer/class/kind 与 dictionary aux 纳入已知图审计，Ingredients 使用同规则；原 null 可 capture 但 Prepare 拒绝，不调用 Default/CreateComparer/业务 getter，不共享或清空。custom、文化/hash-salt 未证明类型拒绝。新表先显式 `(capacity, comparer)` 构造，再 Add 并回读。

普通构造整体抛出时 assignment 尚未发生，`PartialConstructorAllocationRetentionVerified=false`；不能把先保留 comparer 描述成所有 constructor 内未知分配已持有。0.1.22 该轮 Core 输入未改，复用 0.1.21 实际 176/176，不重跑；[0.1.22 历史摘要](logs/guest-comparer-build-verification.json)的插件 Build 警告视为错误通过。七步/21 explicit handles/4 Data stamps 保持，全部 native ABI、完整隔离、进入/静止及 guest/world/bag 权限 false，无自动 Network/GUI 调用。[冷档候选](docs/GUEST_COLD_PROFILE.md)只研究更早首次 load、slot 和输出，不表示已采用；余下资源/actor/cache/output、房主地图、个人捕获与逐产物返航、双端/冷配置验收继续必需。
