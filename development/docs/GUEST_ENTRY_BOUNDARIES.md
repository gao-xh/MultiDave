# 客机影子根的进入与退出边界

最早值得补充观察的是 `SceneLoader.GoToInGameEntry` 的 **prefix**。原 PE 的静态直接目标显示，这个入口自身包含 Player 数据更新、任务查询/失败处理及 Ingame 清理；只等到 `ChangeSceneAsync` 再安装影子根，不能覆盖它之前可能发生的进度操作。prefix 可在本次原方法主体开始前冻结身份观察，但不能据此证明既有缓存已隔离、旧协程已退休或所有加载都经过这个入口。

本轮只读核对现有报告、原生静态调用边和已生成互操作程序集的 Cecil 元数据；没有新增或运行本页提出的只读 hooks，没有调用游戏、克隆、换根、启动游戏或读写存档。当前 [NativeGuestShadowBridge](../src/DaveCoop/Networking/NativeGuestShadowBridge.cs) 的进入及静止边界仍恒 false，不据这些签名开放权限。根桥合同见 [GUEST_SHADOW_BRIDGE](GUEST_SHADOW_BRIDGE.md)，固定加载来源观察的既有实现见 [MAP_ORIGINS](MAP_ORIGINS.md)。

## 加载前的确切声明

以下实例声明均属于 `Assembly-CSharp.dll` 生成包装器；`IEnumerator` 是 `Il2CppSystem.Collections.IEnumerator`，不是已完成的加载事件：

| 声明 | 建议观察与边界 |
| --- | --- |
| `void SceneLoader.GoToInGameEntry(string sceneID, SceneTransitionType transtionType, bool unloadActive)` | prefix 冻结本次入口及现有根/cache 身份；postfix 仅说明原入口返回 |
| `void SceneLoader.ChangeSceneAsync(string, SceneTransitionType, bool, bool, bool, bool, bool, Il2CppSystem.Action, Il2CppSystem.Action, bool, bool, bool, bool)` | 参数顺序见下文；与固定父 entry 关联，不能取代 entry 前置时机 |
| `IEnumerator SceneLoader.CoChangeSceneAsync(DR.GameScene, SceneTransitionType, bool, bool, bool, bool, bool, Il2CppSystem.Action, Il2CppSystem.Action, bool, bool, bool)` | factory postfix 固定原返回 iterator，不能当加载完成 |
| `IEnumerator InGameManager.Start()` | factory postfix 固定实例/iterator；首次 `MoveNext` prefix 可标记该实例开始执行，场景对象已存在 |
| `void SceneLoader.OnStartChangeScene(string SceneName)` / `void SceneLoader.OnEndChangeScene(string SceneName)` | 自然转换阶段观测；名称和 end 不证明旧世界或异步工作已经全部静止 |

`ChangeSceneAsync` 的十三个参数顺序为 `sceneName, sceneTranstionType, throughEmptyScene, initLoading, useStartTransition, useFinishTransition, unloadActiveScene, preLoadAction, postLoadAction, ignoreSameSceneCheck, isRetry, skipEmptySceneOption_isUnloadAssets, firstFindSceneManagerInActiveScene`。`CoChangeSceneAsync` 用 `sceneData` 取代名称，不含 `ignoreSameSceneCheck`，其余相应顺序不变。委托只记录存在性及本地身份，不调用或替换。

真实嵌套包装器为 `SceneLoader/_CoChangeSceneAsync_d__111`、`InGameManager/_Start_d__111`；各自声明 `bool MoveNext()`、`void System_IDisposable_Dispose()`，有直接 native 字段代理 `__1__state : int`、`__2__current : Il2CppSystem.Object`、`__4__this`。通用转换 iterator 另有 `sceneData`、pre/postLoadAction、各转换标志直接字段。公共 `IEnumerator.Current` getter 会调用原方法，观察应按已核实的直接字段复制，不调用它。

现有 `map-entry-owner-native-calls.json` 中，GoToInGameEntry 的唯一直接目标包括 `IngameSaveDataManager.Clear()`、`SaveSystem.UpdatePlayerData()`、`MissionManager.IsInProgressMisison(int)`（原拼写）、`GetMissionData(int)`、`SetMissionFailedV2(MissionData,Action<List<int>>)` 和 `ChangeSceneAsync(...)`。这些是同一已知 unwind family 中的静态边，不证明实际分支、数据流或完整运行顺序。由此得出的实现判断是：影子进入若只围绕下游 ChangeSceneAsync，无法保证覆盖入口自身的进度副作用。

