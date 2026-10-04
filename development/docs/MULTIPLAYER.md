# 第二角色与传输层

当前源码 `0.1.14-dev`、协议 5，Build 警告视为错误通过、Test-Core 134/134 通过；本轮未部署/启动。
当前安装及最近新鲜启动为 `0.1.12-dev`/109 项测试，加载/Update/网络入口与 4 条初始 RouteInputs 已确认，仅主菜单启动通过。
Probe、潜水路线、场景切换与正常返航仍待实机；最近完成潜水验证的是 `0.1.11-dev`。
用户当前不方便试玩，手动潜水 Probe/路线/返航验证已延后，主菜单启动通过不扩展为玩法验收。
新版单游戏 TCP 偏移鱼群可见与原鱼移除时副本同步消失已获用户确认，关闭显示后恢复正常，操作和镜头正常。
发射/挂钩/伤害只读观察已运行；动画、完整捕获链、路线完整读取与正常返航仍待验收。
最近单鱼视觉稳定性确认来自 `0.1.9-dev`。
实际鱼传输/生命周期历史证据来自 `0.1.5/0.1.6-dev`，默认发布包仍为 0.1.0。
M2 在 0.1.2-dev 的真实潜水中通过基础验收。M3 会话/资源键/布局及插值通过核心测试，
0.1.5-dev 的实际潜水日志确认本机 TCP Ready、布局读取、角色/鱼资源解析及显示组件运行，
0.1.7-dev 用户确认预览鱼可见但会突然消失；日志定位到角色临时部件销毁触发自动断开。
0.1.8-dev 已修复该失败路径但仍自动换鱼。0.1.9-dev 锁定目标，用户确认不再突然消失；动画、Disconnect/返航及两游戏验收待完成。
启动证据见 `../logs/network-bootstrap-verification.json`，海洋同步、探针和一条鱼显示诊断见 [WORLD_SYNC](WORLD_SYNC.md)。
当前边界见 [地图选择传输构建摘要](../logs/map-choice-transport-build-verification.json)，0.1.13 历史构建见 [地图选择调用摘要](../logs/map-selection-call-build-verification.json)，已安装 0.1.12-dev 见 [操作门禁摘要](../logs/fish-action-gate-build-verification.json)，历史 0.1.11-dev 潜水边界见 [鱼群与交互摘要](../logs/fish-world-interaction-build-verification.json)。

## M2 显示对象

`Rendering/RemotePreviewController.cs` 从游戏管理器获取本地玩家，
寻找其子节点的 `SpriteRenderer`，新建独立显示节点。仅添加 Transform 和 SpriteRenderer，
不运行复制角色的 Animator、脚本、碰撞体或玩家初始化方法。
共享现有 Sprite/Material 资源，修改只作用于自己的显示节点；清理时销毁自己的节点。

每秒最多采集 30 个姿态，保留 120 帧。姿态包含根位置/旋转/缩放、显示部件的相对姿态、
Sprite 引用、颜色与显示顺序。本地回放默认延迟 1 秒，向右偏移 3 个世界单位并着淡蓝色。
连续姿态插值，离散 Sprite 使用对应时间的历史值；脚本瞬移直接切换到新姿态。
玩家实例或场景句柄变化、原角色消失、F10 停用或插件销毁时清空历史并清理节点。

F8 切换面板，F9 采集诊断，F10 切换回放。配置在插件 cfg 的 `Preview` 节：
`Enabled`、`DelaySeconds`（0.1..2.5）、`OffsetX`（-10..10）。

验收应覆盖：

- 入海能看到完整第二角色，移动和转向呈现延迟，隐藏/显示正常。
- 本地操作与摄像机正常；日志中的 `NativePlayers` 应保持原有玩家数量。
- 本地摄像机仍绑定本地玩家，`LocalCameraBound=true`。
- 切换场景及返航清理旧显示对象，没有遗留角色或连续错误。

