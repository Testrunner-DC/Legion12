# 生命周期完成矩阵（自动基线）

由 `scripts/export-l12-effect-lifecycle-inventory.ps1` 随台账同次生成；不要手工修改此表。
只认精确能力 ID 绑定的具名证据 scope（完全匹配或 `矩阵项-子项` 前缀形式）；不适用必须给出理由；归属、运行入口与测试证据分别展示。
能力段分母：681（含未归属段；档案外段不构成完成证据）。档案：76，已完成：0，未完成：76。
内容指纹：`4f0460941758e431a1ed44832da8d62f589647878c9f3a95ab84ef2229be92ae`。

| 定义证据分桶 | 能力数 |
| --- | ---: |
| composite-definition | 195 |
| fine-definition | 84 |
| owner-unreviewed | 1 |
| shared-rule-owner | 401 |

## 矩阵项

- `normal`：正常结算
- `no-target`：无目标/不能发动
- `negated`：已支付后被无效
- `target-invalidated`：已声明对象逆结算失效
- `duplicate-submit`：重复或过期提交
- `reconnect`：Prompt/堆叠/选择阶段重连
- `payment-cancel`：有费用时的取消/支付失败兜底
- `single-candidate-choice`：有对象选择时的唯一候选仍选择
- `multi-target-applicability`：多目标协议的部分失效继续
- `presentation-consumers`：展示消费者覆盖（按钮/弹框/动效/日志/战报/回放）

## active:paid-extended-range（未完成）

绑定能力段：2。运行入口：activation-eligibility = L12GameEngine.ExtendedRangeSourceUnavailableReason；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；cost-commit = L12GameEngine.TryCommitS1ExtendedActiveAbility；definition = L12StructuredCardSemantics.ExtendedRangeRule；expiry = L12GameEngine.ResetTemporaryCardState；response-stack = L12GameEngine.PushEffect；settlement = L12GameEngine.TryResolveS1ExtendedActive。
档案附加检查：source-invalidated, payment-cancel, paid-cost-preserved, repeat-activation, turn-end-expiry, authoritative-attack。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 2（S01-0003:ability:active:73c59f9367069790、S01-0113:ability:active:e1b5cdab435b4c1f） | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 2 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0003:ability:active:73c59f9367069790、S01-0113:ability:active:e1b5cdab435b4c1f） | 0 |

缺失明细与建议补测范围：
- `duplicate-submit`（重复或过期提交）：S01-0003:ability:active:73c59f9367069790、S01-0113:ability:active:e1b5cdab435b4c1f
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0003:ability:active:73c59f9367069790、S01-0113:ability:active:e1b5cdab435b4c1f

展示消费者出口：档案未声明展示边界。

## after-kill:printed-piercing（未完成）

绑定能力段：2。运行入口：generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：original-combat-kill-only, no-attack-trigger-on-generated, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595
- `target-invalidated`（已声明对象逆结算失效）：S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0606:ability:after-kill:7680beaaf4313595、S02-0611:ability:after-kill:7680beaaf4313595

展示消费者出口：档案未声明展示边界。

## attack:troops-set-on-attack（未完成）

绑定能力段：2。运行入口：attack-settlement = L12GameEngine.Attack；definition = L12StructuredCardRules.CombatProfile；post-attack-revert = L12GameEngine.RevertPendingCombatTroopsModifiers。
档案附加检查：row-condition-current, set-value-parameter, post-attack-revert, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0409:ability:attack:c900a6435336564c、S02-0507:ability:attack:d20040947938d125

展示消费者出口：档案未声明展示边界。

## composite:counter-deployment（未完成）

绑定能力段：2。运行入口：candidate-generation = L12GameEngine.IsCounterDeploymentCandidate；settlement-revalidation = L12GameEngine.SetDeclaredCounterTactics；slot-declaration = L12GameEngine.CreateActivationStepPrompt。
档案附加检查：private-hand-redaction, independent-target-settlement, slot-invalidated。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 1 | 1（S01-0403:ability:death:c3e5fc27d01fe269） | 0 |
| negated | 1 | 1（S01-0403:ability:death:c3e5fc27d01fe269） | 0 |
| target-invalidated | 2 | 0 | 0 |
| duplicate-submit | 1 | 1（S01-0403:ability:death:c3e5fc27d01fe269） | 0 |
| reconnect | 1 | 1（S01-0403:ability:death:c3e5fc27d01fe269） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0403:ability:death:c3e5fc27d01fe269、S02-0009:ability:play:ff53cfd909161da1） | 0 |

缺失明细与建议补测范围：
- `no-target`（无目标/不能发动）：S01-0403:ability:death:c3e5fc27d01fe269
- `negated`（已支付后被无效）：S01-0403:ability:death:c3e5fc27d01fe269
- `duplicate-submit`（重复或过期提交）：S01-0403:ability:death:c3e5fc27d01fe269
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0403:ability:death:c3e5fc27d01fe269
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0403:ability:death:c3e5fc27d01fe269、S02-0009:ability:play:ff53cfd909161da1

展示消费者出口：档案未声明展示边界。

## composite:desert-hand-summon（未完成）

绑定能力段：1。运行入口：candidate-generation = L12GameEngine.IsDesertHandSummonCandidate；cost-commit = L12GameEngine.TryCommitCompositePreStackCosts；settlement-revalidation = L12GameEngine.TryResolveS2FactionTactic。
档案附加检查：cost-prepaid, settlement-slot-invalidated, single-candidate-choice。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 1 | 0 | 0 |
| target-invalidated | 1 | 0 | 0 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 1 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0207:ability:play:528a4430c4b87fb5） | 0 |

缺失明细与建议补测范围：
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0207:ability:play:528a4430c4b87fb5

展示消费者出口：档案未声明展示边界。

## continuous:cooperative-support（未完成）

绑定能力段：1。运行入口：definition = L12StructuredCardRules.HasCooperativeSupport；support-candidates = L12GameEngine.HasLegalLegionSupport；support-validation = L12GameEngine.ValidateDefenseChoice。
档案附加检查：row-condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（ST04-07:ability:continuous:8f395636980d57ca） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（ST04-07:ability:continuous:8f395636980d57ca） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（ST04-07:ability:continuous:8f395636980d57ca） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：ST04-07:ability:continuous:8f395636980d57ca
- `reconnect`（Prompt/堆叠/选择阶段重连）：ST04-07:ability:continuous:8f395636980d57ca
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：ST04-07:ability:continuous:8f395636980d57ca

展示消费者出口：档案未声明展示边界。

## continuous:duel-combat-line（未完成）

绑定能力段：1。运行入口：attack = L12GameEngine.Attack；attack-revalidation = L12GameEngine.TryValidateAttackTarget；combat-settlement = L12GameEngine.ResolveDefenseCore；condition-and-active-state = L12StructuredCardRules.CombatProfile；disaster-target = L12GameEngine.HasMandatoryDisasterLegionTarget。
档案附加检查：structured-split-siblings, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0101:ability:static:b5c9e323c0a061cc） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S01-0101:ability:static:b5c9e323c0a061cc） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0101:ability:static:b5c9e323c0a061cc） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0101:ability:static:b5c9e323c0a061cc
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0101:ability:static:b5c9e323c0a061cc
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0101:ability:static:b5c9e323c0a061cc

展示消费者出口：档案未声明展示边界。

## continuous:field-morale-resource（未完成）

绑定能力段：1。运行入口：automatic-payment = L12GameEngine.TryConsumeMorale；candidate-generation = L12GameEngine.SpendableFieldMoraleResources；composite-reservation = L12GameEngine.CompositeOrdinaryPaymentChoices；definition = L12StructuredCardSemantics.FieldMoraleResourceRule；effect-payment-retry = L12GameEngine.ContinueEffectMoralePayment；manual-payment = L12GameEngine.CreateResourcePaymentPrompt；paid-cost-presentation = L12GameEngine.AddPaidCostPresentationFromSnapshot；rejected-submit-rollback = L12GameEngine.RestoreActiveResourceRollback；selected-payment-commit = L12GameEngine.TryConsumeSelectedResources；selected-payment-revalidation = L12GameEngine.CanConsumeSelectedResources；snapshot-count = L12GameEngine.ActiveResourceCount；snapshot-projection = L12GameEngine.SnapshotField。
档案附加检查：exact-card-family, controller-turn, opponent-turn, front-row, back-row, current-controller, active-only, hidden-or-non-legion, mixed-payment, reservation, effect-payment-cancel, stale-effect-payment-retry, rejected-active-rollback, paid-cost-presentation, stale-resource, duplicate-submit, reconnect-derived-state, authoritative-snapshot-projection。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0212:ability:static:025749085872cdff） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 1（S01-0212:ability:static:025749085872cdff） | 0 |
| reconnect | 0 | 1（S01-0212:ability:static:025749085872cdff） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0212:ability:static:025749085872cdff） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0212:ability:static:025749085872cdff
- `duplicate-submit`（重复或过期提交）：S01-0212:ability:static:025749085872cdff
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0212:ability:static:025749085872cdff
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0212:ability:static:025749085872cdff

展示消费者出口：AddPaidCostPresentationFromSnapshot。

## continuous:front-row-keyword-grant（未完成）

绑定能力段：1。运行入口：active-state = L12StructuredCardRules.HasTaunt；condition = L12StructuredCardRules.ConditionMatches；definition = L12StructuredCardRules.GetCombatRuleAbilities；grant-chain = L12StructuredCardRules.AbilityGrantsKeyword；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：row-condition-current, ability-ref-chain, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0512:ability:static:e44e97f2fb745816） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 1（S02-0512:ability:static:e44e97f2fb745816） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0512:ability:static:e44e97f2fb745816） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0512:ability:static:e44e97f2fb745816） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0512:ability:static:e44e97f2fb745816
- `target-invalidated`（已声明对象逆结算失效）：S02-0512:ability:static:e44e97f2fb745816
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0512:ability:static:e44e97f2fb745816
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0512:ability:static:e44e97f2fb745816

展示消费者出口：BuildActiveKeywords。

## continuous:front-row-keyword-troops（未完成）

绑定能力段：6。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；condition-and-active-state = L12StructuredCardRules.ConditionMatches；definition = L12StructuredCardRules.GetCombatRuleAbilities；keyword-grant-chain = L12StructuredCardRules.HasTaunt；presentation = L12GameEngine.BuildActiveKeywords；troops-bonus-definition = L12StructuredCardRules.OpponentTurnFrontTroopsBonus；troops-consumer = L12GameEngine.RecalculateContinuousTroops。
档案附加检查：row-condition-current, opponent-turn-troops, ability-ref-chain, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 6（S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1） | 0 |
| no-target | 0 | 0 | 6 |
| negated | 0 | 0 | 6 |
| target-invalidated | 0 | 0 | 6 |
| duplicate-submit | 0 | 0 | 6 |
| reconnect | 0 | 6（S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1） | 0 |
| payment-cancel | 0 | 0 | 6 |
| single-candidate-choice | 0 | 0 | 6 |
| multi-target-applicability | 0 | 0 | 6 |
| presentation-consumers | 0 | 6（S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0004:ability:continuous:16dc08d7324d1649、S02-0007:ability:continuous:58ce6286f39b73ee、S02-0615:ability:continuous:16dc08d7324d1649、ST02-02:ability:continuous:c51a646e6338a9d1、ST04-01:ability:continuous:59dd263106457575、ST06-02:ability:continuous:c51a646e6338a9d1

