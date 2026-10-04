# 缓存字典的受限 comparer 候选

[NativeGuestDictionaryComparer](../src/DaveCoop/Networking/NativeGuestDictionaryComparer.cs) 为 [游戏内缓存](GUEST_INGAME_CACHE.md) 与 [食材缓存](GUEST_INGREDIENT_CACHE.md) 共用一条有限 comparator 捕获/复制路径。0.1.22 替代游戏内缓存此前“所有非 null comparer 均拒绝”的规则，并补食材缓存此前未读 comparer 的缺口；未知类型、views 与同步引用仍拒绝。

这只是已编写的实际 typed 源码候选。生产 native entry/quiescence 仍关闭，没有执行 comparer 分配、字典 constructor/Add、缓存切换、游戏或存档操作。编译与纯 CLR 事务测试范围见开发日志/验证摘要，不作为 native 行为或完整客机隔离证据。接口声明和离线复现见 [GUEST_COMPARER_API](GUEST_COMPARER_API.md)。

## 精确白名单与语义边界

仅接受下列七个已声明闭型的 exact native class：

| key | comparer 候选 |
| --- | --- |
| `int` | `GenericEqualityComparer<int>`、`ObjectEqualityComparer<int>` |
| `string` | `GenericEqualityComparer<string>`、`ObjectEqualityComparer<string>` |
| `InGameSaveType` | `GenericEqualityComparer<InGameSaveType>`、`ObjectEqualityComparer<InGameSaveType>`、`EnumEqualityComparer<InGameSaveType>` |

所有 comparer 名称属于 `Il2CppSystem.Collections.Generic`。三 family 经 `EqualityComparer<T>` 到 `Il2CppSystem.Object` 没有已声明 native instance fields；框架 `Il2CppObjectBase` 自身的 `isWrapped/pooledPtr/myGcHandle` 是 CLR wrapper 管理状态，不复制。没有 instance fields 不是完整无状态/只读证明，静态资源、原方法行为、AOT 闭型与初始化不变式仍未知。

`EnumEqualityComparer<T>` 的生成 CLR 参数有 `new()` 约束，代码只显式闭合 `InGameSaveType`（实际 Int32 enum）。报告中的 Enum<int/string> 只是 metadata 替换，不作为支持候选；string 不满足该 CLR 构造约束，int 不是 enum。Nullable、Short/SByte/Long enum comparer、StringComparer 或任何 custom subtype 均不接受，即使它们也没有已声明字段。

源的实际 `il2cpp_object_get_class` 必须等于对应非零 `Il2CppClassPointerStore<T>.NativeClassPtr`，前后复核。不能用 C# `is`、TryCast/Cast 的 assignable 判断当 exact proof。不调用 `EqualityComparer<T>.Default` 或 `CreateComparer`，不根据 key 猜默认选型。候选 class store 的首次初始化也在 fresh 窗口中；一旦匹配即停止初始化后续无关候选，再只复核选中的 store。enum 优先检查 Enum 专闭型，但只有 exact 相等才接受。

源非 null comparer 复制为同 key type、同 class kind、同 native class 的独立实例；不能把 Object 变成 Generic，不能清为 null 或共享原实例来通过。原 comparer 为 null 可准确 Capture/Confirm，Prepare 在任何准备分配前拒绝，不用传 null 给 ctor 猜 native 默认策略。

同 class 检查用于避免已知 key equality/hash 选型替换；没有运行 Equals/GetHashCode，不能据此声称实际 native hash、字符串实现、隐藏状态或默认选择已验证。

## 早期基线、静态资源与引用审计

两个 cache 都在第一项 Serialize/Deserialize 前 Capture。每个非 null dictionary 的原 known stamp 包含 key type、comparer pointer/class/kind，以及 `EqualityComparer<TKey>.defaultComparer` 的实际 direct 静态 pointer；后者不是业务 Default getter。Capture 在 candidate class 初始化前读取该资源，末尾复读；dictionary scan 完成后也再次核对 comparer stamp。

original strict Identity 固定这些身份，不能在 serializer 后重采新 comparer 或吞掉 default 从 null 变为非 null。Prepare/Install 保持早期原图与 prepared 图，active 允许 detached 元素/版本/容器内部变化，但每个仍存在字典的 comparer 必须保持来源同 class kind 和同 key 静态资源；新 nested 字典若没有对应的原 comparer 语义基线也拒绝。

