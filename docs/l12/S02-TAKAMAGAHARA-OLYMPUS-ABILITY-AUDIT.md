# S02 高天原与奥林匹斯逐卡独立语义审计（Batch 6L-C）

更新日期：2026-09-01

## 范围与结论

- 固定范围为 `cards.s2.json` 的 7 张 `gaotianyuan` 高天原卡与 28 张 `olympus` 奥林匹斯卡，含主宰、主神、士气/神力、晋升者与特殊牌，共 35 张、101 项原子目录能力；S02 天灾留后批。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。逐项核对时点、费用预付、独立段、区域/所有者、四状态资源、晋升叠放、数值、隐藏信息、来源 LKI 与响应无效。
- 唯一结论：20 张通过、14 张明确错误并修复、1 张沿用既有疑点、缺少测试 0、未实现 0。海伦的致命替代仍只引用既有 `OPEN-QUESTIONS.md`，本批未猜测裁定。

## 逐卡逐能力结论

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S02-0401 武田信玄 | 2 | 士气不能因主宰效果转活跃；登场检索身份延迟，检索与随后真田登场/士气转活跃分别响应，公开手牌目标和位置候选期声明。 | `L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0402 井伊直虎 | 2 | 登场弃牌为冒号前私密费用、公开休整军团目标先声明；阵亡抽牌模式先声明，来源离场用快照。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0403 冲田总司 | 3 | 登场条件锁定并仅本回合获得冲锋/+1000；进攻合法开始才展示牌库顶，免费打出复用普通打出/位置声明与区域事务。 | `L12EnterPublicTriggerPlans`、`L12EffectGeneratedPlay` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JCRegressionTests` | 通过 |
| S02-0404 八尺琼勾玉 | 5 | 检索身份延迟；主动休整两个公开模式先声明。额外骑兵位移不消费普通移动次数，免死目标必须本回合已位移。 | `L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `S2FactionRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0405 武运在天 铠甲在前 | 3 | 顶5查看/选择/有序回底是隐藏首段；下一张上杉-2并获冲锋为独立必发后段，首段无效不吞后段。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0406 天下布武 | 4 | 三种公开模式及行先声明；行减费、前排进攻+1000或所有活跃高天原各获得一次免费位移按声明结算。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6HRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-04M1 月读 | 3 | 追加位移回合1次且费用/目标/位置前置；后→前每次各建独立可响应+1000触发且无once；前→后每次公开声明士气。 | `L12S2RemainingEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch4RegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |
| S02-0501 赫拉克勒斯·晋升 | 4 | 普通/晋升登场分流；晋升共享底座状态与赋予词条，晋升登场公开手牌费用与敌军目标前置；普通登场自伤模式前置，进攻强攻独立触发。 | `L12Actions`、`L12EnterPublicTriggerPlans`、`RuleKernel` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |
| S02-0502 赫拉克勒斯 | 1 | 登场可选抽2，抽后弃置具体隐藏手牌只在合法结算后选择。 | `L12AtomicRuntimeIntegration` | `AtomicReviewBatch6IARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0503 阿喀琉斯·晋升 | 6 | 晋升共享状态；远程伤害额外-1000按伤害层结算；晋升登场本回合可攻军团；真实击杀存活后才获得至下个己方回合开始、仅前排生效的挑衅。 | `RuleKernel`、`L12CombatTimeline`、`L12StructuredCardRules` | `AtomicReviewBatch6LCRegressionTests`、`LatestBugRegressionTests` | 明确错误→已修复 |
| S02-0504 阿喀琉斯 | 2 | 前排主宰保护读取实时兵力；致命替代回合1次，消耗并翻转1神力后承受替代结果，不影响其他已裁定替代。 | `L12StructuredCardRules`、`L12GameEngine` | `LatestBugRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0505 珀尔修斯·晋升 | 5 | 晋升共享状态；晋升登场公开休整敌军目标前置；自位移回合1次，普通登场冲锋与晋升时点分别建立。 | `RuleKernel`、`L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |
| S02-0506 珀尔修斯 | 1 | 登场弃1手牌为冒号前费用，墓地晋升者目标公开前置；目标失效不返弃牌。 | `L12EnterPublicTriggerPlans` | `AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0507 阿塔兰忒·晋升 | 5 | 晋升共享状态；晋升登场和普通登场各有一次独立可选抽牌；后排派生为弓手、兵力视为3000并按远程规则结算。 | `RuleKernel`、`L12PublicTriggerEffectPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |
| S02-0508 阿塔兰忒 | 2 | 常驻远程+1/无损；阵亡时确切士气翻面目标在候选期声明，来源离场后按快照结算。 | `L12StructuredCardRules`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0509 奥德修斯 | 3 | 0神力减费实时计算；登场免费战术计数只作用下一张；进攻展示的私密手牌战术在声明期仅对选择者可见。 | `L12Actions`、`L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0510 希波吕忒 | 3 | 神力≥5减费与休整时免费前后位移持续生效；主动休整/3士气/弃牌在入栈前支付，墓地奥林匹斯目标与位置前置并按戒指有效阵营重验。 | `L12S2RemainingEffects`、`L12StructuredCardRules` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0511 珀洛特埃 | 3 | 登场本回合可攻军团；进攻目标为军团时先声明是否支付1神力，支付/翻面与+1000/震击只作用本段。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0512 埃涅阿斯 | 4 | 0神力减费；前排挑衅实时；阵亡抽牌模式候选期声明，拒绝不造空栈。 | `L12StructuredCardRules`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0513 亚里士多德 | 3 | 远程常驻；登场翻士气目标前置；主动休整的下一张奥林匹斯军团-1必须识别戒指通用军团并由该军团消费。 | `L12Actions`、`L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0514 柏拉图 | 2 | 远程常驻；牌库顶身份只在登场段合法开始后展示，戒指使通用卡成为合法奥林匹斯命中，其余有序回底。 | `L12S1FactionEffects`、`L12S2FactionEffects` | `AtomicReviewBatch6LCRegressionTests`、`AtomicEffectsTests` | 明确错误→已修复 |
| S02-0515 海伦 | 3 | 远程常驻与神力条件登场弃牌已实现；前排致命替代的声明层级沿用既有待裁定项，本批不猜。 | `L12StructuredCardRules`、`L12GameEngine` | `S2FactionRegressionTests`、`OPEN-QUESTIONS.md` | 有疑点 |
| S02-0516 汉尼拔 | 3 | 活跃不可被攻与相邻+1000实时；进攻先支付1神力并同时声明双方公开军团目标，目标分别失效不互吞。 | `L12AttackPublicTriggerPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6HRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0517 彭忒西勒亚 | 3 | 前排远程常驻；登场本回合可攻军团；进攻消耗并翻转1神力在入栈前支付，+2000本回合有效且无效不退款。 | `L12AttackPublicTriggerPlans`、`L12EnterPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0518 忒修斯 | 3 | 0神力减费；登场翻休整士气目标前置；阵亡墓地晋升者目标先声明并以展示加入手牌公共事件移动。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0519 斯巴达勇士 | 2 | 进攻消耗并翻转1神力在入栈前支付并获得+2000；对方回合+2000为持续层，修正标签不得冒充阿喀琉斯。 | `L12AttackPublicTriggerPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6HRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0520 匠神锻造炉 | 4 | 登场翻士气目标前置；主动休整/士气预付后选模式，下一次晋升减费或非晋升奥林匹斯击杀后转活跃，戒指通用军团合法。 | `L12S2FactionEffects`、`L12KillSourceEvents` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0521 荣耀之路 | 2 | 翻最多3士气与可选检索是独立段；检索2神力入栈前支付，身份结算期展示，戒指通用卡合法并随后洗牌。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0522 倪克斯的陨星 | 2 | -3000首段与可选支付/翻转1神力的-2000后段独立响应；公开目标先声明，费用不因无效返还。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6HRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0523 特洛伊木马 | 2 | 对方进攻后模式与对方战场空位前置，位置重验不覆盖；延迟至下个己方回合结束弃置并抽牌，场上持续-1000。 | `L12PublicTriggerEffectPlans`、`L12GameEngine` | `AtomicReviewBatch6ERegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-05M1 阿尔忒弥斯 | 4 | 远程阵亡翻士气与主动各自回合1次；主动费用/奥林匹斯目标/强攻或震击模式前置，戒指通用军团合法。 | `L12PublicTriggerEffectPlans`、`L12S2RemainingEffects` | `AtomicReviewBatch6GARegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |
| S02-05M2 普罗米修斯 | 1 | 只消耗1神力不翻面；牌库顶身份结算期读取，戒指通用卡合法，剩余牌整体自选顺序回顶或回底。 | `L12S2FactionEffects`、`L12S2ZoneOps` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-05C1 神力 | 2 | 与士气同区且可付普通费用；自身主动只选择活跃神力，消耗并翻面后成为休整士气，回合1次按实例。 | `RuleKernel`、`L12S2FactionEffects` | `AtomicReviewBatch6LCRegressionTests`、`RuleKernelTests` | 通过 |
| S02-05C1A 士气 | 1 | 主动消耗1士气并公开声明要翻转的士气目标；目标失效不产生部分翻转，四状态维度保持正交。 | `L12S2FactionEffects`、`RuleKernel` | `AtomicReviewBatch6LCRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-05D1 奥林匹斯 诸神巅 | 3 | 两项主神能力各自回合1次且公开声明；2神力预付。墓地回收与随后可选登场分别响应，前段无效不吞后段；戒指通用卡合法；主神开场额外2士气只执行一次。 | `L12PublicActiveEffectPlans`、`L12CompositeEffectPlans`、`L12S2RemainingEffects` | `AtomicReviewBatch2RegressionTests`、`AtomicReviewBatch6LCRegressionTests` | 明确错误→已修复 |

