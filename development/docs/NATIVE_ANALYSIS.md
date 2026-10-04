# 离线原生调用分析

`scripts/Inspect-NativeCalls.ps1` 用本机已安装的 LibCpp2IL 和 Iced，读取原
`GameAssembly.dll` 与 IL2CPP metadata。它在独立 .NET 6 进程运行，
不加载或调用游戏程序集、不启动游戏、不挂钩、不部署，也不读取存档。
这是补充接口签名研究的开发工具，不是玩家安装步骤。

离线工具轮的记录对应0.1.15-dev、协议5；该轮没有修改或重新编译插件。
当前0.1.16已按本轮定位的入口接只读来源adapter，范围见[MAP_ORIGINS](MAP_ORIGINS.md)；这不扩大下面静态报告的证据。
此前142项核心测试和插件构建证据保持原范围；离线分析不增加实机验收。
最新安装及主菜单启动仍为0.1.12，最近完成潜水记录仍为0.1.11。

## 使用

需要已有框架的`BepInEx/core`解析依赖、`dotnet`目录的.NET 6库、
带Roslyn的.NET SDK和可运行net6.0的.NET runtime；不下载新依赖。
默认通过Steam注册表和库清单定位游戏，也可传`-GamePath`。
从仓库根目录运行：

```powershell
.\development\scripts\Inspect-NativeCalls.ps1 `
  -Method 'DR.AI.FishAISystem::AddDropItem_Impl','LootBox::Add','IngredientsStorage::AddFromLootBox' `
  -Depth 1 -ReportName 'loot-native-calls'
```

`-Method`是1到16个精确`Namespace.Type::MethodName`选择器；
全名必须唯一，匹配该方法的全部重载。嵌套类型用原始metadata的`+`及尖括号名。
工具另列同一类型中按`<方法名>d__`命名的`MoveNext`候选；这是名称关联，
不证明工厂或协程已执行。缺失或歧义选择器直接报错。

默认展开深度1、最多128个方法、每方法8192条指令。
深度允许0到3、方法1到256、指令64到32768；根方法遗漏、方法配额、
指令截断、共享地址别名截断和不可用范围都会明确记录。
`Depth=0`适合精准复查原方法与相关协程，避免泛型别名消耗展开额度。

报告固定放在忽略的`development/.local/analysis/<ReportName>.json`，
各次编译与请求使用独立GUID目录。成功前后核对两个原文件SHA256，
完成后原子替换报告。失败保留旧报告并报错，旧报告不能作为本次成功证据。
时间、原文件哈希和分析边界属于报告身份，不能只凭同名文件存在判断新鲜度。
报告、解析日志、原文件、原始反汇编及依赖DLL均不提交Git。

## 地址与证据范围

工具从原metadata绑定方法指针；生成的interop DLL仅有包装器，不能替代原方法地址。
Windows x64 PE的exception directory提供有界`RUNTIME_FUNCTION`范围。
工具按version-1 `UNW_FLAG_CHAININFO`的完整Begin/End/Unwind三元组追到共同父项，
合并有明确关联的代码片段，不按相邻函数、同名方法或下一方法地址猜范围。
检测循环并限制单次递归追溯深度为32，未知unwind版本只读本项并标记不支持。
无表项、内部或次级入口不猜主体；最多32个关联片段/262144字节，
只解码唯一文件映射且可执行的代码区域。

