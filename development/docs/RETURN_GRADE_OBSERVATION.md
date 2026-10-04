# 返航品质与数量的自然调用观察

0.1.34-dev 为原默认关闭的 Observe loot and return calls 增加三个自然入口：`LootBox.ApplyFinalGrade(int)`、`LootBoxSlot.get_IsInInvenType()`、`LootBoxSlot.GetExchangeCount()`。共22个精确声明，旧编号1–19保持，新编号20–22。只在游戏自己调用时观察，不主动执行这些业务、计算最终品质或写入背包。

## 为什么必须观察原调用

已知原生范围中，ApplyFinalGrade 从 SaveSystem.GetGameSave 取得 SaveData，再通过 GetLootBox 访问真实存档集合；接收器 this 提供三个实例字段。把它调用到临时员工 LootBox 仍可能修改房主存档袋，不能作为员工转换 helper。

原最终品质的已知分支依赖捕获 rawGrade、实例 default 阈值、原槽 IsInInvenType 返回、原 additive 以及 min/max。当前没有等价的 ItemsUtils.IsInInvenType 声明；ItemExtension.IsIngredient 或 ItemType 枚举不能代替该谓词。完整方法、additive 调用来源和员工适用政策仍未证明。

本轮实际返航调用研究还区分了两个入口：自动返航枚举 IngredientStorage 类别的原槽，构造绑定当前槽的非空数量 delegate，再交 AddFromLootBox；它的 MethodInfo 目标尚未唯一解析。界面提交回调传 null，直接使用传入 CellData 的 TotalCount，但上游 Convert / OnPostProcessMapping 可能已经转换数量。不能把界面 null delegate 推成原捕获 rawCount 的 DirectCount，也不能把任何非空 delegate 推成 ExchangeWholeOnce。

## 固定同步样本

20 的 prefix 固定原 additive，确认 Unity 线程、健康窗口、原指针和精确 LootBox 类，仅读三个品质实例 direct 字段两次。`LootReturnGradeContextCandidate` 保存两份不可变 CLR 读数；缺失或不一致明确不可用，原 signed int32 保留。一致但 min 大于 max 时仍保留读数，BoundsOrdered=false，不计算品质或生成可绑定的 PolicyFingerprint。

品质专用 sampler 每个样本最多16次读取，全进程65536次，开关不重置。正常 prefix 使用12次品质字段/身份/class store读取，After另用2次身份读取；这些计数不包含旧 capture 的原实例、Time.frameCount 及四个重量/容量诊断读取。旧袋诊断仍独立采样。读数一致不证明原子静止、原生 ABI、class 初始化纯度或集合身份。

After 核对同一原接收器的 pointer/class，只复用品质 prefix 候选；Finalizer 仅读 CLR，不读取原生对象。三个品质字段来自接收器，不代表原函数实际处理的存档集合，ReceiverCollectionBound 与 EmployeePolicyApplicable 始终 false。不主动调用 GetGameSave / GetLootBox 补根，不从邻近鱼、相同 TID 或帧时间推断员工政策。

21 的原 bool 和22的原 int 按值保留，不覆盖参数、返回或原异常。两个 getter 的原槽在 Before / After 分别受限复制，After 必须匹配 prefix 的 pointer/class；Finalizer 复用 After，即使不可用也不回退为旧 Before。现槽读取 profile 只支持精确 LootBoxSlot；未支持的 UI 派生类明确不可用，不能冒充已证明的布局。

## 整袋范围与鱼来源

ApplyFinalGrade 处理整袋，在既有 LootCallLineage 中开启 source=null 的明确范围；即使它嵌套在某条鱼的捕获调用内，也遮断该鱼来源。分类、品质 setter、兑换和入仓子调用仅保留同步 ParentCallId，不把整袋处理归给单鱼。正常 Finalizer 退栈后才恢复仍活着的外层鱼范围；独立调用不借历史来源。

原异常、回调重入、错误线程、队列/读取额度耗尽和前后身份不符撤销当前证据，并清除此事件的品质候选。历史队列只作诊断，不能在 drain 后恢复政策或收益。预算、失败与 CallId 高水位跨开关保留；未知卸载不重试，只移除自己的 Harmony owner。

## 本轮验证及后续接线

新增两项夹具使用实际 Core lineage 与不可变候选，验证整袋遮断、bool/int 原返回独立、postfix/finalizer 顺序、正常退栈、原异常、旧 Run/token 和 replay。它们不运行 native callbacks、slot复制或游戏；实际执行与编译记录见[构建摘要](../logs/return-grade-observation-build-verification.json)。

离线 Cecil 核对4类、3挂钩、3实例 Int32 direct 字段及2个存档业务入口。新两组私有 PE 报告分别为96方法/6212指令和14方法/2513指令；第一组触及方法额度且有3处 invalid，第二组有2处 invalid，均不证明完整方法、委托目标或调用遗漏。指令文本无额度省略不等于指令解码完整；原报告、地址和文本仅留 .local。

下一步追踪 CellData 上游数量转换与自动返航委托 MethodInfo，并把实际返航根、ExpeditionId / ReturnId / MemberId 与员工固定捕获产品关联，才可接真实品质/数量政策和既有逐项入仓计划。原捕获 grade/count/weight/指纹、数量转换结果与 FinalGrade继续分开。可信员工 actor/捕获分流、仓库增量/保存、客机隔离、房主世界、真实双端正常返航及 GitHub 冷配置仍属于完整 M3—M7。

本版未部署或启动，观察默认关闭；安装0.1.12、最近潜水0.1.11、默认发行包0.1.0保持原实机范围。

0.1.35后续离线研究已解析自动委托及UI数量上游，详见[RETURN_COUNT_POLICY](RETURN_COUNT_POLICY.md)。上述0.1.34报告的未决范围保留其历史含义；新具体target不是运行时ABI、完整公式或员工政策。ApplyFinalGrade的additive/原存档集合和实际返航绑定仍须核实。
