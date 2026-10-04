# 开发约定

- M1 玩家发现与 M2 回放基础验收通过；当前源码及部署版本为 0.1.11-dev，编译与 85/85 项核心测试通过，新进程加载/Update/网络入口已确认。用户确认单游戏 TCP 偏移鱼群可见、捕获原鱼时副本同步消失、关闭显示后恢复正常，操作和镜头正常；动画、完整捕获链、地图及正常返航/双游戏验收仍待完成。0.1.5/0.1.6-dev 实际鱼传输/生命周期历史证据及 0.1.7/0.1.8-dev 失败记录保留，0.1.9-dev 用户确认锁定身份后不再突然消失。完成情况以 `logs/DEVLOG.md` 和真实运行证据为准。
- 继续工作前阅读 `docs/HANDOFF.md`、`docs/PLAN.md` 和当前阶段的 `docs/GAME_API.md` / `docs/MULTIPLAYER.md` / `docs/WORLD_SYNC.md`；配置别人电脑时使用仓库根目录的配置 Skill。
- 0.1.11-dev 含房主目标反向查询、冻结的本地指针/代次 CLR 快照、完整收到的活动观察鱼群显示，以及 8 个原生交互入口的只读前后成对观察。鱼清单仅玩家当前场景，每鱼最多 16 帧；缺显示或离镜头不释放数字身份，原生 AI/碰撞/收益保持原样。
- F11 的 Display received fish roster / Observe host harpoon and fish interactions 默认关闭，房主观察仍须启用 Transmit read-only fish observations。交互在 prefix 固定绑定，postfix 复用；bool 只是原返回，HpAtDrain 只是主线程消费时读数，不代表捕获结果或授权。只卸载自己的挂钩。
- 加载后的路线/IGP 清单要求每个选中场景至少一组、查找结果与原注册列表一致，并在两个不同 Unity 帧稳定。跨机地址未验证，尚未在加载前采用房主选择；M4 接管、M5 裁定及 M6 收益未实现。构建范围见 `logs/fish-world-interaction-build-verification.json`。
- 0.1.11-dev 的 A03_01_02 本机 TCP 已记录 49 条 Ready 概要、53 条 FishWorld 状态，观察/绑定/可显示/可见最大 16、网格顶点 662，未知资源/缺 Visual/显示错误为零。8 个交互挂钩健康，42 条事件组成 21 对 CallId，覆盖 HarpoonFire、FishHookedByProjectile、FishDamage 和 SpecialDamage，两个原 bool 为 true；Win/Pickup 未见。回调/解析/未配对/查询错误为零，地图读取失败 Selected route incomplete。Local test 的成对鱼是原鱼加偏移诊断副本，同步消失不等于捕获副本。自己的挂钩卸载与 Disconnect 有标记；用户确认主动退出且未返航，正常返航保存未验证。
- 每次改动记录日期、目的、修改文件、执行的验证、结果、遗留问题与下一步。
- 路径通过 Steam 注册表和库清单定位，用户提供路径时尊重该路径，不硬编码本机 F 盘。
- 更新插件前保存并正常退出游戏，脚本不终止用户游戏进程。
- 不将任何游戏数据、互操作程序集、框架二进制、账户信息或机器运行日志提交到 Git。
- 修改 C# 后运行 `scripts/Build-Plugin.ps1`；修改安装代码后验证参数检查、校验失败路径和幂等安装。
- 修改纯 CLR 姿态、协议或传输层后运行 `scripts/Test-Core.ps1`；回环传输通过不等于真实双游戏联机通过。
- 原型成功条件是新启动进程的日志出现加载、Update 和场景标记；文件存在不能证明运行成功。
- 发行包只包含自写 DLL 和 manifest，更新 `config/dependencies.json` 后重新打包及校验。
