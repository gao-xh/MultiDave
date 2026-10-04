# 存档备份

游戏保存退出后，在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\development\scripts\Backup-Saves.ps1
```

默认查当前环境用户的`AppData/LocalLow/nexon/DAVE THE DIVER`整个目录，包含全部SteamSData用户及照片、设置、旧档等，不按槽位或扩展名筛选。
Steam安装root按三个注册表位置发现，各`userdata/*/1868140`整目录一并复制；可选追加当前游戏BepInEx的`.cfg`作为配置上下文。
找不到实际LocalLow目录会拒绝；沙盒/不同Windows用户下可显式传`-UserProfilePath`，Steam位置可传`-SteamRoot`，游戏可传`-GamePath`。
这些参数应指向本人实际目录，不沿用开发者路径；脚本不解析或展示存档内容，字节读取仅用于复制和长度/SHA256核验。

每次输出到`development/.local/save-backups/时间-GUID/`新目录，既有备份与原文件均不覆盖。
`-BackupRoot`可指定外部私人目录；在当前仓库内只允许`development/.local`下，存档和manifest均不得提交或公开。
拒绝reparse/link、越界及备份嵌入来源目录；源文件或目录集合、长度、SHA256变化、复制失配或检测到游戏启动时拒绝成功，保留失败目录与`Verified=false`manifest。

成功须核对两次源清单与逐文件备份，manifest最终`Verified=true`才表示本次已发现来源的复制通过。
缺少Steam应用缓存或配置会列为`Missing`、`CoverageComplete=false`；某Steam账户没有`1868140`只表示无该应用缓存可备。
`Verified=true`不能单独证明所有Steam云服务器存档已备份。游戏关闭检查是备份前、复制期间与完成前的采点；脚本不停止Steam、不冻结云同步、不改变云设置，无法保证检查间隙或后续云回写。

只读复核一份已有备份：

```powershell
powershell -ExecutionPolicy Bypass -File .\development\scripts\Backup-Saves.ps1 -VerifyBackup '你的私人备份目录'
```

此模式只核已成功manifest记录的备份文件集合、长度和SHA256，不改文件、不要求旧备份等于今天的live存档，也不恢复。
本脚本已用自写LocalLow/Steam/config夹具验证复制、Missing、原文件不变及拒绝路径。开发机器的首次真实备份与再次只读复核已通过，合计38个文件（LocalLow18个、两个Steam应用缓存共18个、配置2个），LocalLow内有12份`.sav`。一个其它Steam账户无该应用缓存已记录，不据此声称完整云备份。脱敏证据见[验证记录](../logs/save-backup-verification.json)；真实备份、源路径、账户名及manifest只保留本机。
恢复尚未实现或执行。需要恢复时先保存退出游戏，再备份当前存档，按配置Skill核对实际目标及云同步状态并取得明确恢复覆盖授权；不要直接把备份当成已恢复或已保存验收。
