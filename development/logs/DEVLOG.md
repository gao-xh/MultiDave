# 开发日志

时间按 America/Los_Angeles 记录。机器运行日志位于 `.local/logs/`，不提交。

## 2026-10-03 — 开发环境与插件原型

- 定位到 Steam 安装于 F 盘的戴夫，Build ID `25315876`、Unity `6000.0.52f1`、IL2CPP metadata 31。
- 用户保存退出后安装官方 BepInEx `6.0.0-be.788+5b766a3`，记录原始游戏文件校验值。
- 官方框架 ZIP SHA256：`F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A`。
- 首次启动生成 207 个互操作程序集，测试插件编译与部署成功。
- 真实日志出现 `DAVECOOP_BOOTSTRAP_OK`、`DAVECOOP_UPDATE_OK`，场景到达 `DR_Title`。
- 编译环境使用已有 SDK 5 的 Roslyn 与 BepInEx 自带 .NET 6 库；取消了失败的完整 SDK 下载。
- 原始游戏 EXE、GameAssembly.dll、UnityPlayer.dll 校验值复核一致。
- 原型仅显示诊断面板、读取场景，尚未实现网络。

## 2026-10-03 — 可共享配置仓库

- 用户要求独立开发目录、开发日志、Skills，以及通过 GitHub 链接由 Codex 完成配置。
- 源码、脚本、依赖清单、文档和日志已整理到 `development/`。
- 指定目标仓库为 `https://github.com/gao-xh/MultiDave`，检查时为空且有写入权限。
- 编写玩家自动配置入口：Steam 自动定位、已验证版本检查、官方框架哈希检查、
  插件 ZIP/manifest 检查、旧插件备份、运行日志和按需启动游戏。
- 打包本项目自己的原型 DLL；发行包不含任何游戏或框架 DLL。
- 配置 Skill 放置于仓库 `.agents/skills/dave-coop-setup/`，根目录 AGENTS 引导新会话读取。
- 当前验证：全部 PowerShell 脚本语法检查、自动定位、环境检查、发行打包与本机玩家安装通过。
- 配置器重复安装返回 `AlreadyInstalled=true`；损坏包和非法 ZIP 均被拒绝，现有插件 SHA256 未改变。
- 测试发现显式指定本地包时未校验 ZIP 的 sidecar；已修复根入口并再次通过损坏包测试。
- Skill 使用官方 quick_validate 校验通过；Windows 中文环境使用 Python UTF-8 模式运行校验器。
- 根入口安装并启动后，新进程日志确认加载、Update 和 `DR_Title`；证据保存在同目录日志文件。
- 已通过本机 Git 推送到指定仓库；重新从 GitHub 克隆后，自动定位及幂等安装通过。

## 2026-10-03 — 开发计划与 M1 玩家发现

- 按用户要求建立 `docs/PLAN.md`，以 Windows 双人潜水/局域网 MVP 为目标；
  服务器暂缓，各阶段定义验收条件。开发分支为 `codex/player-discovery`。
- 源码升级为 `0.1.1-dev`，保留 `distribution/` 中已验证的 0.1.0 安装包。
- 新增 `Inspect-GameApi.ps1`，用 Mono.Cecil 离线读取生成接口的类型/成员，
  报告写入忽略目录；明确这些封装不等于原始游戏方法体。
- 新增主线程只读探针及普通 CLR 快照模型，读取玩家位置/旋转/朝向、移动输入、
  动画状态、管理器玩家引用和摄像机跟随。JSONL 每 0.5 秒采样，默认最多 9000 条。
- 编译脚本支持子目录并引用游戏接口与动画模块；`Check-Status.ps1` 新增探针启动标记。
- 首轮真实启动到 `DR_Title`，76 条快照可解析且无探针读取错误。
  辅助方法出现 IL2CPP 导出类型警告；加 `HideFromIl2Cpp` 后重新编译部署并真实重启，警告消除。
- 用户进入 `A01_01_01` 潜水，探针发现 `PlayerGroup(Clone)/DaveCharacter`。
  管理器引用与玩家实例一致，`CameraManager` 跟随同一对象的根 Transform，第二目标为空。
  已读到位置、左右/斜向朝向、非零移动输入、旋转变化与一层动画状态，无探针错误。
- 新增 `Get-DiscoverySummary.ps1`。真实日志汇总通过；合成数据检查了玩家/摄像机统计、
  非零输入、会话新鲜度、损坏中间行、截断尾行和不合格 session 拒绝路径。
  合成测试只验证汇总器，不作为真实联机证据。
