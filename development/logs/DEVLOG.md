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

## 2026-10-03 — 完整观察鱼群显示、原生交互记录与地图选择清单

- 用户确认 0.1.9-dev 的标签鱼不再突然消失，已将身份稳定性列为实测通过；不扩展为动画/返航或合作捕获验收。
- 三个子任务分别实现并评审鱼群显示、原生调用观察及加载后路线/IGP清单；root整合开关、生命周期、测试与日志。
  完整接收的观察鱼群使用每鱼16帧、原子增删/校验/复制；镜头、失联或临时Visual缺失不改数值身份。
  RemoteFishWorld共用自建Sprite/Spine节点，显示错误按鱼隔离；单鱼预览保留已确认的锁定、手动重选和即时标签日志。
  该观察集合仍只覆盖玩家当前场景活动鱼，未接管客机原生AI/碰撞/收益。
- ObservedHostTargets发布冻结的纯CLR pointer→HostEntityTarget快照，不传输本机指针。
  resolver检查代次并二次复核tracker对象/当前代次；world采样失败撤销旧观察映射。
  原生操作仍必须主线程重新查身份；观察快照不是攻击许可。
- FishInteractionHooks观察8处精确声明的prefix/postfix，以CallId配对并在prefix保留当时房主epoch/编号/代次/TID。
  仅只读原伤害bool，不读取原生值类型参数、不跳过原方法、不替换结果；callback不调用Unity。
  队列4096/上下文1024/全进程累计8192条；关闭/换epoch/Disconnect清理，回调失败锁存至重启。
  Drain主线程反查有效原生鱼并记录HpAtDrain；该数值不是原调用前后HP，原bool亦不是收益或捕获确认。
  发射与鱼命中目前尚无同投射物关联，不能称整条鱼叉至入袋链已验证。
- MapSelectionCapture只读已有SceneContext、路线缓存和IGP选中项，不触随机选择/条件/存档。
  每选中scene至少一组、原注册列表一致、两个不同Unity frame指纹一致才报告；不完整时返回null说明原因。
  DTO带边界、路线链/重复校验、Copy及canonical哈希；跨机层级地址未验证，也未接加载前房主选图或协议清单传输。
- F11新增默认关闭的 Display received fish roster 和 Observe host harpoon and fish interactions。
  Test-Core 85/85通过；最终Build-Plugin警告视为错误通过。0.1.11-dev SHA256：
  `05C5379FF301C55D6841FA23DE00BDCC4EB80B6D404CA6122AC5A17523140E01`。
- 确认游戏退出后备份并部署，实际新进程启动于2026-10-04T06:51:29.1422308Z。
  新鲜日志确认BOOTSTRAP 0.1.11-dev、Unity Update、NETWORK_READY及DR_Start/DR_Logo；保留框架Class::Init substitute警告。
  已请求用户真实潜水，分别验证新鱼群、正常原游戏鱼的鱼叉/捕获调用、Disconnect与正常返航保存。
  随后真实A03_01_02记录49条Ready概要与53条鱼群状态；最多16条可反查/可渲染/镜头内鱼，网格662，资源/显示错误为0。
  原生交互42条事件配为21个CallId：HarpoonFire 28、FishHookedByProjectile 10、FishOnTakeDamage 2、SpecialFishOnTakeDamage 2；两个原bool为true。
  14条事件能在Drain反查原生目标，回调/解析/配对/原生查询错误为0；Win/Pickup仍无回调证据。
  用户确认出现与两戴夫偏移一致的成对鱼，抓一只两只一起消失；关闭鱼群显示恢复正常，并确认本地操作和镜头正常。
  MAP_SELECTION持续Selected route incomplete，路线清单未通过；自己的挂钩卸载与Disconnect已有日志，后来又有本机活动。
  用户随后明确确认主动退出且未返航，正常返航保存验收保持未完成。
  原始会话已归档development/.local/verification/fish-world-interaction-0.1.11；公开摘要保留上述部分通过和未通过边界。
- 更新接手/计划/世界/网络说明、核心与构建/原生摘要和配置Skill；Skill校验通过。
  默认发行包仍0.1.0。下一步核对真实调用，再接M4加载前地图/客机隔离、M5请求裁定与M6结算；双游戏及冷安装仍待完成。

## 后续日志格式

每次追加：日期、目标、关键改动、验证命令及实际结果、遗留问题、下一步。
## 2026-10-04 — 操作请求门禁、目标检查与路线输入诊断

- 前轮0.1.11-dev源码/85项测试、实机交互摘要和配置Skill已推送4a7ebb4，属于具体目标进展。
  用户本次确认主动退出且未返航；保持正常返航保存未验收，不将退出记为崩溃或保存成功。
- 协议升至4，新增6种鱼操作请求/结果模型、规范指纹、握手来源绑定、独立有界FIFO及guest outstanding核对。
  请求不被移动帧或世界快照覆盖；控制优先，动作/移动/世界公平轮转。双方须相同协议/Mod版本。
  Gate保留房间内ID高水位，重复请求复用结果、变化负载拒绝、业务拒绝消费ID、缓存淘汰/跨场景不允许重放。
  默认权限事实不可用；短派发租约需重新核对身份/装备/阶段，原生未知结果不重派发且不推断捕获/收益。
- F11新增Check selected fish target，Guest/Local test发送只读ProbeTarget；房主Unity线程反查当前epoch/编号/池代次与终态。
  通过只返回DryRunValidated、OperationId=0；实际地图权限、客机原生隔离、可信玩家/装备和效果桥仍不可用。
  未调用鱼叉、伤害、捕获、拾取或收益写入，第二戴夫的独立射击仍待实现。
- 独立MapRouteObservation在Transmit开启时入海前后最多1Hz读取直接字段，变化才记录MAP_ROUTE_INPUTS。
  读取不依赖Ready或完整路线已成功；上下文、cache/roadmap/first、候选层与加载场景分别报告。
  名称/条目/扫描有界，截断明确；完整选择仍严格校验，不调用随机选图/加载/存档写入。
- 最终评审发现取走请求后网络线程切换scene的竞态：旧Queued/DryRun发布原会误触协议失败并断房。
  会话锁内合法旧场景发送现在返回false，伪造身份/未来epoch/当前错误scene仍拒绝；主线程发布前重查并撤销旧许可。
  GUI捕获请求发布失败，过期接收结果不更新成功状态；场景撤销重置提示且保留房内请求ID。
  生命周期异常立即清只读身份；Probe/交互读取原生字段后再查代次，prefix解析前后查健康。
  同epoch切换诊断不清已发布世界revision，避免状态计数偏离会话版本。
- 初版同版本构建曾安装启动；该版已被最终评审后的构建替代，原始日志保存在忽略的.local/verification/fish-action-gate-0.1.12-initial。
  初版哈希86E08A272CED6BC1439DA36FA40501CA0E9CDECD36349BBC794FED23F5DF9BE7不能用作最终构建实机证据。
- Test-Core 109/109通过，包括规范/来源/去重/限流/权限/代次/未知结果、FIFO/伪造结果、真实TCP往返与take后切场景同房恢复。
  最终Build-Plugin警告视为错误通过；0.1.12-dev SHA256：
  `8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`。
- 确认游戏已退出后备份/部署，安装与构建哈希一致；实际Steam替换后的新进程启动于2026-10-04T07:21:37.0010213Z。
  新鲜日志确认BOOTSTRAP 0.1.12-dev、Unity Update、NETWORK_READY及4条初始路线输入；Class::Init框架警告仍在。
  当前只完成启动，未把主菜单路线值或CLR TCP等同实际Probe/潜水路线/双游戏验收。
  已请求用户潜水做目标检查，确认操作/镜头，再Disconnect、正常返航保存退出。用户现不方便试玩，三项保持待验；继续不依赖试玩的开发。
- 更新README、计划/接手/网络/世界说明、构建/核心摘要与配置Skill；公开范围只含本项目源码和已授权摘要。
  默认发行包仍0.1.0；继续加载前房主选图/客机临时状态及AI隔离、原生执行桥、完整捕获与返航账本和双游戏/冷配置验收。

## 2026-10-04 — 加载阶段即时复制路线与原始IGP选择

- 前轮0.1.12-dev已完成109项测试、部署/新鲜主菜单启动、日志/Skill，并公开推送b1629f6；属于具体目标进展。
  本轮重新核对工作树干净、已安装哈希8F90042C…；用户仍不方便试玩，不重新启动游戏。
  本轮源码0.1.13-dev只构建待部署，原生挂钩ABI、Probe、潜水路线和返航仍未通过。
- 研究生成包装器确认：没有证明全路线/所有IGP在任何资源加载前都已选完，不能用后加载清单假设一个统一阶段。
  SceneContext.firstData/GetSelectedMapLayerCached、IGPSetController.IsInitDone/GetSaveableInterface会runtime_invoke；直接字段代理与主动getter区分。
  私有元数据与研究笔记继续留.local，不提交游戏或生成互操作DLL。
- 新增共享MapRouteSelection和ValidateRoute/CopyRoute/FingerprintRoute：完整3..32场景的唯一ID/名称、有限坐标、双向全链、文本边界及规范深复制。
  route-only指纹为map-route-v1，完整map-selection-v1原字段顺序及IGP约束不变，路线候选不能充当完整世界权限。
- MapSelectionCapture新增指定SceneContext的主线程即时读取，直接复制cache/roadmap/first及连接/偏移。
  末尾重查列表数量/指针与入口以拒绝清空/替换；不含IGP、不要求已加载，也不缓存为新世界许可。
  已加载完整读取复用同实现并保持原所有加载/IGP/注册/两帧条件；两处IsInitDone改直接backing字段。
- MapSelectionHooks观察五处自然调用：cacheSelectedScenePath和LoadSceneMapCacheFromSave postfix、GetRandomIGPSetInfo原__result postfix、
  IGPSetInfo.LoadPrefab工厂prefix、static SceneLoader.LoadSceneAsync(string,LoadSceneMode,bool) prefix。
  MethodInfo精确检查声明/参数/static/返回，异步句柄只反射核对类型；callback全部void/by-value，不跳过原方法或改参数/结果。
- 新MapSelectionHookCapture在确认Unity线程的原回调内即时复制，只将纯CLR路线/选择项/资源key排队。
  非主线程不读任何Unity字段/帧/name；每进程1024调用、队列64、每Update消费16，截断/缺项/错误明示。
  先保留原始IGP返回项再读取可选控制器地址，地址错误不会丢失瞬时选择；Prefab工厂不作为实际请求或加载完成证据。
  observer使用独立读取器，候选错误不会重置已加载manifest稳定窗口；copy错误/丢弃/线程统计跨开关保留。
- F11新增默认关闭Observe map selection calls，独立于TCP和Transmit，可在入海前启用。
  MAP_SELECTION_HOOKS_READY / MAP_SELECTION_CALL / MAP_SELECTION_OBSERVER_STATE / MAP_SELECTION_HOOKS_STOPPED提供安装、值与卸载证据入口。
  Disconnect关闭并清理自己的observer；回调/卸载失败锁存到进程重启，只卸自己的Harmony Owner。
- Test-Core 112/112通过；新增路线非完整manifest、链/边界、深复制/culture/顺序/±0和既有完整指纹黄金值检查。
  最终Build-Plugin警告视为错误通过，SHA256：
  `E5013FB314A17D618F50AF0D8FA3DFB35CCD161DF069703D2759D92FC0CFCCA3`。
  新版未部署/启动/安装原生挂钩；实机最近启动保持0.1.12，最近完成潜水证据保持0.1.11。
- 更新开发/接手/网络/世界文档、配置Skill和构建/核心摘要。默认发行包仍0.1.0。
  后续验证原调用顺序与临时缓存，再实现分阶段房主选择传输/采用、客机临时状态/AI隔离、实际捕鱼和返航账本及双游戏/冷安装验收。

## 2026-10-04 — 分阶段房主地图选择候选传输

- 前轮0.1.13-dev共享路线/自然调用观察、112项测试、日志和Skill已推送4691e5d；本轮继续目标开发。
  用户当前暂不方便试玩，0.1.12-dev手动Probe/潜水路线/返航保持延后；本轮未启动或部署游戏。
  已安装DLL复核仍8F90042C…，最新启动证据保持0.1.12，最近完成潜水证据保持0.1.11；未知退出不归因崩溃/保存。
- 源码0.1.14-dev、协议5：新增MapRouteSlice/MapIgpChoice/MapChoiceRetire三个单载荷。
  路线每片8场景、最多4片；全路线连续/唯一/有限字段及完整指纹校验后原子提交，首个新代次分片立即撤旧候选。
  IGP按当前候选代次和连续revision发布，同组更新保留最新项，128组/128条历史，深复制隔离。
- SessionMachine/SessionPeer新增房主发布、客机接收的独立地图通道，WaitingForScene/epoch0可传；不提升Ready/世界权限。
  地图FIFO32包；控制/心跳优先，动作/移动/世界/地图四路公平。所有路线分片先复制并编码校验，再取消旧批并提交新代次。
  溢出明确Retire，清未发地图包；控制撤销优先于后来新代次，inactive重复不同reason不发第二次冲突撤销。
  合法旧候选发布false，未来代次/当前错指纹/方向/房间/坏schema拒绝；只Close清本房所有地图状态，普通scene切换保留候选。
- MapChoiceController将已经复制的自然回调接入候选通道。cache/restore即使同hash也建立新代次；同hash普通SceneLoad去重。
  无route/controller绑定的原始IGP与Factory事件只计Unbound，不缓冲到后来路线；原生wrapper不进入网络/队列。
  callbackFloor与最高序号跨关闭/BindRoom/房间保留；复制丢失、非Unity回调、ReadError或selection截断主动撤销并抬屏障。
  评审修复IGP/SceneLoad不可用时旧候选残留，以及已观察组后续null/unknown原结果仍保留旧prefab的问题；撤销后需新的自然route才能重开。
  Guest关闭自己的本地observer保留收到的房主候选；房主Retire才撤销远端。NETWORK_STATE/F11状态及MAP_CHOICE日志提供进度。
- DTO抽出为唯一MapSelectionCallObservation.cs；Test-Core和测试csproj编译生产MapChoiceController及DTO，仅使用测试logger。
  新增8项schema/assembler、10项协议/会话/TCP、4项真实adapter/TCP用例；输入是合成的已复制CLR观察，不触发Unity或原生回调。
  首次测试编译缺Pose命名空间引用已补正；最终134/134通过，覆盖回调屏障/跨房间、无预路线缓冲、同hash新代次、连续修订、
  截断/读取失败/同组空结果撤销与同房恢复、guest本地observer关闭、源/方向/房间/复制所有权及FIFO/四路公平/旧协议拒绝。
- 最终Build-Plugin警告视为错误通过，SHA256：
  `AF3CB8BE38E0402ECBA173F86C3C458CC82D6248664E16A35E58FC08729766D0`。
  摘要见map-choice-transport-build-verification.json和core-verification.json；原始编译/测试输出只留.local/verification。
- 严格边界：MapChoiceSnapshot始终ObservationOnly/HostSelectionApplied=false，不构成完整IGP清单、scene epoch或世界采用许可。
  callbackFloor只能排除已排队旧观察。新route边界后迟到的旧原生controller若同名/同地址，仍可能贴当前candidate generation；
  NativeGenerationBound=false，需补本地来源代次与跨机地址证据才可用于采用；Factory不是资源请求/完成。
  自然原生ABI/回调/卸载、新版画面、地图采用、客机AI/临时进度隔离、实际独立鱼叉/捕获/收益、正常返航及双游戏均待验证。
- 已更新开发/接手/网络/世界文档、构建/核心摘要及配置Skill；Skill校验通过。默认发行包保持0.1.0。
  下一步验证原生来源与自然选择顺序，再实现加载前房主选择采用/客机隔离和实际捕鱼/结算；用户方便后执行延后的实机验证。

## 2026-10-04 — 房主与员工、每人独立背包的玩法约定

- 0.1.14-dev地图候选通道、134项CLR/TCP测试、编译/日志/Skill已推送125e671；source与构建哈希本轮不改。
  用户提出需要关联抓到的东西，并希望房主掌主动权、另一个人为员工；随后明确每个人应有自己的背包，避免共用容量太小。
- 最终方案采用每人独立容量、重量与负重；房主自己的原生LootBox，员工由房主Mod持有的独立会话袋。
  两人所得正常返航汇入房主仓库，长期任务/图鉴/经济归房主；员工本地进度不合并。例子20kg+20kg仅说明容量规则，非固定配置。
