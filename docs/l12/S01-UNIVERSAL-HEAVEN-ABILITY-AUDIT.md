# S01 通用与天廷逐卡独立语义审计（Batch 6K-A）

更新日期：2026-09-01

## 范围与结论口径

- 固定范围为 `cards.s1.json` 的 22 张 `universal` 基础卡、24 张 `tianting` 卡（含士气、主宰、阵营与专属衍生文本），以及 10 张 S01 通用天灾，共 56 张、94 项能力。
- 权威顺序为：玩家裁定 > `FAQ-RULINGS.md`/FAQ 材料 > 规则书与关键词 > 印刷卡面。代码和旧测试只作实现证据，不反推规则。
- 每行的“最短规则断言”同时核对时点、入栈前声明/费用、区域与数值层、公开/私密信息，以及空目标、来源离区、目标失效、响应无效等边界。`通过` 表示该卡全部列出的能力均找到运行时和行为测试证据；`明确错误→已修复` 表示本批先红后绿；`有疑点` 表示只登记待裁定而未改语义。
- 总结：46 张通过、8 张明确错误并修复、2 张有疑点；未实现 0、仍缺测试 0。

## 通用基础卡（22 张 / 27 项能力）

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-0001 黑胡子蒂奇 | 2 | 登场弃牌与“随后”双方抽牌分别响应；阵亡抽2后弃1的手牌身份在抽牌后私密选择。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6KARegressionTests`、`AtomicReviewBatch6IBRegressionTests`、`ExtendedCardEffectsTests` | 明确错误→已修复 |
| S01-0002 佣兵部队 | 2 | 自身位移按卡面回合1次；手牌弃置是抵挡的冒号前费用，响应提交后不返还。 | `L12Actions`、`L12PromptsAndSetup`、`L12S1ExtendedEffects` | `Bq20260830_02RegressionTests`、`CombatTimelineRegressionTests` | 通过 |
| S01-0003 攻城投石车 | 2 | 远程进攻无损为静态层；后排支付2士气后仅本回合扩大可进攻目标，卡面无重复收益但既有裁定保留实例回合锁。 | `L12ActiveAbilities`、`L12S1ExtendedEffects`、`L12StructuredCardRules` | `LatestBugRegressionTests`（`ability:extendedRange`） | 通过 |
| S01-0004 无名的渗透者 | 3 | 可在任一战场休整登场但不能攻/援；双方均可付2击杀；阵亡后由所有者而非控制者抽牌。 | `L12Actions`、`L12ActiveAbilities`、`L12S1ExtendedEffects` | `AtomicReviewBatch3RegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0005 万箭齐发 | 1 | 前排/后排/单体模式及单体公开目标在出牌前锁定；结算只施加本回合临时兵力层。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0006 邪恶仪式 | 1 | 具体弃牌为私密冒号前费用，提交后原子弃置；效果无效不返费，伤害非致命。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0007 野外扎营 | 1 | 顶3身份仅结算时查看；检索命中公开展示；追加模式与1资源预声明，追加段独立响应。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6JBRegressionTests`、`NewSystemsTests` | 通过 |
| S01-0008 兵临城下 | 1 | 前排-1000及后排不能支援均为本回合临时层，不改基础兵力或永久支援能力。 | `L12S1ExtendedEffects`、`L12StructuredCardRules` | `AtomicReviewBatch6JCRegressionTests`、`RuleKernelTests` | 通过 |
| S01-0009 战略转移 | 1 | 两个公开目标入栈前分别声明；回手目标失效只取消回手，不吞合法强化段。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6ARegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0010 伪造密令 | 1 | 1~2个不同公开军团及各自前后格预声明；结算重验空位，不重选、不覆盖。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6ARegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0011 瘟疫感染 | 1 | 军团/士气公开对象预声明；只锁其下个对方重置，目标失效则取消本段。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0012 全军出击 | 1 | 只消费下一张从手牌正常打出的费用≤6军团，授予冲锋且不污染后续军团。 | `L12CardEffects`、`L12AtomicRuntimeIntegration` | `GameEngineTests`、`LatestBugRegressionTests` | 通过 |
| S01-0013 前线侦查 | 1 | 查看手牌只对发动者可见；追加费用预付；被影响玩家在追加段结算时选择洗回对象。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6JBRegressionTests` | 通过 |
| S01-0014 祭天仪式 | 1 | 抽牌与随后天灾调整是两段；天灾值 -2..2 在出牌前公开声明，前段无效不吞后段。 | `L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6CRegressionTests` | 通过 |
| S01-0015 议和谈判 | 1 | 己方抽1与谈判分别响应；对方接受/拒绝是被影响玩家在第二段结算时选择。 | `L12CompositeEffectPlans`、`L12CardEffects` | `AtomicReviewBatch6CRegressionTests` | 通过 |
| S01-0016 绝对防御 | 1 | 具体手牌弃置在响应入栈前私密声明并支付；进攻抵挡与效果无效只作用于所响应对象。 | `L12PromptsAndSetup`、`L12S1ExtendedEffects` | `CombatTimelineRegressionTests`、`GameEngineTests` | 通过 |
| S01-0017 拼死反抗 | 1 | 进攻后公开选择单体-2000或全体休整军团-1000；效果持续至下个己方回合结束。 | `L12CombatTimeline`、`L12S1ExtendedEffects` | `CombatTimelineRegressionTests`、`RuleKernelTests` | 通过 |
| S01-0018 落穴陷阱 | 1 | 仅响应军团登场效果并无效该登场效果，不回滚军团已发生的登场与天灾值。 | `L12PromptsAndSetup`、`L12S1ExtendedEffects` | `AtomicReviewBatch6JCRegressionTests`、`NewSystemsTests` | 通过 |
| S01-0019 伏击 | 1 | 我方公开军团目标在响应前声明；结算目标失效只取消+2000，不改响应费用/来源。 | `L12S1ExtendedEffects`、`L12StructuredCardRules` | `AtomicReviewBatch2RegressionTests`、`CombatTimelineRegressionTests` | 通过 |
| S01-0020 战斗至黎明 | 1 | 必发全体+1000与条件可选抽1分别响应；抽牌模式在响应入栈前公开声明，拒绝不造空段。 | `L12PublicResponseEffectPlans`、`L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6KARegressionTests`、`AtomicReviewBatch5RegressionTests` | 明确错误→已修复 |
| S01-0021 摄政皇权 | 1 | 受伤时若存在合法军团则强制私密选择手牌军团、公开声明战场/空格，不得以 `mode:none` 拒绝；全部声明完成才翻开来源入栈，结算重验位置。 | `L12PublicTriggerEffectPlans`、`L12RuleKernelIntegration`、`L12S1ExtendedEffects` | `AtomicReviewBatch6KARegressionTests` | 明确错误→已修复 |
| S01-00C1 士气·通用 | 1 | 只作为额外通用士气资源，不产生卡牌效果栈；普通/神力面与活跃/休整状态正交。 | `L12DeckValidator`、`L12MoralePayments` | `ExtendedCardEffectsTests` | 通过 |