- 打包器新增编译 DLL 属性与发布元数据的版本一致检查。
  当前 `0.1.1-dev` / `0.1.0` 不一致时正确拒绝打包，现有 distribution SHA256 不变。
  使用原发布 DLL 在隔离测试目录运行打包，版本一致时正常生成 0.1.0 包。
- C# 编译（警告视为错误）通过，PowerShell 脚本语法检查通过。
- 更新 README、开发约定、HANDOFF 和 GAME_API，便于后续 Codex 从日志与计划继续。
- 用户继续进入 `Boss_000`，探针确认旧玩家实例从当前采样消失，
  新玩家实例与管理器及摄像机绑定正确，无探针错误，M1 基础验收通过。
- 本地提交 `265e6fc`。自动审批最初拒绝公开推送真实运行摘要；
  用户明确授权公开这批源码、计划、日志和验证摘要后，开发分支已成功推送至 GitHub。
- 遗留：返航/退出路径仍待补充数据。其他潜水场景、特殊角色、
  角色生成原始控制流与框架冷安装尚未验证；第二角色与网络尚未实现。
- 下一步：实现 M2 仅显示的第二角色，并补充返航/退出验证。

## 2026-10-03 — M2 显示原型与 M3 会话基础

- 用户要求设定持续开发目标；已设 goal，完成条件保持为真实双游戏连接、潜水、合作捕获、
  返航结算闭环及可复现配置验证。服务器暂缓。
- 补读 M1 完整旧会话：1886 条快照，无解析失败或探针错误，确认 Boss 返回潜水、
  返航、大厅及主菜单。返航后大厅/主菜单样本未留下潜水玩家或 InGameManager。
  更新历史验证摘要，保留原测试 DLL 哈希，避免与新构建混用。
- M2 新增独立 SpriteRenderer 显示节点和本地延迟回放，不克隆原玩家脚本。
  30Hz 采样、有界姿态时间线、插值、淡蓝色显示、F10 开关及所属场景清理已编译。
  初次真实启动到主菜单成功；实际材质、排序、动画和输入/镜头仍需入海确认。
- 纯 CLR 数学测试发现有限四元数分量仍可能使长度平方溢出；加入有限长度检查并通过测试。
- M3 新增数值姿态/精灵帧协议、精确版本握手、房间和玩家身份、128KiB 长度分帧、
  序号、防重放、并发写串行化和取消。修复无效发送占用序号的问题。
- 新增 SessionMachine、SessionPeer 和 LanHost/LanGuest：有界控制队列和事件队列，
  收发各保留最新快照，深拷贝 DTO，心跳/往返时延/时钟偏移，握手/场景/失联超时。
  场景按房主 epoch 提议、客机核对场景及世界指纹、房主提交；重载及暂停清除旧帧。
- `scripts/Test-Core.ps1` 24/24 通过，包含真实回环 TCP 两端、场景不一致、
  客机重载、超时、监听取消、正常离开及连接清理。`Build-Plugin.ps1` 警告视为错误通过。
  测试仅使用本项目数值 DTO 与场景夹具，不等于真实两个游戏通过。
- 汇总工具在 PowerShell 7.5 自动解析 JSON 日期时误把 UTC 当本地时间，
  导致旧会话被判成当前进程；已保留 DateTime.Kind，当前运行下旧 M1 会话正确返回 false。
  PowerShell 语法检查通过。
- 用户最初不方便试玩，随后表示可以验证；正常退出状态下部署并启动已通过测试的 M2 构建
  `E2A7DEC29ACB894F72A2D8528C099E82AB867DDE00F82DC739707BAA8EBB5AB9`，
  新进程已确认加载、Update、探针及回放组件准备标记，等待入海反馈。
  新增会话源码另行编译，运行中的游戏尚未加载这些新增代码。
- 更新 PLAN、HANDOFF、MULTIPLAYER、README 和配置 Skill。
  默认 distribution 保持已验证的 0.1.0；尚未发行开发构建。
- 下一步：M2 入海验收与日志对照；实现 M3 资源键映射、游戏主线程适配和连接入口，
  然后做真实双游戏移动测试。M4 同一地图、M5 捕鱼裁定、M6 返航结算仍未实现。

## 2026-10-03 — M3 精灵资源键准备

- 会话基础检查点 `aebcf53` 已推送到用户授权的公开开发分支。
- 新增纯 CLR SpriteKey：对精灵/纹理描述生成稳定、版本化元数据键，
  名称长度前缀与浮点位编码避免分隔符及文化格式差异。
