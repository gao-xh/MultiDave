# 固定来源地图候选传输（0.1.17-dev）

0.1.17 将本机固定来源登记器的当前 CLR 快照接入已有协议 5 地图候选通道。房主发送路线和已解析的 IGP 选择，客机接收候选证据；没有采用房主地图、替换随机选择或开放生成、AI、捕获、库存及存档权限。

[地图加载来源观察](MAP_ORIGINS.md)记录的是 0.1.16-dev 的历史观察边界，包括原生入口、固定 scope、typed operation 轮询、限额和 ABI 未验项。其中“仅日志、不接候选通道”是该版本的范围；本页说明 0.1.17 新增的传输接线。原生 ABI、完整来源覆盖和跨机地址仍待实机验证。

## 当前来源接口

`MapOriginRegistry.TryCaptureSource(out MapOriginSourceSnapshot)` 在登记的 Unity CLR 线程读取当前活 owner。它返回 owned 路线及每个活 controller 最近一次仍有效的选择，按 `ControllerLife` 稳定排序。读取不消费 pending/ready choice 队列，不依赖诊断日志是否已经消费，也不读取 singleton 或 native wrapper。

| DTO | 当前字段与含义 |
| --- | --- |
| `Core.World.MapOriginSourceSnapshot` | `OwnerLife`、`RouteFingerprint`、深复制的 `Route` 或 null、owned `Choices` 数组 |
| `Networking.MapOriginSourceFrame` | 观察器 `RunId`、当前 `Healthy`、上述 `Source` |
| `MapOriginChoiceEvidence` | 固定 owner/context、operation、真实 scene handle/life、controller life、load key、callback sequence 和原选择副本 |

无活 owner 时，健康快照为 owner 0、空路线和空选择数组。新活 owner 尚未绑定路线时，owner 非零、路线与指纹为空、选择数组为空；后续同 owner 的合法路线可以补入。pending、unbound、退休 controller 或缺少确切 operation→scene 关系的选择不进入当前快照。登记器 fault 或读取冲突返回 false，输出为空，不能回传此前成功快照。

`MapOriginController.CaptureSourceFrame()` 在实际 Unity 线程上核对观察器健康，调用上述接口后再次核对；失败输出 unhealthy frame。它不从旧 `MapSelectionCallObservation` 的缓存拼路线或补选择。

`ObservationOnly=true`；`NativeGenerationBound`、`HostSelectionApplied`、`NativePermission`、`CrossMachineAddressVerified` 恒为 false。frame 的 `WorldAuthority`、`CargoAuthority` 也恒为 false。固定标量关系被接受不代表原生 ABI 已通过、完整世界清单已形成或员工可执行游戏效果。

## 主线程与房间接线

NetworkController 每次 Update 先更新来源观察器，再完成可能成功的房间连接。连接完成时调用：

```csharp
BindRoom(main, legacyCallbackFloor, originRunFloor, originOwnerFloor);
```

其中 origin floor 是当时实际观察器的 `RunId` 与 `ActiveOwnerLife`。随后在本帧调用：

```csharp
ObserveOrigin(mapOrigins.CaptureSourceFrame(), main);
Update(main, loopback);
```

这两步位于本地角色场景更新之前。只有已绑定同一 `SessionPeer` 实例和握手 Room 的 Host 能发布；Guest 只接收 Host 候选。`WaitingForScene` 允许传输，Transmit 鱼观察开关及 Ready 不构成地图发布权限，也不会因地图证据使会话提前 Ready。

候选仍使用既有 `MapRouteSlice`、`MapIgpChoice`、`MapChoiceRetire`。本机 Run、owner、Context 指针、operation/controller life 和 Scene handle 用于本地核对，不添加为跨机 native 身份，不发送这些指针。wire generation/revision 与本机 owner、房间 scene epoch 各有自己的生命周期。

## 跨房间和迟到来源围栏

房间绑定时封住 `originOwnerFloor` 及此前 owner。即使旧 entry 当时还没有路线，之后才收到 cache 或 operation 完成，也不能作为新房间的来源；需要观察到绑定后的新自然 entry。

同一 Run 的较新 owner 撤销前一个 wire 来源；同指纹的新自然 owner 仍创建新 wire generation。合法的新 owner 在路线尚未绑定时保持待定，不因空路线永久封住。已经发布路线的 owner 随后失去路线则被封住，不能以迟到快照恢复。

适配器最多保留 64 个 Run 的 owner 高水位、退休 owner 和退休 Run 围栏，跨 `Clear` 和换房保留，绝不为扩容淘汰。已退休旧 Run 再出现时直接忽略，不能替换或撤销当前较新 Run 的来源。unhealthy frame 撤销并退休相应 Run；容量耗尽锁存失败，后续帧不能继续发布。

