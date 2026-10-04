# 员工捕获产品与个人容量

0.1.31-dev 在一次性鱼产物选择后增加产品归一化与缓存批次的 LateSeal 入口。房主仍使用自己的原生袋；员工使用房主 Mod 持有的个人袋，各自容量、重量与负重。此桥没有网络或 GUI 生产调用，尚未提交真实员工捕获。

## 从固定选择到产品

`FishYieldSelection.NormalizeOnce` 只在同一账本租约已进入、选择为 RawPlanHeld 时运行。每个正数选择必须已有保留资源；按原顺序各读取一次 `IItemBase.TID`、`ItemGrade`、`ItemWeight`、`ItemType`，每次前后复核来源。原虚调用可能执行原生业务，并非已证纯 getter，因此先标记尝试，异常后不重读、不重新 lookup 或选择。

产品编号来自 TID，允许不同于选择时的 lookup ID；ItemDataID 用于后续食材关联，不能代替产品编号。相同 TID 的不同选择仍是独立产物。当前 native 实现只支持原元数据确认实现 IItemBase 的精确 DR.Items 类。IntegratedItem 有相似字段名但不实现该接口，拒绝而不猜转换。

捕获原品质为基础品质加已固定 bonus，使用 checked 加法及现有 CargoProduct 范围检查。每项 count=1；None 或 Trap 的有效单位重量取原 float，其他已支持 lift 为零。先做原 float 乘法，再转 double；例如 0.1f 对应的 double 保留原二进制值。员工会话袋在 double 中累加各项，这是本 Mod 的袋规则，不宣称等于原生袋逐次 float 累加。非法编号、品质、重量或全 -1 空批次保留未知，不伪造空捕获。

完成后状态为 CaptureProductsHeld，缓存有序 CargoProduct 与指纹。快照深复制可变数组和产品；部分失败保留已经返回的标量和原选择，状态为 EnteredUnknown，不再归一化。线程/重入拒绝不进入新业务。

## 容量与捕获提交

`TrySealCaptureProducts` 仅向既有账本 LateSeal 传递该桥缓存的同一批产品，不接受外部替换。真实房主 producer 仍须提供当前成员/袋修订/来源与 complete、held、no-write、isolation 等事实；此入口不补造标志。产品字段完成不证明实际员工操作归属、原生副作用覆盖或完整捕获链。

容量不足保留批次和资源；之后可以用新鲜事实重新核容量，不再读取原 getter 或重新选择。LateSeal 只预留员工重量，仍为 EnteredUnknown；尚须真实员工分流、共享进度和魚终态凭证才可 ConfirmCapture。断线、返航与场景变化不清未知记录。

## 返航品质与入仓

CargoProduct.Grade 表示捕获时的原槽品质，不能被返航 FinalGrade 覆盖，否则会改变捕获指纹。已知原 GetExchangeCount 使用原品质；AddFromLootBox 则使用 FinalGrade，默认数量取 TotalCount，有兑换 delegate 时取其结果一次。品质转换与数量转换须各自证明，不能共用一个猜出的最终值。

原 ApplyFinalGrade 访问真实房主 SaveData.LootBox；阈值是实例字段，类别判定叶方法、实际 additive 和时机尚未完整验证。不能借临时 LootBox 执行来推导员工品质。本桥始终 FinalReturnGradeKnown=false，冻结 ItemType 也不自动授予分类或入仓权限。

后续员工返航计划应以 ReturnId/CaptureId/ProductIndex 关联，绑定原产品指纹与一次冻结的转换政策，再固定最终品质、入仓数量和独立返航指纹。房主原袋走自然链只观察，员工另走逐项真实入仓/增量/保存确认，不重复 Add 已确认物料。若返航还需资源元数据，先冻结所需值或转移资源所有权，再释放选择桥；Capture Confirmed 本身不证明返航完成。

## 验证范围

夹具运行实际 Core coordinator 与 ledger，使用 synthetic backend 验证四次 getter、部分异常、来源失效、标量边界、产品副本、线程重入和同批容量复核；不运行原生 getter、GC、鱼捕获或存档。实际测试、编译及本轮离线研究计数见 [构建摘要](../logs/employee-fish-products-build-verification.json)。

没有部署或启动此版本，安装0.1.12、最近潜水0.1.11、默认发行包0.1.0保持。真实可信员工 actor、个人分流/负重/捕获凭证、逐项返航、客机隔离/房主世界、双游戏与 GitHub 冷配置继续完整 M3—M7。
