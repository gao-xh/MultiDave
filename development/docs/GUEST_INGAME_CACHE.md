# 潜水运行缓存的有限 typed 影子候选

[NativeGuestIngameCache](../src/DaveCoop/Networking/NativeGuestIngameCache.cs) 提供实际 `IngameSaveDataManager` 缓存的早期捕获、有限准备、只读核对、单字段安装与补偿。它处理已枚举的六种保存记录及可支持的子图，保留非空数据；无法证明的资源、设备和容器辅助引用明确拒绝，不能用共享引用或空表替代。

本轮没有执行 helper、native allocation、字段交换、游戏或存档操作。生产 entry/quiescence 仍拒绝，未接 Network/GUI。构建与纯 CLR 事务测试由开发日志和验证摘要记录，不作为 native ABI、构造有效性、完整缓存隔离或员工玩法验收。每人独立背包、容量和负重规则仍按 [CREW_MODE](CREW_MODE.md)。

实际声明、资源 frontier 和离线复现见 [GUEST_INGAME_API](GUEST_INGAME_API.md)；已有根桥与其它缓存见 [GUEST_SHADOW_BRIDGE](GUEST_SHADOW_BRIDGE.md)、[GUEST_INTERACTION_SHADOW](GUEST_INTERACTION_SHADOW.md)、[GUEST_INGREDIENT_CACHE](GUEST_INGREDIENT_CACHE.md) 和 [GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)。

## 接口与固定来源

同程序集内部合同：

```csharp
static NativeGuestIngameCache CaptureOriginal(
    Action nativeWindow, Action<Il2CppObjectBase> keep);
bool Prepare(Action nativeWindow);
bool ValidateKnownBinding(Action nativeWindow);
bool ValidateKnownReferences(Action nativeWindow);
bool ConfirmOriginalKnownGraph(Action nativeWindow);
bool Install(Action nativeWindow);
bool Restore(Action nativeWindow);
GuestShadowRootReadback ReadCurrent(Action nativeWindow);
```

`OriginalInstance`、`OriginalStorage`、`DetachedStorage` 是 typed 引用；`Prepared`、`Failed`、`KnownReferencesDisjoint`、`StorageInstallEntered`、`StorageRestoreEntered` 和固定文本 `Reason` 提供诊断。诊断不含游戏字段值、key、UID 或指针。

singleton 的真实直接代理为 `SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField`。公共 `s_Instance` / `Instance` / `hasInstance` 会 RuntimeInvoke，不调用。manager 唯一实例 direct 字段是 `ingameSaveDatas : Dictionary<InGameSaveType,InGameSaveData>`。Capture 固定当前线程、singleton 和原字典；不创建替代 singleton。

Capture 必须发生在第一项 Serialize/Deserialize 之前。它冻结已知原记录值、指针关联、容器 shape、全部存储跨度和版本。Prepare 必须匹配这份早期历史，不能在 serializer 之后重采基线。来源窗口由实际 backend 核对 lease、Unity 线程、强引用、manager、标量、原保存根、围栏和真实 entry；捕获窗口不能递归要求尚未建立的本缓存基线。

原 dictionary 为 null 可以准确捕获和确认 Original，Prepare 明确拒绝，不猜 Init 后的空表。非 null 空表与非空表均按同一规则读取；缺六种某项不会凭空补记录。支持的 nested 容器或元素原值为 null 时准确保留，不通过业务 constructor、Init、Save、Clear、Reset 或 getter 补数据。

## 六种 schema 与复制边界

只接受 native 实型精确匹配下列 key/type。`InGameSaveData` 没有自身 direct 实例字段，六种派生的已声明字段都处理；未知 enum、未知子类或 key/type 不符拒绝。

| 原 key | 精确类型及字段 | 有限复制 |
| --- | --- | --- |
| `CharacterHealth=0` | `CharacterHealthData.HP : float` | 保留有限值和 float 位模式，不夹值或计算新的生命值 |
| `CharcterEquip=1`（原拼写） | `CharacterEquipData._currentEquipInInventory_k__BackingField : List<int>`、`gunAmmo : int` | 新 list，保留所有活动装备 ID、顺序及弹药 |
| `CharacterSubHelper=2` | `CharacterSubHelperData._subHelperSlots_k__BackingField : Il2CppReferenceArray<SubHelperSlotData>` | 新数组和支持的每个非 null slot；未知资源/设备分支拒绝 |
| `CharacterInstallDevice=3` | `CharacterInstallDeviceData._currentInstalledDevices_k__BackingField : List<InstallDeviceSaveSlot>` | 新 list、新每个 slot，保留 type/UID/position |
| `PuzzleState=4` | `PuzzleStateSaveData._keyToSolves : Dictionary<string,bool>` | 新字典，保留原 key 与 bool |
| `InGameObject=5` | `InGameObjectSaveData._keyToDatas : Dictionary<string,InGameObjectSaveData.Data>` | 新字典、新每个 Data，分别保留 map key 与 Data.key/isSaved |

