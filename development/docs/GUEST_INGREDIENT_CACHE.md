# 食材缓存的 typed 影子候选

[NativeGuestIngredientCache](../src/DaveCoop/Networking/NativeGuestIngredientCache.cs) 为已有原生根桥增加有限的食材缓存捕获、准备、字段安装、核对与恢复能力。实现使用实际 `IngredientsStorage` / `IngredientsData` 类型；生产进入和静止边界仍关闭，没有执行 native constructor、缓存读取/交换或存档操作，也没有接员工玩法入口。构建和纯 CLR 测试范围以开发日志及构建摘要为准。

原接口及资源继承链见 [GUEST_INGREDIENT_API](GUEST_INGREDIENT_API.md)；五根桥、输出围栏与其它尚未隔离缓存见 [GUEST_SHADOW_BRIDGE](GUEST_SHADOW_BRIDGE.md)、[GUEST_OUTPUT_FENCE](GUEST_OUTPUT_FENCE.md)、[GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)。独立个人袋与房主持久收益规则继续按 [CREW_MODE](CREW_MODE.md)，本缓存不提供捕获、分流、入仓或任务权限。

## 接口与来源绑定

同程序集内部的实际合同为：

```csharp
NativeGuestIngredientCache CaptureOriginal(
    Action requireReadWindow, Action<Il2CppObjectBase> keep);
bool Prepare(Action requirePrepareWindow);
bool ValidateKnownBinding(Action requireReadWindow);
bool ValidateKnownReferences(Action requireReadWindow);
bool ConfirmOriginalKnownGraph(Action requireOriginalReadWindow);
bool Install(Action strictWriteWindow);
bool Restore(Action restoreWindow);
GuestShadowRootReadback ReadCurrent(Action requireReadWindow);
```

Capture 是 static 方法，返回固定当前线程和 singleton 的 helper。`OriginalInstance`、`OriginalStorage`、`DetachedStorage`、`OriginalLoaded` 提供 typed 身份；`Prepared`、`Failed`、`KnownReferencesDisjoint`、`StorageInstallEntered`、`StorageRestoreEntered` 和固定 `Reason` 提供诊断。loaded 值保持原值，`LoadedWriteDispatched` 恒 false。

原 singleton 的真实直接代理是 `SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField`，不是 `_instance`；公共 `s_Instance` / `Instance` / `hasInstance` 都会 RuntimeInvoke，本 helper 不调用。singleton 为 null 时拒绝捕获，不创建另一 singleton。`m_Storage` 为 `Dictionary<int,IngredientsData>`，`m_IsLoaded` 为 bool，均为 native direct field proxy。

在任何 Serialize/Deserialize 前 Capture，冻结原字典指针、loaded 值、存储形状、版本以及全部已声明条目/Entity 值。Capture 窗口由实际 backend 核对 lease、Unity 线程、强引用、manager、原标量、五个原保存根、输出围栏及真实 entry；不能递归要求尚未捕获的 cache 基线。Prepare 必须仍匹配早期原图，不能晚采一份新基线掩盖 serializer callback 修改原子图。

`keep` 由 backend 持有实际强 GC handles，最多增加 singleton、原 dictionary 和新 dictionary 三个不同非空引用。null 原 dictionary 跳过 keep。嵌套 rows/counts/Entity 由 dictionary 的原生图和框架 wrapper 强引用保持；partial Prepare 的新 dictionary、当次 data/counts/Entity wrapper 先存入 helper 字段，再做 post-call guard，未知时随 backend 保留。没有将框架自动 handles 或临时 box 计作仅这三个 explicit handles。

普通 API 阻止错线程和业务重入。每次 Reject 用 Interlocked 推进故障 serial；每个实例操作在 Begin 冻结本次 serial，Reader 在 fresh 窗口前后以及 native read/write 的前后核对它。constructor/Add 回调重入后即使原调用返回，也会立即停止后续构造或写入。cleanup 可以从已 failed 的当前 serial 开始，但本次新增故障同样立刻停止，不清旧 latch。Capture 的局部 helper 尚未向 backend 暴露，只使用来源窗口。