- 新增有界 AssetRegistry：本机不同资源出现同键时标记歧义并拒绝解析，
  清空后不保留前一场景引用。不会把本机实例 ID 发送给对方。
- 新增主线程 SpriteCatalog，读取 Unity Sprite 元数据并解析已加载资源，
  限制缓存和扫描频率；未接入网络显示，不修改或销毁游戏资源。
- `Test-Core.ps1` 新增 5 项有意义的资源测试，总计 29/29 通过。
  `Build-Plugin.ps1` 编译通过，SpriteCatalog 的实际调用与跨机器键一致性待验证。
- 游戏继续运行已部署的 M2 构建，等待用户入海反馈；没有覆盖运行中的插件。
- 下一步：把捕获/资源解析/远程呈现接到主线程会话接口，增加连接入口，
  实测动态图集名称、不同加载配置及精灵槽位；同一游戏地图识别仍属于 M4。

## 2026-10-03 — M2 入海验收与 M3 游戏适配

- 用户在本次 0.1.2-dev 潜水确认：“能，正常，在模仿我的动作”。
  日志记录 `A03_01_02` 的 37 条回放状态，原生玩家数均为 1，摄像机始终跟随本地玩家，
  可见部件最多 4，F10 停用清理一次并重新生成，返航销毁清理一次，回放警告 0。
  M2 基础验收通过；证据对应旧部署哈希 E2A7…，不混入新源码证据。
- 发现武器等 SpriteRenderer 延迟生成：首次 21 部件，F10 重建后 25。
  0.1.3-dev 新增部件集自动核对/刷新；网络槽位使用节点内组件序号，避免全局索引随新增部件变化。
- 加入 F11 房间面板、Host/Join/Local test/Disconnect，默认不连接、不监听。
  打开/关闭面板保存并恢复系统鼠标状态。握手身份读取真实 Steam manifest 与 Unity 版本。
- 主线程适配器捕获本地姿态/资源键，最多 30Hz；网络异步 I/O 仅接收 CLR DTO。
  接收帧经有界 RemoteMotionBuffer 插值，使用 SpriteCatalog 解析，显示节点只含 Transform/SpriteRenderer。
  失联、epoch 变化、返航、断线及装备变化清理自己节点，未修改游戏任务或存档。
- 新增只读布局核对：静态非触发 2D 碰撞体完整几何/矩阵、已加载场景名及动态节点选择地址。
  纯 CLR 指纹按条目排序且保留重复数量，拒绝未知或未就绪布局；它不生成相同地图、不统一实体状态。
- 新增 6 项布局与插值测试，`Test-Core.ps1` 35/35 通过；
  `Build-Plugin.ps1` 警告视为错误编译通过，PowerShell 语法及项目 XML 检查通过。
  构建哈希：1E2264723B799150033C55F0754E73DA9F33AFEF5C61FEE5A5030A7DECB8064D。
- 修复互操作数组 using 编译错误；异步连接拆为普通 async 函数，解决现有编译器对 Task.Run lambda 的错误推断。
- 用户游戏仍在运行，已请其正常保存退出；新构建尚未部署/实际执行。
  F11 UI、原生资源解析、静态几何读取、跨机器资源键和真实双游戏路径仍待验证。
- 下一步：正常退出后部署，先验证主菜单组件加载及本机 TCP 回放，再真实双游戏移动；
  按 M4–M6 继续统一地图/实体、裁定捕鱼拾取和返航结算。默认发行包仍为 0.1.0。

## 2026-10-03 — M3 新进程加载与同一海洋准备

- 用户保存退出后部署并启动 0.1.3-dev，自写 DLL SHA256 与构建一致：
  `1E2264723B799150033C55F0754E73DA9F33AFEF5C61FEE5A5030A7DECB8064D`。
  本次进程启动于 2026-10-04T04:57:33Z，确认加载、Update、玩家探针、回放及
  DAVECOOP_NETWORK_READY，经过 Start/Logo/Title。框架已知 Class::Init 警告仍存在。
  证据见 network-bootstrap-verification.json；仅证明组件加载，不证明网络连接/潜水显示。
- 已请用户按 F11 / Local test 测试潜水、Disconnect 和返航，尚无本次反馈。
  原始启动日志留在忽略的 .local/verification/，不公开机器日志。
