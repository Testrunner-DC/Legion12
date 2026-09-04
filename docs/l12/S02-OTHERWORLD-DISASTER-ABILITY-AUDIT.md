# S02 彼界与天灾逐卡独立语义审计（Batch 6L-D）

更新日期：2026-09-04

## 范围与结论

- 固定范围为 `cards.s2.json` 的 32 张 `otherworld` 彼界卡与 6 张 `S02-DSxx` 天灾卡，含主宰、主神、士气/符文、试炼、王者之剑与特殊牌，共 38 张、108 项原子目录能力。其中 22 张圆桌/彼界主牌已按玩家人工定义拆为 74 项能力。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。逐项核对试炼发动/推进/完成、费用预付、独立段、区域/所有者、符文、隐藏信息、来源 LKI、天灾不可响应与响应无效。
- 唯一结论：30 张通过、8 张明确错误并修复、有疑点 0、缺少测试 0、未实现 0。至此 S1+S2 全 248 张、577 项目录能力均有逐卡唯一结论。

## 逐卡逐能力结论

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S02-0601 亚瑟王 | 2 | 登场符文费用与模式入栈前声明；王者之剑 Limit 1，第二次仍支付但明确无事发生；阵亡私密圆桌骑士与公开位置先声明。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `Bq20260830RegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S02-0602 兰斯洛特 | 5 | 登场可付符文获冲锋；击杀时试炼推进/得符文模式候选期声明，推进与完成事件严格分离。 | `L12EnterPublicTriggerPlans`、`L12TrialAdvanceEffectPlans` | `AtomicReviewBatch6FRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0603 梅林 | 5 | 无法进攻、登场得符文；主动只先声明公开模式/敌军目标并预付符文休整，牌库命中身份和存在性到合法结算期私密选择。 | `L12S2FactionEffects`、`L12StructuredCardRules` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-0604 加拉哈德 | 3 | 独立保留试炼2；登场可推进试炼；圣杯完成后先声明是否回血并以弃置自身为冒号前费用，结算无效不返还，自身离场不触发阵亡。 | `L12TrialAdvanceEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6FRegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-0605 鲍斯 | 4 | 手牌中按我方彼界军团动态减费；戒指通用军团计入彼界；进攻士气预付获强攻；阵亡使对手结算期弃隐藏手牌。 | `L12StructuredCardRules`、`L12AttackPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-0606 帕西瓦尔 | 5 | 独立保留试炼1、贯穿定义；登场得符文；进攻弃手牌预付获+2000；真实击杀后本回合获得贯穿。 | `L12AttackPublicTriggerPlans`、`L12KillSourceEvents` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6HRegressionTests`、`LatestBugRegressionTests` | 通过 |
| S02-0607 高文 | 2 | 登场得符文；进攻声明 X=1..可用符文并预付，符文消费与本回合后续全部进攻增伤为独立段。 | `L12AttackPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests` | 通过 |
| S02-0608 狮心王理查一世 | 4 | 登场推进试炼并在合法结算期选择私密侍从来源；免死独立授予；进攻守方附加弃牌仅在相应抵挡/支援段成功后生效，弃附属费用先付。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0609 侍从骑士 | 3 | 独立保留试炼1；仅不可进攻主宰而非禁止全部进攻；阵亡推进试炼使用离场快照，不自动完成试炼。 | `L12StructuredCardRules`、`L12TrialAdvanceEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6FRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0610 芬恩 | 3 | 独立保留试炼1；登场可推进；推进后符文/转活跃是独立响应段并锁定本回合不可再推进。 | `L12TrialAdvanceEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6FRegressionTests` | 通过 |
| S02-0611 库丘林 | 5 | 手牌中有斯卡哈在场减费；登场前排条件免死按实时位置；击杀后本回合贯穿；免死与贯穿定义独立。 | `L12StructuredCardRules`、`L12KillSourceEvents` | `S02OtherworldHumanAssistedAtomicTests`、`LatestBugRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0612 斯卡哈 | 4 | 手牌中有库丘林在场减费、登场获得冲锋；进攻符文费用入栈前支付，本回合+2000并进攻无损。 | `L12StructuredCardRules`、`L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6HRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0613 圣女贞德 | 3 | 独立保留试炼1；登场弃牌费用预付且保护主宰到下个己方回合；阵亡双方主宰各回血，来源离场不吞候选。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S02-0614 康斯坦丝 | 5 | 独立保留试炼1与远程职介能力；登场得符文/推进试炼公开模式先声明，推进与完成事件分离。 | `L12StructuredCardRules`、`L12TrialAdvanceEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6FRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0615 格温莉安 | 3 | 前排挑衅与敌方回合+1000实时；仅因效果阵亡时，回血/抽牌强制模式候选期声明，日志使用正确卡名。 | `L12StructuredCardRules`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests` | 通过 |
| S02-0616 阿麦金 | 3 | 休整时保护活跃试炼军团；登场得符文可选声明；主动休整后才展示牌库顶，“只拥有彼界特征”不把戒指通用卡误判为唯一彼界。 | `L12AtomicRuntimeIntegration`、`L12S2FactionEffects` | `AtomicReviewBatch6IARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0617 罗宾汉 | 4 | 远程；登场侍从身份结算期选择而位置预声明；进攻得符文必发，条件抽牌为独立可选段。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0618 伊丽莎白·都铎 | 3 | 独立保留试炼2与远程职介能力；登场得符文为必发触发，无空响应栈。 | `L12AtomicRuntimeIntegration`、`L12PublicTriggerEffectPlans` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6IARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0619 克劳迪娅 | 2 | 前排获得刺客远程能力；登场符文费用与公开敌军目标入栈前声明，目标失效不退款。 | `L12EnterPublicTriggerPlans`、`L12StructuredCardRules` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S02-0620 符文之力 | 2 | 得符文首段与可选支付/查看后段分别响应；身份结算期读取，戒指通用卡是合法彼界命中，其余自选顺序回底。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `S02OtherworldHumanAssistedAtomicTests`、`LatestBugRegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-0621 圆桌领域 | 2 | 隐藏检索/重洗首段与可选士气强化后段独立；公开圆桌目标与费用先声明，前段无效不吞后段。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `S02OtherworldHumanAssistedAtomicTests`、`LatestBugRegressionTests`、`AtomicReviewBatch6CRegressionTests` | 通过 |
| S02-0622 槲寄生符咒 | 2 | X符文减费在打出提交前声明支付；公开敌军目标先锁定，结算目标失效不返符文/士气。 | `L12CompositeEffectPlans`、`L12Actions` | `S02OtherworldHumanAssistedAtomicTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S02-06C1 士气 | 2 | 阵营点击回合1次支付2士气得符文；符文令牌点击回合1次先声明推进/抽牌模式并支付1符文。 | `L12S2FactionEffects`、`L12TrialAdvanceEffectPlans` | `AtomicReviewBatch6FRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-06D1 彼界 阿瓦隆 | 4 | 可携带完成试炼；回合开始推进+得符文为确定性不可拒绝事件；回收与随后免费战术独立；主动休整公开目标先声明。 | `L12TrialAdvanceEffectPlans`、`L12PublicActiveEffectPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch6FRegressionTests`、`AtomicReviewBatch2RegressionTests` | 通过 |
| S02-06M1 莫瑞甘 | 3 | 对方阵亡得符文与主动均各自回合1次；主动符文预付且戒指通用军团可作彼界公开目标。 | `L12PublicTriggerEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6GARegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-06M2 安格斯·麦·奥格 | 1 | 试炼上限+1；每次完成试炼的符文是同批可选触发；战术成功推进回合1次强制，推进不等于完成。 | `L12TrialCompletionTriggerPlans`、`L12TrialAdvanceEffectPlans` | `AtomicReviewBatch6BRegressionTests`、`AtomicReviewBatch6FRegressionTests` | 通过 |
| S02-06S1 符文 | 1 | 符文数量是特殊区公开资源；直接点击模式先声明，支付后无效不返，推进不自动完成。 | `L12S2FactionEffects`、`L12TrialAdvanceEffectPlans` | `AtomicReviewBatch6FRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-06S2 王者之剑 | 1 | 全场唯一真实实例，叠放时给亚瑟原本兵力+1000/强攻；试炼替代移除后进入所有者正确区域。 | `L12EnterPublicTriggerPlans`、`L12GameEngine` | `Bq20260830RegressionTests`、`ImmortalityRegressionTests` | 通过 |
| S02-06S3 湖中仙女的馈赠 | 3 | 完成首句可选，未发动时直接进入强制回库洗牌段；减费再独立；牌库身份延迟，剑替代致命保持独立。 | `L12TrialCompletionTriggerPlans`、`L12GameEngine` | `AtomicReviewBatch6BRegressionTests`、`ImmortalityRegressionTests` | 通过 |
| S02-06S4 寻找圣杯之旅 | 3 | 完成时只按公开牌库数量提供查看模式，不以隐藏命中裁剪；结算期按戒指有效彼界检索；圆桌登场得符文独立 once。 | `L12TrialCompletionTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6BRegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-06S5 芬尼亚传奇 | 2 | 完成时 X 符文与重复公开敌军目标先声明并逐段响应；主动转活跃使用戒指有效彼界判定且回合1次。 | `L12TrialCompletionTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6BRegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-06S6 十字军东征 | 1 | 三个模式共享印刷“回合1次”；费用/公开目标先声明，无效不返；“只有彼界特征”保持印刷阵营，不把戒指通用卡误判。 | `L12ActiveAbilities`、`L12S2FactionEffects` | `AtomicReviewBatch2RegressionTests`、`AtomicReviewBatch6LDRegressionTests` | 明确错误→已修复 |
| S02-DS01 天地异变 | 1 | 翻开时真实反转双方牌库，离场再复原；顶部公开且同兵种手牌军团不可打出，洗牌/清点不破坏方向。 | `L12Disasters`、`L12Actions`、`L12GameEngine` | `LatestBugRegressionTests`、`NewSystemsTests` | 通过 |
| S02-DS02 迷雾绝境 | 2 | 双方同时私密弃到5；低兵军团不可攻主宰、挑衅失效、活跃前排不可被攻均为持续层。 | `L12Disasters`、`L12Actions` | `NewSystemsTests`、`RuleKernelTests` | 通过 |
| S02-DS03 无眠之夜 | 3 | 弃置原本兵力≤2000军团；所有结构化主动休整对自身主宰造成中立非致命伤害，不依赖按钮文案。 | `L12Disasters`、`L12StructuredCardRules`、`L12ActiveAbilities` | `NewSystemsTests`、`S2DisasterLevelRegressionTests` | 通过 |
| S02-DS04 风暴乱象 | 2 | 后排全部回所有者手牌，再使前排向后位移并发布位移事件；持续禁止远程进攻。 | `L12Disasters`、`L12Actions` | `NewSystemsTests`、`Bq20260830_02RegressionTests` | 通过 |
| S02-DS05 暴怒之罪 | 3 | 触发对双方主宰造成中立非致命1伤；持续必须优先进攻范围内敌军；不触发卡牌受伤来源奖励。 | `AtomicEffects`、`L12Actions` | `AtomicEffectsTests`、`NewSystemsTests` | 通过 |
| S02-DS06 傲慢之罪 | 2 | 军团较多者按差值弃战场或手牌；主宰效果额外1士气、手牌军团+1费均走公共费用层。 | `L12Disasters`、`L12Actions`、`L12ActiveAbilities` | `NewSystemsTests`、`S2FactionRegressionTests` | 通过 |

