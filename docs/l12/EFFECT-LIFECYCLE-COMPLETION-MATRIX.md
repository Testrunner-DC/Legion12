# 生命周期完成矩阵（自动基线）

由 `scripts/export-l12-effect-lifecycle-inventory.ps1` 随台账同次生成；不要手工修改此表。
只认精确能力 ID 绑定的具名证据 scope（完全匹配或 `矩阵项-子项` 前缀形式）；不适用必须给出理由；归属、运行入口与测试证据分别展示。
能力段分母：686（含未归属段；档案外段不构成完成证据）。档案：78，已完成：78，未完成：0。
内容指纹：`f423ea75d0bbc10f8edbdc730488211eb364b464c34394cee7aff56079e42e79`。

| 定义证据分桶 | 能力数 |
| --- | ---: |
| composite-definition | 194 |
| fine-definition | 84 |
| shared-rule-owner | 408 |

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

## active:paid-extended-range（已完成）

绑定能力段：2。运行入口：activation-eligibility = L12GameEngine.ExtendedRangeSourceUnavailableReason；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；cost-commit = L12GameEngine.TryCommitS1ExtendedActiveAbility；definition = L12StructuredCardSemantics.ExtendedRangeRule；expiry = L12GameEngine.ResetTemporaryCardState；presentation = L12GameEngine.ResolveEffectPresentationSceneId；response-stack = L12GameEngine.PushEffect；settlement = L12GameEngine.TryResolveS1ExtendedActive。
档案附加检查：source-invalidated, payment-cancel, paid-cost-preserved, repeat-activation, turn-end-expiry, authoritative-attack。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 2 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## after-kill:printed-piercing（已完成）

绑定能力段：2。运行入口：generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：original-combat-kill-only, no-attack-trigger-on-generated, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## attack:troops-set-on-attack（已完成）

绑定能力段：2。运行入口：attack-settlement = L12GameEngine.Attack；definition = L12StructuredCardRules.CombatProfile；post-attack-revert = L12GameEngine.RevertPendingCombatTroopsModifiers。
档案附加检查：row-condition-current, set-value-parameter, post-attack-revert, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## composite:counter-deployment（已完成）

绑定能力段：2。运行入口：candidate-generation = L12GameEngine.IsCounterDeploymentCandidate；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settlement-revalidation = L12GameEngine.SetDeclaredCounterTactics；slot-declaration = L12GameEngine.CreateActivationStepPrompt。
档案附加检查：private-hand-redaction, independent-target-settlement, slot-invalidated。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 2 | 0 | 0 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## composite:desert-hand-summon（已完成）

绑定能力段：1。运行入口：candidate-generation = L12GameEngine.IsDesertHandSummonCandidate；cost-commit = L12GameEngine.TryCommitCompositePreStackCosts；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settlement-revalidation = L12GameEngine.TryResolveS2FactionTactic。
档案附加检查：cost-prepaid, settlement-slot-invalidated, single-candidate-choice。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 1 | 0 | 0 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 1 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## continuous:cooperative-support（已完成）

绑定能力段：1。运行入口：definition = L12StructuredCardRules.HasCooperativeSupport；support-candidates = L12GameEngine.HasLegalLegionSupport；support-validation = L12GameEngine.ValidateDefenseChoice。
档案附加检查：row-condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:duel-combat-line（已完成）

绑定能力段：1。运行入口：attack = L12GameEngine.Attack；attack-revalidation = L12GameEngine.TryValidateAttackTarget；combat-settlement = L12GameEngine.ResolveDefenseCore；condition-and-active-state = L12StructuredCardRules.CombatProfile；disaster-target = L12GameEngine.HasMandatoryDisasterLegionTarget。
档案附加检查：structured-split-siblings, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:field-morale-resource（已完成）

绑定能力段：1。运行入口：automatic-payment = L12GameEngine.TryConsumeMorale；candidate-generation = L12GameEngine.SpendableFieldMoraleResources；composite-reservation = L12GameEngine.CompositeOrdinaryPaymentChoices；definition = L12StructuredCardSemantics.FieldMoraleResourceRule；effect-payment-retry = L12GameEngine.ContinueEffectMoralePayment；manual-payment = L12GameEngine.CreateResourcePaymentPrompt；paid-cost-presentation = L12GameEngine.AddPaidCostPresentationFromSnapshot；rejected-submit-rollback = L12GameEngine.RestoreActiveResourceRollback；selected-payment-commit = L12GameEngine.TryConsumeSelectedResources；selected-payment-revalidation = L12GameEngine.CanConsumeSelectedResources；snapshot-count = L12GameEngine.ActiveResourceCount；snapshot-projection = L12GameEngine.SnapshotField。
档案附加检查：exact-card-family, controller-turn, opponent-turn, front-row, back-row, current-controller, active-only, hidden-or-non-legion, mixed-payment, reservation, effect-payment-cancel, stale-effect-payment-retry, rejected-active-rollback, paid-cost-presentation, stale-resource, duplicate-submit, reconnect-derived-state, authoritative-snapshot-projection。

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
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：AddPaidCostPresentationFromSnapshot。

## continuous:front-row-keyword-grant（已完成）

绑定能力段：1。运行入口：active-state = L12StructuredCardRules.HasTaunt；condition = L12StructuredCardRules.ConditionMatches；definition = L12StructuredCardRules.GetCombatRuleAbilities；grant-chain = L12StructuredCardRules.AbilityGrantsKeyword；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：row-condition-current, ability-ref-chain, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## continuous:front-row-keyword-troops（已完成）