- 用户询问如何让两端看到同一海洋、同一批鱼并响应操作。
  新增 docs/WORLD_SYNC.md，明确房主地图选择、入海确认、会话/epoch/实体 ID、
  状态与事件版本、客机 AI/生成/奖励接管、去重裁定、第二玩家命中与返航账本。
  第一闭环为同一条普通鱼的生成/移动/移除，再连接伤害/捕获；同随机种子不作为完成证据。
- 新增 scripts/Inspect-WorldApi.ps1；Inspect-GameApi 支持独立报告名称，
  本机成功读取 20 个地图/鱼/伤害/拾取/临时数据类型的互操作签名。
  未执行生成、伤害、拾取、停止 AI 或存档写入入口；签名不能证明原游戏控制流。
- 源码 0.1.4-dev 新增 Discovery/WorldProbe、WorldProbeController、WorldObservation，
  默认关闭，F7 开启后每 2 秒只读采集地图节点/IGP 选择、分配器、鱼种/位置/HP/捕获/死亡与物品。
  DTO 序列化不持有 Unity 对象；集合、错误、字符串、每进程快照和 32 MiB 文件上限均有界。
  本机实例 ID 仅用于诊断，后续网络身份必须另行分配。
- Build-Plugin 编译通过（警告视为错误），0.1.4-dev 构建 SHA256：
  `587D4D5B4FA602783B5E7DDCC2FB6DF9657B3EE3AD1CACC6FD32799741481A05`。
  两个元数据脚本语法及项目 XML 检查通过。核心未修改，保留原 35/35 的版本与证据，未重跑。
  新探针尚未部署、尚未实机运行，证据见 world-probe-build-verification.json。
- 更新 README、PLAN、HANDOFF、开发约定及配置 Skill 的海洋研究/探针入口。
  当前游戏仍运行 0.1.3-dev；下一次正常退出后部署 0.1.4-dev，并验证探针真实读取及观察开销。
  同一地图生成、鱼/互动同步、双游戏闭环和冷配置验收仍待完成。

## 2026-10-03 — M4 实体身份与鱼状态通道

- 复核当前进程仍运行 0.1.3-dev，日志只有主菜单启动，尚无 F11 连接事件或本次试玩反馈。
  前一目标工作提交了世界方案/探针及真实加载证据，属于具体进展；本次继续独立开发。
- 新增 Core/World/HostEntityRegistry、EntityState/WorldSnapshot/WorldSlice、WorldAssembler。
  房主分配实体 ID，本机 token 不上传；同种鱼分开，释放/池复用和同 epoch 清空不复用 ID。
  有界快照按 64 个实体分块，总上限 4096，检查完整性后一次性提交，旧修订/epoch 不恢复旧鱼。
- 接入 PacketCodec、SessionMachine、SessionPeer 的房主状态发布和客机最新完整快照邮箱。
  控制消息优先，世界块与移动轮流发送；未发完旧快照可由新修订替换，内存保持有界。
  复核发现源快照 epoch 也必须核对，已拒绝把旧本地快照重新标成新场景数据。
- 协议版本更新为 2；避免同为 0.1.4-dev 的较早源码误接受不认识 WorldSlice 的消息。
  新增真实 TCP 旧协议拒绝用例，两端按握手失败清理。
- 新增 Networking/FishStateCapture，在 Unity 线程读取当前场景已初始化鱼的种类、
  姿态、HP、死亡和捕获状态。完整读取/校验及清理后分配 ID，不修改原鱼/AI/物理/收益。
  F11 默认关闭的 Transmit read-only fish observations 勾选启用，最多 5Hz；
  Local test/客机完整收到数值清单后记录 WORLD_RECEIVED，最多每 2 秒输出概要。
- 新增 9 项有意义的实体测试；Test-Core 44/44 通过。
  真实 TCP 用夹具验证清单、HP 更新与移除；这些不是原生鱼或两游戏的捕鱼证据。
  Build-Plugin 警告视为错误通过，最新 0.1.4-dev SHA256：
  `76225076A95982E8A43D24E62C79607A680D67752B95CE8239CE7E164FF163D0`。
  新源码未部署，先前 587D… 的探针构建已由此修订替代，历史日志保留各自验证范围。
- 更新核心/构建摘要、WORLD_SYNC、MULTIPLAYER、PLAN、HANDOFF、README 与 Skill。
  尚需：新进程只读探针/实际鱼通道验证、原生生成与池生命周期挂钩、地图选择接管、
  Sprite/Spine/Mesh 资源与远程鱼显示、客机 AI 隔离、M5 裁定及 M6 返航账本。
  同种池对象在两次轮询之间重启及短命生成/销毁仍需事件入口；本次未将 M4 标记完成。

