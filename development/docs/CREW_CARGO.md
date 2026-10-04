# 员工独立个人袋与Harvest接线

0.1.46-dev、协议11新增房主自然潜水来源、真实Room员工个人袋注册、个人重量/负重状态，以及普通downed鱼的实验Harvest生产调用链。
[实际验证摘要](../logs/crew-cargo-build-verification.json)记录367/367 CLR与本机真实TCP、插件Build通过及输入封存。
本轮没有运行Unity/source hooks、捕获、进度提交、返航、保存或两个游戏。这里描述源码实现，不是完整合作捕鱼验收。

## 配置与真实成员来源

`Network.ExperimentalCrewCargo`默认false，在首次Network Update固定；仍需双方握手CrewActor、真实Host/Ready/Room和当前员工自己身体来源。
普通Host/Join不会因袋空、观察位置或握手开关获得世界/收益许可。Local test与Guest本地存档状态不作房主袋来源。
`Crew.AllowOverweight`默认true：采用房主批准的个人超重Mod政策；设置false才执行严格个人容量拒绝。
员工容量来自房主`Crew.CapacityKg`固定profile，独立于原房主容量，不将两人合成20kg团队袋。
本轮不自动改玩家配置，不部署或启动游戏，也不催用户延后实机。

`NativeHostCargoSource.Install`只挂自然`LootBox.Load`、原`get_m_Box`返回、`PlayerCharacter.StartDiving/EndDiving`四入口。
实际自然Load、返回的原字典和同玩家潜水顺序确定来源，随后绑定当前Manager/player/Scene、原SaveData与LootBox。
两份有界原袋图像包含槽及重量，核根/代次/引用/内容；这四声明及direct代理是离线证据，不是已运行hook或ABI证明。
只把原`HostWeight/HostCapacity`给HostNative成员；员工不镜像它，也不调用原房主Add后复制。
需要新鲜原袋整体不变时重读两图像；普通每步守卫使用轻量潜水根核对，不每primitive深复制整个账本或原袋。

`CrewActorController.UpdateCargo`在真实自然潜水成立后创建固定Room的`ExpeditionCargoLedger`，登记恰好HostNative和EmployeeVirtual两位成员。
Host初始重量/容量取原袋实际样本；employee初始0是新建的真实Mod临时袋，不声称原游戏另一袋已经读取为空。
`CrewCargoBinding`固定同员工GUID、同实际HostCrewControl和profile容量；暂停/同Room换层保成员、confirmed/unknown货物与高水位。
断线关闭binding并保留账本；新Room或新员工不能继承。当前`CargoInventoryController`只保一份retained expedition，
下一潜水/新Room若仍有旧账本则拒新attach，完整多潜水与返航回收周期尚未接完，不能说已自动重开新袋。

## 自己的重量与移动

协议11的BagWeightKg是double，伴ExpeditionId/MemberId/BagRevision，只来自employee已确认账本成员。
未知时重量null、身份null、revision0；confirmed 0只证明当前已确认Mod账本值，不是全量native库存、捕获成功或保存证明。
Reserved/EnteredUnknown未确认批次不并入confirmed重量；网络已确认重量字段也不授权原生执行。
房主Mod负重规则明确为：confirmedWeight<=capacity时factor=1，否则`max(0.25, capacity/confirmedWeight)`。
房主批准速度乘这个自己的factor，状态回传供员工读取；它不是原游戏WeightParameter/debuff算法，也不借Host重量。
Core轻量load查询只返回自己的scalar和未解决数，不提供库存数组、不铸Nativefacts；实时原生性能及双机手感未验。

## 普通死鱼一次Harvest

实际调用链为`NetworkController`收真实CrewInput → `CrewActorController.AcceptCaptureInput`消费Interact升沿 →
`StepCapture`/opaque `NativeEmployeeCaptureCommand` → `NativeEmployeeCaptureCommitBridge.TryPrepare/TryCaptureOnce`。
Space沿用Interact输入；每个升沿/包高水位只消费一次，队列有限，已过期/未绑定/失败的意图不会延迟自动采集。
使用员工自己身体实际位置选择2单位内唯一最近、同Scene且current生命周期的普通downed鱼；未知同距/不支持/DLC或collection profile拒绝。
FishAISystem与exact SABaseFishSystem的dead字段来源区分，Downed不是仅凭鱼TID、标签或显示副本；这套Whole-corpse Harvest是明确Mod规则。
它不假装执行原房主harpoon/UnityEvent/全部原pickup交错RNG，也不借HostPlayer或Host袋容量。