- 新增CREW_MODE.md，写明权责、ExpeditionId/MemberId/操作/来源/CaptureId/每袋修订/ReturnId、捕获事务、分流与逐条入仓。
  两人竞争同一普通鱼仅一个事务；品质、肉量、数量和随机追加产物取房主可信结果，不按原鱼TID或客机申报猜。
  员工前置容量检查与物料产出都要归属绑定，不能先加房主袋再复制/扣减，不能提高房主容量或AddIgnoreOverloaded冒充独立袋。
  房主袋原生入仓不补奖；员工此前未入仓的条目需新结算桥按每个产物确认一次，部分未知不能重放整批。
  潜水账本寿命独立网络房间/scene清理，员工断线保留已确认袋；新player2/昵称不能自动继承旧操作权。
- 三路只读评审核对：当前角色只有显示，employee独立actor/loadout/氧气/HP/投射物与全部自动持久写入隔离仍待实现。
  当前鱼来源仅房主场景，初期同层带队，独立跨层需扩展多场景来源。未知原生结果保持pending，不退款/补Add/猜恢复。
- 新离线元数据研究仅放.local/analysis。已发现产物AddDropItem_Impl/Plus、LootBox.Add、图鉴AddCaughtFish、任务UpdateMissionIntCondition、仓库AddFromLootBox等候选；不是已验证控制流。
  LootBox重量/容量/Box属性getter调用原生方法，direct float字段候选和Slot ObscuredInt读取仍需实测；GetDropItemID参数tier不能当grade。
  原生产物可有随机追加，不能重新roll；Slot没有已发现的(id,count,grade)便利构造；CommitDiveLootDataOnlyJungle不作普通海洋返航入口。
- 同步PLAN/HANDOFF/WORLD_SYNC/开发约定、README链接与配置Skill；这是设计改动，未新编译、部署或启动游戏，不增加任何原生通过项。
  已有134项测试与0.1.14构建证据仍原样保留，Skill和文档一致性另作校验。下一步先建立可信分流/员工袋账本与入仓桥的证据边界，继续M4接管/客机隔离和M5/M6实作。

## 2026-10-04 — 每人独立Cargo账本与Loot/返航只读观察

- 用户再次明确背包是“每个人的”，继续按房主原生袋和员工独立Mod袋开发，不共用容量或负重。
  源码升为0.1.15-dev，协议保持5；本轮未部署或启动游戏。安装DLL复核仍0.1.12-dev/8F90042C…，最新潜水证据仍0.1.11，手动验证按用户要求延后。
- 新增Core/Cargo的CargoTypes/ExpeditionCargoLedger。Expedition与两个Member分别关联容量、重量、预约重量、袋修订与请求高水位。
  完整房主确认产物先形成候选计划，绑定来源Room/epoch/实体/本地代次、RequestId/OperationId/Member/玩家与产物指纹。
  256个捕获/每捕获8产物；深复制、规范GUID/文化/正负零、有限数值与批次检查，不淘汰旧重放屏障。
  未知随机产物不能预先猜成计划、重复随机或用物种TID冒充完整产物；尚未接游戏潜水生命周期或网络袋清单。
- 容量预约按Member执行；HostNative必须重新核对可信原生总重量与当前袋修订，EmployeeVirtual不挤占房主容量。
  Enter再查新鲜操作/来源/个人袋事实，只有明确未进入才释放预约；已进入未知保留来源和重量屏障。
  Host receipt取当前原生总重量，不把确认产物重量再次相加；员工必须有真实完整产物、容量路由、分流、无房主袋写入与捕获终态证据。
- 评审补强两个命名空间/时间问题：首次来源Room绑定跨Disconnect保持，无已验证映射不允许新Room重编号绕过；
  同Room换epoch仍有Reserved/EnteredUnknown时保守拒绝新epoch捕获，防止guest pause换epoch但原鱼未换绕过去重。
  原生重量按Member采样时间和当前BagRevision核对，旧样本不能覆盖较新baseline，同重量的新样本也推进时间与袋修订屏障，防止同帧旧读数覆盖。
  断线撤销新进入能力，仍可核对已进入操作的迟到产物；历史delta需重绑新鲜原生总重量，不能用旧总重退回袋状态。
- 返航冻结唯一ReturnId并停止新预约。Host仅记录原链入仓，拒绝员工式新增物料租约；Employee按CaptureId/ProductIndex一次租约、进入、实际入仓与保存分别确认。
  部分成功不重放整批，Abort不冒充正常返航或释放未知；员工已断线仍保留确认货物/逐项状态。
  CompleteReturn只针对跟踪产物，NativeBagInventoryComplete=false；返航Member袋视图用于审计，逐项状态看ReturnItems，尚非实时袋/负重UI。
  这是caller-serialized内存账本，未有原生执行、存储共同事务或跨崩溃恰好一次保证。
- 新增LootObservationHooks/Capture/Controller，Plugin/NetworkDriver/NetworkController接默认关闭ObserveLootCalls和F11开关。
  独立TCP/Transmit/Ready，精确观察鱼AddDropItem_Impl、LootBox.Add、AddCaughtFish(int,int,bool)、IngredientsStorage.AddFromLootBox四处自然方法的八个前后回调。
  原参数/结果不改，不跳过原方法；Unity线程内即时冻结纯CLR值、原生重量/容量直接字段，消费日志不再解引用wrapper。
  鱼prefix仅冻结自身身份，不将嵌套Add/图鉴/入仓按时间/线程猜归属；slot加密字段与Func/GetTimes内容不读取。
  所有ActualBagDeltaProven/SourceOperationBound/CaptureSuccess/StorageDeltaProven始终false，观察不提交Cargo receipt或打开分流/奖品权限。
- 进程1024前后事件、queue64/context128、每Update消费16。统计/错误锁存跨开关保留，正常重开另建copy队列。
  Stop/Disconnect/配额停止只卸自己的owner；丢弃与OwnHooksRemoved明确记录，停止或队列满可能截断链，不声称观察完整。
  评审补Stop卸载失败状态，失败不继续显示read-only。新F11布局与四处原生ABI/实际读取/卸载均待实机。
- Test-Core 142/142通过，新增8组Cargo夹具覆盖独立容量/超重/重量、操作来源与产物替换、去重/取消/256限额、
  断线迟到receipt/跨Room和epoch围栏、旧weight/BRevision、Host总重不双计、逐项返航部分成功、pending/Abort与深复制/畸形批次。
  夹具使用synthetic房主事实，不运行原生桥。最终Build-Plugin警告视为错误通过，SHA256：
  `1C03DC606FA3DFD218D5288C9780A4C0E226CF38E45CC078DA011AC1A51AD71D`。
- 更新core-verification和cargo-ledger-build-verification摘要、README/AGENTS/PLAN/HANDOFF/MULTIPLAYER/WORLD_SYNC/CREW_MODE与配置Skill；Skill校验通过。
  0.1.14的134项/旧hash保留在map-choice-transport-build-verification，原始输出仅.local/verification，默认发行包仍0.1.0。
  下一步验证完整产物/容量路由/持久副作用与正常入仓链，再接Expedition生命周期、员工分流/入仓桥，继续地图采用、客机隔离、独立actor和真实双游戏闭环。

## 2026-10-04 — 原GameAssembly离线调用工具与真实接入点

- 继续按“每个人的背包”规则，保持0.1.15个人Cargo账本与原生能力未接通状态。
  本轮修改离线工具、文档及Skill，没有修改/重新编译插件，未部署或启动游戏；历史142项测试和0.1.12安装/0.1.11潜水证据保持原范围。
- 新增Inspect-NativeCalls.ps1及自写NativeCallInspector.cs。读取本机原GameAssembly/metadata，不加载或调用游戏，不读存档。
  使用已安装LibCpp2IL/Iced和独立net6进程、SDK5 Roslyn3.11警告视为错误编译，不下载依赖或发布原DLL。
  Steam自动定位、精确Namespace.Type::Method、原metadata方法指针、按命名关联iterator MoveNext；泛型实例/共享别名仍未知。
- 解析283291方法定义、421353个PE runtime-function项，parser识别metadata31.1。
  初版只含首个unwind片段，不能据缺边推断无业务；修为version1 CHAININFO完整三元组父链，严格匹配原.pdata、循环/32层检查，拒绝按相邻代码猜归属。
  3个不支持版本明确计数；无表项/内部/次级入口不猜主体。仅唯一文件映射可执行片段，32片/262144字节与方法/指令配额。
- 5组本机静态报告为Loot21、Expedition211、地图59、GuestSave56、加载owner19条方法记录；根收齐且方法限额未触顶。
  partial、不可用、indirect、外部跳转、alias截断均保留；报告之间可重复方法，不能作为独立接口或完整可达调用图总数。
  真实边表明鱼产物进入Add/IgnoreOverloaded及AddLootingSaveData，袋Add还有CheckOverloadedState/Add_Impl；员工容量、产物与水下进度需同归属分流。
- AddFromLootBox的三片代码包含IngredientsStorage六参数Add；Normal有Result/Finished工厂，Result按鱼肉/鱼卵/采集/关键/种子/装饰分路。
  保存基类有Serialize/加密/目录/文件写；加载有云/转换/复制/删除；SetLoadedData含互动同步。
  这些仅static direct targets，不能证明实际分支/产物增量/正常返航或写盘成功，不能开放员工执行权限。
- map额外报告定位coLoadAdditiveScene与CoLoadSceneAsync.MoveNext直接走Addressables五参LoadSceneAsync，绕过现SceneLoader三参观察。
  需工厂固定iterator owner、每MoveNext恢复、显式子协程继承，继而operation指针/版本→成功Scene.m_Handle→controller寿命；不读当前singleton倒推旧协程。
  cacheSelectedScenePath还有持久cache写目标，IGP.Init仍有保存接口，地图postfix或只跳随机不足以隔离guest。
- 复核修正限额后HashSet仍增长与interior跳转归属边缘；新工具按已知整个family判断外部跳转，generic context不解。
  每次GUID编译目录、原文件前后hash、同目录临时文件原子替换，失败保留旧报告且报错，旧报告不算fresh。
  实际检查MaxMethods1/OmittedRoot1/MaxInstructions64、未知选择器失败且旧报告hash不变、Depth4参数拒绝均通过。
  独立PE复核Loot/GuestSave共93个报告片段，parent三元组/最终root正确，BadChainClaims=0、实际最长链3层；没有原生运行验收。
- 新增NATIVE_ANALYSIS与精简验证JSON，更新AGENTS/README/PLAN/HANDOFF/GAME_API/CREW_MODE/WORLD_SYNC及配置Skill。
  原报告、地址明细、解析日志与检查输出留.local。下一步按已定位入口接实际origin/guest隔离、个人分流与逐产物返航桥，手动与真实双游戏验证继续等待用户方便。

## 2026-10-04 — 0.1.16 加载来源适配器与迟到回调撤销

- 继续遵守用户确定的每人独立背包、容量和负重；个人Cargo账本保持原范围，未接实际员工捕获/返航桥。
  本轮源码升0.1.16-dev、协议仍5；未部署或启动游戏，当前安装0.1.12、最近潜水0.1.11及默认发行包0.1.0均保持原证据范围。
- 新增MapOriginRegistry/Hooks/NativeCapture/Controller及Inspect-MapOriginApi.ps1，默认关闭Network.ObserveMapOrigins。
  原游戏29声明自己的前后/finalizer观察，精确匹配参数/返回/static，不跳过原方法，不改变输入、返回或游戏/存档。
  自然GoToInGameEntry固定owner，通用CoChange和子factory固定原iterator，每MoveNext恢复同scope，未知/退休scope遮父。
- Addressables精确五参typed原handle绑定操作指针/版本及string key；主线程保留wrapper最多64，直接version/status/result前后核对后取真实Scene.m_Handle。
  manager须有确切operation→scene归属，否则iterator固定unbound；controller出生及选择可pending，等待同handle的原操作结果，不从singleton或同名场景补来源。
  排队观测/Core仍只有CLR值；没有completion delegate、原计算getter或延迟队列native解引用。
- 8192进程事件/context256、观测queue64/每Update16，Core有界owner/iterator/operation/Scene/controller/choice/scopes及退休围栏。
  新entry、重复同指纹cache、Context清理、unload/destroy及操作失败撤销；无controller出生的实际卸载也留tombstone，晚完成不得复活。
  线程/读取/配对/丢失/限额失败撤所有证据，CopyCallback同步阻断缺失nestedprefix后的借父scope，Update先健康核对后消费。
- 原方法异常按固定controller/owner撤证；pending Init iterator另固定iteratorLife→ControllerLife，owner0也能撤其已观察选择。
  旧route postfix只有Accepted才登记Context owner，不能抹新entry映射；只卸自己owner并记录清理是否验证，失败锁存要求重启。
- Controller独立TCP；每次新registry使用RunId隔离life编号，日志新增MAP_ORIGIN_HOOKS_READY/CALL/BOUND_CHOICE/OBSERVER_STATE/OBSERVER_WARNING/HOOKS_STOPPED。
  ScalarOriginChainMatched仅本机CLR关系匹配，NativeGenerationBound/TypedReturnABI/HostSelectionApplied/WorldAuthority/CargoAuthority仍false。
  此源不接旧MapChoice generation，不授原生执行/员工或Cargo权限，M4/M5/M6和双游戏仍未完成。
- 同版本框架本机IL/官方source核对typed返回buffer和void finalizer路径；不把框架支持、wrapper签名或安装注册当本游戏原生ABI/完整路径验收。
  SDK5 Roslyn3.11/net6引用编译警告视为错误通过，最终自写插件SHA256：
  `977D090A290F168110AA4D9DE618F84A56954FB94F58F14A49FEA54416507AD8`。
- Test-Core 148/148通过，新增6组synthetic MapOriginRegistry夹具覆盖fixedscope/unknown遮父、bootstrap/迟结果、重放围栏、pendingbirth与未知unload、重复cache/失败操作及线程/读取/lifo/限额撤证。
  夹具不运行NativeHooks/Capture/Controller或游戏；原始Build/Test与元数据/框架研究只留.local，公开摘要见map-origin-build-verification.json。
- F11增加载来源开关，面板按屏幕尺寸缩放并恢复GUI状态，实际画面待验收；更新README/AGENTS/PLAN/HANDOFF/GAME_API/MULTIPLAYER/WORLD_SYNC/CREW_MODE/NATIVE_ANALYSIS及MAP_ORIGINS、配置Skill。
  下一步用户方便后验证fresh入海实际嵌套、typedreturn/Scene值/__state、操作与出生时序和关闭自身挂钩；同时继续准备guest全部持久副作用隔离及房主采用、个人分流与逐产物返航。

## 2026-10-04 — 0.1.17 固定来源候选发送与客机影子桥入口

- 用户指定每个人独立背包、容量与负重，继续按个人Cargo账本开发；本轮未接员工原生捕获/分流/入仓。
  源码升0.1.17-dev、协议保持5，未部署或启动；安装0.1.12、最近潜水0.1.11和默认发行包0.1.0仍为原证据范围，手动试玩继续延后。
- MapOriginRegistry新增TryCaptureSource，在登记线程owned复制当前route与每个活controller最新有效选择，不消费诊断ready队列。
  owner0健康空清单、活owner尚无route保持待定；pending/unbound/退休不输出，fault/冲突/错线程原子失败，不复用旧快照。
  Controller新增带RunId/Healthy的MapOriginSourceFrame，Unity线程前后核对健康；NetworkController接到每帧候选发布。
- MapChoiceController唯一发布来源改为ObserveOrigin；原MapSelectionCallObservation仅计数/水位/诊断，关闭或丢失不撤固定来源。
  4参BindRoom保存实际Run/ActiveOwnerLife floor，建房前entry即使路线迟完成也不能提供来源；新自然owner同指纹仍新wire代次。
  64 Run退休与owner高水位跨Clear/换房保留，旧Run重放不影响新Run；新pending-owner可补route，失败Run/owner不得复活。
- 每帧整份owned清单严格复制/校验，完整owner/context/op/真实Scene/controller标量链、路线指纹与组地址冲突整体拒绝。
  缺key或controller替换先Retire旧wire代次，再发route和完整当前清单；每owner controller历史256不随wire重建清掉，旧life不可回放。
  wire选择最多128，超限不截取；每帧最多8条改变项，余项留owned清单，FIFO32取消/满撤源封owner，不下一帧重播。
  Guest停本地origin保留Host收到候选；普通角色epoch不替代native来源。日志OriginRun/owner/Pending/LegacySuppressed已接，F11仅状态。