## 2026-10-03 — M4 单鱼 Sprite/Spine 显示准备与慢连接完整提交

- 目标：先验证房主实际鱼状态在接收端的显示，再实现完整地图/鱼群接管与互动。
  当前游戏仍运行 0.1.3-dev；未覆盖运行中的插件，也未调用原生伤害、捕获或存档写入。
- 读取鱼显示/生命周期及 Spine 互操作签名，确认 FishSpineAnimator、SkeletonMecanim、
  SkeletonAnimation 创建/更新与动画/皮肤接口；FishAISystem OnEnable/OnDisable/OnDie 是后续池生命周期研究入口。
  新增 scripts/Inspect-FishRenderApi.ps1，实际成功读取游戏 7 个类型与 Spine 9 个类型。
  这些仅是签名，不证明原生控制流、挂钩副作用或实际画面。
- 新增 Core/World/FishVisual、FishPreviewBuffer；EntityState 带可选显示描述并深拷贝。
  网络只含资源键、数值姿态、颜色/排序及皮肤/主动画/时间/缩放，限制长度和数值。
  缓冲只选择一条鱼，保留 16 帧；清单移除、场景/epoch 更换或失联按界限清理/隐藏。
- 新增 Rendering/SpineCatalog、Networking/FishVisualCapture 和 RemoteFishPreview。
  Sprite 复用资源键；Spine 使用骨骼名/缩放/图集名元数据哈希，歧义拒绝，跨机稳定性仍待实测。
  只创建自己的显示组件，未复制鱼 AI/物理/伤害/奖励脚本；动画混合、多轨、约束、槽位材质和非 Spine Mesh 未覆盖。
  本机 TCP 显示向右偏移 3 个单位并着淡蓝色，以便与原鱼区分。
- F11 新增 Preview one received fish，和 Transmit read-only fish observations 均默认关闭。
  NETWORK_STATE 记录显示选择/可见/未知资源及捕获显示失败数；预览异常单独处理并清理自己节点。
- 协议改为 3，拒绝旧协议 2；每块 16 个实体以容纳最大显示字段，快照仍最多 4096 个实体。
  修改 SessionMachine：已开始的批次发完才提交下一批，后续更新仅保留最新清单。
  持续生产新快照不会让慢连接不断丢弃未完成批次；控制和移动消息仍有发送机会，缓存有界。
- Test-Core 50/50 通过（新增 5 项显示描述/缓冲用例和慢连接用例）：
  覆盖编码/复制隔离、畸形字段、最大消息大小、插值/失联/清单移除/epoch 重置，
  以及每发一块便更新状态仍能完成整批原子提交。测试是 CLR 夹具，不是原生鱼或双游戏验收。
  Build-Plugin 警告视为错误通过，引用现有生成的 spine-unity.dll，无新依赖下载。
  当前 0.1.5-dev 自写 DLL SHA256：
  `F8063FA1E1C025A8B17575A5C763AF15B662B44D84377D29787886A17C5BEA60`。
- 更新计划、接手记录、WORLD_SYNC、MULTIPLAYER、README、开发约定、配置 Skill 和公开验证摘要。
  新版本尚未部署，原生初始化、材质、动画、布局读取及跨机资源键仍待验证。
  下一步：正常退出后部署并验证 F7/F11 实际鱼数据和显示；接入池生命周期、房主地图选择，
  客机生成/AI 接管与双游戏验收，再推进 M5 裁定和 M6 返航账本。默认发行包仍为 0.1.0。

## 2026-10-03 — 实际鱼本机 TCP 验证与对象池生命周期观察

- 用户支持房主统一管理世界的方案。前一目标 turn 完成源码/Skill/验证记录并推送 c386f6b，属于具体进展。
  本次先确认游戏已退出，部署并启动 0.1.5-dev，实际进程启动于 2026-10-04T05:45:30Z。
  正常核对新鲜日志、版本与 F8063F… 自写 DLL；Steam 启动后的实际游戏进程与初始启动句柄不同，未使用旧句柄判断加载。
- 用户执行 F11/Local test 与鱼诊断，在 A03_01_02 达到 Ready。
  实际鱼清单经两个本机 TCP 会话传输，59 条接收概要，数量 12..21，最大修订 582。
  59 条状态记录单鱼显示组件可见，未知资源/未解析显示均为零，没有网络/布局/鱼显示插件警告。
  世界探针 72 条快照、玩家探针 452 条快照均无错误；分配器和物品达到探针上限并明确截断。
  本机观察到 4 条鱼的 HP 变化、3 条挂钩状态样本；没有死亡/捕获样本，不能据此确认合作捕获或收益。
  证据见 native-fish-loopback-verification.json；原始机器日志已归档至忽略的 .local/verification/fish-preview-0.1.5。