日志：`DAVECOOP_PREVIEW_READY`、`DAVECOOP_PREVIEW_SPAWN`、
`DAVECOOP_PREVIEW_STATE`、`DAVECOOP_PREVIEW_CLEANUP`；失败为 `DAVECOOP_PREVIEW_WARNING`。
可见部件计数只能证明组件已启用，视觉验收需要实际观察。
实际 `A03_01_02` 中 37 条状态记录保持原生玩家数 1、摄像机本地绑定；
F10 停用/重建与返航清理有日志，用户确认可见并正常模仿动作，M2 基础验收通过。
角色部件随武器/效果生成和销毁改变，0.1.7-dev 已记录部件集重建，但周期检查与采样之间仍会销毁缓存部件。
0.1.8-dev 对销毁的缓存 SpriteRenderer 跳过该帧槽位，并安排下一次 Update 刷新列表，保持会话。
日志 NETWORK_PARTS_STALE 表示跳过并安排刷新，随后 NETWORK_PARTS_CHANGED 表示重建；需要核对连接及鱼清单继续更新。
核心测试不运行 Unity，此恢复路径须实机射击、装备变化与正常断开/返航验证。
其他地图、特殊材质、装备和剪切遮罩组合仍需更多覆盖。

## M3 协议与连接基础

`Core/Protocol` 和 `Core/Transport` 仅处理 CLR 数据与网络流，不引用 Unity。
游戏适配器在主线程处理已验证的数据，0.1.5-dev 已实际执行本机 TCP 路径；跨机器运行验证待完成。

- Hello/Welcome 验证协议、Mod、Steam Build 和 Unity 版本，分配房主 1 / 客机 2。
  当前源码协议版本 5 新增 MapRouteSlice/MapIgpChoice/MapChoiceRetire，保留协议 4 的 FishActionRequest/FishActionResult 和协议 3 的 WorldSlice/鱼显示描述，握手拒绝旧协议 4。
- 房间使用会话 GUID；每个方向使用连续序号，重复或跳号关闭连接。
- 使用 4 字节小端长度前缀，消息最大 128 KiB，循环读取支持 TCP 拆包。
- 发送互斥，避免并发消息字节交错；无效出站消息不占用序号。
- 验证有限数值、有效四元数、坐标/缩放范围、颜色、部件上限及重复部件槽位。
- 部件数据使用资源键与数字姿态，网络消息不包含 Sprite 或 Material 引用。
- 场景 epoch、确认/提交、暂停、心跳及时钟估算已由会话层实现并测试。

`Core/Session` 提供纯状态机与线程安全的游戏适配边界：

- `LanHost` 监听一个客机，握手后停止监听；`LanGuest` 连接房主。
  接受连接可取消，连接/握手有时限；失败时关闭相应连接。
- `SessionPeer` 各用一个读取、发送和定时任务；网络任务只访问 CLR 数据。
  游戏主线程调用 SetLocalScene、PublishFrame、TryTakeRemoteFrame、TryTakeEvent。
- 控制消息最多 32 条，状态事件最多 64 条，玩家收发帧各只保留最新一条。
  世界发送最多保留已开始的一批和下一批最新清单，接收最多保留一批拼装和最新完整清单。
  发布/接收快照深拷贝，防止调用方修改已排队的数据。
- 操作使用独立 FIFO：入站请求最多 16、出站动作/结果 32、入站结果 32、guest outstanding 32，不覆盖旧意图。
  请求源自握手绑定玩家，结果与 guest 自己 outstanding 的元数据/指纹精确核对；控制/心跳优先，动作与姿态/世界/地图四路公平轮转。
  旧 epoch 或非 Ready 请求交给房主 Gate 消费 ID 并明确拒绝；未来 epoch、错误当前场景和冒充来源拒绝。
  正常 pause/场景切换期间旧发布在会话锁内返回 false，保留连接；take 后切换不把旧请求/结果重发进新场景。