- 六组Core快照与六组actualTCP来源夹具新增；原四项源适配已迁移固定来源合同。初次159/160中pending测试Choices=null不符Core空数组，修夹具后160/160通过。
  同时修正常关闭观察器的EmptyRun/Healthy=false不逐帧累加Invalid；真实Healthy/Empty仍拒绝，新增计数断言覆盖。
  夹具编译实际Core/MapChoiceController/DTO，只替代logger，所有来源事实为synthetic；未执行NativeHooks/Capture/Unity provider或两个游戏。
- 最终Build-Plugin警告视为错误通过；末尾空白清理后再编译，SHA256：`B14381625A27B82396D6C474B4B6DACB0DA8DB12ACB7D6CCF07AD79C7C22E1C3`。
  全部NativeGenerationBound/HostSelectionApplied/GuestStateIsolated/WorldAuthority/CargoAuthority保持false；候选不证明完整manifest、真实原生来源、采用或捕获。
- 新增可复现Inspect-GuestStateApi.ps1，实际离线读取7程序集/42类型/46 typed根与saveable引用/178持久输出签名候选，PowerShell解析错误0。
  SaveDataBase.Serialize/Deserialize泛型和Game/Player Data、Interaction直接字段交换候选已定位；string ver不是JSON构造器，SetLoadedData/Load有同步/转换/云副作用，不作纯恢复。
  原PE clone静态报告6选择器/8根/90记录，54有已知unwind族、36范围不可用不猜leaf；根遗漏/指令截断0，40别名截断边。
  不证明具体T分支、Obscured覆盖、深复制/旧缓存引用或全部writer，未调用原生clone/根交换/恢复，未读改存档。
- 新增ORIGIN_MAP_TRANSPORT/GUEST_ISOLATION，更新README/AGENTS/PLAN/HANDOFF/MULTIPLAYER/WORLD_SYNC/GAME_API/CREW_MODE/NATIVE_ANALYSIS和Skill；Skill校验通过。
  当前摘要core-verification及origin-map-transport-build-verification，离线摘要guest-shadow-analysis-verification；0.1.16历史148/hash保持不变，原日志/报告留.local。
  下一步实现有输出围栏的客机shadow准备/安装/核对/恢复及运行缓存切换，继续真实地图采用、个人捕获/入仓桥；用户方便后再验证真实来源链与双游戏闭环。

## 2026-10-04 — 0.1.18 实际原生根桥与已枚举输出围栏

- 前一goal turn为具体进展：0.1.17固定来源候选发送已提交并远端核验。继续完整M3至M7目标，不把CLR或准备代码当完整联机；每人独立背包、容量、负重规则保持。
  本轮源码升0.1.18-dev、协议5，未部署/启动/调用游戏或读改存档，安装0.1.12、最近潜水0.1.11、默认包0.1.0保持原证据范围，手动验证按用户要求延后。
- 新NativeGuestShadowBridge实现实际typed四Data native Serialize/Deserialize、temp Interaction(false)、五个direct根的身份/readback/单次安装与恢复，不调用公共SetLoadedData/Load/Sync。
  强保留SaveSystem、四manager、五original及五detached，最多15 explicit IntPtr gchandles，read_target核对；仅free自己的句柄，不触wrapper私有句柄。
  只有首次实际fence attempt才CAS强保留整个backend，pre拒绝不占global slot；unknown restore保留backend与handles，无自动finalizer把保存放开。
- 四manager IsNewData及原Data版本/时间/dirty/corrupt直接标量核对变化就拒绝，不强行清回旧dirty位。这不证明private树/缓存/旧协程从未修改。
  每根8Mi、lease16Mi UTF16代码单元，JSON仅局部内存不日志/协议，native返回后才查长度，不能约束native初始分配；constructor string ver不是JSON克隆。
- Core/Guest/GuestShadowTransaction固定backend lease/thread/来源，fence-before-clone、step前后fresh核对，逐root逆序readback补偿，Foreign/Unknown不盲写。
  false/throw可能已进入写入，精确Original回读可解决结果但不抹fault；restore/unpatch/free未知不再派发，quiescence或fence不可靠保留引用。
  生产CanEnterBoundary与HasQuiescentBoundary当前恒false；前置拒绝在InstallFence之前，startup primitive自身也fresh拒绝。没有Network/GUI或Plugin自动实例化/调用，不运行root swap或锁用户正常保存。
- 新GuestOutputFence/TargetManifest为194 exact声明：130declared排22open-base和2service-interface，纳88个四closedBase展开。strict owner/static/params/return，source清单与fresh离线报告194逐项相等。
  活动prefix设计跳writer原方法，boolfalse；8TryLoad typedout置null，Injected输入ref不变；Steamasync=0、stream=UInt64.MaxValue，Toolbox Save/Delete Failed=2/1，default0是成功不能用。
  failure值预建CLR而非调用native struct ctor；只卸自己的owner，unknown/其它线程/partial安装失败保持阻断并失健康。
  194不代表全部writer/唯一native地址/已安装detour；sharedgeneric、ref/struct ABI、在途输出、具体service实现及System.IO其它路径仍未知，没有安装或观测真实阻断。
- 新Inspect-GuestOutputApi.ps1实际Cecil离线执行5生成程序集+runtime IL：130declared/88closed展开/MissingNames=[]；GC handle IntPtr/strong false语义与同commit官方源码核对。
  原PE旧静态边仅引用旧hash，不冒充本轮重验原文件；原报告、IL和binary只留.local。两个新研究/实现doc说明完整cache/Interaction/writer/quiescence仍必须完成。
- Test-Core167/167通过，新增7组actual生产事务＋instrumented synthetic backend：fence前零操作边界拒绝、partial/throw补偿、unknown不retry、foreign/manager变更、fence/quiescence保留、thread/lease/reentrant和cleanup一次。
  不执行native bridge/hooks/Unity，不能作为实际克隆/写入/保存隔离或双游戏验证。最终Build警告视为错误通过，SHA256：`E3757E62DAC6DAB4AE715DE5A1D01D6A65E578EB92493C72567BB8724ED4EBF0`。
- 新guest-shadow-build-verification和更新core摘要，0.1.17历史160/hash保留；同步README/开发README/AGENTS/PLAN/HANDOFF/MULTIPLAYER/WORLD_SYNC/CREW_MODE/GAME_API/NATIVE_ANALYSIS/GUEST_ISOLATION与配置Skill。
  Skill正式草稿校验通过并同步；新PowerShell工具解析错误0。最终独立只读评审未发现本轮阻断，确认进入拒绝在原生调用/patch之前；新鲜进程检查0，已安装DLL的SHA256仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`。
  下一步实现可信原生pre-generation/退出静止边界、Interaction/运行缓存切换及完整输出覆盖，随后受控实机原生准备/恢复；继续实际房主地图采用、个人捕获/入仓、双端完整闭环与冷配置发行。全部native权限false，goal仍active。

## 2026-10-04 — 0.1.19 交互缓存准备与已知共享引用检查

- 前一goal turn是实际进展：0.1.18 typed根桥/输出fence已提交a31195e并核验远端。完整M3—M7保持active，不缩减双实例世界/捕获/返航/冷配置范围；独立个人袋规则保持。
- 新typed Interaction helper接入根桥PrepareDetached及已知安装/验证，十direct容器映射+新HashSet，拒原dirty与已知baseline差异，mutable容器/非空arrays/7records/货槽已知图走bounded审计；不调用Sync/SetLoadedData/Load，不写原manager或root作准备。
  原known基线在CaptureOriginal的native serializer前捕获，Prepare必需原绑定baseline且重查，避免把serializer后的改动当原状态；cleanup独立只读核对原known图。严格准备校验与active当前图校验分离，允许detached合法值/版本/dirty变化，原known图及父身份保持；不升全图权限。
  Reader累计16384个storage visits（包括free/tail/bucket），不能凭4096个引用计数忽略大量空尾工作。实际Interop WrapElement会il2cpp_value_box，所以字段/数组只读检查可分配临时native box/框架句柄，并非仅CLR分配；未执行这些读取，不声称精确box数或零native allocation。
  CLR引用审计允许一侧内部共享，跨原/新任何已读节点共享锁存；每侧4096含重复次数、original pass关闭后拒late原引用。三个新生产helper fixture覆盖共享child/跨分支、己方共享及空/0/late/重复工作限额，总170/170通过；不执行native图/容器/field读写。
- Build新增实际Il2CppSystem.Core引用，不复制第三方DLL。首编译CS0576：Interaction别名与游戏global类型冲突，改为PlayerInteractionCache后最终警告视为错误编译通过，SHA256：`778F9E402C4B65731164F97600882C4F8CAC72B20EC6490C92E0C7B34AF9F6A5`。新helper未被native执行，原生entry/quiescence仍false，没有Network/GUI或自动调用，未部署/启动/读改存档。
- 新runtime-cache Cecil脚本实际离线3程序集、25类型/6异构InGame派生/457引用声明/5collections；新Depth1原PE cache分析11roots/78方法、无quota/遗漏，GameCodeExecuted=false。
  entry静态前部可UpdatePlayer/Mission/Clear，下游scene hook已偏晚；Ingredients.Init与Mission.Build触及当前存档时间/任务，不作纯clone。LootBox getter没有可交换m_Box backing；DateTime是真CLR struct、Dictionary.Entry是native ValueType wrapper，不能把后者当无引用leaf。
- 新GUEST_INTERACTION_SHADOW/GUEST_ENTRY_BOUNDARIES/GUEST_RUNTIME_CACHES及可复现Inspector；更新当前验证摘要/core、README/AGENTS/PLAN/HANDOFF/相关接口/玩法和Skill。两个新Inspector及修改后的Build脚本解析错误0，Skill草稿校验通过。新鲜游戏进程检查0，已安装DLL哈希仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`；旧0.1.18的167/hash及installed0.1.12/dive0.1.11/distribution0.1.0历史保留。
  两次独立只读源码评审无本轮阻断，确认序列化前baseline、严格/活动验证分离、baseline失败撤销缓存的KnownDisjoint，以及卸fence后的原图确认路径；不把评审或编译当原生运行证据。
  下一步必须证明真实进入/静止、其它typed运行缓存与旧引用、完整writer/在途工作以及native clone/恢复ABI，再接房主实际地图采用、员工容量/捕获分流、逐产物入仓、双端玩法与冷配置发布。已知绑定/别名不升完整baseline/graph或GuestStateIsolated，手动验证继续延后。

## 2026-10-04 — 0.1.20 食材缓存准备与六步补偿

- 前一goal turn为实际进展：0.1.19 typed交互/已知图检查已提交8e53865且远端核验。完整M3—M7保持active；每人独立袋/容量/负重、房主长期收益及用户手动延后规则保持。本轮源码0.1.20-dev、协议5，未部署/启动/native调用或读改存档。
- 新NativeGuestIngredientCache实际typed捕获/准备/逐字段readback与恢复，接rootbridge第六步。两原known基线均在任何serializer前，独立dictionary/IngredientsData/counts与Entity13实例字段副本；保真原key/loaded/data，原Storage=null拒绝Prepare，不用Storage.Init/Load/Reset或Parent业务getter补值。Parent/static资源与完整cache仍unknown。
- Core新增IngredientsCache/OwnedMixed（仅6th），原五SaveRoots与六步最终确认分离；snapshot/attempt扩六、四Data标量仍4，explicit强handle上限18。partialpair先逆序处理cache，foreign/unknown不覆盖，已进入unknown恢复仅read不retry；缺cache确认不能free/unfence。默认CanEnter/Quiescent仍false，未接Network/GUI。
- Test-Core首次CS0649：fixture OriginalLoaded没有显式初始化，改false后174/174通过。四新增生产事务夹具用双独立CLR字段模拟partial install/restore、未知不retry、foreign/替换singleton、相同bool/knownnull与single-root拒Mixed；不执行nativehelper/setter/constructor或图扫描，不能证明原生逐字段行为/ABI。
- 实际Build警告视为错误通过，SHA256：`4FAA8DA75579A59CFCF113676BAFCFCED651C3C3D15D97C091DD8E5A42170B61`。新Inspector实际离线4程序集/24类型/5继承层/13实例字段/3static资源/3Parent mutable候选/5closed声明，无missingtypes；没有运行constructor/getter/save，原报告只留.local。
- 更新README/开发AGENTS/HANDOFF/PLAN/相关文档、core及guest-ingredient-cache-build-verification，Skill正式校验通过并同步；0.1.19历史170/hash与installed0.1.12/dive0.1.11/default0.1.0保留。validator首次缺PyYAML、补现有缓存模块后默认GBK读取失败，使用现有模块及Python -X utf8后通过，无下载或安装依赖。Inspector PowerShell解析错误0。
  两次独立只读末审无本轮阻断，确认字段/18显式handles预算、非递归窗口、faultserial每读写前后即时停止、null/loaded保真及全六步确认；不作为native运行证据。新鲜进程检查0，安装DLL仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`。已枚举字段/共享引用审计不授完整baseline/资源/caches权限。
  下一步真实自然边界/其它typedcache/旧引用/全输出，随后房主地图采用、个人产物与容量分流、逐产物返航和双端/冷配置完整闭环；完整goal保持active。

## 2026-10-04 — 0.1.21 临时状态表与七步恢复

- 前一goal turn为具体进展：0.1.20食材缓存已提交3f636c4并远端核验；完整M3—M7目标保持active。用户再次明确“每个人的”，每人独立袋/容量/负重、房主唯一长期进度不变。本轮未部署/启动、调用游戏或读改存档；安装0.1.12/潜水0.1.11/default0.1.0保持原范围。
- 新NativeGuestIngameCache接原生根桥第七步，捕获实际singleton/table与已知记录，有限支持六种record schema；非空助手资源、运行设备子图缺独立构造时明确拒绝，不共享/清空原值。Exact native class/key与普通record object_new+IntPtr为未运行候选，不把Unity ScriptableObject当普通record分配。
- Capture三份known原图闭合先于serializer，Prepare三份strict闭合后才可安装；七步/21显式handles/4Data stamps，恢复7→6→Save5。第7单字段拒OwnedMixed，foreign/unknown不覆盖，进入后未知只读保留而不重派发；进入/静止及全部玩法权限仍false，无Network/GUI激活。
- Test-Core176/176通过，新增两组第七cache生产事务夹具验证write后false/throw未知恢复、保留两cache owner、晚到Original读取可清理且无retry，以及foreign/unknown/mixed/换singleton/null拒绝。旧第六部分失败夹具确认未安装第7不能误恢复。夹具仅synthetic CLR，不执行nativehelper/field setter/allocator。
- Inspect-GuestIngameApi实际离线成功：5程序集/89类型/6descendants/34SpecContainer声明/181字段边/8明确frontier/11closed contexts，MissingTypes空、PowerShell AST零错误。原报告只留.local，没有执行native/save。Build警告视为错误通过，SHA256：`CBEB47494C7C3BD3EA9419B78E1CCD683A100FC2A4D3161A597E861563A492D3`。
- 首次Build暴露SubHelperSpecData基类所在Sirenix.Serialization编译引用缺失（CS0012），按实际interop补入Build脚本及项目Private=false引用后通过；不复制/发布该程序集。没有修改已通过Core源码或重跑无关测试。
- 更新日志/当前摘要、接手文档和Skill；正式validator使用现有PyYAML缓存与Python -X utf8通过，Skill同步后SHA一致。独立只读末审无新增阻断，确认第七singlefield、三份closure、faultserial、aux/alias/frontier拒绝及key/child关联；仍不证明native构造或完整图。
- 新鲜进程检查0，安装DLL仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`。保留历史0.1.20/174/hash，用户试玩继续延后。继续实际资源/设备/actor/cache/output与自然进入/静止边界，然后房主地图采用、个人产物/容量分流、逐产物返航、真实双端与GitHub冷配置完整闭环；有限子图源码不是完整联机验收，完整goal保持active。

## 2026-10-04 — 0.1.22 独立比较器与冷启动临时档入口

