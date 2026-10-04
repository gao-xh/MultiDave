# 玩家与摄像机发现

对应开发计划 M1。发布包仍为 0.1.0 加载原型；此开发分支源码为 0.1.1-dev。

## 离线证据

在已验证的 Steam Build `25315876` / Unity `6000.0.52f1` 上，使用 Mono.Cecil
读取本机 BepInEx 生成的互操作程序集，不加载或执行游戏程序集。
运行 `scripts/Inspect-GameApi.ps1` 可重复生成报告，保存在忽略的 `.local/analysis/`。

| 类型/成员 | 已知签名与用途 | 尚待确认 |
| --- | --- | --- |
| `PlayerCharacter : BaseCharacter : MonoBehaviour` | `transform`、`moveInput`、`LookDirection`、`characterAnimator` 可读取角色状态 | 不同模式中是否有多个实例及其生命周期 |
| `PlayerCharacter.Init(Vector3, bool, FlipState)` | 存在初始化入口 | 原始控制流、输入注册和任务监听副作用 |
| `InGameManager.playerCharacter` | 实例属性，类型为 `PlayerCharacter` | 船上和潜水时实际指向的实例 |
| `InGameManager.LoadCharacter / InstantiateCharacter(GameObject)` | 存在角色加载和生成入口 | 异步资源流程、单例及持久化行为 |
| `CameraManager.PrimaryTargetTransform / SecondTargetTransform` | 可读取跟随目标 | 跟随玩家根节点还是子节点，剧情时是否变化 |
| `PerspectiveCameraManager.m_Target`、`OrthographicCameraManager.Target` | 其他摄像机模式的目标 | 哪些场景使用这些管理器 |
| `CharacterController2D` | 包含刚体、碰撞体、移动与朝向属性 | 与玩家的组合方式及碰撞控制边界 |

互操作 DLL 的方法体是本机调用封装，不能当作原始游戏实现。
以上成员尚未用于创建第二角色、改变输入、设置摄像机目标或写入进度。

## 已观察到的潜水实例

用户实际进入 `A01_01_01` 潜水后，探针读到 `PlayerGroup(Clone)/DaveCharacter`。
`InGameManager.playerCharacter` 指向该 `PlayerCharacter`；
`CameraManager.PrimaryTargetTransform` 对应同一 GameObject 的根 Transform，
`SecondTargetTransform` 为 null。潜水中存在一个动画层，状态 hash 和时间随移动变化。
朝向曾读到 `(1, 0)`、`(-1, 0)` 和斜向，翻面伴随 Transform 旋转变化。
这些结果只覆盖本次基础潜水实例，其他场景和模式仍需实测。

同一次运行又进入 `Boss_000`：玩家组件和 GameObject 实例 ID 更换，旧实例在该场景
采样中消失，新玩家继续被 `InGameManager` 和 `CameraManager` 正确绑定，未出现读取错误。
该证据覆盖玩家探针的场景切换和实例替换。

## 运行探针

`src/DaveCoop/Discovery/PlayerProbe.cs` 在 Unity `Update` 主线程运行：

- 每 2 秒寻找活动对象上的玩家、游戏管理器及三类摄像机管理器。
  使用对象查找，避免调用可能创建单例的全局 `Instance` 入口。
- 每 0.5 秒读取位置、旋转、缩放、移动输入、朝向、动画状态及摄像机目标。
- 比较管理器引用与玩家组件实例 ID；通过目标 GameObject 及其父节点 ID
  判断摄像机是否跟随玩家，保留名称、路径和场景句柄供复核。
- 每次拓扑变化和每 5 秒的玩家状态写入 BepInEx 日志；F9 立即扫描并采样。
- 结构化 JSONL 位于游戏的 `BepInEx/plugins/DaveCoop/logs/discovery-*.jsonl`。
  默认每次启动最多 9000 条快照；达到上限后停止文件记录，保留实时观察。
- 各读取点独立处理托管异常并记录失败键；序列化仅接收普通 CLR 数据。
  托管辅助方法标记 `HideFromIl2Cpp`，只有 Unity 回调暴露给运行时。

配置文件：`BepInEx/config/local.davecoop.prototype.cfg`。
`Discovery.EnablePlayerProbe` 控制探针，`Discovery.MaxSnapshots` 范围 1..18000。
F8 显示或隐藏面板。该探针没有网络功能。

## 复现与验收

从仓库根目录执行：

```powershell
.\development\scripts\Inspect-GameApi.ps1
.\development\scripts\Build-Plugin.ps1
# 保存并退出游戏后部署，然后从 Steam 正常启动游戏。
.\development\scripts\Deploy-Plugin.ps1
.\development\scripts\Check-Status.ps1
```

开发版使用 Build/Deploy。根入口 `setup.ps1` 安装的是 `distribution/` 中的发布包。

进入存档、船上及一次潜水，移动与转向约 20 秒，按 F9，然后正常返航、保存退出。
在实际会话期间或退出后运行：

```powershell
.\development\scripts\Get-DiscoverySummary.ps1
```

汇总保存为 `.local/analysis/discovery-summary.json`。检查场景时间线、玩家 ID、
管理器绑定、位置/朝向采样、非零输入、动画和摄像机跟随次数，以及 `ProbeErrors`。
`CurrentProcessSession` 仅在对应游戏进程仍运行时为真；退出后的报告属于历史证据。
汇总中的玩家观察不自动等同于潜水验收，需结合实际试玩与场景时间线确认。

## 后续设计约束

M2 先研究只复制显示组件/姿态的方式。直接克隆完整 `PlayerCharacter` GameObject
可能运行 Awake、注册输入和其他监听；在明确这些行为之前，不尝试完整角色克隆。
远程显示对象不作为游戏管理器的本地玩家，也不绑定本地摄像机。