comparer 实例是 owned graph 节点，纳入现有 `GuestReferenceAudit`；其 wrapper 也在 stamp 中保持强引用。原侧允许共享这些有限实例，新字典逐个获得独立实例，原/新 comparator pointer 共享即失败。已观察 defaultComparer 是未复制的静态资源，仅冻结 pointer 并强保留观察 wrapper，不当作 owned detached 节点审计；其完整资源图及其它 globals 仍未隔离，`ResourceGraphIsolated=false`。

Dictionary `_keys/_values/_syncRoot` 必须前后确证 null；未知辅助对象拒绝，不丢弃后改默认。食材字典同时补 exact native Dictionary class 与这些 auxiliary 检查。游戏内 List/Queue 的同步引用和未支持资源/live-device frontier 继续按原规则拒绝。已有有界 storage 工作、reference observations、字符串和临时 wrappers 限额保持，超限不截断或淘汰围栏。

## 分配、显式注入及未知构造结果

内部共享合同为 `Capture<TKey>(IEqualityComparer<TKey>, Action)` → immutable Stamp，`Copy<TKey>(Stamp, Action, Action<Il2CppObjectBase>)` → typed interface wrapper，以及 RequireCloneable/RequireSameIdentity/RequireSameSemantics。

Copy 先核对已观察 default 资源和同 class store，再用 `il2cpp_object_new` 裸分配，创建 exact derived `IntPtr` wrapper 并交给 cache helper 强保留，之后做 post-call guard。再创建 `IEqualityComparer<TKey>(IntPtr)` 接口 wrapper；它仅包装已确证 pointer，不能用 interface class 代替具体 class 证明。复制不运行 comparer 的业务无参 ctor，不复制 global default 或框架 private handles。裸分配/包装/初始化有效性仍待实机。

四个实际字典使用 public `Dictionary<TKey,TValue>(capacity, freshComparer)`：

- `Dictionary<InGameSaveType,InGameSaveData>`；
- `Dictionary<string,bool>`；
- `Dictionary<string,InGameObjectSaveData.Data>`；
- `Dictionary<int,IngredientsData>`。

构造返回后，在 Add 前核对 dictionary 实际 `_comparer` pointer 等于刚注入的实例，同时 class/kind/default stamp 匹配 source。完整新图和 strict/active 验证再次检查语义、身份与跨侧 aliases。缓存入口没有改成调用业务 Comparer getter。

公共 constructor 是 alloc＋RuntimeInvoke 候选。其生成 wrapper 顺序包含 `object_new → Dictionary(IntPtr) → runtime_invoke → RaiseException`；只有整个 constructor 返回，赋值/保留才发生。若 constructor 抛异常，helper 可能没有拿到新 dictionary wrapper，不能声明所有未知分配已强保留。IngredientsData/Entity 等仍使用公共构造的路径也有同类限制。因此 cache 和共享 helper 的 `PartialConstructorAllocationRetentionVerified` 恒 false；只对成功返回且已保存的 wrappers 描述强保留。

已返回的 comparer、字典与 partial children 随 backend 保留，失败不重新派发 constructor/Add。新 comparer 由框架 wrapper 和 helper 字段/临时集合保持，不新增 root explicit handle；七步/21 explicit handles 预算保持。框架自动 handles、Entry 临时 native boxes 与 constructor 未返回的分配不计作已受这 21 个 handles 保留。

本轮没有增加 unsafe 或私有 native MethodInfo 调用工程。先裸分配 dictionary、wrap/hold，再准确派发 capacity/comparer ctor 的方案仍需独立证实参数 ABI、异常和 AOT，不能以它的设计替代当前限制。

## 故障与权限

共享 helper 的每个 class-store/static field/instance class 读取和分配前后使用 cache Reader 的 fresh thread/lease/fault-serial 窗口。匹配或分配期间重入产生新 fault 后停止后续 dispatch；分配刚返回时只先包装/保留，再做 post-check。constructor 抛错只保留未知结果，不 retry。

cache 恢复和原图确认仍可从旧 failed 状态进入，新增 fault 立即取消；确认原 comparer/default 历史不清 latch，不读取坏 detached 图。字典 single-field restore 与食材 loaded 保真策略没有扩权限。

`NativeCloneAbiVerified`、`PartialConstructorAllocationRetentionVerified`、`SourceBaselineVerified`、`CompleteGraphVerified`、`ResourceGraphIsolated`、`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority` 继续 false。实际默认选型、comparer 全部 native 行为及 static 资源、constructor 未返回分配、其它 cache/旧消费者和真实自然边界仍是完整目标的待完成项。