展示消费者出口：BuildActiveKeywords。

## continuous:front-row-taunt-overlay（未完成）

绑定能力段：4。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；condition-and-active-state = L12StructuredCardRules.HasTaunt；definition = L12StructuredCardRules.GetCombatOverlayAbilities；master-attack-rule = L12GameEngine.CanAttackMasterTarget；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：row-condition-current, authoritative-consumer, closed-overlay-card-set。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 4（S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138） | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 0 | 4 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 0 | 4（S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138） | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 0 | 4（S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0107:ability:static:af427a4637e1c138、S01-0204:ability:static:af427a4637e1c138、S01-0312:ability:static:af427a4637e1c138、ST01-04:ability:static:af427a4637e1c138

展示消费者出口：BuildActiveKeywords。

## continuous:kings-sword-attached（未完成）

绑定能力段：1。运行入口：strong-attack = L12StructuredCardSemantics.GrantsStrongAttackWhileAttached；troops = L12GameEngine.GetTurnAndPositionContinuousTroops。
档案附加检查：attached-source-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-06S2:ability:static:0f86ac377c8c63ee） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-06S2:ability:static:0f86ac377c8c63ee） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-06S2:ability:static:0f86ac377c8c63ee） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-06S2:ability:static:0f86ac377c8c63ee
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-06S2:ability:static:0f86ac377c8c63ee
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-06S2:ability:static:0f86ac377c8c63ee

展示消费者出口：档案未声明展示边界。

## continuous:opponent-turn-field-rule（未完成）

绑定能力段：1。运行入口：authoritative-recalculation = L12GameEngine.RecalculateContinuousTroops；cost-derivation = L12StructuredCardRules.OpponentTurnCostModifier；definition = L12StructuredCardSemantics.OpponentTurnFieldRule；front-troops-derivation = L12StructuredCardRules.OpponentTurnFrontTroopsBonus。
档案附加检查：exact-card-family, opponent-turn, controller-turn, front-row, back-row, current-controller, cost-and-troops-same-definition, leave-reset, reconnect-idempotence。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0212:ability:static:2f33fb3652e7bd28） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 1（S01-0212:ability:static:2f33fb3652e7bd28） | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0212:ability:static:2f33fb3652e7bd28） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0212:ability:static:2f33fb3652e7bd28
- `duplicate-submit`（重复或过期提交）：S01-0212:ability:static:2f33fb3652e7bd28
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0212:ability:static:2f33fb3652e7bd28

展示消费者出口：档案未声明展示边界。

## continuous:out-of-deck-graveyard-lifecycle（未完成）

绑定能力段：2。运行入口：authoritative-departure = L12GameEngine.MoveFieldCardToZone；deck-size-rule = L12SpecialDeckRules.DoesNotCountTowardMainDeck；definition = L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle；departure-replacement = L12SpecialDeckRules.AlwaysReturnsToOwnerGraveyard；hand-library-replacement = L12SpecialDeckRules.CannotEnterHandOrLibrary；opening-zone-rule = L12SpecialDeckRules.StartsInGraveyard。
档案附加检查：exact-card-family, text-independent, deck-count, opening-graveyard, hand-filter, library-filter, owner-graveyard, all-departure-destinations, derived-card-precedence, controller-owner-split, reconnect-authoritative-zone。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 2（S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c） | 0 |
| reconnect | 0 | 2（S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c
- `duplicate-submit`（重复或过期提交）：S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0212:ability:static:6d8b57888db9839b、S02-0201:ability:continuous:16b90b36ef8afe2c

展示消费者出口：档案未声明展示边界。

## continuous:printed-range（未完成）

绑定能力段：47。运行入口：candidate-generation = L12GameEngine.BuildLegalAttackTargets；combat-declaration = L12GameEngine.Attack；condition-and-permission = L12StructuredCardRules.CombatProfile；damage-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.GetCombatRuleAbilities；source-row = L12GameEngine.CanAttackFromRow；target-revalidation = L12GameEngine.TryValidateAttackTarget。
档案附加检查：source-row-change, attack-preview, ranged-no-loss, profession-grant。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 43（S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59） | 0 |
| no-target | 47 | 0 | 0 |
| negated | 0 | 0 | 47 |
| target-invalidated | 0 | 47（S01-0003:ability:static:e3471cd2a7042e59、S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0115:ability:static:9ba2f4f5354a2a05、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0409:ability:static:6c03e83e9e18abb1、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0507:ability:static:3f520b391281b325、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59） | 0 |
| duplicate-submit | 0 | 0 | 47 |
| reconnect | 47 | 0 | 0 |
| payment-cancel | 0 | 0 | 47 |
| single-candidate-choice | 0 | 0 | 47 |
| multi-target-applicability | 0 | 0 | 47 |
| presentation-consumers | 0 | 47（S01-0003:ability:static:e3471cd2a7042e59、S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0115:ability:static:9ba2f4f5354a2a05、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0409:ability:static:6c03e83e9e18abb1、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0507:ability:static:3f520b391281b325、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59
- `target-invalidated`（已声明对象逆结算失效）：S01-0003:ability:static:e3471cd2a7042e59、S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0115:ability:static:9ba2f4f5354a2a05、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0409:ability:static:6c03e83e9e18abb1、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0507:ability:static:3f520b391281b325、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0003:ability:static:e3471cd2a7042e59、S01-0110:ability:static:e3471cd2a7042e59、S01-0111:ability:static:e3471cd2a7042e59、S01-0112:ability:static:e3471cd2a7042e59、S01-0113:ability:static:e3471cd2a7042e59、S01-0114:ability:static:e3471cd2a7042e59、S01-0115:ability:static:9ba2f4f5354a2a05、S01-0116:ability:static:e3471cd2a7042e59、S01-0208:ability:static:e3471cd2a7042e59、S01-0209:ability:static:e3471cd2a7042e59、S01-0210:ability:static:e3471cd2a7042e59、S01-0211:ability:static:e3471cd2a7042e59、S01-0213:ability:static:9ba2f4f5354a2a05、S01-0214:ability:static:e3471cd2a7042e59、S01-0309:ability:static:e3471cd2a7042e59、S01-0313:ability:static:e3471cd2a7042e59、S01-0314:ability:static:e3471cd2a7042e59、S01-0316:ability:static:9ba2f4f5354a2a05、S01-0409:ability:static:6c03e83e9e18abb1、S01-0410:ability:static:e3471cd2a7042e59、S01-0411:ability:static:e3471cd2a7042e59、S01-0413:ability:static:e3471cd2a7042e59、S01-0415:ability:static:9ba2f4f5354a2a05、S01-0416:ability:static:e3471cd2a7042e59、S02-0003:ability:continuous:e9823ffd970d6ce6、S02-0204:ability:continuous:e9823ffd970d6ce6、S02-0304:ability:continuous:e9823ffd970d6ce6、S02-0507:ability:static:3f520b391281b325、S02-0508:ability:static:aa41bff900061e1d、S02-0513:ability:static:e3471cd2a7042e59、S02-0514:ability:static:e3471cd2a7042e59、S02-0515:ability:static:e3471cd2a7042e59、S02-0517:ability:static:9ba2f4f5354a2a05、S02-0614:ability:continuous:e9823ffd970d6ce6、S02-0617:ability:continuous:e9823ffd970d6ce6、S02-0618:ability:continuous:e9823ffd970d6ce6、S02-0619:ability:continuous:f0839056592c5ee2、ST01-07:ability:static:e3471cd2a7042e59、ST01-08:ability:static:9ba2f4f5354a2a05、ST01-09:ability:static:e3471cd2a7042e59、ST02-08:ability:static:e3471cd2a7042e59、ST03-05:ability:static:efd7771da618f0ac、ST04-07:ability:static:e3471cd2a7042e59、ST05-03:ability:continuous:3119db9911c31cf3、ST05-04:ability:static:e3471cd2a7042e59、ST05-08:ability:static:e3471cd2a7042e59、ST05-09:ability:static:e3471cd2a7042e59

展示消费者出口：档案未声明展示边界。

## continuous:ramses-protection-and-entry-cost（未完成）

绑定能力段：1。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；current-round-condition = L12StructuredCardRules.HasSummonTurnCounterTacticProtection；definition = L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection；delegated-entry-inheritance = L12GameEngine.ResolveBatch6JAEnterEffect；entry-cost-calculation = L12GameEngine.PrintedEntryCostModifier；entry-cost-definition = L12StructuredCardSemantics.PrintedEntryCostRule；legacy-delegated-entry-inheritance = L12GameEngine.TryContinueS1Faction；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice；response-candidate-and-submit = L12GameEngine.IsProtectedFromCounterTactics。
档案附加检查：summon-round, four-response-types, delegated-entry, expiry, anonymous-availability, reconnect-derived-state, entry-cost-condition-false, entry-cost-display-and-payment-parity, entry-cost-zero-floor。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0202:ability:static:76a4a87caae11a73） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 1（S01-0202:ability:static:76a4a87caae11a73） | 0 |
| reconnect | 0 | 1（S01-0202:ability:static:76a4a87caae11a73） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0202:ability:static:76a4a87caae11a73） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0202:ability:static:76a4a87caae11a73
- `duplicate-submit`（重复或过期提交）：S01-0202:ability:static:76a4a87caae11a73
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0202:ability:static:76a4a87caae11a73
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0202:ability:static:76a4a87caae11a73

展示消费者出口：档案未声明展示边界。

## continuous:relic-zone-limit-exempt（未完成）

绑定能力段：5。运行入口：artifact-zone-placement = L12GameEngine.PlaceArtifactInRelicZone；definition = L12StructuredCardSemantics.IgnoresRelicZoneLimit。
档案附加检查：exact-card-family, artifact-only, primary-empty, primary-occupied, ordinary-artifact-replaces, hand-play, effect-generated-play, zhuge-generated-play, gm-play, reconnect-zone-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 5（S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134） | 0 |
| no-target | 0 | 0 | 5 |
| negated | 0 | 0 | 5 |
| target-invalidated | 0 | 0 | 5 |
| duplicate-submit | 0 | 5（S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134） | 0 |
| reconnect | 0 | 5（S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134） | 0 |
| payment-cancel | 0 | 0 | 5 |
| single-candidate-choice | 0 | 0 | 5 |
| multi-target-applicability | 0 | 0 | 5 |
| presentation-consumers | 0 | 5（S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134
- `duplicate-submit`（重复或过期提交）：S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0216:ability:static:bf632dc8776cd134、S01-0217:ability:static:bf632dc8776cd134、S01-0218:ability:static:bf632dc8776cd134、S01-0219:ability:static:bf632dc8776cd134、S01-0220:ability:static:bf632dc8776cd134