## 天廷（24 张 / 52 项能力）

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-0101 吕布 | 3 | 进攻无损/不可被远攻为静态；进攻后返4转活跃、登场返2击杀均先声明费用/公开目标，目标失效不退款。 | `L12StructuredCardRules`、`L12PublicTriggerEffectPlans`、`L12CombatTimeline` | `AtomicReviewBatch6JARegressionTests`、`CombatTimelineRegressionTests` | 通过 |
| S01-0102 武则天 | 2 | 登场返1后可锁最多2张公开休整军团；阵亡抽1与回血同一不可分句，来源离场用LKI。 | `L12CardEffects`、`L12RuleKernelIntegration` | `AtomicEffectsTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0103 李靖 | 1 | 展示模式先公开声明，牌库顶身份与顶/底/代替登场选择延迟到合法展示后；依赖“随后”是否另开响应待裁定。 | `L12EnterPublicTriggerPlans`、`L12EffectContinuations` | `AtomicReviewBatch6JARegressionTests`、`GameEngineTests` | 有疑点（OPEN #4） |
| S01-0104 韩信 | 2 | 登场费用减免按支付时士气比较；进攻返1、增兵/强攻模式在触发入栈前锁定并预付。 | `L12Actions`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0105 刘备 | 2 | 登场返1+公开手牌/位置预声明；主动检索身份只在结算读取，检索与随后洗牌分别响应，前段无效仍洗牌。 | `L12PublicTriggerEffectPlans`、`L12ActiveAbilities`、`L12CompositeEffectPlans` | `AtomicReviewBatch6KARegressionTests`、`AtomicReviewBatch6JARegressionTests` | 明确错误→已修复 |
| S01-0106 关羽 | 2 | 位移按回合1次；进攻返1、增兵/必中在触发前声明支付，仅影响本次已建立的进攻。 | `L12AttackPublicTriggerPlans`、`L12ActiveAbilities` | `AtomicReviewBatch6HRegressionTests`、`NewSystemsTests` | 通过 |
| S01-0107 张飞 | 2 | 士气较少时只修正登场费用；前排挑衅及对方回合+1000为条件静态层，离开前排即时撤销。 | `L12Actions`、`L12StructuredCardRules` | `GameEngineTests`、`RuleKernelTests` | 通过 |
| S01-0108 花木兰 | 2 | 登场返1获得冲锋；仅对方回合阵亡时预声明对方休整士气，来源LKI且锁定下次重置。 | `L12CardEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6JBRegressionTests` | 通过 |
| S01-0109 白起 | 2 | 登场追加3休整士气；主动休整后追加1休整士气，来源休整在入栈前完成且无回滚。 | `L12CardEffects`、`L12ActiveAbilities` | `AtomicReviewBatch4RegressionTests`、`NewSystemsTests` | 通过 |
| S01-0110 墨子 | 3 | 远程静态；登场返1与最多2个公开天廷目标前置；阵亡强制抽1使用来源快照。 | `L12S1ExtendedEffects`、`L12StructuredCardRules` | `AtomicReviewBatch6JARegressionTests`、`NewSystemsTests` | 通过 |
| S01-0111 诸葛亮 | 4 | 远程静态；登场查看天灾与随后±1分段且模式前置；进攻/阵亡的牌库顶身份仅结算揭示，返1先声明支付。 | `L12EnterPublicTriggerPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0112 孙武 | 3 | 远程静态；登场返1授予下一战术免费；阵亡公开墓地战术目标前置，目标失效不影响已生成候选。 | `L12S1ExtendedEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0113 养由基 | 2 | 远程静态；后排返1只扩大本回合对后排的攻击权限，卡面无回合次数限制且可重新活跃后再发动。 | `L12MoraleReturns`、`L12S1ExtendedEffects` | `LatestBugRegressionTests`（`ability:extendedRange`） | 通过 |
| S01-0114 秦良玉 | 3 | 远程静态、士气比较费用层、登场追加1休整士气三者互不覆盖。 | `L12Actions`、`L12S1ExtendedEffects`、`L12StructuredCardRules` | `AtomicReviewBatch2RegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0115 荆轲 | 3 | 前排远程静态；登场公开条件锁定后可选抽；阵亡返1为冒号前费用，最多1目标允许0且公开目标前置。 | `L12S1ExtendedEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S01-0116 西施 | 2 | 远程静态；弃置自身+返1在入栈前支付，最多1手牌军团/公开位置先声明；召唤与随后抽牌分别响应，零目标直接从抽牌段开始。 | `L12S1ExtendedEffects`、`L12CompositeEffectPlans` | `AtomicReviewBatch6KARegressionTests`、`LatestBugRegressionTests` | 明确错误→已修复 |
| S01-0117 山河社稷图 | 2 | 登场追加活跃士气；主动两模式的费用/模式前置，牌库顶3身份与选择仅在检索段合法结算时私密展示。 | `L12ActiveAbilities`、`L12CardEffects`、`L12MoraleReturns` | `GameEngineTests`、`NewSystemsTests` | 通过 |
| S01-0118 神妙行军 | 1 | 前排强化目标、可选击杀模式、返2费用及击杀目标全部在出牌前声明；两段独立失效。 | `L12CompositeEffectPlans`、`L12CardEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0119 观星 | 1 | 顶5身份和排序仅结算时私密可见；追加活跃士气模式入栈前声明并作为独立段。 | `L12CompositeEffectPlans`、`L12CardEffects` | `AtomicReviewBatch6CRegressionTests` | 通过 |
| S01-0120 空城计 | 1 | 返1是冒号前费用；抵挡与条件可选抽牌分别响应，抽牌模式预声明，拒绝/条件失效不造空段或退款。 | `L12PublicResponseEffectPlans`、`L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6KARegressionTests`、`AtomicReviewBatch5RegressionTests` | 明确错误→已修复 |
| S01-01C1 士气·天廷 | 2 | 两项均各自回合1次；消耗2追加活跃士气与士气归零追加2休整士气使用不同次数键，同一归零事件幂等。 | `L12ActiveAbilities`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch1RegressionTests`、`AtomicReviewBatch6JBRegressionTests` | 通过 |
| S01-01D1 凌霄宝殿 | 2 | 奖励的追加士气/随后抽牌分段；交换在入栈前休整来源并按目标费用返还士气，击杀与随后复活分段，前段/目标失效不退款不吞后段。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects`、`L12CompositeEffectPlans` | `AtomicReviewBatch6KARegressionTests`、`AtomicReviewBatch2RegressionTests` | 明确错误→已修复 |
| S01-01M1 杨戬 | 4 | 两个主宰能力分别回合1次；抽牌与随后私密回牌分段，回牌身份延迟；返4非致命；哮天犬登场/阵亡为独立触发。 | `L12ActiveAbilities`、`L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6KARegressionTests`、`Bq20260830RegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S01-01M2 孟婆 | 1 | 主能力回合1次且模式前置；返1/公开军团或私密弃牌费用在入栈前完成，抽牌条件在结算重验。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch3RegressionTests`、`RuleKernelTests` | 通过 |