- 地图选择独立 FIFO 最多 32 包，路线每片 8 场景、最多 4 片；只由 host 发布、guest 接收，沿用握手 Room，单包单 payload 且小于 131072 字节。
  generation/revision 独立于 scene epoch，可在 WaitingForScene 传输；不改变 Ready、场景确认或 authority flags。新路线整批校验后原子入队并取消旧未发 batch；新代次首片先撤旧路线，完整拼装才提交。
  完整路线后 IGP revision 连续，同组新修订覆盖旧项，不猜完整 IGP 集合。地图队列满主动清队列并通过控制 MapChoiceRetire 撤销，重复 inactive Retire 返回 false，之后路线另开新 generation。
  普通场景/帧清理保留 preload 候选；显式 Retire 保留房间 generation 高水位，Close 清 source、assembler 和接收 mailbox。合法旧或已退休 choice 取消返回 false；未来/当前冲突、错误方向/Room 与畸形数据 fail closed。
- 房主提出 SceneChange，客机核对场景键和世界指纹后 Ack，房主 Commit。
  双方 Ready 前不发送移动帧；同名场景但不同指纹不能通过。
- 房主加载时 Suspend；客机已确认的场景失效时 Pause，房主重新分配 epoch。
  清空旧帧，丢弃历史 epoch；未来 epoch、错误玩家/房间身份或错误场景权限被拒绝。
- 默认心跳 2 秒、失联/未回复 12 秒、场景确认 90 秒、握手 5 秒，均可通过 SessionOptions 配置。
  Ping/Pong 估算 RTT 与两个进程的时钟偏移；无估算时以接收时间呈现快照。
- 正常 Leave 与取消/超时都会结束任务并关闭连接，状态事件保留可读原因。

场景描述由只读 WorldLayoutReader 提供；核心测试使用的夹具不是游戏地图证据。

Unity 场景句柄和对象实例 ID 只用于本机生命周期；不能作为跨机器实体身份。
场景名称相同也不证明地图相同，M4 仍需真实地图选择/生成与世界状态验证。
下一步补本地原生选择 origin/代次与跨机地址证据，接入加载前房主选择采用和客机临时状态/生成/AI 隔离，再验证两个游戏实例及跨机资源键。

## 精灵资源键准备

`Core/Assets/SpriteKey` 将纹理名、精灵名、纹理大小、区域、枢轴、边框和 PPU
编码为版本化 SHA256 元数据键。长度前缀避免名称分隔歧义，浮点位编码与文化设置无关，
正负零统一。它不是像素校验或游戏资源 GUID；真实两机的运行时纹理命名与加载配置仍待验证。

`AssetRegistry` 最多保存 16384 项。相同键出现不同本机资源 ID 时标记歧义，拒绝解析；
重注册不能绕过歧义，切换场景后清空。实例 ID 只用于本机碰撞检测，不发送给对方。
`Rendering/SpriteCatalog` 在创建它的 Unity 线程读取已加载资源，按需扫描最多每两秒一次，
不创建、下载或销毁游戏 Sprite。该适配器已接入源码中的网络显示，实机调用待验证。

## 游戏内移动测试入口（0.1.5-dev 实际收发，视觉与清理待确认）

`Networking/NetworkDriver` 仅暴露 Unity 回调，普通 CLR Controller 负责 UI 和生命周期。
默认不连接、不监听；F11 打开面板。可在菜单/船上配置房主 IPv4、端口（默认 27182）和名称。

- `Host room`：监听局域网的一个客机；客机使用同端口和房主 LAN IPv4 点 `Join room`。
- `Local test`：同一个游戏中建立两个真实回环 TCP 会话，数字帧经过编码/传输/解码后
  在右侧显示。这是单游戏诊断，不构成两游戏验收。
- `Disconnect`：取消监听/连接、释放会话，清理自己的远程节点并恢复此前暂停的 M2 回放。
  F11 关闭面板会恢复打开前的系统鼠标可见性和锁定状态。
- 0.1.5-dev 的两个默认关闭的鱼诊断开关可传输实际鱼状态并显示一条收到的 Sprite/Spine 鱼。
  原生读取与显示组件已在单游戏实际执行，测试过程与限制见 WORLD_SYNC；它们尚不统一鱼群、AI 或收益。
  0.1.6-dev 新增观察鱼启停/销毁的挂钩，已实机安装并触发；原生池重新启用和卸载恢复待验证。
  0.1.7-dev 选择镜头内近鱼，并加 MultiDave Fish Preview 标签；标签出现也不能单独证明鱼网格已显示。
