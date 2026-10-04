# 游戏内临时保存缓存的精确接口

`IngameSaveDataManager` 的实际 singleton direct proxy 是 `SingletonNoMono<IngameSaveDataManager>._s_Instance_k__BackingField`，其缓存字段是 `ingameSaveDatas : Dictionary<InGameSaveType,InGameSaveData>`。没有 `_instance`；公共 `Instance/s_Instance/hasInstance` 调用原方法，不能用它们补建或捕获缓存。

本页是 0.1.21-dev 的离线声明证据，供 typed 缓存准备、读取与恢复实现使用。本轮没有运行字段代理、类型检查、分配、构造、容器、业务 getter、hook 或游戏，没有读取存档。完整缓存、资源图、原生 ABI、进入/退出静止边界及全部客机权限仍未成立。

## 可复现检查

运行 [Inspect-GuestIngameApi.ps1](../scripts/Inspect-GuestIngameApi.ps1)：

```powershell
.\development\scripts\Inspect-GuestIngameApi.ps1
```

可指定 `-GamePath` 和安全文件名 `-ReportName guest-ingame-api.json`，输出固定在忽略的 `development/.local/analysis/`。脚本通过 Steam 定位游戏，只用已安装 Cecil 读取生成/runtime 程序集；不下载依赖，不加载游戏类型。全部输入读前后 SHA256 一致、必需声明及六种派生类型核对通过后，才原子替换报告。失败保留的旧报告不代表本次成功。

实际执行为 5 个程序集、89 个类型、6 个 `InGameSaveData` 派生声明、34 个 SpecContainer 派生声明、181 条 direct instance child edges、8 条明确的外部/资源 frontier、11 个 closed metadata contexts，`MissingTypes=[]`，PowerShell 解析错误为零。报告为 `.local/analysis/guest-ingame-api.json`，仅留本机；`GameCodeExecuted=false`、`SavesReadOrModified=false`、`NativeHooksInstalled=false`。公开文件不包含原始 IL、native 地址或第三方 DLL。

closed contexts 只对泛型声明替换参数，没有创建具体泛型实例。type catalogue 和 frontier 是有界声明研究，不是完整可变对象图，也不是当前运行 alias 的证明。

## 六种保存记录

`InGameSaveData : Il2CppSystem.Object` 没有已声明 instance direct 字段。本次 metadata 的全部派生类型恰为以下六种，每种均直接继承此基类：

| `InGameSaveType` 实际键 | 类型 | 全部已声明 instance direct 字段 |
| --- | --- | --- |
| `CharacterHealth=0` | `CharacterHealthData` | `HP : float` |
| `CharcterEquip=1` | `CharacterEquipData` | `_currentEquipInInventory_k__BackingField : List<int>`、`gunAmmo : int` |
| `CharacterSubHelper=2` | `CharacterSubHelperData` | `_subHelperSlots_k__BackingField : Il2CppReferenceArray<SubHelperSlotData>` |
| `CharacterInstallDevice=3` | `CharacterInstallDeviceData` | `_currentInstalledDevices_k__BackingField : List<InstallDeviceSaveSlot>` |
| `PuzzleState=4` | `PuzzleStateSaveData` | `_keyToSolves : Dictionary<string,bool>` |
| `InGameObject=5` | `InGameObjectSaveData` | `_keyToDatas : Dictionary<string,InGameObjectSaveData.Data>` |

`CharcterEquip` 是原枚举的真实拼写。六种记录都为 CLR class wrapper，不是 CLR struct。保留实际 key 和每种完整已声明内容；不能用空顶层 dictionary 替代原非空状态，也不能以只复制已熟悉的一种类型宣称六种已支持。未知 enum、key 与实际记录类型不符、未知派生或不可读字段必须明确拒绝。

已声明业务构造如下；所有类型另有仅 wrap 的 `.ctor(IntPtr)`：

| 类型 | 业务构造参数 |
| --- | --- |
| 基类 `InGameSaveData` | 无参 |
| Health | `float` |
| Equip | `Dictionary<EquipmentType,SpecDataBase>, int`，没有无参业务构造 |
| SubHelper | `Il2CppReferenceArray<SubHelperSlotData>` |
| Install、Puzzle、Object | 无参 |

这些生成构造调用 `il2cpp_object_new` 和原构造，不能据签名称为无副作用 copy。`IntPtr` 构造不深复制；它仅包装一个已存在指针，框架还会持有自己的 native GC handle。

## 槽和对象条目的完整已声明字段

`SubHelperSlotData : Il2CppSystem.Object` 有七个 writable direct 字段：

