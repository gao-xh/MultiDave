# 客机原生鱼隔离与房主鱼显示

源码0.1.41-dev、协议8把实验客机的本地鱼出生隔离与现有房主鱼清单显示接在一起。
本页描述源码实现，统一Core/TCP实际311/311与插件Build警告视为错误通过；没有部署、启动游戏、运行原生回调或验证真实双端画面。
当前安装仍0.1.12-dev，最近潜水验证0.1.11-dev，默认发行包0.1.0。
完整生成、AI、碰撞、收益和持久隔离仍未证明，不能据本页宣称已经完成共享海洋或合作捕获。

## 启动与握手

沿用默认关闭、每进程一次的 `Startup.ExperimentalGuestInitialization`。
`NativeGuestInitializationController` 安装原初始化来源、保存输出围栏、地图采用链以及
`NativeGuestFishQuarantine`；没有实验启动实例的普通Host/Join维持原来的手动诊断行为。
初始化与保存边界见[GUEST_INITIALIZATION_BOOTSTRAP](GUEST_INITIALIZATION_BOOTSTRAP.md)，
实际加载调用与出生来源见[GUEST_SCENE_LOAD_LIFECYCLE](GUEST_SCENE_LOAD_LIFECYCLE.md)。

实验Guest创建握手身份时带 `RequestsHostFishDisplay=true`。
协议8的Hello与Welcome身份必须在原JSON中明确带唯一、类型正确的布尔字段；旧协议7不能与当前版本握手。
LAN异步入口及Handshake复制身份，Session再冻结远端请求为 `RemoteRequestsHostFishDisplay`，
不借用调用方之后可修改的身份对象。地图路线的v2指纹与字段语义保持。

这个字段只请求数字观察和显示。房主在Ready时据此自动启用自己的只读鱼生命周期观察并发送现有WorldSnapshot，
不要求玩家额外打开F11的Transmit；普通手动观察开关仍可使用。
它不跳过房间、角色、世界布局、场景epoch或Ready校验，也不证明Guest隔离或授予伤害、捕获、背包权限。

## 原Awake之前的出生链

鱼可能在Scene加载完成或IGP选择完成之前出生，不能等房主清单到达才停止本地模拟。
`NativeGuestSceneController.RegisterFishBirth` 从本次自然回调冻结原FishAISystem、其自身GameObject、
实际Unity身份、精确原生class和Scene handle/name；复用同一实验Guest peer/room、已安装路线与当前entry来源。
登记器只冻结出生前已有operation，以及实际同步祖先范围中尚未返回的加载调用。
未知owner0范围遮父，专用pending manager仅保留已冻结调用的同Scene子出生例外。

出生后不能增加候选或借后来同owner、同键的operation。
尚未返回的加载调用必须取得本次原typed返回，再由同operation/version的成功结果和出生Scene handle精确匹配。
`TryReadFishSource` 使用时复核当前原operation和原鱼身份；Host数字EntityId、鱼TID、DTO数量或历史快照不能充当出生证明。
鱼独立占用出生配额，不取得IGP选择或Init协程权限。

`NativeGuestFishQuarantine` 在支持的原Awake prefix中接上述来源，不先运行原鱼Awake来取得模板。
已初始化标记、已有特殊鱼更新绑定、未知原生子类、重复Awake、共享鱼根或错误线程均拒绝本次实验路径。
必须是actor自身的独立GameObject；检查其子树只有本鱼，且不含PlayerCharacter、Camera、SceneContext或Terrain，
不能停用祖先地图根。

先保存birth token与inert记录，再保留actor、GameObject和Transform的强引用、句柄及子组件身份。
`Inert` 是先登记的抑制标记，单独不证明物理已经关闭。
首次写入前设置一次性尝试标记，关闭已核子树Collider2D、关闭Rigidbody2D.simulated，再执行SetActive(false)。
只有后续实际读回根不活跃、碰撞关闭和物理不模拟才记录完成；部分失败不再次写入、不重试初始化或恢复原鱼。
SetActive触发的同一已登记actor回调走窄抑制分支，不执行跳过Awake的原清理。

## 生命周期与失源

原鱼生命周期、AI/交互/伤害/掉落入口及支持的Damageable、Damager、FishInteractionBody等子组件回调有独立前缀。
已登记鱼与子组件保持抑制；未知子组件若在支持的鱼Awake之前先初始化，会使实验来源失效。
普通玩家、NPC或全局组件不会仅因碰巧嵌套回调而被认作本鱼。
失败后只用受限身份探测查抑制墓碑，不继续认领新的来源；非受管普通组件保留原行为。
被阻断的bool入口不返回成功；不制造AI factory假返回来冒充异步完成。

