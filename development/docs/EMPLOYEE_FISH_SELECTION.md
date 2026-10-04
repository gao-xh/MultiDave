# 员工的一次性产物选择

0.1.30-dev 开始实现实际 typed 选择原语及调用编排。房主的原生袋只归房主；本桥选择员工捕获批次，不调用房主袋暂存。尚未接到网络或 GUI，也不生成完整 CargoProduct、捕获凭证或员工入袋记录。

## 实际代码

`Core/Cargo/FishYieldSelection.cs` 用既有 `ExpeditionCargoLedger.EnterSelection` 仲裁同一来源租约，然后按固定配方执行：

1. 普通拾取调用一次 `FishInteractionBody.GetPickUpGrade`，固定原奖励品质。它本身可能随机选择，不能在登记已进入之前调用。
2. 按 tier1 到 MainTierCount 顺序各调用一次 `DataManager.GetFishDropItemID`。
3. 一次调用 `FishPlusItemPity.RollPlusItem(fishTid, bonusGrade + 1)`。追加产物的提交参数仍是实际 `k_NoneBonusGrade`。
4. 每个正数原结果只解析一次 `DataManager.GetItemV2` 并保留资源；-1明确表示无该项，不补选。不认识的返回值、缺资源或异常都保留未知状态。

普通配方至少选择 tier1；非正 CarvableCount 不能解释为零次。当前账本最多8项，因此选择前只支持最多7个主tier，为可能追加物保留一项；超限不截断、不进入品质/掉落/保底选择。死鱼身体采用独立的 tier1、NoneBonus 配方，不调用普通拾取的品质选择。当前网络 Gate 和显示仍不支持尸体拾取，这份配方不自动放开它们。

这条员工合作流程会先选择全部产物，再处理个人容量。它不复刻原游戏交错入袋、任务事件及随机消费的全部顺序；房主自然捕获继续原游戏路径。

## 原生绑定与资源寿命

`Networking/NativeEmployeeFishSelectionBridge.cs` 固定实际账本的 exact CargoSourceLease、鱼编号/代次、已存在的 FishInfoData、拾取体和 DataManager/Pity 实例。只为 BoundPlayerId=2 的租约准备；这项编号检查不是实际成员、存活、空间或装备证明。

每次派发检查确认的 Unity 线程、自己的活动生命周期 observer、capture使用的同一 tracker、鱼的前后代次和固定来源/参数。业务方法仅在同一 coordinator 的 Selecting 窗口执行，并在调用前固定尝试标志，显式 backend 调用也不能绕过 EnterSelection 或重复派发。首次预约会推进袋修订，进入事实须重读当前修订，不能复用预约时的样本。

每个已选条目保存顺序、原ID、tier、count=1、bonus和lift参数。相同资源ID仍保留多个条目，只对相同原生指针的强handle去重。资源wrapper、指针和handle留在桥内部，CLR快照不携带它们；最多13个显式引用/强handle尝试，另有私有资源绑定。原生wrapper内部临时分配和失败的部分分配并没有完整保留证明。

已进入后，品质、主/追加选择、资源lookup或来源检查失败，都不会再次选择，也不回滚房主RNG/保底。桥由进程级 owned map保留，Disconnect、场景切换或返航不会丢弃未知工作；最多256个未释放桥。释放需要同一账本已记录 Confirmed 或 NativeNotEntered，句柄free未知不再重试。没有超时清空或重连清空未知记录。

当前只准备已经存在的 FishInfoData/body/provider，不调用 GetFishData 或 provider getter补初始化。FishInfoData.TID与FishDataTID一致也是本版的保守配置要求，尚未证明所有游戏配置都满足。类/字段ABI、强handle实际运行与所有支持鱼型仍需实机验证。

## 尚未提交到背包

RawPlanHeld仅表示原选择及资源保留阶段完成，账本仍为 EnteredUnknown。快照明确 FinalProductsVerified=false、CaptureConfirmed=false，不等于容量接受、捕获终态、任务奖励或入袋。原选择会消耗随机；Plus也可能写房主保底/dirty进度。

已知 Add_Impl 路径的原槽品质为基础品质加奖励品质，入袋重量用单精度计算；后续 ApplyFinalGrade再按类别、additive和上下界改变返航品质。基础字段不能代替最终品质，WeightParameter也不能猜作重量乘数。本桥不调用房主袋 ApplyFinalGrade/RefreshWeight，也不以 raw ID/品质伪造 CargoProduct。

下一步须建立房主维护的真实员工身份、空间/存活/装备事实；确定完整产品映射、个人重量和品质政策后，接同一租约 LateSeal。容量拒绝保持原批次及资源，只重核容量；随后还需单次员工分流、共享进度、鱼终态和真实 receipt 才能 ConfirmCapture。员工逐项返航入仓、客机隔离、房主世界采用、武器/生存状态、双端正常闭环与 GitHub 冷配置仍属于完整目标。

## 验证

新增夹具运行实际生产 coordinator 与既有 ledger，通过 synthetic backend检查派发顺序、重复租约、部分异常、来源失效、哨兵、线程/重入以及容量拒绝后不重选；它们不调用上述游戏方法或运行 GC/native callback。

精确接口另经离线元数据核对；原 PE 报告与地址/指令只保存在 `.local`。实际 Core/TCP、编译和研究计数以 [构建摘要](../logs/employee-fish-selection-build-verification.json) 为准。当前没有部署或启动0.1.30，安装0.1.12、最近潜水0.1.11、默认发行包0.1.0保持原验证范围。
