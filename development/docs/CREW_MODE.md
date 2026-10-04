# 房主与员工模式

当前源码0.1.36-dev（协议6），实际Core/TCP271/271及插件编译通过。新增[房主地图候选读取接口](MAP_ADOPTION_ENTRY.md)，即时刷新并返回独立副本；原游戏加载流程尚未接入。见[验证摘要](../logs/map-candidate-build-verification.json)。未部署/启动，安装0.1.12、最近潜水0.1.11、默认包0.1.0保持；客机隔离、房主世界、每人独立背包的真实分流、员工命中、双端正常返航和冷配置仍待完成。

这是按用户提出的“房主掌主动权，第二人充当员工”确定的首版玩法方案。
房主带队潜水，员工提供捕鱼和搬运协作，长期进度归房主。
0.1.22历史源码为0.1.22-dev、协议5，插件Build警告视为错误通过；0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176结果，没有重跑测试。本文件区分玩法约定、CLR基础和待接游戏行为。
现有地图选择仅是候选，未实现地图采用、员工原生操作、独立工作背包或返航结算。

## 首版规则

- 房主选择潜水世界、入海、换层和返航，任务、图鉴、材料、经济与自动保存均由房主的原游戏处理。
- 员工使用自己的本机输入与镜头，计划拥有独立移动、氧气、受伤、鱼叉和弹药状态；装备先使用房主批准的固定配置。
- 按用户最新要求，每人有独立背包、独立容量和独立负重。房主统一管理数据，不共用一个容量上限。
  房主保留自己的原生`LootBox`；员工使用由房主持有的Mod会话工作袋，客机显示确认后的个人清单。
  员工袋是本次潜水有效的独立物料记录，不是房主袋的副本，也不是员工单机存档里的第二个`LootBox`。
- 每人容量来自房主批准的装备配置，品质、肉量、重量及超重规则由房主确认。
  例如两人各20kg时合计可携带40kg，一人负重不能拖慢另一个人；实际配置不固定为20kg。
  容量检查必须按操作人的袋执行，包括捕获前的检查，不能等物品添加时才换袋。
  满包/超重按各自已验证规则处理；初版先不开放两袋转移，独立丢弃也需确认。
- 先支持普通鱼和一套已验证武器，再扩展普通采集物、掉落物和工具。
  任务关键物、NPC、Boss及特殊捕获流程先由房主操作，逐条验证后开放给员工。
- 初期双方在同一场景/层活动，由房主带队换层。当前鱼来源只覆盖房主所在场景，
  不能默认员工独自进入其他层仍有完整鱼群；自由跨层需另外扩展房主多场景模拟和实体来源。
- 员工退出后恢复自己的单机状态，其个人存档、装备、图鉴和剧情不接收这次房主收益。
  隔离必须覆盖所有自动写入路径，不能只屏蔽最终保存文件或某一个保存方法。

## 权责

| 项目 | 房主 | 员工 |
| --- | --- | --- |
| 世界、换层、潜水阶段 | 唯一裁定，决定入海和返航 | 跟随共同阶段，发送操作意图 |
| 移动、瞄准、镜头 | 本地控制自己，并核对员工状态 | 本机独立操作，显示确认/纠正 |
| 装备、氧气、HP、冷却 | 保存两人的独立会话状态，批准固定装备 | 使用自己的临时状态，不借房主单例角色 |
| 鱼AI、伤害、捕获、拾取 | 运行实际世界，统一处理两人的竞争 | 显示结果，不自行奖励或运行另一份鱼AI |
| 背包、重量、品质、数量 | 原生房主袋和权威员工袋分别记账，按人发布容量/负重 | 独立工作袋和个人负重，查看确认结果 |
| 任务、图鉴、仓库、金币 | 沿已验证的原生链推进房主进度 | 查看房主阶段，不独立推进或写个人进度 |
| 返航、结算 | 开始屏障，两袋分别确认入同一房主仓库/保存 | 接受共同返航，恢复个人单机状态 |

