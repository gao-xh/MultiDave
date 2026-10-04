---
name: dave-coop-setup
description: Configure or continue development of the MultiDave prototype for Windows Steam Dave the Diver using this repository, verified packages, and loading logs.
---

# MultiDave 配置与开发

仓库目标为 https://github.com/gao-xh/MultiDave。当前 0.1.0 是插件加载原型，
尚未支持双人联机。仓库根目录是此 SKILL.md 路径向上三级。
先读根目录 AGENTS.md 和 development/logs/DEVLOG.md，避免重复已完成工作。

## 用户只给 GitHub 链接并要求配置

1. 将用户指定的仓库克隆到可写的新目录，读取其 README、AGENTS 和本 Skill。
   已有检出时检查来源和本地修改，不覆盖别人工作。
2. 在仓库根目录运行 `setup.ps1 -InspectOnly`，自动读取 Steam 注册表和库清单。
   找不到或找到多份时才询问游戏目录；不要沿用开发者本机 F 盘路径。
3. 用户要求配置 Mod 已包含安装依赖和插件的授权。游戏正在运行时请其保存退出，
   继续准备文件；不要强制结束进程。网络及游戏目录写入使用环境提供的权限机制。
4. 运行 `setup.ps1 -LaunchGame`。默认使用仓库 distribution 下经 SHA256
   校验的自写插件包；BepInEx 从官方固定 URL 下载并校验。玩家无需 .NET SDK。
   只有明确需要最新 GitHub Release 时才用 `-UseLatestRelease`。
5. 首次启动会生成互操作接口，可等待数分钟并继续给用户进度。
   保持在主菜单，运行 `development/scripts/Check-Status.ps1`。
   验证当前启动进程的日志时间，以及 `DAVECOOP_BOOTSTRAP_OK`、
   `DAVECOOP_UPDATE_OK`、`DAVECOOP_SCENE`。安装文件存在不等于加载成功。
6. 报告已配置内容、真实验证结果、运行日志位置及当前原型范围。
   不声称服务器、第二角色或完整联机已经完成。

## 继续开发

读 `development/docs/HANDOFF.md`。源文件在 `development/src/DaveCoop/`。
按需要修改后运行 `development/scripts/Build-Plugin.ps1`，退出游戏后运行
`Deploy-Plugin.ps1`，再启动并验证。编译脚本使用已有 SDK 的 Roslyn 和
BepInEx 自带 .NET 6 库，也可在支持 net6.0 的 SDK 中使用 csproj。
若无编译器，玩家安装仍可用发布包；开发则先准备合适工具并检查磁盘空间。

每次有意义的修改，将实际改动、验证结果、限制和下一步追加到
`development/logs/DEVLOG.md`。机器安装日志为 `development/.local/logs/*.jsonl`。

## 版本与失败处理

- 未验证的游戏或框架版本：解释检测到的版本，先研究兼容性；不要绕过检查。
- 下载失败：保留失败日志，清理本次临时下载；不把未知来源 DLL 混入已验证环境。
- 插件失败：检查 BepInEx 日志和游戏 Player.log，将问题记入开发日志。
- 停用方式见根 README；不删游戏或用户存档。
- 调整版本后更新依赖清单，重新真实验证，再打包到 distribution。

仓库只发布本项目源码、自写 DLL 和元数据；不发布游戏、互操作程序集或存档。