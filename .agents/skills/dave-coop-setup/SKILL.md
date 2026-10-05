---
name: dave-coop-setup
description: Configure or continue development of the MultiDave prototype for Windows Steam Dave the Diver using this repository, verified packages, and loading logs.
---

# MultiDave 配置与开发

仓库目标为 https://github.com/gao-xh/MultiDave。当前 0.1.0 是插件加载原型，
尚未支持双人联机。仓库根目录是此 SKILL.md 路径向上三级。
先读根目录 AGENTS.md 和 development/logs/DEVLOG.md，避免重复已完成工作。

## 存档保护

配置 Mod、替换插件、修改实验配置或启动前，检查游戏进程；运行时让玩家正常保存退出，
脚本不得终止游戏。游戏退出后执行 [Backup-Saves.ps1](../../../development/scripts/Backup-Saves.ps1)，
按 [SAVE_BACKUP.md](../../../development/docs/SAVE_BACKUP.md)核对实际玩家目录和备份范围。
配置或备份请求已包含创建可逆备份的授权，沿用会话授权，不重复询问。

将成功返回的 `BackupPath` 和 `ManifestSHA256` 写入备份目录之外的独立私有记录。
复核从该记录读取哈希，执行 `-VerifyBackup <备份目录> -ExpectedManifestSHA256 <记录哈希>`；
不得在复核时重新从待检 manifest 计算哈希代替此前记录。只有备份成功、带固定哈希的独立复核成功，
且逐项核对 `Missing` / `CoverageComplete` 后，才继续安装、改配置或启动。
缺失或失败不可报告空备份成功；沙盒用户目录不等于真实玩家目录时使用已确认的
`-UserProfilePath`、`-SteamRoot`、`-GamePath`。确证没有历史存档的首次安装可记录缺失并继续
普通加载原型，实验功能保持关闭。

备份只写入唯一新私有目录，原文件不覆盖；账户路径、manifest、独立哈希记录均不公开或提交 Git。
`Verified` 仅证明已发现来源的文件一致性，不证明游戏存档语义、Steam 云服务器完整或客机隔离。
当前脚本不自动恢复；备份请求不授权覆盖，恢复须先保存退出、另备当前状态并核对目标、云状态和明确覆盖授权。

## 用户只给 GitHub 链接并要求配置

1. 将用户指定的仓库克隆到可写的新目录，读取其 README、AGENTS 和本 Skill。
   已有检出时检查来源和本地修改，不覆盖别人工作。
2. 在仓库根目录运行 `setup.ps1 -InspectOnly`，自动读取 Steam 注册表和库清单。
   找不到或找到多份时才询问游戏目录；不要沿用开发者本机 F 盘路径。
3. 用户要求配置 Mod 已包含安装依赖和插件的授权。游戏正在运行时请其保存退出，
   继续准备文件；不要强制结束进程。网络及游戏目录写入使用环境提供的权限机制。
4. 完成上述存档备份、独立哈希固定复核与缺失项核对后，运行 `setup.ps1 -LaunchGame`。默认使用仓库 distribution 下经 SHA256
   校验的自写插件包；BepInEx 从官方固定 URL 下载并校验。玩家无需 .NET SDK。
   只有明确需要最新 GitHub Release 时才用 `-UseLatestRelease`。
5. 首次启动会生成互操作接口，可等待数分钟并继续给用户进度。
   保持在主菜单，运行 `development/scripts/Check-Status.ps1`。
   验证当前启动进程的日志时间，以及 `DAVECOOP_BOOTSTRAP_OK`、
   `DAVECOOP_UPDATE_OK`、`DAVECOOP_SCENE`。安装文件存在不等于加载成功。
6. 报告已配置内容、真实验证结果、运行日志位置及当前原型范围。
   不声称服务器、第二角色或完整联机已经完成。

## 继续开发

开发版本在 [codex/player-discovery 分支](https://github.com/gao-xh/MultiDave/tree/codex/player-discovery)。
本默认分支仍是 0.1.0 加载原型；需要开发包时明确选择开发分支，先读该分支 Skill 与
`development/docs/PLAYTEST_PACKAGE.md`。`-UseLatestRelease` 只选择稳定发行，不能代替开发预发布入口；
不要把开发构建或包准备成功当成双游戏玩法通过。

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
