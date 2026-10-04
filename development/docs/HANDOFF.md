# MultiDave 接手记录

## 已完成

已在 Windows Steam 版真实验证 BepInEx 788、207 个互操作程序集、
自定义插件加载、Unity Update 回调，以及 `DR_Start`、`DR_Logo`、`DR_Title` 场景读取。
插件日志证据见 `../logs/bootstrap-verification.log`。

开发在 `codex/player-discovery` 分支，当前源码与实机为 `0.1.11-dev`，编译及 85/85 项核心测试通过，新进程加载/Update/网络入口已确认。
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
新增纯 CLR 姿态、协议/TCP、会话、资源键、布局指纹/插值、实体身份/原子快照、鱼显示缓冲及生命周期代次、房主目标查询、鱼群缓冲、路线/IGP 清单与交互绑定观察，当前 85 项测试通过。
0.1.3-dev 已部署，新进程确认 F11 网络组件、加载、Update 和主菜单标记；连接及潜水显示待验证。
启动证据见 `../logs/network-bootstrap-verification.json`。
0.1.4-dev 引入默认关闭的 F7 世界只读探针及房主鱼状态诊断通道，0.1.5-dev 加入一条鱼的 Sprite/Spine 显示验证入口。
当前协议版本为 3，拒绝旧协议；WorldSlice 每块最多 16 个实体，通过场景确认后的 TCP 通道传输有界数值及显示描述。
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

0.1.11-dev 构建范围见 [鱼群与交互构建摘要](../logs/fish-world-interaction-build-verification.json)：

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

M3 游戏适配实机验证和真实双游戏验收尚未完成。
真实双游戏连接验收、客机原生鱼群/拾取接管与临时进度恢复均未完成。
0.1.11-dev 的活动鱼群可见与移除同步已获用户确认，Fire/Hook/Damage 观察已有单游戏日志；动画、完整捕获链和路线完整读取仍待验证，不能据此标记 M4/M5 完成。
服务器方案暂时搁置。当前包是验证开发入口的原型。

## 下一步

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
0.1.11-dev 已确认启动；继续在本次进程的 F11 / Local test 开启 Transmit read-only fish observations，
再按测试范围开启 Display received fish roster 与 Observe host harpoon and fish interactions；单鱼标签诊断仍可另开。
核对 WORLD_RECEIVED / NETWORK_STATE 及 FISH_WORLD_STATE / TRANSITION 的数字总数、可显示/未知资源/镜头内/节点数，
并确认画面、转向、镜头外重入、鱼终态和 Disconnect/正常返航清理。本机偏移后的显示鱼不可捕获；应操作原生鱼观察调用链。
核对 FISH_INTERACTION_READY / INTERACTION / STOPPED 的前后成对 CallId、prefix 绑定、原 bool、消费时 HP 及错误/丢弃统计；
地图清单核对 MAP_SELECTION / WARNING 的完整条件与 PostLoadObservationOnly，不把它当加载前接管。
显示仅创建自己的 SpriteRenderer 或 SkeletonAnimation；原鱼及其 AI 保持原样，不能当成共享鱼群。
对照 F7 探针核实实际鱼值和生命周期，再接入原生生成/AI 接管和 M5 裁定。
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
