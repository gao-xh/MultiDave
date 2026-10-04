# 食材缓存与资源实体的精确接口

`IngredientsStorage` 的当前实例应从 `SingletonNoMono<IngredientsStorage>._s_Instance_k__BackingField` 直接读取。这个基类没有 `_instance`；公共 `s_Instance`、`Instance`、`hasInstance` 都会调用原方法，不能用它们捕获缓存而假定没有初始化副作用。

本页给出 0.1.20-dev 的离线接口证据，供 typed 缓存桥实现使用。没有执行 singleton getter、字段代理、原构造、容器、业务 getter、hook 或保存操作；没有读取存档。源码实现、构建与实机范围另由本轮验证摘要记录。全资源图、完整缓存、原生构造 ABI 和 `GuestStateIsolated` 不因本页成立。

## 可复现检查

运行 [Inspect-GuestIngredientApi.ps1](../scripts/Inspect-GuestIngredientApi.ps1)：

```powershell
.\development\scripts\Inspect-GuestIngredientApi.ps1
```

也可指定 `-GamePath`，以及只允许安全文件名的 `-ReportName guest-ingredient-api.json`。没有任意输出目录参数；结果仅写入忽略的 `development/.local/analysis/`。工具自动定位 Steam，使用已安装 Cecil 读取四个生成/runtime 程序集，不下载依赖或加载游戏类型。先验证全部必需声明与继承链，读前后 hash 相等后才原子替换报告；失败不把旧报告解释为 fresh。

实际执行输出为：4 个程序集、24 个类型、Entity 的 5 层继承、13 个 instance direct 字段、3 个 static 资源字段、3 个 Parent mutable 引用候选、5 个 closed metadata contexts，`MissingTypes=[]`。PowerShell 解析错误为零；`GameCodeExecuted=false`、`SavesReadOrModified=false`、`NativeHooksInstalled=false`。原报告为 `.local/analysis/guest-ingredient-api.json`，只留本机；公开文档不包含原始 IL、地址或 DLL。

closed contexts 是对 generic definition 的类型参数做声明替换，没有创建或调用具体泛型对象。Cecil 的 wrapper 签名、field proxy 和 CLR value-type 分类不证明 native 布局、数组 boxing、运行 alias、构造副作用或完整图。

## 原缓存和条目

`IngredientsStorage : SingletonNoMono<IngredientsStorage>` 仅有两个可读写的 direct 字段：

| 字段 | 精确类型 |
| --- | --- |
| `m_Storage` | `Il2CppSystem.Collections.Generic.Dictionary<int,IngredientsData>` |
| `m_IsLoaded` | `bool` |

`IngredientsData` 的 11 个 direct 字段全部可读写：

| 字段 | 精确类型/复制边界 |
| --- | --- |
| `ingredientsID`、`level`、`parentID`、`rank`、`placeTagMask` | `int`，分别保留实际值；字典 key 不强制等于这些值 |
| `type` | `IngredientsType`，按值保存 |
| `isNew` | `bool` |
| `lastGainTime`、`lastGainGameTime` | `Il2CppSystem.DateTime`，本机 metadata 中是真 CLR struct，base 为 `System.ValueType`，有 public readonly `ulong _dateData`；按值复制，不调用日期 getter |
| `counts` | `Il2CppStructArray<int>`，准备独立数组并复制实际全部元素，不能沿用 original 数组 |
| `_Entity_k__BackingField` | `DR.IngredientsEntity`，可准备独立的已声明实例字段副本，不能假定原 Entity 是共享只读资源 |

已声明 `IngredientsData(int ingredientsID)` 和 `IngredientsData(IntPtr)`。前者分配对象并调用原构造，后者只是包装现有对象；没有 copy 构造。既有原 PE 静态报告找到前者到 `DataManager.GetIngredients(int)` 的唯一业务目标，不能据此声称无其他副作用。准备副本时即使覆盖其 Entity 字段，也不能消除构造过程中可能触及的共享资源。

