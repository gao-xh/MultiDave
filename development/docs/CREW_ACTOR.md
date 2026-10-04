# 房主控制员工移动

当前个人袋生产接线：0.1.46-dev/协议11见[CREW_CARGO](CREW_CARGO.md)，真实Room成员现已接潜水账本/ownweight与有限Harvest源码；下文保留0.1.44移动实现与验证历史，原生捕获/进度/返航保存和完整两机仍未执行或验收。

历史0.1.45-dev另接[员工独立鱼叉](CREW_HARPOON.md)，协议10；下文保留0.1.44移动接线的历史实现范围，武器接通与实际验证以新页及新摘要为准。

0.1.44-dev、协议9新增实际输入、房主物理驱动与员工状态生产路径，默认关闭。
本页描述源码接线；实际CLR/TCP及插件编译结果见[本轮摘要](../logs/crew-actor-build-verification.json)。
尚未执行Unity创建、碰撞查询、位置校正、独立氧气或双游戏画面，不代表M3—M7完成。

## 配置与房间

`Network.ExperimentalCrewActor` 在首次实际 Network Update 固定，默认false。
Host/Join握手同时显式启用 `UsesCrewActor` 才接受这条通道；Local test不创建员工身体。
客机还需要本进程自然初始化的 `Startup.ExperimentalGuestInitialization` 和当前临时进度/鱼隔离来源。
单独普通Join不会宣称已具有临时角色来源。本轮不修改本机配置、部署或启动游戏。
Guest完整持久隔离及原生ABI仍未证明，不通过此开关升级任何通用世界、捕获或背包权限。

房主首次绑定真实Room/player2后生成本房成员token，再生成单调actor代次。
这个token尚未注册为CargoLedger成员；Room/Ready本身不证明原生角色存在。
实际身体创建和回读后才发送首份状态，成功发布后每次输入/移动还须核对运输层的当前actor代次。

## 输入与状态

新增 `CrewInput`（90）由客机发给房主，包含移动轴、瞄准轴、按键、场景、actor代次和输入序号。
不接受客机提供的位置、伤害、HP、氧气、产物或native pointer。
输入收发FIFO各32，满队列关闭整个连接/房间，保留失败证据；不覆盖中间按键意图。
`CrewActorState`（91）由房主发给客机，使用最新状态槽；控制优先，动作/显示/世界/地图/货物/输入/状态七路轮转。
状态源为实际房主Rigidbody2D的position和linearVelocity回读，不把速度计划积分成权威位置。

包封装固定Room、握手来源player、包序号和接收机时钟。两端输入序号高水位跨暂停/换层保留；
同房actor单调，同actor内状态revision与ack拒绝回退/冲突；新actor可以从revision1/ack0开始，迟到旧actor不能重新启用。
房主输入接收时间超过0.25秒即停移动，不采用客机时钟。
客机只使用当前actor的最新状态，接收后超过0.5秒停止发送/校正。

客机按WASD/方向键移动，Shift加速，鼠标位置提供瞄准。F11面板打开时发送中立输入；
新actor首条输入也中立，避免沿用旧角色按住的按钮。
Fire/Recall/Interact目前只观察固定步前上升沿位，多个同类边沿可能合并；
没有武器、捕鱼或拾取派发。后续真实武器应逐意图消费，不把诊断位当每次射击完成记录。

## 实际身体和碰撞

`HostEmployeeActorBody` 使用真实当前manager→player→controller→rb/collider及已加载scene/PhysicsScene2D。
从实际模板获取形状、尺寸/offset、尺度、层和当前层碰撞矩阵，只支持已核Box/Capsule/Circle有限配置。
转向与XY观察不提供员工位置；不支持的配置明确拒绝，不猜层或碰撞尺寸。
四个房主附近出生候选先通过实际scene专属Overlap查询，再启用自己的kinematic刚体与nontrigger碰撞体。
不复制原PlayerCharacter、脚本、tag、原武器/背包/输入/镜头注册，不替换房主角色单例。

