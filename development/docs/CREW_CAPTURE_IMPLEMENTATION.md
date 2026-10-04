# 员工捕获的实际提交入口

本页基于已验证的 0.1.42-dev 源码及既有离线报告，为下一轮实际捕获桥列出调用点。
这里只增加实现说明，没有执行原生方法、启动游戏或新增测试结果。
323 项 CLR/TCP 测试及插件构建不能证明员工命中、捕获、个人入袋或返航已可玩。

员工使用房主持有的独立会话袋，各自容量、重量及负重；房主自己的原生 LootBox 保持原路径。
员工产物不能先写房主袋再复制。图鉴、任务、仓库和长期保存归房主，员工个人存档不接收收益。

## 已有代码能直接复用的部分

实际网络链为 `NetworkController` → `FishActionController.Update` → `HostFishActionGate`。
当前只发送和消费 ProbeTarget；`ReadFacts` 只取得当前鱼身份，效果计划会被结束为未启动。
远端角色、鱼叉头画面和兴趣坐标均不提供可信员工角色、装备或命中。

实际产物链已经写好，但没有网络或潜水生命周期调用方：

1. 同一 `ExpeditionCargoLedger.SourceReserve` 固定成员、请求、鱼代次及全潜水操作身份。
2. `NativeEmployeeFishSelectionBridge.TryPrepare` 固定原鱼、数据、交互体和强资源引用。
3. `FishYieldSelection.SelectOnce` 通过同一账本的 `EnterSelection`，先记未知进入，再一次选择品质、全部主 tier 和追加物。
4. `NormalizeOnce` 一次冻结资源 TID、捕获品质和 float 来源重量；`TrySealCaptureProducts` 复用缓存按员工当前个人袋核容量。
5. `ConfirmCapture`、固定员工返航计划、`CargoReturnMaterializer` 与 `NativeEmployeeStorageBridge` 已有相应状态和执行原语。

`CargoInventoryController.AttachHostLedgerEvidence` 在生产代码没有调用方。
真实潜水控制器应创建并持有一份账本，固定当前 Room 的成员 1/2 到账本 MemberId 的映射，然后接现有投影。
Wire 成员编号、MemberId、Unity instance ID、原生指针和 Gate 的局部 OperationId 不能相互替代。
无需再建立另一套来源租约或镜像 Gate。

## 高层方法不是无背包副作用的终局入口

| 实际方法 | 既有静态证据 | 员工桥的处理 |
| --- | --- | --- |
| `FishInteractionBody.SuccessInteract(BaseCharacter)` | 已知路径转发 UnityEvent，没有证明把参数中的角色传给固定拾取订阅者 | 不借此建立员工归属，不调用它来间接完成合作捕获 |
| `FishAISystem.OnSuccessPickUp()` | 取得交互体，调用 `GetPickUpGrade` 后进入 `SuccessPickupFish` | 会再选品质并进入房主袋路径，不能在已固定员工批次后调用 |
| `FishAISystem.SuccessPickupFish(int,bool)` | 多 tier 主产物及一次追加物均走 AddDrop；已知范围还有未解析的间接尾分支 | 不调用后再拦袋，也不假定尾分支就是某个已知终局方法 |
| `FishAISystem.LootDeadFishBody()` | tier1 主产物、追加物及未解析的间接尾分支 | 死鱼配方、当前网络目标检查和显示另有缺口，不能直接放松 Dead 条件 |
| `FishAISystem.WinFromProjectileinFight()` | 处理 joint、rigidbody、停止状态、game mode 和 buff，包含间接调用 | 属于原投射物/QTE路径，不作为独立员工拾取的通用终局 |
| `FishAISystem.DestroySelf()` | 已知范围释放 joint 和指标，再停用原 GameObject | 是可进一步落实的退场候选，不是捕获或进度凭证 |

`DestroySelf` 是 virtual。现有报告没有证明所有派生实现、OnDisable 回调、对象池返回及完整方法范围。
它的已知直接边没有 LootBox 或进度写入，不能由此证明整个执行没有这些副作用。
调用正常返回和 GameObject 不再活动，只能证明本次退场事实，不能单独证明产物、任务和图鉴已提交。
员工入口还须排除正在由房主原生 joint、投射物或交互流程占有的鱼，不能借房主玩家或鱼叉完成终局。

## 房主进度需要显式拆出并核对

既有声明和静态边提供以下执行候选，参数必须来自同一固定鱼和已选批次：

