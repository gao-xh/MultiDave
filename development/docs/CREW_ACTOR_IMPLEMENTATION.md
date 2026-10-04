# 员工实际 actor 实施路线

本页是 0.1.43 阶段的离线研究与下一轮源码方案，不是员工 actor 已实现或原生验证记录。这组研究只读取实际互操作声明和有限 PE 片段，没有执行游戏业务、启动游戏、读写存档或测试 actor。插件整体编译与 CLR/TCP 结果另见[本轮摘要](../logs/host-fish-visibility-build-verification.json)，不覆盖尚未实现的 actor。房主原角色保持原链；员工仍须拥有独立位置、输入、生存、装备、投射物、背包、容量和负重。完整 M3—M7 与真实双端验收没有因此完成。

建议先实现房主 Mod 创建和驱动的独立 `GameObject`、`Rigidbody2D`、`CapsuleCollider2D` 与自写 actor 组件，再接实际原生受伤/命中桥。使用原世界的碰撞查询和鱼生命周期，不复制整个 `PlayerCharacter`，不替换 `InGameManager` 的本地角色引用，也不把员工输入写进房主 `UserInput`。这是一条可以先交付真实移动和碰撞、再补原生战斗的生产路线；不是再新增一层未调用的权限模型。

## 原角色与武器为什么不能直接复制

本机实际主角色为 `PlayerCharacter : BaseCharacter`，运动组件为 `CharacterController2D`。本轮没有找到精确的顶层 `PlayerController`、`OxygenSystem`、`WeaponHandler`；这不表示游戏其他命名空间或类型不存在相关功能。

原 `PlayerCharacter.Awake` 与 `Init(Vector3, bool, FlipState)` 的已解码片段包含共享状态注册、事件/响应式订阅、装备与呼吸初始化，以及 `CameraManager.SetAttachAudioListener`、`StartDummyCameraTarget` 调用。`Init` 还接多个其他装备系统。克隆原对象后执行这些入口，会进入原共享初始化链；不能仅改 `customMoveInput` 就声称与房主输入、镜头、状态、存档引用分离。

原 `HarpoonWeaponHandler.FireWeapon()` 包含自身状态与后续虚派发。原 `HarpoonProjectile.CollisionDetection(GameObject, Vector2)` 的已知片段读取 handler、handler owner、碰撞许可和钩住状态，并进入输入反馈及多种回调。当前远端鱼叉头只是显示节点，不能启用其脚本或调用这些入口来获得一个独立员工武器。见 [鱼叉显示范围](HARPOON_VISUAL.md)。

## 下一轮最小生产顺序

1. 新 `HostEmployeeActor` 由房主真实会话成员绑定创建。冻结当前实际 scene、入海生命与本地角色来源，创建自己的物理根和驱动；房主选择可用出生位置，使用实际碰撞查询检查空间。
2. 增加员工输入通道。客机发送移动/瞄准/按键意图，房主固定步长执行运动、氧气和冷却，并发布 actor 状态；客机只显示/预测再校正。先完成移动和环境碰撞，不以收到的姿态直接搬动物理根。
3. 新 Mod 投射物具有房主生成的身份、真实位置、运动与扫掠碰撞。命中反查实际 `Damageable`、鱼实例和 lifecycle，再调用一次受支持的原生伤害入口。
4. 员工自身受伤通过自写组件的原生 `IDamageable` 回调承接，更新自己的 HP/氧气状态；原生接口注入和值包装参数 ABI 必须验证。鱼的敌意/目标选择随后接实际第二 actor 来源，不能以新增碰撞体自动视为已完成。
5. 捕鱼完成另接现有选择、产物规范化与账本生命周期。真实伤害或一次 bool 返回不能代替完整捕获、任务、图鉴、完整产物或返航保存证据。

上述是待实现文件的职责安排，本页没有新增这些生产文件、网络消息或执行入口。

## 身份、输入与真实位置

actor 固定绑定实际 `SessionPeer`、Room、握手成员、scene epoch、实际 native scene handle、actor 生命和装备 revision。Room 或 Ready 本身不能证明一个原生 actor 已存在。持有该 actor 的实例才可驱动物理根；换层、失源、断线停止新的输入/射击派发，旧已经进入的捕鱼/返航操作仍保留在潜水账本中，不因 actor 消失而补奖或重试。