当前远程角色仅有显示组件，不能直接参与碰撞、受击或发射鱼叉。
员工模式简化持久进度；第二玩家、独立背包、武器、命中代理及生存状态仍需真正实现。

## 把抓到的东西关联起来

以下身份和字段是设计要求。0.1.15-dev开始实现独立的纯CLR账本，尚未接入协议5或游戏潜水生命周期。

| 记录 | 关联内容 | 用途 |
| --- | --- | --- |
| 潜水记录`ExpeditionId` | 房主生成；覆盖本次潜水及其多个场景、网络连接片段 | 换层/员工断线不能清空已入袋记录 |
| 员工记录`MemberId` | 房主签发的本次潜水成员身份，不等于昵称或固定玩家2 | 新连接不自动继承旧员工的未决操作 |
| 操作记录 | 连接Room、握手玩家身份、RequestId/OperationId、目标epoch/EntityId | 关联意图、原生执行和实际物品结果 |
| 捕获/拾取记录`CaptureId` | 原目标身份、操作、房主确认的物料结果 | 两人同时抓同一来源、重复消息仍只有一笔 |
| 物料记录 | 真实TID、品质、数量、重量、OwnerMemberId、捕获人/协助者 | 肉量与品质取房主确认结果，不采信客机报价 |
| 个人袋修订`BagRevision` | 每个Member独立修订，记录入袋、丢弃、容量与超重状态 | 个人显示和权威袋一致，丢弃也需确认 |
| 返航记录`ReturnId` | 潜水记录、分别冻结的两袋清单、每条物料的入仓状态 | 原生已转移条目不重复写；员工未转移条目由新桥一次入仓 |

鱼的TID是物种，不是独立鱼身份。同一物种两条鱼分别记账；同一鱼的鱼叉、QTE和拾取回调不能各算一次。
一个来源可能产生多个物料条目，采用一个捕获事务加多个真实结果，不能把整条鱼、肉块与附加掉落分别重复兑奖。
场景卸载、池回收、镜头隐藏及预览消失都不是入袋证据。
房主原生目标复核仍需本地生命周期代次，原生指针/包装器不跨网络。
确实看到房主袋增加但无法强关联来源时，记录“未归属原生袋增量”，不要据此再复制进员工袋。
员工袋提交需关联完整产物、操作、实际分流与捕获终态，不按物种/时间接近猜归属或贡献。

## 捕获事务

```mermaid
flowchart LR
    A[房主或员工操作] --> B[核对身份、目标、装备与容量]
    B --> C[保留目标操作权]
    C --> D[房主执行已验证的原生操作]
    D --> E{实际结果证据}
    E -->|目标、产物与对应个人袋提交均能关联| F[提交唯一捕获记录]
    E -->|结果未知或关联不全| G[挂起并核对，不重试或补奖]
    F --> H[更新对应个人袋、负重及捕获贡献]
```

容量预约按Member执行，只是Mod内部并发计划。对应个人袋仍可能变化，执行前和实际提交后都要重查，
房主本地操作也必须进入同一竞争路径；只去重员工请求不能防止房主原生操作同时提交。
鱼叉发射、QTE胜利、原方法返回`true`、鱼被移除、入袋与入仓是不同阶段。
捕获账本必须关联真实目标/操作、房主确认的完整产物和对应个人袋的提交，不能看到任一单独标记便奖励。

房主捕获采用`HostNativeBagReceipt`，确认自己的原生袋实际增加。
员工捕获采用`EmployeeBagReceipt`：在已验证的产物产生/入袋边界，原子加入房主持有的员工袋，
确认这批产物没有同时写进房主原生袋，并确认捕获终态。不能先加房主袋再复制/扣减，
否则会污染房主负重、意外或重复触发任务/图鉴，或在异常时重复物品。
只拦截`LootBox.Add`也不够：更早的容量检查、负重限制及嵌套/异步调用都必须有相同的员工操作归属。
不临时抬高房主容量，不借`AddIgnoreOverloaded`冒充员工容量检查，也不向原调用随意返回假成功。