轻量 `ReadCurrent` 使用独立防递归标记，允许 backend 在 Prepare 的 fresh guard 中只读同一 cache 身份；该 guard 不得再次调用 cache，不能从中重入 Prepare/Install/Restore/图验证。嵌套读取若 Reject，也会推进 serial 并阻止父操作 post-call 后继续。窗口检查持有实际来源的 backend，不是 caller bool 原生授权。

## null 与 loaded 策略

原 `m_Storage=null/m_IsLoaded=false` 可被 Capture、ReadCurrent 和原图确认准确记录，未安装状态也可确认恢复；Prepare 明确拒绝。null dictionary 却 loaded=true 时 Capture 即拒绝，不把矛盾的已加载状态报告为已知基线。null 与空 dictionary 的业务语义尚未证明相同，不调用 Init/Load、不猜空数据或把相同 null 标记为 Detached。

原非空 dictionary 且 `m_IsLoaded=false` 可以按实际条目复制，准备与安装均保留 false，不强制变 true。相反 loaded 值视为 Foreign，保持围栏/引用。自然 Init/Load 如果替换已安装 dictionary，当前只会识别为 Foreign，不能自动把任意新指针认作本租约 owned cache；该行为及其保存副作用尚未验收。活动验证允许已 owned 字典内部的版本、存储数组、行、counts 和 Entity 值变化，外层 singleton、owned dictionary 指针及 loaded 值仍固定。

条目 counts=null 也可读回原基线，但 Prepare 拒绝，不猜默认长度或数量。原 counts 长度为零时保留零长度；只有已确认长度为零的存储数组允许跨侧共享，因为没有可变元素。

## 有限复制内容

Dictionary key 按实际 int 保留，不能假定 key 等于 `ingredientsID`、`parentID` 或 Entity TID。每行新建 `IngredientsData(row.ingredientsID)` 候选，再逐字段写入完整 11 个 direct 字段：

| 字段 | 处理 |
| --- | --- |
| `ingredientsID/level/parentID/rank/placeTagMask` | Int32 原值，不推断或规范化产物 |
| `type/isNew` | 枚举与 bool 原值 |
| `counts : Il2CppStructArray<int>` | 新独立数组，复制原长度和每个整数 |
| `lastGainTime/lastGainGameTime` | 真 CLR `Il2CppSystem.DateTime` 按值复制、以 `_dateData : ulong` 核对 |
| `_Entity_k__BackingField` | null 保留；非空创建独立 Entity，复制下述 13 个 instance 字段 |

Entity 复制的 13 个 backing 字段为 `_ItemsTID`，继承的 `_TID`、`_Type`、`_NameID`、`_DescriptionID`、`_IsUse`、`_NotUseParentTID`、`_TIDNumberConnect`、`_DeliverableToBranch`、`_MaterialColor`、`_IsCompoundable`、`_ContentsThumbnail`、`_CategoryType`；实际名称均带 `_k__BackingField`。类型为 4 int、4 string、4 bool、1 enum。原 Entity 指针及这些值也在早期基线中冻结，不能只复制顶层资源指针。

`IngredientsData(int)`、`DR.IngredientsEntity()`、Dictionary constructor/Add 都是实际 native 调用候选，并非已运行的无副作用 clone。Data ctor 的可解析静态目标含 `DataManager.GetIngredients(int)`；不能据这一条边断言其它副作用不存在。每次 constructor/Add 和字段/数组写入都在 fresh 窗口前后检查，未知失败不重新派发。`IntPtr` constructor 只是 wrap，不作复制。不会调用 Storage Init/Load/Reset、Data UpdateSaveData、业务 getter 或通知方法。

Entity own 字段分开不证明资源图完整隔离。继承上层的 Lazy 字典、DefaultDirectory、初始化委托是 static 资源；Parent 是 RuntimeInvoke getter，没有可复制的 direct Parent 字段，关联 `DR.Items` 还持有三个可变 unlock lists。本 helper 不调用 Parent，不复制全局 sheet 或这些未知消费者，因此 `EntityReadonlyVerified` / `ResourceGraphIsolated` 都保持 false。

## 字典读取和配额

读取 Dictionary 的 `_count/_freeCount/_freeList/_version/_entries/_buckets`，不用 Count、字典 indexer 或业务枚举方法。`_count` 含已使用跨度及 free slots；按 Entry.hashCode 识别活项，活项数量须为 `_count-_freeCount`，key 唯一，next/bucket 值与存储形状须在界内。所有 unused/free/tail Entry.value 必须为空，不能跳过残留可变对象。