- 用户确认是主动退出，未观察到 Disconnect/返航标记；画面、动画及清理验收仍待反馈。
  用户询问第二个戴夫不发射鱼叉：现有角色帧未包含独立投射物/发射事件，更新 M5 范围与显示边界，后续按房主裁定接入。
- 新增 Core/World/FishLifecycleTracker 和 Networking/FishLifecycleHooks，更新 HostEntityRegistry、FishStateCapture、NetworkController。
  查询全部鱼类声明的生命周期方法：FishAISystem OnEnable、两个 OnDisable 与六个 OnDestroy，共 9 个目标。
  使用现有 0Harmony 2.10.2 / IL2CPP 后端，只记录已观察对象的 CLR 状态，不跳过原方法、不写原鱼状态。
  仅开启房主鱼诊断时安装，关闭/断线只卸载自己的挂钩；异常停止鱼发布，回调异常不传播给原游戏。
  未跟踪鱼的回调不分配记录，容量 4096；基类/子类嵌套回调幂等，代次变化使同种池对象取得新的网络 ID。
  当前仍只有完整状态快照，短命生成/销毁和交互事件、客机 AI 接管及地图选择尚待接入。
- 新增 5 项生命周期核心测试，总计 Test-Core 55/55 通过：
  采样间池复用、销毁/指针复用、重复回调、容量/未知回调/清理及并发均覆盖。
  Build-Plugin 警告视为错误通过，新源码/部署 0.1.6-dev SHA256：
  `8563E3C2835178CF03299851C904F471CE5873A094B57A2432BDA44B2AFE1A24`。
- 用户明确允许再次打开验证，已备份并部署 0.1.6-dev，实际新进程启动于 2026-10-04T05:56:35Z。
  新日志确认加载、Update、网络和世界探针；本机 TCP 已在 A03_01_02 就绪。
  当前鱼诊断/显示开关均关闭，尚未安装新生命周期挂钩，已指导用户开启后验证。
  后续核对 FISH_LIFECYCLE_READY、计数/错误、捕获/停用/销毁、关闭诊断/Disconnect 与返航恢复。
  本机运行不构成双游戏、统一地图/AI 或合作捕获验收；M4–M7 的完整目标保持不变。

## 2026-10-03 — 修正镜头内鱼预览定位与明确标签

- 0.1.6-dev 用户开启诊断后，日志确认 9 个生命周期挂钩安装成功及真实回调变化，回调错误为零。
  实际鱼继续经本机 TCP 传输；出现一次连接被拒绝的 SocketException，随后 Local test 已连接并进入 Ready，未隐瞒该警告。
  该轮用户未能辨认淡蓝色鱼，视觉验收不通过，不能把组件启用视为屏幕可见。
- 检查发现预览选择清单首条鱼，未考虑镜头；颜色乘原鱼纹理也不足以唯一识别预览。
  更新 FishPreviewBuffer：支持近鱼参考位置与镜头资格，排除不可见/死亡/捕获鱼，并在近似距离下保留旧选择避免抖动。
  RemoteFishPreview 按偏移后的位置核对相机视口/图层，新增蓝色十字和 MultiDave Fish Preview 标签。
  NETWORK_STATE 增加 FishPreviewInView 与 FishPreviewMeshVertices；标签与网格实际显示分开验收。
- FishLifecycleHooks 在卸载后复核自己的 9 个挂钩注册已移除并记录 FISH_LIFECYCLE_STOPPED，
  NetworkController 新增 NETWORK_DISCONNECTED，用于结合后续帧、玩家操作及返航验证清理。
- 新增 3 项预览选择测试；Test-Core 58/58 通过，覆盖镜头资格/近鱼/死亡/空选择、切换滞后与非法观察位置。
  Build-Plugin 警告视为错误通过，0.1.7-dev 自写 DLL SHA256：
  `53336298F2C47D3BCC3F300E21B5629938C8ED4A2C0A167C95E2B10CAEA31849`。