未来桥接的产物、容量路由、入袋分流及员工入仓事实能力默认不可用。只读日志或合成CLR测试不能开启实际奖励权限。
进入原生操作后结果不确定，保留操作和目标屏障；重复请求查原记录，不重新调用伤害/拾取或补发物品。
重启恢复首先核对真实游戏状态，不根据缺少Mod日志推断“原生未提交”。

## 原游戏入口与验证点

来自本机已生成的互操作元数据，不是已验证的完整调用链。

| 阶段 | 已发现入口 | 必须确认 |
| --- | --- | --- |
| 鱼捕获/拾取 | `FishAISystem.SuccessPickupFish`、`FishInteractionBody.SuccessInteract` | 目标、品质/肉量、原生提交与对象池回收顺序 |
| 房主袋与员工分流 | `LootBox.Add` / `Add_Impl`、袋槽、重量/超重成员 | 原参数含义、产物生成、按人容量、分流与嵌套/异步调用 |
| 图鉴/捕获进度 | `SaveDataCaughtFishRouter.AddCaughtFish` | 写入时刻、重复计数、捕获阶段自动持久化 |
| 返航入仓 | `IngredientsStorage.AddFromLootBox`、`LootBoxSlot.GetExchangeCount` | 房主原袋原链、员工新增适配、品质/数量转换、真实净增加与保存完成 |
| 其他拾取 | `PickupInstanceItem.SuccessInteract` / `OnStoredItem` | 物料类别、任务/金币副作用及唯一来源身份 |

袋槽的ID/Grade/FinalGrade/TotalCount存在`ObscuredInt`字段；不猜内部加密布局或自行解释参数序号。
已确认生成的`LootBox.weight/weightMax/m_Box/AllBoxSlots`属性getter会调用原生方法；不能把它们当成直接字段读取。
重量直接字段候选为`_weight_k__BackingField`，容量字段为`m_WeightMax`；实际稳定读取、袋槽标识、嵌套添加及任务写入仍须实测。
`MissionManager.UpdateMissionIntCondition`存在按物料/品质/数量更新的入口；任务可能在水下变化，不能假设全部进度等返航才写。
鱼的`GetDropItemID`参数是tier，不是grade；产物可能有追加随机掉落，不能拿原鱼TID猜肉量、重复随机或补roll。
`LootBoxSlot`没有已发现的(id,count,grade)便利构造；员工入仓需验证真实槽/转换规则，不假设直接AddIngredients等价。
`CommitDiveLootDataOnlyJungle`是专项入口，不能作为普通海洋返航；普通返航的调用与保存顺序仍待实机。
0.1.11已观察Fire/Hook/Damage，未见Win/Pickup；0.1.12目标检查只读，不能证明入袋或收益已接通。

## 返航和退出

原GameAssembly离线分析补定位：`AddDropItem_Impl`包含袋Add/IgnoreOverloaded及
`AddLootingSaveData`目标，容量与持久副作用必须随员工产物一起分流。
`AddFromLootBox`的关联代码有六参数IngredientsStorage.Add目标，普通返航还分鱼卵、
采集物、关键物品、种子与装饰路径；不能仅一个入仓回调代表整个背包。
保存/加载基类包含文件写/复制/删除等目标，SetLoadedData也含同步；不能当纯恢复接口。
这些是静态目标，不证明分支、增量或保存；复现及下一接入点见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。

- 房主开始正常返航后停止新操作，冻结已确认入袋清单，处理已进入原生的未决操作。
  只有证据充分才标记完成；结果未知不自动重试、退款、回滚库存或重复结算。