绑定能力段：6。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；condition-and-active-state = L12StructuredCardRules.ConditionMatches；definition = L12StructuredCardRules.GetCombatRuleAbilities；keyword-grant-chain = L12StructuredCardRules.HasTaunt；presentation = L12GameEngine.BuildActiveKeywords；troops-bonus-definition = L12StructuredCardRules.OpponentTurnFrontTroopsBonus；troops-consumer = L12GameEngine.RecalculateContinuousTroops。
档案附加检查：row-condition-current, opponent-turn-troops, ability-ref-chain, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 6 | 0 | 0 |
| no-target | 0 | 0 | 6 |
| negated | 0 | 0 | 6 |
| target-invalidated | 0 | 0 | 6 |
| duplicate-submit | 0 | 0 | 6 |
| reconnect | 6 | 0 | 0 |
| payment-cancel | 0 | 0 | 6 |
| single-candidate-choice | 0 | 0 | 6 |
| multi-target-applicability | 0 | 0 | 6 |
| presentation-consumers | 6 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## continuous:front-row-taunt-overlay（已完成）

绑定能力段：4。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；condition-and-active-state = L12StructuredCardRules.HasTaunt；definition = L12StructuredCardRules.GetCombatOverlayAbilities；master-attack-rule = L12GameEngine.CanAttackMasterTarget；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：row-condition-current, authoritative-consumer, closed-overlay-card-set。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 0 | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 0 | 4 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 4 | 0 | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 4 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## continuous:kings-sword-attached（已完成）

绑定能力段：1。运行入口：strong-attack = L12StructuredCardSemantics.GrantsStrongAttackWhileAttached；troops = L12GameEngine.GetTurnAndPositionContinuousTroops。
档案附加检查：attached-source-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:opponent-turn-field-rule（已完成）

绑定能力段：1。运行入口：authoritative-recalculation = L12GameEngine.RecalculateContinuousTroops；cost-derivation = L12StructuredCardRules.OpponentTurnCostModifier；definition = L12StructuredCardSemantics.OpponentTurnFieldRule；front-troops-derivation = L12StructuredCardRules.OpponentTurnFrontTroopsBonus；public-projection = L12GameEngine.SnapshotField。
档案附加检查：exact-card-family, opponent-turn, controller-turn, front-row, back-row, current-controller, cost-and-troops-same-definition, leave-reset, reconnect-idempotence。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:out-of-deck-graveyard-lifecycle（已完成）

绑定能力段：2。运行入口：authoritative-departure = L12GameEngine.MoveFieldCardToZone；deck-size-rule = L12SpecialDeckRules.DoesNotCountTowardMainDeck；definition = L12StructuredCardSemantics.HasOutOfDeckGraveyardLifecycle；departure-replacement = L12SpecialDeckRules.AlwaysReturnsToOwnerGraveyard；hand-library-replacement = L12SpecialDeckRules.CannotEnterHandOrLibrary；opening-zone-rule = L12SpecialDeckRules.StartsInGraveyard。
档案附加检查：exact-card-family, text-independent, deck-count, opening-graveyard, hand-filter, library-filter, owner-graveyard, all-departure-destinations, derived-card-precedence, controller-owner-split, reconnect-authoritative-zone。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:printed-range（已完成）

绑定能力段：47。运行入口：candidate-generation = L12GameEngine.BuildLegalAttackTargets；combat-declaration = L12GameEngine.Attack；condition-and-permission = L12StructuredCardRules.CombatProfile；damage-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.GetCombatRuleAbilities；presentation = L12GameEngine.SnapshotFor；source-row = L12GameEngine.CanAttackFromRow；target-revalidation = L12GameEngine.TryValidateAttackTarget。
档案附加检查：source-row-change, attack-preview, ranged-no-loss, profession-grant。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 47 | 0 | 0 |
| no-target | 47 | 0 | 0 |
| negated | 0 | 0 | 47 |
| target-invalidated | 0 | 0 | 47 |
| duplicate-submit | 0 | 0 | 47 |
| reconnect | 47 | 0 | 0 |
| payment-cancel | 0 | 0 | 47 |
| single-candidate-choice | 0 | 0 | 47 |
| multi-target-applicability | 0 | 0 | 47 |
| presentation-consumers | 47 | 0 | 0 |

展示消费者出口：SnapshotFor。

## continuous:ramses-protection-and-entry-cost（已完成）

绑定能力段：1。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；current-round-condition = L12StructuredCardRules.HasSummonTurnCounterTacticProtection；definition = L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection；delegated-entry-inheritance = L12GameEngine.ResolveBatch6JAEnterEffect；entry-cost-calculation = L12GameEngine.PrintedEntryCostModifier；entry-cost-definition = L12StructuredCardSemantics.PrintedEntryCostRule；legacy-delegated-entry-inheritance = L12GameEngine.TryContinueS1Faction；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice；response-candidate-and-submit = L12GameEngine.IsProtectedFromCounterTactics。
档案附加检查：summon-round, four-response-types, delegated-entry, expiry, anonymous-availability, reconnect-derived-state, entry-cost-condition-false, entry-cost-display-and-payment-parity, entry-cost-zero-floor。

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
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:relic-zone-limit-exempt（已完成）

绑定能力段：5。运行入口：artifact-zone-placement = L12GameEngine.PlaceArtifactInRelicZone；definition = L12StructuredCardSemantics.IgnoresRelicZoneLimit。
档案附加检查：exact-card-family, artifact-only, primary-empty, primary-occupied, ordinary-artifact-replaces, hand-play, effect-generated-play, zhuge-generated-play, gm-play, reconnect-zone-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 5 | 0 | 0 |
| no-target | 0 | 0 | 5 |
| negated | 0 | 0 | 5 |
| target-invalidated | 0 | 0 | 5 |
| duplicate-submit | 5 | 0 | 0 |
| reconnect | 5 | 0 | 0 |
| payment-cancel | 0 | 0 | 5 |
| single-candidate-choice | 0 | 0 | 5 |
| multi-target-applicability | 0 | 0 | 5 |
| presentation-consumers | 5 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:rested-free-front-back-move（已完成）