- 0.1.11-dev 新增默认关闭的 `Display received fish roster (display only)`，通过 `FishWorldBuffer` 原子接受完整数字清单，
  每鱼独立最多 16 帧；镜头外、暂缺 Visual 与超时隐藏均保留清单身份。清单只含房主玩家当前 scene 的活动已初始化鱼。
  主线程 `RemoteFishWorld` / `FishDisplayNode` 解析资源并清理自建节点，原鱼及 AI/碰撞/收益保持原样。
  本机显示仍须开启 Transmit；收到数字鱼总数、可显示/未知资源/镜头内/实际节点数须分别核对，不能把数量当画面证明。
- 默认关闭的 `Observe host harpoon and fish interactions (read-only)` 在开启 Transmit 的房主安装 8 个只读 prefix/postfix 入口。
  冻结的本地指针 CLR 快照提供 prefix 当时的身份，postfix 保留同一 CallId/绑定；原 bool 只是原返回，HpAtDrain 只是消费时读数。
  这不是远程投射物同步、伤害裁定或捕获结果；本轮已健康安装并记录 21 对发射/挂钩/两种伤害调用和自己的卸载/Disconnect，
  两个原 bool 为 true，QTE Win 或 Pickup 尚未见，正常返航仍待确认。
- 0.1.11-dev 另读加载后的路线/IGP 清单，每选中场景至少一组、查找与原注册列表一致、两个不同 Unity 帧稳定后才记录。
  跨机地址未验证，没有在加载前采用房主地图；现有布局确认也不代表地图选择接管。
  本次读取失败 Selected route incomplete，不能把路线清单记为通过。
- 0.1.12-dev 新增 `Check selected fish target`，Guest/Local test 的 Ready 且单鱼预览已选中时发送 ProbeTarget。
  host 主线程重新查原生目标、代次和终态，通过只为 DryRunValidated/OperationId=0；这是目标检查，不发射鱼叉、伤害或捕获。
  GUI 发送异常有捕获，目标读取前后复核 lifecycle 健康/代次，失效不保留旧身份；同 epoch 开关不归零 world revision。
  六种动作模型都有格式/Gate，真实发射/QTE/召回/拾取因缺 actor/loadout、地图权限、guest 隔离、本地竞争裁定及 native bridge 保持拒绝执行。
- Transmit 开启时独立在入海前后最多 1Hz 读取 MapRouteObservation，MAP_ROUTE_INPUTS 只在值变化时记录。
  cache/roadmap/first 缺失或 cache 太短分别失效并撤销旧稳定候选；候选 bSelected 层、加载名称和变化键不是完整路线/地图指纹或加载前接管。
- 0.1.13-dev 新增默认关闭的 Observe map selection calls (read-only)，独立于 TCP/Ready/Transmit。
  在 cache/restore postfix、IGP 原 __result postfix、Prefab IEnumerator factory prefix 和 SceneLoader.LoadSceneAsync prefix 共五处自然边界，当次 Unity 线程冻结有界 CLR。
  全进程 1024 条、queue 64，非 main 跳过 native 读取；空选择/截断/读取错误明确，不保留 native wrapper；Disconnect 关闭并卸载自己的 Observer。
  RouteFingerprint 只属于 MapRouteSelection，不是完整 IGP manifest；factory 不证明实际请求/完成，尚无所有选择先于所有加载的统一屏障，未共享/采用地图或调用选图/load/save 写入。
  两处 IsInitDone 改读直接 backing field，完整加载后选择门槛未放宽；新 observer 仅编译通过，原生回调与卸载仍待实机。