- 房主原生袋让原游戏按已验证的链入仓和保存，不能按CaptureId给这一袋重复Add。
  员工袋尚未写入原生仓库，需要新的返航适配，按唯一物料/批次一次转入房主仓库并核对实际增加。
  入仓过程中区分Pending/NativeEntered/StorageObserved/SaveConfirmed；未知结果不再次写入。
  按ReturnId/CaptureId/ProductIndex记录每个产物，部分成功不能重放整批；原生保存与Mod日志没有共同事务保证，跨崩溃未知条目保留待核对。
  不在水下把员工袋塞进房主袋挤占容量；汇总发生在返航入仓阶段。
- 员工断线不撤销已确认员工袋。它仍由房主本次潜水账本持有，按既定正常返航规则入仓。
  未开始的员工操作取消；已开始的结果继续由房主核对。
  潜水账本由房主潜水生命周期持有，不放入随`NetworkController.Disconnect`清空的房间对象。
- 房主中途退出、崩溃或没有返航，不冒充正常结算；依照原游戏可核对恢复，不自动重放奖励。
  首版不承诺跨进程重连续跑或自动恢复未知事务，新连接的玩家2/同昵称不能自动继承旧员工操作权。
- 客机进入前保存临时状态基线，退出时清自己的代理/临时UI并恢复单机状态。
  自动保存、图鉴、任务、仓库、IGP临时/长期数据等路径要共同隔离和验证，未隔离不能开放正式员工玩法。

## 实现顺序与验收

1. 固定房主/员工角色、批准装备、独立袋与退出规则，定位并验证客机临时进度及所有持久副作用隔离。
2. 验证地图原生来源与跨机标识，采用房主选择并隔离客机鱼生成/AI；同层入场后再授予玩法权限。
3. 建员工独立actor、位置、装备、氧气/HP和投射物状态；原生入口若暗用房主Player/当前武器，不直接作为员工执行桥。
4. 只读关联普通鱼产物、原生容量检查、入袋及正常入仓路径；建立按Member的CLR个人袋、捕获账本与容量预约。
5. 验证员工产物完整分流且不影响房主原袋、重量和自动写入；接一个已验证武器和普通鱼，同一目标只进一个人的袋。
6. 接员工袋原生入仓桥，分别验证两人容量/负重、正常返航汇总仅一次、员工断线保留已确认袋、未知结果不重派发及客机恢复。
7. 完成真实双游戏闭环后扩展采集/掉落/关键物；两袋转移、员工持久报酬和自由跨层属于后续阶段。

源码/协议准备不等于上述玩法通过。当前实机验证延后、默认发行包0.1.0，真实双游戏与员工模式尚未验收。

## 0.1.15-dev 的实现边界

新增`Core/Cargo`个人账本：两位成员分别拥有容量、重量、预约重量、袋修订与请求高水位。
来源与操作、捕获人及完整产物指纹关联；过期事实、重复来源、不同操作或产物替换不能直接确认入袋。
房主袋重量取可信原生总重量，不再重复加产物重量；员工袋只累加自己确认的物料。
本模块只接受房主已经核实完整产物的候选计划，原生随机产物尚不明确时不能建计划、猜肉量或再次随机。

进入执行后未知结果保留来源与预约，不因超时、换场景或员工断线重新派发。
返航冻结后停止新预约；房主袋只能观察原生入仓，不可领取新增物品的派发租约。
员工袋每个`CaptureId/ProductIndex`单独记录入仓阶段，部分成功不会重放整批；保存确认仍是独立步骤。
该账本是内存模型，0.1.24已有只读网络清单；游戏生命周期、持久事务及原生分流/入仓桥仍未接通。
当前来源Room首次预约后绑定；没有已验证的来源迁移，新Room不能重编号绕过未决来源。
跨scene epoch仍有未决预约/执行时保留保守屏障，原操作迟到的真实结果可核对，不重派发。
快照仅列跟踪捕获，房主原有货物未完整枚举，`NativeBagInventoryComplete=false`。
`CompleteReturn`只表示跟踪条目的闭环；返航阶段的Member.Inventory/Weight为审计视图，逐项状态以ReturnItems为准，不能直接作为实时袋或负重UI。

