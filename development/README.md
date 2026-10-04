# 潜水员戴夫联机原型

当前进度：M1 已通过潜水、场景切换与返航读取验证，M2 回放基础验收通过。
当前源码为 0.1.14-dev、协议 5，Build 警告视为错误通过，Test-Core 134/134 通过，本轮只构建待部署，未启动新版本。
当前安装仍为 0.1.12-dev，仅主菜单加载/Update/网络入口及初始路线输入通过；最近完成潜水验证的是 0.1.11-dev。
用户目前不方便试玩，Probe、自然地图回调、路线/场景切换及正常返航验证延后。
0.1.5/0.1.6-dev 已在真实潜水确认鱼探针、本机 TCP 收发、显示组件及生命周期回调运行。
0.1.7-dev 用户确认预览鱼可见，但日志记录角色部件销毁导致自动断开，画面稳定性不通过。
0.1.8-dev 没有旧异常但仍出现自动换鱼；0.1.9-dev 已锁定身份并记录隐藏原因、加入手动重选，已部署，用户确认不再突然消失。
预览鱼目前不能命中/捕获，已确认不再突然消失；动画、池重用/清理及双游戏验收待完成。
完整计划见 [PLAN](docs/PLAN.md)，移动实现见 [MULTIPLAYER](docs/MULTIPLAYER.md)，
地图、鱼与互动方案见 [WORLD_SYNC](docs/WORLD_SYNC.md)。
尚未完成真实双游戏移动验收、同一地图生成、捕鱼同步或存档同步。
当前构建与最终验证范围见 [地图选择传输摘要](logs/map-choice-transport-build-verification.json)；
0.1.13 历史观察构建见 [地图选择调用摘要](logs/map-selection-call-build-verification.json)。

## 本机环境

- 游戏目录：`F:\steam\steamapps\common\Dave the Diver`
- Steam App ID：`1868140`
- 检查时的 Steam Build ID：`25315876`
- Unity：`6000.0.52f1`，Windows x64 IL2CPP
- BepInEx：官方 `6.0.0-be.788+5b766a3`
- 插件：`local.davecoop.prototype`，源码 `0.1.14-dev`，安装/最近启动 `0.1.12-dev`，最近潜水 `0.1.11-dev`，发布包 `0.1.0`

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

当前已安装游戏显示 `DaveCoop Prototype 0.1.12-dev`；0.1.14-dev 尚未部署或启动，不能沿用旧进程日志验证新版本。
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
Observe map selection calls 默认关闭，房主绑定房间后可发布路线/IGP 候选，客机接收证据；不依赖 Ready，也不要求鱼状态 Transmit 开关。
路线每片 8 场景、最多 4 片，独立 32 包 FIFO；控制/心跳与 MapChoiceRetire 优先，动作/角色/世界/地图四路公平。
generation/revision 独立于 epoch；新代次首片撤旧路线，完整拼装才提交。普通场景切换保留地图候选，显式撤销及关房清理；溢出、复制错误与截断主动撤销，未绑定 IGP 不缓存，已发布组再次空/unknown 也撤销候选。
cache/restore 每次合法自然样本都创建新代次，SceneLoader 同指纹去重。callbackFloor 只挡住已有排队回调，无法证明迟到同地址 IGP 的原生代次；NativeGenerationBound=false，全部 Snapshot 为 evidence only、HostSelectionApplied=false。
Test-Core 与测试 csproj 编译实际 MapChoiceController 和 MapSelectionCallObservation，仅替代 logger。4 项源适配用例使用 synthetic DTO/实际回环 TCP，不运行 NativeHook，不能证明原生 ABI 或选择采用。

## 框架安装记录

`scripts/Install-Framework.ps1` 只接受包含 `DaveTheDiver.exe` 的目录，
拒绝覆盖已有框架文件，并将原始游戏文件 SHA256 记录在
`artifacts/original-game-hashes.json`。
框架下载与暂存目录为 `.local/`，均不提交到 Git。

临时停用全部插件：退出游戏后，把游戏目录的 `winhttp.dll` 改名为
`winhttp.dll.disabled`；恢复时改回原名。
仅停用本插件：把 `BepInEx/plugins/DaveCoop/DaveCoop.dll` 移到 plugins 目录之外。

## 后续验证顺序

1. 用户方便时正常保存退出后部署，验证新版本五处自然地图回调及目标检查，补充原生来源/控制器代次与跨机地址证据。
2. 接入实际加载前房主路线/IGP 选择采用、客机临时进度恢复及原生生成/AI 隔离，再用两份游戏核对地图与实体。
3. 接入可信玩家/装备和鱼叉、命中、QTE、拾取的房主原生裁定，覆盖同时操作与重复请求。
4. 完成正常返航、唯一收益账本及客机恢复，最后验证冷配置并更新发行包。

具体阶段与验收条件按 [开发计划](docs/PLAN.md) 执行。

## 官方资料

- [BepInEx IL2CPP 安装](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
- [BepInEx 官方构建](https://builds.bepinex.dev/projects/bepinex_be)
- [插件开发](https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html)
