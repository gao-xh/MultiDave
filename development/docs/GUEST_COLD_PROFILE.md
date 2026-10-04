# 从首次加载创建员工临时档案：候选入口

本页保留独立临时路径/冷档的离线研究方案。0.1.37新增默认关闭的[existing-save自然初始化模式](GUEST_INITIALIZATION_BOOTSTRAP.md)，已接实际首次iterator、Guest房间、围栏和五根事务，仍未运行native。它让原游戏先自然加载，再在缓存初始化前换五根，不更改存档路径、槽位或云设置，不是从首次读取就加载独立新冷档。本页的 `ColdProfileImplemented`、`InitialPathBindingVerified`、`FirstLoadOrderVerified`、`GuestStateIsolated`、世界与背包权限仍为false。

0.1.23 增加默认关闭的[自然启动观察源码](SAVE_STARTUP_OBSERVATION.md)，用实际 prefix/postfix/finalizer 记录加载与路径来源候选，尚未部署或执行。[新的精确研究](GUEST_STARTUP_API.md)表明当前框架从场景切换回调加载插件；这不能排除安装前已读个人档，也不能把 Plugin.Load 线程当已确认 Unity 线程。Demo 路径还直接使用 `persistentDataPath`，不能只改普通目录。观察器不重定向路径、不屏蔽云或生成临时档，完整冷档隔离仍未实现。

可研究让新启动的员工进程从第一次读取起就使用独立临时档案，再由游戏正常初始化其食材、任务、地图实例和其它运行缓存。与在个人档案已经加载后交换七个根相比，这有机会避免旧个人子树、协程及未枚举缓存继续引用原档。这个收益是推断，现有证据不能证明启动时所有引用都从四个存档管理器产生，也不能证明路径替换覆盖全部输出。

用户规则保持：房主唯一长期任务、图鉴、材料与经济进度；每人独立背包、容量与负重。临时档案不实现房主地图采用、客机 AI/实例隔离、独立 actor/装备、员工产物分流或返航入仓。员工默认空档还可能缺少同层潜水所需解锁和装备，不能用它替代经过房主裁定的会话初始化。退出员工模式的初版候选是退出进程后以普通模式重新启动；不在同一进程中切回原档、补保存或重放未知输出。

## 可复现检查

运行仓库中的只读脚本：

```powershell
& .\development\scripts\Inspect-GuestProfileApi.ps1
```

可使用 `-GamePath` 指定已安装游戏，`-ReportName` 仅接受单个 JSON 文件名。脚本用 Cecil 读取生成互操作声明与 wrapper IL，记录输入 SHA256 前后不变；不加载生成的游戏类型，不执行 native API、getter、constructor、挂钩或初始化，不读改存档。报告仅写 `development/.local/analysis/guest-profile-api.json`，机器路径和原始报告不提交。

本机一次实际检查读到 12 个程序集、46 个选中类型和 10 个路径/服务引用声明；其中 SaveSystem 路径方法为 11 个、全部选中类型中以 Load/TryLoad/Reload/ResetAfterCloudLoad/OnLoad 开头的方法为 51 个（不是唯一执行地址或完整加载覆盖）。必需类型 MissingTypes 为空，并附上 3 份既有原 GameAssembly 静态调用报告的摘要。`NativeStorageService` 这个精确命名未出现在本次所选程序集；也没有找到直接实现 `TKoU.UniversalSaveSystem.ISaveSystemService` 的具体类型。这只是所选声明范围内的缺口，不能据此认定平台存储服务不存在或不使用。

`NativeFieldProxy` 表示 wrapper 读写 native 字段；运行时仍需要 native 对象和正确线程。`RuntimeInvoke` 表示原生方法调用，不能把公开 getter 当成无副作用元数据读取。脚本只分类 wrapper，不证明真实参数返回 ABI、首次执行顺序或所有 writer 覆盖。

## 路径、槽位与四个管理器

`Assembly-CSharp.dll` 中的 `DR.Save.SaveSystem` 存在以下实际声明：