| 字段 | 类型/边界 |
| --- | --- |
| `subHelper` | `SubHelperSpecData`，带 Unity 资源子图 |
| `remainCount` | `int` |
| `remainTime`、`lastActiveTime`、`unfocusdTime` | `float`，`unfocusdTime` 按真实拼写 |
| `isAvailable` | `bool` |
| `gearQueue` | `Queue<IInstalledDevice>`，可包含正在使用的原生设备 |

Slot 有无参业务构造及 `IntPtr` wrapper 构造。五个 scalar 不覆盖另外两个 mutable 引用；不能保留原指针或把它们强行置空来生成貌似独立的槽。

`InstallDeviceSaveSlot : Il2CppSystem.Object` 的全部三个 direct 字段为 `_DeviceType_k__BackingField : InGameSaveInstallDeviceType`、`_UID_k__BackingField : string`、`_InstalledPosition_k__BackingField : UnityEngine.Vector3`。业务构造是 `(InGameSaveInstallDeviceType, Vector3, string)`，另有 `IntPtr`。Device 枚举为 CargoBox=0、SensorBomb=1、SensorNet=2。本机 Vector3 是真 CLR struct，public instance `x/y/z : float`，可研究逐值保留；声明不证明游戏构造或字段 ABI 已实测。

`InGameObjectSaveData.Data : Il2CppSystem.Object` 的全部两个 direct 字段为 `key : string`、`isSaved : bool`；业务构造 `(string)`，另有 `IntPtr`。dictionary key 与 Data.key 分别保存原值；不擅自改名或用其中一项覆盖另一项。未知子类同样不能只读两字段后称完整。

## 助手资源与设备是明确边界

`SubHelperSpecData` 直接继承 `Sirenix.OdinInspector.SerializedScriptableObject`，并非 `SpecDataBase`。自身 direct 字段为 `_TID:int`、`_Name:string`、`_SubHelperType:SubHelperType`、`_AnimType:SubHelperAnimType`、`_UseHoldTime:float`、`_SubHelperCount:int`、`_ActiveSoundID:string`、`_FailEmojiType:EmojiType`、`_FailAniKey:string`、`_BuffTID:int`、`Containers:List<SpecDataContainerBase>`。公共 TID/Name/各 IsType/GetContainer 等不是用于补字段的纯 getter。

继承链继续为 SerializedScriptableObject → `UnityEngine.ScriptableObject` → `UnityEngine.Object`。其中 `serializationData : Sirenix.Serialization.SerializationData` 又是 native ValueType wrapper，包含 SerializedBytes、UnityObject lists、Prefab、PrefabModifications 和 SerializationNodes 等 mutable 引用。因此拷贝十一顶层字段也不隔离这个资源，不能使用普通记录的 `object_new` 候选假造有效 Unity 引擎对象。

SpecDataContainerBase 的声明 catalogue 本次为 34 个派生类型；递归子字段还可通向 ResourceConsumeData lists、其它容器、AnimationCurve、GunSpecData、Command_SO 等。报告明确保留八处外部或资源 frontier，不把 catalogue 视为已实现的全图。未知运行子类、引擎对象和未覆盖的子图仍拒绝。

`IInstalledDevice` 在生成 metadata 中是继承 `Il2CppObjectBase` 的 CLR class wrapper，`IsInterface=false`；这不证明原生接口不是 interface。它没有可复制的 instance direct 字段，UniqueID/DeviceTID/DevicePosition/DeviceTransform 和 Init/Remove/Restore 等会调用原方法。设备/Transform 不能由空记录或原 wrapper 共享代替。

未实现这些桥时，可以准确保留原 `subHelper=null` 和真实 empty queue 的状态；非空 resource 或任一 live device 引用必须明确拒绝。empty queue 必须核对实际 size、head/tail、版本、全部 capacity slots 均无设备引用及前后身份，不能只看 size=0。拒绝原非空状态比清空它更能保留用户数据语义。

## 集合声明与完整扫描目标

三种 dictionary 使用同一具体声明布局：`_buckets : Il2CppStructArray<int>`、`_entries : Il2CppReferenceArray<Dictionary<TKey,TValue>.Entry>`、`_count/_freeCount/_freeList/_version : int`、`_comparer`、`_keys`、`_values`、`_syncRoot`。Entry 的 direct 字段为 `hashCode/next : int`、`key : TKey`、`value : TValue`；Entry 是继承 `Il2CppSystem.ValueType` 的 CLR class wrapper。

`List<T>` direct 字段为 `_items : Il2CppArrayBase<T>`、`_size/_version : int`、`_syncRoot : Il2CppSystem.Object`。`Queue<T>` 为 `_array : Il2CppArrayBase<T>`、`_head/_tail/_size/_version : int`、`_syncRoot : Il2CppSystem.Object`。不能将定义中的 ArrayBase 自动改写成已验收的 ReferenceArray；具体 T、runtime array API 和 boxing 路径需另核对。

