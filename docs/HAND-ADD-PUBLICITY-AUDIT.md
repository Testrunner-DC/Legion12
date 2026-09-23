# 有条件加入手牌公开性审计（批次 G）

审计日期：2026-09-24  
边界：只读核对 `AddCardToHandByEffect`、`MoveLibraryCardToHandByEffect` 及其公开包装器的全部调用点；本批不修改服务端规则或事件协议。

## 结论

- 牌库内按阵营、类型、费用等条件检索的主流程大多已使用 `PubliclyRevealThenAddCardToHandByEffect` 或 `PubliclyRevealThenMoveLibraryCardToHandByEffect`。
- 仍发现 17 组“有条件选取后直接加入手牌”的路径没有经过统一公开包装器。其中 3 组在更早阶段已经发过公开 `reveal`，卡牌身份不会实际保密，但出口仍不统一；其余路径依赖墓地公开信息或另发 `return/effect` 事件，不满足“有条件选取统一公开展示”的结构性要求。
- 以下均为后续规则一致性任务，不在批次 G 内修复。

## 待收口清单

| 卡牌/能力 | 约束 | 当前直接路径 | 备注 |
|---|---|---|---|
| 杜阿特之门 | 非自身、【太阳城】、可入手 | `L12S1FactionEffects.cs:276` → `L12GameEngine.EffectPresentations.cs:231` | 未走公开包装器 |
| 众神之乡 | 【太阳城】、可入手 | `L12S1FactionEffects.cs:1269` → `L12GameEngine.EffectPresentations.cs:231` | 未走公开包装器 |
| 英灵殿 | 【阿斯加德】、可入手 | `L12S1FactionEffects.cs:1336` → `L12GameEngine.EffectPresentations.cs:231` | 未走公开包装器 |
| 诸神巅 | 【奥林匹斯】、可入手 | `L12S2RemainingEffects.cs:411` → `L12GameEngine.EffectPresentations.cs:231` | 未走公开包装器 |
| 智慧法典 | 费用与类型条件 | `L12S1ExtendedEffects.cs:1281` → `L12GameEngine.EffectPresentations.cs:231` | 未走公开包装器 |
| 复仇血鹰 | 非自身、【阿斯加德】，墓地二选一去向 | `L12S1ExtendedEffects.cs:1173` → `L12GameEngine.EffectPresentations.cs:201` | 回手对象未走公开包装器 |
| 黄泉之门 | 【高天原】、可入手 | `L12S1FactionEffects.cs:1452` | 直接加入 |
| 彼界 阿瓦隆 | 墓地军团 1 张与战术 1 张 | `L12S2FactionEffects.cs:1659` | 两张均直接加入 |
| 十字军东征 | 仅【彼界】特征 | `L12S2FactionEffects.cs:2008` | 直接加入 |
| 珀尔修斯 | 固定〈珀尔修斯·晋升〉且需弃手 | `L12S2FactionEffects.cs:2268` | 另发 `effect`，但未走公开包装器 |
| 伊姆何泰普 | 【太阳城】、军团、费用至少 6 | `L12S2FactionEffects.cs:2602` → `L12S1ExtendedEffects.cs:873` | 另发 `return`，但未走公开包装器 |
| 伊姆何泰普/珀尔修斯登场触发 | 各自墓地目标条件 | `L12EnterPublicTriggerPlans.cs:778` | 共用直接加入分支 |
| 湖中仙女的馈赠（墓地） | 固定〈亚瑟王〉 | `L12TrialCompletionTriggerPlans.cs:237` | 直接加入 |
| 湖中仙女的馈赠（牌库） | 固定〈亚瑟王〉 | `L12TrialCompletionTriggerPlans.cs:331` | 私有选择后直接加入，是优先级最高的缺口 |
| 诸葛亮（圣物去向选择） | 展示的牌为圣物，玩家选择入手 | `L12S1ExtendedEffects.cs:536` | `L12MoraleReturns.cs:223` 已先公开，身份不泄密；出口仍应统一 |
| 阿麦金 | 已展示牌库顶且仅【彼界】时可入手 | `L12S2FactionEffects.cs:2205` | `L12S2FactionEffects.cs:1500` 已先公开；出口仍应统一 |
| 冲田总司 | 已展示的费用不高于 3 的【高天原】牌，或不符合免费打出条件 | `L12S2FactionEffects.cs:3012` | `L12S2FactionEffects.cs:2974` 已先公开；出口仍应统一 |

## 已核对为合规的代表路径

- 刘备、山河社稷图：`L12ActiveAbilities.cs:957,975`
- 花魁的馈赠：`L12EffectContinuations.cs:560`
- 通用墓地回手触发规格：`L12GraveToHandTriggerEffects.cs:91`
- 万物统御之戒：`L12S2UniversalEffects.cs:299`
- 特勒马科斯：`L12StarterRemainingEffects.cs:1542`
- 弗蕾迪斯：`L12StarterTargetedEffectPlans.cs:442`
- 寻找圣杯之旅：`L12TrialCompletionTriggerPlans.cs:348`
- 圆桌领域、八尺琼勾玉、荣耀之路、武运在天 铠甲在前等限定牌库检索：`L12S2FactionEffects.cs:2520,2540,2557,2724`

## 后续修复原则

1. 不新增并行事件协议；有条件回手统一改经公开包装器。
2. 已提前公开的三组只收敛出口，避免重复播放两次动画。
3. 墓地虽然是公开区域，也应使用统一包装器，确保日志、回放和卡面飞行动画同源。
4. 修复时逐条补服务器规则测试，并核对不会重复触发 `effect-hand-add` 权威事件。
