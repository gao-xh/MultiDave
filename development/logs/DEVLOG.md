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

## 后续日志格式

每次追加：日期、目标、关键改动、验证命令及实际结果、遗留问题、下一步。

## 2026-10-05 — 默认入口存档保护窄回补

- 以默认 `main` 的 `ca9277b46e9209cf395ab72f58433b9da3919d25` 为基线，仅准备根 AGENTS/README/旧 Skill、备份脚本与说明、脱敏验证摘要及此日志追加。默认 0.1.0 发行包、dependencies、setup 和原型 C# 未变，不把开发分支历史或原生功能搬入默认入口。
- 安装或启动前先保存退出，备份整个实际 Dave LocalLow、已发现的 Steam 应用缓存及可选配置；将返回的清单哈希写入备份目录之外的独立私有记录，读回后用 `-VerifyBackup -ExpectedManifestSHA256` 复核，再逐项核对 `Missing` / `CoverageComplete`。脚本新增返回清单哈希、固定哈希前置核对及严格数字/布尔验证；文件与清单同时改写不能冒作原备份，旧无固定哈希模式明确不证明创建时一致。
- 引用本次脚本的实际验证，见 [save-protection-refresh-verification.json](save-protection-refresh-verification.json)：Windows PowerShell 5.1 定向 12/12 组检查通过，脚本 SHA256 `4CDBA13E1022AFCC2C93F82469E3E2F365B0A83D2152EC7249C51E90D92964CE` 前后相同；另有新 38 文件备份及带独立记录哈希的只读复核通过，旧备份保留。实际备份覆盖已发现来源，额外账户无应用缓存使 `CoverageComplete=false`；不宣称 Steam 云服务器完整。旧备份没有创建时固定哈希，不把本次观察到的哈希倒填成旧创建证据。
- 以上测试和真实备份证据来自所附脚本的已有验证，本次默认分支文件准备仅做内容与格式审阅，没有再次运行备份、读取原档或执行安装/游戏。未恢复、改原档或游戏配置、部署插件、启动游戏、重跑 Core/Build；备份语义、完整客机隔离与双游戏玩法均未据此通过。
- README/Skill 显式链接 [codex/player-discovery](https://github.com/gao-xh/MultiDave/tree/codex/player-discovery)，需要开发试玩包时读取该分支独立说明。默认 .0 与开发预发布分开，`-UseLatestRelease` 不自动选择开发包。下一步审阅这份窄回补；文件准备不等于远端已合并或新入口已实机运行。
