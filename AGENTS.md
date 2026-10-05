# MultiDave

开发工作集中在 `development/`。修改前读取 `development/AGENTS.md` 和
`development/logs/DEVLOG.md`，确认当前实现范围及未完成项。

用户给出此仓库链接并要求配置 Mod 时，读取
`.agents/skills/dave-coop-setup/SKILL.md`，按玩家安装流程执行；
源码开发请求则按开发流程执行。不要把配置完成描述成完整联机已实现。

配置 Mod、替换插件、修改实验配置或启动前，先让玩家正常保存退出游戏。
执行 `development/scripts/Backup-Saves.ps1`，将返回的 `BackupPath` 和
`ManifestSHA256` 写入备份目录之外的独立私有记录，再从该记录读取哈希，执行
`-VerifyBackup <目录> -ExpectedManifestSHA256 <记录哈希>`。
备份和独立复核均成功、逐项核对 `Missing` / `CoverageComplete` 后才继续安装或启动。
备份、账户信息及独立记录只保留本机；备份请求不授权恢复覆盖，当前脚本不自动恢复。
默认发行包仍为 0.1.0；开发入口见
https://github.com/gao-xh/MultiDave/tree/codex/player-discovery ，不要混用两种范围。

将每次有意义的改动、验证证据和下一步写入开发日志。
安装脚本的机器运行日志保存在 `development/.local/logs/`，不提交 Git。
仓库不包含游戏、生成的互操作 DLL、框架运行库或用户存档。
`distribution/` 仅包含本项目自己编写的插件和发布元数据。