真实FixedUpdate从身体回读起步，按房主速度配置提出移动，实际Collider.Cast检测环境。
32项满输出拒绝，使用实际命中距离扣skin保守截断，再一次MovePosition；返回只表示提交，不证明物理步已完成。
后续状态继续读实际身体。它是有限Mod运动规则，不等同原角色全部摩擦、trigger、ignore pair、事件或鱼AI目标逻辑。

确认Unity线程；每项原生读写前后核房间/场景/actor纯CLR围栏，分组核实际来源和自己组件身份/几何。
进程最多64次创建尝试、每体最多16显式引用、每操作最多8192前后检查；构造异常前分配不能声称全部保留。
已经进入的未知创建/移动/校正不重试。只停止与销毁自己的身体；Destroy是延迟请求，引用和强handle保留至进程。
清理失败锁存，不用下一帧重新造身体掩盖未知结果。

## 客机校正与显示

`GuestCrewActorCorrection` 只校正实际临时客机原角色的Rigidbody2D位置/速度，保留该角色原动画与镜头作为本地预测。
每组核实际manager/player/controller/rb、attached collider、native class/cache、scene和startup当前来源。
只接受同Z平面、当前actor；入口和首项写入前核latest状态，先消费revision，再一次position setter及一次velocity setter并回读。
开始写入后仍固定该receipt并持续核actor来源；同actor的新状态可能在此期间到达，下一轮另行消费，不宣称全过程只有最新状态。
原生setter不是直接backing field写入。来源在第一项写入前变化时取消；写入后变化或结果未知则停止，不重新派发。
实际动画/镜头/原运动缓存是否正确跟随尚未验证，不声称预测与原生所有缓存已协调。

房主的员工显示根采用房主真实身体回读位置；客机角色帧只提供动画/部件显示。
身体不可用时清显示历史，不回落到客机声明的姿态。
远处鱼兴趣目前仍沿[双观察区域](HOST_FISH_INTEREST.md)和[可见性补充](HOST_FISH_VISIBILITY.md)，
没有将观察帧伪装成可信身体来源，战斗距离将另接实际actor。
同层已加载范围才有此支持；自由跨层和全部远处鱼行为仍未完成/验收。

## 独立生存与个人袋

房主批准 `Crew` 配置固定速度、boost、HP/O2上限、每秒氧气消耗和容量，属于明示Mod规则，不是假称原生装备配置。
只有Ready且实际身体active的FixedUpdate消耗员工氧气；boost额外消耗，零氧禁止boost。
原生环境伤害/氧气补给/窒息/死亡处理和敌方选择第二目标尚未连接，HP有房主入口但无实际native producer。
同一peer/Room暂停或换层后继承HP/O2，零HP不复活；不同Room的新规则尚未绑定完整潜水账本生命。

容量是独立批准标量，实际BagWeight保持null、HasConfirmedCargoWeight=false。
本轮没有个人袋重量、容量前置分流、负重运动或捕获/入仓/返航收益接线；不读取房主LootBox充当员工袋。
断线只清身体/网络，不清已有潜水账本。继续按[每人独立袋](CREW_MODE.md)及
[实际捕获生产路线](CREW_CAPTURE_IMPLEMENTATION.md)接完整产物、负重和逐项返航保存。

## 验证边界

新CLR夹具覆盖源绑定、独立profile/氧气、过期/重放、输入边沿、实际身体回读语义与换层继承。
新增运输夹具含真实回环TCP的身份/输入/状态、take后暂停、旧协议/单端开关与接收溢出。
编译通过也不执行Unity Collider.Cast、Overlap、MovePosition、guest setter、原生接口或两台游戏。
后续须在用户方便时验证实际出生、墙体碰撞、分开游动、镜头校正、pause/换层/断线、远处鱼、武器/受伤、
各人容量/重量/负重、正常返航保存及GitHub冷配置/测试发行；当前默认包仍0.1.0、安装.12、最近潜水.11。