| 字段代理/属性 | static | 读写分类 | 候选用途及限制 |
| --- | --- | --- | --- |
| `string DefaultSaveFolder` | 是 | 直接字段代理，可写 | 中央目录候选；不知道 `.cctor`、Init 或平台分支是否重新赋值。 |
| `string DefaultSaveFileName` / `DefaultSaveFileExtension` | 否 | 直接字段代理，可写 | 文件命名候选；更换文件名不自动隔离其它保存类别。 |
| `string _DefaultSaveFilePath_k__BackingField` | 否 | 直接字段代理，可写 | 路径基线候选；尚未证明与其它目录/文件名字段的优先级。 |
| `string DefaultSaveFilePath` | 否 | getter/setter 均 RuntimeInvoke | 不是纯字段替换入口。 |
| `bool SkipCloudPullForPreset` | 是 | 直接字段代理，可写 | 只能作为待核对的分支输入；名称不证明所有云读取关闭，更不关闭回写。 |
| `_UserOptionManager`、`_PlayerDataManager`、`_GameDataManager`、`_PhotoDataManager` | 否 | 直接字段代理，可写 | 四个实际 manager 身份可冻结；不能只换 GameData。 |
| `_IsInitialized_k__BackingField`、`_IsLoadFinished_k__BackingField`、`_IsGameLoaded_k__BackingField` | 否 | 直接字段代理，可写 | 观察当前标志用；false 不证明此前没有读写，不能改 false 冒充冷启动。 |

中央路径方法均是需要实际原生执行的声明：

```text
string DR.Save.SaveSystem.GetSaveFilePath(DR.Save.SaveDataType,int,DR.Save.SaveSlotType)
string DR.Save.SaveSystem.GetDemoSaveFilePath(DR.Save.SaveDataType,int,DR.Save.SaveSlotType)
string DR.Save.SaveSystem.GetFailedSaveFilePath(DR.Save.SaveDataType,int,DR.Save.SaveSlotType)
string DR.Save.SaveSystem.GetOldSaveFilePath(DR.Save.SaveDataType,int,DR.Save.SaveSlotType)
string DR.Save.SaveSystem.GetSaveFolder()
string DR.Save.SaveSystem.GetDemoSaveFolder()
string DR.Save.SaveSystem.GetSaveFileName(DR.Save.SaveDataType,int,DR.Save.SaveSlotType)
string DR.Save.SaveSystem.GetSaveFileName(DR.Save.SaveSlotType)
```

`SaveSlotType` 为 Auto=0、Manual=1、Ending=2；`SaveDataType` 为 None=0、UserOption=1、PlayerData=2、GameData=3、PhotoData=4。这些只是 metadata 常量，不证明槽位索引是否合法、空闲、无备份映射或不会影响 Ending 状态。`SaveLoadManagerBase<T>.GetSlotTypeAndIndex(int)` 以及 `MaxAutoSaveSlotCount`、`BackupSaveSlotCount`、`BackUpSlotStartIndex` getter 仍需追踪。任意选择一个较大槽位不能作为临时档案隔离方案。

四个 manager 分别继承 `SaveLoadManagerBase<SaveData>`、`<DR.Save.SavePlayerData>`、`<SavePhotoData>`、`<DR.Save.SaveUserOptions>`。它们各有 `_Data_k__BackingField`，Player 另有 `_InstanceData_k__BackingField`。目前未找到四套独立可写路径字段；已有原 PE 静态边表明泛型基类会调用 SaveSystem 的中央路径生成方法。共享 generic 原生地址、override 和平台服务可能绕开单个上层挂钩，需要逐精确声明核对，不能从一个 generic static 边推导所有四类实例的运行覆盖。

## 首次加载的精确候选边界