F11新增默认关闭的`Observe loot and return calls (read-only)`，独立于TCP和鱼观察开关。
历史0.1.15观察四处自然调用：鱼`AddDropItem_Impl`、袋`LootBox.Add`、图鉴`AddCaughtFish(int,int,bool)`、
仓库`IngredientsStorage.AddFromLootBox`；每处前后记录同一个CallId，原参数和返回值保留。
Unity线程内即时复制参数及袋重量/容量直接字段，只排队CLR值，消费日志时不再读取原生对象。
鱼来源仅在鱼产物入口prefix冻结候选身份；嵌套的袋、图鉴或仓库调用不会按时间/线程推测所属鱼或员工。
槽位加密字段和返航转换数量暂不读取，`ActualBagDeltaProven/SourceOperationBound/CaptureSuccess/StorageDeltaProven`始终false。

历史0.1.15的限额是全进程1024条、队列64；0.1.25引入且当前沿用的16入口限额和来源语义见[CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)。
关闭或Disconnect只移除自己的挂钩并清诊断队列，统计与错误锁存不因重开归零；队列丢失或停止时丢弃要计数，日志不是完整链保证。
新版编译和纯CLR测试结果见[独立背包构建摘要](../logs/cargo-ledger-build-verification.json)。
目前未部署或启动0.1.15，四处挂钩的原生ABI、实际读取与完整捕获/正常返航均待实机。

## 0.1.17 当前接线与下一桥

0.1.17 将当前固定来源 CLR 清单接入候选发送：建房时记录实际 Run/owner floor，建房前 entry、退休 Run/owner/controller 和旧回调不能提供新来源。集合删除或替换先退休旧 wire 代次再重发，每帧最多 8 条选择；诊断队列消费不影响当前清单。旧 Observe map selection calls 仅诊断，其关闭或丢失不发送/撤销来源。

新增 6 组 Core 快照和 6 组实际回环 TCP 适配测试，原 4 项源适配已迁移，总计 160/160 通过；Build 警告视为错误通过。测试使用合成标量，不运行 NativeHooks、NativeCapture、Unity provider 或两个游戏。NativeGenerationBound、HostSelectionApplied、GuestStateIsolated、WorldAuthority、CargoAuthority 仍为 false；未部署或启动。

当前行为见[固定来源候选传输](ORIGIN_MAP_TRANSPORT.md)和[0.1.17 构建摘要](../logs/origin-map-transport-build-verification.json)。0.1.14 的 callbackFloor/cache 来源与 0.1.16 的“仅日志”是历史范围，当前发送流程按新文档执行。

客机的原生 Serialize/Deserialize、双 Data/Interaction 根及直接恢复候选已离线定位，见[客机影子桥研究](GUEST_ISOLATION.md)。SaveData(string ver) 不是 JSON 构造器，SetLoadedData/Load 不是纯交换；旧协程、缓存、可变子树及全部持久输出仍需隔离与恢复验证。尚未执行原生克隆/根替换或证明 GuestStateIsolated。每人的独立容量和负重规则保持不变。

## 0.1.18 原生根桥与输出围栏源码

0.1.18新增实际typed原生影子桥、单次事务及已枚举输出围栏源码。四类Data原生JSON round trip、五根直接交换/回读/恢复和15个独立强handle已编译；7组新增事务夹具以合成backend验证partial/unknown补偿、fence/refs保留和一次清理，总167/167通过。

当前生产进入与静止边界恒false，事务在围栏安装前拒绝；startup primitive自身再查边界，未接Network/GUI，未运行克隆、根交换、阻断或恢复。194条精确声明不是所有writer、独立native地址或ABI证明；Interaction未Sync、完整子树/旧缓存/协程隔离仍待完成。全部GuestStateIsolated/NativePermission/WorldAuthority/CargoAuthority保持false，未部署或启动。

