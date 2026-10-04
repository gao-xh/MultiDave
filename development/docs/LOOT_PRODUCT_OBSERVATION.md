# 入袋入口的基础资源观察

每人独立背包需要真实的产物、最终品质和有效重量。0.1.27-dev 在既有默认关闭的 `Observe loot and return calls` 中观察 `LootBox.Add_Impl` 原参数携带的基础资源，帮助核对原游戏计算链。它没有执行个人背包分流或生成捕获凭证。

只在该入口的 Before 回调、已确认 Unity 线程与健康读取窗口内处理原 `DR.IItemBase`。两个已核精确类是 `DR.Items` 和 `IntegratedItem`；仅读取它们的 TID、ItemDataID、ItemGrade、ItemWeight 四个直接 backing field。不调用接口或业务 getter，不用继承猜测类型，不主动查询资源表或重掷追加掉落。每次原生读取前后复核窗口，第二次字段采样不一致或读取异常沿现有故障路径撤销观察证据。

原参数 wrapper 仅存在于同步回调；Context、队列和日志只持有固定类别、状态及复制的 CLR 数字。After 和 Finalizer 复用 Before 的复制结果，明确它不是退出时重采样；Finalizer 不读取原生资源。未知精确类与 null 参数明确标不可用，不能以零值假装已知物品。

两次字段采样相同不证明原生对象静止、原子快照或 ABI 通过。读取 ClassPointerStore 可能涉及类型初始化，不能称整个观察无原生分配或零副作用。每个资源 Before 最多 32 次读取，全进程资源读取上限 65536，开关不重置预算。挂钩仍保持原参数、返回与异常；有界事件、上下文、队列、线程及未知卸载规则沿用 [CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)。

TID 与 ItemDataID 都保留为原字段候选，不猜哪个是实际捕获产品 ID。ItemGrade 是基础资源品质，ItemWeight 是基础资源重量；不能把它们加 bonusGrade、乘 count 或重量参数后直接当作最终货槽品质或个人有效重量。负数或特殊编号也不能转成 CargoProduct。完整产物、捕获归属、槽合并、容量分流、任务与鱼终态仍需原生执行桥。

`LootBoxSlot` 的 ItemID、Grade、FinalGrade、TotalCount 是 ObscuredInt。新 `Inspect-LootProductApi.ps1` 离线读取已生成程序集的精确声明与封装 IL，原报告只存 `.local/analysis`。声明和封装不能证明解码方法无业务副作用，因此本轮不解码、不写货槽。只观察新 AddLootBox 也不能覆盖现有槽合并。

`DAVECOOP_LOOT_CALL` 记录基础资源候选及当前来源链健康；最终品质、有效重量、资源到产品映射、完整产物、成员归属和原生权限保持 false。没有 CargoFacts producer、fake ledger 或新 GUI 权限入口。协议仍为 6。

本轮只编译新原生适配器并运行离线检查；Core/TCP 输入与 0.1.26 验证输入逐文件一致时复用该轮实际 222/222，不计新执行，也不把它们作为原生回调测试。新版未部署或启动，安装 0.1.12、最近潜水 0.1.11、默认包 0.1.0 的证据范围保留。完整个人容量与分流、返航逐项入仓、客机隔离、房主世界、双游戏和 GitHub 冷配置继续按 [PLAN](PLAN.md) 推进。
