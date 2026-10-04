# 客机 Interaction 已知图绑定

`NativeGuestInteractionShadow` 在 detached `SavePlayerData` 上准备新的 `SaveSystemPlayerDataManager.InstanceInteractionData`，替代空缓存占位。它实现 typed 字段读取、已知基线比较、十处容器绑定及独立 IGP HashSet 构造；没有运行游戏、原生克隆、根交换或存档。进入边界仍关闭，见 [GUEST_SHADOW_BRIDGE](GUEST_SHADOW_BRIDGE.md)、[GUEST_OUTPUT_FENCE](GUEST_OUTPUT_FENCE.md) 和 [GUEST_ISOLATION](GUEST_ISOLATION.md)。构建与测试计数由开发日志和构建摘要记录。

员工仍使用房主权威的独立临时袋，容量和负重按本人计算；本桥不提供捕鱼、分流、入仓或持久化权限，规则见 [CREW_MODE](CREW_MODE.md)。

## 实际接口和调用窗口

实现位于 [NativeGuestInteractionShadow.cs](../src/DaveCoop/Networking/NativeGuestInteractionShadow.cs)，同程序集内部使用：

```csharp
NativeGuestInteractionBaseline NativeGuestInteractionShadow.CaptureOriginal(
    DR.Save.SavePlayerData originalPlayer,
    DR.Save.SaveSystemPlayerDataManager.InstanceInteractionData originalInteraction,
    Action requireCloneWindow);

NativeGuestInteractionShadowResult NativeGuestInteractionShadow.Prepare(
    DR.Save.SavePlayerData originalPlayer,
    DR.Save.SaveSystemPlayerDataManager.InstanceInteractionData originalInteraction,
    DR.Save.SavePlayerData detachedPlayer,
    NativeGuestInteractionBaseline capturedBaseline,
    Action requireCloneWindow);

InstanceInteractionData result.Interaction;
bool result.KnownReferencesDisjoint;
bool result.ValidateKnownBinding(Action requireInteractionBindingWindow);
bool result.ValidateKnownReferences(Action requireInteractionBindingWindow);
bool result.ConfirmOriginalKnownGraph(Action requireOriginalReadWindow);
string result.Reason;
bool capturedBaseline.ConfirmOriginalKnownGraph(Action requireOriginalReadWindow);
```

捕获五个 original 根并强保留后，在任何 Serialize/Deserialize 前调用 `CaptureOriginal`，冻结原 Player/Interaction 的两份有界已知字段快照、原父指针和线程身份。它不构造业务数据/HashSet 或修改原对象；读取 Entry/Slot 仍可能触发 runtime 临时 native 装箱。`Prepare` 必须使用这份匹配来源且没有锁存读取失败的 baseline，重读原图后与早期快照比较；不能在序列化之后临时晚采一份基线代替。否则 serializer callback 同时修改原 Player 和缓存时，晚采的相等值会掩盖历史变动。

`Prepare` 的窗口来自持有实际 manager、根、lease 和强引用的生产 backend，每步前后检查 Unity 线程、当前来源、输出围栏及全部 original 根。窗口 delegate 自身不是原生授权；生产 `CanEnterBoundary` 仍拒绝进入，不能用 caller bool、Room、加载标志或已枚举 writer 为零替代原游戏边界。

准备完成、逐根安装前使用严格 `ValidateKnownBinding`。准备后仍用全部原根的 clone 窗口；逐根安装使用检查当前 lease、围栏、manager/引用、原始标量及 fresh entry 边界的 installation 窗口，不要求此时五根全部仍指向 original，因此允许既定的分步安装。它重读 original 和 detached 两侧的固定已知快照：指针、版本、值及 dirty 位变化都会锁存失败。

活动阶段使用 `ValidateKnownReferences`，窗口不要求回到 entry，但仍检查实际 lease、围栏、manager/引用及原始标量。原 Player/Interaction 的已知快照仍须不变，两个 detached 父对象也须保持固定身份；detached 容器、元素、字段、版本及 dirty 位可以变化，但当前图必须仍与 original 已观察可变引用不相交，十处绑定必须相同，当前 Player/Interaction 已知值及 IGP 列表/缓存必须一致。它不要求 detached 保持准备时的内容，不清 dirty，也不自动修复合法或非法缓存。

两个验证方法只读直接字段和互操作数组；不显式构造业务数据/HashSet、不调用 `Add`、`Sync`、manager 方法或根写入。不能称为零 native allocation：`Il2CppReferenceArray<T>.WrapElement` 的值型路径调用 `IL2CPP.il2cpp_value_box`，读取 Entry/Slot 可分配临时 native box、wrapper 及其 strong handle。任一读取、窗口、线程、配额或别名失败都会锁存，后续验证直接返回 false，不能通过重新验证消除未知结果。由外层事务保留引用/围栏并按原根 readback 处理恢复；本 helper 不卸围栏、不重试原生操作。

