# 0827 后台与测试群 Bug 收口核对清单

本清单对应 2026-08-27 的“后台 24 条逐项结论”与测试群 Q1～Q13。按用户要求：后台第 23、24 条和测试群 Q13 本批略过；其余项目在验证通过后统一收口。

## 后台 24 条

| 编号 | 结论 | 状态 | 验证/规则证据 |
| --- | --- | --- | --- |
| 1 | 〈粮草掠夺〉可响应所有“因效果加入手牌” | 已修复 | `ShanheSearchPublishesTheSharedEffectHandAddAuthorityEvent`；统一 `AddCardToHandByEffect` 权威事件 |
| 2 | 〈神妙行军〉无第一段目标仍结算第二段 | 已修复 | `MarchSplitsItsIndependentParagraphsIntoSeparateStackItems`；两个独立堆叠项 |
| 3 | 嬴政冒号前弃牌在响应前支付 | 已修复 | `PendingActivation` 预声明/支付门禁；完整规则测试覆盖 |
| 4 | 军团或新反击战术可覆盖控制者自己的盖伏反击战术 | 已修复 | `LegionCanDisplaceItsControllersCoveredCounterTactic`；统一可替换位置查询 |
| 5 | 冲田总司进攻时效果稳定触发 | 已修复 | `OkitaAttackMayPlayEligibleTopGaotianyuanLegionForFree`、`OkitaAttackAddsIneligibleTopCardToHand`、`MercenaryResponseDoesNotSwallowOkitasAttackTrigger` |
| 6 | 奈芙蒂斯与伊姆何泰普减费叠加 | 已修复 | `NephthysAndImhotepDiscountsAccumulateInEitherOrder`；派生费用累加 |
| 7 | 落穴可响应图特摩斯三世，但效果对其不生效 | 已修复 | 结构化反击保护与入栈规则回归；区分“可发动”和“结算无效” |
| 8 | 隐匿服部半藏不计作军团，不阻止宫本武藏冲锋 | 已修复 | `MiyamotoEntryGainsChargeOnlyWhenNoOtherFrontLegionExists`；战场军团身份统一查询 |
| 9 | 陵墓构造体阵亡时/离场时为两个独立触发 | 已修复 | `TombConstructDeathCreatesIndependentDeathAndLeaveTriggers`；旧合并台账已废止 |
| 10 | 黄金圣甲虫可由同名黄金圣甲虫替换 | 已修复 | `GoldScarabBlocksOtherArtifactsButAllowsAnotherGoldScarab`；不同名圣物仍被禁止 |
| 11 | 加拉哈德第二效果为主动效果 | 已修复 | `GalahadCanPayItselfAfterTheGrailTrialToDrawAndHeal`；后台、快照与实战同源 |
| 12 | 黄金圣甲虫第二效果不休整、独立回合次数、兵力归零阵亡 | 已修复 | `GoldenScarabDebuffDoesNotRestTheArtifactUsesItsOwnTurnKeyAndKillsAtZero` |
| 13 | 鲍斯阵亡效果不被进攻响应链吞掉 | 已修复 | 进攻触发批次/响应上下文公共修复；完整规则测试覆盖 |
| 14 | 双方佣兵部队不会形成重复询问 | 已修复 | 正式响应优先权专项与 `MercenaryResponseDoesNotSwallowOkitasAttackTrigger` |
| 15 | 〈猎杀时刻〉墓地不足 4 张不可打出 | 规则正确 | `HuntingMomentCannotBePlayedBeforeItsFourCardGraveyardCostIsLegal`；不足时灰置且不支付 |
| 16 | 天灾检查等待当前堆叠和衍生触发全部关闭 | 已修复 | `DisasterWaitsForRoundTableEntryTriggerToFullyClose` |
| 17 | 图特摩斯三世进攻时效果不被响应链吞掉 | 已修复 | 与冲田/鲍斯共用进攻触发批次和响应恢复入口 |
| 18 | 傲慢之罪附加费用覆盖主宰效果且不重复返还士气 | 已修复 | `PrideDisasterAddsOneMoraleToMasterEffectCost`、`PrideMasterEffectStagesReturnAndPaymentWithoutDoubleCharging` |
| 19 | 〈增殖的甲虫〉不计入主牌库数量 | 已修复 | 特殊起始卡/构筑验证同源；完整规则测试覆盖 |
| 20 | 效果界面可最小化且最小化后只保留展开条 | 已修复 | UI 契约锁定 `minimized` 分支隐藏原弹框与浮动详情 |
| 21 | 落穴不能响应圣物登场 | 已修复 | `PitfallCannotRespondToAnArtifactEnterEffect` |
| 22 | 手机沙盒可代行对手结束回合及双方选择 | 已修复 | `SandboxControllerCanIssueNormalRulesCommandsForEitherPlayerOnlyInSandbox`；UI 契约锁定双方代操作 |
| 23 | 鼠标移入卡牌统一自动放大 | 略过 | 按用户要求不纳入本批 |
| 24 | 账号与昵称分离 | 略过 | 按用户要求不纳入本批 |

