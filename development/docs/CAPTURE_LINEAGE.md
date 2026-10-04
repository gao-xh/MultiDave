# 捕获产物与容量调用的来源观察

每人独立袋需要同时关联玩家操作、原生鱼、已选产物、个人容量检查及实际入袋。只拦截 `LootBox.Add` 会遗漏追加掉落和水下进度；把房主袋里的物品再复制给员工也会污染房主负重及进度。玩法合同见 [CREW_MODE](CREW_MODE.md)。

0.1.25-dev 扩展已有默认关闭的 `Observe loot and return calls (read-only)`。本轮只观察自然发生的调用：不主动捕鱼、不调用随机选择、业务 getter、伤害、入袋或保存，不改原参数、返回值和异常。新入口、原生挂钩 ABI、自然调用顺序和显示仍未部署或实机验证。它不创建可玩的员工，也不给账本传输填入虚构的货物。

## 已核对的边界

`scripts/Inspect-CaptureLineageApi.ps1` 通过 Cecil 离线核对 7 个类型、16 个精确声明、5 个直接字段代理及互动 owner 的继承关系。原报告仅存 `.local/analysis/capture-lineage-api.json`；生成包装器声明和字段 getter IL 不等于原游戏逻辑或运行 ABI 已验证。

| 入口 | 观察用途 |
| --- | --- |
| FishInteractionBody.SuccessInteract / CheckAvailableInteraction | 原互动和容量候选边界；actor 参数只记录存在，不签发成员身份 |
| FishAISystem.SuccessPickupFish | 本次自然拾取调用的固定鱼源候选 |
| AddDropItemLootBoxWithPlus / AddDropItem_Impl / AddDropPlusItem_Impl | 主产物与追加产物的自然调用范围 |
| LootBox.Add / AddIgnoreOverloaded | 原 id、count、bonusGrade、liftType、任务参数及原 bool |
| LootBox.Add_Impl | 实际 itemData 参数存在及数量；不调用接口 getter 解读物料 |
| LootBox.CheckOverloadedState / RefreshOverweight | 原检查结果、负重参数和袋字段采样候选 |
| FishPlusItemPity.RollPlusItem | 本来就发生的随机选择及原 int；不提前调用或重掷 |
| SaveData.AddLootingSaveData / AddLootBox | 水下进度及新槽写入；槽仍只观察存在，key 只存有界哈希和长度 |
| SaveDataCaughtFishRouter.AddCaughtFish | 原图鉴参数；不是独立奖励凭证 |
| IngredientsStorage.AddFromLootBox | 返航调用范围；不调用转换委托或解释加密槽字段 |

