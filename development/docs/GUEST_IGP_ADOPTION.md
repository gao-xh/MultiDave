# 客机采用房主场景生成选择

0.1.39-dev、协议7在默认关闭的实验客机初始化中加入场景加载来源和IGP消费者。
它延续[路线加载前采用](GUEST_MAP_ROUTE_ADOPTION.md)，由原游戏的Init协程使用本地已有资源继续生成。
本轮验证结果见[验证摘要](../logs/guest-igp-adoption-build-verification.json)；实际Core/TCP292/292通过，新增7项controller来源夹具；插件Build警告视为错误通过。完整stdout/PASS清单、执行UTC、源与显式工具/引用hash已留存，源码/脚本执行前后相同。
未部署或启动游戏，安装仍0.1.12-dev，最近潜水验证0.1.11-dev，默认发行包0.1.0。

## 场景和控制器来源

`NativeGuestSceneController`依赖实际入海、原Reset正常返回、六根安装和同一Guest peer/room。
原工厂返回固定协程对象，精确MoveNext在自己的来源范围内运行。
初始CoLoad沿原CoChange调用取得来源；普通换层和附加层加载还核对本地catalog、
本Mod实际分配的roadmap记录，以及原loader当前scene manager所属的已加载场景来源。
当前owner、场景名字、网络DTO或调用方true标记都不能单独认领对象。

Addressables五参入口的原typed返回必须实际运行并正常返回，才保留其operation及version。
读取直接status、result和实际Scene handle后登记完成；使用控制器来源时再次核对同一原生operation。
IGP出生冻结实际actor、Unity对象身份、scene handle和出生前已经登记的operation集合。
只允许集合中精确完成的operation绑定一次；后来的同owner请求不能补借旧birth。
Init工厂的原返回与`_Init_d__16.__4__this`固定绑定，该专用协程可等待出生来源完成。
普通owner0协程不会因此得到后来owner。

未知嵌套协程和工厂遮住父来源；已有未知范围时不能通过roadmap或当前manager重新认领。
自然调用返回后按配对token恢复范围，finalizer只处理CLR生命周期和来源失败，不读Unity字段。
场景卸载、controller销毁、Context清理、新入海、换代、断线和未知异常会撤销来源。
已分配的强引用、句柄和未知临时状态保留至进程结束，不热恢复或重新尝试未知操作。

如果IGP出生早于原Addressables返回而没有任何已登记eligible operation，当前实现拒绝认领。
这项真实时序尚未验证，不能据本轮静态接口和编译结果宣称已经覆盖。
boss、InstantLoadAdditive、特殊事件等其它加载流程也尚未证明完整支持。
普通返航清理后，已安装来源与后续无所属加载的区分仍须补齐；当前加载入口可能拒绝此类调用。
本轮没有验证正常返航，不能把来源失效保护当成完整场景切换支持。

## 等待和唯一资源选择

`NativeGuestIgpController`挂钩原Init、固定MoveNext、GetRandomIGPSetInfo、
static IsDoneAllIGPInit和OnDestroy。原Init的0/1/2依赖等待状态保留原state/current。
实际场景来源或房主选择未到时，本次Move返回继续等待，不手动注册controller或用null冒充选择完成。
已经等待的本次控制器不能让全局Done提前通过；没有token时仅实际已开始的加载或
已完成场景中房主明确声明而尚未出生的组构成等待，不把尚未开始的整条未来路线当成当前等待任务。
bootstrap的完整原调度仍需实机检查。

`NativeGuestIgpSelection`只解析controller的真实本地IGPSetInfoList。
按全层级地址、scene、模式和资源名定位唯一info；不用随机、条件或存档业务getter再次确认。
addressable以唯一prefabName匹配，允许加载前Prefab为空；nonaddressable必须有实际活着的本地Prefab且名字匹配。
info、列表/数组结构和版本、actor、scene-operation来源、房主代次与资源身份持续检查。
跨机层级地址一致性尚未实测。

第一次原随机入口只返回固定的本地info，不再次随机。
提交前严格验证；原协程已经选择该info后，addressable的Prefab允许从空自然出现一次，
保留并固定其实际身份。后续替换、失效或未知保留失败不重查、不重选。
这个字段变化不能证明完整addressable资源来源。
原Move结束还核对实际IsInitDone、CurrIGPSetInfo和CurrIGPSet，不仅依赖bool返回。

## 有界保留与证据范围

来源登记保留owner/iterator/operation/scene/controller的历史围栏，配额不淘汰旧身份。
本地选择helper最多256个owner，每个最多5个明确强句柄，每次最多8192个受守卫的原生步骤。
重复帧等待不使用永久累计读取次数作为终止条件，分配、生命周期与每次调用仍有上限。
partial内部构造/ABI和所有消费者引用的完整保留没有被证明。

NETWORK_STATE新增ExperimentalGuestSuppliedIgpChoices和ExperimentalGuestIgpStatus；
日志DAVECOOP_GUEST_IGP_WAITING_SOURCE、WAITING_CHOICE和CHOICE_SUPPLIED描述当前源码消费者的事实。
即使将来出现CHOICE_SUPPLIED，它也不能单独证明生成、共享鱼AI、完整客机隔离或收益授权。

本轮Core新增7项controller来源/协程/生命周期夹具，仅运行真实CLR登记器；全套另包含既有回环TCP，
不执行Unity/native hooks或新生成消费者。游戏能力、HostSelectionApplied通用许可、GuestStateIsolated、
WorldAuthority、CargoAuthority、原生字段/typed返回ABI、完整初始场景配置及真实双端验收仍为false或未完成。
后续继续生成/AI与持久状态隔离、可信员工actor/装备/生存/投射物、房主命中裁定、
每人独立产物袋/前置容量/重量/负重和逐项返航入库保存，以及GitHub冷配置和测试发行。

离线Cecil元数据实查5types/28properties，其中22direct、21selectedmethods，UTC2026-10-04T19:06:40.1855668Z→19:06:41.8273413Z；没有执行游戏代码或原生API。原metadata/wrapper IL/地址仅保留.local，公开记录只有自写来源说明、数量、UTC及hash。

## 0.1.40 原加载调用与退休增量

见[GUEST_SCENE_LOAD_LIFECYCLE](GUEST_SCENE_LOAD_LIFECYCLE.md)及新验证摘要。真实prefix登记固定Move加载调用，出生冻结同调用或已登记operation，配对原typed返回/实际成功Scene才绑定；专用pending manager仅为同场景子出生保留该call，未知0仍遮断且不授权加载。原IGP birth先于factory mask；Host typed返回核__runOriginal，异常/skip/finalizer失配撤证。精确自然退休后普通加载保原，仅自产unknown masks可留至配对退出，所有旧fixed tombstones拒恢复；refs/fence/临时根不释放。实际301/301与Build通过，native和正常返航未验证。下一步继续完整M3—M7、生成AI/持久隔离、员工actor/命中、每人完整产物/容量/负重与逐项返航、真实双端/GitHub冷配置，远区域与自由跨层仍待实现。
