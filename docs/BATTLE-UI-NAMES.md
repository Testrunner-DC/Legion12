# 对战界面组件命名清单

建议以后用“编号＋我方/对方＋修改要求”描述，例如“B04我方计时框向右移”“B10圣物区加宽”。这些是界面分区名，不要求记住代码文件；编号作为沟通用，不会显示在正式页面。

## 常驻场面

| 编号 | 名称 | 内容／位置 | 实现位置 |
|---|---|---|---|
| A01 | 本局天灾栏 | 左上圆形天灾图标及状态 | GameBoard |
| A02 | 当前天灾详情区 | 当前天灾卡图／数值信息 | GameBoard |
| A03 | 选中卡牌详情栏 | 左侧卡图、名字、属性、效果；弹框期间仍可查看 | GameBoard |
| A04 | 阶段轨道 | 天灾、重置、抽牌、士气、主要、结束及回合数 | PhaseTrack |
| A05 | 对战工具栏 | 左下设置、在线人数、连接等入口 | BattleUtilityDock |
| B01 | 我方／对方战场半区 | 各自整块场面及区域边界 | PlayerMat |
| B02 | 前排三格 | 前排1／2／3军团位 | PlayerMat |
| B03 | 后排三格 | 后排1／2／3军团／盖伏位 | PlayerMat |
| B04 | 回合与计时框 | 我方／对方回合状态、总时限及操作时限 | PlayerTurnClock |
| B05 | 手牌扇区 | 我方／对方手牌或卡背、数量、选择状态 | HandArea |
| B06 | 场上卡牌 | 军团卡图、数值徽章、可操作和选中标记 | PlayerMat＋CardTile |
| B07 | 主宰区 | 主宰卡图、血量与保护标记 | PlayerMat |
| B08 | 士气区 | 士气图标、活跃/休整、临时/锁定、数量 | PlayerMat |
| B09 | 阵营效果按钮 | 士气区旁阵营效果入口 | PlayerMat |
| B10 | 圣物区 | 圣物及额外圣物卡图 | PlayerMat |
| B11 | 额外区／试炼区 | 试炼卡、当前进度等特殊卡 | PlayerMat |
| B12 | 主宰专属资源栏 | 符文、卡诺匹斯罐等专属标记 | PlayerMat |
| B13 | 主牌库堆 | 牌背、余牌数 | PlayerMat |
| B14 | 墓地堆 | 墓地顶卡／数量、查看入口 | PlayerMat |
| C01 | 双方玩家信息栏 | 右上昵称、段位、称号；缺项留空 | GameBoard |
| C02 | 对局记录栏 | 右侧可滚动的结果日志、可点击卡名 | BattleEventLog |
| C03 | 对局操作栏 | 结束回合及当前合法操作 | GameActions |
| C04 | 顶部会话操作栏 | 连接状态、返回大厅、投降等 | GamePage |

## 按操作出现的组件

| 编号 | 名称 | 用途 | 实现位置 |
|---|---|---|---|
| D01 | 效果选择弹框 | 发动／不发动、分支、排序等 | PromptOverlay |
| D02 | 响应弹框 | 响应来源、效果详情、响应／不响应 | PromptOverlay |
| D03 | 选卡候选项 | 弹框内可选卡及详情入口 | PromptCardCandidate |
| D04 | 场面目标确认条 | 直接点场上目标后确认 | GameBoard |
| D05 | 登场位置提示条 | 绿色空位选择与预览 | GameBoard |
| D06 | 资源支付确认条 | 士气／返还／混合费用选择数量与确认 | GameBoard |
| D07 | 手牌调度弹框 | 先后手、调度卡、准备倒计时 | PromptOverlay＋SetupDecisionClock |
| D08 | 天灾禁选弹框 | 禁选／选择／确认及倒计时 | PromptOverlay＋SetupDecisionClock |
| D09 | 战斗对抗层 | 攻击者、目标、兵力、抵挡和支援操作 | GameBoard＋GameActions |
| D10 | 墓地浏览弹框 | 双方墓地卡牌与详情 | GraveyardOverlay |
| D11 | 主宰详情弹框 | 主宰信息和合法能力 | MasterOverlay |
| D12 | 阵营效果详情弹框 | 阵营卡文、动作、最小化 | PlayerMat |
| D13 | 沙盒控制面板 | GM卡牌、阶段与数值控制 | GmPanel |
| D14 | 沙盒选卡弹框 | 加卡或更换自定天灾 | SandboxCardPicker |
| D15 | 设置弹框 | 对战中设置入口打开的配置 | L12SettingsModal |
| D16 | 好友邀请卡片 | 右下局部邀请、撤回与最小化 | SiteShell |
| D17 | 胜负结算界面 | 结果展示、玩家确认返回 | GamePage |

## 动效层（不等于额外操作按钮）

| 编号 | 名称 | 用途 |
|---|---|---|
| E01 | 卡效文案动效 | 已执行的效果段／分支文案（ActionPresentationLayer） |
| E02 | 战斗移动动效 | 进攻、命中、兵力变化（CombatMotionPresentationLayer） |
| E03 | 卡牌移区动效 | 抽牌、进入战场、离场等（ZoneMovementPresentationLayer） |
| E04 | 阶段提示动效 | 阶段切换提示（PhasePlayback） |
| E05 | 公开卡展示层 | 合法公开卡、私人查看仅对应操作者（GameBoard） |
| E06 | 骰点展示层 | 掷骰过程与最终结果（GameBoard） |
| E07 | 特殊胜利动效 | 奥西里斯等专用结算展示（OsirisVictorySequence） |

源码目录：`opcgpro-vue/src/l12/game/`；共享的卡牌、设置和页面容器位于`opcgpro-vue/src/l12/`及`site/`。窄屏可能折叠或移动组件，以上名字和编号不变。