绑定能力段：1。运行入口：move-command = L12GameEngine.Move。
档案附加检查：source-rested-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:simple-troops-rule（已完成）

绑定能力段：4。运行入口：continuous-recalc = L12GameEngine.RecalculateContinuousTroops；turn-and-position-bonus = L12GameEngine.GetTurnAndPositionContinuousTroops。
档案附加检查：shared-recalc-outlet, condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 0 | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 0 | 4 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 4 | 0 | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 4 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:structured-combat-rule（已完成）

绑定能力段：16。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；combat-settlement = L12GameEngine.ResolveDefenseCore；condition-and-active-state = L12StructuredCardRules.CombatProfile；definition = L12StructuredCardRules.GetCombatRuleAbilities；master-protection = L12StructuredCardRules.ProtectsMasterFromTroops；presentation = L12GameEngine.SnapshotFor；support-source-revalidation = L12StructuredCardRules.CannotSupport；support-target-revalidation = L12StructuredCardRules.CannotReceiveBackRowSupport；trial-protection = L12StructuredCardRules.ProtectsActiveTrialLegions。
档案附加检查：row-and-ready-condition, source-current-type, candidate-and-submit-parity, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 16 | 0 | 0 |
| no-target | 0 | 0 | 16 |
| negated | 0 | 0 | 16 |
| target-invalidated | 0 | 0 | 16 |
| duplicate-submit | 0 | 0 | 16 |
| reconnect | 16 | 0 | 0 |
| payment-cancel | 0 | 0 | 16 |
| single-candidate-choice | 0 | 0 | 16 |
| multi-target-applicability | 0 | 0 | 16 |
| presentation-consumers | 16 | 0 | 0 |

展示消费者出口：SnapshotFor。

## continuous:summon-turn-counter-protection（已完成）

绑定能力段：2。运行入口：current-round-condition = L12StructuredCardRules.HasSummonTurnCounterTacticProtection；definition = L12StructuredCardSemantics.HasSummonTurnCounterTacticProtection；delegated-entry-inheritance = L12GameEngine.ResolveBatch6JAEnterEffect；legacy-delegated-entry-inheritance = L12GameEngine.TryContinueS1Faction；response-candidate-and-submit = L12GameEngine.IsProtectedFromCounterTactics。
档案附加检查：summon-round, four-response-types, delegated-entry, expiry, anonymous-availability, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## continuous:tomb-guard-master-aura（已完成）

绑定能力段：1。运行入口：current-cost = L12GameEngine.RecalculateContinuousTroops；current-troops = L12GameEngine.GetTurnAndPositionContinuousTroops；definition = L12StructuredCardSemantics.MasterFieldAuraRule；presentation = L12GameEngine.SnapshotField。
档案附加检查：field-only, cost-and-troops-same-definition, current-controller, current-cost-consumers, presentation-consumers, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：SnapshotField。

## cost:active-rest（已完成）

绑定能力段：27。运行入口：button-eligibility = L12GameEngine.BuildAbilityViews；cost-commit = L12GameEngine.CommitStructuredActiveRestCost；cost-presentation = L12GameEngine.AddActivePaidCostPresentation；response-stack = L12GameEngine.PushEffect；runtime-identity = L12StructuredCardRules.IsActiveRestAbility。
档案附加检查：active-rest-cost, paid-cost-preserved, readied-source-reuse, runtime-branch-mapping。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 27 | 0 | 0 |
| no-target | 0 | 0 | 27 |
| negated | 27 | 0 | 0 |
| target-invalidated | 0 | 0 | 27 |
| duplicate-submit | 27 | 0 | 0 |
| reconnect | 27 | 0 | 0 |
| payment-cancel | 0 | 0 | 27 |
| single-candidate-choice | 0 | 0 | 27 |
| multi-target-applicability | 0 | 0 | 27 |
| presentation-consumers | 27 | 0 | 0 |

展示消费者出口：AddActivePaidCostPresentation。

## death:immortal-replacement（已完成）

绑定能力段：2。运行入口：active-state = L12GameEngine.HasActiveImmortal；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField。
档案附加检查：single-use, troops-set-to-1000, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## declaration:front-row-composite-line（已完成）

绑定能力段：3。运行入口：entry-cost = L12GameEngine.PrintedEntryCostModifier；taunt = L12StructuredCardRules.HasTaunt；troops = L12StructuredCardRules.OpponentTurnFrontTroopsBonus。
档案附加检查：row-condition-current, authoritative-consumer, structured-split-siblings。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## declaration:saladin-line（已完成）

绑定能力段：1。运行入口：attack-passive = L12GameEngine.ApplyS1FactionAttackPassives；move = L12GameEngine.CavalryMove。
档案附加检查：composite-line-declaration, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## disaster:continuous-rule（已完成）

