# 首次加载、框架触发与保存路径的精确入口

本页为只读离线研究，补充[冷档候选](GUEST_COLD_PROFILE.md)的实际声明和静态调用边。没有运行游戏、native API、构造、字段 getter、挂钩、首次加载或存档操作；没有改变启动模式、目录、槽位或云设置。以下入口可供默认关闭的自然观察适配器使用，声明与静态边都不证明原生 ABI、首次执行顺序、最终目录绑定或完整隔离。

## 复现与报告

从仓库根目录执行[Inspect-GuestStartupApi.ps1](../scripts/Inspect-GuestStartupApi.ps1)：

```powershell
.\development\scripts\Inspect-GuestStartupApi.ps1
```

脚本支持 `-GamePath`，通过 Steam 定位已安装游戏；`-ReportName` 只接受单个 JSON 文件名。Cecil 读取游戏互操作声明与已安装 BepInEx/Interop 的 managed IL，不加载生成游戏类型，不下载依赖、不打开存档。报告只写忽略目录 `.local/analysis/guest-startup-api.json`；输入读前后 SHA256 一致、必需类型均找到后原子替换，旧报告不代表失败后的 fresh 成功。

本次实际离线报告读取 5 程序集、22 类型，附 45 个所选自然 hook 声明和 8 个框架方法；声明数量不是挂钩注册数或唯一 native 地址数。`SelectedHookDeclarations` 标明 open generic owner，不能直接把基类模板当作已覆盖四个实例。`.cctor` 记录属于生成 wrapper 元数据初始化，不能当作原游戏 `.cctor` 的已运行证据。

原 PE 研究使用已有[Inspect-NativeCalls.ps1](../scripts/Inspect-NativeCalls.ps1)：

```powershell
.\development\scripts\Inspect-NativeCalls.ps1 -Method `
  'SaveUtil::Awake','GameBase::Awake_Impl','GameBase::Init','GameBase::LoadGameData', `
  'DR.Save.SaveSystem::Init','DR.Save.SaveSystem::InitSaveSystem','DR.Save.SaveSystem::.cctor', `
  'DR.Save.SaveSystem::LoadAllData','DR.Save.SaveSystem::LoadGameOnInit', `
  'DR.Save.SaveSystem::GetSaveFolder','DR.Save.SaveSystem::GetSaveFilePath', `
  'DR.Save.SaveSystem::GetDemoSaveFolder','DR.Save.SaveSystem::GetDemoSaveFilePath', `
  'DR.Save.SaveSystem::GetFailedSaveFilePath','DR.Save.SaveSystem::GetOldSaveFilePath', `
  'DR.Save.SaveSystemUserOptionManager::Awake' `
  -Depth 1 -MaxMethods 256 -ReportName 'guest-startup-native-calls'
```

实际输出 16 exact selector roots、176 method records，另按编译器名字关联 3 个 iterator MoveNext；它们不增加 Roots 数。81 条没有包含入口的 unwind range，工具不猜 leaf 主体；1 条 GameBase.Init.MoveNext 为 partial bounded decoding，方法配额未触顶、遗漏根与指令配额截断为零。其它条目也只证明已知 unwind 片段，不证明完整方法、分支可达性或间接调用。原始报告、native 地址和游戏 DLL 不进入公开文件，范围见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。

## 插件 Load 的实际触发边界

本机 `BepInEx.Unity.IL2CPP.dll` 的 managed IL 显示，`IL2CPPChainloader.Initialize(string)` 对 runtime invoke 安装框架 detour；`OnInvokeMethod(IntPtr,IntPtr,IntPtr,IntPtr)` 比较方法名 `Internal_ActiveSceneChanged`。匹配路径包含 PreloadInteropAssemblies 和 BaseChainloader.Execute，随后才派发原 runtime-invoke delegate。LoadPlugin 的已读顺序是创建插件实例、PluginLoad 事件、调用 BasePlugin.Load。

因此报告的 `PrimaryLoaderTriggerObservedInIl=true` 只表示本机框架 IL 中存在这个触发点，不表示实际游戏已经执行它。它提供“该回调原调用前加载插件”的框架候选时机，不能证明插件早于所有 SaveUtil/GameBase/UserOption Awake、SaveSystem 原静态初始化、平台服务创建或个人文件读取。事件发生前是否已有场景初始化工作，仍缺本游戏实际时序；不能将插件 Load 时间、首次 Update 或 `IsInitialized=false` 转成 `FirstLoadOrderVerified`。

反射读取生成类型声明不等于读取了 game 字段，但 CLR wrapper/native class 的后续初始化、Harmony detour 注册仍可能涉及 native 工作，必须另记 ABI 与初始化边界。当前仅做离线声明研究，不安装观察或改原方法。

