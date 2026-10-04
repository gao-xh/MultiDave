# 客机原生根影子桥

0.1.18-dev 新增 [NativeGuestShadowBridge](../src/DaveCoop/Networking/NativeGuestShadowBridge.cs) 和纯 CLR 的 [GuestShadowTransaction](../src/DaveCoop/Core/Guest/GuestShadowTransaction.cs)。桥包含实际的原生序列化、强引用、直接根交换、回读及恢复代码；当前没有接入 Network、GUI 或游戏生命周期，未调用这些原生操作。接口研究及其静态证据见 [GUEST_ISOLATION](GUEST_ISOLATION.md)，已枚举输出的围栏范围见 [GUEST_OUTPUT_FENCE](GUEST_OUTPUT_FENCE.md)。

当前生产桥的 `CanEnterBoundary()` 和 `HasQuiescentBoundary()` 始终返回 false。事务在安装围栏之前先核对实际进入边界，因此普通 `Install()` 会拒绝进入，保持零 patch、零原根捕获、零显式 GC handle。桥的安装围栏、捕获、准备和安装根入口自身也重新检查进入边界，不能通过直接调用这些 primitive 绕过事务。房间、加载标记或已知 writer 计数为零均不代替真实原生边界。

`GuestStateIsolated`、`NativePermission`、`WorldAuthority`、`CargoAuthority`、`NativeCloneAbiVerified`、`DeepCloneVerified`、`RuntimeCachesIsolated` 和 `InteractionSynchronized` 全部固定为 false。本页描述可审查的源码合同，不表示客机存档隔离、地图采用或个人袋结算已完成。

## 固定租约与五个根

构造器必须运行在已确认的 Unity 线程，固定 `UnityThreadId`、本地 `HostBindingId` 和单次 `LeaseId`；不能重新绑定同一个 bridge。`IGuestShadowBackend` 的事实由实际桥实例提供，不接受调用者传入“隔离成功”标志。

`TryClaimLease()` 仅消耗这个实例，不保留全局 inactive owner。首次真正尝试安装围栏前，通过单一进程 owner 的 CAS 强持有整个 backend：它同时保留围栏、原根、shadow、回读身份与恢复记录。失败或未知恢复不能只留下孤立的 handle 数字。只有确认安全释放全部引用后才能清理这个强 owner；在当前前置拒绝路径不会占用它。

保持现有 SaveSystem 和四个 manager，交换下列生成的 native 字段代理：

| 根 | 实际 manager 字段 | detached 数据 |
| --- | --- | --- |
| GameData | `SaveSystemGameDataManager._Data_k__BackingField` | `SaveData` 原生 JSON round trip |
| PlayerData | `SaveSystemPlayerDataManager._Data_k__BackingField` | `SavePlayerData` 原生 JSON round trip |
| PlayerInteraction | `SaveSystemPlayerDataManager._InstanceData_k__BackingField` | 新 `InstanceInteractionData(false)`，0.1.19 以类型化字段绑定 detached Player 的十组容器及新 IGP HashSet |
| PhotoData | `SaveSystemPhotoDataManager._Data_k__BackingField` | `SavePhotoData` 原生 JSON round trip |
| UserOption | `SaveSystemUserOptionManager._Data_k__BackingField` | `SaveUserOptions` 原生 JSON round trip |

bridge 从 `Singleton<SaveSystem>._instance` 及 SaveSystem 的四个直接 manager 字段捕获身份。回读同时核对 native 对象指针和 Unity 对象直接 `m_CachedPtr`，拒绝已销毁或替换的 manager。公共 `Data`、`Instance`、`SetLoadedData()`、`LoadData()` 和 Sync 均不用于交换或恢复；直接字段代理仍是 native 内存访问，必须在正确线程及本租约围栏内执行。

## 克隆、内存与强引用

四种 Data 使用实际包装器 `SaveDataBase.Serialize<T>(original)` 和 `SaveDataBase.Deserialize<T>(SaveDataType,json)`；依次使用 `GameData`、`PlayerData`、`PhotoData`、`UserOption` 枚举。每个顶层返回对象必须非空且与所有已持有对象指针不同，并核对 Version。每个 native step 前后检查 manager、原根、输出围栏及当前进入边界。

临时 JSON 只在内存保留，不进入日志、网络或文件。限额按照 `string.Length`，即每根最多 8 Mi 个 UTF-16 代码单元、单租约累计最多 16 Mi 个 UTF-16 代码单元，四次克隆不重置预算。没有按 UTF-8 bytes 计量；若转成 UTF-8，其上界可按每代码单元三 bytes 估算，但桥不执行此编码。native Serialize 返回之后才能检查长度，这不能限制其内部临时分配或超限返回字符串的初始分配。

