# S02 太阳城与阿斯加德逐卡独立语义审计（Batch 6L-B）

更新日期：2026-09-01

## 范围与结论

- 固定范围为 `cards.s2.json` 的 8 张 `taiyangcheng` 太阳城卡与 8 张 `asgard` 阿斯加德卡，含主宰、Token/特殊牌与跨产品区域规则，共 16 张、44 项能力；S02 天灾留后批。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。逐行核对时点、费用预付、独立段、区域/所有者、数值、隐藏信息、多实例、来源 LKI 与响应无效。
- 唯一结论：12 张通过、4 张明确错误并修复、有疑点 0、缺少测试 0、未实现 0。

## 逐卡逐能力结论

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S02-0201 增殖的甲虫 | 2 | 不进入手牌/牌库且开局在所有者墓地；从任意战场离开均消灭并进入所有者墓地，不能进攻或支援。 | `L12SpecialDeckRules`、`L12GameEngine` | `DeckValidatorTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S02-0202 陵墓圣武士 | 2 | 己方回合每张卡名含〈陵墓〉的己方军团离场均按当时控制者登记一次，登场费递减并在回合末清零；阵亡守卫和位置前置。 | `L12GameEngine`、`L12Actions`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6LBRegressionTests`、`AtomicReviewBatch6ERegressionTests` | 明确错误→已修复 |
| S02-0203 哈特谢普苏特 | 3 | 无守卫时减费；登场甲虫与阵亡抽牌均在触发候选期声明，拒绝不造空栈，甲虫位置结算重验。 | `L12Actions`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S02-0204 伊姆何泰普 | 3 | 远程无损；登场条件锁定后公开墓地费用6+太阳城目标先声明并以展示加入手牌事件移动；主动休整只降低下一张带天灾等级的太阳城军团。 | `L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0205 黄金圣甲虫 | 4 | 在圣物区阻止其他圣物手牌打出；登场甲虫位置前置；两个主动先休整，守卫/私密弃牌费用与最多2公开敌军目标提交后入栈。 | `L12StructuredCardRules`、`L12PublicActiveEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch3RegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S02-0206 无畏的刺杀 | 3 | 前排太阳城目标出牌前声明；+3000与对军团确击仅本回合，目标本回合不能因效果转活跃，结束时按所有者区域弃置。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch3RegressionTests`、`RuleKernelTests` | 通过 |
| S02-0207 沙漠君临 | 1 | 最多3张公开场上军团、等量天灾等级的私密手牌军团与公开位置完整声明；军团费用入栈前原子弃置，位置失效不覆盖、不改选、不退款。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch1RegressionTests`、`AtomicReviewBatch6LBRegressionTests` | 明确错误→已修复 |
| S02-02M1 奈芙蒂斯 | 3 | 守卫不能进攻主宰；己方回合弃任意数量军团是主动费用并转化为下一张天灾太阳城减费；对方回合费用2+太阳城阵亡的甲虫触发回合1次且位置前置。 | `L12PublicActiveEffectPlans`、`L12PublicTriggerEffectPlans` | `S2FactionRegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S02-0301 雷神之锤 | 4 | 登场仅获得本回合可攻主宰；阵亡发动模式先声明，抽后弃牌身份延迟；墓地主动按钮即发动意图，有序3张其他墓地费用与公开位置提交后原子回底并入栈。 | `L12PublicTriggerEffectPlans`、`L12S2RemainingEffects` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6LBRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S02-0302 步行者罗洛 | 4 | 墓地最多8张阿斯加德卡有序回底为登场费用减免；前排挑衅且不能支援，登场治疗为确定效果。 | `L12Actions`、`L12AtomicRuntimeIntegration` | `S2FactionRegressionTests`、`AtomicEffectsTests` | 通过 |
| S02-0303 卡纽特 | 2 | 可选主宰伤害为冒号前登场费用；登场最多2张不同名阿斯加德军团目标入栈前声明，战场/墓地来源均用快照分别发动阵亡效果。 | `L12EnterPublicTriggerPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0304 玛格丽特一世 | 3 | 远程静态；登场弃顶模式候选期声明；受主宰效果伤害时休整费用预付，治疗与随后禁疗是两个独立响应段。 | `L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch6GARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0305 安德华拉诺特 | 4 | 开局选择与起手修正不重复；结束阶段由拥有者私密弃至6；不能从手牌打出圣物；主宰伤害抽牌与首伤改2分别按己/对方回合次数处理。 | `L12PromptsAndSetup`、`L12PublicTriggerEffectPlans`、`L12GameEngine` | `AtomicReviewBatch6GARegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0306 密米尔之泉 | 2 | 本回合累计效果伤害达到2后回合1次；治疗1+抽1同段，随后可选弃顶2另开响应，拒绝/无效前段不吞后段。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch6CRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S02-0307 海拉的凝视 | 1 | 弃置己方牌库顶1张为冒号前费用，支付后才入栈；公开敌军目标预声明，目标失效不恢复牌库费用。 | `L12CompositeEffectPlans`、`L12S2FactionEffects` | `AtomicReviewBatch3RegressionTests` | 通过 |
| S02-03M1 雷神索尔 | 3 | 开局锤检索计入起手；血量不高于3时主动消耗2份合法公开资源后入栈，跨控制守卫按卡面在己方回合可作士气；后续登场冲锋只持续本回合且效果回血永久禁止。 | `L12MoralePayments`、`L12S2RemainingEffects` | `AtomicReviewBatch6LBRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |

## 公共根因、跨范围控制与保留边界

- `RemoveFromField` 与 `MoveFieldCardToZone` 是普通军团真正离场的公共事务；两处统一登记〈陵墓圣武士〉的控制者计数，移动/晋升叠放等不构成离场的裸格位交换不误计。计数只在该控制者己方回合生效并在其回合结束清零，所有者墓地规则不改变计数归属。
- 〈沙漠君临〉复用 HandPlay `Composite/PendingActivation`。全部目标与位置先验证，弃置费用在真实 `StackItem` 前支付；结算只消费不可变声明并调用公共 Try 登场事务，响应后手牌目标或格位失效仅取消登场。
- 主动按钮本身已表达发动意图，删除〈雷神之锤〉墓地能力的重复 yes/no Prompt；有序墓地费用与位置仍可在提交前取消，提交后费用和 once 不恢复。
- 〈陵墓守卫〉“我方回合在战场可视为1张士气”按当前控制者和回合判断，不附加控制者必须为太阳城阵营的文字外条件；公共资源选择继续同时支持普通士气、神力与守卫。
- S1 伊西斯/奥西里斯、五罐、拉美西斯、洛基、瓦尔基里与诸神黄昏作为跨范围控制沿用 6K-B 已验收结论：五罐完成记录位于 `CanopicProgress`，不是五张真实普通圣物；所有者墓地、额外区与特殊胜利使用单一实例。没有新增裁定项。

## 红绿与验证证据

- 红基线为2/6绿、4/6红，分别命中陵墓离场计数、沙漠君临冒号前费用、雷神之锤重复发动确认与跨控制陵墓守卫资源；修复后专项6/6、新旧精确组合8/8、迁移兼容7/7。
- S2阵营、6E、6I-B、Batch1与6K-B组合兼容251/251；串行完整规则979/979。Focused首次在572条已通过后触发.NET 10 testhost内部CLR Regex瞬态崩溃，随后显式串行复验979/979；Batch的Release配置规则同样979/979。
- 16卡/44项静态门禁、公共触发声明门禁、矩阵导出和原子审计248/248通过；`cardCase=0`、`cardConditional=187`、`cardSwitchArm=67`、`effectTextInference=0`、未路由有权威入口74/74、无权威入口0。运行时`CreatePrompt(`棘轮随重复确认移除收紧至135个静态令牌。
- 完整Release与Git同步留全部批次最终统一收口；本批不提交、不推送、不部署。