新字典、List/Queue、struct/reference 数组的构造及 Add/Enqueue 均为原生调用候选。浅 copy 构造沿用原元素不构成隔离。直接读取时需核对 count/free/live slots、next/bucket 范围、全部 capacity/free/tail 值及版本和前后身份；扫描工作、重复引用、字符串及分配都要有独立限额。dict comparer/views/syncRoot 不是自动只读，不能漏扫后声称完整 graph。

## Obscured 的真实分类

六种根记录及上述三个普通 child 的 direct 字段未直接包含 Obscured 类型。工具额外记录相关类型布局，为资源递归和后续扩展区分真 CLR struct 与 native ValueType wrapper：

- `ObscuredInt`、`ObscuredBool` 为真 CLR struct，均有 currentCryptoKey、hiddenValue、inited、fakeValue、fakeValueActive 五个 instance CLR 字段；cryptoKey 是 static，不能复制为实例状态。
- `ObscuredVector3` 为真 CLR struct，hiddenValue 是真 struct RawEncryptedVector3 的三个 int，fakeValue 是 Vector3；其余 currentCryptoKey/inited/fakeValueActive 也须完整保留。
- `ObscuredFloat` 的 CLR `IsValueType=false`，继承 `Il2CppSystem.ValueType`，是 wrapper。direct 字段为 currentCryptoKey:int、hiddenValue:ACTkByte4、hiddenValueOld:Il2CppStructArray<byte>、inited:bool、fakeValue:float、fakeValueActive:bool。旧 byte array 是可变引用，不能将这个对象当 float 按值共享。
- ACTkByte4 为真 CLR struct，四个 byte 字段；名字包含 ValueType 不足以判断是否 CLR struct。

本轮不解密、转换、调用随机 key、Encrypt/Decrypt/implicit operator 或 getter，也不从解密值重新构造加密对象。上述 wrapper 分类和字段范围仍不证明原生对象布局、boxing 或全部私有状态。

## exact 类型与分配候选

本机 Il2CppInterop.Runtime 声明为：

| 成员 | 精确返回/参数类型 |
| --- | --- |
| `IL2CPP.il2cpp_object_get_class` | `IntPtr (IntPtr obj)` |
| `IL2CPP.il2cpp_object_new` | `IntPtr (IntPtr klass)` |
| `IL2CPP.il2cpp_class_is_assignable_from` | `bool (IntPtr klass, IntPtr oklass)` |
| `Il2CppClassPointerStore<T>.NativeClassPtr` | `IntPtr` 字段 |

前三个是 PInvoke 声明，NativeClassPtr 是 public static writable 字段。TryCast<T> 的实际框架 body 使用 `il2cpp_class_is_assignable_from`，Cast<T> 调用 TryCast；它们接受可赋值派生类型，不能代替 exact class 检查。候选应在实际 bound window 内比较非零 `il2cpp_object_get_class(pointer)` 与该类型非零 NativeClassPtr，并在原生读/分配及回读前后复核来源；未知派生拒绝，不依赖 C# `is` 或 wrapper 名字。

读取 NativeClassPtr 也可能触发 generic store/type initializer。框架 generic initializer 调用 RuntimeHelpers.RunClassConstructor 与 native class/nested-type resolution，生成 wrapper initializer 也有 native 初始化路径；不是纯 CLR 无原生动作。因此不能提前在关闭边界外为“校验”读取 class store，再声称零 native。

对六种普通记录及普通 slot/Data，可研究 `il2cpp_object_new(exactClass)` → typed `IntPtr` wrapper → 完整 declared-field 复制。这条候选不派发原记录业务构造，但仍涉及 native allocation、class 初始化、wrapper handles、写入及回读；不能据可编译签名证明 ctor 被绕过后对象有效、所有字段已初始化或完整 clone。Unity 资源、live device、未知派生不适用这条普通记录候选。`NativeAllocationAbiVerified` 与完整资源/客机权限保持 false。

## 缺席与恢复边界

manager singleton 为 null 时不调用 Instance 补建；当前无法绑定 owner，拒绝。`ingameSaveDatas=null` 与空 dictionary 分别记录真实身份；可捕获缺席，但首个非空图准备实现应明确拒绝无法支持的原 null，不填空假造状态。原非空 dictionary 必须完整支持全部实际键/记录，任何未知或未覆盖 child 使准备失败，不删掉那一项继续安装。

原 known baseline 应在任何 serializer 之前冻结，准备与安装比较原图及副本已知引用，活动核对允许 owned 副本合法变化；原图变化、资源未知、读取失败和配额不是 permission。这里只新增离线工具与接口文档；单字段安装、一次恢复、foreign/unknown 保留围栏与 handles、完整输出和真实静止边界由实际桥另行实现及验收。没有授予员工个人袋、捕鱼、入仓、任务或保存权限。