展示消费者出口：档案未声明展示边界。

## continuous:rested-free-front-back-move（未完成）

绑定能力段：1。运行入口：move-command = L12GameEngine.Move。
档案附加检查：source-rested-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0510:ability:static:5193793609facf70） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0510:ability:static:5193793609facf70） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0510:ability:static:5193793609facf70） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0510:ability:static:5193793609facf70
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0510:ability:static:5193793609facf70
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0510:ability:static:5193793609facf70

展示消费者出口：档案未声明展示边界。

## continuous:simple-troops-rule（未完成）

绑定能力段：4。运行入口：continuous-recalc = L12GameEngine.RecalculateContinuousTroops；turn-and-position-bonus = L12GameEngine.GetTurnAndPositionContinuousTroops。
档案附加检查：shared-recalc-outlet, condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 4（S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606） | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 0 | 4 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 0 | 4（S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606） | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 0 | 4（S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0203:ability:static:0a317a499dc4420e、S02-0516:ability:static:a29458736f52d0a9、S02-0519:ability:static:2b21805b14115304、S02-0523:ability:static:05da64c53e8a7606

展示消费者出口：档案未声明展示边界。

## continuous:structured-combat-rule（未完成）

绑定能力段：16。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；combat-settlement = L12GameEngine.ResolveDefenseCore；condition-and-active-state = L12StructuredCardRules.CombatProfile；definition = L12StructuredCardRules.GetCombatRuleAbilities；master-protection = L12StructuredCardRules.ProtectsMasterFromTroops；support-source-revalidation = L12StructuredCardRules.CannotSupport；support-target-revalidation = L12StructuredCardRules.CannotReceiveBackRowSupport；trial-protection = L12StructuredCardRules.ProtectsActiveTrialLegions。
档案附加检查：row-and-ready-condition, source-current-type, candidate-and-submit-parity, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 16（S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f） | 0 |
| no-target | 0 | 16（S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f） | 0 |
| negated | 0 | 0 | 16 |
| target-invalidated | 0 | 16（S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f） | 0 |
| duplicate-submit | 0 | 0 | 16 |
| reconnect | 0 | 16（S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f） | 0 |
| payment-cancel | 0 | 0 | 16 |
| single-candidate-choice | 0 | 0 | 16 |
| multi-target-applicability | 0 | 0 | 16 |
| presentation-consumers | 0 | 16（S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f
- `no-target`（无目标/不能发动）：S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f
- `target-invalidated`（已声明对象逆结算失效）：S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0004:ability:static:1644ef88125b05c1、S01-0101:ability:static:1041797d91099ae1、S01-0101:ability:static:1f027ad861ea0006、S02-0002:ability:continuous:5643b9f0c6e298e6、S02-0005:ability:continuous:0663e3d5b31edc67、S02-0007:ability:continuous:602cafbbc29faa3f、S02-0101:ability:continuous:4cd3104ae17d316d、S02-0201:ability:continuous:39b0b1524eaed536、S02-02M1:ability:continuous:a83e1e0971bbe6f0、S02-0302:ability:continuous:48719a94741bbf36、S02-0503:ability:static:5e2fcb0f2798f57a、S02-0504:ability:static:0ada28f438439ac2、S02-0516:ability:static:17774ead9eb8ed69、S02-0603:ability:continuous:5e0d666ac6a386ba、S02-0609:ability:continuous:dc2aa603cc3d136c、S02-0616:ability:continuous:5afe2828d587391f

展示消费者出口：档案未声明展示边界。

## continuous:summon-turn-counter-protection（未完成）

绑定能力段：2。运行入口：current-round-condition = L12StructuredCardRules.HasSummonTurnCounterTacticProtection；definition = L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection；delegated-entry-inheritance = L12GameEngine.ResolveBatch6JAEnterEffect；legacy-delegated-entry-inheritance = L12GameEngine.TryContinueS1Faction；response-candidate-and-submit = L12GameEngine.IsProtectedFromCounterTactics。
档案附加检查：summon-round, four-response-types, delegated-entry, expiry, anonymous-availability, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 2（S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94） | 0 |
| reconnect | 0 | 2（S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94
- `duplicate-submit`（重复或过期提交）：S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0201:ability:static:7d31de8999ce168a、ST02-01:ability:continuous:42ada4e462a2fb94

展示消费者出口：档案未声明展示边界。

## cost:active-rest（未完成）

绑定能力段：27。运行入口：button-eligibility = L12GameEngine.BuildAbilityViews；cost-commit = L12GameEngine.CommitStructuredActiveRestCost；cost-presentation = L12GameEngine.AddActivePaidCostPresentation；response-stack = L12GameEngine.PushEffect；runtime-identity = L12StructuredCardRules.IsActiveRestAbility。
档案附加检查：active-rest-cost, paid-cost-preserved, readied-source-reuse, runtime-branch-mapping。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| no-target | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| negated | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| target-invalidated | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| duplicate-submit | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| reconnect | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| payment-cancel | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |
| single-candidate-choice | 0 | 11（S01-0105:ability:active:0e81cd47a6221fd8、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-04D1:ability:active:67457fb394219836、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0603:ability:active:8768d3f1fcb44728、S02-06D1:ability:active:30a9d18991dc8481、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 16 |
| multi-target-applicability | 0 | 0 | 27 |
| presentation-consumers | 0 | 27（S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `no-target`（无目标/不能发动）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `negated`（已支付后被无效）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `target-invalidated`（已声明对象逆结算失效）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `duplicate-submit`（重复或过期提交）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-0105:ability:active:0e81cd47a6221fd8、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-04D1:ability:active:67457fb394219836、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0603:ability:active:8768d3f1fcb44728、S02-06D1:ability:active:30a9d18991dc8481、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0105:ability:active:0e81cd47a6221fd8、S01-0109:ability:active:88c64e7a7e50fb25、S01-0117:ability:active:ba48403c4da1e24c、S01-01D1:ability:active:2b7ae6d9b09b600b、S01-0214:ability:active:30e47404439f2371、S01-0215:ability:active:6984859bdd4fa8b1、S01-0317:ability:active:90c21e26f3d58b69、S01-03D1:ability:active:79829ccbe13dcca0、S01-04D1:ability:active:67457fb394219836、S02-0003:ability:active:484fb98a6af8df3f、S02-0104:ability:active:1687d445c6acc308、S02-0204:ability:active:4257a82eec559a94、S02-0205:ability:active:8023ed21f8771697、S02-0404:ability:active:b30de444d37a3b6e、S02-0510:ability:active:2ee4c7f29b568e48、S02-0513:ability:active:0b4d5245336709f8、S02-0520:ability:active:e4e320d416a9c103、S02-05D1:ability:active:f160e84288ecb28c、S02-0603:ability:active:8768d3f1fcb44728、S02-0616:ability:active:3616b237df312569、S02-06D1:ability:active:30a9d18991dc8481、ST02-05:ability:active:80aa98cc24ef764e、ST03-05:ability:active:87d142bd0e12a218、ST03-07:ability:active:0d4ebc1a2ab8b128、ST04-06:ability:active:8f6b1b9dfc246e36、ST05-06:ability:active:cc5d71f55d3a253f、ST06-09:ability:active:e533dbf15f08cea0

展示消费者出口：AddActivePaidCostPresentation。

## death:immortal-replacement（未完成）

绑定能力段：2。运行入口：active-state = L12GameEngine.HasActiveImmortal；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField。
档案附加检查：single-use, troops-set-to-1000, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591
- `target-invalidated`（已声明对象逆结算失效）：S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0220:ability:death:00f139f8bc316591、S01-0411:ability:death:00f139f8bc316591

展示消费者出口：档案未声明展示边界。

## declaration:front-row-composite-line（未完成）

绑定能力段：3。运行入口：entry-cost = L12GameEngine.PrintedEntryCostModifier；taunt = L12StructuredCardRules.HasTaunt；troops = L12StructuredCardRules.OpponentTurnFrontTroopsBonus。
档案附加检查：row-condition-current, authoritative-consumer, structured-split-siblings。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc） | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 0 | 3（S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc） | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0107:ability:static:715fe715dcb8ea28、S01-0204:ability:static:4108715d77479b32、S01-0312:ability:static:b2e1a67373ad69cc

展示消费者出口：档案未声明展示边界。

## declaration:saladin-line（未完成）

绑定能力段：1。运行入口：attack-passive = L12GameEngine.ApplyS1FactionAttackPassives；move = L12GameEngine.Move。
档案附加检查：composite-line-declaration, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0206:ability:static:cb39cf42a7feea7b） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S01-0206:ability:static:cb39cf42a7feea7b） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0206:ability:static:cb39cf42a7feea7b） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0206:ability:static:cb39cf42a7feea7b
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0206:ability:static:cb39cf42a7feea7b
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0206:ability:static:cb39cf42a7feea7b

展示消费者出口：档案未声明展示边界。

## disaster:continuous-rule（未完成）

绑定能力段：14。运行入口：attack-legion-validation = L12GameEngine.TryValidateAttackTarget；attack-master-validation = L12GameEngine.CanAttackMasterTarget；disaster-value = L12GameEngine.SetDisasterValue；effect-hook = L12GameEngine.PushEffect；hand-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；main-phase-effect = L12GameEngine.BeginMainPhaseDisasterEffect；master-ability-quote = L12GameEngine.QuoteActiveMorale；placement-and-movement = L12GameEngine.PlayCard；rule-registry = L12ActiveDisasterRules.HasRegisteredContinuousRule。
档案附加检查：registry-closed-set, condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 14（S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f） | 0 |
| no-target | 0 | 0 | 14 |
| negated | 0 | 0 | 14 |
| target-invalidated | 0 | 0 | 14 |
| duplicate-submit | 0 | 0 | 14 |
| reconnect | 0 | 14（S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f） | 0 |
| payment-cancel | 0 | 0 | 14 |
| single-candidate-choice | 0 | 2（S01-DS01:ability:static:9d604388d9725837、S02-DS05:ability:static:335d304b639c5d3f） | 12 |
| multi-target-applicability | 0 | 0 | 14 |
| presentation-consumers | 0 | 14（S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-DS01:ability:static:9d604388d9725837、S02-DS05:ability:static:335d304b639c5d3f
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-DS01:ability:static:9d604388d9725837、S01-DS02:ability:static:4408d437a8ab5e5a、S01-DS03:ability:static:70004a014a03d2a0、S01-DS04:ability:attack:68f2ff0b600a41e8、S01-DS04:ability:static:017c7359962a2512、S01-DS08:ability:static:3e7cd5724f09420c、S01-DS10:ability:static:33501d2503c08b73、S02-DS01:ability:static:31558cb4f3e2c3da、S02-DS02:ability:static:01aeea1f7fc7e317、S02-DS03:ability:continuous:fed4f60f2af1523c、S02-DS04:ability:static:00575cc9fcb1aaaa、S02-DS05:ability:attack:4326fa5eef9e6e3a、S02-DS05:ability:static:335d304b639c5d3f、S02-DS06:ability:static:c1632b7b22b87c4f