恢复时优先用 baseline 自身的 `ConfirmOriginalKnownGraph`，result 的同名方法委托这份 baseline。它只重读 original Player/Interaction 并与序列化前已冻结原快照比较，不要求 detached 当前图可读或匹配；Serialize/Deserialize 中途失败、没有 result 时也可使用。即使先前已锁存故障也可以做这项独立只读确认；成功不清故障、不恢复 `KnownReferencesDisjoint`、不授予权限，失败继续锁存。外层卸围栏前须确认自己围栏及真实静止边界；自己围栏确认卸除后的释放复核可用只检查 CLR owner、manager、handle 和 original 根的 original-read 窗口，不能要求已经卸除的 fence 仍 active。这项重读仍可能产生临时 native box，并非零分配。已知原子图不同，不能仅凭五个顶层根和 dirty 标量相同释放引用。

## 十处真实字段映射

下表名称均省略末尾 `_k__BackingField`，实际代码使用完整 backing 字段代理。`SavePlayerData` 的 `CargoBoxs` 拼写与 Interaction 的 `CargoBoxes` 不同。

| detached Player 字段 | 新 Interaction 字段 | 容器类型 |
| --- | --- | --- |
| `_InstalledCargoBoxs` | `_InstalledCargoBoxes` | `List<PlayerInstalledCargoBoxData>` |
| `_InstalledSensorDevices` | 同名 | `List<PlayerInstalledSensorDeviceData>` |
| `_InstalledTriggerDevices` | 同名 | `List<PlayerInstalledTriggerDeviceData>` |
| `_InstalledFunctionalDevices` | 同名 | `List<PlayerInstalledFunctionalDeviceData>` |
| `_UsedInGameInteractionItems` | 同名 | `Dictionary<string,List<string>>` |
| `_UsedInGameBreakableItems` | 同名 | `Dictionary<string,List<PlayerInteractionObjectData>>` |
| `_UsedCrabTrapZone` | 同名 | `Dictionary<string,List<PlayerCrabTrapData>>` |
| `_UsedRandomActivator` | 同名 | `Dictionary<string,List<PlayerRandomActivatorData>>` |
| `_SpawnedInGameExclusiveItems` | 同名 | `Dictionary<string,string>` |
| `_UsedIGPSetObjects` | 同名 | `List<string>` |

准备时十个容器均须存在。原 Interaction 的十个 dirty backing 位必须全部 false：`IsCargoBoxUpdated`、`IsSensorDevicesUpdated`、`IsTriggerDevicesUpdated`、`IsFunctionalDevicesUpdated`、`IsInteractionItemsUpdated`、`IsBreakableItemsUpdated`、`IsUsedCrabTrapZoneUpdated`、`IsUsedRandomActivatorUpdated`、`IsSpawnedExclusiveItemsUpdated`、`IsIGPSetObjectsUpdated`。原 Player 与原 Interaction 的已知值必须相等，detached Player 也须保留同样的已知值。

原运行缓存若有尚未提交变化，仅克隆 Player 会丢失这部分语义。当前明确拒绝 dirty 或已知值不一致的来源，不从字段名推断提交方向、不调用 `SyncInstanceDataWithPlayerData` / `SyncDataWithInstanceData`。即使上述比较相等，也不证明全部源基线已捕获，`SourceBaselineVerified` 始终 false。

围栏和来源检查通过后，单次调用 `new InstanceInteractionData(false)`，把上述容器绑定至 detached Player，单次构造 `Il2CppSystem.Collections.Generic.HashSet<string>()`。先在 CLR 中按 Ordinal 去重并排序 IGP 标识，再对每个唯一值单次 `Add(string)`，确认返回 true 后绑定 `_usedIGPSetRuntimeSet_k__BackingField`。新 Interaction 的十个 dirty 位显式写 false。constructor 和 Add 都是实际 `RuntimeInvoke`，仅设计在受保护的准备窗口执行，失败或未知不得重试。验证阶段不再构造或修改 HashSet。

## 已核对的元素和直接布局

四类设备记录都是可变引用类，继承 `PlayerInstalledDeviceDataBase`。读取其九个 backing 字段：`DeviceSubHelperType`、`DeviceType`、`DeviceInstalledSceneType`、`DeviceUID`、`DeviceTID`、`DevicePositionX/Y/Z`、`SavedSlotIndex`。Sensor、Trigger、Functional 分别另读对应 `*DeviceType`。

Cargo 另读 `Weight` 及 `CargoProductList : List<CargoSlot>`；列表、非空存储数组、每个可变 `CargoSlot` 都加入引用审计，slot 的 `itemID/itemGrade` 直接读取。Breakable 读取 `UniqueID/RemainCount/MaxCount`；Crab 读取 `UniqueID/SetUpTime/BaitLevel/TrapState`；Random 读取 `UniqueID/RandIdx`。蟹笼时间为 `Il2CppSystem.DateTime` CLR 值结构，用 public readonly `_dateData : ulong` 比较，不调用其日期 getter。设备浮点字段须 finite，以 CLR 位模式比较。

