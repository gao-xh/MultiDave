# 员工逐项返航计划与原生入仓原语

0.1.32-dev 为既有个人背包账本增加不可变员工返航计划，并实现一次入仓调用的 coordinator 和 typed native helper。它们没有 GUI/network 生产调用，尚未执行实际品质转换、员工入仓或保存。

## 保持捕获与返航的关联

`CargoEmployeeReturnPlan` 固定 ExpeditionId、ReturnId、MemberId、CaptureId、ProductIndex、捕获产品指纹、转换政策指纹，以及 ingredientID、parentID、rank、FinalGrade、StorageCount、Place 六个入仓参数。它只承接真实 producer 已证明的转换结果，不自行推导品质或肉量，也不把 raw 产品编号直接当 ingredientID。

该计划使用独立指纹；捕获 CargoProduct 的原品质、数量、重量及指纹保持不变。即使入仓数量与原数量不同、FinalGrade 与捕获品质不同，也不能覆盖捕获记录。两种指纹沿用既有 CanonicalHash 的小写十六进制编码。

`BindEmployeeReturnPlan` 将计划绑定到原 ReturnItem；首次仅允许 Returning、已确认捕获、员工个人袋、Unclaimed，并核对新鲜房主事实、产品/计划指纹和 ReturnConversionVerified。同计划重复返回 Duplicate；已绑定的政策或任一输出变化为 Conflict，进入原生、断线、Abort 或保存后也不能换计划。同计划重复不推进阶段。

员工 LeaseMaterialization、EnterMaterialization、ObserveEmployeeStorage、ConfirmStorageSave 都核对固定计划指纹及原捕获产品指纹。没有计划不能继续员工入仓。房主原生袋仍按游戏自然链观察，不需要员工计划，不新增房主 Add。当前协议6清单只传原捕获产品及返航计数；新计划是房主本地审计资料，没有新增返航 wire。

## 一次入仓调用

`CargoReturnMaterializer` 使用同一账本的 EnterMaterialization 仲裁。只有本账本绑定的 exact 计划对象、当前 Leased 条目与新鲜入仓事实可进入。进入后先标 EnteredUnknown，再执行目标守卫与一次 Add；竞争 coordinator、重入及错误线程不能重复派发。

原 Add 返回后先保留 OriginalCallReturned，再核对目标；前守卫、Add 或后守卫异常均保留未知，不能重派发。void 返回不等于库存增加，更不等于保存完成。只有同计划的实际仓库增量与保存证据才能推进既有 StorageObserved、SaveConfirmed 和 CompleteReturn。

`NativeEmployeeStorageBridge` 只消费已冻结的六参计划，调用实际 `IngredientsStorage.Add(ingredientID,parentID,rank,grade,count,place)`，不造员工 LootBoxSlot、不执行房主 ApplyFinalGrade。原生准备仅支持 Main=0、Branch=1，枚举中的 Max、Unknown 也拒绝。计划的标量有效范围检查不是游戏材料分类证明。

桥仅为已绑定、已 Leased 的员工条目准备，固定已经存在的 IngredientsStorage、其 dictionary、SaveSystem、游戏保存 manager 与 SaveData，最多5个显式强引用/handle尝试。每次前后核对自己的 owner、handles、精确 storage 类、dictionary/loaded 状态、SaveSystem→manager→SaveData 身份与 Unity 对象寿命。没有活鱼要求，因为已确认捕获的鱼可能已经回收。

实际 Add 仅在同一 coordinator 的精确 Add 调用窗口可调用，前后守卫回调不能提前执行 Add；尝试在原调用前标记。owner 最多现有捕获限额乘产物限额，保留 partial/unknown 引用；断线或场景退出不清未知。仅同账本、同计划 SaveConfirmed 可释放，且整个 Dispatch 退栈前不能释放，即使同步回调已经推进保存阶段。成功 free 后清 wrapper 字段，free 未知不重试。这些只是编译后的保留原语，原生 ABI、内部/partial 分配和完整依赖隔离仍未验证。

## 仍需接入的转换与生产证据

原 AddFromLootBox 按槽产品 TID 查 Items，再按 Items.ItemDataID 查 IngredientsEntity；ingredientID 取原料继承 TID，parentID 取 Items.TID，rank 取 Items.ItemRank，普通海洋 place 为 Main。IngredientsEntity.ItemsTID 不能猜作 Add 的 parentID；选中 GetItemV2 资源与返航 GetItems 资源是否等价仍需证明。

`ItemsUtils.ExchangeCountFromWholeItems(wholeItemID,itemCount,grade)` 是实际数量转换入口，其 grade 来自捕获原品质；FinalGrade 是入仓品质。两种转换分开，一次转换结果应在原生 getter/调用之前登记尝试，异常后保留而不重算。本版尚未接通该 producer，也没有声明输出计划已覆盖全部材料类别。

ItemType 枚举值不能代替未证明的 IsInInvenType 谓词，原 ApplyFinalGrade 的实际 additive/调用时机及类别政策仍待核对。真实 host producer 必须先固定这些事实与政策，再提供 ReturnConversionVerified 和既有入仓 capability；helper 不把开关、房间身份、CLR 夹具标志或已返回的 Add 补成权限。

下一步在释放选择资源前冻结/转移所需映射资料，接一次品质/数量转换、实际仓库桶增量及保存确认。房主世界采用、客机隔离、真实员工身份/武器/生存/捕鱼分流、双实例正常返航和 GitHub 冷配置仍属于完整 M3—M7。

## 验证

夹具运行实际账本、计划和 coordinator，使用 synthetic facts/backend 验证固定计划、捕获记录不变、所有员工阶段匹配、竞争派发、异常保留及 void 返回边界。离线 Cecil 核对 typed 声明和 direct 字段代理，不运行原生 getter 或游戏业务。实际测试数、编译和元数据计数见 [构建摘要](../logs/employee-return-plan-build-verification.json)。

本版未部署或启动，安装0.1.12、最近潜水0.1.11、默认发行包0.1.0保持原验证范围。

0.1.33另接[返航映射与数量缓存原语](EMPLOYEE_RETURN_MAPPING.md)：计划的ingredient/parent/rank/count可从固定结果纯CLR组成，不重算、不Bind。原rawGrade保持，FinalGrade/Place/policy及模式producer仍须真实证明；本页0.1.32构建摘要保持历史。

0.1.34另补[自然返航规则观察](RETURN_GRADE_OBSERVATION.md)，固定原additive和3receiverfield、原槽谓词/数量返回供后续policy生产。原Apply集合是host save，禁temp员工bag；UI/自动数量来源分开、上游Convert/精确delegate/employee适用仍未证，不能仅这些候选Bind或入仓。