展示消费者出口：档案未声明展示边界。

## granted-static:front-row-taunt-on-kill（未完成）

绑定能力段：1。运行入口：active-state = L12StructuredCardRules.HasTaunt；grant = L12GameEngine.GrantTauntUntilNextOwnTurnEnd；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, front-row-required, turn-expiry, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0503:ability:granted-static:e67d03cee97f98a6） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 1（S02-0503:ability:granted-static:e67d03cee97f98a6） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0503:ability:granted-static:e67d03cee97f98a6） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0503:ability:granted-static:e67d03cee97f98a6） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0503:ability:granted-static:e67d03cee97f98a6
- `target-invalidated`（已声明对象逆结算失效）：S02-0503:ability:granted-static:e67d03cee97f98a6
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0503:ability:granted-static:e67d03cee97f98a6
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0503:ability:granted-static:e67d03cee97f98a6

展示消费者出口：BuildActiveKeywords。

## granted:constance-modes（未完成）

绑定能力段：1。运行入口：settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| no-target | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| negated | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| target-invalidated | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| duplicate-submit | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| reconnect | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0614:ability:granted:45f31f84b8f800cd） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0614:ability:granted:45f31f84b8f800cd
- `no-target`（无目标/不能发动）：S02-0614:ability:granted:45f31f84b8f800cd
- `negated`（已支付后被无效）：S02-0614:ability:granted:45f31f84b8f800cd
- `target-invalidated`（已声明对象逆结算失效）：S02-0614:ability:granted:45f31f84b8f800cd
- `duplicate-submit`（重复或过期提交）：S02-0614:ability:granted:45f31f84b8f800cd
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0614:ability:granted:45f31f84b8f800cd
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0614:ability:granted:45f31f84b8f800cd

展示消费者出口：档案未声明展示边界。

## granted:gain-rune（未完成）

绑定能力段：2。运行入口：settlement = L12S2ZoneOps.GainRunes。
档案附加检查：parent-grant-boundary, rune-cap, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0602:ability:granted:6235a3f3a12afdbb、S02-0614:ability:granted:6235a3f3a12afdbb

展示消费者出口：档案未声明展示边界。

## granted:lancelot-kill-modes（未完成）

绑定能力段：1。运行入口：settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| no-target | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| negated | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| target-invalidated | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| duplicate-submit | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| reconnect | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0602:ability:granted:7a7545729484412a） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0602:ability:granted:7a7545729484412a
- `no-target`（无目标/不能发动）：S02-0602:ability:granted:7a7545729484412a
- `negated`（已支付后被无效）：S02-0602:ability:granted:7a7545729484412a
- `target-invalidated`（已声明对象逆结算失效）：S02-0602:ability:granted:7a7545729484412a
- `duplicate-submit`（重复或过期提交）：S02-0602:ability:granted:7a7545729484412a
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0602:ability:granted:7a7545729484412a
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0602:ability:granted:7a7545729484412a

展示消费者出口：档案未声明展示边界。

## granted:prayer-modes（未完成）

绑定能力段：2。运行入口：preview = L12GameEngine.BeginPrayerPublicPreview；settle = L12GameEngine.TryResolveS2UniversalTactic。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| no-target | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| negated | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| target-invalidated | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| duplicate-submit | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| reconnect | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |
| payment-cancel | 0 | 1（S02-0012:ability:granted:e5bb0cce96aba072） | 1 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `no-target`（无目标/不能发动）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `negated`（已支付后被无效）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `target-invalidated`（已声明对象逆结算失效）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `duplicate-submit`（重复或过期提交）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072
- `payment-cancel`（有费用时的取消/支付失败兜底）：S02-0012:ability:granted:e5bb0cce96aba072
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0012:ability:granted:1c5ef0343f70615c、S02-0012:ability:granted:e5bb0cce96aba072

展示消费者出口：档案未声明展示边界。

## granted:ruined-ritual-modes（未完成）

绑定能力段：2。运行入口：response-commit = L12GameEngine.CommitS2CounterResponse；settle = L12GameEngine.ResolveS2CounterEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| no-target | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| negated | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| target-invalidated | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| duplicate-submit | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| reconnect | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `no-target`（无目标/不能发动）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `negated`（已支付后被无效）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `target-invalidated`（已声明对象逆结算失效）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `duplicate-submit`（重复或过期提交）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0016:ability:granted:df2c369f365d4497、S02-0016:ability:granted:dfd998389876f15e

展示消费者出口：档案未声明展示边界。

## granted:tenka-modes（未完成）

绑定能力段：3。运行入口：attack-bonus = L12GameEngine.Attack；free-move = L12GameEngine.Move；settle = L12GameEngine.TryResolveS2FactionTactic。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| no-target | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| negated | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| target-invalidated | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| duplicate-submit | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| reconnect | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 1（S02-0406:ability:granted:4f1f5a1d4791b5ef） | 2 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `no-target`（无目标/不能发动）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `negated`（已支付后被无效）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `target-invalidated`（已声明对象逆结算失效）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `duplicate-submit`（重复或过期提交）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S02-0406:ability:granted:4f1f5a1d4791b5ef
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0406:ability:granted:4f1f5a1d4791b5ef、S02-0406:ability:granted:4f26e688b66affd4、S02-0406:ability:granted:6aa04cbf27f6b4b7

展示消费者出口：档案未声明展示边界。

## hand-play:artifact-block（未完成）

绑定能力段：2。运行入口：authoritative-submit = L12GameEngine.PlayCard；button-and-snapshot = L12GameEngine.SnapshotHand；condition-and-reason = L12StructuredCardRules.HandPlayBlockReason；definition = L12StructuredCardSemantics.HandPlayBlockRule。
档案附加检查：source-zone, same-card-exception, priority, non-artifact-unaffected, display-and-submit-parity, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 2（S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a） | 0 |
| reconnect | 0 | 2（S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a
- `duplicate-submit`（重复或过期提交）：S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0205:ability:continuous:44bfa636b58de089、S02-0305:ability:continuous:26b824128ffced1a

展示消费者出口：档案未声明展示边界。

## hand-play:printed-entry-cost-condition（未完成）

绑定能力段：8。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；condition-and-calculation = L12GameEngine.PrintedEntryCostModifier；definition = L12StructuredCardSemantics.PrintedEntryCostRule；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice。
档案附加检查：condition-false, zero-floor, payment-cancel, reconnect-payment, display-and-payment-parity。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 8（S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d） | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 0 | 8（S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d） | 0 |
| reconnect | 0 | 8（S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d） | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 0 | 8（S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d
- `duplicate-submit`（重复或过期提交）：S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0104:ability:static:a91d7d481db612a9、S01-0114:ability:static:a91d7d481db612a9、S01-0301:ability:static:71dd875155781eb0、S01-0302:ability:static:acc29b0ca499d087、S01-0305:ability:static:9ed1ca8df2e5f029、S01-0306:ability:static:9ed1ca8df2e5f029、S02-0202:ability:continuous:94759febdd62fd32、S02-0203:ability:continuous:418e71545576e12d

展示消费者出口：档案未声明展示边界。

## hand-play:self-damage-entry-discount（未完成）

绑定能力段：6。运行入口：cost-calculation = L12GameEngine.GetPlayCostWithSigurdDiscount；declaration-and-choice = L12GameEngine.PlayCard；definition = L12StructuredCardRules.SelfDamageEntryDiscount；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice；self-damage-payment = L12GameEngine.PayMasterDamageCostAndCanContinue。
档案附加检查：optional-choice, payment-cancel, last-health-terminal, reconnect-payment, card-remains-on-lethal-cost。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 6（S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7） | 0 |
| no-target | 0 | 0 | 6 |
| negated | 0 | 0 | 6 |
| target-invalidated | 0 | 0 | 6 |
| duplicate-submit | 0 | 6（S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7） | 0 |
| reconnect | 6 | 0 | 0 |
| payment-cancel | 0 | 6（S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7） | 0 |
| single-candidate-choice | 0 | 0 | 6 |
| multi-target-applicability | 0 | 0 | 6 |
| presentation-consumers | 0 | 6（S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7
- `duplicate-submit`（重复或过期提交）：S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0303:ability:hand-play:5e06807975eda2b7、S01-0304:ability:hand-play:5e06807975eda2b7、S01-0308:ability:hand-play:5e06807975eda2b7、S01-0310:ability:hand-play:5e06807975eda2b7、S01-0314:ability:hand-play:5e06807975eda2b7、S02-0303:ability:hand-play:5e06807975eda2b7

展示消费者出口：档案未声明展示边界。

## hand-play:structured-hand-condition-cost（未完成）

绑定能力段：10。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；condition-and-calculation = L12StructuredCardRules.HandPlayCostModifier；definition = L12StructuredCardRules.TryGetStructuredAbilities；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice。
档案附加检查：condition-false, source-still-in-hand, effective-faction, zero-floor, payment-cancel, reconnect-payment, display-and-payment-parity。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 10（S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877） | 0 |
| no-target | 0 | 0 | 10 |
| negated | 0 | 0 | 10 |
| target-invalidated | 0 | 0 | 10 |
| duplicate-submit | 0 | 10（S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877） | 0 |
| reconnect | 0 | 10（S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877） | 0 |
| payment-cancel | 0 | 0 | 10 |
| single-candidate-choice | 0 | 0 | 10 |
| multi-target-applicability | 0 | 0 | 10 |
| presentation-consumers | 0 | 10（S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877
- `duplicate-submit`（重复或过期提交）：S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0509:ability:static:fff4ed8e0ac25ed9、S02-0510:ability:static:52b46f1b508e6aa1、S02-0512:ability:static:fff4ed8e0ac25ed9、S02-0518:ability:static:fff4ed8e0ac25ed9、S02-0605:ability:continuous:5ff487de55c0ca1d、S02-0611:ability:continuous:5745356459e85080、S02-0612:ability:continuous:064a0a1c5382575c、ST03-02:ability:continuous:057a02a660ebfae1、ST04-10:ability:continuous:2a1c905931cd7b32、ST06-01:ability:continuous:3ced1d4d38141877

展示消费者出口：档案未声明展示边界。

## keyword-granted:charge（未完成）

绑定能力段：4。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasPrintedKeywordReference；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 4（S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a） | 0 |
| no-target | 0 | 4（S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a） | 0 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 4（S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a） | 0 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 0 | 4（S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a） | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 0 | 4（S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a
- `no-target`（无目标/不能发动）：S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a
- `target-invalidated`（已声明对象逆结算失效）：S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-03M1:ability:granted:f4dd24f1fb07f3d5、S02-0403:ability:granted:f4dd24f1fb07f3d5、S02-0405:ability:granted:f4dd24f1fb07f3d5、ST01-01:ability:granted:c502e9ac1489cd1a

