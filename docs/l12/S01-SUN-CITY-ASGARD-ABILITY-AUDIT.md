# S01 太阳城与阿斯加德逐卡独立语义审计（Batch 6K-B）

更新日期：2026-09-02

## 范围与结论

- 固定范围为 `cards.s1.json` 的 29 张 `taiyangcheng` 卡与 24 张 `asgard` 卡（含主宰、士气、阵营、Token/特殊牌），共 53 张、124 项能力；不重复 S01 通用、天廷或天灾。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。每行均独立核对时点、费用/目标声明、区域、数值层、公开与私密信息、目标失效、来源 LKI、多实例次数与响应无效。
- 唯一结论：45 张通过、8 张明确错误并修复、有疑点 0；缺少测试 0、未实现 0。霍列姆赫布与托勒密十三世已按 2026-09-02 玩家裁定闭环。

## 太阳城（29 张 / 69 项能力）

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-0201 图特摩斯三世 | 4 | 登场回合反击免疫；登场击杀目标前置；进攻/阵亡的全体-1000与随后击杀分别响应，击杀目标入栈前声明且失效只取消后段。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6KBRegressionTests` | 明确错误→已修复 |
| S01-0202 拉美西斯二世 | 3 | 登场保护与无守卫减费按公开场面；最多3张其他同阵营军团及顺序先声明，各委托登场效果保持自己的声明/响应。 | `L12EnterPublicTriggerPlans`、`L12S1FactionEffects` | `Bq20260830RegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0203 美尼斯 | 2 | 对方回合持续兵力层按守卫存在重算；进攻可弃自身在内的己方军团作为冒号费用，预付后不恢复且来源离场不获得自身强化。 | `L12AttackPublicTriggerPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0204 陵墓构造体 | 5 | 前排挑衅、叠放与每守卫+1000分层；阵亡/离场各自生成候选，按最后已知附属、所有者墓地与公开空格原子登场。 | `L12PublicTriggerEffectPlans`、`L12AuthoritativeCardZones` | `AtomicReviewBatch6DRegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0205 霍列姆赫布 | 2 | 登场弃守卫费用在入栈前声明支付；即将阵亡时选择并重验受控战场陵墓守卫，代替者承接原致命动作的真实目的区、所有者解析及离场/阵亡事件。 | `L12EnterPublicTriggerPlans`、`L12LethalReplacements` | `AtomicReviewBatch6JARegressionTests`、`LatestBugRegressionTests`、`RulingClosureRegressionTests` | 通过 |
| S01-0206 萨拉丁 | 4 | 位移回合1次；相邻攻击加成仅本次进攻；进攻/阵亡可选守卫与公开位置先声明，结算重验不覆盖。 | `L12PublicTriggerEffectPlans`、`L12S1FactionEffects` | `GameEngineTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0207 图坦卡蒙 | 2 | 登场最多2守卫及各位置前置；阵亡公开墓地目标前置并放牌库顶，目标失效不改来源离场。 | `L12PublicTriggerEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0208 阿伊 | 3 | 远程静态；登场守卫与位置前置；进攻士气费用及前排低兵力目标入栈前声明支付，失效不退款。 | `L12AttackPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0209 纳芙蒂蒂 | 3 | 远程静态；登场条件锁定后由受影响对手在结算私密弃牌；阵亡按当时手牌数同时伤害/治疗。 | `L12S1FactionEffects`、`L12RuleKernelIntegration` | `AtomicReviewBatch6KBRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S01-0210 尼托克丽丝 | 3 | 远程静态；登场公开守卫目标前置；阵亡墓地军团与公开位置前置并以 Try 登场事务重验。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0211 托勒密十三世 | 2 | 远程静态；父效果完整离栈后只复制上一张主动战术的效果，重新声明该效果所需模式/目标，不重付打出或冒号前费用，不制造虚拟区域移动。 | `L12PostResolutionGeneratedEffects`、`L12CompositeEffectPlans` | `Bq20260830RegressionTests`、`AtomicReviewBatch6JCRegressionTests`、`RulingClosureRegressionTests` | 通过 |
| S01-0212 陵墓守卫 | 3 | 构筑外置且不能入手/牌库；任何离场回所有者墓地；战场资源、对方回合费用与前排兵力均按当前控制区/位置计算。 | `L12SpecialDeckRules`、`L12MoralePayments`、`L12AuthoritativeCardZones` | `DeckValidatorTests`、`ExtendedCardEffectsTests`、`AtomicReviewBatch6DRegressionTests` | 通过 |
| S01-0213 锡瓦的卡巴 | 2 | 前排远程静态；对方进攻后私密来源与公开空格先声明，免费活跃登场后锁定的休整士气按当时合法公开对象处理。 | `L12PublicTriggerEffectPlans`、`L12CombatTimeline` | `AtomicReviewBatch6KBRegressionTests`、`CombatTimelineRegressionTests` | 通过 |
| S01-0214 克利奥帕特拉七世 | 2 | 远程静态；主动休整与1资源预付，墓地守卫及公开位置在入栈前声明，结算位置失效不退款。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects` | `ExtendedCardEffectsTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0215 安卡神碑 | 2 | 登场强化目标前置；主动按钮即发动意图，模式、私密弃牌/公开守卫费用与目标在休整入栈前原子提交。 | `L12EnterPublicTriggerPlans`、`L12PublicActiveEffectPlans` | `ExtendedCardEffectsTests`、`AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0216 卡诺匹斯箱 | 2 | 不占普通圣物上限；隐藏检索只在首段合法开始后读取并展示命中，随后治疗+弃置另开响应，首段无效仍执行后段。 | `L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6KBRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S01-0217 卡诺匹斯罐 一 | 2 | 公开太阳城目标入栈前声明；强化/强攻与随后弃置分别响应，目标失效不阻止弃置段。 | `L12EnterPublicTriggerPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch6JARegressionTests`、`ExtendedCardEffectsTests` | 通过 |
| S01-0218 卡诺匹斯罐 二 | 2 | 下1张手牌战术免费为首段；随后弃置为独立段，首段无效不得吞弃置，免费打出仍走正常 HandPlay。 | `L12CompositeEffectPlans`、`L12EffectGeneratedPlay`、`L12S1FactionEffects` | `AtomicReviewBatch6KBRegressionTests`、`AtomicReviewBatch6JCRegressionTests` | 明确错误→已修复 |
| S01-0219 卡诺匹斯罐 三 | 2 | 2点临时士气属于本回合资源层；随后弃置独立响应，任一段无效不回滚另一合法段。 | `L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6KBRegressionTests`、`Bq20260830_02RegressionTests` | 明确错误→已修复 |
| S01-0220 卡诺匹斯罐 四 | 2 | 最多2个公开太阳城目标先声明；免死与随后弃置分别响应，无目标时首个真实栈直接为弃置段。 | `L12EnterPublicTriggerPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0221 杜阿特之门 | 1 | 模式与公开击杀/最多0墓地回收目标出牌前声明；结算只取消失效对象。 | `L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0222 法老王的庆典 | 1 | 顶5身份只在合法结算查看；两张命中展示，手牌/墓地分配后其余由发动者私密排序回底。 | `L12S1FactionEffects`、`L12PromptsAndSetup` | `ExtendedCardEffectsTests`、`LatestBugRegressionTests` | 通过 |
| S01-0223 不朽之礼 | 1 | 对方造成费用>2军团离场时必抽1；随后墓地守卫与公开位置前置，抽牌无效不吞独立登场段。 | `L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch6KBRegressionTests`、`AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0224 智慧法典 卷一 | 2 | 对方发动前弃牌是其手牌私密费用；成功发动后的抽牌与随后公开墓地回收分别响应；一次性成功奖励只属于被响应的确切StackItem，不复制到其语义独立后段。 | `L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch5RegressionTests`、`AtomicReviewBatch6CRegressionTests`、`LatestBugRegressionTests` | 明确错误→已修复 |
| S01-02C1 士气·太阳城 | 2 | 两项各自回合1次；资源费用、墓地守卫与公开位置先声明，抽牌条件在按钮提交时验证。 | `L12PublicActiveEffectPlans`、`L12ActiveAbilities` | `ExtendedCardEffectsTests`、`RuleKernelTests` | 通过 |
| S01-02D1 众神之乡 | 4 | 守卫持续层与开场士气独立；顶3隐藏处理与随后公开墓地回收分别响应，回收目标前置；置底敌军按所有者。 | `L12PublicActiveEffectPlans`、`L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch3RegressionTests`、`AtomicReviewBatch6KBRegressionTests` | 明确错误→已修复 |
| S01-02M1 伊西斯 | 2 | 开场奥西里斯用单一实例入所有者墓地；主动弃3守卫、公开卡诺匹斯目标及奖励模式全部预声明支付。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch3RegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-02M2 复苏的奥西里斯 | 2 | 五种罐条件成立才替换；双人胜利/多人治疗+公开军团位置按模式处理，守卫+1000为持续层。 | `L12ActiveAbilities`、`L12S1FactionEffects` | `LatestBugRegressionTests`、`S2FactionRegressionTests` | 通过 |
| S01-02M3 梅杰德 | 2 | 主动普通/强化模式、资源、可选休整守卫与目标前置；对方回合受伤触发墓地守卫/位置前置且回合1次。 | `L12PublicActiveEffectPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch3RegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |

## 阿斯加德（24 张 / 55 项能力）

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-0301 贝奥武夫 | 4 | 墓地减费按当前公开墓地；登场弃顶2确定；进攻伤害费用预付；阵亡抽牌可选模式入栈前声明。 | `L12AttackPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6IARegressionTests` | 通过 |
| S01-0302 金发哈拉尔 | 3 | 登场费按己方军团数；低血时进攻获得本回合强攻；阵亡治疗强制且来源离场用 LKI。 | `L12Actions`、`L12S1FactionEffects` | `GameEngineTests`、`RuleKernelTests` | 通过 |
| S01-0303 传奇的拉格纳 | 3 | 自伤减费为打出选择；低血登场冲锋；阵亡抽1后弃1同句结算，弃牌身份仅抽后私密选择。 | `L12PublicTriggerEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6IBRegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0304 无情者哈拉尔 | 3 | 自伤减费前置；登场血量条件锁定；阵亡强制公开击杀目标入栈前声明，无目标不造空栈。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S01-0305 勇士比约恩 | 2 | 低血减费；阵亡时伤害及有序4张墓地费用、公开位置全部预付/声明，位置失效不退款且不覆盖。 | `L12PublicTriggerEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0306 奥拉夫二世 | 3 | 低血减费；进攻有序墓地费用预付；阵亡抽2后弃1的具体手牌身份合法延迟。 | `L12AttackPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S01-0307 阿尔维达 | 2 | 主动弃自身不是阵亡，伤害与私密手牌军团/公开位置先提交；阵亡公开墓地回收目标前置。 | `L12PublicActiveEffectPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6IBRegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0308 血斧艾瑞克 | 3 | 自伤减费；造成主宰伤害后由受影响对手私密弃牌；阵亡公开墓地军团/位置前置并 Try 登场。 | `L12S1FactionEffects`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6ERegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0309 布伦希尔德 | 3 | 远程静态；登场自伤费用与齐格鲁德/位置预付预声明；阵亡抽牌可选条件在候选建立时锁定。 | `L12PublicTriggerEffectPlans`、`L12EnterPublicTriggerPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6ERegressionTests` | 通过 |
| S01-0310 齐格鲁德 | 3 | 自伤减费；位移回合1次；进攻时仅在格拉墨仍存在时获得本回合兵力层。 | `L12Actions`、`L12S1FactionEffects` | `GameEngineTests`、`LatestBugRegressionTests` | 通过 |
| S01-0311 古斯塔夫一世 | 2 | 进攻与进攻后各自独立时点；各自2张有序墓地费用入栈前预付，后者使用 pending→final 回合1次。 | `L12AttackPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6HRegressionTests`、`AtomicReviewBatch6JBRegressionTests` | 通过 |
| S01-0312 铁盾拉葛莎 | 1 | 前排挑衅与对方回合+1000均为位置/回合条件持续层，离位或换回合即时撤销未消费层。 | `L12StructuredCardRules`、`L12RuleKernel` | `GameEngineTests`、`RuleKernelTests` | 通过 |
| S01-0313 神箭奥德尔 | 3 | 远程静态；登场自伤费用入栈前声明；阵亡公开活跃军团目标前置并重验。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S01-0314 奥尔加 | 3 | 远程静态与自伤减费；主动弃自身不是阵亡，公开前排目标前置且兵力-2000仅本回合。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects` | `LatestBugRegressionTests`、`RuleKernelTests` | 通过 |
| S01-0315 无骨者伊瓦尔 | 1 | 是否查看顶3在触发入栈前公开声明；牌库身份仅首段合法开始后私密揭示，拒绝不生成空响应栈。 | `L12PublicTriggerEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6KBRegressionTests` | 明确错误→已修复 |
| S01-0316 夺命诗人埃吉尔 | 2 | 前排远程静态；登场自伤+弃顶2冒号费用及公开敌军目标入栈前完整提交，费用不因无效返还。 | `L12EnterPublicTriggerPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6JARegressionTests` | 通过 |
| S01-0317 神剑格拉墨 | 3 | 登场弃顶2费用与公开置底目标前置；主动休整的4张墓地军团有序费用预付；2资源转活跃独立。 | `L12EnterPublicTriggerPlans`、`L12PublicActiveEffectPlans` | `AtomicReviewBatch6JARegressionTests`、`ExtendedCardEffectsTests` | 通过 |
| S01-0318 女武神的召唤 | 1 | 自伤条件费用与墓地军团/公开位置出牌前声明；低血免伤只修正该费用，位置失效不退款。 | `L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0319 猎杀时刻 | 1 | 有序4张墓地费用与公开击杀目标出牌前声明并原子支付；无效/目标失效不恢复费用。 | `L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6ARegressionTests` | 通过 |
| S01-0320 复仇血鹰 | 1 | 军团阵亡时全体-1000与随后两张公开墓地分配分别响应；后段部分目标失效不吞前段。 | `L12PublicTriggerEffectPlans`、`L12CompositeEffectPlans` | `AtomicReviewBatch5RegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-03C1 士气·阿斯加德 | 1 | 回合1次消耗2抽牌；低血时额外1资源/治疗模式在入栈前声明并一起支付，两个按钮不产生额外次数。 | `L12PublicActiveEffectPlans`、`L12ActiveAbilities` | `ExtendedCardEffectsTests`、`RuleKernelTests` | 通过 |
| S01-03D1 英灵殿 | 4 | 自伤减费、主动双击杀及开场士气按各自时点；弃顶2与随后公开墓地回收分别响应，目标前置且前段无效不吞后段。 | `L12PublicActiveEffectPlans`、`L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch3RegressionTests`、`AtomicReviewBatch6KBRegressionTests` | 明确错误→已修复 |
| S01-03M1 瓦尔基里 | 2 | 抽牌阶段替换为弃顶2；主动1资源+自伤与两张公开墓地目标/分配预声明，单一真实实例分别回底/入手。 | `L12PublicActiveEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch3RegressionTests`、`ExtendedCardEffectsTests` | 通过 |
| S01-03M2 洛基 | 1 | 两模式合计回合1次并共用同一 once key；1资源预付，墓地2张公开目标前置，抽后弃牌身份合法延迟。 | `L12ActiveAbilities`、`L12PublicActiveEffectPlans` | `AtomicReviewBatch3RegressionTests`、`ExtendedCardEffectsTests` | 通过 |

## 同类扫描与保留边界

- 全池扫描 `CreatePrompt`、`QueueOrPushTriggeredEffect`、`PushEffect`、`FinishStackItem`、`CompositeFirstSegmentData`、墓地/牌库移动和直接落位；本批确定性根因只迁移上表 8 张卡的 9 个时点，其中智慧法典修复的是独立后段不得继承已完成StackItem的一次性奖励标记。
- 纳芙蒂蒂、血斧艾瑞克等由受影响玩家在结算选择自己的隐藏手牌是合法结算期 Prompt；拉格纳、奥拉夫、洛基的“抽后弃牌”也必须延迟，未迁到公开声明。
- 卡诺匹斯罐一/四已由 6J-A 分段，本批补齐箱、罐二、罐三；陵墓构造体所有者/LKI 与位置事务沿用 6D/6E；雷神之锤墓地登场属于 S02 交叉控制组，未混入本批。
- 霍列姆赫布与托勒密十三世已按玩家裁定收口；全池致命替代与重复效果路径已扫描，未改动无关卡牌语义。