现有 `FishActionRequest` 已有 `RequestId`、`PlayerId`、`SceneEpoch`、`SceneKey`、`EquipmentSlot`、`LoadoutRevision`、瞄准向量及动作类型；它没有连续移动输入。`ReceivedFishAction` 的 Room/member/sequence/time 来自实际握手与包封装。后续 `CrewInput` 应沿同样来源记录，增加移动轴、瞄准轴、按键边沿/保持、输入 sequence 和 actor revision，设队列/速率/过期预算。一次开火边沿只消费一次，不能把每帧 held 状态变成重复操作。

当前 `PlayerFrame` 与 `HostFishInterestSource` 的远端位置是观察/显示来源，不是房主实际 employee 身体、命中位置或受伤许可。兴趣区域可继续消费显示位置；可信战斗距离必须来自新 actor 的房主物理状态。网络不接收客机声明的伤害、最终命中、HP、氧气、产物或 native pointer。

## 运动与碰撞参数来自实际世界

实际 `PlayerCharacter._Controller2D_k__BackingField` 指向 `CharacterController2D`；后者的 `m_Rigidbody`、`m_CharacterCollider`、`m_DynamicCollider` 为直接字段代理。可在已确认 Unity 线程、当前 scene/角色前后核对下，读取实际物理配置作为有限模板。新 actor 复制支持的尺寸、offset、trigger 策略和刚体配置值，保持自己的组件与引用；不复制原脚本、tag 驱动行为、UnityEvent 或角色状态树。

collision layer 不能硬编码。应读取实际相关 player/collider 所属对象的 layer，并以 `Physics2D.GetLayerCollisionMask(int)` 获取当前碰撞矩阵；移动阻挡和伤害目标分别建立明确的过滤策略。原 trigger、ignore pair、子 collider 与角色业务判断仍须核对，不能把“同 layer”描述成与原角色所有碰撞/AI 行为等价。有限模板不支持的 collider 形状或角色配置应明确报不支持，而不是猜一个默认层/尺寸。

本机已核的物理声明：

| 用途 | 精确声明 |
| --- | --- |
| 房主物理步进 | `Rigidbody2D.MovePosition(Vector2) : void` |
| 身体扫掠 | `Collider2D.Cast(Vector2, ContactFilter2D, Il2CppStructArray<RaycastHit2D>, float, bool) : int` |
| 投射物扫掠候选 | `Physics2D.CircleCast(Vector2, float, Vector2, ContactFilter2D, Il2CppStructArray<RaycastHit2D>, float) : int` |
| 实际层矩阵 | `Physics2D.GetLayerCollisionMask(int) : int` |
| 过滤层 | `ContactFilter2D.SetLayerMask(LayerMask) : void` |

`ContactFilter2D` 和 `RaycastHit2D` 在本机声明中为真实 CLR struct。命中结果的 collider 查询仍涉及 Unity native 调用；内部 collider 数字不作为跨机身份。实际 typed 数组、查询 ABI、trigger/depth 设置和新组件生命周期尚未执行验证。

第一版可明确采用 Mod 自己的有限游泳规则：速度来自房主批准配置，固定步长进行 sweep/接触处理后 `MovePosition`，保持实际 scene 和 Z 约束。这不声称复制原 `CharacterController2D` 的全部摩擦、翻转、强制移动或动画逻辑。客户端姿态只用于显示，不能作为无碰撞的传送输入。

## 固定装备与生存状态

原装备有实际直接链 `PlayerCharacter.m_InstanceItemInven` → `InstanceItemInventory._currentEquipInInventory_k__BackingField : Dictionary<EquipmentType, SpecDataBase>`。`SpecDataBase` 是 `SerializedScriptableObject`，包含 `_TID`、`_Damage`、`_BuffIDs`、`elementType`、`buffEffects` 和资源引用；不能把原库存、handler 或可变资源整图直接共享给独立员工。

下一轮由房主批准固定装备 profile，冻结装备 TID、所需伤害/速度/冷却/弹药标量、buff ID、attack type 与来源 revision。未核对的装备类型、buff 资源或攻击类别不猜值；先支持明示有限 profile。读取前后核 source 和资源 identity/值，并保留需要继续调用的实际资源强引用。装备原业务 getter、constructor/class initialization 与 Unity 查询都不是纯 CLR 操作。

`PlayerBreathHandler` 的直接字段不仅有 `m_HP` 和计时值，还持有 `_player`、`m_CharacterStatus`、`m_Damageable`、UI 和回调。因此员工先用 Mod 自己的独立 HP/O2、消耗时钟和批准容量；不调用原 `PlayerBreathHandler.Init(PlayerCharacter)` 来拼装第二套共享 UI/状态。深度、氧气补充与环境伤害要读实际 employee 身体和受支持的世界来源，不能借房主深度或客机 HP。

