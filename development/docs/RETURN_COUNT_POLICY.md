# 返航数量来源

本轮离线研究解析了自动返航的具体数量委托，以及鱼类界面提交前的数量转换。它补充 0.1.34 的[自然调用观察](RETURN_GRADE_OBSERVATION.md)，不开放员工捕鱼或入仓权限。房主和员工仍各有独立容量、重量和捕获记录。

## 自动返航

已知 `StartDiveResultProcess.MoveNext` 路径把当前原槽（IngredientStorage 枚举项）绑定为 `System.Func<int>` 的接收器，再将委托交给 `IngredientsStorage.AddFromLootBox`。原 PE 的两个 encoded metadata usage 分别唯一指向 `LootBoxSlot.GetExchangeCount()` 与 `System.Func<int>`；这次具体目标解析不依赖共享构造器的别名。

已知 `GetExchangeCount` 范围取槽的 ItemID、TotalCount 和 raw Grade，交给 `ItemsUtils.ExchangeCountFromWholeItems`。数量计算使用返航当时的槽数量和原品质；`AddFromLootBox` 入仓时另取 FinalGrade。不能用 FinalGrade 替换数量计算的 Grade，也不能把原槽 TotalCount 无条件替换成捕获时的一条 rawCount。

新解析实际检查两个 metadata usage 和四个原槽字段；复用此前 48 指令的 GetExchangeCount 报告与原调用范围，没有把复用算成新反汇编。具体静态目标和参数来源不证明运行时委托调用、ABI、完整函数、内部公式配置或一般整数溢出行为。

## 界面提交

鱼类界面把原槽转换为 `LootBoxFishCellData` 后，在 OnPostProcessMapping 中调用该 UI 副本的 GetExchangeCount，并把结果写回 TotalCount。鱼列表存在代表项时，按原 ItemID 与 AutoLiftedType 分组，累加后续原槽的 GetExchangeCount；该已知分组条件不比较 Grade 或 FinalGrade。

因此鱼类提交回调的 null count delegate 使用的是已经转换、可能累计的 UI 数量。再次兑换 UI TotalCount 会重复转换；直接提交捕获 rawCount 也会漏掉肉量转换。员工的固定原产物和返航转换结果须分别保存，不把 UI 合并记录当成某一条鱼的捕获回执。

普通 CellData 的已知后处理没有相同的兑换调用。鱼卵列表按 ID 合并数量，并按原 FinalGrade 累计 CountPerGrade，后续走鱼场 AddRoe 流程；不能按普通食材入仓处理。转换链包含 SetOldLooting 进度写入；普通 CellData 的鱼卵早退分支须单独区分，不能主动调用 Convert 来充当纯数量辅助函数。

这组新 PE 报告实际包含16个方法记录、1298条指令；无方法额度触顶、根遗漏、非法指令或指令文本省略。一个 leaf 方法没有已知范围，闭型 AutoMappableObject.Mapping 的全部字段复制、虚调用目标和类别判定仍未完整解析。这些静态片段不是完整方法或实际界面验收。

## 接入员工结算

现有[返航映射原语](EMPLOYEE_RETURN_MAPPING.md)保持候选状态。未来 producer 必须在真实返航上下文中固定 ExpeditionId、ReturnId、MemberId、原产物指纹、类别、转换输入和政策。提前持有资源及缓存映射可以保留身份；提前选择 DirectCount 或 ExchangeWholeOnce 仍不能代替实际员工政策。

一次数量转换、最终入仓品质、仓库增量和保存应分别确认。房主原袋沿原游戏链处理，不再补 Add；员工尚未入仓的固定产物使用既有逐项计划，同一项未知结果不重算或补奖。两人的袋容量保持独立。

原报告、字段偏移、地址和指令仅保存在忽略的 .local；公开[验证摘要](../logs/harpoon-visual-build-verification.json)只保留计数、时间、hash 和自写结论。实际员工 actor、武器命中、捕获分流、返航品质政策、仓库保存、客机隔离、房主世界采用和双游戏正常返航仍须完成。
