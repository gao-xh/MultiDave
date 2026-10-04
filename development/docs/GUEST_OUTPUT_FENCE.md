# 客机持久输出围栏的精确声明

0.1.38当前补充见[客机路线采用](GUEST_MAP_ROUTE_ADOPTION.md)及[本轮285/285/Build摘要](../logs/map-route-adoption-build-verification.json)：protocol7/v2完整声明路线字段、30项host origin目标及固定pending-manager出生来源、9项默认关闭guest加载消费者，实际Reset后/CoLoad前六根安装源码已接；没有运行native、IGP采用或全世界/收益验收。以下0.1.37及更早数字/流程保留为历史。

本页给出可直接用于声明匹配的清单和返回策略。它来自本机生成互操作元数据、包装器 IL，以及此前原 GameAssembly 的静态调用报告；没有运行游戏、原生克隆、字段交换、保存或挂钩。它不是所有 writer 的覆盖证明，不授予 `GuestStateIsolated`、`WorldAuthority`、捕获或结算权限。影子根和旧引用边界另见 [GUEST_ISOLATION](GUEST_ISOLATION.md)。

## 0.1.37 默认关闭的自然初始化 profile

实际源码接线见[GUEST_INITIALIZATION_BOOTSTRAP](GUEST_INITIALIZATION_BOOTSTRAP.md)与[本轮摘要](../logs/guest-initialization-build-verification.json)：275/275实际Core/TCP及插件Build通过，未运行原生围栏或游戏。`GuestOutputFence` 默认构造仍是 `ExistingCaches`，194项全部初始阻断。`NaturalInitialization` 只用于本次实际Guest startup source，完整inventory197项，初始156阻断、41延后；配对原加载返回后、任何serializer/根交换前，`SealInitialization()` 一次安装延后目标，原有持久阻断不卸除。Phase计数区分完整声明、初始阻断、尚未安装的deferred、已读回自己的installed及已派发patch attempt，不把声明数量当运行验收。

41条精确白名单为SaveSystem三个Load入口、四derived manager各CreateManagedData/OnLoadData、Player derived LoadData/SetLoadedData，以及四closed base各七个建根/Load方法；完整声明和数量见新启动页。四处 `CreateNewAndSave(bool)` 不放过，没有底层write覆盖证明时不支持靠它补缺档。

只在Natural profile新增这三个精确public static void wrapper：

- `Il2CppSystem.IO.File.Delete(string)`
- `Il2CppSystem.IO.File.Copy(string,string)`
- `Il2CppSystem.IO.File.Copy(string,string,bool)`

离线Cecil实际核准3/3声明，未执行它们。任一声明缺失会使Natural安装拒绝；任一匹配调用仍跳过原方法，同时增加 `BlockedFileOperations` 并锁存健康失败。source首次进入及后续窗口要求该计数为0，不能把skip void当Copy/Delete成功。未知方法、错线程、partial install/seal或cleanup未知均保留已有持久阻断及owner，不自动unpatch或重复安装。

以下194清单保留ExistingCaches合同；Natural是其基础上只增加这三项。197不是全部writer、唯一native地址或ABI证明，未证明其它File/Directory、接口实现、已经在途输出及Steam客户端同步。本source真实quiet仍false，Disconnect不Restore/unpatch/free，退出并以关闭开关的新进程启动才能切回个人角色。

运行 [Inspect-GuestOutputApi.ps1](../scripts/Inspect-GuestOutputApi.ps1) 可重新生成忽略目录里的 `.local/analysis/guest-output-fence-api.json`：

~~~powershell
.\development\scripts\Inspect-GuestOutputApi.ps1
~~~

脚本只用 Cecil 读取五个生成程序集和 Il2CppInterop.Runtime。实际输出为 130 条 declared 方法、88 条四种 closed base 方法展开，`MissingNames=[]`；数量包含可选声明和两个需要专门处理 ref/out 的方法，不是已经安装的 detour 数。报告逐条保存 Owner/ReflectionOwner/Name/static/virtual/abstract、完整参数、返回、包装器 IL 和建议策略。程序集读前后 hash 一致；旧原 PE 报告只保留其原 hash，不冒充本轮重新验证过的原文件。