| 类型与实际入口 | 必须核对的边界 |
| --- | --- |
| `SaveUtil.Awake()`；`GameBase.Awake_Impl()` | 插件是否在这些入口及任何个人读取之前就已完成冷启动绑定，未证明。 |
| `GameBase.Init(): IEnumerator`；`GameBase/_Init_d__32.MoveNext(): bool` | 工厂调用与每个 MoveNext 的实际首次加载关系，不能用 factory 返回当初始化完成。 |
| `SaveSystem.Init(Il2CppSystem.Action)`；`SaveSystem.InitSaveSystem(Il2CppSystem.Action): IEnumerator`；`SaveSystem/_InitSaveSystem_d__42.MoveNext(): bool` | manager 创建、默认路径赋值、云回灌及最早 Load 的真实因果窗口。 |
| `SaveSystem.LoadAllData()`、`LoadGameOnInit()`、`LoadGame()`、`ReloadData()`、`ResetAfterCloudLoad()` | 四类数据以及重新加载、云加载后处理不能逃出临时绑定。 |
| `SaveSystemUserOptionManager.Awake()` | 可能早于主 GameData 加载创建或读取设置；现有声明不足以排除先读个人设置。 |
| `GameBase.LoadGameData(): IEnumerator`；`GameBase/_LoadGameData_d__43.MoveNext()`；`GameBase.LoadSavedData()` | 首个业务缓存建立前必须已绑定员工临时来源。 |
| `GameBase.InitAfterSaveSystem(): IEnumerator`；`GameBase/_InitAfterSaveSystem_d__45.MoveNext()` | 正常缓存初始化完成的候选观察点，不是完整静止或所有缓存证明。 |

基类还存在 `CreateNew(bool)`、`CreateNewAndSave(bool)`、`CreateManagedData()`、`OnLoadData()`、`SetLoadedData(T)`。它们都是原生业务入口，不能当成只分配 CLR 临时对象的 constructor。冷档方案若以后允许它们自然运行，也必须先证路径和其它输出已完整约束。现在不会执行这些入口。

现有 `guest-save-native-calls.json` 提供了更具体的静态边：

- `SaveLoadManagerBase<T>.SaveData(bool)` 调中央 `GetSaveFolder` / `GetSaveFilePath`，并有 `Directory.Exists/CreateDirectory`、`File.WriteAllText` 边。
- `SaveLoadManagerBase<T>.LoadData()` 调 `LoadAllFromCloud`、`GetSaveFilePath` / `GetOldSaveFilePath` / `GetFailedSaveFilePath`，并有 `File.Exists/ReadAllText/Copy/Delete`、反序列化和版本转换边。因此“加载”自身可以写文件，不能等到首次 Save 再装围栏。
- Player manager 的 `LoadData()` 与 `SetLoadedData(SavePlayerData)` 调基类、创建 `InstanceInteractionData(bool)` 并调用 `SyncInstanceDataWithPlayerData()`；后者调用 `LoadRuntimeIGPHashData()`。冷启动让这一原链从临时 PlayerData 生成缓存是可研究的具体路径，不是已验证的完整 detached 缓存证明。

其它现有报告显示 `SceneLoader.GoToInGameEntry` 在同一方法内触及任务、`UpdatePlayerData` 和 `IngameSaveDataManager.Clear`；只在后续 ChangeSceneAsync 替换状态已经偏晚。`IngredientsStorage.Init` 与任务初始化也会使用当前存档。原 PE 边只表示离线解析出的调用目标，存在间接调用、generic 分支和未知范围；不证明这几步的实际时序或字段数据流。

## 目录以外的输出与来源

冷目录不能隔离这些接口，必须有独立证据与精确策略：