- 前一goal turn为实际进展：0.1.21七步typed缓存源码已提交a8bbefc并核验远端，176项Core/Build通过；本轮完整目标保持active，独立两袋/容量/负重与房主长期进度不变，未部署/启动或调用游戏/存档。
- 新shared comparer接Ingredients与Ingame四种dict：三key、七精确闭型同class独立object_new/IntPtr候选，显式(capacity,comparer)先于Add并回读实际comparer；原/新指针/class/kind和已知静态来源进早期基线/审计。未知custom和未支持aux拒绝，source null准确捕获但Prepare拒，不从Default/CreateComparer重选或清null。metadata声明无instance字段不证hidden state/native hash/equality。
- 普通native dictionary ctor抛时assignment/Hold尚未发生，已明确PartialConstructorAllocationRetentionVerified=false；未把暂存成功wrapper当所有未知分配持有证明。进入/静止与所有native/guest/world/cargo权限仍false，七步和21explicit handles未扩，不接GUI/Network。
- Comparer Inspector实际离线3程序集/27类型/7继承families/19声明contexts/8dictctors，无missing与输入hash变化。Enum<string/int>仅metadata替换，Enum候选仅InGameSaveType(int32)；不复制框架private GC字段。新增冷档Inspector/doc核对DefaultSaveFolder/direct instance path/SkipCloudPullForPreset和首次load/云路径；只改slot/目录不能保护原档，候选未采用。
- Test-Core源码与选定adapter/脚本共61项逐文件git object一致，复用0.1.21真实176/176结果，本轮未重跑Core或执行nativehelper。Build警告视为错误通过，SHA256：`FED729C9C9ADEF297D9282D5C76D237C548AB7983115BFEF383BE60394328C50`；详情见guest-comparer-build-verification及core-verification。历史hash/安装0.1.12/潜水0.1.11/default0.1.0保留。
- 日志、HANDOFF/PLAN及Skill同步，后续核实际首load路径隔离与全部输出/actor/cache/资源，然后房主地图采用、个人容量/产物分流、逐产物返航及真实双端/GitHub冷配置完整闭环；比较器与冷档候选不作完整目标完成证据。
- 本轮 Skill 草稿正式校验通过并同步，两个新 Inspector 的 PowerShell 解析错误为 0；独立源码和发布记录审查无阻断。新鲜游戏进程检查为 0，已安装插件 SHA256 仍为 `8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，没有部署或启动新版本。

## 2026-10-04 — 0.1.23 启动加载与路径来源观察

- 前一goal turn为实际进展：0.1.22独立comparer源码已编译、提交75b6b42并核验远端；Core未改复用前轮176项结果。完整M3—M7保持active，每人独立袋/容量/负重及房主长期进度不变。本轮未部署/启动/native业务或读改存档。
- 新默认关闭Startup/ObserveSaveStartup在Plugin.Load最早来源启动自然调用诊断，prefix/postfix/finalizer保持原参数、原结果和原异常；有界CLR trace记录nested配对、post/finalizer及丢失/线程/安装/停止缺口，不能给never-loaded或native authority。路径callback只存有界哈希，原native wrapper不入队；Plugin.Load线程不是Unity证明，第一次实际Update登记后才允许该线程direct字段读取候选。
- 精确离线研究发现当前BepInEx IL2CPPChainloader在Internal_ActiveSceneChanged runtime-invoke detour中Preload→Execute→Plugin.Load后才原Invoke，这不证明早于所有Awake/.cctor/读档。Hook setup可能原生class初始化；工厂返回不代表load完成，SkipCloud字段名/静态边不证明全部云策略。临时档与路径重定向未实现/未开启，全部native/isolation/world/bag/firstload/entry/quiet仍false。
- 本轮实际Test-Core 182/182通过，新增6组生产trace CLR夹具；测试只验证控制/配对/限额/证据降级，不执行游戏挂钩、路径或存档。Build警告视为错误通过，SHA256：`694D0DCB5E117977BE377ECB3465898208B4149634113DC6CB32B5379210FC75`；详情见save-startup-build-verification及core-verification。保留0.1.22历史Build/复用及installed0.1.12/dive0.1.11/default0.1.0。
- 日志、PLAN/HANDOFF、相关文档及Skill更新；继续证明实际首次加载/完整来源输出、员工临时状态/actor/AI隔离、房主地图采用、个人容量与产物分流、逐项返航入仓、真实双端与冷配置闭环，不缩减原目标。
- Skill 草稿正式校验通过，新 Inspector 的 PowerShell 解析错误为 0，两次独立只读源码末审无阻断。实际离线启动报告为 5 程序集/22 类型/45 声明/8 框架方法，36 个源码目标逐精确声明核对；原 PE 为 16 roots/176 records，81 no-unwind/1 partial 未补猜。新鲜进程检查 0，已安装 SHA256 仍为 `8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，本轮没有部署或启动。
- 已将正式校验的 Skill 同步到仓库 .agents 并核对 SHA256 一致；当前与历史构建摘要分开，0.1.22 的复用测试仍保留为历史记录，本轮 182 项为真实新执行。

## 2026-10-04 — 0.1.24 每人独立袋的只读同步

- 用户再次明确“每个人的”；继续每人独立背包/容量/负重，房主唯一长期进度，完整M3—M7 goal保持active。前轮0.1.23源码已提交46c249b并核验远端；本轮未部署/启动/native业务或读改存档。
- 新CargoFrames/Assembler将真实账本投影为两成员tracked-only记录；来源Room、expedition/member、整体generation/revision与bagrevision分开，完整捕获产物深copy及指纹；32items/64pages/max2048，空1片。预约/未知/待返航数量明示；不猜重量，Returned保历史残余值。
- 协议6新增CargoInventorySlice，host-only/guest-only、Room绑定、首片撤旧、全批原子commit；独立lane与action/frame/world/map公平，控制优先，已开始批次发完+一份nextlatest，普通scene不清账本。配额64expeditions不淘汰身份，高水位/phase/成员/返回身份与BagRevision/request不得回退。
- 生产CargoInventoryController接NetworkController绑定/4Hz Update/Disconnect；内容变化才增加整体版本，getter与take后重新核对receive高水位，断线保留confirmed/unknown且拒新peer冒认。无游戏nativeproducer初始化/attach、fakeledger或权限UI，不开原生捕鱼/分流/入仓/存档权限。
- 本轮实际Test-Core 200/200通过，新增18项schema/assembler/session/真实回环TCP及实际生产controller夹具；只用synthetic CLR能力事实，不执行game/native。Build警告视为错误通过，SHA256：`2BA5D4501D4054D91A30360098E70C5B7BEA8C3242D190EA6AEFDFD71F0E049E`。当前记录见cargo-transport-build-verification与core-verification；0.1.23/182/hash及旧实机范围保留。
- 只读审查发现合法原账本可能留.1/.2扣除后的ReservedWeight浮点余量：投影Returned不要求精确0，保历史值，生产ledger返航fixture覆盖；缓存并发首片边界增加take后和getter新鲜复核。
- PLAN/HANDOFF/相关文档与Skill同步；后续接真实潜水成员、独立容量/产物来源及员工分流，逐项返航入仓、guest完整隔离/actor/AI、房主世界采用、实际双端与GitHub冷配置，不缩减目标。
- 首次Core 200通过但插件Build在CargoFrames嵌套SelectMany处报CS0656（interop引用环境缺NullableAttribute构造）；改显式foreach扁平化，不引入新依赖。Core源改变后重新执行两套检查，最终200/200和上述Build/hash均为修订后的真实结果。
- Skill草稿正式validator通过并同步，SHA256为`1A39FDE2C1A96B5E3E262B18CC61EEC7ABD2288A325395903DB4C3723FD5A960`；Test-Core PowerShell解析零错误，两名独立代理只读源码复查无阻断。新鲜游戏进程检查0，安装DLL仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，本轮没有部署/启动。

## 2026-10-04 — 0.1.25 捕获来源观察与个人容量时机

- 用户确定每人独立背包/容量/负重；前轮0.1.24已提交2270bca并远端核验，完整M3—M7 goal保持active。当前协议6，未部署/启动/native业务或读改存档；旧实机与默认包范围保留。
- 新生产Core LootCallLineage固定Run/CallId/SourceRoot与prefix source候选，严格同步LIFO、未知鱼遮父、同源嵌套保持根、新鱼/代次分开；postfix保留scope，finalizer退出并记录原异常。高水位/线程/opaque token/额度/队列失效锁存，不绑定Member/Operation或构造CargoFacts。
- Loot三适配器扩16 exacttargets，主/追加产物、原随机int、容量与负重、普通/Ignore Add、Add_Impl、新槽与水下进度均只读；prefix原生读取前后确认窗口及冻结重入拒绝，finalizer仅CLR。key exactUTF16 hash+length、512/65536预算，slot加密字段/终态不读取。
- copy/Core512queue、128context、32depth、256fishordinals、每Update16、process/run8192events；state/lifecycle各128。Hook损失即时撤Core、controller先fresh健康再drain，旧HealthyAtCapture另带当前健康。正常toggle新实例；unknown卸钩CAS一次保留引用，不重试或重置全局故障。
- 新Cecil脚本实际7types/16hooks/5fields/owner继承5层、missing为空；首读误用不存在的overweight backing，按实际字段清单改为_overloadedThreshold_k__BackingField后通过。PE实际16selectors/18roots/145records/54无containing unwind，无quota/遗漏/截断；原始报告/IL/地址只留.local，不执行GameCode。
- 静态路径显示原生进入后才选追加物且会更新保底计数，当前Cargo.Reserve入口前完整产物合同不足以直接接捕获。下一桥须单独来源/操作租约及已选产物受控阶段，避免预Roll、重复执行、房主袋污染与两Gate OperationId冲突；Add_Impl/现有槽/进度副作用都需明确归属。
- 本轮实际Core/TCP209/209通过，新增9组生产来源栈夹具；Build警告视为错误通过，SHA256 `970282A99808D88AFC8327F94DD9EE866384B270982AC3D40AF7CABADB158918`。测试不运行native callbacks/ABI，所有员工/产物完整/捕获/袋增量/native权限false。两个独立只读末审无阻断；新脚本AST错误0，正式Skill校验通过并同步。
- 新CAPTURE_LINEAGE和构建摘要、当前core记录及交接/计划/Skill同步；历史0.1.24的200和cargo-transport摘要不改。新鲜进程0、安装DLL仍8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B。后续真实个人分流/入仓、guest隔离、房主世界采用、双端正常闭环/冷配置继续必需。

## 2026-10-04 — 0.1.26 来源租约与延后个人产物

- 前一goal turn为实际进展：0.1.25已提交aee2df4并核验远端，本轮继续完整M3—M7 active，每人独立袋/容量/负重与房主长期进度保持；未部署/启动/native业务或读改存档。
- 新CargoSourceLease及实际ExpeditionCargoLedger两阶段：SourceReserve铸造exactownedlease、统一capture operation高水位、固定member/source/actor/loadout；旧Reserve成功纳同高水位而失败op90不推高。两成员request/gate同1可mint1/2；256记录含关闭/取消tombstone不淘汰。
- EnterSelection在随机/其它native业务前需已存在整批隔离proof，标EnteredUnknown；LateSeal需完整已选products+held+NoBagWriteYet/current个人容量，才绑定weight，不重Roll/重进/自动confirm。未选Snapshot Requestnull/Intent/YieldBoundfalse，原wire6仍计reserved/unknown。
- 返航冻结原capture成员，未选unknown即使没有returnitems也不complete；offline/Returning/Aborted原lease可晚绑定并只补原productitems，不恢复nativeentry或新成员。真正receipt、逐itemstorage/save仍独立；unknown/断线/scene/newroom不清来源。
- 本轮交叉源审查发现0.1.25 Loot复制器误把HostEntityTarget.LocalToken与nativeptr比：真实LocalToken=UnityGetInstanceID，ObservedHostTargets另按pointer索引且resolver前后核代次。改非0且允许负ID，不新增native读；旧纯CLR tests未执行此native适配器，旧末审也未核出，此次已沿实际调用链核对。
- 首轮221/Build通过，末审发现首次完整选定产物因容量拒绝后仍可换轻批次；修为容量检查前固定首份fullvalid选择，拒换weight/grade，但同批可fresh capacity再核。新增专门fixture后重跑两套检查，最终Core/TCP222/222、Build警告视为错误通过，SHA256 `B176F3346119311C29A7A4A4CA1C82B4BE4EA110B7130BC3FC76471B60371E1A`；11生产账本＋2生产controller/TCP新增fixtures。全部能力来自synthetic facts，不执行native或声明玩家实际归属。
- 新CAPTURE_SELECTION、currentcore/capture-selection摘要及交接/计划/相关Skill同步；旧0.1.25的209、观察摘要与安装0.1.12/潜水0.1.11/default0.1.0保留。实际原生整批选择/首次write暂停桥、finalgrade/effectiveweight/Obscuredslot/副作用/终态、员工分流和逐项入仓、guest隔离/房主世界与双端冷配置仍必需。
- 独立只读末审确认产物固定缺口已修复；正式Skill校验通过并同步。最终新鲜游戏进程为0，已安装DLL仍为`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，未部署或启动本轮源码。

## 2026-10-04 — 0.1.27 原入袋参数的基础资源观察

- 继续用户确定的每人独立背包/容量/负重，房主唯一长期进度；前轮已提交e9d36e6，完整M3—M7保持active。本轮不部署/启动，不运行原生业务，不读改存档，用户延后试玩期间继续独立开发。
- 既有默认关闭16个Loot观察入口中，Add_Impl仅Before同步传原IItemBase到复制器；按DR.Items或IntegratedItem精确native class读取四backing fields两次，末核pointer/class/store。原wrapper不入Hook Context、复制队列或日志；immutable CLR Resource随prefix保存，After/Finalizer复用Before结果，Finalizer不重读资源。unknown/null所有数字null，读取失败无条件清资源候选再撤链。
- 每个资源prefix最多32次读取、进程65536且开关不清计数；线程、重入、前后窗口与已有512queue/128context/8192event及owncleanup沿用。ClassStore/typedwrap可能初始化native类，两次样本一致不证明原子静止或ABI。TID/ItemDataID及basegrade/baseweight只候选，不猜产品ID、bonus/count/重量参数公式，不产生CargoFacts或最终品质/有效重量/完整产物/员工分流权限。
- 新Inspector实际离线1assembly/4types/2资源类/8directfields/8publicgetterdecl/4slotdirectfields，输入前后hash一致F41167D67D226866B22EB76A239B796B0D1E40F57177E62FBAAFC2284471626E，AST错误0。当前ObscuredInt是CLR struct，slotgetter用ldobj且无value_box；5instancefields、13所选原生方法声明与2整数转换均有runtime_invoke，解码副作用未知且未执行。原报告/封装IL仅.local，槽合并及最终计算仍待核实。
- Build警告视为错误通过，SHA256 `687C8F6959163C33B85C026C05D2F645D0900BE12328761BB2CDFC64158BCD60`。75个Core/TCP源和runner输入核对前commit内容，并核对上一轮实际验证输入SHA256；一个历史文件的Git blob与工作区换行表示不同，实际验证字节hash仍一致。复用0.1.26真实222/222，本轮未重跑；这些测试不编译/执行新原生复制器。两名独立只读末审无阻断。
- 新LOOT_PRODUCT_OBSERVATION、当前core/loot-product摘要与交接/计划/Skill草稿同步；历史0.1.26选择摘要、实际测试时间及安装0.1.12/潜水0.1.11/default0.1.0保留。实际整批选择/首次写入暂停、最终grade/effectiveweight/Obscured槽、个人容量分流与鱼终态、员工逐项返航入仓、客机隔离/房主世界与双端/GitHub冷配置仍需完成。
- 正式Skill校验通过并同步到仓库，SHA256 `4C2A47E506D6FB0E41C77B0AF961BA0C4333C83D728EC14A33AE1307654CE3D7`；Inspector解析错误0。新鲜进程检查0，安装DLL仍`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，本轮没有部署或启动。

## 2026-10-04 — 0.1.28 货槽候选与原计算路径