这是明确的合作 Mod 规则，尚未证明与单机所有魅力、debuff、装备和事件逻辑等价。独立袋的重量、容量、负重也必须作用于自己的 actor；不能只独立记录库存却仍读取房主 LootBox 来决定员工速度/氧气。

## 从真实命中进入原生伤害

新投射物在房主拥有独立生命、攻击身份、位置、剩余距离、冷却/弹药与一次命中状态。连续 sweep 使用真实世界 collider；命中后沿实际组件/鱼已知 damageable 列表做有界反查，得到确切 `(scene epoch, entity ID, generation, native fish)`，并在原生派发前后复核 `FishLifecycleHooks`/`FishStateCapture` 当前绑定。不能用最近鱼、显示 sprite、客户端 target ID 或单次距离判断代替碰撞来源。现有 target registry 可复用身份机制，但还没有完整 collision→fish/body Damageable producer，下一轮需要实接。

受支持目标的最短原生候选是 `Damageable.TakeDamage(AttackData) : bool`。其已解码片段包含初始化、全局停伤状态、damageable checker、custom filter、gimmick、抗性/debuff 与 defender 回调；保留这个管线，不直接调用鱼 `OnTakeDamage` 绕过这些规则。实际派生鱼或 body damageable 的额外 override/profile 仍须核对。

已有攻击来源类型可复用为有限候选：

| 类型/接口 | 精确声明或直接字段 |
| --- | --- |
| `HarpoonIDamager : Il2CppSystem.Object` | public `.ctor()`；public `.ctor(IntPtr)` 只包装 |
| 该来源的真实 Transform | `_Owner_k__BackingField : Transform`、`_transform_k__BackingField : Transform`，均直接读写代理 |
| 攻击数据 | `AttackData(IDamager, Damager, int, AttackType, Il2CppReferenceArray<BuffDebuffEffectData>, int, string, bool)` |
| 命中入口 | `Damageable.TakeDamage(AttackData) : bool` |
| 原伤害源回调 | `IDamager.OnDoAttack(AttackData, DefenseData) : void`；`OnNonDamageableAttack(AttackData, Collider2D) : void` |

新 `HarpoonIDamager` 的 Owner/transform 应绑定自己的 actor/投射物，不能使用房主 handler、damager 或 Transform。构造返回后尽早强保留、exact native class 核对再作为接口包装；`IntPtr` 包装不是独立对象构造。普通构造抛错前 helper 可能尚未得到 wrapper，不能声称所有未知分配已保留。

`AttackData` 与 `DefenseData` 在本机是继承 `Il2CppSystem.ValueType` 的 CLR **class wrapper**，不是可以任意按值复制的 C# struct。构造、native unbox/参数 marshalling 和接口回调都需按实际 typed wrapper 处理。`Damager` 参数在声明中可传引用不表示任意目标管线允许 null；所需组件、checker、buff 与 attack type 要在支持 profile 内确认，不能以 null 或猜 enum 凑出看似成功的调用。

`HarpoonIDamager.OnDoAttack` 本轮没有可分析的 unwind 范围，不能称为空函数或无副作用。伤害一旦可能进入即保留 unknown 和引用，不以 bool、未见回调或超时重新执行。`AttackData.attackID`/投射物 ID 与 ActionGate 的局部 operation 都不能冒充账本全局 Cargo operation。

## 员工受伤回调与敌方目标

生成的 `IDamageable`/`IDamager` 是继承 `Il2CppObjectBase` 的 CLR wrapper class，不是可直接写在 C# interface 实现列表中的接口。本机框架有 `ClassInjector.RegisterTypeInIl2Cpp<T>(RegisterTypeOptions)` 与 Type overload，`RegisterTypeOptions.Interfaces : Il2CppInterfaceCollection`，该 collection 有 public `IEnumerable<Type>` 构造。因此实际候选是注册自写 `MonoBehaviour` 的 native interface 绑定，再把它作为新 actor 的 damageable owner；不是伪造一个 CLR `IDamageable`。

需实现并验证的 `IDamageable` 精确形态为 `transform : Transform`、`OnTakeDamage(AttackData, DefenseData) : bool`、`IsDead() : bool`、`OnDie() : void`。自写接收回调只更新该 actor 的独立生存状态，死亡停止其输入/武器；原攻击对象的后续 native 回调保持正确语义。实际 `Damageable.m_DefenseData` 及 `DefenseData.defender`/`damageable` 为已核直接字段，但 `Damageable.Owner` 是业务 getter，不能写不存在的 Owner backing 来绑定。

