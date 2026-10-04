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

报告只记录直接调用/外部跳转地址、精确方法指针匹配的候选名称及未解析计数，
不输出机器码或完整指令文本。同一地址可能被多个方法共用，最多展示16个别名，
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