- 0.1.14-dev 的 MapChoiceController 将 host 自然观察转成协议 5 候选；Observer 默认关闭，发布须绑定房间，独立于鱼 Transmit 和 Ready。
  cache/restore 即使同指纹也创建新 generation，SceneLoader 同指纹仅去重。callbackFloor 挡住绑定/撤销前已排队的旧观察；copy 错误/丢失/Truncated 主动撤销，未绑定 IGP 不缓存。
  已发布组再次空/unknown 返回时撤销候选，未知新组空值仍 Unbound。DTO 无原生 controller/context 代次证据，迟到同 scene/address 的旧原生回调仍可附当前候选；NativeGenerationBound=false。
  MAP_CHOICE_* 和 NETWORK_STATE 分别记录发布/接收/撤销及候选代次，所有 Snapshot 只为 evidence、HostSelectionApplied=false；它们不能填补 MapAuthorityReady/GuestStateIsolated。新版未部署/启动，原生 ABI 与两游戏均待验。

0.1.11-dev 实际 A03_01_02 已记录 49 条 Loopback Ready 概要、53 条 FishWorld 状态，观察/绑定/可显示/可见最大 16，
网格顶点 662，未知/缺 Visual/显示错误为零。用户确认成对偏移鱼可见，捕获原鱼时副本同时消失，
关闭 Display received fish roster 后恢复正常，操作和镜头正常；这是原鱼加诊断副本的 Local test 行为，不是统一世界或捕获副本。
交互 42 条事件组成 21 对 CallId：HarpoonFire 28、FishHookedByProjectile 10、FishDamage 2、SpecialDamage 2；
两个原 bool 为 true，14 条事件消费时有可用原生目标，相关绑定固定，回调/解析/未配对/查询错误为零。
两种伤害声明及 __state 配对在该实机路径已观察，Win/Pickup 尚未见；用户确认主动退出、未返航；动画与正常返航保存仍未验证。
不能将组件数、原方法返回、副本同步消失或断线标记作为合作捕获/返航验收。

LocalAvatarCapture 每 0.5 秒核对玩家及显示部件，最多 30Hz 捕获姿态和资源键。
槽位包含局部路径及节点内组件序号，装备延迟加载后自动刷新。Native 对象不进入网络任务。
RemoteMotionBuffer 每个 epoch 锚定一次两进程时间，避免后续 RTT 变化重排帧；最多保留 120 帧，
一秒未收到角色帧便隐藏。NetworkAvatarRenderer 按槽位插值并解析 Sprite，未知/歧义资源隐藏。
渲染使用本地材质模板；跨机不同装备/材质及动态纹理名仍需实测。

WorldLayoutReader 只读取：已加载场景名、动态节点选择地址、静态非触发 Collider2D 的路径、
变换矩阵、偏移与完整几何。排除角色、动态刚体；支持 Box/Circle/Capsule/Polygon/Edge/Composite。
最多 4096 个布局条目、131072 个顶点，不使用进程 ID 或 Unity 实例 ID 生成跨机器指纹。
指纹按条目排序并保留重复数量。未加载/无法读取/不支持的布局不能进入 Ready。
该签名是初始静态布局的核对，尚未在实机证明跨机稳定性；不会生成相同地图或同步鱼/道具状态。

日志：DAVECOOP_NETWORK_READY / CONNECTED / EVENT / STATE / WARNING、
DAVECOOP_LAYOUT_READY / WARNING。真实验证至少覆盖本机显示、双机连接/转向、场景切换、
不同地图的等待/超时、未知资源处理和取消/断线后本地游戏可用。

## 自动测试

在已配置游戏的开发机器、仓库根目录执行：

```powershell
.\development\scripts\Test-Core.ps1
.\development\scripts\Build-Plugin.ps1
```

测试脚本使用已有 Roslyn 和 BepInEx 自带库编译，执行测试需要系统 .NET 6 Runtime。
有支持 net6.0 的 SDK 时，纯 CLR 测试项目也可独立运行：

```powershell
dotnet run --project development/tests/DaveCoop.Core.Tests/DaveCoop.Core.Tests.csproj
```