绑定能力段：14。运行入口：attack-legion-validation = L12GameEngine.TryValidateAttackTarget；attack-master-validation = L12GameEngine.CanAttackMasterTarget；disaster-value = L12GameEngine.SetDisasterValue；effect-hook = L12GameEngine.PushEffect；hand-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；main-phase-effect = L12GameEngine.BeginMainPhaseDisasterEffect；master-ability-quote = L12GameEngine.QuoteActiveMorale；placement-and-movement = L12GameEngine.PlayCard；presentation = L12GameEngine.SnapshotFor；rule-registry = L12ActiveDisasterRules.HasRegisteredContinuousRule。
档案附加检查：registry-closed-set, condition-current, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 14 | 0 | 0 |
| no-target | 0 | 0 | 14 |
| negated | 0 | 0 | 14 |
| target-invalidated | 0 | 0 | 14 |
| duplicate-submit | 0 | 0 | 14 |
| reconnect | 14 | 0 | 0 |
| payment-cancel | 0 | 0 | 14 |
| single-candidate-choice | 0 | 0 | 14 |
| multi-target-applicability | 0 | 0 | 14 |
| presentation-consumers | 14 | 0 | 0 |

展示消费者出口：SnapshotFor。

## granted-static:front-row-taunt-on-kill（已完成）

绑定能力段：1。运行入口：active-state = L12StructuredCardRules.HasTaunt；grant = L12GameEngine.GrantTauntUntilNextOwnTurnEnd；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, front-row-required, turn-expiry, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## granted:constance-modes（已完成）

绑定能力段：1。运行入口：presentation = L12GameEngine.ResolveEffectPresentationSceneId；settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 1 | 0 | 0 |
| negated | 1 | 0 | 0 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## granted:gain-rune（已完成）

绑定能力段：2。运行入口：presentation = L12GameEngine.ResolveEffectPresentationSceneId；settlement = L12S2ZoneOps.GainRunes。
档案附加检查：parent-grant-boundary, rune-cap, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## granted:lancelot-kill-modes（已完成）

绑定能力段：1。运行入口：presentation = L12GameEngine.ResolveEffectPresentationSceneId；settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 1 | 0 | 0 |
| negated | 1 | 0 | 0 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## granted:prayer-modes（已完成）

绑定能力段：2。运行入口：presentation = L12GameEngine.SnapshotFor；preview = L12GameEngine.BeginPrayerPublicPreview；private-preview = L12GameEngine.BeginPrayerPrivatePreview；settle = L12GameEngine.TryResolveS2UniversalTactic。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 2 | 0 | 0 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 1 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：SnapshotFor。

## granted:ruined-ritual-modes（已完成）

绑定能力段：2。运行入口：presentation = L12GameEngine.SnapshotFor；response-commit = L12GameEngine.CommitS2CounterResponse；settle = L12GameEngine.ResolveS2CounterEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 2 | 0 | 0 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：SnapshotFor。

## granted:tenka-modes（已完成）

绑定能力段：3。运行入口：attack-bonus = L12GameEngine.Attack；free-move = L12GameEngine.Move；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settle = L12GameEngine.TryResolveS2FactionTactic。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 3 | 0 | 0 |
| negated | 3 | 0 | 0 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 3 | 0 | 0 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## hand-play:artifact-block（已完成）

绑定能力段：2。运行入口：authoritative-submit = L12GameEngine.PlayCard；button-and-snapshot = L12GameEngine.SnapshotHand；condition-and-reason = L12StructuredCardRules.HandPlayBlockReason；definition = L12StructuredCardSemantics.HandPlayBlockRule。
档案附加检查：source-zone, same-card-exception, priority, non-artifact-unaffected, display-and-submit-parity, reconnect-derived-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## hand-play:printed-entry-cost-condition（已完成）

绑定能力段：8。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；condition-and-calculation = L12GameEngine.PrintedEntryCostModifier；definition = L12StructuredCardSemantics.PrintedEntryCostRule；presentation = L12GameEngine.SnapshotHand；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice。
档案附加检查：condition-false, zero-floor, payment-cancel, reconnect-payment, display-and-payment-parity。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 8 | 0 | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 8 | 0 | 0 |
| reconnect | 8 | 0 | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 8 | 0 | 0 |

展示消费者出口：SnapshotHand。

## hand-play:self-damage-entry-discount（已完成）

绑定能力段：6。运行入口：cost-calculation = L12GameEngine.GetPlayCostWithSigurdDiscount；declaration-and-choice = L12GameEngine.PlayCard；definition = L12StructuredCardRules.SelfDamageEntryDiscount；presentation = L12GameEngine.SnapshotHand；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice；self-damage-payment = L12GameEngine.PayMasterDamageCostAndCanContinue。
档案附加检查：optional-choice, payment-cancel, last-health-terminal, reconnect-payment, card-remains-on-lethal-cost。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 6 | 0 | 0 |
| no-target | 0 | 0 | 6 |
| negated | 0 | 0 | 6 |
| target-invalidated | 0 | 0 | 6 |
| duplicate-submit | 6 | 0 | 0 |
| reconnect | 6 | 0 | 0 |
| payment-cancel | 6 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 6 |
| multi-target-applicability | 0 | 0 | 6 |
| presentation-consumers | 6 | 0 | 0 |

展示消费者出口：SnapshotHand。

## hand-play:structured-hand-condition-cost（已完成）

绑定能力段：10。运行入口：button-and-snapshot = L12GameEngine.SnapshotHand；combined-play-cost = L12GameEngine.GetPlayCostWithSigurdDiscount；condition-and-calculation = L12StructuredCardRules.HandPlayCostModifier；definition = L12StructuredCardRules.TryGetStructuredAbilities；resource-payment = L12GameEngine.EnsurePlayResourcePaymentChoice。
档案附加检查：condition-false, source-still-in-hand, effective-faction, zero-floor, payment-cancel, reconnect-payment, display-and-payment-parity。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 10 | 0 | 0 |
| no-target | 0 | 0 | 10 |
| negated | 0 | 0 | 10 |
| target-invalidated | 0 | 0 | 10 |
| duplicate-submit | 10 | 0 | 0 |
| reconnect | 10 | 0 | 0 |
| payment-cancel | 0 | 0 | 10 |
| single-candidate-choice | 0 | 0 | 10 |
| multi-target-applicability | 0 | 0 | 10 |
| presentation-consumers | 10 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## keyword-granted:charge（已完成）