实现与下一步见[原生根桥](GUEST_SHADOW_BRIDGE.md)、[输出围栏](GUEST_OUTPUT_FENCE.md)及[0.1.18构建摘要](../logs/guest-shadow-build-verification.json)。下一步必须实现可信原生进入/静止边界与缓存/Interaction切换，再进行受控实机验证；个人袋分流、真实地图采用及双游戏闭环仍按原计划推进。

0.1.19版交互准备只补客机临时状态基础，未开放员工捕获/入仓权限；每人独立容量和负重、房主原袋不重复Add、员工逐产物结算规则保持。完整缓存及旧引用边界见[GUEST_RUNTIME_CACHES](GUEST_RUNTIME_CACHES.md)。

0.1.20 已将[独立食材缓存准备/恢复](GUEST_INGREDIENT_CACHE.md)接入第六步源码，174项测试仅覆盖CLR控制及此前范围；[当前摘要](../logs/guest-ingredient-cache-build-verification.json)不证明原生运行。每人独立容量/负重及房主唯一长期收益规则保持；完整资源、缓存、真实地图、捕鱼分流、返航和双游戏仍待验收。

0.1.21 增加第七[Ingame缓存](GUEST_INGAME_CACHE.md)，[API](GUEST_INGAME_API.md)核对六类记录及mutable子图。非空SubHelperSpecData和live gearQueue未支持时拒绝，不分享、不改空。三known原图在Serialize前闭合，准备后strict复查；七步按7→6→Save5恢复，21explicit handles/4Data stamps，第七singlefield无Mixed。176项Core仅控制证据，[本轮摘要](../logs/guest-ingame-cache-build-verification.json)的插件构建警告视为错误通过，native候选未执行。

临时缓存副本不提供员工装备、工具、背包或捕获权限；entry/quiet/native/guest/world/bag仍false。每人的独立容量、重量与负重不变，房主原袋不补Add、员工未入仓产物须逐项真实确认。完整资源/actor/其它缓存/输出、房主地图采用、个人捕获与正常返航、实际双端/冷配置和M3—M7仍待完成。

## 0.1.22 comparer 源码不代替员工玩法

[独立字典comparer](GUEST_DICTIONARY_COMPARERS.md)与[接口](GUEST_COMPARER_API.md)仅补缓存准备。int/string/InGameSaveType(int32)三key的精确Generic/Object与该enum专用Enum候选同class独立复制；source pointer/class/kind及aux审计、null原Capture后Prepare拒绝同样约束Ingredients。不调用Default/CreateComparer/getter，不共享、清空或改成其它语义；custom/文化/hash-salt未知拒。新表显式(capacity,comparer)后才Add；constructor抛时assignment未发生，PartialConstructorAllocationRetentionVerified=false，不表示全部未知allocation已持有。

七步/21explicit handles/4Data stamps不扩，全ABI/fullisolation/entry/quiet/native/guest/world/bag权限false，无Network/GUI自动native调用。[0.1.22 历史摘要](../logs/guest-comparer-build-verification.json)的Build警告视为错误通过；0.1.22 该轮 Core 输入未改，复用0.1.21实际176/176，未重跑。[冷档候选](GUEST_COLD_PROFILE.md)仅研究首load、slot和输出，未采用。每人独立袋/容量/重量/负重、房主唯一长期收益、房主原袋不补Add及员工逐产物返航保持；资源/actor/cache/output、房主地图、个人真实捕获、双端/冷配置和M3—M7仍待验收。

## 0.1.24 个人袋账本传输

已编译并实际通过 200/200 项 Core/TCP 验证；新增 18 项分页、会话与真实生产适配器夹具。详见 [CARGO_TRANSPORT](CARGO_TRANSPORT.md) 和 [构建摘要](../logs/cargo-transport-build-verification.json)。协议 6 在房间内传两袋的只读账本，整批分页原子提交、五路公平发送及慢连接保留已开始批次；场景变化不清袋。Disconnected 保留确认与未知记录，新 Room/peer 不自动恢复旧成员；重量及 Returned 阶段均仅历史记录。没有游戏原生 producer 接入，没有虚构账本或 GUI 权限开关；实际容量、捕获归属、员工分流、逐项返航与隔离、世界、双端/冷配置继续必需。