必须先验证框架 interface 注册、这些 value-wrapper 参数/返回 marshalling、`Damageable.TryInitRequires` 能否发现新 owner，以及实际敌方攻击链是否接受新类型。元数据存在这些声明不等于运行成功。若自动 owner 发现不能成立，应据实际失败补 typed 绑定，不返回虚构的成功 bool。也可明示有限 Mod 接触伤害规则作为阶段行为，但这不完成原生敌方战斗覆盖。

新增 collider 不证明鱼会把员工当攻击目标。实际 AI 可能仍引用唯一 local player；本轮没有证明其第二目标选择链。后续要由真实鱼/攻击 source 支持在两个实际 actor 中选择目标，并保留原鱼区域、状态、攻击冷却和世界暂停条件。当前双区域 allocator/LOD 只维护兴趣与活动结果，不是员工命中或受伤权限。

## 捕鱼、袋与返航仍接原有事务

独立 actor 的实际命中不应触发房主 `SuccessPickupFish`/LootBox 后再复制奖励。捕获执行继续用现有 source reserve、entered selection、一次 grade/tier/plus 选择、规范化 raw products、同批容量 seal 与真实终态确认。员工袋是房主持有的会话袋；房主原袋原链不重复 Add。完整产物、任务/图鉴、终态与共享进度需要实际 commit 桥，伤害 bool 本身不能填 `CargoFacts`。

capture raw grade 与 return final grade 分阶段；已确认产品指纹不随返航悄改。返航仍走固定转换计划、实际一次 storage Add、库存 delta 与 save 确认。员工断线不清潜水账本，已经 entered 的 unknown 操作不因 actor 退休而重派发或补奖。见 [个人袋与首版规则](CREW_MODE.md)、[产物规范化](EMPLOYEE_FISH_PRODUCTS.md) 和 [数量转换](RETURN_COUNT_POLICY.md)。

## 下一轮实现与验证边界

先实接 actor 的创建、fixed input 消费、物理 driver 与 host 状态发布，再接独立投射物/真实 target 反查。使用确认 Unity 线程，每次 native 读取、构造和执行前后核固定 source；重入、失败、配额和旧 scene/actor token 撤证，native wrapper 只由明确的有界 owner 强保留，网络与日志仅 CLR 身份/值。断线后不接纳旧输入，unknown native 操作只回读不重新执行。

几何/输入/高水位的 CLR 测试与 TCP 往返可先覆盖真实生产 helper；原生受伤、接口 ABI、实际 enemy targeting、装备/buff、碰撞矩阵与双端相互作用需之后实际游戏验证。本轮没有编写 actor helper 或这些 tests，不复用旧测试来声称 actor 已通过。

## 本轮私有证据摘要

原元数据、wrapper IL、PE 指令、地址与机器路径仅保留 `development/.local/analysis/`；公开本页只有自写说明与计数/hash。

| 私有报告 | 实际范围与 UTC | SHA256 |
| --- | --- | --- |
| `crew-actor-api.json` | 24 types、846 selected method declarations、853 properties；3 个请求的精确顶层名称未找到。21:34:46.9178556Z→21:34:51.1896266Z | `74D3B5662E4D746218750567EA3AFBDA4FB8417EF62EA3676AA4FD6CB81C7A1A` |
| `crew-actor-physics-api.json` | 2 assemblies、10 types、136 selected method declarations，另15 properties/20 fields；缺类型 0。21:36:14.0658171Z→21:36:14.3821859Z | `E9C5E697D6830B047A58C6583702FD4B41D7C114E02B751B8356FCCD1FF69D3B` |
| `crew-actor-native-instructions.json` | 8 selectors、12 methods、5123 decoded/text instructions；11 known-range 方法、1 leaf 无范围，28 indirect calls、1 indirect branch。21:31:35.7919123Z→21:31:38.2661755Z | `D48FD33CC8B75DB26F34E639B1B5B396CC1B43E5E30A20B61099164225A43DD6` |

日期均为 2026-10-04。PE Depth 0，方法上限 48、每方法解码上限 4096，输出上限每方法 4096/总 16384；本轮没有 instruction text omitted、method quota 或 omitted root。共享别名、虚派发、泛型、未解析 native helper 与完整控制流仍未知；known-range 解码和这些计数不证明完整方法、ABI、执行结果或无其他副作用。Assembly-CSharp 输入 SHA256 为 `F41167D67D226866B22EB76A239B796B0D1E40F57177E62FBAAFC2284471626E`，Cecil 读取前后输入 hash 相同。