绑定能力段：4。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasPrintedKeywordReference；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 0 | 0 |
| no-target | 0 | 0 | 4 |
| negated | 0 | 0 | 4 |
| target-invalidated | 0 | 0 | 4 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 4 | 0 | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 0 | 0 | 4 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 4 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword-granted:death-immunity（已完成）

绑定能力段：1。运行入口：active-state = L12GameEngine.HasActiveImmortal；definition = L12StructuredCardRules.HasPrintedKeywordReference；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ExpireEffectsAtPlayerTurnStart。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword-granted:must-hit（已完成）

绑定能力段：1。运行入口：attack-declaration = L12GameEngine.Attack；defense-submit = L12GameEngine.ValidateDefenseChoice；definition = L12StructuredCardRules.HasPrintedKeywordReference；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword-granted:piercing（已完成）

绑定能力段：1。运行入口：combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasPrintedKeywordReference；generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；master-target-revalidation = L12GameEngine.CanAttackMasterTarget；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## keyword-granted:taunt（已完成）

绑定能力段：2。运行入口：active-state = L12StructuredCardRules.HasTaunt；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasPrintedKeywordReference；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword:charge（已完成）

绑定能力段：3。运行入口：attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasKeywordDefinition；leave-reset = L12GameEngine.ResetCardAfterLeavingField；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword:death-immunity（已完成）

绑定能力段：2。运行入口：active-state = L12GameEngine.HasActiveImmortal；definition = L12StructuredCardRules.HasKeywordDefinition；grant = L12GameEngine.GrantImmortalUntilNextTurnStart；lethal-replacement = L12GameEngine.RemoveFromField；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ExpireEffectsAtPlayerTurnStart。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword:piercing（已完成）

绑定能力段：2。运行入口：combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasKeywordDefinition；generated-attack = L12GameEngine.BeginPiercingAttack；kill-fact-gate = L12GameEngine.ResolveTypedKillSourceEvent；master-target-revalidation = L12GameEngine.CanAttackMasterTarget；printed-identity = L12StructuredCardRules.HasPrintedKeywordReference；printed-settlement = L12GameEngine.TryResolveS2FactionAfterAttack。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## keyword:shock（已完成）

绑定能力段：2。运行入口：attack-trigger = L12GameEngine.ApplyS2Shock；combat-settlement = L12GameEngine.ResolveDefenseCore；definition = L12StructuredCardRules.HasKeywordDefinition；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ResetTemporaryCardState。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword:strong-attack（已完成）

绑定能力段：2。运行入口：active-state = L12StructuredCardSemantics.HasEffectiveStrongAttack；combat-settlement = L12GameEngine.Attack；definition = L12StructuredCardRules.HasKeywordDefinition；grant = L12GameEngine.GrantStrongAttack；presentation = L12GameEngine.BuildActiveKeywords；turn-expiry = L12GameEngine.ResetTemporaryCardState。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## keyword:taunt（已完成）

绑定能力段：8。运行入口：active-state = L12StructuredCardRules.HasTaunt；attack-candidates = L12GameEngine.BuildLegalAttackTargets；attack-revalidation = L12GameEngine.TryValidateAttackTarget；definition = L12StructuredCardRules.HasKeywordDefinition；presentation = L12GameEngine.BuildActiveKeywords。
档案附加检查：parent-grant-boundary, authoritative-consumer, leave-or-turn-expiry, reconnect-state。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 8 | 0 | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 0 | 0 | 8 |
| reconnect | 8 | 0 | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 8 | 0 | 0 |

展示消费者出口：BuildActiveKeywords。

## leave:attached-tactics-discard（已完成）

绑定能力段：1。运行入口：discard = L12GameEngine.DiscardAttachedCards。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## leave:master-legion-return（已完成）

绑定能力段：1。运行入口：departure = L12GameEngine.CompleteMasterLegionDeparture；morale-trigger = L12GameEngine.TryResolveSimpleResourceTrigger。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## lethal-replacement:offer-pipeline（已完成）

绑定能力段：3。运行入口：apply = L12GameEngine.TryApplyCardLethalSubstitution；candidates = L12GameEngine.CardLethalSubstitutionCandidates；eligibility = L12GameEngine.CanUseAchillesLethalReplacement；offer = L12GameEngine.TryOfferEffectLethalReplacement；presentation = L12GameEngine.SnapshotFor；substitution-kind = L12GameEngine.CardLethalSubstitutionKind。
档案附加检查：payment-cancel, once-per-turn, front-row-required, recovery-resume, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 1 | 0 | 2 |
| duplicate-submit | 3 | 0 | 0 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 2 | 0 | 1 |
| single-candidate-choice | 1 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：SnapshotFor。

## morale:active-effect-pipeline（已完成）