正常关闭来源观察器撤销当前候选，正常重新开启使用新 Run。回调丢失、读取错误、错线程、scope 冲突及原生观察配额失败仍沿用来源观察器的失败锁存规则；有关重启和自己的挂钩清理见历史来源文档。

## 当前选择集合与有界发送

每帧处理的是完整的“当前已观察且可解析的选择集合”。这不证明所有游戏 controller 都已观察，也不代表完整 `MapSelectionManifest` 或加载屏障。

适配器先深复制并校验整帧：路线字段必须符合严格路线 schema，指纹必须一致；每条选择必须属于同 owner/指纹，具有非零 Context、确切 operation/scene/controller life、实际 scene handle、有效 callback sequence 和 load key。controller 的实际场景名必须对应路线 SceneId；同场景链、handle/life、controller 及 `(SceneId, ControllerAddress)` 不能冲突。地址仅为现有跨机候选地址，不证明跨机匹配已经成立。

Core 来源最多覆盖 256 个 controller；wire 选择上限为 128。超过 wire 上限时整帧拒绝并撤销来源，不截取前 128 条冒充完整集合。每个 Run/owner 的 controller 历史最多 256，包含已经消失的 life 围栏；wire 重建不清这段历史。旧 life 再出现、回调序号回退或相同序号携带不同选择都会拒绝整帧并封 owner。

每次 `ObserveOrigin` 最多发布 8 条新增或改变的选择。余项保留在 owned 集合中，下一帧继续；相同选择不增加 wire revision。来源快照独立于每 Update 最多 16 条的原生诊断消费预算，因此诊断 drain 不会消耗待发送选择。

若此前集合中的 key 消失，或同一 key 换成另一个 controller life，适配器先退休旧 wire generation，再发送新 generation 的路线与当前集合。这样客机不会继续保留已销毁或卸载 controller 的旧选择；存活 controller 的本机 life 保持原值，仅 wire 身份重建。

既有地图 FIFO 为 32 包，路线每片最多 8 个场景、最多 4 片，控制撤销优先。发布被取消、队列满或 wire 来源已失效时，适配器撤销并封住当前 owner，不在下一帧自动重派发该来源。接收端完整路线原子提交，选择可分多帧到达；即便当前选择已发完，也不代表原生地图采用或完整世界准备完成。

## 关闭、撤销与客机行为

旧 `Observe map selection calls` 只保留独立水位、计数和诊断；其 `Observe` 不发布也不撤销固定来源候选。关闭旧观察器、旧 callback gap 或复制错误不会误撤本页来源。

Host 关闭固定来源观察器或其证据失效后，通过地图控制消息撤销自己的候选。Guest 关闭本地来源观察器只停止本地观察，接收到的 Host 路线和选择保留，直到 Host 明确 Retire、较新 wire generation 替换或房间关闭。Guest 本地 unhealthy frame 不得撤销远端 Host 证据。

普通角色场景/epoch 更新不清掉 preload 候选；真正的 origin entry、路线或 controller 生命周期变化由固定来源快照反映。Disconnect 关闭自己的观察器、关闭 peer 并清本房间接收快照；Run/owner 退休围栏保留。个人 Cargo 潜水账本不连接这条 Disconnect 清理链。

## 诊断与验证范围

`DAVECOOP_MAP_CHOICE_BOUND` 记录房间及 origin floor；`ROUTE_SENT`、`CHOICE_SENT` 带本机 Run/owner 与 wire 身份；`RETIRED`、`ORIGIN_UNUSABLE`、`CHOICE_CANCELED` 说明撤销原因；`LEGACY_SUPPRESSED` 表示旧来源仅作诊断；`RECEIVED` 表示客机候选进度。日志统一声明 candidate evidence、`NativeGenerationBound=false`、`HostSelectionApplied=false`，并保持原生采用及跨机地址能力为 false。

F11 显示候选和来源观察器状态；网络摘要另记录当前 origin Run/owner、发送/接收计数和待发送数量 `MapChoiceOriginPending`，其值来自 `PendingOriginChoices`。待发送数量为零仅说明这一帧已解析的集合发送完毕，不能解释为所有 IGP 完成或允许入海。

本轮 Core 快照测试覆盖复制、最新选择、同指纹新 owner、pending exact completion、退休及错误线程等边界；适配器测试编译实际 `MapChoiceController` 和实际 DTO，使用合成 CLR 来源帧、logger 桩与实际本机 TCP。它们没有运行原生 hooks、Unity、地图加载或两个游戏，不能证明 native callback、ABI、地址及采用成功。构建、测试数量与 hash 以本轮开发日志和验证摘要为准。

下一步仍需核对真实自然入海的原生链和跨机 controller 地址，再实现加载前采用与客机临时进度/生成和 AI 隔离；随后才能接房主原生裁定及个人袋/返航桥。详见 [世界同步](WORLD_SYNC.md)、[房主与员工模式](CREW_MODE.md)和[当前交接](HANDOFF.md)。