`Il2CppStructArray<int>` 的可编译分配声明为 `.ctor(long size)` 或 `.ctor(int[] arr)`，另有仅 wrap 的 `.ctor(IntPtr)`；indexer 使用 `int` index，来自 Interop.Runtime 数组 API，非游戏业务 getter。长度与元素实际读取/写入仍涉及 native 内存，构造和数组 ABI 未实机。不要根据 SushiBar.Place 枚举擅自改 counts 长度；Mod 配额与游戏真实容量应分开。

公共 `IngredientsData.Entity`、ParentTID、Price、IsNotSale、TotalCount、SetCount、UpdateSaveData 等入口会调用原方法，不用于复制或恢复。Storage.Init/Load/Reset/Clear 同样不能作纯字段恢复：既有静态 Init 路径还修改保存时间，详见 [GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)。

## Entity 的十三个实例字段

真实继承链为 `DR.IngredientsEntity → DR.Ingredients → DR.DesignSheetDataHelper<int,DR.Ingredients> → DR.BaseSheetDataHelper → Il2CppSystem.Object`。本次生成 metadata 找到下列全部已声明 instance direct 字段：4 个 int、4 个 string、4 个 bool、1 个 enum。

| 所属类型 | 字段 | 类型 |
| --- | --- | --- |
| `DR.IngredientsEntity` | `_ItemsTID_k__BackingField` | `int` |
| `DR.Ingredients` | `_TID_k__BackingField` | `int` |
| 同上 | `_Type_k__BackingField` | `int` |
| 同上 | `_NameID_k__BackingField` | `string` |
| 同上 | `_DescriptionID_k__BackingField` | `string` |
| 同上 | `_IsUse_k__BackingField` | `bool` |
| 同上 | `_NotUseParentTID_k__BackingField` | `bool` |
| 同上 | `_TIDNumberConnect_k__BackingField` | `int` |
| 同上 | `_DeliverableToBranch_k__BackingField` | `bool` |
| 同上 | `_MaterialColor_k__BackingField` | `string` |
| 同上 | `_IsCompoundable_k__BackingField` | `bool` |
| 同上 | `_ContentsThumbnail_k__BackingField` | `string` |
| 同上 | `_CategoryType_k__BackingField` | `DR.IngredientsCategoryType` |

public `DR.IngredientsEntity()` 确实存在，可作为逐条分配并复制这十三个 direct 字段的有限准备入口；它会分配并调用原构造，元数据不能证明构造纯净。`DR.IngredientsEntity(IntPtr)` 不复制。此实例已声明字段没有 mutable collection，但字段有 setter，整个 Entity 不能据名称当只读对象。独立 Entity 顶层指针及十三字段回读比沿用 original Entity 指针更明确；仍只证明已声明实例部分，不能升格完整资源隔离。

上层另外三个 direct 字段全部是 static：`DR.DesignSheetDataHelper<int,DR.Ingredients>._data : Lazy<Dictionary<int,DR.Ingredients>>`，以及 `DR.BaseSheetDataHelper.DefaultDirectory : string`、`_onInit_k__BackingField : Il2CppSystem.Action`。复制十三实例字段不会复制这些全局表、lazy factory 或委托；不能读 Lazy.Value、调用 LoadData/Find/Init 来证明它们安全。

`DR.IngredientsEntity.Parent : DR.Items` 是 RuntimeInvoke getter，没有直接 Parent backing field。Icon/Name/Description/PathFormat/MaxCount 等同样是业务 getter；本轮不调用它们，也没有证明原 getter 的全部查表和资源路径。`DataManager._IngredientsDataDic_k__BackingField : Dictionary<int,DR.IngredientsEntity>` 本身为 direct 共享目录，GetIngredients/ParsingIngredientsEntity 为原方法。