在同ledger先`SourceReserve`铸lease，再`SelectOnce`不可逆EnteredUnknown，grade/main每tier/plus保底一次，native资源和参数强持有。
`NormalizeCaptureProductsOnce`冻结原TID、capture槽品质和每产品float重量；employee总账double求和属于Mod容量规则。
`MapReturnProductsOnce`为每项显式`ExchangeWholeOnce`，原lookup/food/count返回只取一次；异常保partial未知不再lookup或exchange。
这是自动返航路径启发的员工计数政策，不表示FinalGrade或完整原返航政策已经得证。
同lease `LateSeal`固定完整批次指纹后验个人容量；严格拒绝仍留原selected批次与未知来源，不重Roll/重选/换轻批次。
当前生产捕获失败会停止本cargo来源，续租已保批次后恢复materialization的UI/调度还未接通。

容量接受后才逐产品派发明确Looting/mission更新，并写房主caught-fish图鉴；原字段回读与配对返回只覆盖有限已知profile。
随后一次原`DestroySelf`配对`__runOriginal`、actual corpse/停用/current生命周期状态，核原房主袋两图不变，才向同lease确认员工货物。
正常void/bool返回、鱼隐藏、空袋或旧lineage候选均不能独自作receipt。完整任务、achievement、所有品质/类型及原捕获副作用仍未验证。
Host原袋继续自己自然捕获，employee产物不先写Host袋再拷贝，也没有水下临时挤占原Host容量。

14条精确fence覆盖此有限同步调用窗中的原pickup、body交互、bag写及终态；预期14声明不是实际安装通过。
worker先用纯CLR已持wrapper身份判断，不从worker读Pointer；未知别名wrapper与窗外异步回调仍不能证明全局无写，
`NoHostBagWriteObserved`只描述本次已核同步profile/原袋样本，不升全覆盖原生能力。
confirmed/unknown source墓碑、强refs和已进入业务保留；池重用须实际更高生命周期，失败不hot restore或重派未知调用。
限制含256owner、每owner32强引用、262144自写check steps和4096进度记录；step不计wrapper内部全部native调用，不是性能实测。

## 保存、返航与剩余目标

capture raw Grade指纹保持原样；FinalGrade/return policy、`BindEmployeeReturnPlan`、storage materialization及actual delta/save尚未由本流水线接通。
自动count映射完成不等于已入仓或已保存，EndDiving自然返回也不是返航receipt。Host原袋返航应沿自然链，不能再次manual Add。
Guest完整persistent/cache/输出隔离、原生ABI、全部worker/async副作用、完整共享progress/achievements、远距离双游戏和实际操作尚未执行或验证。
远处鱼兴趣来源仍是`NetworkController`收到的原PlayerFrame/`HostFishInterest`观察位置；本轮房主真实员工身体位置只接actor显示/捕获，
还没有替换allocator/LOD/visibility双区域兴趣。Roster可传当前Host同loaded scene的活动鱼，不按Host视锥过滤；客机按自己的camera裁剪。
默认关闭的生成/LOD/可见性实验仅覆盖有限原自然分支，不支持主客同时不同层，不能声称真实员工body已维持远区海洋。
通用Native/World/Cargo/GuestStateIsolated权限仍false，NativeBagInventoryComplete/NativeExecutionImplemented/CrashSafeExactlyOnce仍false。
installed.12、最近dive.11、默认发行包.0保持历史；本轮开发DLL没有部署。
完整M3—M7仍active：每人独立袋/容量/重量/负重、可信员工装备与生存、完整产物前置分流、逐项正常返航delta/save、
同海洋远区域两人实际玩法、Guest完整隔离、真实双端验收与GitHub冷配置/测试发行仍必须完成。
