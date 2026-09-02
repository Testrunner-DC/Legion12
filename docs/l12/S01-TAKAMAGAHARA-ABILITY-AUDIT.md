# S01 高天原逐卡独立语义审计（Batch 6K-C）

更新日期：2026-09-01

## 范围与结论

- 固定范围为 `cards.s1.json` 全部 24 张 `gaotianyuan` 卡，含主宰、士气、阵营、草薙剑和特殊区形态，共 55 项原子目录能力。
- 权威顺序为玩家裁定 > `FAQ-RULINGS.md`/FAQ > 规则书与关键词 > 印刷卡面。每行独立核对时点、入栈声明/费用、区域、数值层、可见性、目标失效、来源 LKI、多实例次数与响应无效。
- 唯一结论：14 张通过、10 张明确错误并修复，有疑点 0、缺少测试 0、未实现 0。本批没有新增待裁定项。

## 逐卡逐能力结论

| 卡号 / 卡名 | 项数 | 最短规则断言 | 运行时代码证据 | 测试证据 | 唯一状态 |
|---|---:|---|---|---|---|
| S01-0401 本多忠胜 | 2 | 登场冲锋确定；进攻全体费用-1与随后击杀分别响应，公开击杀目标在首段入栈前声明，两段互不回滚。 | `L12AttackPublicTriggerPlans`、`L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-0402 织田信长 | 2 | 登场击杀与进攻士气费用/全体费用层均在公共声明后结算，已付士气不因无效返还。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0403 上杉谦信 | 4 | 登场阈值按公开反击数计算；阵亡的最多2张手牌身份私密、格位公开，声明后位置重验且不覆盖。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 通过 |
| S01-0404 真田幸村 | 1 | 登场获得冲锋是无选择确定效果，不生成空声明。 | `L12AtomicRuntimeIntegration`、`AtomicEffects` | `RuleKernelTests`、`NewSystemsTests` | 通过 |
| S01-0405 宫本武藏 | 2 | 隐匿盖牌不是军团，不阻断“无其他军团”冲锋；进攻抽牌可选模式在候选建立时声明。 | `L12AtomicRuntimeIntegration`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6KCRegressionTests` | 通过 |
| S01-0406 土方岁三 | 2 | 登场两个公开击杀目标、进攻士气费用与目标均入栈前声明，单目标失效不改选。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0407 坂本龙马 | 2 | 登场最多2军团的移动目标先声明；阵亡私密手牌军团+公开格位先声明，万物统御之戒使通用卡按控制者阵营处理。 | `L12EnterPublicTriggerPlans`、`L12PublicTriggerEffectPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6IBRegressionTests` | 明确错误→已修复 |
| S01-0408 高杉晋作 | 2 | 登场抽1与同句目标减费在一个语义段；登场/进攻的公开目标与进攻士气费用均前置。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6JARegressionTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0409 源义经 | 4 | 后排进攻的2000兵力/距离/无损仅属当次进攻；移动回合1次；击杀抽牌可选声明不在结算期首问。 | `L12Actions`、`L12AtomicRuntimeIntegration` | `CombatTimelineRegressionTests`、`AtomicReviewBatch6IARegressionTests` | 通过 |
| S01-0410 巴御前 | 2 | 远程距离/无损为静态战斗档案；登场冲锋确定结算。 | `L12StructuredCardRules`、`L12AtomicRuntimeIntegration` | `GameEngineTests`、`RuleKernelTests` | 通过 |
| S01-0411 安倍晴明 | 3 | 远程静态正确；登场免死公开目标入栈前声明，只消费一次替代。 | `L12EnterPublicTriggerPlans`、`L12RuleKernel` | `AtomicReviewBatch6JARegressionTests`、`LatestBugRegressionTests` | 通过 |
| S01-0412 立花誚千代 | 2 | 登场公开减费目标先声明；阵亡全体减费用 LKI 延续到下个己方回合结束。 | `L12EnterPublicTriggerPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6JARegressionTests`、`RuleKernelTests` | 通过 |
| S01-0413 源博雅 | 4 | 远程静态正确；登场条件快照与可选抽牌前置；进攻时公开反击目标入栈前声明。 | `L12AtomicRuntimeIntegration`、`L12AttackPublicTriggerPlans` | `AtomicReviewBatch6IARegressionTests`、`AtomicReviewBatch6HRegressionTests` | 通过 |
| S01-0414 桂小五郎 | 2 | 进攻后返牌库顶与“返回牌库时”士气段是独立候选；来源离区后用 LKI，后段无效不回滚返回。 | `L12PublicTriggerEffectPlans`、`L12AuthoritativeCardZones` | `AtomicReviewBatch6DRegressionTests` | 通过 |
| S01-0415 服部半藏 | 3 | 登场先公开3秒后覆盖；覆盖时不是军团、不得被选/进攻/移动/支援，仅天灾依然影响；后续回合翻正不重置登场回合。 | `L12CardEffects`、`L12StructuredCardRules`、`L12Disasters` | `NewSystemsTests`、`AtomicReviewBatch6KCRegressionTests` | 通过 |
| S01-0416 稻姬本多小松 | 3 | 远程静态正确；登场与进攻共用公开前排阵营军团查询，排除隐匿且包含戒指下的通用军团。 | `L12EnterPublicTriggerPlans`、`L12AttackPublicTriggerPlans`、`L12StructuredCardRules` | `AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-0417 草薙剑 | 2 | 登场击杀目标前置；两个主动模式仅可选公开军团并按戒指阵营；圣物/军团形态始终是单一实例，离场按所有者回牌库顶。 | `L12EnterPublicTriggerPlans`、`L12PublicActiveEffectPlans`、`L12AuthoritativeCardZones` | `AtomicReviewBatch6DRegressionTests`、`AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-0418 天诛 | 1 | 打出前声明公开费用不高于7的击杀目标；付费/移入Resolving后无效不回手，目标失效不改选。 | `L12CompositeEffectPlans`、`L12HandPlay` | `AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-0419 花魁的馈赠 | 1 | 顶3身份只在首段合法开始后查看，选牌展示入手、其余私密排序回底；随后公开士气先声明且独立响应，戒指阵营正确。 | `L12CompositeEffectPlans`、`L12S1FactionEffects`、`L12StructuredCardRules` | `AtomicReviewBatch6CRegressionTests`、`AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-0420 切腹仪式 | 2 | 仅在对方进攻后触发；抽1与同句公开目标减费同段，目标在候选入栈前声明并在结算重验。 | `L12PublicResponseEffectPlans`、`L12S1ExtendedEffects` | `AtomicReviewBatch6JBRegressionTests`、`CombatTimelineRegressionTests` | 通过 |
| S01-04C1 士气·高天原 | 1 | 回合1次先选择并消耗2士气；效果结算先抽1，再按结算后的场面选择活跃军团及相邻空位，目标选择可直接“不位移”，不得在费用阶段预选。 | `L12ActiveAbilities`、`L12EffectContinuations` | `AtomicReviewBatch3RegressionTests`、`NewSystemsTests` | 明确错误→已修复 |
| S01-04D1 黄泉之门 | 3 | 两个回合次数独立；抽牌/全体费用与随后两击杀分成4段；主动休整的公开墓地目标先声明，戒指下通用墓地卡可被回收。 | `L12PublicActiveEffectPlans`、`L12CompositeEffectPlans`、`L12StructuredCardRules` | `AtomicReviewBatch2RegressionTests`、`AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |
| S01-04M1 天照大神 | 2 | 两项回合1次独立；士气/弃手牌费用与公开目标入栈前完整提交；减费→击杀、翻士气→前排强化均分段，无效不返费也不吞后段。 | `L12PublicActiveEffectPlans`、`L12CompositeEffectPlans`、`L12S1FactionEffects` | `AtomicReviewBatch6KCRegressionTests`、`S2FactionRegressionTests` | 明确错误→已修复 |
| S01-04M2 须佐之男 | 3 | 前排进攻强化只在当回合后续的前排进攻中生效；公开目标排除隐匿并按戒指阵营；草薙剑使用单一实例、入前排不占圣物区、离场可回所有者牌库顶。 | `L12PublicActiveEffectPlans`、`L12Actions`、`L12AuthoritativeCardZones` | `AtomicReviewBatch6DRegressionTests`、`AtomicReviewBatch6KCRegressionTests` | 明确错误→已修复 |

## 公共根因、扫描与保留边界

- 公开军团与阵营判定收敛为 `PublicLegions` / `PublicFactionLegions` + `L12StructuredCardRules.HasFaction`：隐匿盖牌不是军团，万物统御之戒在手牌、牌库、墓地、战场与额外区均以控制者阵营对通用卡生效。生产路径中的裸 `Faction ==/!= "gaotianyuan"` 已清零。
- 本多、天照与天诛接入公共 PendingActivation/Composite，公开目标与冒号前费用只在真实入栈前提交；结算期只消费不可变 `declared:*`。
- 公共延迟段改为每次只取一个 `DeferredEffectStack` 项并开放它自己的响应窗口；不再批量压栈后仅顶层可响应。
- 合法隐藏信息仍保持延迟：花魁的顶3只在对应段开始后私密查看；坂本龙马的手牌军团身份只对所有者可见。隐匿对手快照只显示卡背，天灾公开影响不因此被过滤。
- 全池扫描同步修正了 S2 中武田信玄、八尺琼勾玉、井伊直虎、冲田总司与天下布武对同一戒指阵营规则的裸分支；这些是跨阵营公共根因控制组，不扩张其他卡面语义。
