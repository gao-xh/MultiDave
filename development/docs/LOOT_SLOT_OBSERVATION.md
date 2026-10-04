# 货槽字段的受限观察

每人独立背包需要真实产物和品质。0.1.28-dev 引入货槽四字段的受限候选；0.1.29-dev 另观察总数量、品质和最终品质的自然更新，补充已有槽合并时遗漏的数据。所有观察默认关闭，不生成捕获、背包增量或返航入仓凭证。

## 本地解码的严格范围

本机原函数的已知 unwind 范围显示：非零密钥分支执行 32 位 XOR；零密钥分支会取静态密钥；未初始化路径会初始化接收器，检测路径可能触发间接调用。这个证据不证明整个方法覆盖、真实 ABI 或原生解码无副作用，也不能推断调用生成的结构副本必定修改原货槽。

纯 CLR `LootSlotSnapshot.DecodeInt` 只接收复制的五个标量，仅在 `inited=true`、`currentCryptoKey!=0` 时计算候选值；fakeValueActive 时还需匹配 fakeValue。缺初始化、零密钥、缺快照或校验不符均返回不可用和 null 值。它不补初始化、不查询检测器、不取静态密钥，不仿造整个 GetDecrypted 行为。原生检测器关闭时可能接受某些不一致值，本地候选仍保守拒绝。

## 原生复制与证据边界

`SaveData.AddLootBox` 与 `IngredientsStorage.AddFromLootBox` 仍仅在 Before 复制槽字段。新增的 `LootBoxSlot.set_TotalCount`、`set_Grade`、`set_FinalGrade` 在 Before、After 分别复制槽字段；它们是同一原调用的前后观察，没有主动调用 setter。19 个目标均按精确声明注册；原来的16个方法编号保持，新增编号17–19。

每次样本先确认实际线程、健康窗口与精确 LootBoxSlot 类，读取 ItemID、Grade、FinalGrade、TotalCount 的直接结构字段两次，逐个比较五个原始标量，再复核 pointer、class 和 class store。每次样本最多32次槽读取，setter前后合计最多64次，全进程65536次且开关不重置预算。顺序样本一致不证明原子静止、槽寿命或 ABI。已有槽原生业务可能先更新重量，setter不是容量检查前的暂停点。

setter的原参数仅在 Before 同步复制五个标量并形成不可变 CLR 候选；不保留原结构或调用整数转换。Context 保留不可变 Before/After 样本、参数候选，以及用于核对同次调用的私有 pointer/class 标量；wrapper、密钥、加密值不保留，私有 pointer/class 不入队或日志。After 必须匹配固定 prefix 的 pointer/class，不能改绑新槽。

Finalizer不读取原生槽。已有 After 时复用它，即使 After 是 Unavailable，也不能回退为旧的可用 Before；没有 After 才复用 Before。事件明确记录样本源阶段和是否只复用。原两个槽边界的 After/Finalizer 继续复用 Before。

`RunId + CallId` 只标识本次槽样本，不建立持久槽编号。相同地址可能复用，相同 ItemID 可以有不同槽；不能据此关联跨调用袋身份、库存归属或计算增量。SlotLifetimeVerified 与 SlotInventoryIdentityVerified 始终 false。

未知精确类或 null 槽明确不可用；原生读取异常撤销链，无条件清空故障事件的前后样本及参数候选并撤销 prefix，即使其他错误已经锁存。队列中的既有候选仅作为历史诊断输出，并标记当前链已失健康。解码拒绝不改原游戏输入、返回、异常或货槽。

候选逐字段保留，不用零冒充未知；负数、特殊 ItemID 和品质也不能直接构造 CargoProduct。旧 `SlotDataReadable=false` 仍说明原生 ABI/完整货槽读取未证；候选可用是另一项证据。数量 setter 参数是新总数，不能当本次捕获数量；FinalGrade 字段或 setter 的出现不证明本次捕获的终局品质、全部主/追加产物完成或鱼终态成功。

静态分析中 Add_Impl 的已知新槽路径设置原 Grade 后调用 AddLootBox，没有提供完整终局品质时刻；现有槽合并也不一定走该边界。ApplyFinalGrade 的已知分支包含阈值、类别和上下界裁剪，不能无条件把 grade 与 bonus 相加。入袋重量的已知分支依赖 lift 类别和接口值；WeightParameter 在观察到的超重分支参与阈值与 debuff 参数，不能猜它是基础重量的乘数。入仓 GetExchangeCount 还会走物料转换，不能把 TotalCount 直接当最终肉量。

完整主产物与追加选择、操作/成员归属、个人容量、任务/鱼终态、有效重量和逐项入仓仍需真实执行桥。所有原生权限、捕获成功、完整产物、最终品质验证、背包增量、客机隔离和世界权限仍为 false。

## 离线工具与验证

`Inspect-NativeCalls.ps1 -IncludeInstructions` 显式开启私有指令文本；默认关闭。每方法默认2048条、全报告8192条，硬上限8192/16384；每条最多256 UTF-16 单元、全报告保留字符上限1048576。超限整条省略，文本截断不改变原解码/调用边统计或方法完整性。格式化一条字符串的初始分配不受保留字符预算保证。原指令、地址、立即数及报告只留 `.local`。

0.1.28 的实际关闭、开启、低文本额度三种模式均解码13方法/464指令/58调用边；解码与调用边逐项一致。完整文本464条，低额度只保留3条并标461条省略。重量报告18方法/1687指令，4个没有范围的 leaf 没有补猜或输出。历史证据见[0.1.28摘要](../logs/loot-slot-build-verification.json)。工具编译与报告校验不算游戏执行。

实际 Core、插件编译、离线声明核验及未验证项以[0.1.29摘要](../logs/loot-slot-mutation-build-verification.json)为准。两个既有 CLR 来源栈夹具扩展了17–19的嵌套、未知来源遮蔽、postfix保留作用域和严格finalizer顺序；它们不运行原生观察器或证明参数 ABI。0.1.28 的四组标量算法夹具继续保留。未部署/启动，安装0.1.12、最近潜水0.1.11、默认发行包0.1.0保持；完整双端、正常返航和冷配置继续按[PLAN](PLAN.md)推进。

后续原生执行按[员工选择与提交桥](FISH_YIELD_BRIDGE.md)补齐多tier/主随机、一次追加、进度及终态；槽观察不提供整批暂停或可信成员绑定。
