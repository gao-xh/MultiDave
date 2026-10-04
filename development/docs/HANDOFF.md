# MultiDave 接手记录

## 已完成

已在 Windows Steam 版真实验证 BepInEx 788、207 个互操作程序集、
自定义插件加载、Unity Update 回调，以及 `DR_Start`、`DR_Logo`、`DR_Title` 场景读取。
插件日志证据见 `../logs/bootstrap-verification.log`。

首次安装记录在忽略的 `artifacts/framework-install.json`；
原始 EXE、GameAssembly.dll、UnityPlayer.dll 的 SHA256 经复核未改变。

玩家入口为仓库根目录 `setup.ps1`，通过 `Install-Mod.ps1` 进行自动定位、
框架校验、发布包校验、插件备份与安装，机器日志写入 `.local/logs/*.jsonl`。
源码入口为 `src/DaveCoop/Plugin.cs`，编译脚本可使用现有 SDK 5 的 Roslyn
和游戏目录中 BepInEx 自带的 .NET 6 库，无需另行下载完整 SDK。

## 尚未完成

第二角色、独立输入、局域网连接、网络协议、鱼/拾取同步、存档同步均未实现。
服务器方案暂时搁置。当前包是验证开发入口的原型。

## 下一步

通过已生成的 `BepInEx/interop/Assembly-CSharp.dll` 检查玩家对象、
创建流程、输入入口和摄像机绑定。先做只读观察和记录，确认生命周期，
再验证第二角色不会夺取输入/摄像机或触发单例、任务和存档副作用。

## 已遇到的问题

- 沙箱内网络请求曾被拒绝；使用授权的工具提权联网完成官方框架下载。
- 完整 SDK 下载遇到 C 盘空间不足；删除此次未完成下载，改用已有编译器。
- 框架的 dotnet 目录包含 `System.IO.Compression.Native.dll`；
  它不能作为 C# 托管引用，编译脚本已排除 Native DLL。
- 首次互操作生成有若干方法恢复失败，框架日志有 Class::Init 替代实现警告；
  基础插件仍通过加载和 Update 验证。这不代表所有游戏函数均可挂钩。
- 当前沙箱内窗口枚举无法取得用户桌面窗口，视觉结果需另行确认；
  加载验证以当前游戏日志为证据。

## 发行边界

仓库提供自写插件安装包，BepInEx 从官方站按固定 SHA256 下载。
游戏版本改变时先重新验证，再更新依赖和发布包元数据。
禁止把编译通过解释为全流程联机已完成。

## GitHub 配置验证

已推送到 https://github.com/gao-xh/MultiDave 。从链接重新克隆后的自动定位、
安装与启动流程已执行，结果见 ../logs/github-verification.json。
测试机器已有框架，因此尚未独立验证根入口在完全未装框架的新机器上的冷安装。
框架本身的首次安装及生成接口在本次开发中已通过。
