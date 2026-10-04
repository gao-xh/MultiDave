# MultiDave

开发工作集中在 `development/`。修改前读取 `development/AGENTS.md` 和
`development/logs/DEVLOG.md`，确认当前实现范围及未完成项。

用户给出此仓库链接并要求配置 Mod 时，读取
`.agents/skills/dave-coop-setup/SKILL.md`，按玩家安装流程执行；
源码开发请求则按开发流程执行。不要把配置完成描述成完整联机已实现。

将每次有意义的改动、验证证据和下一步写入开发日志。
安装脚本的机器运行日志保存在 `development/.local/logs/`，不提交 Git。
仓库不包含游戏、生成的互操作 DLL、框架运行库或用户存档。
`distribution/` 仅包含本项目自己编写的插件和发布元数据。