绑定能力段：14。运行入口：button = L12GameEngine.FactionEffectSnapshot；commit = L12GameEngine.CommitActiveAbilityCore；cost-table = L12GameEngine.GetActiveAbilityMoraleCost；eligibility = L12GameEngine.ActiveAbilityUnavailableReason；identity-normalization = L12MoraleIdentityCatalog.CanonicalEffectCardId；presentation = L12GameEngine.SnapshotFor；settlement-dispatch = L12GameEngine.ResolveActiveEffect；usage-rule = L12ActiveUsageRules.Find。
档案附加检查：canonical-version-parity, once-per-turn, morale-cost-table, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 14 | 0 | 0 |
| no-target | 14 | 0 | 0 |
| negated | 14 | 0 | 0 |
| target-invalidated | 2 | 0 | 12 |
| duplicate-submit | 14 | 0 | 0 |
| reconnect | 14 | 0 | 0 |
| payment-cancel | 12 | 0 | 2 |
| single-candidate-choice | 2 | 0 | 12 |
| multi-target-applicability | 0 | 0 | 14 |
| presentation-consumers | 14 | 0 | 0 |

展示消费者出口：SnapshotFor。

## morale:resource-identity（已完成）

绑定能力段：3。运行入口：manual-selection = L12GameEngine.CanConsumeSelectedResources；payment = L12GameEngine.TryConsumeMorale；presentation = L12GameEngine.SnapshotMorale；resource-count = L12GameEngine.ActiveResourceCount。
档案附加检查：counts-as-morale-structural, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 0 | 0 | 3 |
| negated | 0 | 0 | 3 |
| target-invalidated | 0 | 0 | 3 |
| duplicate-submit | 0 | 0 | 3 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 0 | 0 | 3 |
| single-candidate-choice | 0 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：SnapshotMorale。

## pipeline:active-effect（已完成）

绑定能力段：45。运行入口：begin = L12GameEngine.BeginActiveAbility；commit = L12GameEngine.CommitActiveAbilityCore；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settle = L12GameEngine.ResolveActiveEffect；stack = L12GameEngine.PushEffect；usage = L12ActiveUsageRules.Find；views = L12GameEngine.BuildAbilityViews。
档案附加检查：per-card-branch, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 45 | 0 | 0 |
| no-target | 45 | 0 | 0 |
| negated | 45 | 0 | 0 |
| target-invalidated | 26 | 0 | 19 |
| duplicate-submit | 45 | 0 | 0 |
| reconnect | 45 | 0 | 0 |
| payment-cancel | 35 | 0 | 10 |
| single-candidate-choice | 26 | 0 | 19 |
| multi-target-applicability | 1 | 0 | 44 |
| presentation-consumers | 45 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## pipeline:disaster-authority（已完成）

绑定能力段：4。运行入口：damage = L12GameEngine.DamageMasterNonLethalFromDisaster；presentation = L12GameEngine.SnapshotFor；settle = L12GameEngine.ResolveDisasterEffect；trigger = L12GameEngine.BeginDisasterTrigger；turn-end = L12GameEngine.ResolveEndPhaseDisasterEffect；turn-start = L12GameEngine.ResolveTurnStartDisasterEffectIfNeeded。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 0 | 0 |
| no-target | 3 | 0 | 1 |
| negated | 0 | 0 | 4 |
| target-invalidated | 1 | 0 | 3 |
| duplicate-submit | 2 | 0 | 2 |
| reconnect | 4 | 0 | 0 |
| payment-cancel | 0 | 0 | 4 |
| single-candidate-choice | 1 | 0 | 3 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 4 | 0 | 0 |

展示消费者出口：SnapshotFor。

## pipeline:hand-play（已完成）

绑定能力段：19。运行入口：composite-declaration = L12GameEngine.BeginCompositeHandPlayDeclaration；composite-validation = L12GameEngine.ValidateCompositeHandPlayDeclaration；cost = L12GameEngine.GetPlayCostWithSigurdDiscount；play = L12GameEngine.PlayCard；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settle = L12GameEngine.ResolveTacticEffect。
档案附加检查：per-card-flow, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 19 | 0 | 0 |
| no-target | 19 | 0 | 0 |
| negated | 19 | 0 | 0 |
| target-invalidated | 12 | 0 | 7 |
| duplicate-submit | 19 | 0 | 0 |
| reconnect | 19 | 0 | 0 |
| payment-cancel | 8 | 0 | 11 |
| single-candidate-choice | 12 | 0 | 7 |
| multi-target-applicability | 0 | 0 | 19 |
| presentation-consumers | 19 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## pipeline:public-trigger（已完成）

绑定能力段：39。运行入口：batch-plan = L12TriggerBatchPlanner.Plan；begin-declaration = L12GameEngine.TryBeginPublicTriggerDeclaration；candidates = L12GameEngine.QueueTriggerCandidates；complete-declaration = L12GameEngine.TryCompletePublicTriggerDeclaration；presentation = L12GameEngine.ResolveTriggeredEffectDisplayText；settle = L12GameEngine.ResolveTopStack。
档案附加检查：per-card-plan, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 39 | 0 | 0 |
| no-target | 39 | 0 | 0 |
| negated | 39 | 0 | 0 |
| target-invalidated | 9 | 0 | 30 |
| duplicate-submit | 39 | 0 | 0 |
| reconnect | 39 | 0 | 0 |
| payment-cancel | 12 | 0 | 27 |
| single-candidate-choice | 9 | 0 | 30 |
| multi-target-applicability | 0 | 0 | 39 |
| presentation-consumers | 39 | 0 | 0 |

展示消费者出口：ResolveTriggeredEffectDisplayText。

## pipeline:response（已完成）

绑定能力段：2。运行入口：candidates = L12GameEngine.LegalResponseSources；capability = L12StructuredCardSemantics.SpecialResponseCapability；pool-timing = L12GameEngine.IsPoolCounterResponseAtTiming；presentation = L12GameEngine.ResolveResponseEffectDisplayText；settle = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：capability-registry, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 2 | 0 | 0 |
| negated | 2 | 0 | 0 |
| target-invalidated | 1 | 0 | 1 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 1 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveResponseEffectDisplayText。

