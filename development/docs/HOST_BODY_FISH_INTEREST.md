# 房主员工身体的鱼区域来源

0.1.48-dev、协议11将合作鱼区域接到房主创建的员工物理身体位置。
[本轮实际记录](../logs/host-body-interest-build-verification.json)为373/373 CLR与本机TCP通过、插件Build警告视为错误通过，185个输入前后相同。
其中6组新Core夹具核对模式、身份、撤销、高水位、时钟和样本拥有性；它们不执行Unity身体读取或原生鱼挂钩。
本轮未部署或启动游戏，原生ABI、远距离效果及两个真实游戏仍未验证。

## 两种固定来源

`HostFishInterestKind.HostEmployeeBody`使用`CrewActorController.TryCaptureFishInterest`产生的员工窗口。
控制器固定当前peer、Room、员工member、actor及Scene，从自己持有的实际身体两次读取位置并要求逐分量相同，
随后冻结不可变`HostFishBodySample`；新窗口取代旧token，使用时再核同身体、控制器、scene及实际位置。
采点相同只说明本次有限读取一致，不证明原生全局原子性或身体ABI。

`HostFishInterestKind.RemoteObservation`保留旧角色帧观察模式，以实际接收帧的Room/player/包序号与接收时间冻结位置。
它不使用显示插值，不把观察位置当成房主物理员工、命中、捕获或收益许可。
启用合作员工模式后来源固定为身体：客户端姿态帧不能刷新、覆盖或复活身体兴趣；身体缺失或失效时没有帧回退。
身体兴趣的`PacketSequence/ReceivedAt/RemoteSampleTime`均为0，另用`MemberId/ActorRevision/HostSampleRevision/HostSampledAt`描述房主采样。

## 当前窗口与撤销

实际Network Update确认Unity线程后才能读取原生对象；构造线程、收到包或Local test不提供这项确认。
身体模式要求同一个当前Host/Ready peer与Room、玩家1/2、双方UsesCrewActor、实际当前actor revision及同scene epoch/key。
member必须canonical GUID，并在同buffer首次有效样本后固定；样本序号与actor高水位跨Clear保留，不因暂停或换层归零。
位置必须有限，房主采样时钟不得倒退，TTL最多1秒；客机远端时钟、最新输入包或姿态包的年龄不延长这份窗口。
身体在原点、静止或没有最新输入时仍可产生合法位置采样，不能把零坐标或未移动当作失源。

`HostFishInterestSource`同时核本地manager/player/Transform与原生Scene，以及员工窗口的实际同scene身份。
暂停、换层、actor退休、更换peer/Room、断线、过期、重入或读取失败都会撤销当前窗口；旧窗口不能重新成为当前来源。
这些绑定是本次区域适配来源，不能升级World/Cargo、Guest隔离或动作权限。

## 三处共用与覆盖范围

普通allocator、鱼LOD和鱼可见性三处都读取同一`HostFishInterestSource`，没有各自借用客户端显示位置。
来源不可用时不增加员工区域，保留原游戏路径；原房主区域仍由原逻辑决定。

- allocator仅支持已核普通生成协程、`minDistance=0`、`force=false`的匹配距离查询；Wave、其他最小距离及未知流程不推算。
- LOD仅对自然请求已绑定的鱼条目，在匹配原job正常完成后合并原区域与平移到员工位置的区域；保持独立X/Y迟滞、Z及原生命周期，不连成两人之间的大矩形。
- 可见性仅在已核同鱼更新范围、实际同鱼renderer的原false返回上补充严格正交镜头候选；不写renderer/GO或主动额外更新AI。

三处沿用已有有界profile和不支持分支；正交投影、同平面、当前Host已加载scene以及实际活动/已注册鱼的限制仍在。
来源改成真实身体不代表全部远区生成、全部鱼类型、全部激活writer或跨层加载已完成。
Host roster仍只采当前玩家同Scene的活动鱼且不按Host视锥过滤；Guest按自己的camera裁剪收到的显示，
未生成、停用、失去LOD或未知资源的鱼不能靠客机镜头恢复。主客同时不同层不在本入口覆盖内。

`Network.ExperimentalHostFishAreas`和合作员工入口仍保持默认关闭，不自动改配置。
原生挂钩、job/数组布局、实际生成/AI/镜头效果、性能及双游戏验收尚未执行；完整GuestStateIsolated、通用Native/World/Cargo权限仍false。
每人独立袋、可信装备与生存、捕获/返航保存及完整M3—M7继续按[当前计划](PLAN.md)推进。
