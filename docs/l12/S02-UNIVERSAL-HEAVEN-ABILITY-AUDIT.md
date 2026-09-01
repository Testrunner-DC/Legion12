# S02 通用与天廷逐卡独立语义审计（Batch 6L-A）

更新日期：2026-09-01

## 范围与结论

- 固定范围为 `cards.s2.json` 全部 18 张 `universal` 通用卡与 8 张 `tianting` 天廷卡，含反击战术、主宰、士气/阵营关联与特殊军团，共 26 张、51 项能力。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。每行独立核对时点、声明与费用、区域/所有者、数值层、隐藏信息、AuthorityEvent、空目标、来源 LKI、多实例次数与响应无效。
- 唯一结论：22 张通过、3 张明确错误并修复、1 张有疑点，缺少测试 0、未实现 0。疑点仅为既有 `OPEN-QUESTIONS.md` 中〈信仰狂热者〉的声明层级，不猜测；6M 最终交叉审阅补获〈万物统御之戒〉声明期隐藏命中存在性泄露并完成红绿修复。

## 逐卡逐能力结论

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S02-0001 驱魔道士 陆瑛 | 2 | 对方战术结算后的返手模式先进入 TriggerCandidate 声明；登场费用税只影响对方下回合从手牌打出的主动战术。 | `L12PublicTriggerEffectPlans`、`L12S2UniversalEffects` | `AtomicReviewBatch6JBRegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0002 疯狂的爱丽丝 | 2 | 进攻无损走战斗数值层；回合1次击杀后转活跃为可选候选，拒绝释放 pending、提交才最终消费。 | `L12RuleKernelIntegration`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0003 宫廷魔术师 | 3 | 远程静态正确；登场公开反击目标先声明；主动休整后直到下个己方回合开始前阻止全场反击战术发动。 | `L12EnterPublicTriggerPlans`、`L12S2UniversalEffects` | `AtomicReviewBatch6JARegressionTests`、`LatestBugRegressionTests` | 通过 |
| S02-0004 路易芒德兰 | 2 | 前排时获得挑衅；仅对方回合获得+1000持续兵力层，不污染基础兵力。 | `L12RuleKernelIntegration`、`L12StructuredCardRules.S02HumanAssisted` | `AtomicReviewBatch3RegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0005 戏法师的傀儡 | 2 | 无法进攻；对方进攻主宰时从私密手牌声明公开前排位置，效果成功才休整登场并改目标，被无效仍留手，位置失效不覆盖。 | `L12PromptsAndSetup`、`L12GameEngine` | `Bq20260830RegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0006 信仰狂热者 | 2 | 牌库弃置或效果弃手的条件、每回合1次和免费主宰能力已实现；触发时应先声明“发动狂热者”还是直接声明所选主宰能力仍待用户裁定。 | `L12S2FactionEffects`、`L12ActiveAbilities` | `Bq20260830RegressionTests`、`S2FactionRegressionTests` | 有疑点 |
| S02-0007 重装士兵 | 3 | 无法进攻且无法被远程进攻；前排挑衅及对方回合+1000均由结构化战斗规则派生。 | `L12GameEngine`、`L12RuleKernelIntegration` | `RuleKernelTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0008 万物统御之戒 | 2 | 圣物区内通用卡在所有权威区域按控制者主宰阵营判定；登场声明只看公开牌库数量，弃手费用私密预付，牌库检索身份与是否命中只在合法结算期读取并展示加入，随后重洗。 | `L12StructuredCardRules`、`L12EnterPublicTriggerPlans`、`L12EffectGeneratedPlay` | `AtomicReviewBatch6JARegressionTests`（含6M隐藏命中存在性回归）、`AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S02-0009 防御部署 | 1 | 最多2张私密反击身份与各自公开后排位置提交后才入栈；位置逐张重验不覆盖；手牌不高于4的抽牌是独立后段。 | `L12CompositeEffectPlans`、`L12S2UniversalEffects` | `AtomicReviewBatch6ARegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0010 黑色莲花 | 2 | 天灾值模式与可选士气段先声明；3份资源在入栈前原子支付，无效不返；转为士气后返还时进入所有者墓地。 | `L12CompositeEffectPlans`、`L12MoraleReturns` | `AtomicReviewBatch6ARegressionTests`、`MoraleReturnSelectionTests` | 通过 |
| S02-0011 纷乱箭 | 1 | 打出前声明最多3张原本兵力不高于2000的公开目标；分别重验，单目标失效不改选、不吞其他目标。 | `L12CompositeEffectPlans`、`L12S2UniversalEffects` | `AtomicReviewBatch6ARegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0012 祷告仪式 | 3 | 对手同意/拒绝属于受影响方结算选择；拒绝后查看是独立可选候选，1士气预付，天灾身份仅该段合法开始后私密读取。 | `L12S2UniversalEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6JBRegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0013 神圣伽锁 | 3 | 打出前声明对方圣物；结算重验且不改选；宿主离区按伽锁所有者入墓，对方主动弃置时3士气预付、无效不返。 | `L12CompositeEffectPlans`、`L12S2UniversalEffects`、`L12ActiveAbilities` | `AtomicReviewBatch6ARegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0014 瞬间的思路 | 1 | 效果合法结算时若己方手牌不高于4则抽2；抽牌与入手日志不泄露私密身份。 | `L12S2UniversalEffects`、`L12GameEngine` | `S02HumanAssistedAtomicTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0015 地主的胁迫 | 1 | 仅响应对方抵挡/支援 AuthorityEvent；额外弃牌由受影响对手在该响应段结算时私密选择，否则仅令对应防御无效。 | `L12S2CounterTactics`、`L12AuthorityEvents` | `S2UniversalEffectsTests`、`TriggeredEffectPresentationTests` | 通过 |
| S02-0016 破败仪式 | 3 | 响应非手牌登场；模式和匿名盲选手牌在入栈前声明，或锁定本次公开登场军团；目标失效只取消所选模式。 | `L12PublicResponseEffectPlans`、`L12S2CounterTactics` | `S2UniversalEffectsTests`、`RuleKernelTests` | 通过 |
| S02-0017 粮草掠夺 | 1 | 响应效果入手 AuthorityEvent；对手手牌随机匿名盲选且不泄露顺序/身份，回牌库顶与随后抽1为独立响应段。 | `L12PublicResponseEffectPlans`、`L12CompositeEffectPlans`、`L12S2CounterTactics` | `AtomicReviewBatch5RegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0018 毒药发作 | 1 | 响应效果转活跃 AuthorityEvent；无效该次批次与随后由对手私密弃1为独立响应段，前段无效不吞后段。 | `L12CompositeEffectPlans`、`L12S2CounterTactics` | `S2UniversalEffectsTests`、`S2FactionRegressionTests` | 通过 |
| S02-0101 始皇帝 嬴政 | 2 | 前排保护读取当前兵力；登场私密选定并弃置费用8军团后才入栈，击杀与随后返还/封锁追加分段，只有天廷阵营效果可在本回合继续追加士气。 | `L12S2FactionEffects`、`L12CompositeEffectPlans`、`L12GameEngine` | `AtomicReviewBatch6LARegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0102 李牧 | 2 | 主宰返还4+后的追加士气模式与次数先声明；登场只声明展示/抽牌模式，不预读牌库顶，展示处理与随后抽牌分别响应，免费打出复用 HandPlay。 | `L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans`、`L12EffectGeneratedPlay` | `AtomicReviewBatch6GARegressionTests`、`AtomicReviewBatch6GBRegressionTests`、`AtomicReviewBatch6JCRegressionTests` | 通过 |
| S02-0103 平阳昭公主 | 2 | 登场的下一次主宰伤害变2为确定效果；进攻展示牌库顶只在该段开始后读取，按展示结果结算增兵或回底。 | `L12AttackPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6HRegressionTests`、`RuleKernelTests` | 通过 |
| S02-0104 神农鼎 | 2 | 登场抽牌可选模式在候选期声明；主动休整、确切1士气返还费用及公开主宰能力目标在入栈前提交，无效不返费。 | `L12PublicTriggerEffectPlans`、`L12PublicActiveEffectPlans`、`L12MoraleReturns` | `AtomicReviewBatch6IARegressionTests`、`AtomicEffectsTests` | 通过 |
| S02-0105 乾坤·阳 | 1 | 原本兵力不高于3000的击杀目标先声明；可选抽牌段与确切1士气返还费用先声明并独立响应。 | `L12CompositeEffectPlans`、`L12S2UniversalEffects` | `AtomicReviewBatch1RegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S02-0106 乾坤·阴 | 1 | 响应模式确定；牌库顶身份仅在效果段合法开始后展示，随后只有展示结果成立时才声明公开己方军团目标，未命中则回底。 | `L12S2CounterTactics`、`L12PromptsAndSetup` | `AtomicReviewBatch4RegressionTests`、`TriggeredEffectPresentationTests` | 通过 |
| S02-01M1 孙悟空 | 4 | 回合1次精确返还2至8士气及公开前排位置必须提交后才入栈；位置结算重验不覆盖且不退款；军团形态进攻后/回合末/任意离场回主宰区，条件追加士气另行声明。 | `L12S2RemainingEffects`、`L12CombatTimeline`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6LARegressionTests`、`AtomicReviewBatch6JBRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-01S1 哮天犬·稚 | 2 | 主宰返还4+后可选前排位置先声明，特殊牌以唯一实例活跃登场；阵亡追加休整士气模式先声明，来源离场使用快照。 | `L12PublicTriggerEffectPlans`、`L12SpecialDeckRules`、`L12S2FactionEffects` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |

## 公共根因、扫描与保留边界

- 孙悟空沿用公共 `PendingActivation`，把资源费用和公开位置作为同一提交事务；结算只读取不可变 `slot/count`，空位失效不改选、不覆盖、不返还费用或次数。
- 始皇帝沿用 `Composite/PendingCompositeSegments`，击杀与“随后返还全部士气并限制追加”各自获得响应窗口。第一段无效仍进入第二段；第二段无效不回滚第一段或已付手牌费用。
- `AddMorale` 统一消费始皇帝的回合限制。普通卡效/主宰/军团/试炼追加被拒绝，只有 `S01-01C1` 天廷阵营效果显式标记为阵营来源；阶段追加发生在其他回合，不受本回合字段影响。
- 全池反击战术复核确认 S02-0015 至 S02-0018 均基于 AuthorityEvent，防御/非手牌登场/效果入手/效果转活跃四类窗口互不串线；同回合覆盖的反击仍可发动。
- 合法结算期 Prompt 保留：祷告仪式的对手同意/拒绝、乾坤·阴展示后才可知的目标、毒药发作由受影响方弃牌。信仰狂热者声明层级继续隔离在 `OPEN-QUESTIONS.md`。