| 调用候选 | 已知用途和证据限制 |
| --- | --- |
| `MissionManager.UpdateMissionIntCondition(DR.IItemBase,int,int)` | 原 `LootBox.Add_Impl` 有此直接边。应固定实际资源、捕获槽品质和数量；不能根据枚举名称替代所有任务分支 |
| `SaveDataCaughtFishRouter.AddCaughtFish(DR.FishInfoData,int,bool)` | 入口不要求 LootBox 或槽参数，仍含路由及接口分派；普通员工路径的调用位置、品质、次数和无袋副作用仍需核对。原无人机路径的调用不能直接套用普通拾取 |
| `SaveData.AddLootingSaveData(int,bool)` | 原主/追加产物路径调用。对应原选择 ID 的语义和 `isNew` 参数须与原分支核对，不能拿食材 ID 或鱼 TID 替代 |

原 `LootBox.Add_Impl` 另有 delivery HUD、unlock 和 achievement 相关直接边。
类型接口、虚调用、条件分支及原回调尚未完整解析，不能把上表当作完整进度清单。
击杀成就与拾取进度也是不同阶段；不能捕获后再统一补一次 Kill。
这些调用可以写房主长期进度，部分还可能依赖房主原袋的当前内容或触发其他事件，不能当纯函数。
员工任务贡献的规则、主/追加物计数和重复防护应与同一操作固定，不绕回原 Add_Impl 来“补齐进度”。

## 下一轮最短生产接线

推荐增加实际 `HostEmployeeCaptureController` 和窄 `NativeEmployeeCaptureCommitBridge`，
它们连接现有账本与原生调用，不复制账本的准入、租约或去重逻辑。

1. 自然潜水边界创建固定 Expedition 和两成员账本；同 Room 的实际接收请求绑定员工 MemberId。
   房主维护员工位置、碰撞、存活、批准装备、弹药和修订，客户端显示位置不直接成为可信射击起点。
2. 一种已核武器由房主创建或裁定实际投射物及命中；固定目标 entity/generation 和员工操作。
   房主自然捕获与员工操作必须在同一来源租约上竞争，不能只锁网络请求而让本地拾取绕过。
   缺来源归属时不将普通 UnityEvent、布尔结果或同步包含自动变成员证明。
3. 由真实受控选择窗口进入既有租约，再一次选择和归一化全部产物。
   当前调用自身没有 Addhost，不等于已证明任意原生回调不会写袋；受控分支仍须审计和实际验收。
   容量不足保留原批次和资源，仅重核个人容量或明确策略，不重选品质、主物或追加物。
4. 核准批次后运行一次明确的员工提交路径。每个原生步骤在调用前登记已尝试，
   同一线程、鱼代次、房主保存根和资源在调用前后复核；未知回调、异常或失源保留整个操作。
   不用跳过原 Add 后返回 true 的办法，不靠 SetActive(false) 假造完整捕获成功。
5. 按已核顺序处理有限的房主进度和原鱼退场，取得固定批次、员工分流、无房主袋写入及真实终局证明，
   再调用既有 `ConfirmCapture`。部分进度已写但后续失败时仍为未知，不能退款、补奖或重派先前步骤。
   已确认员工库存才发布到现有 Cargo 通道；个人重量/超重对本人生效，不能借房主 LootBox 的负重计算。
6. 在捕获确认前按既有 API 要求保留返航所需资源/映射上下文。
   `MapReturnOnce` 目前要求同一租约仍为 EnteredUnknown；不能先 Confirm/free，再假设可重新查询原鱼。
   映射、Direct/Exchange 模式与 FinalGrade 政策须分别固定；只有已验证的返航政策才能绑定员工计划。
   正常返航逐项调用现有 materializer，读取实际入仓增量并等待保存确认。房主原袋仍走自然结算，不补 Add。

首个 profile 应按真实可证明的普通鱼类型、配方和终局限制开放。
Boss、特殊鱼、多人争抢、原生 QTE、网、无人机、采集物和跨层都不能由普通方法名推定已支持。
该限制是逐条接线顺序，完整 M3—M7、每人独立袋/容量/负重、Guest 持久隔离、真实双端返航和 GitHub 冷配置目标保持。

## 下一轮需要验证的实际行为

纯 CLR 继续使用真实账本验证竞争、已进入未知、原批次容量重核和返航屏障。
原生需要确认固定普通鱼的退场实现与回调、前置容量拒绝路径、进度的真实参数/顺序、
主/追加物各只提交一次、房主袋重量/槽位不变、员工负重、实际入仓保存，以及本地房主同时捕获的竞争。
任何部分未知都不得因请求、void 返回、鱼消失、画面标签或 CLR 夹具通过而升级为成功。

离线依据来自自写工具生成的私有选择入口、主/追加写袋、保底和进度报告；
已知范围不是完整方法或运行证明。原指令、地址、游戏/interop DLL、存档与机器日志不进入本页或发布包。
