# 远端鱼叉头显示

0.1.35-dev 把当前本地戴夫的外部鱼叉头接入现有 PlayerFrame.Parts 传输与 NetworkAvatarRenderer。真实 Host/Guest 显示对方自己的采样；Local test 仍是本机 TCP 偏移显示。当前改动只同步原游戏自然生成的鱼叉头外观，命中、伤害、捕获和原生员工武器另需房主裁定。

原 LocalAvatarCapture 仅扫描 Player 的子树。原鱼叉头离开该子树后，即使戴夫的发射动作已同步，独立鱼叉头也不会进入角色帧。这次在每帧采样中单独解析当前玩家的鱼叉关系，保持原静态身体部件集合，避免发射或收回引起角色显示反复清空。

源关系使用 `PlayerCharacter.m_InstanceItemInven`、inventory.harpoonHandler、handler.harpoonProjectile 与 handler.projectileRenderer 的 direct 字段代理。还核对 projectile.m_HarpoonHandler 反向关系、Owner 属于当前玩家、场景及原采样前后的实例身份；manager 的 direct 玩家 backing 必须仍指向当前绑定戴夫。不调用 CurrentInstanceItemInventory 业务 getter 或主动发射鱼叉。只支持一枚 SpriteRenderer 鱼叉头，绳子、粒子与枪弹不在此实现范围。

动态鱼叉头与原身体 renderer 去重，使用独立显示代次的稳定槽，替换、失效、关闭或场景变化撤销旧动态绑定。关系已核对的鱼叉头从身体集合排除，每帧独立采样，因此正常发射和收回无需刷新身体集合。关系未知时保留原身体扫描；真实装备或身体集合变化仍沿原刷新流程。读取失败或超过既有128部件、256字符槽限制时省略本帧额外部件并记录诊断，不把这种外观读取失败当成联机断开。

远端节点沿用现有姿态插值、资源键解析与清理。鱼叉头材质模板仅使用仍活着且资源键匹配的自身鱼叉头；没有匹配模板时使用 SpriteRenderer 默认材质，不套用身体材质。显示节点只包含显示组件，不创建原生鱼叉、碰撞体或奖励。

离线 Cecil 核对六个请求属性及两个 inventory 候选，明确 CurrentInstanceItemInventory 是业务 getter，而 m_InstanceItemInven 是 direct 代理；其余五个关系字段也是 direct 代理。另一个窄 metadata 报告核对 manager 玩家 getter 与 backing 字段，新增归属检查只读 backing。元数据签名与源代码编译不证明运行时字段 ABI、所有装备层级、鱼叉外观或双游戏运行。

本轮实际编译与复用 Core 验证边界见[构建摘要](../logs/harpoon-visual-build-verification.json)。新代码未部署或启动；安装版本仍0.1.12-dev，最近潜水验证仍0.1.11-dev，默认发行包仍0.1.0。用户方便验证时，确认发射、移动、转向、收回和断线清理，再逐步接房主裁定的真实员工武器与独立背包。