## 可实施的 frame 内租约

先创建自己的 Harmony owner，并对 manifest 中每条声明用 `DeclaredOnly`、正确 static/instance、完整参数和返回进行精确匹配。所有目标安装和自身注册核对成功后，才能发出绑定实际主线程、manager/root 身份、HostBindingId 和 LeaseId 的 fence 租约。不是根据 Room/scene 名字猜客机，也不凭 caller 的几个 bool 放行。

围栏活动时阻止所列输出方法执行；非活动时原参数、原返回和原逻辑不变。bool 明确返回 false，void 只跳过原方法，不调用伪造成功回调。即使统计或日志失败，活动围栏也不能回落为放行。回调无需读取原生参数、JSON、路径、账户或 byte buffer；只记 bounded CLR 方法标签、计数和租约状态。

活动租约来自确定的 frame 内事务，不能单靠 Unity 线程局部变量挡输出：其他线程若进入已列 writer，仍须阻止并使健康状态失效。异常、manager 替换、安装/清理失败、未知在途输出或恢复不完整时保持 fence 与强引用，不释放为普通保存。原有异步写、云请求、stream、iterator/delegate 不会因新增 prefix 自动取消；没有原生 quiescence 证明就不能宣布隔离或安全恢复完毕。

仅匹配下表原游戏/Steam/Unity 声明，不全局 patch 任意 `System.IO`。目前原 PE 有直接文件写、目录创建、加载转换/Copy/Delete 和未解析调用；这证明不能只挡最后的 SaveGameData，却不能证明下面已经覆盖这些路径。

## 四种 closed generic 与 override

基础类型必须展开为四个真实 closed 类型，不能 patch open generic 并声称覆盖全部：

- ``DR.Save.SaveLoadManagerBase<SaveData>``
- ``DR.Save.SaveLoadManagerBase<DR.Save.SavePlayerData>``
- ``DR.Save.SaveLoadManagerBase<SavePhotoData>``
- ``DR.Save.SaveLoadManagerBase<DR.Save.SaveUserOptions>``

原ExistingCaches显式manifest排除22条open-base声明和2条abstract interface，再纳四个闭包的88条，合同为106+88=194条。Natural另加三个File声明，仍不扩大这份基础表或放开writer。194不是全部writer、唯一原生地址数或已经安装的挂钩数。基础表中的两个out方法展开为8条，必须使用各closed类型的失败prefix。

下表基础声明中的 `T` 按对应类型替换，`T&` 是真实 byref。Game/Photo 的 declared SaveData/DeleteSaveFile，Player 的 declared LoadData/SetLoadedData，以及各 derived OnLoadData/CreateManagedData/Reset 必须分别检查。Player/UserOption 没有声明自己的 SaveData，不可用 derived `DeclaredOnly` 查到不存在的方法后静默跳过。

四个闭包可能共享原生实现地址；declared MethodInfo、相同包装器字段名、独立 CLR 泛型 static 字段或 Harmony owner 存在，都不证明四个原生入口被完整拦截。要记录闭包原方法信息和实际 detour/alias 情况，未知、冲突或某个实例不可匹配应使安装失败，不重复堆叠 detour 后谎报覆盖。此前静态报告明确没有解决具体泛型实例和间接/虚调用。接口方法也只是签名，不能把 abstract interface 注册当具体 writer 被保护。

## 返回与 out 参数

| 原返回 | 活动围栏的准确失败策略 |
| --- | --- |
| void | 返回 prefix false；不合成成功 callback |
| bool | 设置 `__result=false` 后返回 prefix false |
| NativeMethods 的 API-call ulong | 设置 0 |
| NativeMethods 的 FileWriteStreamOpen ulong | 设置 `ulong.MaxValue` |
| Steamworks.SteamAPICall_t | 本机为真正 CLR struct；default 后 `m_SteamAPICall=0` |
| Steamworks.UGCFileWriteStreamHandle_t | 本机为真正 CLR struct；default 后直接赋 `m_UGCFileWriteStreamHandle=ulong.MaxValue` |
| Toolbox.SaveSystem.SaveResult | 显式 Failed=2；默认 Succeed=0 会谎报成功 |
| Toolbox.SaveSystem.DeleteResult | 显式 Failed=1；默认 Succeed=0 会谎报成功 |
| 未确认结果、ref-return、泛化 out | 失败安装，不猜默认值 |