| 类型 | 使用的直接字段 | 读取方式与限制 |
| --- | --- | --- |
| `List<T>` | `_size/_version/_items : Il2CppArrayBase<T>` | 核对 size 与 storage length，读所有容量槽并复核版本/数组；不调用 List Count 或 indexer |
| `Dictionary<string,T>` | `_count/_freeCount/_freeList/_version/_entries/_buckets` | `_count` 是含空闲槽的使用跨度，活项数量须等于 `_count-_freeCount`；按 hashCode 识别活项，CLR Ordinal 排序 |
| `Dictionary<string,T>.Entry` | `hashCode/next/key/value` | native ValueType wrapper；临时 boxed wrapper 不作为持久图引用，真实存储数组和值容器单独审计 |
| `HashSet<string>` | `_count/_lastIndex/_freeList/_version/_slots/_buckets` | 核对活项计数与唯一值，并与 IGP 列表去重值相等 |
| `HashSet<string>.Slot` | `hashCode/next/value` | 同样为 native ValueType wrapper；读取布局尚待真实 ABI 验证 |

Entry storage 是 `Il2CppReferenceArray<Dictionary<string,T>.Entry>`，Slot storage 是 `Il2CppReferenceArray<HashSet<string>.Slot>`。这些声明以及 runtime 数组 Length/indexer 签名来自离线 Cecil；typed array/entry boxing、实际布局和泛型 ABI 尚未在游戏中运行验证。数组访问依赖 Il2CppInterop 的 native 数组操作，不能把声明编译当作真实 native 读取正确性证据。

所有非空存储数组、容器、已核对的可变元素及 Cargo 子记录均审计。只有确认 Length 为零且 shape 正确的数组允许两侧共享，它们没有可变元素。List 未使用尾槽、Dictionary 空闲/未使用槽若残留引用，或 HashSet 空闲/未使用槽保留非空值，当前保守拒绝。没有通过截断或跳过尾槽隐藏别名。

## 配额与未覆盖引用

`GuestReferenceAudit` 每侧最多 4096 次引用观察，重复读取也计数；original 两图封闭后才能开始 detached 两图，首次跨侧别名、零指针、越序或超额即锁存。每次失败都检查返回 bool 并抛固定原因，异常文本不含存档值、对象名或标识内容。

每个容器 storage 最大 1024；单字符串最大 512 UTF16 单元；一个 Reader 对复制字符串的累计预算为 512 Ki UTF16 单元。另有独立 `MaxStorageReads=16384` 工作预算，跨四图累计每次 array slot/bucket 访问，包括未使用尾部、空闲槽和无引用的槽；CaptureOriginal/原图确认的两图也使用同样累计预算。引用预算不能替代扫描预算，storage visits 也不是临时 box 或所有 GC handle 的精确计数。所有这些检查都是读回后限制，不限制底层 native 字符串/数组包装器的初始工作。

已复制的 CLR string 和 DateTime 值不作为可变引用。Dictionary/HashSet comparer、`_syncRoot`、Dictionary keys/values cache、HashSet serialization cache、SavePlayerData 其它成员、未知派生字段及 manager/协程持有的旧引用尚未归入完整图；新根的顶层指针不同也不能排除这些 shared alias。自然业务修改是否总能在既定边界恢复十处绑定和 IGP 一致性仍需实测。

`KnownReferencesDisjoint` 只表示本次实际读取的已声明引用未发现跨侧共享。`CompleteGraphVerified`、`SourceBaselineVerified`、`NativeCloneAbiVerified`、`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority` 全部恒 false。typed 准备能力没有接 Network/GUI，不等于客机隔离、员工玩法或保存安全验收。

## 离线复现

```powershell
.\development\scripts\Inspect-GuestInteractionApi.ps1
# 可显式传 -GamePath；报告只能写 development/.local/analysis。
```

脚本只通过 Cecil 读取生成的 `Assembly-CSharp`、`Il2Cppmscorlib`、`Il2CppSystem.Core`、`UnityEngine.CoreModule` 及 `Il2CppInterop.Runtime` 元数据/包装器 IL，记录输入 SHA256、缺失类型和生成时间。它不加载/执行游戏类型，不读取存档、不调用游戏 API；原始元数据报告不提交。

既有 `guest-shadow-clone-native-calls.json` 中 `SyncInstanceDataWithPlayerData` 的唯一无歧义 managed direct target 是 `LoadRuntimeIGPHashData`，后者能见 HashSet Clear；其余包含未解析/共享别名边。静态调用边不证明同步方向、完整克隆、旧缓存失效或执行顺序。本实现不调用这些同步方法；进一步实际缓存/输出/退出静止边界验证完成前保持生产进入关闭。