- 用户主动关闭后已归档 0.1.6-dev 原始会话，再备份/部署 0.1.7-dev 并打开；实际进程启动于 2026-10-04T06:06:14Z。
  A04_01_02 的本机 TCP、实际鱼读取与生命周期回调运行，记录镜头内预览及 55 个网格顶点，无本次插件警告。
  该地图有 1 个无法取得显示描述的鱼，已明确记录；数值鱼状态不受影响，所有鱼型/材质尚未覆盖。
  用户反馈“看到了”，已继续请求动画/转向、Disconnect 与正常返航验收，不据此标记完整 M4/M5。
- 更新 README、PLAN、HANDOFF、WORLD_SYNC、MULTIPLAYER、配置 Skill 与验证摘要，分别保留 0.1.5/0.1.6/0.1.7 的版本哈希和范围。
  下一步：完成视觉/清理复核，实际池重生与地图选择接管、完整客机鱼群/AI、鱼叉事件及房主捕获裁定；真实双游戏与冷配置仍是目标条件。

## 2026-10-03 — 修复镜头内预览突然消失的自动断线路径

- 用户确认 0.1.7-dev 的预览鱼可见，随后报告会在镜头内突然消失。
  最终归档日志记录两次 Local avatar part was destroyed，触发 NETWORK_DISCONNECTED 和预览清理。
  前条日志所述无插件警告仅为初始观察时点；最终会话有这两条失败记录，稳定性验收不通过。
  FISH_LIFECYCLE_STOPPED 核对自己的注册已卸载；没有正常返航证据，不据此标记玩法恢复通过。
- 更新 LocalAvatarCapture：跳过已销毁的缓存 SpriteRenderer 槽位，不让普通武器/效果显示变化终止连接。
  NetworkController 记录 NETWORK_PARTS_STALE、安排下一次 Update 的部件列表刷新；原玩家不可用仍按既有场景边界处理。
  RemoteFishPreview 放宽已选鱼边缘范围，新鱼仍要求位于镜头内部，以减少筛选闪失。
- Build-Plugin 警告视为错误通过；Test-Core 58/58 通过。
  这些 CLR 测试不运行 Unity，不能证明原生销毁对象恢复已通过；实机射击、稳定性和清理仍待确认。
  0.1.8-dev 自写 DLL SHA256：`7950EF93D20CF6E9912C14390D8649CB1C501ADB73A61495DEF9899777C875B5`。
- 已确认游戏退出，归档 0.1.7 原始会话到忽略的 .local/verification/fish-preview-0.1.7，备份并部署修复版。
  Steam 更换启动进程后，实际新进程启动于 2026-10-04T06:16:09Z，新鲜日志确认 0.1.8-dev 加载和 Unity Update。
  已请求用户潜水、移动/转向/射击，观察预览是否突然消失，再 Disconnect/正常返航保存退出。
- 更新公开摘要、README、计划/接手/世界/网络文档及配置 Skill，保留旧失败证据。
  默认发行包仍为 0.1.0。下一步实机复核恢复、实际池重用与正常清理，继续地图/鱼群接管及房主互动裁定。

## 2026-10-03 — 锁定预览鱼身份并记录即时隐藏原因

- 前个目标 turn 已完成 0.1.8-dev 修复、编译、58 项核心测试、部署、Skill/记录及公开推送 5089f0d，属于具体进展。
  本次重新核对当前源码、进程和日志，继续推进原目标；未把单鱼预览当成完整 M4/M5。
- 0.1.8-dev 在 A03_01_02 真实潜水，用户报告仍会消失，鱼和标签一起消失，标签又会到另一条鱼身上。
  最终归档 17 条 Ready 概要均显示鱼可见/镜头内/资源正常，编号 11→18→3→15→18→20，支持自动近鱼重选导致目标跳变。
  没有旧 Local avatar part was destroyed 异常，也没有 NETWORK_PARTS_STALE，不能据此确认该恢复路径已实际执行。
  用户另指出标签鱼抓不了：当前只有自己的显示组件，没有命中/捕获通道，已说明 M5 缺失并记录，未启用原生奖励副本。
- FishPreviewBuffer 现在保留当前活鱼身份；距离更近、镜头资格变化、短暂 Visual 不可见/null 不重选。
  初选、合法移除/死亡/捕获或 RequestReselect 才按镜头内最近候选选择；手动重选保留 epoch/revision 重放屏障。
  Sample 提供 Stale/BeforeArrival/EmptyHistory/InvalidClock/Ready 原因，不改变一秒失联隐藏界限。