## private-zone:strict-hand-entry（已完成）

绑定能力段：3。运行入口：declaration = L12GameEngine.CreateActivationStepPrompt；dependent-continuation = L12GameEngine.QueueNextCompositeSegment；failed-settlement = L12GameEngine.RecordTargetSettlementFailure；presentation = L12GameEngine.SnapshotFor；settlement-revalidation = L12GameEngine.TrySummonFromHand；source-failure = L12GameEngine.RecordResolutionFailure。
档案附加检查：private-hand-redaction, settlement-slot-invalidated, stale-instance-no-replacement, then-requires-success。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 3 | 0 | 0 |
| no-target | 3 | 0 | 0 |
| negated | 3 | 0 | 0 |
| target-invalidated | 2 | 0 | 1 |
| duplicate-submit | 3 | 0 | 0 |
| reconnect | 3 | 0 | 0 |
| payment-cancel | 2 | 0 | 1 |
| single-candidate-choice | 2 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 3 |
| presentation-consumers | 3 | 0 | 0 |

展示消费者出口：SnapshotFor。

## reaction:hand-block（已完成）

绑定能力段：1。运行入口：candidates = L12GameEngine.LegalResponseSources；commit = L12GameEngine.CommitMercenaryResponse；pool-timing = L12GameEngine.CanMasterCardPoolRespondAtTiming；settlement = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：self-discard-cost, defender-only, capability-registry, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 1 | 0 | 0 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 1 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## reaction:negate-pipeline（已完成）

绑定能力段：2。运行入口：candidates = L12GameEngine.LegalResponseSources；commit = L12GameEngine.CommitNegateResponse；pool-timing = L12GameEngine.IsPoolCounterResponseAtTiming；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settlement = L12GameEngine.ResolveTopStack；submit = L12GameEngine.BeginSelectedStackResponse。
档案附加检查：payment-cancel, capability-registry, pool-parity, authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 1 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## replacement:anderstorp-damage-floor（已完成）

绑定能力段：1。运行入口：damage-floor = L12GameEngine.AdjustAnderstorpRingDamage。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## replacement:lake-lady-sword（已完成）

绑定能力段：2。运行入口：replacement = L12GameEngine.TryApplyLakeLadySwordReplacement。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## replacement:morale-zone-resource（已完成）

绑定能力段：1。运行入口：definition = L12StructuredCardSemantics.MoraleZoneResourceRule；payment-identity = L12GameEngine.OrdinaryPaymentSemanticKey；payment-prompt = L12GameEngine.CreateResourcePaymentPrompt；presentation-projection = L12GameEngine.SnapshotMorale；return-prompt = L12GameEngine.CreateReturnMoralePrompt；return-settlement = L12GameEngine.ReturnMoraleCardToDestination；snapshot-projection = L12GameEngine.SnapshotMorale。
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
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：SnapshotMorale。

## resource:morale-face-flip（已完成）

绑定能力段：11。运行入口：candidate-generation = L12GameEngine.CanFlipMoraleToGodPower；identity-definition = L12MoraleIdentityCatalog.CanUseGodPowerFace；presentation = L12GameEngine.SnapshotMorale；resolution-prompt = L12GameEngine.PromptS2FlipMorale；settlement-mutation = L12S2ZoneOps.FlipMoraleFace；toggle-candidate-generation = L12GameEngine.CanToggleMoraleFace。
档案附加检查：exact-printed-family, version-alias, black-lotus-excluded, candidate-settlement-parity, rested-only-filter, single-candidate-choice, multi-target-independent-revalidation, v2-prompt-reconnect。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 11 | 0 | 0 |
| no-target | 11 | 0 | 0 |
| negated | 11 | 0 | 0 |
| target-invalidated | 7 | 0 | 4 |
| duplicate-submit | 11 | 0 | 0 |
| reconnect | 11 | 0 | 0 |
| payment-cancel | 4 | 0 | 7 |
| single-candidate-choice | 7 | 0 | 4 |
| multi-target-applicability | 2 | 0 | 9 |
| presentation-consumers | 11 | 0 | 0 |

展示消费者出口：SnapshotMorale。

## rule-action:cavalry-move（已完成）

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
| presentation-consumers | 8 | 0 | 0 |

展示消费者出口：NativeCavalryMovePresentation。

## rule:game-setup（已完成）

绑定能力段：8。运行入口：hand-preparation = L12GameEngine.PrepareLibrariesAndHands；presentation = L12GameEngine.SnapshotFor；setup-defaults = L12GameEngine.BeginOptionalS2Setup。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 8 | 0 | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 2 | 0 | 6 |
| reconnect | 8 | 0 | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 8 | 0 | 0 |

展示消费者出口：SnapshotFor。

## rule:isis-setup（已完成）

绑定能力段：1。运行入口：setup = L12GameEngine.PrepareLibrariesAndHands。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## rule:once-per-turn-by-name（已完成）

绑定能力段：2。运行入口：usage-check = L12CardNameUsageRules.HasUsed；usage-commit = L12CardNameUsageRules.TryUse；usage-key = L12CardNameUsageRules.Key。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## rule:thor-hammer-master-gate（已完成）

绑定能力段：1。运行入口：button-projection = L12GameEngine.BuildAbilityViews；commit = L12GameEngine.TryCommitS2RemainingAbility；declaration = L12GameEngine.TryBeginS2RemainingAbility；master-gate = L12StructuredCardSemantics.MasterAbilityGate。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## rule:trial-capacity（已完成）