`SubHelperSlotData` 全部七个字段为 `subHelper`、`remainCount`、`remainTime`、`lastActiveTime`、`unfocusdTime`（原拼写）、`isAvailable`、`gearQueue`。只有 `subHelper=null` 的分支可以进入候选复制。`SubHelperSpecData` 是 `SerializedScriptableObject`，含多态 Containers 和继承的 serializationData/Unity 资源图；普通 `object_new` 不能证明它是有效独立 Unity 资产。不沿用其资源指针，不清空原非空资源。

`gearQueue` 为 `Queue<IInstalledDevice>`。原 null 保留；非 null 必须 `_size=0`，`_head/_tail` 满足真实空队列 shape，全部 capacity slots 都为 null，`_syncRoot=null`。新 queue 保留容量及已读空队列 head/tail/version。非空设备、逻辑空但尾部残留设备或同步引用都在准备分配前拒绝；不调用设备 getter、Remove、Restore，也不把 live actor 当普通记录复制。

`InstallDeviceSaveSlot` 的三个 backing 字段是 `_DeviceType_k__BackingField`、`_UID_k__BackingField`、`_InstalledPosition_k__BackingField`。`UnityEngine.Vector3` 是实际 CLR struct，按值保留 x/y/z。`InGameObjectSaveData.Data` 只有 `key:string` 和 `isSaved:bool`。

原 root row、slot、设备保存 slot、Object.Data 或空 Queue 的重复 native pointer 会被 preflight 拒绝。当前没有 pointer→clone memoization，不能把原同侧共享记录逐项拆成新对象后声称 alias 语义保真。字符串按不可变值处理，标量按值；不复制身份相同的原可变节点到 detached 侧。

以上六类 direct 图不含 Obscured 字段。不会通过猜普通数值字段解密 Obscured；已核对的 ObscuredFloat 是含数组的 native ValueType wrapper，不能因 ValueType 名称按 immutable struct 共享。资源分支中的这类对象仍在未支持 frontier 中。

## 分配、工作预算与已知引用

普通保存记录通过 `IL2CPP.il2cpp_object_new` 与 typed `IntPtr` wrapper 创建候选，逐字段复制；不运行业务游戏 constructor 来猜初始化。尤其 Equip 的业务 constructor 需要装备/Spec 字典，不能当无参 clone。裸分配绕过游戏 constructor，不证明隐含不变式有效。

实际 class 校验是 `il2cpp_object_get_class(pointer) == Il2CppClassPointerStore<T>.NativeClassPtr` 且 class 非零，并再次核对 class store。C# `is`、TryCast/Cast 是 assignable 判断，不能证明精确 native 类型。class store 的首次初始化也可能调用 native metadata，因此获取 class、分配、分配后核对分别在 fresh 窗口内。分配返回后先创建/强保留 wrapper，再进行 post-call guard；其后才读 class 或写业务字段。Dictionary/List/Queue constructor 与 Add 仍是实际 native 调用候选，并非已验证纯 clone。

字典读取 `_count/_freeCount/_freeList/_version/_entries/_buckets` 与 Entry 的 `hashCode/next/key/value`；不使用业务 Count、indexer 或枚举方法。读取全部 buckets 和 entries capacity，包括 free/unused/tail；残留引用、重复 key、超界/不一致 shape 拒绝。Identity 包含每存储槽 key 与 child pointer 的关联，不能只用排序后的逻辑值掩盖原 key/对象调换。List 读取 `_size/_version/_items`，全部 capacity slots 也扫描；活动外 mutable 引用拒绝。

0.1.21 的 Dictionary `_comparer/_keys/_values/_syncRoot` 只支持确证为 null，普通容量构造生成的非空 comparer 也会拒绝。0.1.22 按 [GUEST_DICTIONARY_COMPARERS](GUEST_DICTIONARY_COMPARERS.md)捕获有限精确 class 候选，准备独立同类 comparer，并在 `(capacity, freshComparer)` 构造后、Add 前核对实际 comparer；原/新 pointer、class、kind 和已知 static default 指针进入基线与引用审计。原 null 可准确捕获和确认，但 Prepare 拒绝，不猜测默认语义。未知/custom comparer 及 `_keys/_values/_syncRoot` 非空仍拒绝，不共享、替换或清空。List 与 Queue 的 `_syncRoot` 也前后复核为 null。该源码尚未证明原生 comparer 构造、相等、哈希或完整资源隔离。