`map-load-owner-native-calls.json` 中 Start 的 MoveNext 有 `GameBase.StartGame(...)`、`DynamicIngameNodeLoader.Init()`、任务查询、`LootBox.Load(ILootBoxEventListener)`、`RestoreFromIngame()`、`InitPlayerCharacter()`、`PlayerCharacter.StartDiving()` 和 `FishDataInit()` 直接目标。它还可加载 additive scene、实例化资源及注册回调；其主体有未解析间接调用。首次 Start 执行已偏晚，不能作为所有世界对象尚未创建的证明。

## SaveSystem 加载阶段

同一程序集的精确实例声明：

```csharp
void DR.Save.SaveSystem.Init(Il2CppSystem.Action onDone);
Il2CppSystem.Collections.IEnumerator DR.Save.SaveSystem.InitSaveSystem(Il2CppSystem.Action onDone);
void DR.Save.SaveSystem.LoadGame();
void DR.Save.SaveSystem.LoadGameOnInit();
void DR.Save.SaveSystem.LoadAllData();
void DR.Save.SaveSystem.ReloadData();
bool DR.Save.SaveSystem.LoadGameDataFromSlot(int slotIndex, DR.Save.SaveSlotType slotType);
void GameBase.LoadSavedData();
Il2CppSystem.Collections.IEnumerator GameBase.InitAfterSaveSystem();
```

`DR.Save.SaveSystem/_InitSaveSystem_d__42` 和 `GameBase/_InitAfterSaveSystem_d__45` 各有 `bool MoveNext()`、`void System_IDisposable_Dispose()` 及直接 state/current/this 字段；InitSaveSystem iterator 还直接保存 `onDone : Il2CppSystem.Action`。factory 产生 iterator 不等于加载结束；MoveNext 返回 false 或 Dispose 只界定该 iterator 的一次自然结果，不能证明委托及其他 coroutine 完成。

SaveSystem 的 `_IsInitialized_k__BackingField`、`_IsLoadFinished_k__BackingField`、`_IsGameLoaded_k__BackingField` 及四个 manager 字段都是直接 native 字段代理；公共同名状态/manager getter 会 RuntimeInvoke。可在上述自然回调中冻结三 bool、manager 与五根身份，比较本次加载前后根是否更换。flags 为 true 仍不能推出 Mission/Ingredients、Interaction、旧 saveable、pending load 或全部输出已安全。桥读取这些标志作为必要检查，保持更强进入条件为 false。

## 入海前已可能存在的缓存

元数据明确区分原存档子树与运行缓存：

| 存档与运行对象 | 已核实的直接字段 |
| --- | --- |
| `SaveData` | `m_IngredientsData : Dictionary<int,IngredientsSave>`、`m_MissionData : Dictionary<int,MissionDataSave>`、`m_MissionManagerData : SaveDataMissionManager` |
| `IngredientsStorage` | `m_Storage : Dictionary<int,IngredientsData>`、`m_IsLoaded : bool` |
| `MissionManager` | 静态 `_IsLoaded_k__BackingField : bool`、`m_MissionList : Dictionary<int,MissionData>`、处理 queue、`m_MissionProcessRoutine : CoroutineHelper`、listeners/delegates |
| `IngameSaveDataManager` | `ingameSaveDatas : Dictionary<InGameSaveType,InGameSaveData>` |
| `DR.InteriorStorage` | `m_GameDataManager : SaveSystemGameDataManager`、`m_InteriorDatas`、`m_IsLoaded` 及 disposable/bundle 缓存 |

新的离线 `guest-runtime-cache-native-calls.json` 包含十一根声明、七十八条方法记录，未触及方法配额。唯一直接静态边显示：`IngredientsStorage.Init()` 读取 `GetGameSave()`、创建 IngredientsData，并可修改 IngredientsSave 的 LastGainTime/LastGainGameTime；`IngredientsData.UpdateSaveData()` 调用 `GetGameSave()` 和 `SaveData.UpdateIngredientsSaveData(...)`。`IngredientsStorage.Load()` 没有可归属主体，未知路径不能解释为纯读取。

`MissionManager.Load()` 进入 `InitMissionList()`/`OrderInProgressList()`；InitMissionList 建 MissionData，BuildInternal 读取 GetGameSave 并触及 MissionDataSave 的 AcceptedTime/ClosedTime/FailedTime/TimeProcessDateTime setter；`MissionData.UpdateSave()` 又写 `UpdateMissionSaveData(...)`。因此不能在围栏外调用 Init/Load/Build 冒充 detached clone。entry 自身读取或处理 MissionManager，说明这些运行对象可能在资源加载之前参与进度；尚未观察具体进程中它们何时建立、是否别名原存档子对象及何时停止使用。