## 公共根因、跨范围控制与保留边界

- 梅林主动声明只读取公开模式、公开敌军与公开牌库数量；费用/休整提交后，合法效果开始才创建私密 `s2-merlin-search`，未命中也公开结果并重洗，命中按卡面展示后走权威加入手牌事件。
- 所有写明【彼界】的效果筛选与提交重验统一调用 `L12StructuredCardRules.HasFaction`，覆盖鲍斯减费、莫瑞甘、芬尼亚、符文之力及圣杯检索。明确写“只拥有/只有【彼界】特征”的阿麦金和十字军回收继续使用印刷阵营，不受万物统御之戒扩展。
- `ActiveAbilityUsageKey` 将十字军三种按钮归并为同一 `crusade-choice` 次数；任一模式提交即消耗本回合次数，取消声明不消耗。
- 加拉哈德由 `PendingActivation` 预声明可选回血；权威离场事务先支付弃置自身，再创建 StackItem。效果无效不抽牌/回血，但费用永不返还。
- S02 天灾全部通过 `unrespondable=true` 的天灾堆叠入口即时结算；`S02-DS05` 继续复用已验证原子中立非致命伤害。S01 最终天灾〈堙灭〉仍固定天灾牌库最后、回合开始即时且天灾值锁 0，本批没有改变跨赛季公共规则。
- 本批没有新增裁定项；既有 `OPEN-QUESTIONS.md` 内容未覆盖。

## 红绿与验证证据

- 红基线 1/9 绿、8/9 红，精确命中梅林隐藏泄露、圣杯隐藏存在性、彼界有效阵营、十字军共享次数及加拉哈德费用时序；扩充后专项 11/11。
- 2026-09-04 人工拆分后，22 张圆桌/彼界主牌由旧机械边界 49 项重构为 74 项，独立保留试炼值、授予能力、关键词定义、复合句独立段及手牌条件；相关实战入口继续复用既有公共计划，不恢复单卡结算旁路。专项人工原子与结构化查询 19/19、彼界/试炼/进攻兼容合并 275/275，Focused、Batch、Release 完整规则均 2245/2245 通过；Release 平台持久化 62/62、UI 契约 217 项、卡图契约 28 项/324 张及生产构建通过；38 卡/108 项、全池 248 张/577 项唯一审计门禁与公共声明门禁通过。