原文件有421353个runtime-function表项；解析到283291个metadata方法定义，
parser按本机Unity版本识别为metadata31.1。定义数量不是可调用方法数量。
3个unwind项版本不支持；本次选中并解码的片段均为支持的version 1。
结构依据：[Microsoft x64 exception handling](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64?view=msvc-170)，
metadata版本依据：[LibCpp2IL metadata实现](https://github.com/SamboyCoding/Cpp2IL/blob/development/LibCpp2IL/Metadata/Il2CppMetadata.cs)。

报告默认只记录直接调用/外部跳转地址、精确方法指针匹配的候选名称及未解析计数，
不输出机器码或完整指令文本。开发时可显式 `-IncludeInstructions` 保留有界私有指令文本，范围与验证见 [货槽观察](LOOT_SLOT_OBSERVATION.md)。
`-MaxInstructionTextPerMethod` 默认2048、上限8192；`-MaxInstructionTextTotal` 默认8192、上限16384。每条最多256 UTF-16单元、全报告最多1048576保留字符，整条省略会另标文本截断，不改变原解码边界或方法完整性。格式化临时分配不受保留字符预算保证；原文本/地址/立即数仍只留.local，不提交Git。
同一地址可能被多个方法共用，最多展示16个别名，
总数和截断另外记录；即使只有一个`Singleton<T>`候选，具体泛型T仍未解析。
间接调用、虚调用、delegate、泛型实例、分支条件及数据流保持未知。
已知unwind族全部解码也不证明方法完整或指令可达；partial/缺边不是业务不存在的证据。
静态目标不能证明运行顺序、捕获成功、库存增量、写盘完成或世界权限。

## 本次定位结果

| 报告 | 方法记录 | 根选择器 | Partial | 其他不可用 |
| --- | ---: | ---: | ---: | ---: |
| loot-native-calls | 21 | 3 | 0 | 0 |
| expedition-native-calls | 211 | 7 | 5 | 57 |
| map-origin-native-calls | 59 | 7 | 0 | 18 |
| guest-save-native-calls | 56 | 5 | 0 | 1 |
| map-load-owner-native-calls | 19 | 14 | 2 | 2 |

各报告根均收齐、方法配额未触顶。不同报告可能重复记录同一方法，数量不能相加当作独立接口数量。
共享地址和partial记录保留原限制。精简验证摘要见
[native-call-analysis-verification.json](../logs/native-call-analysis-verification.json)。

- 鱼`AddDropItem_Impl`存在到`LootBox.Add`、`AddIgnoreOverloaded`、
  `SaveData.AddLootingSaveData`的直接目标；袋`Add`包含`CheckOverloadedState`和`Add_Impl`目标。
  因此员工产物、容量和水下进度必须同归属分流；只复制房主袋或只挡最终保存不足以隔离。
- `IngredientsStorage.AddFromLootBox`的3段关联代码包括实际六参数
  `Add(ingredientsID,parentID,rank,grade,count,place)`目标。
  这些参数是下一步只读转换观察点；仍不能自行拿鱼TID/品质猜数量，或直接新增奖励。
- 正常返航`Normal.MoveNext`有Result与Finished工厂目标；Result有鱼肉、
  鱼卵及多类物料处理/清袋目标。采集物、关键物品、种子和装饰进入不同库存路径。
  工厂、void、原bool均不能当作整个返航或保存确认；跟踪产物需按类别逐项核对。
- 游戏数据保存基类存在Serialize、加密、目录创建和文件写入目标；
  Player加载基类还包括Deserialize、云加载、转换、文件复制/删除。
  `SetLoadedData`包括基类与互动状态同步，不能作为无副作用的简单赋值。
  员工影子状态必须覆盖两个根、互动数据、运行缓存/在途引用和集中持久输出。
- `cacheSelectedScenePath`包含`GetGameSave/ResetSceneMapLayerCacheList`目标；
  `IGP.Init.MoveNext`包括`GetRandom/Instantiate/GetSaveableInterface`。
  客机只在postfix换路线或只跳过随机选择不能隔离原存档路径。
- `SceneLoader.coLoadAdditiveScene`及`CoLoadSceneAsync`的`MoveNext`直接调用
  Addressables五参`LoadSceneAsync(Object,LoadSceneMode,bool,int,SceneReleaseMode)`，
  绕过现有三参SceneLoader观察入口。当前观察不覆盖所有真实资源请求。

## 接入顺序

1. 固定加载工厂返回的iterator身份与owner，`MoveNext`每次恢复同一owner。
   父协程创建子协程时显式继承，不靠跨帧线程调用栈；旧协程不绑定当前Context。
   覆盖CoChangeLevel、LoadAdditive、coLoadAdditive、CoLoadSceneAsync及InGameManager.Start。
2. 在固定owner作用域捕获Addressables精确五参入口的typed handle，
   以操作指针/版本关联真实成功结果的`Scene.m_Handle`，再绑定首次controller寿命。
   SceneLoader中间层按同一操作去重；`OnDestroy`前退休，不能按scene名称倒推归属。
3. 客机路线提交早于`GetSceneDataCacheList`创建枚举器及资源请求；
   首次`GetRandom`采用唯一匹配的本地房主项，同时完成临时save与生成/AI隔离。
4. 验证实际产物转换、个人容量路由和正常返航链后，将Cargo账本接到真实Expedition。
   房主原袋沿原链，员工袋未入仓产物按类别逐项桥接；未知结果保持未决。

后续0.1.16已实现第1、2项的默认关闭来源观察adapter，具体出生时序与真实typed返回仍待验收；不证明完整加载覆盖。
第3、4项采用、guest隔离及个人分流/返航桥尚未接通；NativeGenerationBound、HostSelectionApplied、
GuestStateIsolated、NativeExecutionImplemented等能力保持false。
用户方便后的自然调用与双游戏验证仍按PLAN执行。

## 0.1.17 当前接线与下一桥

0.1.17 将当前固定来源 CLR 清单接入候选发送：建房时记录实际 Run/owner floor，建房前 entry、退休 Run/owner/controller 和旧回调不能提供新来源。集合删除或替换先退休旧 wire 代次再重发，每帧最多 8 条选择；诊断队列消费不影响当前清单。旧 Observe map selection calls 仅诊断，其关闭或丢失不发送/撤销来源。

新增 6 组 Core 快照和 6 组实际回环 TCP 适配测试，原 4 项源适配已迁移，总计 160/160 通过；Build 警告视为错误通过。测试使用合成标量，不运行 NativeHooks、NativeCapture、Unity provider 或两个游戏。NativeGenerationBound、HostSelectionApplied、GuestStateIsolated、WorldAuthority、CargoAuthority 仍为 false；未部署或启动。

当前行为见[固定来源候选传输](ORIGIN_MAP_TRANSPORT.md)和[0.1.17 构建摘要](../logs/origin-map-transport-build-verification.json)。0.1.14 的 callbackFloor/cache 来源与 0.1.16 的“仅日志”是历史范围，当前发送流程按新文档执行。

客机的原生 Serialize/Deserialize、双 Data/Interaction 根及直接恢复候选已离线定位，见[客机影子桥研究](GUEST_ISOLATION.md)。SaveData(string ver) 不是 JSON 构造器，SetLoadedData/Load 不是纯交换；旧协程、缓存、可变子树及全部持久输出仍需隔离与恢复验证。尚未执行原生克隆/根替换或证明 GuestStateIsolated。每人的独立容量和负重规则保持不变。

## 0.1.18 原生根桥与输出围栏源码

0.1.18新增实际typed原生影子桥、单次事务及已枚举输出围栏源码。四类Data原生JSON round trip、五根直接交换/回读/恢复和15个独立强handle已编译；7组新增事务夹具以合成backend验证partial/unknown补偿、fence/refs保留和一次清理，总167/167通过。

当前生产进入与静止边界恒false，事务在围栏安装前拒绝；startup primitive自身再查边界，未接Network/GUI，未运行克隆、根交换、阻断或恢复。194条精确声明不是所有writer、独立native地址或ABI证明；Interaction未Sync、完整子树/旧缓存/协程隔离仍待完成。全部GuestStateIsolated/NativePermission/WorldAuthority/CargoAuthority保持false，未部署或启动。

实现与下一步见[原生根桥](GUEST_SHADOW_BRIDGE.md)、[输出围栏](GUEST_OUTPUT_FENCE.md)及[0.1.18构建摘要](../logs/guest-shadow-build-verification.json)。下一步必须实现可信原生进入/静止边界与缓存/Interaction切换，再进行受控实机验证；个人袋分流、真实地图采用及双游戏闭环仍按原计划推进。

## 0.1.25 捕获分流关键根

本轮离线重查16精确选择器/18重载根/145方法记录，54条缺可用containing unwind；无方法配额触顶、根遗漏或指令截断。自然拾取包含普通与追加掉落，追加路径触及保底随机计数，Add_Impl还包含重量/槽位/任务等副作用。关键根未提供已证明的捕获协程来源。原报告仅存.local/analysis/capture-bag-diversion-critical-calls.json；静态边与共享别名不证明数据流/完整运行顺序。精确声明另由Inspect-CaptureLineageApi离线核对7types/16hooks/5fields/owner5层。生产时机限制及观察语义见[CAPTURE_LINEAGE](CAPTURE_LINEAGE.md)，本轮不执行游戏/native/save。

## 捕获选择与提交的最新研究

插件保持0.1.29；本轮只执行新离线研究工具，不重复原226测试或插件Build。普通拾取按CarvableCount逐tier，死鱼身体为另一个tier1配方；主选择也会随机，追加只一次并改保底/dirty。原Add的成功返回在实际Add_Impl之后，槽前已经有负重效果，原容量并非整批预期重量检查。SuccessInteract的UnityEvent转发不提供已证actor归属，回收/尸体状态也非捕获凭证。按[员工选择与提交桥](FISH_YIELD_BRIDGE.md)实现明确合作批次规则；不再增加镜像Core Gate，先接实际成员、选择/分流producer与现账本。完整M3—M7和实机闭环继续必需。

0.1.30补两组新精确私有报告：GetPickUpGrade一方法66指令，grade helper三方法148指令（其中IsInInvenType无containing range，不猜叶body）。它们改变下一桥的顺序：品质选择也计入已进入RNG/可能cache副作用；与主/Plus一起一次固定。完整方法、closed generic选择/cache、最终产品与native ABI仍未证明，原文本/地址不发布；构建与236项synthetic/Core验证见[EMPLOYEE_FISH_SELECTION](EMPLOYEE_FISH_SELECTION.md)。

0.1.31补一份AddFromLootBox精确Depth0私有报告，1方法112指令、1间接call、无截断/配额；完整方法/间接调用仍未证。原metadata实际3类14getter，确认TID编号与DR.Items接口支持范围。新源/测试为捕获产品归一化，不执行game；原报告/文本/地址仅.local，见[产品桥](EMPLOYEE_FISH_PRODUCTS.md)。

0.1.32无新PE执行，仅新私有Cecil原语/直接字段绑定核对，13声明42代理＋6storage/save代理。利用已知六参形成实际typed Add helper，同ledger进入后一次调用；没有原生运行/转换策略/入仓或存档证明。原报告/IL/依赖仅.local，构建与Core范围见[EMPLOYEE_RETURN_PLAN](EMPLOYEE_RETURN_PLAN.md)。

0.1.33仅新增一份exact count helper Depth0私有PE分析，1方法48指令7direct edges/0indirect/1known range，48text/757字符/0省略，无quota/truncation/invalid/overrun；完整method与公式内部语义未证。另私有Cecil4types/2lookupdecls/2pointerctors/5directInt32及4classinit。原地址/IL/指令不发布，摘要见[EMPLOYEE_RETURN_MAPPING](EMPLOYEE_RETURN_MAPPING.md)。

0.1.34新两组私有PE：3roots/96methods/6212instr/920edges/78ranges/24indirect/3invalid触方法quota，14roots/14methods/2513instr/475edges/22ranges/3indirect/2invalid。文本6209与2511无省略；invalid无正常text，不能把文本数称instr数或把noTextOmitted当fullbody。automatic caller countdelegate非null但targetunknown，UI null输入CellData可能已Convert；缺Apply caller不证明不存在。原报告/地址/文本.local，见[自然规则观察](RETURN_GRADE_OBSERVATION.md)。
