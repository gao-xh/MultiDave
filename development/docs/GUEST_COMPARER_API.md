# 字典 comparer 的精确接口与独立候选

0.1.21 的 Ingame 缓存只接受 dictionary 的 `_comparer/_keys/_values/_syncRoot` 均为 null；非空 comparer 会明确拒绝。普通容量构造可能生成非空默认 comparer，因此拒绝可以出现在常见原表或新表。0.1.22 的有限独立候选见 [GUEST_DICTIONARY_COMPARERS](GUEST_DICTIONARY_COMPARERS.md)；未知比较器与未支持的辅助引用仍拒绝，不能通过清掉 comparer、忽略该引用或共享原对象消除限制。

本页给出 0.1.22 离线接口研究，为独立 comparer 准备提供可审查候选。没有运行 Default、CreateComparer、字段代理、构造、Equals/GetHashCode、分配、hook、游戏或存档。没有改变现有 helper、门禁或协议，全部进入/静止、native、guest、world 和个人袋权限继续关闭。

## 可复现范围

从仓库根目录运行 [Inspect-GuestComparerApi.ps1](../scripts/Inspect-GuestComparerApi.ps1)：

```powershell
.\development\scripts\Inspect-GuestComparerApi.ps1
```

支持 `-GamePath` 和安全文件名 `-ReportName guest-comparer-api.json`。脚本自动定位 Steam，用已安装 Cecil 读取 Il2Cppmscorlib、Assembly-CSharp 和 Interop.Runtime；不加载游戏类型，不下载依赖，不读取存档。只允许写入忽略的 `development/.local/analysis/`，全部输入读前后 hash 一致、必需声明和继承链核对通过后原子替换报告；旧报告不代表失败后的 fresh 成功。

实际执行结果为 3 个程序集、27 个类型、7 个 comparer 继承 family、19 个 closed metadata contexts、8 个 dictionary instance constructor，`MissingTypes=[]`，PowerShell 解析错误为零。原报告为 `.local/analysis/guest-comparer-api.json`；GameCodeExecuted/SavesReadOrModified/NativeHooksInstalled/DefaultSelectionVerified 均 false。公开文件不包含原始 IL、native 地址或第三方 DLL。

报告记录全部已声明 CLR instance/static 字段及 native field proxies；生成 NativeFieldInfoPtr/NativeMethodInfoPtr 仅记录声明，不读其运行值。closed contexts 是类型参数的元数据替换，不创建闭合泛型，不证明约束满足、native AOT 实例存在或正确构造。

## 泛型 comparer 的继承与字段

准确命名空间为 `Il2CppSystem.Collections.Generic`：

| 类型 | 直接基类 | 已声明 native instance 字段 | 业务构造 |
| --- | --- | --- | --- |
| `EqualityComparer<T>` | `Il2CppSystem.Object` | 无 | 无参 |
| `GenericEqualityComparer<T>` | `EqualityComparer<T>` | 无 | 无参 |
| `ObjectEqualityComparer<T>` | `EqualityComparer<T>` | 无 | 无参 |
| `EnumEqualityComparer<T>` | `EqualityComparer<T>` | 无 | 无参，另有 serialization 参数构造 |
| `SByteEnumEqualityComparer<T>` / `ShortEnumEqualityComparer<T>` | `EnumEqualityComparer<T>` | 无 | 无参，另有 serialization 参数构造 |
| `LongEnumEqualityComparer<T>` | `EqualityComparer<T>` | 无 | 无参，另有 serialization 参数构造 |
| `NullableEqualityComparer<T>` | `EqualityComparer<Nullable<T>>` | 无 | 无参 |
| `ByteEqualityComparer` | `EqualityComparer<byte>` | 无 | 无参 |

这些包装器另有 `.ctor(IntPtr)`，只 wrap 原对象。Generic/Object 的 CLR generic parameter 没有约束；Enum 包装器只声明 `new()` 约束。原生泛型约束、哪些组合有 AOT 实例、Enum 的实际底层类型与默认选择分支不能从这些 CLR 约束推导。19 个 contexts 中的 `EnumEqualityComparer<string/int>` 仅为参数替换：C# string 不满足 `new()`，int 也不是 enum，均不作为合法构造或原生支持候选。helper 的 enum 白名单只限实际 `InGameSaveType`，其底型 `System.Int32` 已由字段元数据核对；仍未验证该闭型的原生构造或 Default 选择。