每个容器实际 storage 最多 1024；一个 Reader 累计最多 65536 次 storage visits，跨其全部原/新图读取计数，包含 buckets/free/tail。每侧 `GuestReferenceAudit` 最多 4096 次 reference observations（重复计数）；temporary wrapper 强保留也最多 4096。单字符串最多 512 UTF16 单元，Reader 累计字符串内容最多 512 Ki 单元。超限拒绝，不截断后报告完整；引用额度不替代实际扫描工作额度。

Dictionary.Entry 是 native ValueType wrapper，数组 `WrapElement` 可 `il2cpp_value_box`；只读捕获/核对也可能分配临时 native box、wrapper 与框架强句柄。storage visits 不是精确 box、GC handle 或 native 分配数。临时 Entry box 不作为持久 mutable graph 节点；真实记录、容器及非空数组审计。确证长度为零的数组不含 mutable 元素，允许空数组跨侧共享。

backend 的 `keep` 最多额外保留 singleton、非 null 原 dictionary、新 dictionary 三个 explicit 强 handles；root 桥统一控制总预算。成功返回的新 root 在 post-check 前存 helper 字段，成功返回且未挂入 root 的新 child/container/comparer 在 post-check 前存 temporary wrappers。失败/未知时继续保留已取得的 helper 和 backend 引用；这三个 explicit handles 不包含框架自动 handles 或临时 boxes。普通 native constructor 可在 caller assignment/Hold 前抛异常，尚未返回的分配不能据此宣称已保留，`PartialConstructorAllocationRetentionVerified=false`。

## 单字段 readback、安装与补偿

`ReadCurrent` 只核对固定 singleton 和单一 dictionary 指针，并复读确认一致，不递归读完整图。返回 Original、Detached、Foreign 或 Unknown；该缓存没有两字段 OwnedMixed。相同 bool 或其它 CLR 标记不替代真实指针身份。

Prepare、Install 和 Restore 各只有一次派发尝试。Install 先严格核对原图与 prepared 图，再设置 `ingameSaveDatas`，写入前记录 entered，之后读回 Detached。Restore 只对确证 owned Detached 写回 Original；Foreign/Unknown 不覆盖。setter 抛异常或 post-check 失败之后只允许后续 readback，不能再派发。迟到 Original 证据可以确认已恢复，不能因此清除故障 latch。

`ValidateKnownBinding` 用于准备后和逐根安装，允许当前 cache 仍为 Original；原历史和 prepared 身份/值都严格不变。`ValidateKnownReferences` 用于已安装的活动状态，只允许固定 owned dictionary 为 Detached，原已知图仍冻结；允许 detached 内部字段、版本、存储或新元素变化，但仍须精确 schema、支持的 frontier、配额与跨侧引用不相交。合法资源或 live queue 出现而本实现尚不能证明时也拒绝，不能宣称员工活动缓存已全部覆盖。

`ConfirmOriginalKnownGraph` 可从已有 failed 状态独立只读原图，必须 singleton/dictionary 已 Original；不依赖损坏的 detached 图、不重新创建对象、不清 Failed 或恢复 KnownReferencesDisjoint。卸围栏后的 Release 窗口可不要求 active fence；卸除前的原根/manager/已知图和真实 quiescence 确认仍由 backend 完成。

helper 拒绝错线程和业务重入。每次 Reject 用 Interlocked 推进 fault serial，各实例操作冻结当前 serial，每个窗口和 read/write 前后核对；native callback 重入后不继续新的 dispatch。cleanup 可从先前 failed 的 serial 开始，但期间新增 fault 立即取消。轻量 ReadCurrent 使用独立防递归标记，可在外层 guard 中读固定身份；窗口不得再次递归调用它。所有窗口由持有真实来源的 backend 执行，caller bool 不能授予原生权限。

`NativeAllocationAbiVerified`、`NativeCloneAbiVerified`、`ResourceGraphIsolated`、`SourceBaselineVerified`、`CompleteGraphVerified`、`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority` 始终 false。`KnownReferencesDisjoint` 只描述本次实际读取的有限节点。完整目标仍需要 SubHelper 资产/actor 设备及其它资源/旧消费者、真实边界、全输出/在途工作、native ABI 与双端玩法证明。
