# 每人独立背包的账本同步

用户确定房主和员工各有自己的背包、容量和负重。房主原生 LootBox 只承载房主自己的物品，员工产物保存在房主持有的独立 Mod 账本中。容量不合并，不能把员工捕获先放进房主袋再复制。玩法归属及返航方案见 [CREW_MODE](CREW_MODE.md)。

0.1.24-dev / 协议 6 增加只读账本传输。它连接已有 `ExpeditionCargoLedger`、`CargoInventoryController`、`SessionPeer`、编码器和回环 TCP。没有游戏适配器创建或接入经过原生核实的潜水账本；正常打开房间不会产生假物品或新背包开关。原生捕获、员工分流、容量读取、负重效果、入仓及存档隔离仍待实现和实机验收。

## 数据的含义

| 字段 | 含义 |
| --- | --- |
| ExpeditionId / MemberId | 房主账本中固定的潜水与成员身份；不从昵称或新连接的 PlayerId 推断 |
| SourceRoomId | 固定来源房间；旧账本不自动迁移到新房间 |
| Generation / Revision | 房间内单调的传输版本，独立于每个 BagRevision |
| Capacity / Weight / ReservedWeight | 各人的账本记录；不声明当前原生背包采样已验证 |
| Inventory | 已确认且追踪到的捕获产物，包含 CaptureId、ProductIndex 和原产物数据 |
| ReservedCaptureCount / UnknownCaptureCount | 已预约及已进入但结果未知的捕获数量；不能超时清零或重放 |
| PendingReturnProductCount | 账本内尚未完成确认的返航产物数量；不是完整仓库清单 |
| Phase / ReturnId | 本次潜水的账本阶段和返航标识；不证明原生返航或保存成功 |

`LedgerObservationOnly=true`。`NativeBagInventoryComplete`、`NativeExecutionImplemented`、`CrashSafeExactlyOnce` 都为 false。房主原袋可能包含没有经过此账本的物品，不能用 Inventory 求和替代房主总重，也不能把已记录重量标成实时重量。

原账本 `Returned` 时 Inventory 为空，但 Weight 不会因此归零。该阶段仍是历史记录，不能据此显示“当前负重已清零”。返航入仓的逐项执行状态仍由原账本 ReturnItems 审计，本通道没有派发或结算接口。

## 传输和生命周期

只有握手确定的房主可以发布，员工只能接收；封包与账本来源房间必须一致。未完成场景协商时也可以传账本，收到清单不会升级 Ready、采用世界或取得捕鱼权限。场景切换保留此数据，关闭连接清传输队列和接收显示。

两名成员、最多 256 次捕获，每次最多 8 个产物，总计最多 2048 条。每片最多 32 条、最多 64 片；空名单也需要一片。整批发布前复制及验证所有页和编码大小，接收全批且指纹一致后才提交。新版首片立即撤销旧显示，缺页不提交。重复冲突、伪造房间或角色、跨成员复制同一产物身份均拒绝。

独立的账本发送通道与动作、角色、世界、地图轮流发送，控制和心跳优先。已经开始的整批发完后才发送下一批；只保留一批最新等待快照，慢连接不会因不断收到新版而永远拼不出完整清单。场景清理不会重置账本传输版本。

主线程适配器只接受同一房主 peer 的一个外部账本，内容指纹变化才递增整体版本。Connected、预约变为未知、返航阶段等变化即使没有增加 BagRevision，也能产生新版。接收缓存随新批次首片失效。适配器及网络状态仅报告记录数量和传输次数，不启用游戏能力。

Disconnect 保留房主所持账本、已确认货物、未知执行屏障和返航记录，标员工离线。新的 peer、RoomId 或另一账本不能自动继承或覆盖它。重连成员恢复、后续潜水账本接替、跨进程持久恢复都需要额外真实生命周期和身份协议，本轮没有作出恰好一次崩溃恢复保证。

## 验证与下一步

运行 `development/scripts/Test-Core.ps1` 验证 CLR 账本投影、严格分页、原子接收、深复制、身份和版本围栏、公平发送，以及实际 TCP 与生产适配器往返；运行 `development/scripts/Build-Plugin.ps1` 编译插件。构建和测试的实际数量、哈希及未验证范围见 [cargo-transport-build-verification.json](../logs/cargo-transport-build-verification.json)。

测试中的完整产物和能力事实均为 synthetic CLR 数据，不调用游戏原生方法。它们证明数据传输和账本保留，不能证明实际两份游戏捕鱼或返航。用户暂不方便试玩时继续源代码开发，不自动部署、启动或重复催测。

后续接真实潜水成员、各人容量及原生完整产物来源，再按操作归属分流员工袋；房主原袋沿原返航链，员工物料按 ReturnId / CaptureId / ProductIndex 逐项入仓，未知原生结果不重派发。房主世界采用、客机存档与 AI 隔离、独立角色生存/装备/投射物、真实双端和 GitHub 冷配置仍是完整 M3—M7 的必需工作。