## 通用天灾（10 张 / 15 项能力）

天灾效果按规则不可响应，因此同一天灾触发文本内的顺序处理不机械拆成响应段；仍逐项核对隐藏信息、所有者区域与持续层。

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-DS01 黯陨晨星 | 1 | 主要阶段开始公开掷骰；双数休整己方活跃士气，单数由回合玩家公开选本回合模式。 | `L12Disasters`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6JBRegressionTests`、`S2UniversalEffectsTests` | 通过 |
| S01-DS02 百鬼夜行 | 3 | 触发非致命伤害；持续只给带天灾等级军团的主宰伤害+1；回合末手牌按所有者自选顺序压至5。 | `L12Disasters`、`L12Actions`、`L12StructuredCardRules` | `ExtendedCardEffectsTests`、`NewSystemsTests` | 通过 |
| S01-DS03 腐秽大地 | 3 | 触发将后排军团送所有者墓地；持续禁止普通后排落位；反击战术费用为0但仍走正常响应合法性。 | `L12Disasters`、`L12Actions`、`L12EffectGeneratedPlay` | `ImmortalityRegressionTests`、`NewSystemsTests` | 通过 |
| S01-DS04 雷霆天怒 | 2 | 进攻掷骰1~2时休整并结束该次进攻；触发最低点返回己方军团，但最低点并列无权威规则。 | `L12Disasters`、`L12Actions`、`L12StructuredCardRules` | `NewSystemsTests` | 有疑点（OPEN #5） |
| S01-DS05 魔龙降世 | 1 | 掷骰决定公开列并按所有者入墓；随后各玩家对自己墓地4张公开有序选择回牌库底，天灾不可响应。 | `L12Disasters`、`L12AuthoritativeCardZones` | `NewSystemsTests` | 通过 |
| S01-DS06 神之天平 | 1 | 先统一血量并按是否变化抽牌，再各自弃1抽1；私密弃牌由各受影响玩家选择。 | `L12Disasters`、`L12StructuredCardRules` | `NewSystemsTests` | 通过 |
| S01-DS07 天启默示录 | 1 | 各玩家选择战场保留至2，其他军团进入所有者墓地；手牌自选顺序回底后抽4，身份仅本人可见。 | `L12Disasters`、`L12AuthoritativeCardZones` | `NewSystemsTests` | 通过 |
| S01-DS08 虚构的圣杯 | 1 | 每次合法使用圣物效果时对该玩家主宰造成1点非致命伤害；不是入栈前费用。 | `L12PromptsAndSetup`、`L12StructuredCardRules` | `ExtendedCardEffectsTests`、`LatestBugRegressionTests` | 通过 |
| S01-DS09 诸神黄昏 | 1 | 全军团进入所有者墓地；开场/主动触发分支分别抽牌；主动分支立即结束并给触发者追加回合。 | `L12Disasters`、`L12StructuredCardRules` | `NewSystemsTests` | 通过 |
| S01-DS10 堙灭 | 1 | 每个回合开始对所有主宰各造成1点非致命伤害，依次产生合法受伤触发且不造成致命。 | `L12GameEngine`、`L12Disasters` | `Bq20260830_02RegressionTests`、`NewSystemsTests` | 通过 |

## 同类全池扫描与未改边界

- 扫描 `cards.s1.json` / `cards.s2.json` 的“随后”、冒号费用、最多、牌库顶部/查看/展示/检索、回合1次；运行时扫描 `CreatePrompt`、`PushEffect`、`QueueOrPushTriggeredEffect`、`CompositeFirstSegmentData`、`PendingActivation`、`FinishStackItem`、`FindSource`、直接战场赋值与手牌/牌库/墓地移动。
- 本批根因只命中上述 8 张 S01 范围卡；同一公共框架上的 S01-0014/0015/0118/0119、S02 李牧、玛格丽特、血鹰、卡诺匹斯等既有迁移作为控制组，未重复改写。
- 天灾不可响应，故 S01-DS05/06/07/09 的“随后”保留一次天灾结算中的顺序处理；这不是多段响应遗漏。
- 李靖与雷霆天怒仅登记 `OPEN-QUESTIONS.md`，没有按实现偏好改卡效。
