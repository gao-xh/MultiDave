# MultiDave 接手记录

## 已完成

已在 Windows Steam 版真实验证 BepInEx 788、207 个互操作程序集、
自定义插件加载、Unity Update 回调，以及 `DR_Start`、`DR_Logo`、`DR_Title` 场景读取。
插件日志证据见 `../logs/bootstrap-verification.log`。

开发在 `codex/player-discovery` 分支，当前源码为 `0.1.2-dev`，M1 历史证据来自 `0.1.1-dev`。
加入只读玩家、输入、动画和摄像机探针，离线元数据检查工具及 JSONL 汇总工具。
真实潜水 `A01_01_01` 已读到 `PlayerGroup(Clone)/DaveCharacter`，
`InGameManager.playerCharacter` 与该实例一致，`CameraManager` 跟随其根 Transform。
玩家位置、旋转、朝向、非零移动输入和动画状态已成功采样。
从 `A01_01_01` 切换到 `Boss_000` 后，旧玩家不再出现在采样中，新玩家与管理器、
摄像机重新绑定，跨场景探针无错误。M1 基础验收通过，证据见
`../logs/player-discovery-verification.json`。
当前默认安装包仍为 0.1.0；根入口不部署开发分支的源码构建。

旧 M1 会话完整日志最终为 1886 条快照，记录 Boss 返回潜水、返航大厅及主菜单，无探针错误。
新增 M2 独立显示节点与延迟本地回放，编译及真实主菜单启动通过，用户暂不方便入海试玩。
新增纯 CLR 姿态时间线、协议/握手/TCP 分帧与校验、会话与局域网双端接口，24 项测试通过。
细节见 `MULTIPLAYER.md`，最新测试证据见 `../logs/core-verification.json`。

首次安装记录在忽略的 `artifacts/framework-install.json`；
原始 EXE、GameAssembly.dll、UnityPlayer.dll 的 SHA256 经复核未改变。

玩家入口为仓库根目录 `setup.ps1`，通过 `Install-Mod.ps1` 进行自动定位、
框架校验、发布包校验、插件备份与安装，机器日志写入 `.local/logs/*.jsonl`。
源码入口为 `src/DaveCoop/Plugin.cs`，编译脚本可使用现有 SDK 5 的 Roslyn
和游戏目录中 BepInEx 自带的 .NET 6 库，无需另行下载完整 SDK。

## 尚未完成

M2 真实第二角色显示正在等待用户结果；M3 会话基础已测试，游戏网络适配尚未实现。
真实双游戏连接、鱼/拾取同步、存档同步均未实现。
服务器方案暂时搁置。当前包是验证开发入口的原型。

## 下一步

按 `PLAN.md`、`GAME_API.md` 和 `MULTIPLAYER.md` 继续。
用户已恢复试玩条件，当前游戏部署的 M2 构建 SHA256 为
`E2A7DEC29ACB894F72A2D8528C099E82AB867DDE00F82DC739707BAA8EBB5AB9`，等待反馈和日志。
源码中新加的 SessionMachine / SessionPeer / LanRoom 已编译和通过核心测试，尚未部署。
继续 M3 资源键映射和主线程游戏适配，同时保留 M2 视觉/输入/摄像机/清理验收。
Sprite 回放仅验证显示路径，网络消息必须解析资源键，不可跨线程使用 Native Sprite 引用。
后续游戏交互仍需研究真实控制流和原角色组件副作用。

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
