---
name: dave-coop-setup
description: Configure or continue development of the MultiDave prototype for Windows Steam Dave the Diver using this repository, verified packages, and loading logs.
---

# MultiDave 配置与开发

仓库目标为 https://github.com/gao-xh/MultiDave。仓库根目录是此文件路径向上三级。
先读根目录 AGENTS.md 和 development/logs/DEVLOG.md，确定当前实现与验证范围。
默认发行包是 0.1.0 插件加载原型。开发源码进度以 HANDOFF 和 PLAN 为准；
本机 TCP 测试通过不能代替真实双游戏联机验收。

## 用户给 GitHub 链接并要求配置

1. 克隆用户指定仓库到可写的新目录，读取 README、AGENTS 和本 Skill。
   已有检出时检查来源和本地修改，保留别人工作。
2. 在根目录运行 `setup.ps1 -InspectOnly`，自动读取 Steam 注册表和库清单。
   找不到或找到多份时才询问游戏目录，不沿用开发者本机路径。
3. 配置 Mod 包含安装依赖和插件的授权。游戏运行时请用户保存退出并继续准备文件；
   安装脚本会拒绝覆盖运行中的插件。网络及目录写入使用环境的权限机制。
4. 运行 `setup.ps1 -LaunchGame`。默认安装 distribution 下经 SHA256 校验的
   自写插件包；框架从官方固定 URL 下载并校验，玩家不需要 .NET SDK。
   明确要求最新 GitHub Release 时才使用 `-UseLatestRelease`。
5. 首次启动会生成互操作接口，期间给用户进度。保持主菜单，运行
   `development/scripts/Check-Status.ps1`，验证当前进程日志的新鲜度与
   `DAVECOOP_BOOTSTRAP_OK`、`DAVECOOP_UPDATE_OK`、`DAVECOOP_SCENE`。
6. 报告版本、实际加载证据、日志位置和支持范围。安装文件存在不能证明加载成功。

## 继续开发

读 `development/docs/HANDOFF.md`、`PLAN.md`；研究游戏接口时读 `GAME_API.md`，
研究显示/协议/会话时读 `MULTIPLAYER.md`，研究同一海洋、鱼与互动时读 `WORLD_SYNC.md`。
源码在 `development/src/DaveCoop/`。

- C# 修改后运行 `development/scripts/Build-Plugin.ps1`。
  纯 CLR 姿态、协议或会话修改后另运行 `development/scripts/Test-Core.ps1`。
  现有脚本使用已安装 SDK 的 Roslyn 与框架 .NET 6 库；测试运行需要 .NET 6 runtime。
  支持 net6.0 的 SDK 也可使用项目文件，按当前机器的真实结果记录验证路径。
- 游戏正常退出后运行 `development/scripts/Deploy-Plugin.ps1`；
  再启动，并从本次进程日志验证。缺少编译器时玩家配置仍可使用发行包。
- 第二角色的本地回放由 F10 切换；潜水时验证显示、转向、输入、镜头及返航清理。
  用户不方便试玩时记录待验证项，继续独立开发，不把主菜单启动当成画面验收。
- 源码开发版的 F11 打开房间面板。先按 MULTIPLAYER 验证 Local test 的真实 TCP
  收发与主线程显示，再验证 Host/Join 的两个游戏实例；默认发行包不包含这个新入口。
  对照 NETWORK/LAYOUT 标记记录成功或失败，布局指纹通过不等于统一地图/实体已完成。
- 0.1.4-dev 新增 F7 世界只读探针，默认关闭，已编译但实机验证状态以 HANDOFF 为准。
  正常退出后部署、验证新版本启动，再观察地图选择、鱼 HP/捕获和返航生命周期。
  `development/scripts/Inspect-WorldApi.ps1` 可复现接口签名研究；元数据不能证明挂钩副作用。
- 同版源码的 F11 可开启 Transmit read-only fish observations，在 Host/Local test 潜水时
  核对 WORLD_RECEIVED 与 NETWORK_STATE 的数量/修订；这是实际鱼数据的只读通道，
  尚不创建远程鱼或裁定捕获。协议为 2，双方源码/版本应匹配。
- 网络线程只处理纯 CLR 数据；Unity 对象和资源键解析放在主线程。
  真实双实例、同一地图及捕鱼/结算验收按 PLAN 的阶段条件执行。

每次有意义的修改追加 `development/logs/DEVLOG.md`，同步接手文档和真实验证摘要。
机器运行日志放在忽略的 `development/.local/`，历史会话与新构建的证据分别记录。

## 版本与发行

未验证的游戏或框架版本先研究兼容性；下载及插件失败保留本次错误记录。
停用办法见根 README，不删除游戏或存档。发行前更新依赖清单、重新验证并打包；
打包器会检查 DLL 与发布版本一致。开发构建不自动替换默认发行包。

仓库只发布本项目源码、自写 DLL 和元数据，不发布游戏、互操作程序集或存档。