新原 PE 报告使用 16 个精确选择器、18 个重载根，得到 145 条方法记录，其中 54 条无可用 unwind 范围；无方法配额触顶、根遗漏或指令截断。唯一直接目标支持拾取→普通产物与追加产物→袋 Add 或 Ignore→Add_Impl，追加路径还走保底随机计数，Add_Impl 有任务、成就、解锁和货槽写入。静态直接目标、共享别名、部分或缺失范围不能证明可达分支、数据流、运行顺序或捕获完成。报告只留 `.local/analysis/capture-bag-diversion-critical-calls.json`，复现和范围见 [NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。

关键路径未发现足以绑定捕获归属的协程工厂。共享地址列出的迭代器别名不作捕获协程证据；脱离当前同步范围的异步回调保持未绑定，不借最近一次鱼或 singleton 补来源。

## 调用归属的含义

每次观察有固定 RunId、CallId、ParentCallId、SourceRootCallId 和深度。prefix 在已确认 Unity 线程即时冻结鱼源候选，再开始原方法；postfix 记录原结果，void finalizer 严格结束观察 scope，保留原异常。正常返回、bool 为 true 或 int 非零都不代表捕获成功。

postfix 不退出 scope；正常无异常的重复 finalizer 不在 Core 重发记录。回调冻结期间的重入立即停止证据链，原方法内正常嵌套仍可观察。`HealthyAtCapture` 仅描述排队时状态；消费日志另带 `CurrentLineageHealthy` 与 `CurrentLineageIntegrityLost`，此前排队的记录不能在失效后继续充当健康链。

同一完整鱼源候选的嵌套普通与追加产物可以保留原根；不同鱼或新代次建立自己的根。鱼入口无法解析来源时遮住父候选，不从外层鱼继承。prefix 后保留原候选值，不在 Drain 或对象池复用后重新按指针猜归属。鱼 ordinal 仅在本地观察 run 有意义，不是原生寿命或跨机器标识证明。原生包装器只在同步回调内读取，不排入 CLR 队列或网络。

Parent 是被观察方法之间的同步包含关系，不是原生直接 caller 证明。任务、响应式通知或其他重入逻辑可能在范围内产生无关 Add；因此这批参数只是候选产物，不能自动声明属于当前鱼或员工。员工 MemberId、RPC OperationId、装备和独立 actor 仍需原生执行桥明确绑定。

`SourceOperationBound`、完整产物、最终品质、实际袋增量、捕获终态、入仓增量及原生代次验证均保持 false。该观察不会构造已验证的 `CargoCaptureFacts`，也不会自动确认账本中的捕获或移除未知屏障。

观察有队列、调用上下文、深度、全进程事件及 key 哈希预算。错线程、缺失或冲突配对、错序 finalizer、原异常、丢失或配额不足会锁存证据缺口；关闭、断线和不确定的卸钩不能成为正常闭环凭证。自己的卸钩尝试及结果单独记录，不因反复 Stop 重试未知的清理。

具体上限是：Core 与复制队列各 512，待配对上下文 128、同步深度 32、每次 Update 消费 16、鱼 ordinal 256；Core run 与 Hook 全进程各 8192 条事件。key 最多 512 个 UTF-16 代码单元，全进程最多处理 65536 单元；状态与生命周期日志各限 128 次。切换观察不重置全进程预算或故障。只有已确认自己挂钩卸载成功，下一次开启才创建新 Hook、复制器及 Core Run。

## 下一生产桥必须解决的时机问题

现有 `ExpeditionCargoLedger.Reserve` 接受入口前已核实的完整产物计划。原游戏的追加随机物却在进入捕获后才选择，并且选择本身可能改保底计数。不能为了填账本提前调用一次随机函数，再运行原捕获；不能把事后 Add 参数冒充入口前的完整计划。

后续桥需要独立的操作/来源租约，以及原游戏已经选择产物后的受控阶段：保持同一 OperationId，不重派发；覆盖整批主产物和追加物、最终品质与重量，并在对应个人容量检查和写入边界分流。房主本地操作与员工操作需要共享潜水级编号和竞争规则，不能把两个 Gate 各自从 1 开始的编号直接作为账本 OperationId。

只跳过 Add_Impl 会丢掉其中的重量、任务、成就、解锁或货槽副作用；只拦新 AddLootBox 也覆盖不了已有槽更新。完整分流须逐条决定已验证的原生副作用归属，不能伪造原 bool 成功或使用 IgnoreOverloaded 替代员工容量判断。

## 验证

运行 `Inspect-CaptureLineageApi.ps1` 复现精确声明检查；`Test-Core.ps1` 验证生产 CLR 调用栈、固定来源、遮挡、配对、异常、丢失和限额；`Build-Plugin.ps1` 编译真实观察适配器。实际数量、哈希和原生未验证项见 [capture-lineage-build-verification.json](../logs/capture-lineage-build-verification.json)。

用户暂不方便试玩时继续实现桥及隔离；不自动部署、启动或重复催测。后续实机核对 LOOT_HOOKS_READY、LOOT_CALL、调用来源记录、LOOT_OBSERVER_STATE 及自己的卸钩，验证自然拾取、追加物和容量检查，再核对每人独立负重及正常返航。本机 CLR 测试不替代真实双游戏捕鱼或返航验收。