| 来源/输出 | 精确候选与缺口 |
| --- | --- |
| 云存档 | 四个 closed Base 的 `LoadFromCloud(int,SaveSlotType)`、`LoadAllFromCloud()`、`CopyFileToCloud(int,bool)`、`WriteOnCloud(string,int,SaveSlotType)`、`WriteAllAutoSaveOnCloud(string)`，及 SteamRemoteStorage / NativeMethods 读写、异步/流式调用。SkipCloudPull 名称不代表覆盖。 |
| Steam 平台设置 | `SteamRemoteStorage.SetCloudEnabledForApp(bool)` 是原生平台调用。候选方案应限制员工进程的调用，不以永久修改用户云开关代替隔离；Steam 客户端 Auto-Cloud、退出时同步及已经在途的请求仍未核对。 |
| 成就/统计 | `SteamAchievements.Start()`、`SetStatById(string,int)`、`UnlockAchievement(string)`、`UnlockAchievementWithStat(string,string,int)`、`UpdateAchievementValueWithStat(DR.Achievement,int,bool)`；底层 `SteamUserStats.SetStat`、`SetAchievement`、`StoreStats`、`ResetAllStats` 等不依赖临时目录。UniversalAchievement service 的真实实例/实现未证明。 |
| PlayerPrefs | `SetInt/SetFloat/SetString`、TrySet/Injected、DeleteKey/DeleteAll/Save 与原生缓存独立。临时设置可研究仅本进程的内存键视图，但读取、输出和退出 flush 必须一起约束。 |
| Toolbox | `Toolbox.SaveSystem.SaveManager.SaveFolderPath` 为 static RuntimeInvoke getter，无可写 backing 声明；`slotNumber` 可写但不代表 DR.Save 槽位。`Awake/ReadSaveFile/LoadAllSaveDatas/GetFilePath(int)` 是另外的路径边界。 |
| PixelCrushers | `DiskSavedGameDataStorer.GetSaveGameFilename/GetSavedGameInfoFilename/WriteStringToFile`；`PlayerPrefsSavedGameDataStorer.GetPlayerPrefsKey/StoreSavedGameData`。声明不证明游戏实际使用，但未能排除独立持久写。 |
| GDK/通用平台 | `GDKSaveLoadModule._saveSystemService` 为直接字段代理；其 `InitializeSaveSystemService(): IEnumerator` 及 File/Move/Delete 经 `ISaveSystemService` 的实际实现未识别。在 Steam 上是否运行未证明。RelativePath 的 constructor/SetValue 为原生调用，不可认为自动重定向到临时根。 |
| 额外路径持有者 | `DR.TCS.FileDataVer0/Ver1.SaveFilePath` 是可写直接字段代理；是否为当前存档、迁移或独立系统未知，不能忽略。Unity `Application.persistentDataPath` 为只读 RuntimeInvoke getter，没有声明级目录 setter。 |

现有 [已枚举输出围栏](GUEST_OUTPUT_FENCE.md) 的默认ExistingCaches194目标及Natural197目标只是声明合同，没有证明writer全覆盖、初始执行顺序、native ABI、共享泛型地址、所有接口实现、System.IO所有调用、在途输出或Steam客户端同步。Natural初始阻断156项、只延后41精确内存建根/加载声明；原load返回后clone前一次Seal197。新增File.Copy/Delete三入口任一尝试撤健康，不能把skip void当成功。此策略不是临时目录或独立首次读取证明，也不能关掉旧persist fences来宣称安全。

## 独立冷档路径方案的后续候选（未采用）

先完成“启动前参数 → 不可变临时身份 → 最早路径/云/设置/成就边界”的只读来源记录，不接 Join 或改 true。唯一 profileId 绑定本进程，所有 Auto/Manual/Ending、backup、old、failed、demo 与四类数据的路径都需证明落在同一预期临时根；越界、来源未知或已经发生个人 Load 时拒绝进入。对真实 Unity 线程、manager 固定身份及每次原 factory/MoveNext 重新核对，不能从 `IsInitialized=false` 推断未加载。

随后才实现默认关闭的冷启动 adapter：在全输出与路径策略健康时让原四 manager 自然加载临时数据，并按真实缓存图验证 Mission、Ingredients、Ingame、Interaction、Photo、Options 来源；任何缺口保留拒绝。路径 prefix 的字符串返回、shared generic 和异步调用必须有实际 ABI 与顺序证据。若发生已经进入的未知输出，不通过重写、删除个人文件、补 Save 或重新 Load 猜恢复。

通过上述前置证据后，冷档有机会减少热切换七根所需的原图恢复工作。它仍需房主选择实际采用、员工 own actor 与原生鱼 AI/进度隔离、独立容量/产物分流、逐项返航入仓及双端测试；本轮研究没有替换既有源码方案或完成这些权限。