Entry 为 native ValueType wrapper，`Il2CppReferenceArray<Entry>` 的 runtime `WrapElement` 包含 `il2cpp_value_box` 路径；只读核对也可能分配临时 native box、wrapper 与 strong handle，不是零 native allocation。临时 Entry box 不当作持久图节点，真实 dictionary、非空 entries/buckets/counts 数组、Data 和 Entity 由 `GuestReferenceAudit` 审计。原侧封闭后再审新侧，任一已读跨侧别名、零引用或超额即失败；同侧内部共享允许。

每个 dictionary storage 最大 4096、每行 counts 最大 16、每侧 reference observations 最大 4096（重复计数）、一个 Reader storage visits 最大 65536（含 bucket/free/tail/counts）。一个 resource string 最大 512 UTF16 单元、复制字符串累计最大 512 Ki 单元。引用计数不替代扫描工作预算；visits 不是 native boxes/handles 的精确数，读回后的限额不限制底层初始包装或构造分配。超限拒绝，不截断后报告完整。

Dictionary `_comparer/_keys/_values/_syncRoot`、未知派生字段、static 资源及 UI/任务/协程的旧条目引用未被完整审计；known graph 不等于完整 mutable graph。generic closed metadata、typed entry boxing/数组布局、日期字段与实际 native setter/constructor ABI 都待实机。

## 两字段安装、恢复与验证

`ReadCurrent` 只在 fixed singleton 上直接重读两字段并复核一致性，不递归读完整原/新图。联合 singleton + dictionary pointer + loaded 值分类为 Original、Detached、OwnedMixed、Foreign 或 Unknown，不用 bool 本身冒充身份。当前 loaded 保真且原/新 expected bool 相同，因此通常不会出现 OwnedMixed；只有两个 expected 值有已证明差异且每字段分别属于原/新时才能使用该状态，不能为制造 Mixed 改 loaded=true。

Install 一次核对严格原/准备快照，storage setter 在可能进入前记录 attempt，再重新读回；loaded 因值相同不派发无意义 setter，但独立 fresh 读取确认。future 不同 loaded 策略须另证并扩展 helper，不能把 Core 的 synthetic partial pair 当现实际 native loaded 写入证据。`Prepare`、Install 和 Restore 各只允许一次派发尝试。

Restore 反向处理 loaded、storage：当前保真策略 loaded 已是原值，先确认，再只对确证 owned 的 detached storage 写回原指针。Unknown/Foreign 不覆盖；field setter 抛异常或 post-check 失败后只允许后续 readback，不能再次写。先前 graph 验证故障或 detached 图损坏不阻止这项有界原字段补偿；helper 不要求 active fence 或读取完整 detached 图才 restore，实际恢复窗口及围栏保留由 backend 决定。最终两字段 Original 和原 known 图确认独立于写入返回。

严格 `ValidateKnownBinding` 用于准备后和逐根安装期间，可见 cache 仍为 Original；它冻结原图和 prepared 图，值与 key 一致、已知引用不相交。活动 `ValidateKnownReferences` 要求 cache 为确证 Detached，允许 owned 字典内部合法变化，仅原已知图保持冻结并再次审计当前别名；不会重建或清缓存。

`ConfirmOriginalKnownGraph` 可在 failed 后、detached 图坏掉或 serializer 中途失败时独立读原已知图，必须两字段也已 Original。自己 fence 卸除后的 Release 复核可用不要求 active fence 的 original-read 窗口；卸前仍由 backend 确认自己的围栏和真实静止边界。该确认成功不清故障或恢复 KnownReferencesDisjoint，失败继续锁存。未知恢复保留 backend、handles、helper 图和围栏，不因 timeout 或断房释放。

`KnownReferencesDisjoint` 只覆盖实际已读节点。`NativeCloneAbiVerified`、`EntityReadonlyVerified`、`ResourceGraphIsolated`、`SourceBaselineVerified`、`CompleteGraphVerified`、`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority` 始终 false。已知食材缓存合同不是完整客机状态隔离或保存安全验收。