`DR.Items` 明确有三个可变的 direct `List<string>`：`__unlockConditionTypeList_k__BackingField`、`__unlockConditionValueList_k__BackingField`、`__unlockConditionDetailValueList_k__BackingField`（名称以两个下划线开头）。它还有自身 scalar/string 字段及同一表 helper 体系。Entity 独立实例不隔离 Parent 查表结果、Items lists 或全局表；不能把“复制十三字段”描述为整个资源图只读/独立。

## Dictionary 的具体声明

`Dictionary<int,IngredientsData>` 的 direct proxy 包括：

| 字段 | 声明 |
| --- | --- |
| `_buckets` | `Il2CppStructArray<int>` |
| `_entries` | `Il2CppReferenceArray<Dictionary<int,IngredientsData>.Entry>` |
| `_count`、`_freeCount`、`_freeList`、`_version` | `int` |
| `_comparer` | `IEqualityComparer<int>` |
| `_keys`、`_values` | 对应 KeyCollection/ValueCollection |
| `_syncRoot` | `Il2CppSystem.Object` |

Entry 的四个 direct 字段为 `hashCode : int`、`next : int`、`key : int`、`value : IngredientsData`。Entry 继承 `Il2CppSystem.ValueType`，本机 CLR metadata 的 `IsValueType=false`，与 DateTime 的真 CLR struct 不同。native array 索引读取 Entry 可产生临时 native box，不能把此 box 当持久图节点，也不能据它是“值类型”省略 value 引用。

新 dictionary 可使用无参/capacity 构造及 `Add(int,IngredientsData)`；这些调用原泛型容器方法。`new Dictionary(original)` 会沿用条目引用，不能作独立 cache clone。捕获用 direct 数组/字段，检查实际 count/free/version、所有容量槽及前后身份，不以 Count/indexer/枚举业务方法代替。未使用槽和 bucket 读取也必须计入有界工作预算。

comparer、views 和 syncRoot 是声明中的附加引用。没有证明任意自定义 comparer 或已有 views 都不可变，不能默默分享原 comparer/旧 KeyCollection/ValueCollection，或只扫 entries 就声称字典全图完整。新 dictionary 的默认资源、闭合泛型实例和数组 ABI 仍需实际验证；本轮报告不会调用这些对象。

## null 与 loaded=false

metadata 不证明 singleton、dictionary、counts 或 Entity 在任何时刻都非空，也不证明 loaded=true 才存在原 cache。捕获应记录真实状态，不能用调用 Instance/Init/Load 的方式补齐缺失对象。

- singleton 为 null：当前没有可绑定 cache owner，拒绝安装，不创建 singleton。
- `m_Storage` 为 null 且 loaded=true：已加载图不可读，拒绝；不能补空字典掩盖缺失。
- `m_Storage` 为 null 且 loaded=false：只能记录原 cache 缺席及原 false；首个非空图实现可明确报未支持并拒绝。若未来支持该状态，必须按两个字段的实际身份保存、安装和恢复 null/false，不能默认为初始化成功。
- dictionary 非空、loaded=false：可以研究复制其已观察条目，但不得将 loaded 改成 true 或认为初始化已完成。若 prototype 要求已加载图，应明确拒绝这个原始状态。空的非 null dictionary 与 null 是不同身份，不能合并。

counts/Entity 缺失、未知资源类型、配额或读取失败也应明确拒绝，不调用公共 getter补值。字典与 loaded 是两个独立 native 字段；逐字段单次派发、回读、Mixed/Unknown 与逆序恢复仍按实际 cache 桥合同实现，不能当原子交换。

下一验证应先核对 owned original snapshot 在原 serializer 前冻结，准备所有独立条目、counts、Entity 十三字段，再检查 original 已知图未变化且跨侧 mutable 引用无交集；父资源、旧 UI/closure 引用、任务图、在途工作与完整输出覆盖继续保留未知。进入/退出边界与客机权限保持关闭，个人背包和员工返航收益不由食材 cache copy 授权。