本机当前已通过 134/134 项测试。用例覆盖缓冲边界/容量/排序/清理、姿态插值、异常四元数、
JSON 数字结构往返、错误数据拒绝、拆包/截断、TCP 双端握手及双向快照、
版本不匹配、并发发送、重复序号、连接关闭和读取取消；另覆盖场景握手与不一致超时、
旧 epoch 清理、客机重载、身份/权限错误、时钟偏移、深拷贝与队列上限、
真实回环会话的移动数据/正常离开、静默握手超时及监听/已连接取消。
资源测试另覆盖多文化键稳定性、描述区分、非法元数据、歧义拒绝、容量与场景清理。
新测试覆盖布局枚举顺序/重复几何/选择内容/畸形输入、插值时钟锚定、旧 epoch、容量和失联隐藏。
实体测试覆盖同种不同 ID/池复用/容量、分块完整性/复制所有权、丢弃旧修订与空清单、
房主权限/旧 epoch、消息公平与上限、真实 TCP 清单/血量更新/移除和旧协议拒绝。
新增鱼显示描述往返/复制隔离/非法值/最大消息大小、显示时钟/插值/失联/移除，以及慢连接持续采样完成整批提交。
生命周期用例覆盖两次采样之间的启停循环、销毁/地址复用、嵌套重复回调、容量/清理及并发。
预览选择另覆盖初选资格、锁定身份、暂缺显示恢复、手动重选/旧修订屏障、死亡/捕获/空选择与明确隐藏原因。
房主目标测试覆盖反向查询撤销、epoch/池代次隔离、容量/复制所有权和冻结指针映射的并发读取。
鱼群用例覆盖完整清单原子增删更新、每鱼历史/复制隔离、缺 Visual/终态保留、旧 epoch/修订、超时恢复及 4096/16 帧界限。
路线/IGP 用例覆盖规范排序/文化稳定性、清单边界/连通性、资源模式、畸形/重复数据与复制所有权；
这些是 CLR 描述夹具，不能证明已执行原生路线读取、交互挂钩或加载前地图接管。
新增路线用例覆盖有效完整路线没有 groups 时仍不能成为完整 manifest、断链/畸形边界、深复制、排序/文化/正负零规范指纹，以及旧完整 manifest 黄金指纹兼容；不执行原生地图回调。
操作用例覆盖 6 种意图 schema、规范指纹/复制、来源冒充、同键冲突、pending/terminal 重复、缓存淘汰后重放、
业务拒绝 ID 消费、scene/room 失效、FIFO/限流/新鲜事实、目标池代次退休、装备/空间/阶段许可、派发租约与原生未知不重试。
协议/会话用例另覆盖单 payload、旧协议拒绝、方向/来源权限、guest outstanding 与结果指纹/operation 核对、动作队列上限和公平调度。
三种本机 TCP 操作夹具（往返、旧协议拒绝、take 后场景切换恢复）通过，只证明实际网络流的请求/结果及场景失效行为，不执行原生攻击/捕获，也不等于真实游戏切换或两游戏验收。
地图夹具覆盖 8 场景分片/原子拼装、复制所有权、独立代次与连续修订、同组更新、旧批次打断、32 包 FIFO、溢出/重复撤销、Room/方向/冲突拒绝、四路公平、场景保留/关房清理，以及真实 TCP 完整路线/选择/撤销和协议 4 拒绝。
4 项源适配测试由 Test-Core 与测试 csproj 编译实际 MapChoiceController/MapSelectionCallObservation，仅替代 logger，用 synthetic DTO 与实际回环 TCP 执行候选路径。自然边界、callbackFloor、未绑定选择和失效撤销不构成原生 origin 证明；没有运行 NativeHook。
这些测试使用同一进程中的两个真实回环 TCP 端点，没有运行两份游戏实例。

## 尚需完成

M3 游戏适配实机验证与双游戏移动同步、M4 地图/实体、
M5 捕鱼/伤害/拾取、M6 临时客机进度与返航结算、M7 两机器和首次冷安装均未完成。
M4 下一步是本地来源/代次与跨机地址确认后的实际选择采用和客机原生隔离；当前地图快照一直为 ObservationOnly=true/HostSelectionApplied=false。
持续目标保留完整双人潜水闭环，不能以回放、TCP 测试或同名场景代替完成验收。