- RemoteFishPreview 新增有界即时 FISH_PREVIEW_SELECTION / TRANSITION，与两秒概要分开。
  NetworkController 记录 Status/SnapshotAge、明确场景/停用/异常/断开清理原因，F11 增加手动重选按钮。
  选中鱼源不可见时仍按源状态隐藏，但身份不跳；完整状态观察集合不是永久销毁事件，完整世界接管仍需完善生命周期语义。
- 两个只读子任务分别评审选择/世界接管及地图加载签名；测试子任务只改两份测试文件。
  新增 4 项身份/暂时显示/重选重放/采样诊断用例并更新旧预期；Test-Core 62/62 通过，Build-Plugin 警告视为错误通过。
  0.1.9-dev SHA256：`1F2D0C3B8B42B9439BAE1ADB6339238DA792D98A7DC50FC055317908B9811D86`。
- 确认游戏已退出，归档 0.1.8 原始日志到忽略的 .local/verification/fish-preview-0.1.8，备份/部署 0.1.9-dev 并启动。
  实际 Steam 重启后的进程启动于 2026-10-04T06:26:47Z；加载与后续 Update/潜水验证分别核对。
  更新真实失败摘要、核心证据、计划/接手/Skill；0.1.9-dev 即时日志确认初选保持、出镜/重入及显式手动重选，用户明确反馈“不再消失”。
  本轮身份稳定性通过；有自己的卸载/Disconnect 标记，动画和正常返航仍未确认。
  原生证据见 native-fish-preview-identity-verification.json，预览仍不可捕获。
- 地图研究确认：现有四份日志 173 条世界快照、Dynamic 节点均为 0；相同 A03 入场会选择不同 IGP 及 B 场景。
  SceneContext 路线缓存及 IGP 随机返回是下一步观察入口；Init/LoadPrefab 返回 IEnumerator，工厂 postfix 不等于加载完成。
  另有 IGP 的 ISaveableInstanceData/StoreUsedInstacneID 边界，尚未改写选择或存档。
  后续继续完整鱼群注册/房主反向绑定、真实命中事件及加载前路线清单，再接入客机接管与 M5/M6；默认包仍为 0.1.0。

## 2026-10-03 — M5 房主目标身份与原生查询准备

- 0.1.9-dev 的身份稳定性修复及真实用户反馈已公开推送 ac6bd73。
  后续核对游戏进程已退出，归档原始会话到忽略的 .local/verification/fish-preview-0.1.9。
  没有正常返航场景或明确退出方式反馈，维持返航未验证；不把进程结束当成保存成功。
- 新增只读值类型 HostEntityTarget 与 HostEntityRegistry 的 epoch/EntityId 反向查询。
  替换、Unbind、Clear、新 epoch 同步撤销旧反向条目；同 epoch Clear 保留递增编号，目标快照不充当永久许可。
  新增 FishLifecycleTracker.TryGetActiveGeneration：未观察/失活/销毁/清理后查询失败，不创建新对象代次。
- FishStateCapture 保存原生包装器，Unity 线程 TryResolveNativeFish 重新核对指针、实例编号、TID、场景和活跃代次。
  查询不做攻击/伤害/捕获/收益写入；真实操作还需要请求权限、距离/装备/状态规则和结果去重。
  清理保留房内 ID 单调性，断房间 ResetRoom；NETWORK_STATE 新增 HostFishBindableTargets，准备原生身份查询实测。
- 新增 5 项反向查询测试及 1 项生命周期查询用例；Test-Core 68/68 通过，Build-Plugin 警告视为错误通过。
  源码 0.1.10-dev SHA256：`5BF9494E3E6C7782C9A3F4B077C7C1075EEC6CF92892B39B76734A993232204D`。
  该新构建未部署/运行；最新实机证据仍是 0.1.9-dev，默认包仍为 0.1.0。
- M5 离线研究确认鱼自身覆写 HookedByProjectile/WinFromProjectileinFight，鱼叉 Fire/CollisionDetection、
  Damageable.TakeDamage、Damager.DoDamage、LootBox.Add、图鉴和返航入仓是分别观察的候选。
  GetAttackData 是 ref-return 属性，不能当普通返回方法挂钩；攻击/收益原始控制流与副作用仍未验证。
  新增 Inspect-FishInteractionApi.ps1，报告只保留忽略目录；后续先只读观察真实射击至入袋，再接入房主请求裁定。
- 更新 README、核心/构建证据、接手/计划/世界文档和 Skill；继续完整鱼群/地图接管、命中请求与房主捕获/返航闭环。

## 后续日志格式

每次追加：日期、目标、关键改动、验证命令及实际结果、遗留问题、下一步。
