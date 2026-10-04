# 员工独立鱼叉

0.1.45-dev、协议10增加员工实际输入、房主独立投射物、普通鱼原生伤害接线，以及客机自己的鱼叉显示。
这是开发源码实现；[实际验证](../logs/crew-harpoon-build-verification.json)只运行CLR/TCP和插件编译。
尚未运行Unity投射物、伤害入口或双游戏，不能把此阶段说成可完整合作捕鱼。

## 来源与配置

`Network.ExperimentalCrewHarpoon`默认false，首次Network Update固定。房主启用此项，并且双方实际Host/Join握手选择CrewActor。
Guest仍需真实自然初始化临时角色来源；Local test不创建武器。真实鱼解析还需房主开启`TransmitFishObservations`，并持有健康的当前鱼生命周期/清单。
本轮不修改安装配置、不部署或启动游戏。速度、射程、冷却、圆扫掠半径和整数字面伤害来自房主批准的`CrewHarpoon` Mod配置。
默认12单位/秒、8单位射程、0.5秒冷却、0.08半径、10基础伤害；不借房主当前装备、技能、品质、背包或客机声明的伤害。
这是一套有限Mod规则，装备等效和原角色全部鱼叉行为仍待后续接入。

## 输入和物理

客机每Unity Update采按钮，变化立即发独立CrewInput；无按钮变化时移动/瞄准30Hz。新actor第一帧中立，F11/暂停时中立。
只保Unity实际采到的按钮状态，不宣称获取帧间所有操作系统事件。鼠标左键发射，R召回。
房主在移动控制接受每条receipt后即时喂独立武器模型；不用OR合并的诊断Fire位派发。
模型保Room/actor/epoch/输入及包序列/房主接收时间，同类不同意图FIFO32，最多一根活叉。
冷却/活叉/过期拒绝消费该次意图，不等冷却结束补射；同一帧Fire/Recall升沿召回优先并消费Fire。
暂停/失源须新鲜释放Fire/Recall才重新接受；换层/actor重建保同Room输入、包和ShotId高水位。

每FixedUpdate用房主自己身体的实际position回读生成命令，投射物再次核发射原点完全一致。
读取身体实际Scene、碰撞mask和自己Transform；不从客机pose、显示插值或消息目标创建原生攻击。
同Scene的CircleCast先扫掠再一次写自己的Transform，并回读实际位置。
沿用当前身体模板的实际碰撞mask，包含trigger；未知trigger也停止，不推断为可穿越；它不证明原鱼叉完整层规则等效。
排除自己的actor碰撞体，选择最短命中（包括0距离）；32满输出、同距不同碰撞体冲突、无效来源拒绝，不穿墙另选鱼。
真实opaque hit保原minted命令/碰撞体及出生代次，不能从ShotId或显示状态重新猜命中。
超距停止，R首版直接停自己的活叉；还没有原生绳索、挂钩、QTE、完整收回动画或捕获。

## 一次原生伤害

只接受已核exact FishAISystem/SABaseFishSystem与Damageable普通候选；实际hit collider须为Damageable登记的HitCollider。
当前鱼清单、生命周期generation、native fish/defense/damageable/scene和碰撞来源均重新核对。
Core TryHit消费同一个Fire command后只产生damage intent；桥另mark进入未知，再构造自己的inactive攻击来源GO、Damager、HarpoonIDamager及AttackData。
Owner指向真实员工自己actor，攻击变换为实际hitpoint处的桥自有Transform；空buff数组为自己创建的native数组。
原生AttackType.Player_Harpoon/EElement.None已由离线声明核对；实际TakeDamage入口只派发一次，原过滤、抗性及defender回调沿原链运行。
原bool先冻结，再核source；正常伤害可能停用鱼，因此后核source不重新要求鱼仍活。
bool不是HP差值、捕获、掉落或入袋凭证。GetCaster、OnDoAttack等未知native leaf不猜noop，异常或已进入未知不重试、补奖、热释放。
原生body、值类型/接口ABI、回调副作用和完整方法覆盖仍未验证；不通过这个窄桥提升通用权限。

## 显示和清理

CrewActorState新增必须、唯一的HarpoonShotId/HarpoonActive/HarpoonPosition/HarpoonDirection四字段，协议10拒绝旧9/8及更早协议。
状态来自房主真实投射物位置回读；只供显示，不包含可授权的目标、damage/capture claim或native pointer。
活ShotId不回退；结束、暂停或换actor不能复活同ShotId；旧合法state revision丢弃，同revision冲突拒绝。
客机在最新fresh真实临时角色来源下创建自己的LineRenderer，不运行原武器脚本。
状态过期、校正/source失败、暂停、换层、Disconnect时隐藏/停止自己的显示；清理未知锁存，下一房间不重建掩盖失败。
场景、Room、actor、shot持续核对；already-entered physics、构造或伤害不重新执行。
Destroy只是延迟请求，强引用/handle保留，不声称已证完全销毁。

## 限额与剩余验收

投射物全进程最多4096创建尝试、每shot8显式引用、每调用8192自身Check；damage桥最多64owner/64碰撞解析尝试（墙和不支持目标也计）、每shot16显式引用/8192自身Check。
客机显示最多64创建尝试、4显式引用/每次4096自身Check；所有预算不包含wrapper内部及source复合getter的全部native调用，不是CPU性能证明。
限额耗尽或未知失败保留证据、停止本开发通道，需要重启；后续复用/性能仍须另做真实验证。
真实双端须验发射/墙体/鱼种/反应/镜头内外/召回、短按、pause/换层/Disconnect和原生错误；还要验同层远距离生成/活动/性能。
员工incoming生存伤害、敌方完整第二目标、装备、完整捕获/产物前置分流、个人袋/容量/重量/负重、逐项返航delta/save，以及Guest完整隔离、GitHub冷配置/测试发行仍属完整目标。
默认发行包.0、安装.12、最近手动潜水.11均是历史状态；本轮未访问游戏进程或已安装插件。