完整 generated 继承继续为 EqualityComparer → Il2CppSystem.Object → Il2CppObjectBase。上述 comparer/base 没有已声明 native instance field proxy，但框架基类有自己的 CLR wrapper/GC handle 状态。EqualityComparer<T> 明确有 static `defaultComparer : EqualityComparer<T>`，为可读写 native field proxy；它不是实例字段，也不能在克隆时改写或清空这个全局缓存。

没有实例 field 声明只限定已知实例复制范围，不证明原对象只读、无副作用、native stateless、没有隐藏字段/全局依赖或可安全共享。记录 Equals/GetHashCode 的 wrapper 为 RuntimeInvoke，仍可能读取其它资源、全局哈希状态或调用 T 的行为。未知 actual native subtype 必须拒绝，不能按名称后缀或 wrapper 的 C# `is` 放行。

## Default 和具体选择边界

| 入口 | 精确声明/分类 |
| --- | --- |
| `EqualityComparer<T>.defaultComparer` | static writable native field proxy，非 RuntimeInvoke getter |
| `EqualityComparer<T>.Default` | static getter，返回 `EqualityComparer<T>`，RuntimeInvoke |
| `EqualityComparer<T>.CreateComparer()` | static 原方法，返回 `EqualityComparer<T>`，RuntimeInvoke |
| `IEqualityComparer<T>.Equals(T,T)` | 返回 bool，原虚调用包装器 |
| `IEqualityComparer<T>.GetHashCode(T)` | 返回 int，原虚调用包装器 |

接口生成类型 `IEqualityComparer<T>` 继承 Il2CppObjectBase，在 CLR metadata 中是 class wrapper；它有 public `.ctor(IntPtr)`。非泛型 `Il2CppSystem.Collections.IEqualityComparer` 同样具有 Object 参数的 Equals/GetHashCode 和 IntPtr wrapper 构造。这不证明原生接口不是 interface。

当前能定位的默认选择候选为 Default/CreateComparer 及各具体 family，没有读取原生选择主体或运行结果。不能确定本游戏的 int/string/InGameSaveType 实际默认类，也不能从通用 .NET 经验推断 IL2CPP 分支。对原 dictionary 应捕获它实际 `_comparer` 的 native 类型与身份；不能先调用 Default 来制造缺失的原 comparer，再称捕获了原状态。

`Il2CppSystem.Int32` 是真 CLR struct，instance `m_value:int`；InGameSaveType 是真 enum，底层 `value__:int`，键值见[GUEST_INGAME_API](GUEST_INGAME_API.md)。生成 `Il2CppSystem.String` 是 Object wrapper，direct `_stringLength:int/_firstChar:char`，静态 Empty；容器声明中的 `string` 则显示为 System.String 参数。此差异不证明两套 generic class store 可以任意互换。Int32/String/Enum 的 Equals/GetHashCode 声明已记录，未执行其 native 实现。

工具也记录 StringComparer、OrdinalComparer、OrdinalCaseSensitiveComparer、OrdinalIgnoreCaseComparer 和 CultureAwareComparer。OrdinalComparer 有 inherited `_ignoreCase:bool`，CultureAwareComparer 持 `_compareInfo:CompareInfo` 及 `_options:CompareOptions`；不是都无实例状态。不能把未知 string comparer 改成 GenericEqualityComparer 或 Ordinal 来假定语义等价。

## 独立具体 comparer 的最小候选

可研究的有限声明包括 `GenericEqualityComparer<int/string>`、`ObjectEqualityComparer<int/string>` 和 `EnumEqualityComparer<InGameSaveType>`。它们的无参业务构造均是 native allocation 加原 constructor；IntPtr 只是包装入口。候选列表不确定默认选择，也不自动允许所有 key/family 组合。

较窄的安装合同应先固定原 dictionary、实际 comparer pointer、exact class 和 key 类型，只接受明确实现的 finite family；再准备同一个 exact native class 的不同对象，保留新 wrapper/lease 引用，并重新检查原 comparer 与全部已读状态没有变化。无法证明 actual subtype、closed class pointer 或独立构造有效时拒绝。原 null comparer 单独保留，不凭 null 猜任意默认类或自动强行改成显式 comparer。

exact 校验候选为 `IL2CPP.il2cpp_object_get_class(pointer)` 与非零 `Il2CppClassPointerStore<ExactComparer>.NativeClassPtr` 相等。TryCast/Cast 使用 assignable 判断；只在 exact source/new class 已核对后，才可研究转换为 `IEqualityComparer<TKey>` wrapper 参数，并保持其 native pointer 等于新 comparer。接口包装不另生成 comparer，也不是 exact 类型证明。