展示消费者出口：BuildActiveKeywords。

## keyword-granted:death-immunity（未完成）

绑定能力段：1。运行入口：active-state = L12GameEngine.HasActiveImmortal；definition = L12StructuredCardRules.HasPrintedKeywordReference；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ExpireEffectsAtPlayerTurnStart。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0404:ability:granted:e3ff02735b6b18f4） | 0 |
| no-target | 0 | 1（S02-0404:ability:granted:e3ff02735b6b18f4） | 0 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 1（S02-0404:ability:granted:e3ff02735b6b18f4） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0404:ability:granted:e3ff02735b6b18f4） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0404:ability:granted:e3ff02735b6b18f4） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0404:ability:granted:e3ff02735b6b18f4
- `no-target`（无目标/不能发动）：S02-0404:ability:granted:e3ff02735b6b18f4
- `target-invalidated`（已声明对象逆结算失效）：S02-0404:ability:granted:e3ff02735b6b18f4
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0404:ability:granted:e3ff02735b6b18f4
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0404:ability:granted:e3ff02735b6b18f4

展示消费者出口：BuildActiveKeywords。

## keyword-granted:must-hit（未完成）

绑定能力段：1。运行入口：attack-declaration = L12GameEngine.Attack；defense-submit = L12GameEngine.ValidateDefenseChoice；definition = L12StructuredCardRules.HasPrintedKeywordReference；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0206:ability:granted:1aba3f5bd15a426d） | 0 |
| no-target | 0 | 1（S02-0206:ability:granted:1aba3f5bd15a426d） | 0 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 1（S02-0206:ability:granted:1aba3f5bd15a426d） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0206:ability:granted:1aba3f5bd15a426d） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0206:ability:granted:1aba3f5bd15a426d） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0206:ability:granted:1aba3f5bd15a426d
- `no-target`（无目标/不能发动）：S02-0206:ability:granted:1aba3f5bd15a426d
- `target-invalidated`（已声明对象逆结算失效）：S02-0206:ability:granted:1aba3f5bd15a426d
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0206:ability:granted:1aba3f5bd15a426d
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0206:ability:granted:1aba3f5bd15a426d

展示消费者出口：BuildActiveKeywords。

## keyword-granted:piercing（未完成）

绑定能力段：1。运行入口：combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasPrintedKeywordReference；generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；master-target-revalidation = L12GameEngine.CanAttackMasterTarget；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（ST01-01:ability:granted:6ec4b634ed12b206） | 0 |
| no-target | 0 | 1（ST01-01:ability:granted:6ec4b634ed12b206） | 0 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 1（ST01-01:ability:granted:6ec4b634ed12b206） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（ST01-01:ability:granted:6ec4b634ed12b206） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（ST01-01:ability:granted:6ec4b634ed12b206） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：ST01-01:ability:granted:6ec4b634ed12b206
- `no-target`（无目标/不能发动）：ST01-01:ability:granted:6ec4b634ed12b206
- `target-invalidated`（已声明对象逆结算失效）：ST01-01:ability:granted:6ec4b634ed12b206
- `reconnect`（Prompt/堆叠/选择阶段重连）：ST01-01:ability:granted:6ec4b634ed12b206
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：ST01-01:ability:granted:6ec4b634ed12b206

展示消费者出口：档案未声明展示边界。

## keyword-granted:taunt（未完成）

绑定能力段：2。运行入口：active-state = L12StructuredCardRules.HasTaunt；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasPrintedKeywordReference；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e） | 0 |
| no-target | 0 | 2（S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e） | 0 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e
- `no-target`（无目标/不能发动）：S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e
- `target-invalidated`（已声明对象逆结算失效）：S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0004:ability:granted:be3174252606645e、S02-0007:ability:granted:be3174252606645e

展示消费者出口：BuildActiveKeywords。

## keyword:charge（未完成）

绑定能力段：3。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasKeywordDefinition；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d） | 0 |
| no-target | 0 | 3（S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d） | 0 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 3（S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d） | 0 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 0 | 3（S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d） | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d
- `no-target`（无目标/不能发动）：S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d
- `target-invalidated`（已声明对象逆结算失效）：S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0505:ability:keyword-definition:cf232142ca7d10f9、S02-0602:ability:keyword-definition:beff9037e2c10a9d、S02-0612:ability:keyword-definition:beff9037e2c10a9d

展示消费者出口：BuildActiveKeywords。

## keyword:death-immunity（未完成）

绑定能力段：2。运行入口：active-state = L12GameEngine.HasActiveImmortal；definition = L12StructuredCardRules.HasKeywordDefinition；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ExpireEffectsAtPlayerTurnStart。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b） | 0 |
| no-target | 0 | 2（S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b） | 0 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b
- `no-target`（无目标/不能发动）：S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b
- `target-invalidated`（已声明对象逆结算失效）：S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0608:ability:keyword-definition:4d1e472a814a1e0b、S02-0611:ability:keyword-definition:4d1e472a814a1e0b

展示消费者出口：BuildActiveKeywords。

## keyword:piercing（未完成）

绑定能力段：2。运行入口：combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasKeywordDefinition；generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；master-target-revalidation = L12GameEngine.CanAttackMasterTarget；printed-identity = L12StructuredCardRules.HasPrintedKeywordReference；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f） | 0 |
| no-target | 0 | 2（S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f） | 0 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f
- `no-target`（无目标/不能发动）：S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f
- `target-invalidated`（已声明对象逆结算失效）：S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0606:ability:keyword-definition:672734be0285300f、S02-0611:ability:keyword-definition:672734be0285300f

展示消费者出口：档案未声明展示边界。

## keyword:shock（未完成）

绑定能力段：2。运行入口：attack-trigger = L12GameEngine.ApplyS2Shock；combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasKeywordDefinition；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ResetTemporaryCardState。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae） | 0 |
| no-target | 0 | 2（S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae） | 0 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae
- `no-target`（无目标/不能发动）：S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae
- `target-invalidated`（已声明对象逆结算失效）：S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0511:ability:keyword-definition:96aa4e9504b12339、S02-05M1:ability:keyword-definition:41657ed47ef085ae

展示消费者出口：BuildActiveKeywords。

## keyword:strong-attack（未完成）

绑定能力段：2。运行入口：active-state = L12StructuredCardSemantics.HasEffectiveStrongAttack；combat-settlement = L12GameEngine.Attack；definition = L12StructuredCardRules.HasKeywordDefinition；grant = L12GameEngine.GrantStrongAttack；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ResetTemporaryCardState。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8） | 0 |
| no-target | 0 | 2（S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8） | 0 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 2（S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8
- `no-target`（无目标/不能发动）：S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8
- `target-invalidated`（已声明对象逆结算失效）：S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-05M1:ability:keyword-definition:995c52041c470ca4、S02-0605:ability:keyword-definition:60bccaeb6d982ea8

展示消费者出口：BuildActiveKeywords。

## keyword:taunt（未完成）

绑定能力段：8。运行入口：active-state = L12StructuredCardRules.HasTaunt；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasKeywordDefinition；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 8（S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a） | 0 |
| no-target | 0 | 8（S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a） | 0 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 8（S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a） | 0 |
| duplicate-submit | 0 | 0 | 8 |
| reconnect | 0 | 8（S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a） | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 0 | 8（S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a
- `no-target`（无目标/不能发动）：S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a
- `target-invalidated`（已声明对象逆结算失效）：S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0302:ability:keyword-definition:eaba79729a9d7a65、S02-0503:ability:keyword-definition:6692b63a59c971d0、S02-0512:ability:keyword-definition:6692b63a59c971d0、S02-0615:ability:keyword-definition:8a4c9aff096f6526、ST01-04:ability:keyword-definition:c24a6b9d8de8435a、ST02-02:ability:keyword-definition:c24a6b9d8de8435a、ST04-01:ability:keyword-definition:c24a6b9d8de8435a、ST06-02:ability:keyword-definition:c24a6b9d8de8435a

展示消费者出口：BuildActiveKeywords。

## leave:attached-tactics-discard（未完成）

绑定能力段：1。运行入口：discard = L12GameEngine.DiscardAttachedCards。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| no-target | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| negated | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| target-invalidated | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| duplicate-submit | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| reconnect | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0013:ability:host-leaves-artifact:b2720c3b205be910） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `no-target`（无目标/不能发动）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `negated`（已支付后被无效）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `target-invalidated`（已声明对象逆结算失效）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `duplicate-submit`（重复或过期提交）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0013:ability:host-leaves-artifact:b2720c3b205be910
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0013:ability:host-leaves-artifact:b2720c3b205be910

展示消费者出口：档案未声明展示边界。

## leave:master-legion-return（未完成）

绑定能力段：1。运行入口：departure = L12GameEngine.CompleteMasterLegionDeparture；morale-trigger = L12GameEngine.TryResolveSimpleResourceTrigger。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| no-target | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| negated | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| target-invalidated | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| duplicate-submit | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| reconnect | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-01M1:ability:leave:cf42cfffe1b9b9bc） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `no-target`（无目标/不能发动）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `negated`（已支付后被无效）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `target-invalidated`（已声明对象逆结算失效）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `duplicate-submit`（重复或过期提交）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-01M1:ability:leave:cf42cfffe1b9b9bc
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-01M1:ability:leave:cf42cfffe1b9b9bc

展示消费者出口：档案未声明展示边界。

## lethal-replacement:offer-pipeline（未完成）

绑定能力段：3。运行入口：apply = L12GameEngine.TryApplyCardLethalSubstitution；candidates = L12GameEngine.CardLethalSubstitutionCandidates；eligibility = L12GameEngine.CanUseAchillesLethalReplacement；offer = L12GameEngine.TryOfferEffectLethalReplacement；substitution-kind = L12GameEngine.CardLethalSubstitutionKind。
档案附加检查：payment-cancel, once-per-turn, front-row-required, recovery-resume, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 3（S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 0 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 0 | 3（S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 0 |
| payment-cancel | 0 | 2（S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 1 |
| single-candidate-choice | 0 | 1（S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 2 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d
- `target-invalidated`（已声明对象逆结算失效）：S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d
- `payment-cancel`（有费用时的取消/支付失败兜底）：S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S02-0515:ability:lethal-replacement:654c3040d6da8b4d
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0205:ability:death:7016351513168cdb、S02-0504:ability:lethal-replacement:3fb565d50830f260、S02-0515:ability:lethal-replacement:654c3040d6da8b4d

展示消费者出口：档案未声明展示边界。

## morale:active-effect-pipeline（未完成）