## 0.1.25 捕获来源与个人容量边界

本轮 Build 及实际 Core/TCP 209/209 通过，新增9组生产 CLR 来源栈夹具。已有默认关闭的 Loot 观察扩至16精确入口，区分主/追加产物、原随机返回、容量检查、入袋与水下进度；prefix冻结候选、postfix保留scope、void finalizer退出，未知鱼边界遮父，异常/丢失/错线程锁存。详见[CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)与[本轮摘要](../logs/capture-lineage-build-verification.json)。同步包含不是直接caller或员工归属，原bool/int、鱼消失和袋重量都不单独确认捕获；原生回调/ABI未运行。

下一步接独立的潜水级操作编号与来源租约，再在原游戏已经选定整批主/追加产物后的受控阶段进行个人容量检查和分流。不提前重掷保底随机、不把事后Add参数冒充入口前完整计划，不先入房主袋再复制。需要覆盖现有槽合并与任务/成就/解锁副作用；正常返航不能清未知结果，逐产物入仓与客机完整隔离仍待接。

## 0.1.26 来源预约与延后完整产物

Build及实际Core/TCP222/222通过；新增11组生产账本夹具、2组实际清单适配器/TCP。详见[CAPTURE_SELECTION](CAPTURE_SELECTION.md)与[本轮摘要](../logs/capture-selection-build-verification.json)。SourceReserve共享潜水账本捕获编号与来源围栏，EnterSelection在native业务前须已有整批隔离能力，LateSeal仅绑定已选完整且仍未写袋的产物和个人容量。未选Request=null、Intent保留；EnteredUnknown不重新执行/取消，return或断线不把它当零产物完成；晚绑定只补原capture的返航条目。第一份完整已核选择在容量检查之前固定，容量拒绝也不能改成较轻产物；同批次可以新鲜容量再核，不重Roll。旧Reserve仍需已核完整计划，Gate局部编号与Unity实例编号不能当潜水operation或会话player身份。真实native证明全部false，无producer attach、选择/分流/入仓/save执行；下一步显式原选择与提交桥、最终grade/effectiveweight/slot/任务与鱼终态、客机隔离及双端/冷配置。

## 0.1.27 原参数基础资源观察

既有默认关闭16入口在Add_Impl Before同步读取两种精确类的四个资源direct backing fields，After/Finalizer复用owned CLR候选。未知类/null明示不可用，不调用业务getter、主动抽随机或解码货槽。TID/ItemDataID、basegrade/baseweight不代替最终产品映射/品质/有效重量；原生调用、ABI、整批产物与个人分流权限仍false。Build通过；纯CLR/TCP输入与前commit逐文件一致，复用0.1.26实际222/222，本轮未重跑。详见[资源观察](LOOT_PRODUCT_OBSERVATION.md)与[本轮摘要](../logs/loot-product-build-verification.json)。未部署/启动，继续真实选择/提交桥、个人容量/捕获/逐项返航、客机隔离/房主世界及双端冷配置。

## 0.1.28 受限货槽候选

Build及本轮实际Core/TCP226/226通过，新增4组已初始化/非零key的CLR标量解码夹具；原生slot仍仅编译未执行，16默认关闭入口保持。Before对exact槽四struct字段两次守卫采样，prefix仅存候选，After/Finalizer复用且不读原生槽；密钥/隐藏值不入队/日志。未init/key0/fake校验不符明确Unavailable，不补初始化/查静态key或检测器。离线新工具文本默认关闭，三模式原decode/edges不变，4无范围leaf不补猜。最终品质时刻、接口重量、肉量转换与完整产物/个人分流/入仓仍待实际桥。详见[货槽观察](LOOT_SLOT_OBSERVATION.md)及[本轮摘要](../logs/loot-slot-build-verification.json)。

## 0.1.29 已有货槽与品质更新观察