- 前轮0.1.27基础资源观察已提交5d0618d并远端核验，属于实际进展。本轮继续完整M3—M7 active、每人独立袋/容量/负重与房主唯一长期进度；未部署/启动，不执行原游戏业务或读改存档。
- NativeCallInspector新增显式默认关闭的私有指令文本输出，2048/method与8192/report默认，硬限8192/16384、256单条/1048576保留字符；只记录已有合法mapped/unwind范围指令，整条omit不改decode/edges。实际off/on/cap三模式13method/464decoded/58edges逐项相同，on464条/8083字符，cap3条/461省略；weight18method/1687decoded/278edges/30011字符，4个no-range leaf无文本且不补猜。四次工具warning-as-error编译与私下逐记录/范围/统计校验通过，原文本/地址/立即数只.local。
- 原非零key已初始化分支给出32位XOR子集；key0取静态key、未init初始化接收器、detector路径含未解析间接call，不能把原decode当无副作用getter。初始化写接收器不证明struct副本必改原slot。新Core decoder仅复制五标量、initedtrue/key非0并保守fakeactive校验，缺条件返回null/固定原因，不补key/初始化/调用检测器，不等价完整原GetDecrypted。
- 既有默认关闭16 Loot入口中，仅AddLootBox/IngredientsAddFromLootBox Before向复制器传slot。exactclass+4directstruct各二读、五CLR原值比较和末ptr/class/store复核，正常14read，32/prefix与65536/process不toggle归零；prefix/queue/log只immutable4candidate，不存wrapper、密钥或hidden值。After/Finalizer复用Before；faulting事件无条件清slot candidate/撤prefix，先前队列仅历史诊断带CurrentLineageHealthy=false。unknown/null或decoderunavailable不改原游戏；全部权限/ABI/最终品质/重量/完整yield/BagDelta仍false。
- 实际weight已知路径有lift类别/接口取值，超重参数用于阈值/debuff，新槽Add边界未给终局FinalGrade时刻；ApplyFinalGrade有类别/阈值/clamp，GetExchangeCount还走物料转换。不能用basegrade+bonus/count×baseweight直接填捕获/肉量；完整capture/成员/受控选择提交/个人容量分流及receipt仍需真实桥。
- 本轮真实Core/TCP226/226通过，新增4组literal signed边界/immutable输入、未init/key0、fake/未知与无CargoPermission夹具；不执行native sampler/decoder或真实slot。Build警告视为错误通过，SHA256 `17C60F94E17BE08001BB9E6984388C15ABC85640574CE3076B89616C1A3FE5EA`；两名独立native/文档末审无阻断，Core与工具均另有非作者审查。126源/项目/验证输入已封存。
- 新LOOT_SLOT_OBSERVATION、NATIVE_ANALYSIS及当前core/loot-slot摘要、交接/计划/Skill更新；保留0.1.27的复用222与旧构建/实机证据。官方Skill校验通过并同步，SHA256 `1544C800E23BBD97E5B68C6E41E3024D05FDA1DE050E9E91C8A9AF43FDEB645E`；两个Inspector AST错误0。新鲜进程0，安装DLL仍`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，实际回调、个人分流/逐项入仓、guest隔离/房主世界与双端/GitHub冷配置待完成。

## 2026-10-04 — 0.1.29 已有货槽与品质更新观察

- 前轮0.1.28已提交2868b82并远端核验。本轮继续每人独立袋/容量/负重、房主唯一长期进度与完整M3—M7；没有部署/启动、调用原游戏业务或读改存档。
- 默认关闭的Loot观察新增17=set_TotalCount、18=set_Grade、19=set_FinalGrade，原16编号不变。三个public instance void setter参数为by-value ObscuredInt；原参数只在同步Before窗口复制五CLR标量并形成不可变候选，原结构/密钥/hidden不留Hook Context、prefix、queue或日志。不主动调用setter、解码、初始化或检测器。
- 三个setter的Before/After分别核exactclass、四direct字段双样本与末pointer/class/store；After必须匹配本call prefix私有pointer/class。32reads/sample、最多64/call与65536/process不toggle归零；正常14/sample。RunId+CallId仅标本次样本，没有持久槽编号/寿命/库存身份。Finalizer复用After（含Unavailable），不存在After才复用Before；原两个槽边界仍Before-only。故障无条件清当前前后样本/参数并撤prefix，旧队列仅历史诊断带当前不健康。
- 数量setter是新总数，不能当捕获增量；FinalGrade更新不证明捕获终局或主/追加整批完成。已知原重量更新可能早于这些setter，新增入口不是首次write前的容量/产物暂停桥；source仍只是同步包含，不提供员工/操作归属或receipt。所有槽寿命/ABI/最终品质/BagDelta/FullYield/native权限保持false。
- 新Cecil实际一次成功：8types/19精确声明/5directfields/owner继承5层、missing0，三setterByReference=false，AST错误0，互操作输入SHA256 `F41167D67D226866B22EB76A239B796B0D1E40F57177E62FBAAFC2284471626E`。原报告只留.local，不执行game code。两名独立只读审查核对实际上游、回调/数据生命期、失效及文档，未见阻断。
- 本轮实际Core/TCP226/226通过，扩展两个既有来源栈夹具覆盖17–19固定父链、未知来源遮蔽、postfix保留scope及finalizer LIFO；新增测试数为0，不执行native观察。Build警告视为错误通过，SHA256 `A99DF3C237FEC536F66DE878559840043D7E97ED9041B58CC09DAC2BE3225BE1`；124源/项目/runner输入在执行前封存、执行后相同。没有重复既已通过的测试。
- 更新LOOT_SLOT_OBSERVATION、currentcore及新的loot-slot-mutation摘要、交接/计划与相关Skill；旧0.1.28实际226及0.1.27复用222证据保持。官方Skill校验通过并同步，SHA256 `CB1A9F5056B53620E9C0E15FA0B075969C068D4EC1B7B697C9A85A74619E9CF1`。新鲜进程0，安装DLL仍`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`；原生参数ABI/真实槽观察、完整捕获产物与个人容量分流、逐项入仓、guest隔离/房主世界、正常双端闭环与GitHub冷配置仍待完成。

## 2026-10-04 — 员工整批产物的选择与提交研究

- 前轮0.1.29已提交e95d8c2并远端核验。本轮继续完整M3—M7与每人独立袋/容量/负重；插件源码保持0.1.29，新增离线研究工具与可实施流程，不把更多事后观察当成整批分流桥。
- 四组新的精确选择器离线报告实际成功：普通主/追加7方法655指令、映射/保底8方法580指令、上游入口4方法212指令、容量添加2方法106指令；共21方法记录1553指令。无方法配额触顶、根遗漏、非法指令、越界或指令/文本截断。仅已知unwind范围，不证明完整函数/间接目标/ABI，原报告与文本/地址只留.local；没有调用游戏业务或读改存档。
- 新证据：SuccessPickupFish是至少tier1的循环，按CarvableCount处理后才追加一次；LootDeadFishBody另为tier1配方。主GetFishDropItemByTier本身可抽随机，提交不能重查ID；Plus原Roll会经SetCounter/SetFishDropPityPending更新保底、创建缺项/置dirty，不能当纯getter或重Roll。资源lookup/品质与最终重量仍不能由候选猜完整产品。
- Add/Ignore只在实际Add_Impl正常结束后返回true；槽前刷新负重阈值/玩家效果，槽后才写实际袋重量并触及其他进度。原容量是当前超重与接口重量>0检查，不是统一整批预期重量。临时LootBox仍引用全局Save/Player；不能跳原写入伪true、先入房主袋再复制，或借Ignore开放员工无限容量。
- 上游SuccessInteract只见UnityEvent转发，撤回“显式actor参数即可同步绑定拾取”的未证建议；OnSuccessPickUp另取交互体/品质。DestroySelf的静态虚槽候选与尸体状态不是captured终态，私下字段误认已纠正，公开摘要不含该错误。活动死鱼可仍有实体身份，但Gate及显示暂不支持尸体拾取，需要真实阶段/显示/空间/adapter一起接入，未放松现有权限。
- 新Inspect-FishYieldApi实际一次成功，AST错误0：17types/27businessdecl/42directproxies/3singletoncontexts/13interfaceproperties/4liftvalues/1byref/missing0，互操作输入前后hash一致F41167D67D226866B22EB76A239B796B0D1E40F57177E62FBAAFC2284471626E。脚本SHA256 `93CF568D38273C6D346089E5ACD8A1D4608CA5CF6F2FBF142298634739FA13ED`；另两代理独立只读核工具及执行文档，无阻断，措辞精确调整已完成。
- FISH_YIELD_BRIDGE确定员工显式合作批次路线：固定真实成员/actor/source→一次完整有序选择→个人容量→单次分流与共享进度/终态→真实receipt→既有账本投影。明确不同于单机交错随机顺序；原房主自然路径保持。现账本已有租约/挂起，不增加镜像Gate；实际native producer、装备品质/最终grade/重量/terminal/任务与返航仍需接通。
- 124个前轮源/项目/runner验证输入逐字节匹配原封存，插件DLL仍SHA256 `A99DF3C237FEC536F66DE878559840043D7E97ED9041B58CC09DAC2BE3225BE1`；复用0.1.29实际226测试/Build，不重跑、不计新执行。新的fish-yield-analysis摘要与currentcore/交接/计划/接口/Skill同步；旧0.1.29实际构建摘要保持。官方Skill校验通过并同步，SHA256 `667624099AB600DEC962567FA08360BEF87BD229544679A8D30ABC6B0DDA337E`。
- 新鲜进程0，安装DLL仍`8F90042C1177CB6861A7D86ED9768AE1B1966C52B7768BA6E32CBD38D54E5F5B`，未部署或启动。原binary/metadata最终hash与四报告一致；所有真实成员/选择隔离/整批分流/receipt/guest/world权限false。实际双端、正常返航和GitHub冷配置仍待完成，完整目标继续active。

## 2026-10-04 — 0.1.30 员工一次性品质与产物选择

- 前轮研究已提交3e5b96d并核远端，属于实际进展；完整M3—M7继续active。每人独立袋/容量/负重、房主唯一长期进度保持。本轮未部署/启动、调用原游戏业务或读改存档。
- 实际Core FishYieldSelection用同ExpeditionCargoLedger EnterSelection仲裁，之后一次Body品质随机、至少tier1有序主选择和一次Plus，固定-1无产物及原ID/参数；正项一次资源lookup/retention，不重选、不造CargoProduct或receipt。普通最多7主预留plus；deadbody另单tier配方，不自动放开现Gate/显示。
- actual NativeEmployeeFishSelectionBridge已写typed GetPickUpGrade/GetFishDropItemID/RollPlusItem/GetItemV2，持exact employee lease/source/profile/provider；仅Core Selecting窗口可业务调用，所有attempt先标记。新OwnsActiveTracker/UsesLifecycle只读query核实际活动observer及same tracker，identity与资源检查末复核代次。窗口/keeper code编译未native执行，函数ABI不作已验。
- 私有ordered资源/参数与最多13显式reference/handle尝试，shared resource指针可复用handle但不合并drop条目；owned map最多256桥，Disconnect/return不free未知。release只看同ledger Confirmed/NativeNotEntered，未知free不重试；原生内部/partial分配完整保留未证。已有profile初始化/_info.TID等式是保守兼容限制，不补provider getter/GetFishData。
- 新Grade研究4方法214指令（两报告1/66与3/148，含一无unwind叶）；GetPickUpGrade非已证装备算法，本身加权随机，必须进入后一次固定。raw Grade加法/float重量与后续FinalGrade不可混；Plus保底/dirty已有房主进度副作用。private Cecil实际2程序集/10directproxies/4business/4lift/input前后hash一致，不运行game。
- 本轮实际Core/TCP236/236，新增10个真实coordinator＋ledger夹具：顺序/同lease/partial business及guard异常/freshfacts/tier/哨兵/线程重入/重复resource与synthetic容量挂起；只CLR，不证明GC、资源或native权限。Build警告视为错误通过，SHA256 `36A362401478FC9C66BD5EE7F11B2C3D7D9475C3B5818007A7493AE88A9902AE`；130输入执行前封存、执行后相同。独立native/interface审查无阻断，发现生命周期健康围栏与注释过宽后已修正，再封存验证。
- 新EMPLOYEE_FISH_SELECTION与构建摘要/currentcore/交接/计划/Skill同步；旧0.1.29研究与226构建保持。下一步正面接真实host-owned employee actor、完整product/品质/个人重量、LateSeal、单次分流/共享进度/终态receipt与逐项返航；host世界/guest隔离、武器生存、正常双端与GitHub冷配置仍待完整实现，未声称可合作捕鱼。
- 两名独立只读末审最终READY；已把旧研究段标为0.1.29历史并纠正未证装备算法措辞，新0.1.30实际236不与历史复用混淆。正式Skill校验通过并同步，SHA256 `179CE534888A30AF254185BE0995C0190AAD5B9CDB1ABEF2502141E615A0078F`；纯文档修正后130验证输入仍相同，无需重复测试。新鲜游戏进程0；安装DLL保持8F90042C…E5F5B，原binary/metadata及两个interop最终hash与研究输入一致。

## 2026-10-04 — 0.1.31 员工捕获产品与同批个人容量

- 前轮e9c6f6b已推送并核远端；本轮继续每人独立袋/容量/负重，房主唯一长期进度。未部署/启动或执行游戏业务，未读改存档。
- 新Core FishYieldProducts把既有已进入选择归一化为缓存批次：四getter一次、原TID与lookup分开、checked原品质、原float乘法再转double；员工袋double累加明确为Mod规则。snapshot owned copy，部分异常保留标量与未知，全-1不造空receipt。TrySeal只用内部同批产品与外部真实freshfacts，容量拒绝不重选、不读getter。
- 新NativeEmployeeFishProducts仅typed编译，支持exact DR.Items、四gettermask顺序/attempt前标记、source/lifecycle/strongrefs原守卫；不增加lookup/handles，不补fake capability或网络调用。IntegratedItem原metadata不实现IItemBase。
- 原metadata新成功报告3类14getter，前次PowerShell字段重载访问失败未写报告，修正为CLR reflection后成功；原文件hash一致。新AddFromLootBox私有PE研究1方法112指令/1间接call，无截断/配额；入仓FinalGrade、兑换数量与捕获rawGrade分开。完整方法/分类叶/additive/实际native语义未证，原指令地址不发布。
- 实际Core/TCP246/246，新增10 production coordinator+ledger产品夹具；只synthetic backend，不执行native。Build警告视为错误通过，SHA256 `D5C9D930C17EBEB991C1DA26FF3723287E52C919C1F07426FE75A529F2EC6078`；133输入执行前封存、后相同。
- 新EMPLOYEE_FISH_PRODUCTS与实际构建摘要/currentcore/交接/计划/Skill同步；修正CAPTURE_SELECTION最终品质歧义，旧.30选择236摘要保持。下一步接真实员工actor/分流/共享进度/鱼终态，再接独立返航转换与逐项实际入仓/保存。资源须先冻结返航metadata或转移ownership再free。guest隔离/房主世界、双端正常闭环及冷配置继续完整M3—M7。
- 两名独立只读源/文档末审READY，正式Skill校验/复制hash匹配，SHA256 `04B0EEAD11D05EC83EB6202B1DFD8B1B92ED3A82326287E5BECC4C0891AB810B`。最终133验证输入相同，无新代码变更不重复测试；新鲜游戏进程0，安装DLL保持8F90042C…E5F5B，原binary/metadata与两个interop最终hash匹配。

## 2026-10-04 — 0.1.32 独立员工返航计划与一次入仓原语

- 前轮830c235已推送并核远端，属于实际进展；本轮沿每人独立容量/负重和房主长期进度继续完整M3—M7。未部署/启动、原生业务或存档执行。
- 新CargoEmployeeReturnPlan绑定捕获raw产品FP、独立policyFP和六参入仓输出，readonly且culture稳定；既有ReturnItem首次pin只Confirmed Returning员工Unclaimed。sameplan Duplicate、changedpolicy/任一输出Conflict；employee四阶段都核sameplanFP，host自然链不需员工plan、不重复Add。
- 新CargoReturnMaterializer同ledger Enter之后才guard/一次Add，exactplan/线程/重入/竞争仲裁；失败unknown不重复，returnedcall单独记录但不delta/save。新NativeEmployeeStorageBridge typed Add有5explicit strongdeps、既有storage/dictionary/SaveSystem→manager→SaveData守卫、Main/Branch窄profile、未知跨断线保留、sameplan SaveConfirmed后free，未知free不重试。无GUI/network/可信producer。
- metadata实际1程序集13精确声明42direct代理；补storage/save 5类7property6direct代理，inputhash匹配。ItemDataID/Items/Ingredients映射与六参分开，兑换用capture rawGrade，FinalGrade政策另核；分类leaf、原资源/GetItems等价及完整转换仍未知。未跑新PE，原报告/IL只.local。
- 独立末审在执行前发现hash大小写不一致（生产ctor及一组synthetic policy），已按现CanonicalHash小写规范修复；Native Place Enum.IsDefined包含sentinel，已改Main/Branch白名单；save后显式清wrapper refs。修后再封存运行，不把未运行检查当通过。
- 本轮实际Core/TCP256/256，新增6返航计划+4提交夹具，4旧员工返航流程适配；只synthetic facts/backend，不native。Build警告视为错误通过，SHA256 `27D31C9BEC68F535BF8584729A234DE60CB370A25BC81C2918E40FECA5341DE9`；138执行前封存输入后相同。
- 新EMPLOYEE_RETURN_PLAN/currentcore/实际构建摘要与接手/计划/Skill同步，旧.31产品246摘要保持。下一步冻结实际资源/分类/品质数量政策和生命周期证据，接仓库bucket增量/保存；可信员工actor/捕获分流、guest隔离/房主世界、双端正常闭环与冷配置仍必须完成。
- 两名独立只读末审READY，正式Skill校验/复制hash匹配，SHA256 `A1A057A73ED035B3026CABA1ADF51C843BAABBDA72ABBE1889CAE68134438831`。另补精确Add调用窗口与独立整Dispatch in-flight释放围栏，不能同步回调推进SaveConfirmed后提前free。最终138输入字节相同，无新代码变化不重复测试；新鲜game进程0、安装DLL保持8F90042C…E5F5B，原binary/metadata及两个interop最终hash匹配。Cecil主私有脚本与Binding内联追加分列，不声称主脚本单独复现追加记录。

## 2026-10-04 — 0.1.33 员工返航映射与一次数量缓存

- 前轮945e1d7已推送并核远端；继续每人独立背包/容量/负重和房主长期进度的完整M3—M7。当前未部署/启动、未执行游戏业务或读取存档。
- 新FishReturnProducts沿实际selection/normalization/sameledger，固定稀疏DropOrdinal与压缩ProductIndex、原lookup/ProductTID/rawGrade/count/type/FP/mode。预拒零尝试，entered后各returnedscalar先保存再source/ledger postguard；整批原子Ready，unknown不retry，Direct无helper、Exchange用原rawGrade/count一次。纯计划creation不Bind/授权，capture数据不覆写。
- 新NativeEmployeeFishReturnMapping typed GetItems/GetIngredients/direct字段/实际Exchange，exactCurrentRequest+单调attempt mask，attempt先于guard/classstore/业务。17显式ref限额、256独立owner、精确类/查询ID窄profile；source13只有Ready且Map退栈/同ledgerConfirm才free并clear字段，17只有全同产品plan SaveConfirmed退栈后free；unknown跨Disconnect保留不retry，无GUI/network producer。
- 实际离线API4types/2businessdecls/2IntPtrctors/5int代理/4classinit，inputhash匹配；新PE一次Depth0分析1方法48指令7direct edges/0indirect/1known range，48text/757字符/0省略无trunc/quota/invalid/overrun。helper内部又GetItems并公式rawGrade后乘count；完整公式/原映射资源等价和一般乘法安全未知。原报告/地址/IL/指令只.local。
- 实际Core/TCP267/267：11新夹具覆盖mixed mode/duplicate IDs/sentinel、前后源丢失/同ledger同步Confirm、partial/no retry、thread/reentry/owned数据、pure plan、capacity拒后同缓存seal/Confirm/源消失。synthetic资源/facts不证明native政策/收益。
- 独立末审发现17owner只保native依赖、原13free后可能丢bridge/ledger/缓存，已补强managed Backend上下文及exactowner核对。Native-only修不影响实际Core84源输入；141全插件输入重新执行前seal并最终Build，编译警告视为错误通过，旧seal/log保留，不把修后输入称为Core再次执行。
- 最终DLL SHA256 `651D26DB75C4B098178DFE7C94D5CE99BED507C8FA2D9793D6F2ABDDDAE43003`；新增EMPLOYEE_RETURN_MAPPING、真实摘要/currentCore/计划/接手/log/Skill同步。后续实际模式分类/FinalGrade政策、员工actor/捕鱼分流、仓库bucket增量/save、guest隔离/房主海洋、真实双端正常闭环和GitHub冷配置继续必需。
- 两名独立源码/窗口复核及最终文档证据审查READY；正式Skill校验、protected copy与hash匹配，SHA256 `EC70E10701A3B4E956153879948961BD797A7A1D2102819EFFAE42559F5D4D28`。最终141插件输入及84实际Core输入核对无变化；binary/metadata/两个interop/安装DLL最终hash保持。新鲜Get-Process枚举game进程0；CIM因权限拒绝未作为0证据，不终止进程。Skill、日志、计划与公开摘要收尾通过，原始指令仍只.local。

## 2026-10-04 — 0.1.34 原返航品质与数量自然边界

- 前轮0de7ea7已公开推送并核remote，属于真实进展；本轮继续每人独立背包/容量/负重、房主长期进度及完整M3—M7，未部署/启动或读写存档。
- 原ApplyFinalGrade接收器3k字段只供阈值，集合来自SaveSystem.GetGameSave→SaveData.GetLootBox，临时员工袋调用仍可能写host。ItemsUtils没有IsInInvenType等价声明；不以ItemType/IsIngredient或原caller absence补policy。
- 现默认off LootObserver新增3exact natural hooks20–22，TargetCount22/旧code保持。原additive同步prefix+3instancefield双读immutable candidate，min>max保raw/Boundsfalse，不生成FinalGrade或PolicyFP；21bool22int保原返回，槽Before/After同prefixclass/ptr，FinalizerCLR。20整袋unknownscope遮singlefish，原exception/reentry/thread/budget/root错配撤证并清gradecandidate，无收益能力。
- 新品质budget16/sample、65536/process不可toggle reset，正常品质samplerprefix12/after2，不含原Frame/instance及四bagweight诊断。typed业务/SaveRoot不主动调用，只原游戏自然方法，exactLootBoxSlot profile不支持CellData时明确Unavailable。
- 实际新Cecil4types/3hooks/3instance int directfields/2savebusiness声明/输入hashsame。新两PE组96方法6212指令920edges78ranges24indirect，3invalid触方法quota；14方法2513指令475edges22ranges3indirect，2invalid。文本6209/2511无省略≠fullmethod，原地址/IL/文本只.local。
- 已知automatic原slot nonnull委托→AddFromLootBox，但MethodInfo未唯一；UI fish/normal callback传null，只证明使用该CellData.TotalCount，上游Convert/OnPostProcessMapping未知，不能据此直接用原capture rawCount。最初Direct结论已按此输入范围纠正；所有实际模式/品质/集合/employee政策仍false。
- 本轮实际Core/TCP269/269，2新lineage夹具验证unknown全袋遮源、independentbool/int、postfix仍scope/正常Final恢复、exception丢parent/queued历史不授权/旧Run和replay。编译警告视为错误通过，SHA256 `048382538EBF2348AB83711861121C4C0E40FCA903FB5AE261B1D10A92EEB950`；144执行前seal输入后same，86actualCore源。
- 新RETURN_GRADE_OBSERVATION、真实构建/currentCore摘要、计划/接手/log/Skill同步；.33 mapping和.32 plan历史保持。下一步UI上游数量/automatic delegate MethodInfo、真实返航根/Exp/Return/Member绑定，接employee政策/实际捕鱼分流/仓库delta/save；guest/world/员工actor与双实例正常闭环/GitHub冷配置继续必需。
- 两名独立源码/配对只读复核及最终文档审查READY，正式Skill校验/protected copy与hash匹配，SHA256 `E2FF0272428DB08C51DEA9B6C21BFD20BE4D2BEAFA994A8DDA8A2850F356CBE9`。144执行前后输入和86实际Core源相同，原binary/metadata/两个interop/安装DLL最终hash匹配；新鲜Get-Process game进程0、不终止进程。明确品质12/2额外不包含原weight读取，UI null不推raw模式；历史摘要保持，原指令不发布。

## 2026-10-04 — 0.1.35 自然鱼叉头显示与数量来源

- 前轮3d2b72c已推送；用户询问进度，本轮明确仍未达到完整双人捕鱼/返航入仓可玩阶段。保持每人独立容量/负重、房主长期进度与完整M3—M7目标；未部署/启动或读写存档。
- 新LocalHarpoonVisualCapture以manager当前玩家、m_InstanceItemInven、handler→projectile/renderer direct链及反向handler/Owner/scene/identity前后核对，冻结1枚头追加现有PlayerFrame真实TCP/Renderer。ownhead排除身体集合并动态去重，reserved显示代次不传nativeID；超限/读错只omit，清当前template，未知不借旧头。两dict部分登记回滚，template每帧SpriteKey活匹配/失配恢复默认，不套身体材质；新增8诊断。无Core/协议/射击碰撞/收益权限改动。
- 真实Host/Guest发布各自本机角色帧，只有Local test偏移；Ready仍严格Scene/WorldFingerprint，相异随机地图会Waiting。本轮接线不替代客机隔离/房主世界或可信员工actor/武器；自然头Owner/scene窄profile、跨端素材、发射/收回/换装备和正常返航清理仍待实机。
- 实际新Cecil核4请求types/6properties(5direct+1业务getter)/2inventory候选，使用inventory direct而非业务getter；另1type/6properties窄核manager玩家backing，新检查不主动用playerCharacter getter。原Assembly输入hash匹配，原报告只.local，不执行native/game。
- 数量新解析2encodedmetadata globals/4slotfields，唯一绑定IngredientStorage原槽GetExchangeCount委托；原ItemID/TotalCount/rawGrade兑换，入仓另FinalGrade。复用旧GetExchangeCount48指令不计新PE执行。UI新Cecil1assembly/15types/16decl，新PE16methods1298instr190edges18ranges10indirectcalls，无invalid/quota/rootomit/textomit，1leaf无已知range。鱼UI已兑换再按ID/lift累计、分组不比品质；Roe按FinalGrade分档且走鱼场。Convert链含SetOldLooting，不能主动当纯helper；完整mapping/virtual/category/formula/ABI/员工政策仍未知。
- 实际Build警告视为错误通过，DLL SHA256 `00295B8B0E56F86C84B265F632F9388239878A0D46A830C2FF334FD53C602E34`。145源/项目/runner输入在Build前seal、后同；86实际Core输入与0.1.34一致，复用其269/269原执行与时间，本轮未重跑/无新测试。两名非作者只读源码及文档审查READY；原指令/字段偏移/地址/报告不发布。
- HARPOON_VISUAL、RETURN_COUNT_POLICY、新构建摘要/currentCore/12入口状态/计划/接手/Skill同步；旧.34自然观察与.33/.32结算证据保留历史。Skill官方校验、protected copy及hash匹配，SHA256 `1F7B827F563815A5EE88BFDCA588973F19D567E779FCCD27702503539F1259B2`。最终145/86输入同，原game/metadata/两个interop/安装DLLhash保持，新鲜Get-Process进程0、不终止进程；安装.12/潜水.11/default.0保持。
- 下一步实际加载前房主地图采用与guest隔离、可信员工actor/装备、命中与个人完整产物分流，再用真实Expedition/Return/Member上下文固定数量/最终品质政策及逐项仓库delta/save。真实双游戏正常返航、武器生存与GitHub冷配置仍必需；不将鱼叉显示、CLR测试或静态数量解析算作完整玩法。

## 2026-10-04 — 0.1.36 当前房主候选消费者与实际加载边界

- 前轮67db1db已推送并核remote；本轮继续完整M3—M7与每人独立袋，不部署、启动或执行游戏/存档业务。
- MapChoiceController新TryCaptureRemoteChoices只接受同reference已绑定Guest/liveRoom，立即消费真实TCP邮箱，深复制route/scene/IGP；复制前后核room/gen/rev/fingerprint，partial新代次或retire/Close/头不符撤旧。true仍可Route=null或Retired，完整route不代表完整IGP或Ready；尚无自然原生加载调用方。Update/Loopback原行为保留，Guest本地stop不撤Host。非作者只读源码审查READY。
- 新2真实TCP用例已在Program注册：即时刷新/owned mutation/同代次choice更新/host retire，逐页真实TCP换代首片撤旧/mailbox被取走/Clear-rebind/wrongpeer/Host/Close。source origin仍synthetic CLR，不证明native来源。实际Core/TCP271/271，UTC17:14:44.7607724→17:14:48.8999740；Build警告视为错误通过，UTC17:14:48.9021134→17:14:50.2710791。DLL SHA256 `7F793A2ACE1594CD04E9C7A5A4CF93423F4FA2B88DEAF3938C5651D81B711F63`。145执行前封存输入最终相同，86实际Core输入，本轮真实重跑而非复用旧269。
- 新三PE私有报告共19roots/25methods/5085decoded/5082text，3partial+1Unavailable，无quota/root/textomit/trunc。两map组12methods1973instr与5methods2079instr，guest组8methods1033instr；Cecil另核1assembly/12types/7decl/269direct/Missing0。原始指令/地址/offset只.local，公开摘要仅自写结论/count/time/hash；CompleteMethod/ABI/runtime全未知。
- 实际原入口确认cache写SaveData，路线须cache/roadmap/首尾/高度/schema一致，IGP可skip随机返回唯一匹配本地info；未收到选择需原MoveNext异步等帧，returnnull会假完成。manager factory早于exactoperation成功时冻结owner0且不补绑定，真实Unity顺序仍未证，不能声称host route生产必定成立。
- LoadSavedData发起SaveSystem.Init，不是postload；InitAfterSaveSystem首次MoveNext位于自然缓存/任务/奖励初始化前，但现七根桥缺cache不能自然出生，实际接线仍待做。RestoreRoot需先证消费者退休，不能等卸fence/free前才查quiet；当前硬拒进入/静止条件与全部Native/Guest/HostSelection权限false保持。
- 新MAP_ADOPTION_ENTRY/currentcore/本轮实际摘要/12入口状态、HANDOFF/PLAN/WORLD/GUEST_ENTRY及Skill同步；旧.35鱼叉显示和返航数量证据保历史。非作者文档/证据末审READY；Skill官方validator与protected copy/hash匹配，SHA256 `26FD8A6E8F1AF6A69BD42D0B453B851ED04F6C2A1859A0B08BA09FF42421961C`。本轮安装.12/最近潜水.11/default.0不变，不把退出或旧画面记正常返航。
- 下一步实际guest初始化隔离边界、完整route采用与IGP等待/替换，再接可信员工actor/生存/装备、房主命中/每人完整产物分流与返航仓库delta/save。真实双游戏正常闭环和GitHub冷配置继续为完成条件；当前目标保持active，未把候选API或离线研究称为可玩联机。

## 2026-10-04 — 0.1.37 客机自然初始化source与五根启动接线

- 0.1.36候选消费者、边界研究及271项验证保持历史；本轮继续完整M3—M7和每人独立袋/容量/重量/负重、房主长期进度目标，没有部署、启动游戏、运行native业务或读写存档。
- Plugin新增默认false的Startup/ExperimentalGuestInitialization，先装自己的Natural输出围栏及5个exact自然source hook。早callback只留同installation thread opaque wrappers；实际Diagnostics.Update才确认native-read线程。Network首次Update自动按配置host/port加入Guest，握手固定实际peer/Room；配对Awake_Impl/LoadSavedData/LoadAllData/原InitAfter factory及returnediterator，首MoveNext需state0/currentnull/同GameBase、5非空根/4manager与已列冷缓存前后身份。
- 输出围栏兼容旧Existing194；Natural197=156初阻+41deferred，clone前一次Seal全部目标，不移除persist hooks。新增Il2Cpp File.Delete(string)及Copy两overload，离线Cecil3/3核准；缺签名拒Natural安装，任何尝试BlockedFileOperations递增并锁存失败，不把skipvoid当Copy成功。exact白名单不宽泛放Load，CreateNewAndSave4处仍blocked；partial/未知保持owner/fence，不retry。writer全覆盖/共享地址/ABI仍未证。
- 同一个source-owned fence与实际opaque lease进入NativeGuestShadowBridge+GuestShadowTransaction固定Natural5根；原Interaction baseline先于serializer，四Data typed roundtrip、新Interaction绑定detachedPlayer、根逐项一次写/回读。Natural不用旧Ingredients/Ingame clone补空，而是在临时5根确认后放行原初始化，让已列cache自然出生；旧Existing7进入仍硬false，不改原flags/路径/slot/cloud。fullSaveGraph/cache/资源/首次加载顺序及完整guest隔离仍false。
- Core固定profile/rootorder且snapshot owned；补偿在开始及每个RestoreRoot前核actualquiet，再核binding/readback。生产Natural quiet恒false，因此失败/Disconnect不恢复个人根、不unpatch/free，保留source3+bridge最多15额外strong handles及managed owner，切role必须新进程。RootShadowInstalled只是5根标志，不授Guest/Native/World/Cargo/HostSelection权限；没有房主地图原生消费者或可玩员工分流。
- 本轮实际Test-Core退出0，tail为275/275 tests passed.、工具walltime4.55秒，4新夹具为五根profile、profile不可变、非quiet失败不补偿、每root恢复前freshquiet；仅instrumented synthetic backend，不native。完整stdout及确切测试起止UTC未留，86Core输入在成功执行后采hash，不能称Core执行前seal/前后相同，也不是历史271复用。
- 最终插件Build警告视为错误通过，UTC2026-10-04T17:54:04.5287763Z→17:54:05.9503523Z；147插件输入执行前封存且Build前后字节一致。DLL SHA256 `FB608E8763EBC4CCE0F7BF231ED43DD550110A1D438D7FBED12622132C4E71D0`，实际证据见[本轮摘要](guest-initialization-build-verification.json)与currentcore。
- 新GUEST_INITIALIZATION_BOOTSTRAP和12当前入口headers、HANDOFF/PLAN/WORLD、GUEST_ENTRY_BOUNDARIES/OUTPUT_FENCE/COLD_PROFILE/SHADOW_BRIDGE/RUNTIME_CACHES及开发约定已按实际源码同步；旧.36的271/Build及边界研究保留历史。正式Skill已通过官方校验并同步，SHA256 `7904219EC063226D7CEC3CC69F6CDCCFB6D3D5C4EF6306FADC4C1C417BF24ED6`；原interop/Cecil报告及地址/IL只.local。安装.12、最近潜水.11、默认包.0保持；本文档子任务没有执行额外游戏进程或安装文件检查，不扩大实机证据。
- 下一步验证真实冷启动来源/根/缓存/持久输出与未知保留，接完整房主route和IGP自然采用，再接可信员工actor/装备/生存/命中、每人完整产物与容量分流及返航仓库delta/save。真实双端正常闭环和GitHub冷配置仍待完成，不把source接线、275合成/TCP或Build当M4/完整隔离验收。

- 两名非作者源码末审与公开证据/Skill/文档末审READY；已修确认实际Update前读取Pointer、首Move原生预检前重入、异线程配对、native Exception.Message及日志回调后放行等具体缺陷。最终仅文档调整，147插件构建输入仍相同，不重复运行Core或Build；原生初始化、fence安装及5根业务尚未执行。

## 2026-10-04 — 0.1.38 房主路线加载前采用与pending-manager真实来源

- 继续完整M3—M7及每人独立袋/容量/重量/负重、房主长期进度目标，未部署/启动游戏或运行原生业务，不读取/写入存档。当前安装.12、最近潜水.11、默认发行包.0保历史；本轮没有另查游戏进程或安装DLL。
- 源码0.1.38-dev/协议7；route-v2/selection-v2纳Priority、PreferenceWeight、PreloadAndNotUnloadable、TotalSceneHeight，复制/分片/拼装/身份贯通。Decode要求实际字段存在且类型正确，合法0/false可通过；总高度只按finite/有界校验不猜与各层求和，本地IsSceneLoaded独立置false。旧协议6拒绝。
- 房主实际Host在BindRoom来源floor前启用自己的origin producer；观察目标29→30，新增InGameManager.OnDestroy_Impl。manager实际出生冻结scene handle、当时eligible operation集合与actor/iterator身份；只有精确operation同owner及birth-handle完成才绑定/提交pending路线，不从后来singleton/当前owner猜。32 managers、64 operation wrappers/实际Move scope，newentry/unload/destroy/exception/quota撤证。
- Guest默认false启动模式依.37实际自然五根source与真实peer-room，独立原map-call/origin observer互斥。新NativeGuestMapController的9个exact声明绑定GoToEntry→原CoChange factory返回→首state0/currentnull异步等完整route→同fixedMove的SceneLoader原Reset正常返回→staticCoLoad factory前六根安装→固定原childMove加载前后复核。支持StartCoroutine在原Entry主体返回前的同步首Move，允许后续yield/state/current自然变化，未知嵌套遮父，原skip不伪成功。源码未执行native Patch/ctor/字段写。
- actualCatalog固定DataManager/dictionary指针及count/free/version/entries/buckets；SceneData只读direct backing五scalar，不用RuntimeInvoke业务getter。唯一独立nativeLayer id/name匹配且hostentry catalog/type/additive/diving相容，才在首CoChange之前一次绑定sceneData；无layer证据保原bootstrap/key/mode/activate，InitialSceneProfileVerified恒false。原Mod分配的任何旧layer list不作为下一轮独立证明；context在Reset原返回后即绑定、Clear/Reset/Cache清理撤销，partial准备也不复活。
- NativeGuestMapRoute按Entry→Next/Previous链构造三类普通native record、明确capacity原生BCL容器与独立exact int comparer，写/回读cache/roadmap/首尾/list/总高度六根，各一次。record raw allocation先strong hold再wrapper；ctor返回前未知内部allocation完整保留未证。来源32entries×7=224明确handles；每路线最多106handles、32retained owners、每方法8192guarded steps，全部unknown保持owner/refs/partial roots、无热restore/unpatch/free/retry，不开World/Cargo/完整Guest权限。
- 非作者真实入口/窗口审查修复Manager-only不可达入口、SceneLoader Reset实际decl、Entry同步首Move、out-token异常清窗、bootstrap无context、末尾source失效、旧Mod list自举证明等问题；第二非作者核host integration/pending-manager生产链，均READY。完整nativeABI/控制流/共享鱼AI及IGP仍未验收。
- 首验证Core284/285，旧adapter测试绑定“not implemented”提示文本；修为实际owned observation-only candidate、host-selection false及真实WaitingForScene/epoch合同，不据文案授权限。第二验证Core285/285而Build CS0619拒绝旧5参Harmony.Patch；改为项目现用named参数重载。失败stdout/输入保留私下，不写成通过。
- 最终实际Test-Core（包含编译）UTC `2026-10-04T18:43:35.4098120Z`→`2026-10-04T18:43:40.5384050Z`，285/285：4新路线schema/字段明确存在/TCP夹具、6新manager出生来源/生命周期夹具；完整stdout/PASS列表与Program全部注册逐一一致，只是CLR合成和实际回环TCP。87份实际Core源码、104份插件源码及项目/执行脚本分别按92/109份核对；149份联合输入含两个私有验证器执行前封存、自写bytes保留、执行后同hash；所选工具/显式编译引用另封存，不称完整OS/SDK传递依赖。
- 最终Build警告视为错误通过，UTC `2026-10-04T18:43:41.8929187Z`→`2026-10-04T18:43:44.0024187Z`；插件SHA256 `BAD8A38F0B96F10895EA3E716BF3CAB719997C156BE4C020A89C3C22A483105B`，测试DLLSHA256 `350F174DDA3B48D123BC89C3B236E83F953195B4257062012728EDFF485AC907`。真实证据见[本轮摘要](map-route-adoption-build-verification.json)和currentcore，旧.37的275/postrun-only Core输入记录保持，不把本次preseal追补旧轮。
- 离线两份Cecil实际4types/56properties/13ctors与6types/522properties/22selectedmethods，不将全部properties说成direct。新PE实际8roots/11methods/2128decoded/2127text/9indirect，有1partial/1unavailable/1invalid，完整body/runtime order未证。自写摘要附UTC/输入和report哈希，原PE/metadata/IL/地址/offset只.local，未执行原生。
- 新GUEST_MAP_ROUTE_ADOPTION、12当前入口header、HANDOFF/PLAN/WORLD、MAP_ORIGINS/ORIGIN_MAP_TRANSPORT/Guest边界与fence历史路由已同步；Skill官方validator通过、protected目标hash与draft相同，SHA256 `CE0BB8DB20179B7D8BC304243152F7415F6A5BF8F2ACC2507959D6938C299EA4`。所有游戏能力/IGP/完整Guest/世界/货袋/正常返航验收仍false。
- 下一步接实际IGP.Init固定来源与未收到选择时异步等待、唯一local info，再完成生成与AI隔离、可信员工actor/装备/生存/投射物、房主原生命中、每人完整产物/前置容量/重量/负重分流及员工逐项返航仓库delta/save。完整真实双端正常闭环和GitHub冷配置仍待实现，goal保持active。

- 末次非作者源码及文档/证据/Skill审查READY；修正4处旧当前协议措辞并复验Skill。最终只有文档/记录变化，149份已编译输入仍相同，不重复运行Core/Build；公开提交只含47份自写源码、测试、文档/Skill与sanitized摘要，原生/双端验收未完成。

## 2026-10-04 — 0.1.39-dev：原IGP选择采用与实际场景来源

- 上轮状态核对发现controller出生只冻结owner、未冻结preexisting ops，改变本轮接入顺序；本轮先补真实来源，再接固定原Init等待与唯一localinfo，不缩减完整M3—M7。
- Guest Scene source12声明，既有Map9加IGP5共26注册/24不同声明；真实原factory/typediterator范围绑定Addressables原五参typed结果、op/version/status/Scene，completed op保留并在controller使用前后fresh重查。初始自然加载与ordinary换层/additive核actualcatalog、helper实际allocation road身份及loaded-native-manager scene。
- Core controller birth冻结出生前operation集合，重复不扩，later同owner/handle不可认领；专用controller原factory iterator可等待精确completion，generic owner0不升级。Host自然capture已接专用iterator、exacttypedactor与pending映射/resume Poll，来源快照只观察、不授权限。
- 原Init0/1/2无choice/source保持state/current等帧；原GetRandom仅一回返回唯一匹配localinfo，不主动调用随机/条件/Saveable。staticDone不漏waiting birth，但未开始future层和unknownbootstrap不构成等待；当前loaded scene必须actualsource与host显式groups相符。completed实际IsInitDone/CurrInfo/CurrSet复核，旧已退休entry不当新entry等待。
- 本地helperlist/array结构/version/actor/info原指针及key冻结；addressable key-only、原选择后Prefab空→live一次先标attempt再hold，active验证不重扫/重Roll，nonaddr须livePrefab name。每helper5明确handles/256 retained owners，8192每次steps。Scene来源64process ops、256iterators/controller、832refs、8192每Execute reads，无逐帧永久累计cap。
- 两位非作者逐段审查，实际修复unknown factory mask/parent0或非Move重新认owner、expired与failed回普通body、bootstrap key别名、空token提前Done或future层等待死锁、oldentry退休过滤、完成op丢nativefresh校验、GC分配postguard留存和normalPrefab填入误拒。最后源码差量READY；nativeABI/真实控制流/跨机地址/完整资源/AI/隔离仍未证。
- 实际Test-Core编译加测试UTC `2026-10-04T19:36:59.0729018Z`→`2026-10-04T19:37:05.2937318Z`：292/292，7新controllerbirth/iterator/source/lifecycle/thread/quota夹具与原285全执行；完整stdout的PASS顺序逐一匹配Program。实际Core88源码/93验证输入，插件107源码/112验证输入；153联合源/项目/执行及两个私有验证器前封存并保自写bytes，Core/Build后同hash；显式编译引用和所选PowerShell/dotnet/csc另封存，不称完整OS/SDK传递依赖。
- Build警告视为错误UTC `2026-10-04T19:37:07.0724571Z`→`2026-10-04T19:37:10.0487019Z`通过；插件SHA256 `E320D347AB95C46BCC11511C6849FE0D39F3045CC2270C7F230E655DFBD38A23`，测试DLLSHA256 `6DC8F7905B7A209397774964B34A2B41357250A8A45561F0FC471C04570CC7C2`。首次统一292/292与Build通过后，末审发现HostRetire或新generation尚未锁存Failed时，IGP三个入口可能误放行；补VerifyGenerationWindow后重新完整封存并执行最终统一验证。首次成功输入与输出保留，最终摘要只引用修后版本；没有失败被记成通过，.38/.37旧摘要保持。新证据见guest-igp-adoption-build-verification.json及currentcore。
- freshCecil私有report实际5types/28properties（22direct）/21selectedmethods，UTC19:06:40.1855668→19:06:41.8273413Z，输入F411前后相同；公开只自写说明/count/UTC/hash，原metadata/wrapperIL/地址不提交。
- 用户询问主客距离远的显示：当前只读鱼来源限房主当前场景，镜头外节点隐藏保身份；同层两端独立镜头是首版方案，还须房主维护两人附近区域，自由跨层需扩多场景模拟，未作为已实现。
- 未部署/启动、运行native或读写存档；安装.12/最近潜水.11/default.0保持，不催延后测试。所有Native/GuestStateIsolated/HostSelectionApplied通用权限/World/Cargo仍false；Addressables返回前无eligibleop出生明确拒绝而非覆盖声明。后续继续完整生成/AI/持久隔离、可信员工actor/equip/O2/HP/projectile、房主命中和每人独立完整产物/前置容量/重量/负重分流、逐项返航warehouse delta/save、真实双端正常闭环/GitHub冷配置和测试发行，goal保持active。

- 配置Skill draft与protected目标SHA256一致，官方quick_validate两处通过：`FB069B5D79E2F9993AF37897F82C98B1DDB8C1EAAD09098CB4C80C81BAF938A2`。旧.38/.37摘要与HEAD归一换行内容一致，153最终编译输入仍同，文档收尾不再重跑Core或Build。已将自然返航清理后普通无所属加载可能被已安装来源拒绝的已知限制写入新文档；完整返航仍需接线和实机验证。

- 非作者源码审查、公开文档/证据/Skill审查及发布边界审查均READY；修正7项新Registry夹具与整套既有TCP的范围措辞。待公开32份文件仅自写源码、测试、项目、文档/Skill与数量/时间/hash摘要，不含游戏二进制、原metadata/IL/地址或机器日志；最终编译输入未改。

## 2026-10-04 — 0.1.40 原加载调用出生来源与自然退休

- 上轮.39已公开1304c57，本轮继续完整M3—M7；未部署/启动、执行native或读写存档，安装.12/潜水.11/default.0保持。
- actual fixedMove加载prefix mint单次call，原typed返回__runOriginal=true才冻结ptr/version/key，finalizer LIFO收尾；birth冻结真实ancestor在途call+已有operation，重复不扩，later op不可借。Host与Guest实际prefix/postfix已接该producer，原IGP birth移至mask前，Host工厂birth后pair mask。
- 专用pendingManager仅同actualscene子birth且已冻结同call的真实iterator范围可继承call候选；generic/未知孙0仍遮断，owner0不新增load/operation/world权限。异常/skip/unload/失源/thread/quota撤证，MaxLoadCalls128process tombstones不淘汰。
- 精确Context自然退休设置本producer marker，旧owner inactive且healthy、correctthread、无inflight和无fixedscope才保原普通factory/Addressables/unknownMove；自产unknown masks可保到finalizer退出，已知old initial/scene/controller iterator即使新entry也拒。根/强引用/fence保持、failed不解，完整正常返航/保存仍未验。
- 最终实际Core编译与测试UTC `2026-10-04T19:56:30.1284050Z`→`2026-10-04T19:56:36.1458718Z`，301/301（9新Registry+原292全执行，原套含真实TCP）；stdout完整PASS顺序与Program吻合。Core实际89源码、插件107源码，联合154输入含项目/脚本/两私有验证器执行前封存并留自写bytes、后同hash；所选工具/显式refs另封存，不称完整OS/SDK closure。
- Build警告视为错误UTC `2026-10-04T19:56:37.8682782Z`→`2026-10-04T19:56:40.3847680Z`通过，插件SHA `C6E358DC9A13F5E8EB79F01CD6E2C58CA1B7F58F620B270B31B923BA6B587668`，测试DLLSHA `F919608BC5B23D96ABCFDE9E702824E164B3986AD5AC4F3BFF38A7BF3987194F`。新摘要scene-load-lifecycle-build-verification.json/currentcore；旧.39/.38/.37不回填。
- 新离线Cecil3程序集/9types/133相关声明/缺类型0；两PE10roots/23methods/2012decoded=text、partial/unavailable/invalid/quota0、7indirectcalls/1branch/3alias-truncatededges，仍有共享别名/泛型/Unity icall未知，不证明完整body或实际同步出生。原metadata/PE指令/地址/report只.local，公开自写说明/count/UTC/hash。
- 新doc/12headers/HANDOFF/PLAN/WORLD/旧IGP后续路由/Skill draft同步。完整Guest生成/AI/持久隔离、可信员工actor/equip/生存/命中、每人完整产物与前置容量/重量/负重分流、员工逐项返航warehouse delta/save、真实双端闭环/GitHub冷配置及测试发行仍待完成；far-region/free-crosslayer仍未实现，goal保持active。

- Skill草稿及protected目标官方校验通过并同SHA256 `2DFDE65B10512F6C841E062BCCFDEDA4A966BE180DEB70E6490450D5DB2E4A1D`；最终154份编译输入保持，旧.39/.38/.37摘要与HEAD归一换行内容一致，未重复运行Core/Build。非作者源码、文档/证据/Skill与发布边界末审完成后只提交自写文件；原生加载、正常返航及完整双端验收仍未执行。

- 三项末审最终READY，公开28份自写文本：源码/测试/项目、文档与Skill、相对路径/count/hash/UTC摘要。修正Host原执行标志字段为CaptureImplemented，未把源码已实现写成原生已执行；153→154联合输入来自新增实际fixture源码，Core89/Plugin107及94/112验证输入均与实际编译清单匹配。原metadata/IL/PE指令/地址/报告与机器日志不提交。

## 2026-10-04 — 0.1.41 客机鱼隔离与自动房主观察

- 上轮.40公开4d904c3，继续完整M3—M7；未部署/启动、执行native或读写存档，安装.12/潜水.11/default.0保持。
- 实际FishAwake出生复用固定op/call/Scene来源，IGP256与fish4096独立配额、共享不可复用pointer/life；鱼不能获IGP choice/Inititerator权限。隔离root与actor引用/inert记录先于单次停用，精确受管root生命周期/已核交互入口阻断；异步外部更新/未知类型或子组件先Awake/重新启用撤source，不假造Observable/IEnumerator成功。完整覆盖及native执行顺序仍未证。
- 协议8Hello/Welcome必须显式唯一bool，身份在首await前owned复制并保会话；仅请求Host自动活动鱼观察。Guest自动显示Receive/Render前后核actualstartup/samepeer/source；失源清自己节点，refs/fence/墓碑保留、无hotrestore。普通诊断沿原手动，World/Cargo/GuestStateIsolated等仍false。
- 最终实际Core编译与测试UTC `2026-10-04T20:28:06.2511555Z`→`2026-10-04T20:28:12.0309763Z`，311/311，完整PASS顺序与Program吻合（新增6 Registry＋4 codec/TCP，原套全执行）。Core实际91源码/Plugin108源码，联合157输入执行前保自写bytes并封存，Core/Build后相同；所选工具与显式引用另hash，不称完整OS/SDK closure。
- Build警告视为错误UTC `2026-10-04T20:28:13.7377344Z`→`2026-10-04T20:28:16.4838335Z`通过；插件SHA `A095D878B1C38416A65DB875544EC13903034CF56C02AD631F6F57C035622E4A`，测试DLLSHA `62DDDFA7470CED0BB8C87619EB93C1A1610C94B1E5A6D2F77FE0FE26485A606C`。新摘要guest-fish-isolation-build-verification/currentcore；旧.40/.39/.38/.37不回填。
- Fresh离线metadata与一次bounded PE只.local，公开相对路径/count/UTC/hash与自写说明；间接调用/共享alias/未知edges保留，不声称完整body、自然同步出生或全部Unity脚本顺序通过。
- 用户再次询问远距离显示：同层各自镜头方案仍需Host联合两人活动区域与allocator/LOD接线；当前仅Host当前场景活动鱼，不保证远处未生成/停更鱼或自由跨层。每人独立袋/容量/重量/负重保持，可信员工actor/捕鱼/产物分流/逐项返航delta/save及真实双端/GitHub冷配置仍待完成，goal保持active。

- Skill草稿及protected目标官方校验通过并同SHA256 `88BCF335D673E2B7F4F8B48255E8843E0FB6CF4353CDCE0BAAF3C317B0A46A0F`；最终157份编译输入保持，旧.40/.39/.38/.37摘要与HEAD归一换行内容一致，未重复运行Core/Build。原生鱼隔离/完整覆盖、远区域/两玩家联合LOD及正常返航/完整双端验收仍未执行。

- 三份新私有报告对应相对路径/hash/UTC及各自counts；PE为8roots/9methods/3106decoded=text、14间接调用、327未解析精确入口边/7别名截断边，UnresolvedFlowInstructions=0是另一指标，不混作0未知边。已知unwind解码不证明完整body/脚本顺序；此前未保留的不完整96声明统计已废弃，fresh实际21types/118targets/error0。仅补记录/文档，不修改已验证源码或重复测试。

- 原生源码链、文档/证据/Skill与发布边界三项非作者末审READY；38份公开自写文本，157最终编译/执行输入不变。完整PE计数已补入两当前摘要，未知入口边与方法完整性未证明确保留；补入同层远距离联合两成员活动区的实施和验收条件，尚未实现。按既有授权公开源码/文档/摘要，游戏二进制、存档、原metadata/IL/PE指令与机器日志仍只留本机。

## 2026-10-04 — 0.1.42-dev：房主双成员鱼区域

- 用户询问主客相距很远时的显示，继续同层两独立兴趣区域；没有把两人间所有海域启用，也不将观察坐标当可信员工或捕鱼许可。
- 新actual TCP receipt Room/member/sequence和Unity-thread source；失去current Host/Ready/samepeer/epoch/scene/本地identity或原接收时间过期立即撤源，Local test排除，插值显示不作来源。默认关闭实验入口首次Update读取，未改游戏配置。
- 普通生成器固定原typedMove同步来源、实际Transform.position/同allocator中心一次配对，min0/!force限定；只代理距离查询返回值，原getter缓存、真实中心、随机、保存身份、原body保持。未知/嵌套来源遮断，Wave保原，StopAlloc未用作区域开关。
- 中央LOD保自然RequestManagement实际fish/target/data寿命，原Complete等待匹配原job正常返回后、消费前只改绑定鱼newLayer，合并独立两区域并保迟滞/Z/Behaviour/原生命周期。typedNativeArray仅实际buffer身份转换需unsafe；不猜offset/未join写数组/强改GO。group和线程前注册不补猜；原生时序/ABI、完整fish覆盖与远处Renderer.isVisible避让仍未证。
- 实际Core/TCP 323/323通过，UTC `2026-10-04T21:18:16.9413064Z` → `2026-10-04T21:18:22.4146304Z`；完整PASS逐一对应Program；12新增兴趣来源/数学夹具，只有CLR与实际回环TCP。实际Core源码97份、插件源码114份、联合输入166份执行前封存并保自写bytes、Core/Build后同hash。
- 插件Build警告视为错误通过，UTC `2026-10-04T21:18:24.0118168Z` → `2026-10-04T21:18:26.3555546Z`，SHA256 `F26172ADF5074D30E81792C36C557C011CF289B5C9EAB7B2E82BCFDC46EF4E60`；摘要见[本轮记录](host-fish-interest-build-verification.json)。显式新interop引用仅编译，不公开依赖DLL；所选工具/reference不是完整OS/SDK闭包。旧.41/.40/.39/.38/.37摘要保持历史，不把新验证追补旧轮。
- HOST_FISH_INTEREST、当前入口、接手与PLAN/WORLD和配置Skill同步；原生远距离、Guest完整隔离、World/Cargo权限仍false。未部署/启动或读写存档，不催延后测试；完整M3—M7、个人袋/负重、原生员工命中、逐项正常返航保存、真实双端和GitHub冷配置仍待完成，goal保持active。

- 初次统一验证实际Core323/323通过但插件两处接口编译失败；私有完整stdout/stderr与seal保留。仅修Harmony.Patch命名新重载和真实Il2CppArrayBase类型后重新封存，最终Core323/323和Build通过；未为失败轮写成功摘要。
- 配置Skill官方draft/protected校验均通过，目标与draft同SHA256 `248508A0506CA6DC9A60ACBD3EA60A79013186B4CB6EAD0F61E82258FCB894F9`。LOD4/allocator30仅预期声明，不是运行安装或ABI证据；LOD最多512rows/batch、4096records/32managers/24576显式handles及1retained owner/process，primitive/guard steps不含source复合getter内部全部native调用。实际性能与完整远处AI仍待验。

## 2026-10-04 — 0.1.43-dev：远处鱼避让与员工生产路径

- 上一轮仅核对用户远距离显示问题，本轮继续完整M3—M7；未部署/启动或读写存档，安装.12/潜水.11/default.0保持。
- 新默认关闭的原自然fishUpdate/同鱼renderer同步桥，保原true，只将员工附近的严格内含正交几何作为一次原false补充；未知/其他线程/嵌套/失源保原，不写鱼/renderer/GO或主动调用AI。原生ABI、多camera/shadow差异、全部鱼行为和实际性能待验。
- 独立员工actor与capture生产调用方离线研究形成新实施文档；现有选择/个人袋/返航桥需真实生产者，不能再次增恒false门禁或将guest pose当实际命中。后续接Host-owned物理角色与输入/批准装备/O2/HP/投射物，真实鱼命中终局及个人产物/容量分流、逐项返航。
- 实际Core/TCP 327/327通过，UTC `2026-10-04T21:40:37.0656545Z` → `2026-10-04T21:40:44.5454772Z`；完整PASS对应Program，新增4个几何边界夹具。实际Core源码99份、插件源码116份、联合输入169份执行前保自写bytes并封存，Core/Build后同hash。
- 插件Build警告视为错误通过，UTC `2026-10-04T21:40:46.4929215Z` → `2026-10-04T21:40:48.9181589Z`，SHA256 `BEC78C63A0B946875E2E96979B6AADC6AF8577B00BF455475A21D24DC8A7BAF8`；摘要见[本轮记录](host-fish-visibility-build-verification.json)。所选工具/reference不称完整OS/SDK闭包；旧.42/.41/.40/.39/.38/.37摘要保持历史。
- 接手/计划/世界与配置Skill更新；远距离、Guest完整隔离、员工玩法/两袋及返航保存、真实双端/GitHub冷配置与测试发行仍需完成，goal保持active。

- 非作者末审修正大坐标/tinyextent与边界微小平移的double区间外包、自定义camera双矩阵affine检查，以及originaltrue零额外原生读取；完整来源在读组与最终返回前复核，primitive读只作scope/thread/reentry守卫，实际性能仍未测。
- 新可见性Cecil实际2assemblies/16types/13methods/缺0；复用.42的338条Update已知范围，12未解析精确入口边不是本轮新PE或全body。员工研究新24types/846method/853property/缺精确名3，physics10types/136method另15property/20field/缺0；PE8roots/12methods/5123decoded=text、28indirectcalls/1branch，零省略/配额不是全方法/ABI证据。原metadata/IL/PE/地址及机器路径只.local，公开相对path/count/UTC/hash与自写方案。
- Skill官方草稿校验通过、复制后protected校验通过，同SHA256 `837F4863E0695AE14AF0A8DD0182CB48F1B3B515D64729446A728C5C0DF453C6`；2条visibility target为预期声明，非运行安装证明。全部169个编译/执行输入仍相同，只有文档/摘要/Skill收尾，没有重复Core/Build或执行游戏。

## 2026-10-04 — 0.1.44-dev：员工输入与房主移动接线

- 继续完整M3—M7，不部署/启动或读写存档，安装.12/潜水.11/default.0保持；用户延后实机，不催测。
- 协议9要求双方显式UsesCrewActor，拒绝8及更早；原PlayerFrame显示/地图schema2保留。实际Room/player2输入FIFO保持边沿与跨场景高水位，房主独立物理身体接受FixedUpdate移动并回读状态，客机临时角色核最新actor/state校正；不接客户端权威pose。
- 默认ExperimentalCrewActor=false，同peer/Room换层HP/O2继承，HP0不复活；氧气0只禁止boost，无真实HP损伤/窒息输入。首次新actor输入中立，F11面板保持中立，未知已进入源写不重派。身体命令提交不证明本帧已完成碰撞/位移。
- 实际Core/TCP 347/347通过，UTC `2026-10-04T22:09:11.8233844Z` → `2026-10-04T22:09:18.5093976Z`，包含编译及测试；完整stdout/PASS顺序与Program一致。新增CrewControlTests 9项、CrewTransportTests 11项，总数/注册从实际封存源推导；纯CLR与真实本机TCP，不运行Unity/backend。Core实际源码103份、插件源码121份、联合输入176份预先封存留自写bytes，Core/Build后hash相同。
- Build警告视为错误通过，UTC `2026-10-04T22:09:20.9813437Z` → `2026-10-04T22:09:24.3637842Z`，插件SHA256 `949006B0E34396AE14CAD53C7EC5894D981E22D2585784B5944706D1959EAE91`；见[本轮摘要](crew-actor-build-verification.json)。所选工具/显式引用封存不等于完整OS/SDK闭包；.43及更早摘要不回填。
- 本轮fresh离线Cecil证据：私有 `development/.local/analysis/employee-body-api.json`：Assemblies=2、Types=15、Methods=99、Properties=180、Missing=0，UTC `2026-10-04T21:58:43.0620124Z` → `2026-10-04T21:58:43.3717466Z`，报告SHA256 `505D8DE5457CE84F26F5C83EEE3F80F9DAB4284BBD2BED8B2D03C2BD86407F0D`；输入hash匹配实际编译引用。 原声明只留.local，不把metadata编译接口当nativeABI/Unity运行证明；.43 physics研究仅Inherited，未本轮重跑PE。
- 本轮没有执行原生身体创建/碰撞查询/客机校正/生存或两游戏。actor成员尚未绑定CargoLedger，BagWeightKg=null/HasConfirmedCargoWeight=false，profile容量不是货袋提交。装备/武器/投射物/真实命中、main+plus完整产物与前置容量分流、各自袋/重量/负重、逐项正常返航入仓保存、Guest全部输出隔离及真实双端/GitHub冷配置仍待完成；所有通用Native/World/Cargo/GuestStateIsolated权限false，goal保持active。
- 12当前header、当前协议说明、接手/计划/世界/员工范围及.43研究页历史intro同步；配置Skill只生成crew-actor私有草稿，正式官方校验/受保护复制另由root执行，不据草稿生成声称已通过。

- 非作者末审后拆分guest Rigidbody.transform与Transform.position读取，在首项setter前再次核最新state；写后固定receipt/actor来源，不声称全段latest原子性。第一次封存347/347和Build通过后因这一源码修改重封存并完整复测；两次独立private run保留，没有失败执行。
- 最终实际347/347、Build及176个联合输入封存前后相同；官方Skill草稿/受保护复制后双校验通过，同SHA `38CDA2387E0F9DCF8480C46E859176B36BA3A503091279C2E92CB6C12D7405B5`。后续仅文档/Skill/摘要收尾，不复跑测试或游戏。
- 身体8192预算计Read前后Check，静态已知分支估计Box4702/Capsule4530/Circle4400，排除wrapper内部native调用；不是实际CPU/ABI/碰撞证据。Stop回读与Destroy请求分开，引用/强handle保留，完整销毁未验证。

## 2026-10-04 — 0.1.45-dev 员工独立鱼叉

- 目的：让员工从房主自己创建的实际身体发射，保持真实碰撞/目标和一次原生伤害来源，不按显示副本命中。新增武器模型/10组CLR夹具、scene CircleCast投射物、fresh原生damage桥、guest独立显示和协议10四字段；修复跨actor活旧shot复用、旧revision取消顺序、guest显示失源清理与30Hz短按漏帧。
- 实际Core/TCP 358/358，UTC 2026-10-04T22:42:56.8112594Z → 2026-10-04T22:43:02.6068785Z；Build警告视为错误，UTC 2026-10-04T22:43:04.3951351Z → 2026-10-04T22:43:06.8142278Z。181联合输入执行前/后相同，完整stdout/stderr/PASS/rsp/源码/程序集私有保留；DLL SHA256 `DC8C72DB0A849DE08820D8AF9DA84A3D3B788308D930D2FF9B3E0781B7DAC20F`。
- 实际研究统计/UTC/hash范围见[新摘要](crew-harpoon-build-verification.json)，raw声明/PE/IL仅.local。离线已核声明和有限指令范围不证明Unity ABI、完整leaf或游戏方法；原TakeDamage bool仅观察。
- 非作者审查后收紧同Shot同actor延续、合法旧revision丢弃、Display cleanup失败锁存及每UnityUpdate即时按钮变化；未知已进入不重试、收益/捕获不确认。保留历史.44及更早摘要。未部署、启动、检查运行游戏/安装DLL或保存。
- 仍需真实原生投射物/普通鱼伤害/显示/远距离双端，员工incoming环境伤害/装备/完整敌方选择、完整捕获/产物前置分流、个人袋/容量/重量/负重、逐项返航save、Guest完整隔离/GitHub冷配置/测试发行；完整goal保持active。

- 本轮前两次独立封存验证均保留：第一次357/358（新夹具后段时钟回退，修正fixture时间，Build未运行）；第二次358/358、Build拒绝当前wrapper不存在的SceneManager.GetSceneByHandle；改从实际临时客机角色/GO读取Scene并核当前source后第三次完整Core/Build通过。未复用失败构建，未删除原run/stdout/stderr。

- 官方Skill草稿/受保护复制后实际双校验通过，同SHA `E62AB013E4A1A8CCF97E1A3A3E125C2DC3EB2344648C9EA2909B0123BE602197`；验证UTC与validator hash保在.local，摘要同步。源码封存保持，未复跑测试或启动游戏。