绑定能力段：14。运行入口：button = L12GameEngine.FactionEffectSnapshot；commit = L12GameEngine.CommitActiveAbilityCore；cost-table = L12GameEngine.GetActiveAbilityMoraleCost；eligibility = L12GameEngine.ActiveAbilityUnavailableReason；identity-normalization = L12MoraleIdentityCatalog.CanonicalEffectCardId；settlement-dispatch = L12GameEngine.ResolveActiveEffect；usage-rule = L12ActiveUsageRules.Find。
档案附加检查：canonical-version-parity, once-per-turn, morale-cost-table, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |
| no-target | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |
| negated | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |
| target-invalidated | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |
| duplicate-submit | 0 | 0 | 14 |
| reconnect | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |
| payment-cancel | 0 | 0 | 14 |
| single-candidate-choice | 0 | 2（S01-04C1:ability:static:7f60c31c00b0f718、ST04-C1:ability:static:d9cac21fb706e3c8） | 12 |
| multi-target-applicability | 0 | 0 | 14 |
| presentation-consumers | 0 | 14（S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee
- `no-target`（无目标/不能发动）：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee
- `negated`（已支付后被无效）：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee
- `target-invalidated`（已声明对象逆结算失效）：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-04C1:ability:static:7f60c31c00b0f718、ST04-C1:ability:static:d9cac21fb706e3c8
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-01C1:ability:active:3a8789b35c0c2be4、S01-02C1:ability:static:91802cda49d575fb、S01-02C1:ability:static:ddab147dd97c360f、S01-03C1:ability:static:fa92f5d792a32bdc、S01-04C1:ability:static:7f60c31c00b0f718、S02-05C1:ability:active:5dec5c18aaf62a03、S02-05C1A:ability:active:5dec5c18aaf62a03、S02-06C1:ability:static:7339369656140c39、ST01-C1:ability:static:6907bfcf5dbbfeb4、ST02-C1:ability:static:29d1864e955f856e、ST02-C1:ability:static:f2b97501194b5c40、ST03-C1:ability:static:36b1c5751cc508f9、ST04-C1:ability:static:d9cac21fb706e3c8、ST06-C1:ability:static:88a76dc195d499ee

展示消费者出口：档案未声明展示边界。

## morale:resource-identity（未完成）

绑定能力段：3。运行入口：manual-selection = L12GameEngine.CanConsumeSelectedResources；payment = L12GameEngine.TryConsumeMorale；resource-count = L12GameEngine.ActiveResourceCount。
档案附加检查：counts-as-morale-structural, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf） | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 0 | 3（S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf） | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-00C1:ability:static:db1ae0a9efb4bff8、S02-05C1:ability:static:5879d4c3fe97b3cf、S02-05C1A:ability:static:5879d4c3fe97b3cf

展示消费者出口：档案未声明展示边界。

## pipeline:active-effect（未完成）

绑定能力段：45。运行入口：begin = L12GameEngine.BeginActiveAbility；commit = L12GameEngine.CommitActiveAbilityCore；settle = L12GameEngine.ResolveActiveEffect；stack = L12GameEngine.PushEffect；usage = L12ActiveUsageRules.Find；views = L12GameEngine.BuildAbilityViews。
档案附加检查：per-card-branch, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |
| no-target | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |
| negated | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |
| target-invalidated | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |
| duplicate-submit | 0 | 0 | 45 |
| reconnect | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |
| payment-cancel | 0 | 0 | 45 |
| single-candidate-choice | 0 | 27（S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M3:ability:static:705baec08fc6bc02、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04M1:ability:static:2c285709f5669922、S01-04M2:ability:attack:ebb2054e23f75cd1、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S6:ability:after-attack:b158f5749a6c161e） | 18 |
| multi-target-applicability | 0 | 2（S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de） | 43 |
| presentation-consumers | 0 | 45（S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e
- `no-target`（无目标/不能发动）：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e
- `negated`（已支付后被无效）：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e
- `target-invalidated`（已声明对象逆结算失效）：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M3:ability:static:705baec08fc6bc02、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04M1:ability:static:2c285709f5669922、S01-04M2:ability:attack:ebb2054e23f75cd1、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S6:ability:after-attack:b158f5749a6c161e
- `multi-target-applicability`（多目标协议的部分失效继续）：S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0004:ability:active:6f9f6988e1ea4be0、S01-01M1:ability:static:c03878ecc263c0e6、S01-01M1:ability:static:d024f673ff236321、S01-01M2:ability:static:f3ee48a69ee29306、S01-0215:ability:mode-ready-guard:3e3294affff84b58、S01-0215:ability:mode-rest-and-draw:8c1a03af8e682c53、S01-02D1:ability:static:0c86a6851cf9d2ce、S01-02D1:ability:static:dbf8222a61a31140、S01-02M1:ability:static:53475d8f080332f1、S01-02M2:ability:static:f3398615ac233d89、S01-02M3:ability:static:705baec08fc6bc02、S01-0307:ability:static:b89287bced985f8c、S01-0314:ability:active:a923615d65edc8ea、S01-03D1:ability:static:342ed2c72fcd22aa、S01-03D1:ability:static:d89d0b3dade7b6c8、S01-03M1:ability:static:d047647f18d541e4、S01-03M2:ability:static:e3e85412fe04e44b、S01-0417:ability:static:f10ff922d718f82e、S01-04D1:ability:static:3c467d3eba318af6、S01-04D1:ability:static:fcd47c32a0a46e1a、S01-04M1:ability:static:2c285709f5669922、S01-04M1:ability:static:51c3f1e1976210f8、S01-04M2:ability:attack:ebb2054e23f75cd1、S01-04M2:ability:static:ce8699cac703af1c、S02-0013:ability:active-while-attached:f64dc7647e481c5f、S02-01M1:ability:active:4834e3b50d036f27、S02-0205:ability:active:bf422a987e0ab5de、S02-02M1:ability:active:014219b1c6c557fa、S02-0301:ability:active:61c655977499e4be、S02-03M1:ability:active:54e6f9c40764f804、S02-0404:ability:granted:2c2b9693ca8cf3b8、S02-0404:ability:granted:e7c384ccba9ff2f3、S02-0520:ability:mode-promotion-discount:98eb71c68928c091、S02-0520:ability:mode-ready-after-kill:927badbb354c7607、S02-05D1:ability:active:1e9195c93dff4ee9、S02-05M1:ability:active:b2bece6897eb980b、S02-05M2:ability:active:e4b2c63a32960f8e、S02-0603:ability:granted:8cd73702b7db90b0、S02-0603:ability:granted:ee3b46417c9fc4f7、S02-0604:ability:trial-completed:9d25a05a194bedc1、S02-06D1:ability:static:65b6607da57e5096、S02-06M1:ability:active:08922e53e852b78f、S02-06S1:ability:static:75769d93e0ca669f、S02-06S5:ability:static:5444a7c87e0351bd、S02-06S6:ability:after-attack:b158f5749a6c161e

展示消费者出口：档案未声明展示边界。

## pipeline:disaster-authority（未完成）

绑定能力段：4。运行入口：damage = L12GameEngine.DamageMasterNonLethalFromDisaster；settle = L12GameEngine.ResolveDisasterEffect；trigger = L12GameEngine.BeginDisasterTrigger；turn-end = L12GameEngine.ResolveEndPhaseDisasterEffect；turn-start = L12GameEngine.ResolveTurnStartDisasterEffectIfNeeded。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 4（S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf） | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 4（S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf） | 0 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 0 | 4（S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf） | 0 |
| payment-cancel | 0 | 1（ST-DS03:ability:disaster:c974ef724419ccdf） | 3 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 0 | 4（S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf
- `target-invalidated`（已声明对象逆结算失效）：S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf
- `payment-cancel`（有费用时的取消/支付失败兜底）：ST-DS03:ability:disaster:c974ef724419ccdf
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-DS02:ability:turn-end:9d632a451357ff71、S01-DS10:ability:turn-start:a790e35d0012c86f、ST-DS01:ability:disaster:00612b44a6a3ac99、ST-DS03:ability:disaster:c974ef724419ccdf

展示消费者出口：档案未声明展示边界。

## pipeline:hand-play（未完成）

绑定能力段：19。运行入口：composite-declaration = L12GameEngine.BeginCompositeHandPlayDeclaration；composite-validation = L12GameEngine.ValidateCompositeHandPlayDeclaration；cost = L12GameEngine.GetPlayCostWithSigurdDiscount；play = L12GameEngine.PlayCard；settle = L12GameEngine.ResolveTacticEffect。
档案附加检查：per-card-flow, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |
| no-target | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |
| negated | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |
| target-invalidated | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |
| duplicate-submit | 0 | 0 | 19 |
| reconnect | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |
| payment-cancel | 0 | 0 | 19 |
| single-candidate-choice | 0 | 12（S02-0206:ability:play:bd784d08e38e0ed8、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 7 |
| multi-target-applicability | 0 | 0 | 19 |
| presentation-consumers | 0 | 19（S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `no-target`（无目标/不能发动）：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `negated`（已支付后被无效）：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `target-invalidated`（已声明对象逆结算失效）：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S02-0206:ability:play:bd784d08e38e0ed8、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0012:ability:play:bafe1ab6a18493c0、S02-0206:ability:play:bd784d08e38e0ed8、S02-0206:ability:play:ca021e5c16b59965、S02-0302:ability:hand-play:4e8ff9ea92325bac、S02-0306:ability:master-effect-damage-threshold:978e2dc72d59418c、S02-0307:ability:play:f9b21f21c30d2eb3、S02-0405:ability:play:0a13775c2081e642、S02-0405:ability:play:b03190adf1322a1a、S02-0406:ability:play:35815c7115c7ce71、S02-0521:ability:play-additional:2b5a094468a81a7a、S02-0522:ability:play-additional:49fb773d1512e5b3、S02-0522:ability:play:a09dadaebc5e13de、S02-0620:ability:play:ac4a80f231805917、S02-0620:ability:play:c2e3d34e7ac83c86、S02-0621:ability:play:9a7d744018bd9e66、S02-0621:ability:play:ecdfaa719e9112ba、S02-0622:ability:hand-play:5b5e4bf8f495f21a、S02-0622:ability:play:d5226a525c565d25、ST03-01:ability:entry-discount:373b8092202cdf17

展示消费者出口：档案未声明展示边界。

## pipeline:public-trigger（未完成）

