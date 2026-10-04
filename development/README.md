# 潜水员戴夫联机原型

当前进度：M1 已通过潜水、场景切换与返航读取验证。
M2 回放代码已编译并通过主菜单启动，实际入海显示待验收；M3 传输/会话层已通过 24 项测试。
完整计划见 [PLAN](docs/PLAN.md)，实现与待验证范围见 [MULTIPLAYER](docs/MULTIPLAYER.md)。
尚未完成第二角色实机验收、游戏内网络连接、捕鱼同步或存档同步。

## 本机环境

- 游戏目录：`F:\steam\steamapps\common\Dave the Diver`
- Steam App ID：`1868140`
- 检查时的 Steam Build ID：`25315876`
- Unity：`6000.0.52f1`，Windows x64 IL2CPP
- BepInEx：官方 `6.0.0-be.788+5b766a3`
- 插件：`local.davecoop.prototype`，源码 `0.1.2-dev`，发布包 `0.1.0`

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

游戏左上角显示 `DaveCoop Prototype 0.1.2-dev`、加载状态、当前场景和发现的玩家数量。
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
F10 切换第二角色延迟回放；实际显示仍待试玩验收。

## 框架安装记录

`scripts/Install-Framework.ps1` 只接受包含 `DaveTheDiver.exe` 的目录，
拒绝覆盖已有框架文件，并将原始游戏文件 SHA256 记录在
`artifacts/original-game-hashes.json`。
框架下载与暂存目录为 `.local/`，均不提交到 Git。

临时停用全部插件：退出游戏后，把游戏目录的 `winhttp.dll` 改名为
`winhttp.dll.disabled`；恢复时改回原名。
仅停用本插件：把 `BepInEx/plugins/DaveCoop/DaveCoop.dll` 移到 plugins 目录之外。

## 后续验证顺序

1. 找到本地玩家、角色创建入口、输入和摄像机绑定逻辑。
2. 生成仅用于观察的第二角色，并确保不会改变任务或存档。
3. 加入局域网房主/客户端连接和角色移动同步。
4. 在房主统一裁决下实现鱼、伤害、拾取和返航结算。

具体阶段与验收条件按 [开发计划](docs/PLAN.md) 执行。

## 官方资料

- [BepInEx IL2CPP 安装](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
- [BepInEx 官方构建](https://builds.bepinex.dev/projects/bepinex_be)
- [插件开发](https://docs.bepinex.dev/master/articles/dev_guide/plugin_tutorial/2_plugin_start.html)
