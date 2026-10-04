# 房主地图清单到客机加载入口

0.1.36-dev 新增 `MapChoiceController.TryCaptureRemoteChoices`。实际 Core/TCP
271/271 与插件编译通过，见[验证摘要](../logs/map-candidate-build-verification.json)。
原生加载流程尚未调用这个接口，`HostSelectionApplied`、`GuestStateIsolated`
和全部游戏操作权限仍为 false；本轮没有部署或启动游戏。

## 当前清单接口

调用方必须是已经绑定的同一个 Guest `SessionPeer`，处于仍有效的房间。
接口立即消费 TCP 邮箱，再在复制前后核对房间、代次、修订和路线指纹。
返回的路线、场景与 IGP 选择均是独立副本；修改它们不会修改控制器缓存。
同一代次的新选择会更新，换代首片会撤掉旧路线，房主撤销或断线也会失效。

返回 true 只表示当前候选快照可读。`Route == null` 表示路线尚未收齐；
`Retired == true` 表示候选已撤销。完整路线也不证明全部 IGP 已到达。
加载消费者须按当前场景和控制器地址等待对应选择，并在原生写入前重新核对来源。
Guest 关闭本地观察不会撤销房主候选。清空或重新绑定不会重放已消费的旧邮箱。

## 原游戏采用入口

本轮离线读取原游戏的实际指令，确认了两个具体入口，但尚未实现采用桥。

- 路线在 `BuildMapLayerData` 或 `LoadSceneMapCacheFromSave` 前采用，需准备
  `sceneLayerDataList`、`selectedMapLayerCacheList`、`m_SceneRoadmap`、首尾层和
  总高度的完整一致状态。原缓存流程会写 `SaveData.ResetSceneMapLayerCacheList`，
  因而必须先建立客机进度隔离。当前 DTO 还缺优先级、权重及不可卸载配置；
  已加载状态属于客机本地，不能复制房主的运行状态。
- IGP 的 `GetRandomIGPSetInfo` prefix 可以返回客机列表中唯一匹配房主选择的
  本地对象，并跳过原随机选择。原 `Init.MoveNext` 后续自然设置当前 IGP、
  加载或实例化 prefab，并登记使用信息。对应选择未到时须异步等待下一帧；
  返回 null 会让原流程继续并标记初始化完成，不能作为等待方案。

还有一个房主来源时序风险：当前 manager 工厂只接受已经完成的精确场景加载操作。
如果 `InGameManager.Start` 工厂早于该操作完成，iterator 会固定为未绑定，
后续完成不会补绑定，路线可能无法发布。静态报告没有证明 Unity 的实际先后顺序；
现有 controller 的 pending 机制也没有解决 manager 的这一点。

## 客机隔离边界

`GameBase.LoadSavedData` 发起 `SaveSystem.Init`，不能当作加载完成通知。
`LoadAllData` 的四个 manager 加载及 turn 更新也不能单独证明没有旧引用。
`InitAfterSaveSystem` 首次 `MoveNext` 位于多种缓存、任务、奖励等自然初始化前，
是需要接入的具体候选窗口。现有七根桥只支持已存在且已审计的缓存，
缺失缓存需要支持随后自然出生的生命周期，不能主动调用 Init 或造空表补齐。

退出恢复同样需要先确认使用临时根的消费者已经退休。现有 Core 补偿先恢复根、
后检查静止边界；生产桥必须在不安全窗口拒绝 `RestoreRoot`，保留临时状态、
写入围栏与资源。围栏阻止持久输出，不能阻止旧消费者修改已恢复的原根。
Disconnect、已加载标志或单个销毁回调都不能替代这个边界。

下一步依次接真实初始化隔离窗口、完整路线 schema 与采用桥、IGP 等待和替换，
再验证两端同一世界与房主实体裁定。每人独立背包、员工武器命中、产物分流、
逐项返航入仓和保存仍属于完整 M3—M7 目标。

三组新离线报告共 25 个方法、5085 条解码指令、5082 条文本，
有 3 个部分解码方法及 1 个无可用范围的方法，没有配额或文本截断。
完整方法、原生 ABI、实际初始化顺序和隔离效果尚未验证。
原始指令、偏移及地址仅保存在 `.local`；公开摘要只记录自写结论、数量、时间和 hash。
