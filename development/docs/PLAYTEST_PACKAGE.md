# 开发试玩包

当前可生成 `DaveCoop-0.1.48-dev.zip`（协议11），这是原生玩法尚未验证的开发包。
它使用[已通过构建的 DLL](../logs/host-body-interest-build-verification.json)，不重新编译、不更换默认0.1.0发行包或`dependencies.json`。

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\development\scripts\Package-Plugin.ps1 -Development
```

需要已安装的游戏与BepInEx中的Cecil，脚本只读取游戏版本及程序集元数据，不加载插件或启动游戏。
可传`-GamePath`选择安装目录；可传`-VerificationPath`选择对应构建摘要，默认使用`development/logs/core-verification.json`。
摘要必须记录Build通过、警告视为错误及执行前封存/执行后输入一致；DLL哈希与Cecil读取的插件版本必须匹配摘要。
没有`-Development`时仍执行旧发行版本限制，不能将开发DLL误打成0.1.0。

输出位于`development/artifacts/playtest/`，包括ZIP和同名`.zip.sha256`。
ZIP文件只有`manifest.json`及`BepInEx/plugins/DaveCoop/DaveCoop.dll`，本轮还含一个空目录条目`BepInEx/plugins/`；schema1兼容既有安装器，额外标记`development-native-unverified`。
不包含游戏、interop/框架DLL、配置、存档、研究报告或机器日志。

当前实际生成包：

- ZIP SHA256：`29E99BAAC9E0F5E1CDC00B8EB48B94919D98F9C94E028E6343655D9FA6D9AB27`
- DLL SHA256：`D8054A2BAC3AFA0E7F834FA4DCE7DD8A32C25F03D0079F997437781298AC484E`
- 已读取ZIP核对两条文件、空目录条目、manifest、sidecar、DLL哈希及编译版本；373项Core/TCP及Build通过；六组新测试验证身体来源与客机帧分离、过期和代次拒绝，不执行原生身体导出或鱼区域回调，实机仍待验证。

0.1.47及0.1.46历史包保留；同一打包脚本的9项错误参数/失配拒绝来自该历史轮，默认发行文件未变。本轮没有因版本更新重复这些检查，也没有将其写成新版原生试玩通过。

将ZIP与sidecar放在一起，游戏保存退出并按[存档备份说明](SAVE_BACKUP.md)完成备份和`-VerifyBackup`复核后，才可用现有入口安装；双方应使用同一开发包：

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1 -PackagePath .\development\artifacts\playtest\DaveCoop-0.1.48-dev.zip
```

安装命令会写插件目录，本轮没有执行安装、部署或启动，也没有改配置或存档。

角色配置可由`development/scripts/New-PlaytestConfig.ps1 -HostAddress <房主实际IPv4> -Port <双方约定端口>`生成到新的`development/artifacts/`目录。它只生成Host/Guest两份cfg，不安装、不覆盖现有cfg、不启动游戏。已用保留测试地址验证生成及7项输入/路径拒绝，共8项检查通过；测试地址不能作为实际连接地址。

Guest配置开启实验Guest启动及角色功能；鱼叉、个人袋和其它诊断默认关闭。完整Guest持久写隔离尚未验收，备份不是隔离证明。不能因为生成成功而直接覆盖玩家配置。本机目前只有一台电脑，尚未执行双游戏测试，也未安装任一配置。

试玩应先核对新进程版本与加载日志，再确认双端连接、同层独立移动/互见/各自镜头及正常断开；这些还不是已通过验收。

真实两端测试时，先启动Host并选择F11 → Host room，再启动已配置的Guest，分别核对本次`DAVECOOP_NETWORK_CONNECTED`和Guest启动状态。两端都须手动开始自然新潜水：客机不会自动入海；房主先进入新潜水后，客机须在场景等待90秒超时前进入。分别核对`DAVECOOP_LAYOUT_READY`及Session Ready；移动还要求真实Guest场景与鱼隔离来源通过，握手和收到角色状态不足以证明可移动，失败时保留原因日志。Guest断开后须退出进程重新启动，才能回到个人进度。
实验功能保持原有默认关闭设置；原生ABI、Guest完整隔离、远区实际玩法、捕获收益及正常返航入仓保存均未验证，
每人独立袋/容量/重量/负重及完整M3—M7仍须完成。详细范围见[员工个人袋](CREW_CARGO.md)和[接手记录](HANDOFF.md)。