## 最小精确自然观察声明

以下均由 `Assembly-CSharp.dll` 声明，为 instance 方法；业务包装器调用 RuntimeInvoke。前后观察应保留原参数、原返回和异常，不调用 MoveNext/Load 来补证。

| 声明 | 有限观察意义 |
| --- | --- |
| `void SaveUtil.Awake()` | 早期自然调用候选；本次静态解析没有已识别业务 direct call，不证明没有字段或静态状态修改。 |
| `void GameBase.Awake_Impl()` | 静态边包含 gameObject 与 DontDestroyOnLoad；不能据此证明首次加载尚未发生。 |
| `IEnumerator GameBase.Init()` | 枚举器工厂；不能以返回对象当初始化完成。 |
| `void DR.Save.SaveSystem.Init(Il2CppSystem.Action onDone)` | 静态边包含 InitSaveSystem 工厂；callback 只记录 presence，不 invoke。 |
| `IEnumerator SaveSystem.InitSaveSystem(Il2CppSystem.Action onDone)` | 工厂 owner/iterator 关系候选。 |
| `bool SaveSystem._InitSaveSystem_d__42.MoveNext()` | 原 bool 与每次 state/owner 的观察，不等于首次路径绑定完成。 |
| `void SaveSystem.LoadAllData()`、`LoadGameOnInit()`、`LoadGame()`、`ReloadData()`、`ResetAfterCloudLoad()` | 冷路径不能只覆盖一个初始入口；静态或 runtime load 均可能含文件转换/回灌。 |
| `void GameBase.LoadSavedData()` | 无参数，业务缓存加载的自然候选边界。 |
| `IEnumerator GameBase.LoadGameData()`、`InitAfterSaveSystem()` | 各自工厂须关联自己的 iterator，不能借全局当前 owner。 |
| `bool GameBase._Init_d__32.MoveNext()`、`_LoadGameData_d__43.MoveNext()`、`_InitAfterSaveSystem_d__45.MoveNext()` | 声明均无参，factory/每次执行/原 bool 区分记录。 |
| `void SaveSystemUserOptionManager.Awake()` | 可能有独立早期初始化；静态无 direct edge 不证明未读个人设置。 |

表中 `IEnumerator` 精确为 `Il2CppSystem.Collections.IEnumerator`。Cecil nested 名用 `GameBase/_Init_d__32` 等；C# wrapper 为 `GameBase._Init_d__32`。这些 iterator 直接字段代理包括 `__1__state:int`、`__2__current:Il2CppSystem.Object`、`__4__this:所属owner`；SaveSystem iterator 另有 `onDone:Il2CppSystem.Action`。它们仍是 native 字段访问，未知线程时不得读取 pointer、owner 或 state。

四个 derived manager 各自准确声明 `void CreateManagedData()` 和 `void OnLoadData()`；Player 另覆写 `void LoadData()` 与 `void SetLoadedData(DR.Save.SavePlayerData data)`。其它 LoadData/SetLoadedData 声明来自 `SaveLoadManagerBase<T>`，基类另有同名 CreateManagedData/OnLoadData。为减少 shared generic 假覆盖，可先观察四个 derived 的无参自然入口与 Player 的实际 SetLoadedData，不构造 manager、不重派发创建或加载。方法名 Create 不表示纯分配；SetLoadedData/OnLoadData 的副作用不能当只交换根。

SaveUtil 没有自己的 direct SaveSystem 字段；GameBase 继承 `Singleton<GameBase>`，也没有已声明 SaveSystem owner 字段。不能把当时的全局 singleton 补成它们的调用 parent。SaveSystem 自身继承 `Singleton<SaveSystem>`，其 `_instance` 是直接静态代理；调用公共 Instance 可能创建对象，不用于采样。

## 已核直接字段与路径声明

SaveSystem 的可观察 direct proxies 为 static `DefaultSaveFolder:string`、`SkipCloudPullForPreset:bool`；instance `DefaultSaveFileName:string`、`DefaultSaveFileExtension:string`、`_DefaultSaveFilePath_k__BackingField:string`，以及三个 bool `_IsInitialized_k__BackingField`、`_IsLoadFinished_k__BackingField`、`_IsGameLoaded_k__BackingField`。四 manager 根精确为 `_GameDataManager`、`_PlayerDataManager`、`_PhotoDataManager`、`_UserOptionManager`，类型各为相应 SaveSystem manager。各 derived 的 `_Data_k__BackingField` 类型分别是 SaveData/SavePlayerData/SavePhotoData/SaveUserOptions；Player 另有 `_InstanceData_k__BackingField`。