普通 comparer 可研究 `il2cpp_object_new(exactClass)` → typed IntPtr wrapper → owned 强保留的准备路线，或经过已核对的无参业务构造。两者都涉及 class 初始化、native 分配、包装与持有；无实例字段不证明绕过 constructor 后不变式正确。object_get_class/class store/constructor 的读取与调用均应在实际 fresh 来源窗口前后核对，不能在默认关闭边界外提前执行初始化。

即使 source/new exact class 相同，也需继续验证哈希/相等语义及重建 dictionary 的行为，不能复用旧 buckets/hashCode 并假定新 comparer 会给同一结果。重新 Add 也会调用 native comparer；它不是纯字段复制。只读对比或少量样本不能证明所有 key、回调、副作用和完整 immutable 资源性质。

## dictionary 构造与先持有边界

精确 generic instance 构造声明包括：

```csharp
Dictionary<TKey,TValue>()
Dictionary<TKey,TValue>(int capacity)
Dictionary<TKey,TValue>(IEqualityComparer<TKey> comparer)
Dictionary<TKey,TValue>(int capacity, IEqualityComparer<TKey> comparer)
Dictionary<TKey,TValue>(IDictionary<TKey,TValue> dictionary)
Dictionary<TKey,TValue>(IDictionary<TKey,TValue> dictionary, IEqualityComparer<TKey> comparer)
Dictionary<TKey,TValue>(SerializationInfo info, StreamingContext context)
Dictionary<TKey,TValue>(IntPtr pointer)
```

报告给出 int→IngredientsData、InGameSaveType→InGameSaveData、string→bool、string→Object.Data 四个闭型声明上下文。容量构造和容量＋comparer 构造均会 allocation 和 RuntimeInvoke；不能认定前者不触及默认 comparer，也不能认定后者一定原样保留传入对象而不做其它工作。准备后应直接回读 `_comparer` pointer/exact class，核对 source/new 独立及 constructor 结果；读回未知保留失败状态，不把 comparer 字段手工改成 null。

`Dictionary(int,IEqualityComparer<TKey>)` 的 generated wrapper 调用顺序已离线核对：先 native object allocation，再 IntPtr wrapper，最后调用原 constructor 并处理 native exception。因此普通 `new Dictionary(...)` 整体抛出时，helper 的赋值/Hold 尚未完成；不能据代码右侧是 new 就宣称失败的新对象已归本租约强持有。

对应原 constructor 的 generated method-info 字段精确为 `NativeMethodInfoPtr__ctor_Public_Void_Int32_IEqualityComparer_1_TKey_0 : IntPtr`，访问属性是 **private static readonly**。这不是 public 构造方法，也不是可以直接写入的根。读取具体闭型私有 info cache 涉及反射和类型初始化，仍须单独 fresh 核对；原报告只记录声明，不读取值。

为在 constructor 进入前建立 owned 持有，可研究先 raw allocation→typed wrap/强保留，再只对这个新对象派发同一个 `(capacity, comparer)` native MethodInfo 的路线。它需要另证 unsafe 参数布局、exception-out 处理、实际 closed method pointer、初始化及失败后回读/保留。不能通过对 wrapper 再调用普通 new/CLR constructor 来假定不会第二次 allocation；不能重复未知原 constructor。当前未实现或运行这条 constructor-dispatch 桥。

## 后续证明范围

本 Inspector 只提供离线声明证据。0.1.22 的[有限源码](GUEST_DICTIONARY_COMPARERS.md)已将三种 key 的七个精确 comparer 声明接入 Ingredients 与 Ingame，在 Add 前注入独立同类候选并核对原/新 pointer、class、kind 和已知 static default 指针；实际构造、相等和哈希仍未执行。独立 finite comparer 支持也不证明 dictionary views、syncRoot、自定义 comparer、全局资源或其它缓存完成隔离，不能把 _keys/_values/_syncRoot 的非空状态顺便清掉。

真实 source/boundary、全输出及在途工作、资源/设备/actor/旧引用、原生分配与恢复 ABI 仍需完成。每人独立背包、容量与负重，房主唯一持久收益规则保持；房主地图采用、个人捕获/容量分流/逐项返航、真实双端、冷配置及完整M3—M7目标不会因默认 comparer 候选而缩减。DefaultSelectionVerified、ReadonlyComparerVerified、CompleteGraphVerified 和 GuestStateIsolated 均 false。
