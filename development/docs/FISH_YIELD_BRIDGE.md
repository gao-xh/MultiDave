# 员工捕获的选择与提交桥

每人独立背包需要在房主的真实世界中固定捕获成员、选择产物，再写入对应个人袋。0.1.29 的19处观察不能暂停完整产物批次。本次离线研究进一步确认了原选择的分支和副作用；游戏执行桥仍未接通，不能从这些静态证据开启权限。

## 已核对的路径

| 入口或阶段 | 已知范围中的行为 | 对执行桥的影响 |
| --- | --- | --- |
| `FishAISystem.SuccessPickupFish` | 先处理tier1，再依据FishInfoData.CarvableCount继续主掉落；最后处理一次追加掉落 | 主产物不是固定一份。按顺序固定所有tier；不把非正数上限解释为空产物 |
| `FishAISystem.LootDeadFishBody` | tier1主产物与一次追加路径 | 这是另一种配方，不能套用全部CarvableCount层级 |
| `DataManager.GetFishDropItemID` / `GetFishDropItemByTier` | 支持的已知分支包含按tier筛选、权重汇总和随机选择；另一分支直接取首项，缺项返回哨兵值 | 主产物也可能随机。每次选择的原结果必须固定，不在提交或容量失败后重新查询 |
| `FishPlusItemPity.RollPlusItem` | 根据原品质参数查询追加物，更新保底计数；无追加物有明确哨兵结果 | 一次捕获的追加选择只运行一次。保留无追加物，不能补roll |
| `SaveData.SetFishDropPityPending` | 已知路径会更新保底数据，可能创建缺失记录，并标记存档变脏 | 选择阶段已有房主进度副作用，不能把异常或容量拒绝当作“原生未进入” |
| `LootBox.Add` | 查资源、检查原容量、调用Add_Impl，正常结束才返回true | 不能跳过Add_Impl后伪造成功返回 |
| `LootBox.AddIgnoreOverloaded` | 绕过原容量检查后调用Add_Impl | 不能借此实现员工容量检查 |
| `LootBox.Add_Impl` | 槽创建/合并之前刷新负重阈值和玩家效果；槽修改后再写实际袋重量，另有任务、解锁等处理 | 只拦新槽、setter或最终AddLootBox都已经太晚 |

原容量的已知路径主要检查当前袋重量是否超过阈值，并检查物品接口返回的重量是否大于零；它不是统一的“当前重量＋整批新重量≤容量”函数。现有账本的容量策略是明确的Mod规则候选，尚未证明与原游戏相同；不能用房主的原容量getter为员工授权。

所有结论仅针对实际检查到的已知PE/unwind范围。没有证明完整方法体、虚调用/委托的全部目标、运行分支、字段ABI或资源映射。原指令、地址和完整报告保留在 `.local`。

## 入口与终态不能按名称猜

`FishInteractionBody.SuccessInteract(BaseCharacter player)` 的已知代码按交互类型转发UnityEvent，不能从参数存在就证明它直接调用拾取、传递了actor或固定了订阅者。`OnSuccessPickUp`另取交互体与拾取品质再调用SuccessPickupFish；原生事件、回调归属与异步边界仍需核对。

静态方法槽与本机运行库结构提示末端虚调用候选为DestroySelf；实际子类override、回调与ABI未执行。已核字段区分尸体状态和捕获状态，不能把回收/停用、DestroySelf或鱼消失当作捕获和入袋凭证。

当前Gate一概拒绝TargetDead，完整收到的鱼显示也隐藏Dead；活动尸体本身仍可能保留房主实体编号和代次。后续死鱼拾取需同时接入真实可拾取阶段、目标显示、空间条件和操作适配，不能仅放宽一个布尔条件。

## 接下来实现的员工批次规则

首版员工使用显式的合作批次流程：固定捕获来源配方，按tier顺序选择主产物及一次追加产物，全部暂存后检查员工个人容量，再单次提交。房主自己的自然捕获继续使用原游戏路径。员工流程需要独立规则和验收，不声称复刻单机“选择一项→入袋→再选择”的全部随机消费顺序；中间任务、事件或解锁的影响仍需审计。

1. 固定真实Session成员、员工actor、装备版本、源鱼代次和支持的交互阶段。Room Ready、显示角色或最近观察事件不能代替这些绑定。
2. 使用现有ExpeditionCargoLedger的SourceReserve和EnterSelection。Enter之前必须已有可用的整批选择隔离能力；选择中的RNG/保底写入也计入已进入状态。
3. 一次性保存每个tier的原drop结果、原追加结果及精确资源引用/参数，明确空项和不支持分支；不重新调用原AddDrop入口作为提交，也不重新抽随机。
4. 核实产品ID、原品质与最终品质时刻、有效重量和员工容量策略后，使用同一原租约LateSeal。容量拒绝保留同一批次；不能换成轻产物或丢弃未知屏障。正常返航前必须明确处理挂起结果。
5. 员工物料写员工Mod袋，共享图鉴/任务等房主进度走已核路径；覆盖现有槽、负重、图鉴、终态和副作用。不能临时借用原LootBox，其内部仍可能引用房主全局SaveData和Player。
6. 取得真实单次终态与分流凭证后才ConfirmCapture，接现有CargoInventory投影；返航按原约定逐项入房主仓库，未知结果不重派发。

账本的来源租约、容量挂起和返航屏障已经存在，不再新增镜像Gate。实际生产缺口是可信成员绑定、原生选择/提交producer，以及把二者接入现有账本。GetPickUpGrade的装备算法、最终品质additive来源、子类终态和caller进度顺序仍待补齐。全部M3—M7、客机隔离、房主世界采用、独立武器/生存状态、实际双端和冷配置继续按[PLAN](PLAN.md)推进。

## 复现与证据

`Inspect-FishYieldApi.ps1`离线读取生成的互操作程序集，核对精确业务声明、直接字段和接口；不调用游戏方法，不安装挂钩或读存档。`Inspect-NativeCalls.ps1 -IncludeInstructions`对精确选择器输出私有静态报告；工具限制见[NATIVE_ANALYSIS](NATIVE_ANALYSIS.md)。

本次新报告与原文件哈希、配额统计、实际命令及未验证项见[研究摘要](../logs/fish-yield-analysis-verification.json)。插件源码版本保持0.1.29；源码/测试输入未改时复用该版实际226项测试与编译，不能把复用算作本轮运行，也不将工具执行当原生玩法验收。