新增三处默认关闭的自然setter观察，覆盖新槽入口之外的总数量合并和品质更新候选。Before/After各自复制四字段，After固定同次prefix身份；Finalizer复用After（含Unavailable），没有After才复用Before。RunId+CallId仅标识本次样本，不能跨调用关联货槽或证明槽寿命/袋身份。数量setter传入的是新总数，不是捕获增量；FinalGrade setter不证明整批或捕获终局。Build和实际226/226 Core/TCP通过，扩展两个既有CLR来源栈夹具；原生回调/ABI未执行，个人产物分流仍待接通。详见[货槽观察](LOOT_SLOT_OBSERVATION.md)及[本轮摘要](../logs/loot-slot-mutation-build-verification.json)。

## 0.1.29捕获选择与提交的历史研究

该研究阶段插件保持0.1.29；该轮只执行新离线研究工具，不重复原226测试或插件Build。普通拾取按CarvableCount逐tier，死鱼身体为另一个tier1配方；主选择也会随机，追加只一次并改保底/dirty。原Add的成功返回在实际Add_Impl之后，槽前已经有负重效果，原容量并非整批预期重量检查。SuccessInteract的UnityEvent转发不提供已证actor归属，回收/尸体状态也非捕获凭证。按[员工选择与提交桥](FISH_YIELD_BRIDGE.md)实现明确合作批次规则；不再增加镜像Core Gate，先接实际成员、选择/分流producer与现账本。完整M3—M7和实机闭环继续必需。

员工选择原语已在0.1.30实现，见[EMPLOYEE_FISH_SELECTION](EMPLOYEE_FISH_SELECTION.md)。选择中的奖励品质也可能随机，须同来源租约进入后一次固定；主/追加结果和强资源保持不重选。只是raw计划，不是员工入袋；真实容量、最终产品、共享进度与单次终态receipt、逐项返航仍需接入。每人独立袋/容量/负重和房主长期进度规则保持。

0.1.31的[捕获产品桥](EMPLOYEE_FISH_PRODUCTS.md)为员工自己的袋固定有序产品及个人重量。容量失败保留整批，不影响房主原袋，也不重选；仍需真实员工分流/捕获凭证。房主唯一长期进度，返航品质/数量与逐项入仓另实现。

0.1.32[员工逐项返航](EMPLOYEE_RETURN_PLAN.md)将转换输出与原捕获分开：每项政策/六参固定，partial保存或断线不重整批，未知阻止正常完成。员工各自袋与容量，返航物料入房主仓库；房主原袋仍自然链只观察。真实映射/FinalGrade/数量政策与入仓增量/保存未验。

0.1.33[返航映射与数量缓存](EMPLOYEE_RETURN_MAPPING.md)按员工原产品index关联ingredient/parent/rank/实际一次兑换候选。各自背包、容量和重量继续独立；rawGrade不改FinalGrade，兑换数不改原capture count。容量拒后重验和鱼回收后pure计划沿同一缓存，不重复roll/lookup/exchange；类别与最终品质producer、入仓增量/保存及实际两袋玩法仍待接。

0.1.34[自然返航规则](RETURN_GRADE_OBSERVATION.md)保原additive/阈值与槽分类/兑换返回，不把整袋归给一条捕获鱼。自动返航非null数量delegate与UI null/CellData输入分开；UI上游转换未证，不直接用原count入仓。Apply读取host save集合，禁temp员工袋调用。员工真实政策/捕鱼分流/个人容量、仓库增量/保存仍需绑定，两个袋各自容量/负重继续。

0.1.35鱼叉头显示只补远端外观，不建立原生员工武器或共享容量。后续数量解析已确认自动委托与UI鱼预先转换，见[RETURN_COUNT_POLICY](RETURN_COUNT_POLICY.md)。员工原捕获grade/count/weight/指纹与返航品质、肉量转换保持分开；实际员工返航producer与独立袋分流仍待接，每个人的容量和负重规则保持。