另外已有直接保留原根的候选字段：`PlayerCharacter/__c__DisplayClass464_0` 与 `_464_1` 的 `playerData : SavePlayerData`、`InGameManager/_InitSunangEmitterSystem_d__177._save_5__2 : SaveData`。只换 manager 的 Data 字段不会改写已创建 closure/iterator 的字段。元数据证明它们能够持有引用，不证明本次进程已创建或保存了哪个 root；只扫 typed 字段也排除不了 Object、容器和更深子树中的引用。

## 返回船与退出的精确观察候选

```csharp
void InGameManager.GoToLobby(SceneTransitionColorType sceneTransitionColorType, bool isPlayerDead);
void InGameManager.GoToLobbyInternal(SceneTransitionColorType sceneTransitionColorType);
void SceneLoader.GoToLobbyEntry(SceneTransitionType transitionType, bool initLoading,
    bool useEmptyScene, bool useStartTransition, bool useFinishTransition);
void SceneLoader.GoToJungleEntry(SceneTransitionType transitionType, SceneType forwardScene,
    bool initLoading, bool useEmptyScene, bool useStartTransition, bool useFinishTransition);
Il2CppSystem.Collections.IEnumerator LobbyPostRoutine.LobbyProcessRoutineNormal();
Il2CppSystem.Collections.IEnumerator LobbyPostRoutine.StartDiveResultProcess();
Il2CppSystem.Collections.IEnumerator LobbyPostRoutine.LobbyProcessFinishedRoutine(Il2CppSystem.Action onActionMissionProcessFinished);
void InGameManager.OnDestroy_Impl();
void DR.Save.SaveSystem.OnDestroy_Impl();
void ApplicationManager.OnApplicationQuit();
```

Lobby 的三个 iterator 分别为 `_LobbyProcessRoutineNormal_d__18`、`_StartDiveResultProcess_d__19`、`_LobbyProcessFinishedRoutine_d__20`；均有 bool MoveNext、void System_IDisposable_Dispose 及直接 state/current/this。LobbyPostRoutine 自身的 `m_Routine : CoroutineHelper` 是直接字段；CoroutineHelper 的 `_Coroutine_k__BackingField`、`m_Target`、`m_DefaultRoutine`、`OnRoutineFinished` 可观察身份/存在性，非空或为空都不证明所有原生工作已结束。

`expedition-native-calls.json` 的 GoToLobbyInternal 包含 AddCargoBoxLoot、Ingame 清理、GameInfoSave.LastEntryScene 修改和 Lobby/Jungle 转换目标。Normal/Result/Finished 的 MoveNext 还触及任务、Ingredients 入仓、多类物料、邮件、对话、scenario 与委托。返船请求、场景 end、单个 iterator false、manager Destroy、单个保存 bool 都不能作为全局静止或正常结算完成。ApplicationQuit 可以记录已观察的退出请求；崩溃、强制结束或遗漏的 callback 必须保持未知，不能补造正常返航。

## 最小只读自然观察合同

下一适配器可默认关闭，先观察 Entry prefix/postfix、SaveSystem Init factory/MoveNext、LoadGame 前后、InGame Start factory/MoveNext、Return request、Lobby 三 factory/MoveNext 和已核实的 unload/destroy/quit。metadata 注册必须检查原 declaring type、static、参数与返回；不跳过原方法、不替换原参数/结果、不调用委托或保存方法。Read-only 本身也需要 ABI 和实际线程/清理验收。

每个 callback 在已确认的 Unity 线程立即复制有界 CLR 值：RunId、单调 CallId、阶段、线程/帧、实例/iterator 的本地身份、直接 state、原 bool/异常存在性、三加载 bool、manager/five-root/cache 身份。factory 为返回的具体 iterator 固定本次 owner，每次 MoveNext 使用这个 owner；unknown/retired 遮住父 scope，不从当前 singleton 倒推来源。local native 指针仅供同步核对，不上传；队列不保留 wrapper 或延迟 native 读取。容器计数只有核实直接 backing field 才读，否则报不可用。

entry 和 load/return 混入、身份变化、线程错误、读取错误、未配对、丢事件、配额或未知结束均撤销候选，不让旧 baseline 自动复活。可采用既有观察器的 process quota、context/queue 限额、sticky failure 和只卸自己 owner 模式；本页没有新增 hooks 或实现这份合同。

这些记录最多证明“此自然边界上观察到哪些身份和阶段”，仍不是 CanEnterBoundary/HasQuiescentBoundary 的 true 证据。完成具体 Interaction detached 绑定后，还须证明缓存/旧引用归属、所有在途写入与异步工作生命周期及完整输出围栏，才能讨论真实进入和退出。加载前采用、GuestStateIsolated、世界与捕获/返航权限继续关闭。