绑定能力段：40。运行入口：batch-plan = L12TriggerBatchPlanner.Plan；begin-declaration = L12GameEngine.TryBeginPublicTriggerDeclaration；candidates = L12GameEngine.QueueTriggerCandidates；complete-declaration = L12GameEngine.TryCompletePublicTriggerDeclaration；settle = L12GameEngine.ResolveTopStack。
档案附加检查：per-card-plan, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |
| no-target | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |
| negated | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |
| target-invalidated | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |
| duplicate-submit | 0 | 0 | 40 |
| reconnect | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |
| payment-cancel | 0 | 13（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-0311:ability:static:3409dd9fa29f684f、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0612:ability:attack:c195f409c875e9eb、S02-06S5:ability:static:1e799825eedf3331） | 27 |
| single-candidate-choice | 0 | 9（S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0614:ability:enter:601eddfb8abbb8d2、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331） | 31 |
| multi-target-applicability | 0 | 0 | 40 |
| presentation-consumers | 0 | 40（S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93
- `no-target`（无目标/不能发动）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93
- `negated`（已支付后被无效）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93
- `target-invalidated`（已声明对象逆结算失效）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-0311:ability:static:3409dd9fa29f684f、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0612:ability:attack:c195f409c875e9eb、S02-06S5:ability:static:1e799825eedf3331
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0614:ability:enter:601eddfb8abbb8d2、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-01D1:ability:static:103012fd4239104f、S01-01M1:ability:death:ee5adb706424f233、S01-01M1:ability:static:0924c3a5995ba164、S01-0204:ability:leave:a59801f7c2874f4a、S01-02M3:ability:static:3a86c87f975d5851、S01-02M3:ability:static:c339139cc1c9b00c、S01-0311:ability:after-attack:65ce2315ff4c0465、S01-0311:ability:static:3409dd9fa29f684f、S01-0414:ability:static:e001b352b3693d93、S01-04M2:ability:leave:4e83a7191108369b、S02-0001:ability:after-opponent-tactic:6d30a9b672845491、S02-0006:ability:discarded:89d3ee4207648aa1、S02-0103:ability:attack:607e6460eed6637b、S02-01S1:ability:master-morale-return:8d098fe32e7b253b、S02-02M1:ability:friendly-legion-death:a366c9a7f75b5f29、S02-0304:ability:master-damaged-by-effect:31c5c76dff1c8e0b、S02-0305:ability:master-damaged:a4a2c92cad3ad28c、S02-0401:ability:continuous:9601da1d8445f865、S02-04M1:ability:friendly-back-to-front:03cb93e7e3eeedf3、S02-04M1:ability:friendly-front-to-back:e46218b2ac936410、S02-04M1:ability:friendly-legion-moves:654df25d049352f7、S02-0503:ability:after-attack:e3ced12ddde14fdb、S02-0511:ability:attack:c367ee3457cbd5f4、S02-0516:ability:attack:077dc7337586413c、S02-0523:ability:after-opponent-attack:5bff9b891b7b1cba、S02-0602:ability:after-kill:e290e1e434e45531、S02-0605:ability:attack:82a5bf2622bf4d20、S02-0607:ability:attack:25d5c998d14502d7、S02-0608:ability:attack:0999d120e02e3c50、S02-0608:ability:attack:4581df1cc635dd68、S02-0610:ability:after-trial:451b6d549a5c98c4、S02-0611:ability:enter:0cc32f023a1b4f11、S02-0612:ability:attack:c195f409c875e9eb、S02-0614:ability:enter:601eddfb8abbb8d2、S02-0617:ability:attack:c8dd6c6601a73ebb、S02-06M2:ability:tactic-effect-resolved:e802cc6dcf73fe92、S02-06S3:ability:static:3616e3ca17ffd729、S02-06S4:ability:trial-complete:f95fed6f3ff0efc0、S02-06S5:ability:static:1e799825eedf3331、ST01-C1:ability:static:605b9aa3d8a1ed93

展示消费者出口：档案未声明展示边界。

## pipeline:response（未完成）

绑定能力段：2。运行入口：candidates = L12GameEngine.LegalResponseSources；pool-timing = L12GameEngine.IsPoolCounterResponseAtTiming；settle = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：capability-registry-pending, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |
| no-target | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |
| negated | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |
| target-invalidated | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 1（S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 1 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `no-target`（无目标/不能发动）：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `negated`（已支付后被无效）：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `target-invalidated`（已声明对象逆结算失效）：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0005:ability:opponent-attacks-master:806afb384f303aee、S02-0106:ability:opponent-attack-or-effect:899eef6cc1186e9c

展示消费者出口：档案未声明展示边界。

## private-zone:strict-hand-entry（未完成）

绑定能力段：3。运行入口：declaration = L12GameEngine.CreateActivationStepPrompt；dependent-continuation = L12GameEngine.QueueNextCompositeSegment；failed-settlement = L12GameEngine.RecordTargetSettlementFailure；settlement-revalidation = L12GameEngine.TrySummonFromHand；source-failure = L12GameEngine.RecordResolutionFailure。
档案附加检查：private-hand-redaction, settlement-slot-invalidated, stale-instance-no-replacement, then-requires-success。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| no-target | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| negated | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| target-invalidated | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| duplicate-submit | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| reconnect | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| payment-cancel | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |
| single-candidate-choice | 0 | 2（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd） | 1 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `no-target`（无目标/不能发动）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `negated`（已支付后被无效）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `target-invalidated`（已声明对象逆结算失效）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `duplicate-submit`（重复或过期提交）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0105:ability:enter:ee4ec5ee9f9e1cce、S01-0116:ability:static:74c527aaab5e91cd、S01-0213:ability:after-attack:55cfe31dc7ed5969

展示消费者出口：档案未声明展示边界。

## reaction:hand-block（未完成）

绑定能力段：1。运行入口：candidates = L12GameEngine.LegalResponseSources；commit = L12GameEngine.CommitMercenaryResponse；pool-timing = L12GameEngine.CanMasterCardPoolRespondAtTiming；settlement = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：self-discard-cost, defender-only, capability-registry, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |
| target-invalidated | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |
| payment-cancel | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-0002:ability:reaction:a472c4e7c34abf4b） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0002:ability:reaction:a472c4e7c34abf4b
- `negated`（已支付后被无效）：S01-0002:ability:reaction:a472c4e7c34abf4b
- `target-invalidated`（已声明对象逆结算失效）：S01-0002:ability:reaction:a472c4e7c34abf4b
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0002:ability:reaction:a472c4e7c34abf4b
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-0002:ability:reaction:a472c4e7c34abf4b
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0002:ability:reaction:a472c4e7c34abf4b

展示消费者出口：档案未声明展示边界。

## reaction:negate-pipeline（未完成）

绑定能力段：2。运行入口：candidates = L12GameEngine.LegalResponseSources；commit = L12GameEngine.CommitNegateResponse；pool-timing = L12GameEngine.IsPoolCounterResponseAtTiming；settlement = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：payment-cancel, capability-registry, pool-parity, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 2（S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77） | 0 |
| target-invalidated | 0 | 2（S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77） | 0 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77） | 0 |
| payment-cancel | 0 | 1（S01-0016:ability:reaction:eda8f9987e9ccfe3） | 1 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77
- `negated`（已支付后被无效）：S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77
- `target-invalidated`（已声明对象逆结算失效）：S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-0016:ability:reaction:eda8f9987e9ccfe3
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0016:ability:reaction:eda8f9987e9ccfe3、S01-0018:ability:reaction:248207b49df4bd77

展示消费者出口：档案未声明展示边界。

## replacement:anderstorp-damage-floor（未完成）

绑定能力段：1。运行入口：damage-floor = L12GameEngine.AdjustAnderstorpRingDamage。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| no-target | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| negated | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| target-invalidated | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| duplicate-submit | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| reconnect | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0305:ability:master-damaged:4c8ce907eed1f778） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `no-target`（无目标/不能发动）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `negated`（已支付后被无效）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `target-invalidated`（已声明对象逆结算失效）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `duplicate-submit`（重复或过期提交）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0305:ability:master-damaged:4c8ce907eed1f778
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0305:ability:master-damaged:4c8ce907eed1f778

展示消费者出口：档案未声明展示边界。

## replacement:lake-lady-sword（未完成）

绑定能力段：2。运行入口：replacement = L12GameEngine.TryApplyLakeLadySwordReplacement。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| no-target | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| negated | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| target-invalidated | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| duplicate-submit | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| reconnect | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |
| payment-cancel | 0 | 1（S02-06S3:ability:static:f7e019a543066afd） | 1 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `no-target`（无目标/不能发动）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `negated`（已支付后被无效）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `target-invalidated`（已声明对象逆结算失效）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `duplicate-submit`（重复或过期提交）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd
- `payment-cancel`（有费用时的取消/支付失败兜底）：S02-06S3:ability:static:f7e019a543066afd
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-06S3:ability:death:84330d935c195208、S02-06S3:ability:static:f7e019a543066afd

展示消费者出口：档案未声明展示边界。

## replacement:morale-zone-resource（未完成）

绑定能力段：1。运行入口：definition = L12StructuredCardSemantics.MoraleZoneResourceRule；payment-identity = L12GameEngine.OrdinaryPaymentSemanticKey；payment-prompt = L12GameEngine.CreateResourcePaymentPrompt；return-prompt = L12GameEngine.CreateReturnMoralePrompt；return-settlement = L12GameEngine.ReturnMoraleCardToDestination；snapshot-projection = L12GameEngine.SnapshotMorale。
档案附加检查：exact-card-family, entered-tapped, payment-distinct-identity, return-owner-graveyard, automatic-return, duplicate-submit, v2-snapshot, replay-projection, frontend-structured-identity。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0010:ability:return-as-morale:9169de0e99d296e2） | 0 |

缺失明细与建议补测范围：
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0010:ability:return-as-morale:9169de0e99d296e2

展示消费者出口：档案未声明展示边界。

## resource:morale-face-flip（未完成）

绑定能力段：11。运行入口：candidate-generation = L12GameEngine.CanFlipMoraleToGodPower；identity-definition = L12MoraleIdentityCatalog.CanUseGodPowerFace；resolution-prompt = L12GameEngine.PromptS2FlipMorale；settlement-mutation = L12S2ZoneOps.FlipMoraleFace；toggle-candidate-generation = L12GameEngine.CanToggleMoraleFace。
档案附加检查：exact-printed-family, version-alias, black-lotus-excluded, candidate-settlement-parity, rested-only-filter, single-candidate-choice, multi-target-independent-revalidation, v2-prompt-reconnect。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 5 | 6（S02-0508:ability:death:9aea23b4138e399e、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |
| no-target | 4 | 7（S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65） | 0 |
| negated | 1 | 10（S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |
| target-invalidated | 3 | 8（S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |
| duplicate-submit | 2 | 9（S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |
| reconnect | 0 | 11（S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |
| payment-cancel | 0 | 4（S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 7 |
| single-candidate-choice | 4 | 3（S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527） | 4 |
| multi-target-applicability | 1 | 1（ST05-M1:ability:active:b1f11ab05f68dda0） | 9 |
| presentation-consumers | 0 | 11（S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0508:ability:death:9aea23b4138e399e、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `no-target`（无目标/不能发动）：S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65
- `negated`（已支付后被无效）：S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `target-invalidated`（已声明对象逆结算失效）：S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `duplicate-submit`（重复或过期提交）：S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `payment-cancel`（有费用时的取消/支付失败兜底）：S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0
- `single-candidate-choice`（有对象选择时的唯一候选仍选择）：S02-0521:ability:play:4ae24413479102d1、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527
- `multi-target-applicability`（多目标协议的部分失效继续）：ST05-M1:ability:active:b1f11ab05f68dda0
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0508:ability:death:9aea23b4138e399e、S02-0513:ability:enter:eef83ec51f2ef093、S02-0518:ability:enter:6e9ddf89fefa712f、S02-0520:ability:enter:361ec387b847ecee、S02-0521:ability:play:4ae24413479102d1、S02-05C1:ability:active:1ae9b19504eac93a、S02-05C1A:ability:active:1ae9b19504eac93a、S02-05D1:ability:active:519ab3c1379a9256、S02-05M1:ability:friendly-ranged-death:ba2dac5cf1c08527、ST05-C1:ability:static:6fe475d8923feb65、ST05-M1:ability:active:b1f11ab05f68dda0