直接字段 getter 不经 RuntimeInvoke，仍读取 native 内存；业务 `DefaultSaveFilePath` getter/setter、manager getter、状态 getter与下面的路径方法均为原调用。只在已确认 Unity 线程内冻结有限当前字段，不用业务 getter 补值，manager/root presence 也不证明其完整身份、未曾加载或缓存来源。

精确 string 原返回入口如下，均 instance：

```text
string SaveSystem.GetSaveFolder()
string SaveSystem.GetDemoSaveFolder()
string SaveSystem.GetSaveFilePath(SaveDataType,int,SaveSlotType)
string SaveSystem.GetDemoSaveFilePath(SaveDataType,int,SaveSlotType)
string SaveSystem.GetFailedSaveFilePath(SaveDataType,int,SaveSlotType)
string SaveSystem.GetOldSaveFilePath(SaveDataType,int,SaveSlotType)
string SaveSystem.GetSaveFileName(SaveDataType,int,SaveSlotType)
string SaveSystem.GetSaveFileName(SaveSlotType)
string SaveSystem.GetFailedSaveFileName(SaveDataType,int,SaveSlotType)
string SaveSystem.GetOldSaveFileName(SaveDataType,int,SaveSlotType)
string SaveSystem.GetSaveFileExtension()
```

enum 命名空间为 `DR.Save`，底型 int32：SaveSlotType Auto=0/Manual=1/Ending=2；SaveDataType None=0/UserOption=1/PlayerData=2/GameData=3/PhotoData=4。各三参数 path 的 slotType 原声明 optional default=0，Harmony 匹配仍须完整三参签名，不能漏掉最后 enum。

无别名的已识别静态业务边是：普通 path → GetSaveFolder/GetSaveFileName/Path.Combine；Failed/Old path → GetSaveFolder/对应 name/Combine。GetSaveFolder 与 GetDemoSaveFolder 均包含 persistentDataPath/Combine，DemoSaveFilePath 还直接包含 persistentDataPath/GetSaveFileName/Combine。不能只观察或改写 GetSaveFolder 就声称覆盖 Demo、failed、old、backup、所有 slot 与四类数据。边表没有参数/返回字符串或字段数据流，尚未证明 DefaultSaveFolder 与 instance path 的优先级、是否重新赋值、哪些值最终落目录以及所有业务分支。

## SkipCloud 与最小观察合同

本次 InitSaveSystem.MoveNext 的静态目标含 GetSteamID、persistentDataPath/Combine、LoadGame/LoadGameOnInit/TestLoadGameData；存在间接调用及共享地址别名。LoadAllData 含四 manager getter与四个间接派发；不能从表中唯一识别全部具体 Load。LoadGameOnInit 没有 containing unwind range，标为 unavailable，不以名字或相邻范围补原实现。

`SkipCloudPullForPreset` 只已证明 static bool 字段声明；当前 PE 工具没有输出此字段的读取数据流或控制依赖，因此未证它跳过哪条云分支、是否四个 manager 一致、是否仅 preset 以及是否影响回写。既有报告显示 Base.LoadData 可以调用 LoadAllFromCloud，不能把观察该 bool=true 当全部云读取/写入受限。`SkipCloudBranchVerified=false`，成就/统计、PlayerPrefs、其它服务与 Steam 客户端同步仍须分别核对。

可接入的只读合同是：插件 Load 时记录本机框架触发来源候选与固定 run；自然 prefix/postfix 记录 stage、CLR thread、CallId、原标量/原返回摘要，未知线程不触及 wrapper pointer/字段。已确认 Unity 回调后才读上述 direct fields，将 owner/iterator 固定到当次调用，factory 与 MoveNext 成对区分。有界 string 原结果立即转长度/hash，不记录个人完整路径、JSON或本机目录；wrapper 不排入后台队列。丢失、错误、线程未知或配额到顶明确使链不完整，不能继续沿用早期顺序结论。观察既不替换 path/返回、也不关闭云或调用 Load；首次顺序及原生回调 ABI 必须另行实测。

`PluginLoadBeforeEverySaveAwakeVerified`、`InitialPathBindingVerified`、`FirstLoadOrderVerified`、`FinalDirectoryBindingVerified`、`SkipCloudBranchVerified`、`PersistentOutputsComplete`、`GuestStateIsolated`、World/Cargo 权限均 false。冷档尚未采用；每人独立袋/容量/重量/负重、房主唯一长期收益保持，完整资源/actor/cache/output、房主地图采用、个人捕获/产物分流和逐项返航、真实双端与 GitHub 冷配置的 M3—M7 目标不缩减。