绑定能力段：2。运行入口：capacity = L12SpecialDeckRules.TrialCapacity；completed-setup = L12SpecialDeckRules.StartsTrialsCompleted；presentation = L12GameEngine.SnapshotFor；validator = L12DeckValidator.TryValidate。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 0 | 0 | 2 |
| negated | 0 | 0 | 2 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 0 | 0 | 2 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 0 | 0 | 2 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：SnapshotFor。

## rule:trial-value（已完成）

绑定能力段：8。运行入口：advance-core = L12GameEngine.AdvanceTrialCore；button = L12GameEngine.BuildAbilityViews；commit = L12GameEngine.TryCommitTrialAdvanceActivation；completion = L12GameEngine.CompleteTrialRuleAction；presentation = L12GameEngine.SnapshotFor；printed-identity = L12StructuredCardRules.IsTrialLegion；settlement = L12GameEngine.ResolveUsualTrialAdvance。
档案附加检查：trial-value-matches-card-data, summon-round-lock, completion-flip, st06-no-printed-segment。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 8 | 0 | 0 |
| no-target | 0 | 0 | 8 |
| negated | 0 | 0 | 8 |
| target-invalidated | 0 | 0 | 8 |
| duplicate-submit | 0 | 0 | 8 |
| reconnect | 8 | 0 | 0 |
| payment-cancel | 0 | 0 | 8 |
| single-candidate-choice | 0 | 0 | 8 |
| multi-target-applicability | 0 | 0 | 8 |
| presentation-consumers | 8 | 0 | 0 |

展示消费者出口：SnapshotFor。

## rule:universal-faction-mapping（已完成）

绑定能力段：1。运行入口：candidate-consumer = L12GameEngine.IsDesertHandSummonCandidate；effective-faction = L12StructuredCardRules.EffectiveFaction；effective-traits = L12StructuredCardRules.EffectiveTraits；faction-check = L12StructuredCardRules.HasFaction。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## rule:valkyrie-draw-phase（已完成）

绑定能力段：1。运行入口：turn-start = L12GameEngine.ContinueAutomaticTurnStart。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 0 | 0 | 1 |
| negated | 0 | 0 | 1 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 0 | 0 | 1 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：档案未声明展示边界。

## summon-flow:promotion-entry（已完成）

绑定能力段：4。运行入口：commit = L12GameEngine.PlayS2Promotion；cost-calculation = L12GameEngine.S2PromotionGodPowerCost；entry = L12GameEngine.BeginS2PromotionEntry；foundation-candidates = L12GameEngine.S2PromotionFoundations；foundation-detach = L12GameEngine.DetachPromotionFoundations；identity = L12GameEngine.IsS2PromotionCard；options = L12GameEngine.BuildS2PromotionOptions；presentation = L12GameEngine.SnapshotFor；state-inheritance = L12S2ZoneOps.InheritPromotionState。
档案附加检查：payment-cancel, foundation-invalidated, god-power-consume-and-flip, promotion-discount, single-candidate-choice。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 4 | 0 | 0 |
| no-target | 4 | 0 | 0 |
| negated | 0 | 0 | 4 |
| target-invalidated | 4 | 0 | 0 |
| duplicate-submit | 0 | 0 | 4 |
| reconnect | 4 | 0 | 0 |
| payment-cancel | 4 | 0 | 0 |
| single-candidate-choice | 4 | 0 | 0 |
| multi-target-applicability | 0 | 0 | 4 |
| presentation-consumers | 4 | 0 | 0 |

展示消费者出口：SnapshotFor。

## trigger:paid-self-state（已完成）

绑定能力段：2。运行入口：begin-declaration = L12GameEngine.TryBeginTrialAdvanceTriggerDeclaration；candidate = L12GameEngine.CreateTriggerCandidate；cost-commit = L12GameEngine.TryCompleteTrialAdvanceTriggerDeclaration；presentation = L12GameEngine.ResolveEffectPresentationSceneId；settlement = L12GameEngine.TryResolveTrialAdvanceEffect；source-failure = L12GameEngine.RecordResolutionFailure。
档案附加检查：paid-cost-preserved, source-invalidated, optional-decline。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 2 | 0 | 0 |
| no-target | 2 | 0 | 0 |
| negated | 2 | 0 | 0 |
| target-invalidated | 0 | 0 | 2 |
| duplicate-submit | 2 | 0 | 0 |
| reconnect | 2 | 0 | 0 |
| payment-cancel | 2 | 0 | 0 |
| single-candidate-choice | 0 | 0 | 2 |
| multi-target-applicability | 0 | 0 | 2 |
| presentation-consumers | 2 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

## turn-start:avalon（已完成）

绑定能力段：1。运行入口：presentation = L12GameEngine.ResolveEffectPresentationSceneId；queue = L12GameEngine.QueueAvalonTurnStart；settle = L12GameEngine.TryResolveTrialAdvanceEffect。
档案附加检查：authoritative-consumer。

| 矩阵项 | 已有具名证据 | 缺失 | 不适用（含理由） |
| --- | --- | --- | --- |
| normal | 1 | 0 | 0 |
| no-target | 1 | 0 | 0 |
| negated | 1 | 0 | 0 |
| target-invalidated | 0 | 0 | 1 |
| duplicate-submit | 1 | 0 | 0 |
| reconnect | 1 | 0 | 0 |
| payment-cancel | 0 | 0 | 1 |
| single-candidate-choice | 0 | 0 | 1 |
| multi-target-applicability | 0 | 0 | 1 |
| presentation-consumers | 1 | 0 | 0 |

展示消费者出口：ResolveEffectPresentationSceneId。

