# 员工捕获物的返航映射与数量缓存

0.1.33-dev 在已冻结的员工捕获产品上增加返航资源映射和一次数量转换原语。房主保留自己的原生背包，员工使用房主持有的独立会话袋，各自容量和重量；本改动没有把两个袋合并。当前没有 GUI/network 业务入口，实际捕获分流、最终品质政策、入仓增量和保存仍须接通。

## 固定输入，保留原捕获

`FishYieldSelection.MapReturnOnce` 只在同一来源租约已经进入、原选择与产品归一化完整且仍未确认捕获时开始。每个正产物固定 ProductIndex、原 DropOrdinal、选中查询编号、实际产品 TID、捕获 rawGrade/rawCount、原 ItemType、原产品指纹和数量模式。无产物的 -1 槽不占 ProductIndex 或模式；重复产品 TID 仍是两个独立条目。

模式数组先复制。长度不符、未定义模式、缺少 backend、未归一化、错误线程及重入在业务尝试前拒绝，不消耗一次映射机会。目前窄 profile 只支持原鱼产物 count=1。数量模式必须由未来已验证的原调用分类 producer 提供，不能仅看 ItemType 就猜原 AddFromLootBox 的 count delegate 是否为空。

进入后顺序保留物品、读取原物品 TID / ItemDataID / ItemRank / ItemType、保留食材并读取继承 TID。每一步前后同时核对同一 ledger 的 EnteredUnknown 来源和原生来源；每个已返回标量先存入 partial sample，再做后守卫。途中异常永久保持 EnteredUnknown，不重新查找、兑换或发布半批完整结果。整批完成才原子发布 MappingReady。

原产品 TID 必须等于已经冻结的捕获 TID，ItemType 必须匹配归一化结果；选中 GetItemV2 查询编号无需等于产品 TID。映射结果的 parentID 是 Items.TID，ingredientID 是 IngredientsEntity 继承的 Ingredients.TID，rank 是 Items.ItemRank。IngredientsEntity.ItemsTID 不能代替 parentID。Native 另使用保守精确查询 profile：继承 TID 必须等于查询 ItemDataID，否则保留观察值并撤销完整映射。

## 数量只转换一次

DirectCount 使用冻结的原 count，不调用数量辅助函数。ExchangeWholeOnce 使用实际 `ItemsUtils.ExchangeCountFromWholeItems(productTid,rawCount,rawGrade)`，尝试在调用前登记，缓存原返回值。count 必须为正且不超过现有返航计划 schema 的一百万；零、负数、超限或异常不能改为直接数量，也不能重新计算。

本轮只读 PE 范围证据显示辅助函数内部再次 GetItems，使用其公式和 rawGrade 计算一个单位，再乘 itemCount。它没有证明内部资源与先前映射是同一个对象、公式配置保持不变、GameFormulaManager 完整行为或一般 int32 乘法不溢出。当前 count=1 窄 profile 不等于完整公式验证。最终入仓品质与兑换数量分开；数量输入不替换为 FinalGrade，也不改变原捕获的品质、数量、重量及指纹。

完成结果是不可变标量，Samples / Results 数组分别复制给调用者。容量拒绝后可以用同一批捕获重新核对容量，不重新映射或兑换。`CreateMappedReturnPlan` 在 creator thread、空闲 MappingReady 时只组合固定映射/数量与调用者仍待证明的政策指纹、FinalGrade、Place；不 Bind、不确认捕获或入仓，也不提供 NativePermission。

## 原生资源保留和释放

`NativeEmployeeFishReturnMapping` 是现选择桥的 typed partial，新增独立返航 owner，最多17个显式强引用/handle尝试：一个已存在的 DataManager，八个正产物各一个 Items 和 IngredientsEntity。按原指针去重 handle，但不合并产品请求。未知查找、handle 获取、业务返回或 free 继续保留，最多256个未释放来源 owner，不因 Disconnect 或场景退出重建。

每个 native 步骤只认 Core 当前不可变 request 的精确对象及 Mapping 窗口；先登记步骤再读取来源、class store 或执行 lookup。GetItems / GetIngredients 每个映射条目各调用一次，数量辅助函数的内部再次查找另算；direct backing 的重复稳定性审计不冒充新的业务 getter。class 初始化、lookup、Interop 和完整依赖副作用仍未证明为纯读取。

整个公共 Map 调用的独立 in-flight 状态阻止同步回调提前释放原13个选择引用。确认捕获后释放来源还要求 MappingReady；unknown 映射不能清除所需资源。成功释放来源会清鱼、info、body、DataManager、pity 字段；返航 owner 独立持有必要资源，缓存结果不再要求活鱼。

返航 owner 还保留原 bridge、选择结果和同一 ledger 的托管上下文；原13个引用释放或 controller 丢弃之后，不能只剩 native handles 而丢失结算依据。返航17个引用只在同一 ledger、同一捕获的全部正产品拥有匹配原产品指纹、固定 ingredientID / parentID / rank / count 的计划且全部 SaveConfirmed 后释放；释放前不得仍在 Map 调用中。最终品质及政策由计划自身和真实 producer 另证，不由映射猜。未知 free 不重试。CLR 夹具不执行这些 native handles 或释放。

## 验证边界与下一步

新增夹具运行实际选择、归一化、映射和账本，使用 synthetic 资源返回覆盖顺序、稀疏槽、模式复制、部分失败、前后守卫、同步确认、线程/重入、固定结果及候选计划边界。实际执行数、时间、输入 SHA256 和插件编译见 [构建摘要](../logs/employee-return-mapping-build-verification.json)。离线 API / PE 原报告及指令只留本机 .local；公开记录只有自写源码、文字、计数、时间和 hash。

接下来需固定实际类别/数量模式来源及 FinalGrade 政策，再接真实员工 actor、捕鱼分流、仓库 bucket 增量和保存。客机隔离、房主世界采用、实际双实例正常返航及 GitHub 冷配置继续属于完整 M3—M7。本版未部署或启动；安装0.1.12、最近潜水0.1.11、默认发行包0.1.0的实机范围保持。

0.1.34研究确认数量模式需更强来源：UI null只用已处理CellData.TotalCount，原capture是否已转换尚unknown；自动非nulldelegate的target未唯一解析。先看[自然规则观察](RETURN_GRADE_OBSERVATION.md)，.33 mapping保持候选/权限false，不据delegate presence补Exchange或把UI null补raw Direct。后续固定真实policy/绑定后沿既有不变原产品与一次缓存接plan。