Steam 流的 Invalid 是 `0xffffffffffffffff`，与 API-call 的零失败值不同。[Steam Remote Storage 文档](https://partner.steamgames.com/doc/api/ISteamRemoteStorage#FileWriteStreamOpen)、[Steamworks.NET 类型声明](https://github.com/rlabrecque/Steamworks.NET/blob/master/com.rlabrecque.steamworks.net/Runtime/types/SteamRemoteStorage/UGCFileWriteStreamHandle_t.cs)

本机生成 Steam handle 的 ulong 构造器调用原生 `il2cpp_runtime_invoke`，不能在 fence callback 里为构造失败值再执行它。使用 CLR struct 的直接 UInt64 字段即可；自己的 typed-return ABI 仍需要实际验证，不凭此字段清单宣称已验证。

`TryLoadFromSlot(int,SaveSlotType,out T)` 和 `TryLoadFromJson(string,out T)` 返回 false 时还应由对应 closed typed prefix 将 out 引用置 null。只设置 bool 不定义 out 值不够。报告保留这两个候选，但缺精确 typed out 处理时不能纳入通用跳过策略，也不能因此声称 load 分支全覆盖。原 GDK callback 型声明同样不能伪回调成功。

PlayerPrefs 四个 `_Injected` 的 `ManagedSpanWrapper&` 是输入 ref span，必须保持参数不动；bool 失败或 void 跳过即可，不套用 out-null 规则。报告的 ParameterRoles 和包装器 IL 可用于区分这些声明。

## 逐条匹配清单

下表都是完整生成声明，括号内顺序不能因 Optional/default 值而减少参数。nested 的 Cecil `/` 在 reflection 中使用 `+`；报告另有 ReflectionOwner。基础表仍写 T，展开后的准确参数见报告 ClosedBaseTargets。此清单可比首个 manifest 更宽：abstract interface、ref/out 或扩展系统若不纳实际 manifest，应明确记录未覆盖，不能静默宣称成功。


### DR.Save.SaveLoadManagerBase`1

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveLoadManagerBase`1::Reset()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::ReportProgressMissionState()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::SaveData(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::CopyFileToCloud(System.Int32,System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::WriteOnCloud(System.String,System.Int32,DR.Save.SaveSlotType)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::WriteAllAutoSaveOnCloud(System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::SaveBackupData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Boolean DR.Save.SaveLoadManagerBase`1::SaveSlotWithJson(System.String,System.Int32,DR.Save.SaveSlotType)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveLoadManagerBase`1::SaveOnSelectedSlot(System.Int32,DR.Save.SaveSlotType)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveLoadManagerBase`1::TryLoadFromSlot(System.Int32,DR.Save.SaveSlotType,T&)`` | False | Typed closed out-data=null and __result=false; no original load. |
| ``System.Boolean DR.Save.SaveLoadManagerBase`1::TryLoadFromJson(System.String,T&)`` | False | Typed closed out-data=null and __result=false; no original load. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::SetLoadedData(T)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::LoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::LoadFromCloud(System.Int32,DR.Save.SaveSlotType)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::LoadAllFromCloud()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::WriteOldFileOnConvert(System.String,System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::CreateNew(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::CreateNewAndSave(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::DeleteSaveFile(System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::DeleteSaveFile()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::CreateManagedData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveLoadManagerBase`1::OnLoadData()`` | False | Skip original; no success callback or result synthesized. |

### DR.Save.SaveSystem

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveSystem::SaveAllData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Boolean DR.Save.SaveSystem::SaveGameDataInSlot(System.Int32,System.Boolean)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveSystem::SaveGameDataInSlot(System.Int32,System.Boolean,DR.Save.SaveSlotType)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveSystem::LoadGameDataFromSlot(System.Int32,DR.Save.SaveSlotType)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveSystem::LoadGameDataFromJson(System.String)`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveSystem::TrySaveGameData()`` | False | Skip original; __result=false. |
| ``System.Boolean DR.Save.SaveSystem::SaveGameData()`` | False | Skip original; __result=false. |
| ``System.Void DR.Save.SaveSystem::SavePhotoData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::DeleteGameData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::LoadGame()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::ResetAfterCloudLoad()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::ReloadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::LoadGameOnInit()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::CheckSaveVersion()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::LoadAllData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::TestSaveGameData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::TestLoadGameData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystem::TestSavePhotoData()`` | False | Skip original; no success callback or result synthesized. |

### DR.Save.SaveSystemGameDataManager

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveSystemGameDataManager::Reset()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::SaveData(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::CreateManagedData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::OnLoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::CheckSaveVersion()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::CopyDemoSaveFiles()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemGameDataManager::DeleteSaveFile()`` | False | Skip original; no success callback or result synthesized. |

### DR.Save.SaveSystemPhotoDataManager

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveSystemPhotoDataManager::SaveData(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPhotoDataManager::CreateManagedData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPhotoDataManager::OnLoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPhotoDataManager::DeleteSaveFile()`` | False | Skip original; no success callback or result synthesized. |

### DR.Save.SaveSystemPlayerDataManager

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveSystemPlayerDataManager::CreateManagedData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPlayerDataManager::LoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPlayerDataManager::OnLoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPlayerDataManager::SetLoadedData(DR.Save.SavePlayerData)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemPlayerDataManager::ClearData(System.Boolean)`` | False | Skip original; no success callback or result synthesized. |

### DR.Save.SaveSystemUserOptionManager

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void DR.Save.SaveSystemUserOptionManager::CreateManagedData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemUserOptionManager::OnLoadData()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void DR.Save.SaveSystemUserOptionManager::SetMakeBackupEndingSlot(System.Boolean,System.Boolean)`` | False | Skip original; no success callback or result synthesized. |

### GDKSaveLoadModule

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void GDKSaveLoadModule::SaveData(System.String,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void GDKSaveLoadModule::DeleteData(System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void GDKSaveLoadModule::DeleteFiles(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray)`` | False | Skip original; no success callback or result synthesized. |

### SteamAchievements

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void SteamAchievements::SetStatById(System.String,System.Int32)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::UnlockAchievement(System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::UnlockAchievementWithStat(System.String,System.String,System.Int32)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::UpdateAchievementValueWithStat(DR.Achievement,System.Int32,System.Boolean)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::LockAchievement(System.String)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::ResetAll()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::OnDREvent(ResetEvent)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::OnDREvent(AchievementUpdateDataEvent)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::UnlockProgressSyncFromSave()`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::CompletedMission(MissionData,System.Int32)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void SteamAchievements::UpdateMissionCondition(System.Int32)`` | False | Skip original; no success callback or result synthesized. |

### Steamworks.NativeMethods

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileWrite(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Int32)`` | True | Skip original; __result=false. |
| ``System.UInt64 Steamworks.NativeMethods::ISteamRemoteStorage_FileWriteAsync(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.UInt32)`` | True | Skip original; __result=0 (API call invalid). |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileForget(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileDelete(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=false. |
| ``System.UInt64 Steamworks.NativeMethods::ISteamRemoteStorage_FileShare(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=0 (API call invalid). |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_SetSyncPlatforms(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,Steamworks.ERemoteStoragePlatform)`` | True | Skip original; __result=false. |
| ``System.UInt64 Steamworks.NativeMethods::ISteamRemoteStorage_FileWriteStreamOpen(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=UInt64.MaxValue (stream invalid). |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileWriteStreamWriteChunk(System.IntPtr,Steamworks.UGCFileWriteStreamHandle_t,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Int32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileWriteStreamClose(System.IntPtr,Steamworks.UGCFileWriteStreamHandle_t)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_FileWriteStreamCancel(System.IntPtr,Steamworks.UGCFileWriteStreamHandle_t)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_BeginFileWriteBatch(System.IntPtr)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamRemoteStorage_EndFileWriteBatch(System.IntPtr)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_SetStatInt32(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,System.Int32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_SetStatFloat(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,System.Single)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_UpdateAvgRateStat(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,System.Single,System.Double)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_SetAchievement(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_ClearAchievement(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_StoreStats(System.IntPtr)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_IndicateAchievementProgress(System.IntPtr,Steamworks.InteropHelp/UTF8StringHandle,System.UInt32,System.UInt32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.NativeMethods::ISteamUserStats_ResetAllStats(System.IntPtr,System.Boolean)`` | True | Skip original; __result=false. |

### Steamworks.SteamRemoteStorage

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileWrite(System.String,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Int32)`` | True | Skip original; __result=false. |
| ``Steamworks.SteamAPICall_t Steamworks.SteamRemoteStorage::FileWriteAsync(System.String,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.UInt32)`` | True | Skip original; default struct has m_SteamAPICall=0. |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileForget(System.String)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileDelete(System.String)`` | True | Skip original; __result=false. |
| ``Steamworks.SteamAPICall_t Steamworks.SteamRemoteStorage::FileShare(System.String)`` | True | Skip original; default struct has m_SteamAPICall=0. |
| ``System.Boolean Steamworks.SteamRemoteStorage::SetSyncPlatforms(System.String,Steamworks.ERemoteStoragePlatform)`` | True | Skip original; __result=false. |
| ``Steamworks.UGCFileWriteStreamHandle_t Steamworks.SteamRemoteStorage::FileWriteStreamOpen(System.String)`` | True | Skip original; default struct then direct m_UGCFileWriteStreamHandle=UInt64.MaxValue; do not call ulong constructor. |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileWriteStreamWriteChunk(Steamworks.UGCFileWriteStreamHandle_t,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>,System.Int32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileWriteStreamClose(Steamworks.UGCFileWriteStreamHandle_t)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamRemoteStorage::FileWriteStreamCancel(Steamworks.UGCFileWriteStreamHandle_t)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamRemoteStorage::BeginFileWriteBatch()`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamRemoteStorage::EndFileWriteBatch()`` | True | Skip original; __result=false. |

### Steamworks.SteamUserStats

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Boolean Steamworks.SteamUserStats::SetStat(System.String,System.Int32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::SetStat(System.String,System.Single)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::UpdateAvgRateStat(System.String,System.Single,System.Double)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::SetAchievement(System.String)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::ClearAchievement(System.String)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::StoreStats()`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::IndicateAchievementProgress(System.String,System.UInt32,System.UInt32)`` | True | Skip original; __result=false. |
| ``System.Boolean Steamworks.SteamUserStats::ResetAllStats(System.Boolean)`` | True | Skip original; __result=false. |

### TKoU.UniversalSaveSystem.ISaveSystemService

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Void TKoU.UniversalSaveSystem.ISaveSystemService::FileWriteBytes(TKoU.UniversalSaveSystem.Utils.RelativePath,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1<System.Byte>)`` | False | Skip original; no success callback or result synthesized. |
| ``System.Void TKoU.UniversalSaveSystem.ISaveSystemService::FileDelete(TKoU.UniversalSaveSystem.Utils.RelativePath)`` | False | Skip original; no success callback or result synthesized. |

### Toolbox.SaveSystem.SaveManager

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``Toolbox.SaveSystem.SaveResult Toolbox.SaveSystem.SaveManager::Save(Il2CppSystem.Object,System.Int32,System.Boolean)`` | False | Skip original; SaveResult.Failed=2, never default Succeed=0. |
| ``Toolbox.SaveSystem.DeleteResult Toolbox.SaveSystem.SaveManager::Delete(System.Int32)`` | False | Skip original; DeleteResult.Failed=1, never default Succeed=0. |

### UnityEngine.PlayerPrefs

| 完整声明 | static | 策略 |
| --- | --- | --- |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetInt(System.String,System.Int32)`` | True | Skip original; __result=false. |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetFloat(System.String,System.Single)`` | True | Skip original; __result=false. |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetSetString(System.String,System.String)`` | True | Skip original; __result=false. |
| ``System.Void UnityEngine.PlayerPrefs::SetInt(System.String,System.Int32)`` | True | Skip original; no success callback or result synthesized. |
| ``System.Void UnityEngine.PlayerPrefs::SetFloat(System.String,System.Single)`` | True | Skip original; no success callback or result synthesized. |
| ``System.Void UnityEngine.PlayerPrefs::SetString(System.String,System.String)`` | True | Skip original; no success callback or result synthesized. |
| ``System.Void UnityEngine.PlayerPrefs::DeleteKey(System.String)`` | True | Skip original; no success callback or result synthesized. |
| ``System.Void UnityEngine.PlayerPrefs::DeleteAll()`` | True | Skip original; no success callback or result synthesized. |
| ``System.Void UnityEngine.PlayerPrefs::Save()`` | True | Skip original; no success callback or result synthesized. |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetInt_Injected(UnityEngine.Bindings.ManagedSpanWrapper&,System.Int32)`` | True | Input ref spans untouched; skip original and __result=false. |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetFloat_Injected(UnityEngine.Bindings.ManagedSpanWrapper&,System.Single)`` | True | Input ref spans untouched; skip original and __result=false. |
| ``System.Boolean UnityEngine.PlayerPrefs::TrySetSetString_Injected(UnityEngine.Bindings.ManagedSpanWrapper&,UnityEngine.Bindings.ManagedSpanWrapper&)`` | True | Input ref spans untouched; skip original and __result=false. |
| ``System.Void UnityEngine.PlayerPrefs::DeleteKey_Injected(UnityEngine.Bindings.ManagedSpanWrapper&)`` | True | Input ref spans untouched; skip original without synthesized success. |

## 原 PE 与覆盖边界

报告附入已有 `guest-save-native-calls.json` / `guest-shadow-clone-native-calls.json` 的相关静态边和报告 hash。它们显示 save 基类静态路径可到 Serialize、加密、目录和文件写，load 可到云读取、转换、Copy/Delete。原方法范围、共享别名、间接调用和 closed generic 仍有限；此脚本没有重新反汇编，也没调用 writer。

具体 manifest 的范围还不包含所有其他保存系统实现、直接 native 文件 API、未解析 delegate/async、所有 Steam Workshop 输出或 Steam 自动云同步。PlayerPrefs 在活动租约内挡 setter/delete/Save 是局部输出围栏，不是判断所有键都是游戏进度，也不应永久禁用玩家偏好设置。袋/任务/图鉴和已捕获旧对象的内存更新仍需 shadow/cache/alias 完整隔离。

## 本机强 GC 接口

installed Runtime informational version 为 `1.5.3+dbda1cb353b0f4253345dc45136d170b9e50a5a0`。Cecil 确认四个 public static P/Invoke 的签名使用 **IntPtr**，不是 uint：

~~~csharp
IntPtr IL2CPP.il2cpp_gchandle_new(IntPtr pointer, bool pinned);
IntPtr IL2CPP.il2cpp_gchandle_new_weakref(IntPtr pointer, bool trackResurrection);
IntPtr IL2CPP.il2cpp_gchandle_get_target(IntPtr handle);
void IL2CPP.il2cpp_gchandle_free(IntPtr handle);
~~~

普通 `Il2CppObjectBase` 构造后内部持有 non-pinned 强 GC handle；Pointer 由该 handle 取得 target，零 target 会抛异常；finalizer 释放内部 handle。这与本机 IL 和同 commit [Il2CppObjectBase 源码](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.Runtime/InteropTypes/Il2CppObjectBase.cs)、[IL2CPP extern 源码](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.Runtime/IL2CPP.cs) 一致。

保留强 CLR wrapper 引用可保持其 handle 存活。若桥另开显式 handle，它必须独立所有、明确只释放一次；不能手动释放 wrapper 私有 handle，也不能用 weakref 保证恢复引用。GC 存活不阻止 Unity 对象 Destroy、不证明 manager 生命周期/所有子树别名已经隔离；恢复仍须核对具体实例和直接根。任何 handle/native ABI/深复制/回调覆盖未知，都保持隔离与原生执行权限 false。