## 测试群 Q1～Q13

| 编号 | 结论 | 状态 | 验证/规则证据 |
| --- | --- | --- | --- |
| Q1 | 〈暴怒之罪〉对双方主宰造成非致命伤害，天灾为中立来源 | 已修复 | 天灾中立来源与梅杰德隔离回归；完整规则测试覆盖 |
| Q2 | 〈雷神之锤〉可从墓地发动 | 已修复 | `ThorHammerGraveyardAbilityPredeclaresOrderedCostAndSlotBeforeReviving` |
| Q3 | 雷神之锤从墓地登场的回合可进攻主宰 | 已修复 | 同一端到端回归锁定登场权限 |
| Q4 | 寻找圣杯之旅、符文获得与试炼发动分离 | 已修复 | `CompletedGrailTrialOffersOneRuneWhenRoundTableLegionEnters`、符文/试炼专项 |
| Q5 | 奥拉夫二世获得强攻后对主宰造成 2 点伤害 | 已修复 | `StrongAttackGrantedDuringAttackUpdatesTheCurrentMasterDamageSnapshot` |
| Q6 | 戏法师的傀儡在提交前可取消 | 已修复 | `MagiciansPuppetMayCancelBeforeCommittingAndReturnsToTheSameResponseWindow` |
| Q7 | 贯穿 1000 被傀儡改为 1000 目标后双方阵亡 | 已修复 | `PiercingRetargetedByMagiciansPuppetUsesTheRealRemainingTroopsAndBothLegionsDie` |
| Q8 | 普罗米修斯真实主宰入口可发动且只消耗神力 | 已修复 | `PrometheusMasterSnapshotAndRealMasterActivationConsumeButDoNotFlipGodPower` |
| Q9 | 符文费用直接点击场面圆形符文支付 | 已修复 | `GawainConsumesTheRunesClickedOnTheBoard`；UI 契约禁止恢复编号弹框 |
| Q10 | 沙盒对手进攻主宰时可由单人操作者选择不抵挡/不支援 | 已修复 | 沙盒双方代操作规则与 UI 契约 |
| Q11 | 沙盒上方玩家手牌操作按钮不被裁切 | 已修复 | `.opponent-hand .hand-actions` 向棋盘中心展开的 UI 契约 |
| Q12 | 响应窗口显示来源、当前效果与响应事件 | 已修复 | 响应 Prompt 数据与 UI 契约；正式响应专项 |
| Q13 | 沙盒手牌排序 | 略过 | 按用户要求不纳入本批 |

## 公共根因收口

1. 进攻顺序统一为“宣言与费用 → 进攻时触发 → 响应 → 支援/抵挡 → 战斗”，响应结束恢复原触发上下文。
2. 冒号前费用统一走 `PendingActivation`：完整预声明、校验、支付后才入栈；取消发生在公开信息与支付之前。
3. 抽牌、展示后加入、检索、墓地回手统一发布效果加入手牌权威事件。
4. 费用、兵力、关键词、职介与目标限制统一读取派生规则，不由 UI 或单卡卡号重新推断。
5. 隐匿、衍生卡、圣物限制、特殊起始卡统一读取卡牌身份与区域规则。
6. 响应窗口资格只读取公开卡池与公开状态；真实选项只列实际合法实例，双方连续让过立即按 LIFO 结算。

## 发布门禁

- 全量 `TwelveLegions.Tests` 通过。
- 平台持久化发布门禁通过（8/8）。额外运行 GrandUMI 全套 171 项时有 10 项 One Piece/EB/OP16 既有失败，不属于本批十二军团规则范围，也不作为 L12 发布门禁伪装为通过。
- UI 契约与前端生产构建通过。
- 原子化旧卡号分支保持 0，不允许重新增加卡号 `case` 或 `EffectText.Contains` 推断。
- `git diff --check` 通过；确认远端 `main` 后一次性提交、推送与部署。