bridge 最多独立拥有 15 个 native 强 GC handle：SaveSystem 加四个 manager、五个原根、五个 detached 根。使用已安装 Il2CppInterop 的 `IL2CPP.il2cpp_gchandle_new(pointer,false)`；handle 是 `IntPtr`，不是 uint。获取后先记录所有权，再以 `il2cpp_gchandle_get_target` 回读，不以普通数字指针证明存活。`false` 表示不 pin，仍是强 handle。

生成 wrapper 本身也有强 GC handle；桥额外拥有的 handle 用于明确 lease 生命周期，只释放自己的句柄，不接触 wrapper 私有 `myGcHandle`。本机 IL 与同 commit 官方源码已核对：[Il2CppObjectBase](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.Runtime/InteropTypes/Il2CppObjectBase.cs)、[IL2CPP GC API](https://github.com/BepInEx/Il2CppInterop/blob/dbda1cb353b0f4253345dc45136d170b9e50a5a0/Il2CppInterop.Runtime/IL2CPP.cs)。这项框架证据不证明四个 closed generic 克隆的游戏行为或 ABI 已通过实测。

桥没有把原 Interaction 的缓存引用复制到新对象，也没有调用 Sync；构造器是否隐式复用状态仍未实测。它目前只是新建临时对象，不能证明与 player shadow 的交互状态一致。顶层指针不同、版本相同和序列化可编译都不证明可变子树深复制、Obscured 私有状态完整、旧 saveable/delegate/iterator 脱离或运行缓存隔离。

## 回读、补偿与保留

每个根的安装和恢复写入均 single-use：在派发 native 字段写之前记录 attempt，随后回读 `Original`、`Detached`、`Foreign` 或 `Unknown`。事务逐根逆序补偿：已是 Original 不重复写；只有本租约确切 Detached 才恢复原引用；Foreign、Unknown 或 manager 改变时不盲目覆盖。false/异常不能推断写入未发生，后续只能回读已派发的写入，不能重派发。

bridge 捕获四个 manager 的 `IsNewData` backing field，以及四个原 Data 的 Version、BuildVersion、lastUpdateLocalTime、IsUpdated 和 corrupted 直接标量。准备、安装、校验和最终原根确认会检查这些值未变；变化时拒绝，不把 dirty flag 强行清回旧值。已知 Detached 仍可尝试恢复原指针，随后变化的标量会阻止“原进度已恢复”确认。这些有限检查不能证明整个原子树或缓存从未被旧引用修改。

恢复原根后仍须确认原 managers、原 roots、围栏健康及真实原生静止边界，才允许移除自己的围栏及释放自己的 handles。当前没有完整静止边界，`HasQuiescentBoundary()` 固定 false，因此源码不能据原指针匹配就释放。未来若发生不完整恢复，backend 强 owner、仍拥有的 handles 和围栏必须保持；没有自动 Dispose/finalizer 把保存放开。

释放逐 handle 先记录 attempt，native free 确实返回后才计为已释放；异常不会无条件标记 lease released，也不会重复 free 未知句柄。围栏卸除同样核对 inactive 和自己的 hooks 已移除。固定诊断不转发原异常 Message，避免其中可能的 JSON 泄漏。

## 下一次实际进入前的证据

必须先证明原游戏尚未创建员工世界对象且旧 writer、load、cache、delegate 和 coroutine 不会改原进度的真实进入边界，并证明退出时原生静止边界；仅新增 caller gate 不够。还需验收四个具体 T 的 native round trip、完整 mutable 子树与 Obscured 状态、Interaction 重建和双向缓存/反向引用，以及全部持久输出和阻断 ABI。围栏枚举清单不是所有输出覆盖证明。

通过这些核对后才能安排受控 native 安装与失败恢复测试，随后另接游戏生命周期。RootShadowInstalled 仍与 GuestStateIsolated 分开；影子桥不授予 guest 原生动作或收益权限。每个人独立容量/重量、房主原生袋与员工临时袋的分流和返航一次入仓，继续按 [CREW_MODE](CREW_MODE.md) 的真实产物与 receipt 合同实现，不能用根交换或保存 bool 当作捕获/结算证据。

## 0.1.19 交互准备的具体接线

空的临时Interaction已改为[typed helper](GUEST_INTERACTION_SHADOW.md)：拒原dirty，比较十组已知Player/Interaction内容，绑定detachedPlayer、新建IGP hash，并审计已核对的可变容器/数组/记录。准备及逐根安装/验证再次核对已知绑定；恢复原Interaction仍只按保存的原引用，不调用Sync或Load。

完整source baseline/可变图、泛型数组/Entry ABI、其它运行缓存与输出/静止未验证。准备窗口仍恒false，未执行此helper或任何native；总170项中的三项新测试只执行生产CLR引用审计，不执行native图读取或容器构造。
