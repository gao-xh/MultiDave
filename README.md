# MultiDave

《潜水员戴夫》Windows Steam 版的联机 Mod 开发项目。

**当前为 0.1.0 开发原型，只验证插件加载、场景读取和状态面板。
尚未实现双人联机、第二角色或捕鱼同步。**

## 把链接交给 Codex 配置

将以下内容发送给能够访问你本机文件和运行终端的 Codex：

> 请配置 https://github.com/gao-xh/MultiDave 。先克隆仓库，读取 AGENTS.md
> 和 .agents/skills/dave-coop-setup/SKILL.md，自动定位 Steam 游戏，运行
> setup.ps1，启动到主菜单并检查真实加载日志。遇到版本不兼容或游戏正在运行时，
> 明确说明需要处理的步骤，不要强制关闭游戏。请记录配置结果。

Codex 可以完成游戏定位、依赖下载与校验、插件安装、启动验证和日志记录。
必要的文件写入或网络访问仍受你本机 Codex 的权限设置控制。

```powershell
# 仓库根目录：只检查环境。
.\setup.ps1 -InspectOnly

# 保存并退出游戏后，安装仓库附带的原型包并启动验证。
.\setup.ps1 -LaunchGame
```

玩家安装不需要 .NET SDK。首次启动框架需要联网下载 Unity 依赖并生成接口，
可能耗时数分钟。安装后左上角出现 `DaveCoop Prototype`，F8 切换面板。

## 已验证版本

- Windows x64，Steam App ID `1868140`
- 游戏 Steam Build ID `25315876`，Unity `6000.0.52f1`
- BepInEx `6.0.0-be.788+5b766a3`

安装器会拒绝尚未验证的游戏版本或已有的其他框架版本，避免自动覆盖现有环境。
当前原型会显示尚未实现网络的状态。

## 开发与接手

- [开发目录与编译说明](development/README.md)
- [开发日志与现有证据](development/logs/DEVLOG.md)
- [阶段记录与下一步](development/docs/HANDOFF.md)
- [分阶段开发计划](development/docs/PLAN.md)
- [玩家与摄像机发现](development/docs/GAME_API.md)
- [第二角色与传输层](development/docs/MULTIPLAYER.md)
- [Codex 配置技能](.agents/skills/dave-coop-setup/SKILL.md)

源代码和重复使用的配置工具位于 `development/`；
`distribution/` 是供自动安装使用的本项目插件包。
本仓库不分发游戏、游戏接口程序集、存档或 BepInEx 运行库。

`codex/player-discovery` 分支源码为 0.1.3-dev：玩家探针与第二角色回放基础验收通过，
传输/会话/资源键/布局与插值有 35 项本机测试，游戏房间入口已编译待实机验证。
开发时使用编译和部署脚本；默认玩家安装包保持 0.1.0。

## 暂时停用

保存并退出游戏后，将游戏目录的 `winhttp.dll` 改名为
`winhttp.dll.disabled` 可停用框架；改回原名即可恢复。
只停用 MultiDave 时，将 `BepInEx/plugins/DaveCoop/DaveCoop.dll`
移到 `plugins` 目录之外。