自然OnDestroy、实际Scene卸载、Context退休或新entry撤销旧birth，登记器与组件身份墓碑不淘汰。
旧鱼没有运行原Awake，退休后也不能恢复原清理或重新模拟；抑制墓碑不是新对象寿命或权限证明。
原生指针复用可能保守阻断后来对象，这个限制没有实机验证。

`Update` 与显示前复核都检查当前来源、根活跃状态和完整已核物理子树。
换代、断线、线程变化、重入、未知异常或配额耗尽使来源失效，清理自建显示；
原鱼记录、已分配强引用、保存围栏和影子根保持到进程结束，不热恢复个人进度或重新启用鱼。
自然退休后的普通加载分支不因此变成完整返航、仓库或保存验收。

## 数字清单怎样显示

`NetworkController` 的实验Guest显示必须通过
`NativeGuestInitializationController.CanDisplayHostFish(peer, actualPlayerSceneHandle)`：
固定真实peer、健康启动与影子根、当前路线entry，以及该Scene唯一实际完成operation都须成立。
该Scene已登记鱼还须有同一精确birth/source并保持隔离；原生鱼数为零时也不能省掉实际Scene来源。
接收和渲染前后重新核对房间、epoch、SceneKey与来源，不依据Hello布尔字段直接创建画面。
单鱼预览的接收、渲染和marker也复用这个来源检查；失源不保留旧标签或副本。

收到的房主清单仍经过现有完整数字名单、每鱼16帧历史与过期隐藏规则，再由RemoteFishWorld/FishDisplayNode显示。
正常Guest用房主坐标；单游戏Local test保留偏移和染色诊断。
SpriteCatalog、SpineCatalog从已加载资产解析显示资源，不运行Guest原鱼AI/初始化来补资源。
资源未加载或无法解析时报告不可显示，不能因此放行本地鱼。

自建显示节点只有Sprite或Spine画面，没有原生鱼AI、碰撞、投射物命中、捕获或收益。
死亡/捕获状态、名单删除或视觉消失都不是员工成功入袋的收据；当前死亡鱼显示规则也未成为尸体交互桥。
房主源仍只观察房主玩家当前Scene的活动鱼，每0.2秒采样；完整清单只表示完整收到的活动观察名单。

## 配额、证据与未完成项

进程最多4096个保留鱼birth、每鱼3个明确句柄、最多12288个句柄；每鱼最多256个子组件身份、128个Collider2D、
64个Rigidbody2D，组件墓碑总数最多65536。每Update最多轮转检查16条记录，日志最多128条。
单次调用最多8192窗口步数，前后守卫也计步；完整显示复核会扫描本Scene的登记鱼。
大鱼群或复杂子树可能耗尽单次预算而失效，这不是4096条鱼都可同时稳定显示的保证。
`QuarantinedFishCount` 当前取保留记录总数，包括退休或部分失败记录，不能当作全部成功隔离的数量。

重新执行的离线声明核对覆盖21类、118个目标声明，声明错误与缺失类型均0；这不是原生回调或ABI已执行。
12个所查子组件类型没有自己的DeclaredOnly OnDisable，因此没有按名称虚构该目标；共同基类清理顺序仍未知。
SABase更新订阅、中央AI/LOD、allocator、动态任务、boss、其它子类、外部body/renderer与子组件提前执行等仍有覆盖缺口。
根不活跃不证明所有外部已登记工作已经停止。UniRx与TerrainModule仅是本机构建引用，不作为游戏运行库发布。

FullCoverageVerified、NativeRuntimeVerified、NativeFieldAbiVerified、GuestStateIsolated、WorldAuthority与CargoAuthority保持false。
本轮统一测试实际311/311与Build通过，见[真实摘要](../logs/guest-fish-isolation-build-verification.json)；原生初始化、SetActive顺序、真实资源、双端画面、正常返航及完整持久输出隔离均未验证。

首版规则仍是房主唯一长期进度、每人独立袋/容量/重量/负重：房主自己的原生LootBox，
员工由房主持有独立会话袋，员工产物不能先入房主袋再复制。
现有显示不接通员工武器、完整产物/前置容量分流或返航一次入仓/保存。
同层远距离还需房主维护两人附近活跃区域，自由跨层、allocator/LOD接管仍未实现。
继续完整M3—M7与真实双端闭环，不以本页实验隔离或数字显示结束目标。

离线PE范围为8个选择根、9个方法、3106条已解码且保留文本的指令，含14个间接调用、327条未解析精确入口边与7条别名截断边。已知unwind范围可解码仍不证明完整方法体或脚本执行顺序；原指令、地址与报告不发布。
