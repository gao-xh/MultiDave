# 个人背包的来源租约与延后产物绑定

每人独立背包要求捕获物和容量都归同一名玩家。原游戏可能先处理主产物入袋，再选择追加掉落；上一版只接收入口前完整产物计划的账本不能直接承接这种流程。0.1.26-dev 在同一个 `ExpeditionCargoLedger` 增加来源租约与延后绑定，不提前抽随机、不先写房主袋再复制。现有已核完整产物的 `Reserve` 合同保留。

`SourceReserve` 固定潜水、成员、请求、鱼来源、角色与装备修订，铸造只能由原账本持有的租约，并分配该潜水账本统一的捕获 OperationId。两个成员可以各有 RequestId=1，但不能各自指定 OperationId=1。`HostFishActionGate` 的编号仍是各 Gate 内部编号；实际原生适配器必须映射到该租约，游戏内这条接线尚未启用。请求、编号和来源记录不会因取消、断线或换场景被清掉重新使用。

会话玩家1/2、成员GUID、Unity角色实例编号和原生pointer各有自己的用途。当前本地玩家观察的 PlayerId 是 Unity实例编号，不能直接填写 `CargoSourceFacts.BoundPlayerId`；实际桥需从已核会话角色绑定对应个人袋，并分别验证原生角色寿命。

`EnterSelection` 在进入产物选择前把原操作标为 `EnteredUnknown`。这一步必须有新鲜的 `YieldSelectionIsolationVerified`，证明执行桥已准备好暂停整批产物的首次入袋和物化。现有原生桥不能提供这项证明，因此游戏中不会执行该路径。自然观察的 Parent、Roll 返回或最后一笔 Add 都不能补出这项进入权限。

`LateSeal` 接收原操作已经选出的完整产物及真实最终品质、有效重量，要求 `CompleteSelectedYield`、`MaterializationBoundaryHeld` 和 `NoBagWriteYet`，再按对应个人袋的当前容量与修订预留重量。它仍然保留 `EnteredUnknown`，不重新进入选择、不重掷随机，也不自动确认捕获。后续仍要同一操作的真实捕获终态、房主原袋增量或员工分流凭证，才能调用 `ConfirmCapture`。

第一份身份与完整选择证明都通过的批次，在个人容量和袋修订检查之前固定。容量拒绝也保留该批次，不能换成更轻的物品、数量或品质再试；同一批次后续重新核对新鲜容量不等于重新执行选择。此时仍没有容量预约、袋增量或捕获凭证。

选产物前的来源预约没有已知货物、重量或虚构的零重量产品。快照明确保留 `Intent`，`YieldBound=false`、`Request=null`；现有协议6清单通过预约及未知计数显示其状态。`ReservedWeight=0` 只表示尚未绑定重量，不证明鱼没有产物或返航已完成。

取消只适用于还没进入选择的原租约，并要求明确的未进入证明。进入后异常、容量不足、断线或超时保留原来源屏障；不能改成 NativeNotEntered、补发或释放给另一个玩家。返航冻结停止新来源预约；已经进入但产物未绑定的操作即使没有 ReturnItems，也阻止 `CompleteReturn`。原操作后续取得有效完整产物时，需要只给原 CaptureId 补齐返航条目，不扩大成员或新增捕获。

## 真实原生阶段仍需完成

静态调用布局显示 `SuccessPickupFish` 和 `AddDropItemLootBoxWithPlus` 先走主产物、再走追加路径；`RollPlusItem` 的原返回只是追加选择候选，主袋副作用可能已发生。已检查的声明和报告没有提供一个现成的“完整产物数组已选、全部写入尚未开始”的入口。需要显式拆分选择与提交的执行桥，并逐条验证鱼、任务、图鉴、保底计数、重量和槽位副作用。

`DataManager.GetFishDropItemID`、`FishPlusItemPity.RollPlusItem` 和 `DataManager.GetItemV2` 是待验证的业务候选，不能作为纯 getter 提前调用。资源的 ItemGrade/ItemWeight 不等于槽位最终品质或个人有效重量，不能猜加法与乘法。`LootBoxSlot` 的数量与品质为 ObscuredInt，现有槽合并也可能绕过新 `AddLootBox`；只拦新槽或跳整个 `Add_Impl` 都不足以完成分流。接口与观察范围见 [CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)。

本轮另修正来源观察中的身份命名空间：`HostEntityTarget.LocalToken` 是 Unity `GetInstanceID`，可以为负数；原生 pointer 是独立键。`ResolveObservedPointer` 已按 pointer 与活动代次前后核对，复制器不再把 LocalToken 与 pointer 比较，也不增加新的业务读取。新版观察仍未部署或实机验证。

## 验证范围

纯 CLR 测试验证真实账本的来源竞争、编号、延后容量、未知结果和返航屏障；真实生产清单适配器的本机 TCP 测试验证这些状态的传输与断线保留。数据和能力事实来自合成夹具，不运行选择、入袋、图鉴、入仓或保存，也不证明实际双游戏合作。实际测试数量、构建哈希及未验证项以 [本轮摘要](../logs/capture-selection-build-verification.json) 为准。

游戏中仍没有可信潜水成员及原生产物生产器接入。实际个人容量/负重、分流、逐项入仓、客机隔离、房主世界采用、正常双端闭环及 GitHub 冷配置继续按 [PLAN](PLAN.md) 推进；用户延后试玩期间继续开发，不自动部署或启动。

0.1.27 在既有入口增加[基础资源观察](LOOT_PRODUCT_OBSERVATION.md)，资源字段仍不能代替最终品质、有效重量、完整选择或原生分流凭证。

0.1.28 另增加[受限货槽候选](LOOT_SLOT_OBSERVATION.md)，不调用原生解码或把Before的FinalGrade候选当捕获终局/入仓凭证。

0.1.29 的[货槽前后观察](LOOT_SLOT_OBSERVATION.md)补三处setter；仅自然更新候选，不提供跨调用槽身份、完整产物或个人背包增量凭证。