## 公共根因、跨范围控制与保留边界

- 月读后→前效果从同步计数迁为每次一个 `TriggerCandidate`；同一位移的追加位移候选仍与它进入拥有者排序批次。只有独立栈项成功结算才增加该军团本回合的进攻增益，无每回合一次限制。
- `HandPlay Composite` 为〈武运在天 铠甲在前〉建立隐藏检索段与确定性上杉增益段；诸神巅回收模式复用 Active Composite 建立回收/随后登场两段。任一前段被无效不吞后段，已支付费用和已完成区域移动不回滚。
- `L12S2ZoneOps.InheritPromotionState` 统一迁移休整、限时修正及其已吸收伤害、被赋予关键词、挑衅/免死到期、禁转活跃、移动/进攻次数与临时进攻许可；不启用底座印刷文本/兵力，离场仍由整组所有者区域事务处理。
- 所有写明【奥林匹斯】的运行时筛选与提交重验统一调用 `L12StructuredCardRules.HasFaction`。仅晋升卡身份/同名底座配对、阵营开局与叠放拆组保留印刷 `Faction == "olympus"` 判断；牌库顶/检索身份仍到合法效果开始后才读取。
- 海伦致命替代沿用既有待裁定项；本批没有新增裁定。天灾不在 6L-C 范围。

## 红绿与验证证据

- 红基线 1/8 绿、7/8 红，命中月读同步状态、武运在天串行段、晋升状态遗漏与戒指奥林匹斯筛选；扩充后专项 13/13，旧诸神巅/荣耀之路/普罗米修斯/亚里士多德/武运在天精确兼容 6/6。
- 最终专项 13/13、精确兼容 6/6；Focused 与 Batch 规则均 992/992。35 卡/101 项静态门禁、矩阵导出与原子 248/248 通过，`cardCase=0`、`cardConditional=186`、`cardSwitchArm=67`、`effectTextInference=0`、未路由有权威入口 74/74、无权威入口 0；`git diff --check` 通过。完整 Release 与 Git 同步留全部批次最终统一收口，本批不提交、不推送、不部署。
