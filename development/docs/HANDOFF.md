# MultiDave 接手记录

## 已完成

已在 Windows Steam 版真实验证 BepInEx 788、207 个互操作程序集、
自定义插件加载、Unity Update 回调，以及 `DR_Start`、`DR_Logo`、`DR_Title` 场景读取。
插件日志证据见 `../logs/bootstrap-verification.log`。

开发在 `codex/player-discovery` 分支，当前源码为 `0.1.4-dev`，M1 历史证据来自 `0.1.1-dev`。
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
新增纯 CLR 姿态时间线、协议/握手/TCP 分帧与校验、会话、资源键、布局指纹及插值，35 项测试通过。
0.1.3-dev 已部署，新进程确认 F11 网络组件、加载、Update 和主菜单标记；连接及潜水显示待验证。
启动证据见 `../logs/network-bootstrap-verification.json`。
0.1.4-dev 新增默认关闭的 F7 世界只读探针，已编译，尚未部署或实机运行。
地图/鱼/互动方案及可复现接口研究见 `WORLD_SYNC.md`、`scripts/Inspect-WorldApi.ps1`。
细节见 `MULTIPLAYER.md`，最新测试证据见 `../logs/core-verification.json`。

首次安装记录在忽略的 `artifacts/framework-install.json`；
原始 EXE、GameAssembly.dll、UnityPlayer.dll 的 SHA256 经复核未改变。

玩家入口为仓库根目录 `setup.ps1`，通过 `Install-Mod.ps1` 进行自动定位、
框架校验、发布包校验、插件备份与安装，机器日志写入 `.local/logs/*.jsonl`。
源码入口为 `src/DaveCoop/Plugin.cs`，编译脚本可使用现有 SDK 5 的 Roslyn
和游戏目录中 BepInEx 自带的 .NET 6 库，无需另行下载完整 SDK。

## 尚未完成

M3 游戏适配实机验证和真实双游戏验收尚未完成。
真实双游戏连接、鱼/拾取同步、存档同步均未实现。
服务器方案暂时搁置。当前包是验证开发入口的原型。

## 下一步

按 `PLAN.md`、`GAME_API.md`、`MULTIPLAYER.md` 和 `WORLD_SYNC.md` 继续。
M2 历史验证构建 SHA256 为 `E2A7DEC29ACB894F72A2D8528C099E82AB867DDE00F82DC739707BAA8EBB5AB9`。
用户保存退出后部署并启动 0.1.3-dev，SHA256 为 `1E2264723B799150033C55F0754E73DA9F33AFEF5C61FEE5A5030A7DECB8064D`。
当前游戏运行这一版本，DAVECOOP_NETWORK_READY 已确认；已请用户按 F11 / Local test 执行潜水。
新源码 0.1.4-dev 编译 SHA256 为 `587D4D5B4FA602783B5E7DDCC2FB6DF9657B3EE3AD1CACC6FD32799741481A05`。
正常保存退出后再部署世界探针，先核对新进程加载，F7 开启观察入海/捕鱼/返航生命周期。
不要把新探针的编译证据或旧版本启动证据当成它已运行。
对照 NETWORK_STATE 的 Ready/角色帧/资源及 LAYOUT_READY 或 WARNING，修复实际问题。
本机测试仅是单游戏中的两个 TCP 会话；不能标记 M3 双游戏或 M4 同一地图完成。
之后做真实双游戏测试，并验证资源键、地图布局指纹在两机上的稳定性。
Sprite 回放仅验证显示路径，网络消息必须解析资源键，不可跨线程使用 Native Sprite 引用。
后续游戏交互仍需研究真实控制流和原角色组件副作用。
已经读到地图节点/IGP 选择、FishAllocator 生成、FishAISystem 的种类/HP/捕获状态、
Damageable.TakeDamage 与鱼/物品 SuccessInteract 等签名；尚未执行这些写入入口。
探针记录本机 ID 仅用于观察，后续网络实体使用房主分配的 RoomId/epoch/EntityId。

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
