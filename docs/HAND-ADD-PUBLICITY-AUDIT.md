# 有条件加入手牌公开性审计（批次 G）

审计日期：2026-09-24  
边界：核对并收口 `AddCardToHandByEffect`、`MoveLibraryCardToHandByEffect` 及其公开包装器的全部调用点；不新增并行事件协议。

## 结论

- 牌库或墓地内按阵营、类型、费用等条件检索后入手的路径统一使用公开包装器。
- 原发现的17组路径已全部收口：14组经`PubliclyRevealThenAddCardToHandByEffect`公开后入手；诸葛亮、阿麦金、冲田总司3组因上一步已经公开，改经`AddPreviouslyRevealedCardToHandByEffect`，不会重复播放卡面动画。
- 静态守卫扫描所有底层`AddCardToHandByEffect`调用；除核心实现与两个经过审计的场上回手路径外，不允许有条件检索绕过公开出口。

## 已收口清单

| 卡牌/能力 | 约束 | 原直接路径 | 收口结果 |
|---|---|---|---|
| 杜阿特之门 | 非自身、【太阳城】、可入手 | `L12S1FactionEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 公开后入手 |
| 众神之乡 | 【太阳城】、可入手 | `L12S1FactionEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 公开后入手 |
| 英灵殿 | 【阿斯加德】、可入手 | `L12S1FactionEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 公开后入手 |
| 诸神巅 | 【奥林匹斯】、可入手 | `L12S2RemainingEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 公开后入手 |
| 智慧法典 | 费用与类型条件 | `L12S1ExtendedEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 公开后入手 |
| 复仇血鹰 | 非自身、【阿斯加德】，墓地二选一去向 | `L12S1ExtendedEffects.cs` → `L12GameEngine.EffectPresentations.cs` | 回手对象公开后入手 |
| 黄泉之门 | 【高天原】、可入手 | `L12S1FactionEffects.cs` | 公开后入手 |
| 彼界 阿瓦隆 | 墓地军团 1 张与战术 1 张 | `L12S2FactionEffects.cs` | 两张分别公开后入手 |
| 十字军东征 | 仅【彼界】特征 | `L12S2FactionEffects.cs` | 公开后入手 |
| 珀尔修斯 | 固定〈珀尔修斯·晋升〉且需弃手 | `L12S2FactionEffects.cs` | 公开后入手 |
| 伊姆何泰普 | 【太阳城】、军团、费用至少 6 | `L12S2FactionEffects.cs` → `L12S1ExtendedEffects.cs` | 公开后入手 |
| 伊姆何泰普/珀尔修斯登场触发 | 各自墓地目标条件 | `L12EnterPublicTriggerPlans.cs` | 共用公开后入手出口 |
| 湖中仙女的馈赠（墓地） | 固定〈亚瑟王〉 | `L12TrialCompletionTriggerPlans.cs` | 公开后入手 |
| 湖中仙女的馈赠（牌库） | 固定〈亚瑟王〉 | `L12TrialCompletionTriggerPlans.cs` | 私有选择后公开再入手 |
| 诸葛亮（圣物去向选择） | 展示的牌为圣物，玩家选择入手 | `L12S1ExtendedEffects.cs` | 已公开卡直接入手，不重复展示 |
| 阿麦金 | 已展示牌库顶且仅【彼界】时可入手 | `L12S2FactionEffects.cs` | 已公开卡直接入手，不重复展示 |
| 冲田总司 | 已展示的费用不高于 3 的【高天原】牌，或不符合免费打出条件 | `L12S2FactionEffects.cs` | 已公开卡直接入手，不重复展示 |

## 已核对为合规的代表路径

- 刘备、山河社稷图：`L12ActiveAbilities.cs:957,975`
- 花魁的馈赠：`L12EffectContinuations.cs:560`
- 通用墓地回手触发规格：`L12GraveToHandTriggerEffects.cs:91`
- 万物统御之戒：`L12S2UniversalEffects.cs:299`
- 特勒马科斯：`L12StarterRemainingEffects.cs:1542`
- 弗蕾迪斯：`L12StarterTargetedEffectPlans.cs:442`
- 寻找圣杯之旅：`L12TrialCompletionTriggerPlans.cs:348`
- 圆桌领域、八尺琼勾玉、荣耀之路、武运在天 铠甲在前等限定牌库检索：`L12S2FactionEffects.cs:2520,2540,2557,2724`

## 持续约束

1. 不新增并行事件协议；有条件回手统一改经公开包装器。
2. 已提前公开的三组只收敛出口，避免重复播放两次动画。
3. 墓地虽然是公开区域，也应使用统一包装器，确保日志、回放和卡面飞行动画同源。
4. `ConditionalHandAddPublicityGuardTests`持续禁止新路径绕过公开出口，并核对不会重复触发 `effect-hand-add` 权威事件。