展示消费者出口：档案未声明展示边界。

## rule-action:cavalry-move（未完成）

绑定能力段：8。运行入口：button = L12GameEngine.BuildRuleActionViews；candidate-generation = L12GameEngine.CavalryMoveDestinationKeys；command = L12GameEngine.CavalryMove；destination-revalidation = L12GameEngine.IsLegalCavalryMoveDestination；movement-event = L12GameEngine.RecordLegionMovement；presentation = L12GameEngine.NativeCavalryMovePresentation；source-eligibility = L12GameEngine.CavalryMoveSourceUnavailableReason；timing = L12GameEngine.CavalryMoveTimingUnavailableReason。
档案附加检查：source-invalidated, destination-invalidated, single-candidate-choice。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 8 | 0 | 0 |
| no-target | 8 | 0 | 0 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 8 | 0 | 0 |
| reconnect | 8 | 0 | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 0 | 8（S01-0002:ability:active:2786430f57a9abaa、S01-0106:ability:active:2786430f57a9abaa、S01-0310:ability:active:0a0575206e996652、S01-0409:ability:active:56a01edf47ee1225、S02-0505:ability:active:bac4cb5d348f29f1、ST01-01:ability:active:69626894e55e27e5、ST04-01:ability:active:2786430f57a9abaa、ST06-04:ability:active:719cc1c7c1084fa0） | 0 |

缺失明细与建议补测范围：
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-0002:ability:active:2786430f57a9abaa、S01-0106:ability:active:2786430f57a9abaa、S01-0310:ability:active:0a0575206e996652、S01-0409:ability:active:56a01edf47ee1225、S02-0505:ability:active:bac4cb5d348f29f1、ST01-01:ability:active:69626894e55e27e5、ST04-01:ability:active:2786430f57a9abaa、ST06-04:ability:active:719cc1c7c1084fa0

展示消费者出口：NativeCavalryMovePresentation。

## rule:game-setup（未完成）

绑定能力段：3。运行入口：hand-preparation = L12GameEngine.PrepareLibrariesAndHands；setup-defaults = L12GameEngine.BeginOptionalS2Setup。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 3（S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64） | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 0 | 3（S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64） | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 0 | 3（S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0305:ability:game-setup:cf14affeb486a9f7、S02-03M1:ability:game-setup:46b2a85c54cecc56、S02-05D1:ability:setup:cb6a45eff0631d64

展示消费者出口：档案未声明展示边界。

## rule:isis-setup（未完成）

绑定能力段：1。运行入口：setup = L12GameEngine.PrepareLibrariesAndHands。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| no-target | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| negated | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| target-invalidated | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| duplicate-submit | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| reconnect | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-02M1:ability:static:68187ab0edb25d9c） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-02M1:ability:static:68187ab0edb25d9c
- `no-target`（无目标/不能发动）：S01-02M1:ability:static:68187ab0edb25d9c
- `negated`（已支付后被无效）：S01-02M1:ability:static:68187ab0edb25d9c
- `target-invalidated`（已声明对象逆结算失效）：S01-02M1:ability:static:68187ab0edb25d9c
- `duplicate-submit`（重复或过期提交）：S01-02M1:ability:static:68187ab0edb25d9c
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-02M1:ability:static:68187ab0edb25d9c
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-02M1:ability:static:68187ab0edb25d9c

展示消费者出口：档案未声明展示边界。

## rule:once-per-turn-by-name（未完成）

绑定能力段：2。运行入口：usage-check = L12CardNameUsageRules.HasUsed；usage-commit = L12CardNameUsageRules.TryUse；usage-key = L12CardNameUsageRules.Key。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac） | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 0 | 2（S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0006:ability:continuous:7f3bdf9055e53845、S02-0306:ability:continuous:a5a8e191442bbfac

展示消费者出口：档案未声明展示边界。

## rule:thor-hammer-master-gate（未完成）

绑定能力段：1。运行入口：ability-gate = L12GameEngine.BuildAbilityViews。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0301:ability:continuous:e48cf407ce847427） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0301:ability:continuous:e48cf407ce847427） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0301:ability:continuous:e48cf407ce847427） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0301:ability:continuous:e48cf407ce847427
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0301:ability:continuous:e48cf407ce847427
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0301:ability:continuous:e48cf407ce847427

展示消费者出口：档案未声明展示边界。

## rule:trial-capacity（未完成）

绑定能力段：2。运行入口：capacity = L12SpecialDeckRules.TrialCapacity；validator = L12DeckValidator.TryValidate。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| no-target | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| negated | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| target-invalidated | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| duplicate-submit | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| reconnect | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 0 | 2（S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `no-target`（无目标/不能发动）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `negated`（已支付后被无效）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `target-invalidated`（已声明对象逆结算失效）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `duplicate-submit`（重复或过期提交）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-06D1:ability:static:b173428fa383ae26、S02-06M2:ability:rule:f86cd3914a10b001

展示消费者出口：档案未声明展示边界。

## rule:trial-value（未完成）

绑定能力段：8。运行入口：advance-core = L12GameEngine.AdvanceTrialCore；button = L12GameEngine.BuildAbilityViews；commit = L12GameEngine.TryCommitTrialAdvanceActivation；completion = L12GameEngine.CompleteTrialRuleAction；printed-identity = L12StructuredCardRules.IsTrialLegion；settlement = L12GameEngine.ResolveUsualTrialAdvance。
档案附加检查：trial-value-matches-card-data, summon-round-lock, completion-flip, st06-no-printed-segment。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 8（S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125） | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 8（S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125） | 0 |
| duplicate-submit | 0 | 0 | 8 |
| reconnect | 0 | 8（S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125） | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 0 | 8（S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125
- `target-invalidated`（已声明对象逆结算失效）：S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0604:ability:trial:2117897dcefd3125、S02-0606:ability:trial:bb29c925c9fcdc82、S02-0609:ability:trial:bb29c925c9fcdc82、S02-0610:ability:trial:bb29c925c9fcdc82、S02-0613:ability:trial:bb29c925c9fcdc82、S02-0614:ability:trial:bb29c925c9fcdc82、S02-0617:ability:trial:bb29c925c9fcdc82、S02-0618:ability:trial:2117897dcefd3125

展示消费者出口：档案未声明展示边界。

## rule:universal-faction-mapping（未完成）

绑定能力段：1。运行入口：effective-faction = L12StructuredCardRules.EffectiveFaction；effective-traits = L12StructuredCardRules.EffectiveTraits；faction-check = L12StructuredCardRules.HasFaction。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-0008:ability:continuous:766cca673a9815ad） | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 0 | 1（S02-0008:ability:continuous:766cca673a9815ad） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-0008:ability:continuous:766cca673a9815ad） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0008:ability:continuous:766cca673a9815ad
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0008:ability:continuous:766cca673a9815ad
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0008:ability:continuous:766cca673a9815ad

展示消费者出口：档案未声明展示边界。

## rule:valkyrie-draw-phase（未完成）

绑定能力段：1。运行入口：turn-start = L12GameEngine.ContinueAutomaticTurnStart。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| no-target | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| negated | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| target-invalidated | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| duplicate-submit | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| reconnect | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| payment-cancel | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S01-03M1:ability:static:f1ba346550e4decc） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S01-03M1:ability:static:f1ba346550e4decc
- `no-target`（无目标/不能发动）：S01-03M1:ability:static:f1ba346550e4decc
- `negated`（已支付后被无效）：S01-03M1:ability:static:f1ba346550e4decc
- `target-invalidated`（已声明对象逆结算失效）：S01-03M1:ability:static:f1ba346550e4decc
- `duplicate-submit`（重复或过期提交）：S01-03M1:ability:static:f1ba346550e4decc
- `reconnect`（Prompt/堆叠/选择阶段重连）：S01-03M1:ability:static:f1ba346550e4decc
- `payment-cancel`（有费用时的取消/支付失败兜底）：S01-03M1:ability:static:f1ba346550e4decc
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S01-03M1:ability:static:f1ba346550e4decc

展示消费者出口：档案未声明展示边界。

## summon-flow:promotion-entry（未完成）

绑定能力段：4。运行入口：commit = L12GameEngine.PlayS2Promotion；cost-calculation = L12GameEngine.S2PromotionGodPowerCost；entry = L12GameEngine.BeginS2PromotionEntry；foundation-candidates = L12GameEngine.S2PromotionFoundations；foundation-detach = L12GameEngine.DetachPromotionFoundations；identity = L12GameEngine.IsS2PromotionCard；options = L12GameEngine.BuildS2PromotionOptions；state-inheritance = L12S2ZoneOps.InheritPromotionState。
档案附加检查：payment-cancel, foundation-invalidated, god-power-consume-and-flip, promotion-discount, single-candidate-choice。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |
| no-target | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |
| payment-cancel | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |
| single-candidate-choice | 4 | 0 | 0 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 0 | 4（S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824
- `no-target`（无目标/不能发动）：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824
- `target-invalidated`（已声明对象逆结算失效）：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824
- `payment-cancel`（有费用时的取消/支付失败兜底）：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-0501:ability:promotion:3eb467465ef47272、S02-0503:ability:promotion:3eb467465ef47272、S02-0505:ability:promotion:e890e8664470e824、S02-0507:ability:promotion:e890e8664470e824

展示消费者出口：档案未声明展示边界。

## turn-start:avalon（未完成）

绑定能力段：1。运行入口：queue = L12GameEngine.QueueAvalonTurnStart；settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| no-target | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| negated | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| target-invalidated | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| duplicate-submit | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| reconnect | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 0 | 1（S02-06D1:ability:turn-start:97dca04b36fe51bf） | 0 |

缺失明细与建议补测范围：
- `normal`（正常结算）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `no-target`（无目标/不能发动）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `negated`（已支付后被无效）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `target-invalidated`（已声明对象逆结算失效）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `duplicate-submit`（重复或过期提交）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `reconnect`（Prompt/堆叠/选择阶段重连）：S02-06D1:ability:turn-start:97dca04b36fe51bf
- `presentation-consumers`（展示消费者覆盖（按钮/弹框/动效/日志/战报/回放））：S02-06D1:ability:turn-start:97dca04b36fe51bf

展示消费者出口：档案未声明展示边界。

