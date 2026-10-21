import assert from 'node:assert/strict'
import { buildMobileEffectMatrix } from './lib/l12-mobile-effect-matrix.mjs'

const catalog = [{ id: 'A', nameZh: '测试军团', cardType: 'legion' }, { id: 'B', nameZh: '普通卡', cardType: 'morale' }]
const inventory = { Schema: 3, CardCount: 2, CardsWithoutAbilities: ['B'], Abilities: [{ CardId: 'A', Name: '测试军团', EntryEvidence: 'fine-definition', RouteCandidates: [], TestReferences: [], Profile: null,
  Definition: { AbilityId: 'A:ability', CardId: 'A', ExecutionModel: 'triggered', Trigger: 'enter', CostText: null, ResolutionText: '兵力增加', Text: '公开卡文',
    Atoms: [{ Order: 2, Stage: 'effect', Kind: 'operation.modify-troops', Parameters: { amount: '1000' } }, { Order: 1, Stage: 'condition', Kind: 'condition.expression', Parameters: { expression: '合法目标' } }],
    Presentations: [{ SceneId: 'branch-a', BranchLabel: '前排', RequiredChoices: { mode: 'front' } }, { SceneId: 'branch-b', BranchLabel: '后排', RequiredChoices: { mode: 'back' } }] } }] }
const input = JSON.stringify(inventory)
const build = value => buildMobileEffectMatrix(value, catalog, [{ kind: '', continuationExpression: 'dynamic-continuation', choiceModeInCall: 'runtime' }])
const matrix = build(inventory)
assert.equal(JSON.stringify(inventory), input)
assert.deepEqual(matrix.counts, { cards: 2, abilities: 1, noAbilityCards: 1, branches: 2, presentations: 2, expectedSemanticsPending: 0, fullChainPassesClaimed: 0 })
assert.equal(matrix.rows[0].profileId, null)
assert.equal(matrix.rows[0].evidenceLayers.staticOwnership.entryEvidence, 'fine-definition')
assert.equal(matrix.rows[0].evidenceLayers.authoritativeEngineTouchAckSettlement.status, 'EVIDENCE_NOT_YET_MAPPED')
assert.deepEqual(matrix.rows[0].orderedAtoms.map(atom => atom.order), [1, 2])
assert.equal(matrix.rows[0].branches[1].requiredChoices.mode, 'back')
assert.equal(matrix.promptCallSites[0].actualRuntimeChoicePrivacyMapping, 'DYNAMIC_FACTORY_REQUIRES_RUNTIME_MAPPING')
const changed = structuredClone(inventory)
changed.Abilities[0].Definition.Atoms[0].Parameters.amount = '2000'
assert.notEqual(build(changed).rows[0].semanticIndex, matrix.rows[0].semanticIndex, 'Same kind with different amount is not interchangeable')
changed.Abilities[0].Definition.Atoms[0].Parameters.amount = '1000'
changed.Abilities[0].Definition.Atoms[0].Stage = 'cost'
assert.notEqual(build(changed).rows[0].semanticIndex, matrix.rows[0].semanticIndex, 'Cost and effect stages remain distinct')
const duplicate = structuredClone(inventory); duplicate.Abilities.push(duplicate.Abilities[0])
assert.throws(() => build(duplicate), /unique/)
const omitted = structuredClone(inventory); omitted.CardsWithoutAbilities = []
assert.throws(() => build(omitted), /denominator/)
const ownerless = structuredClone(inventory); ownerless.Abilities[0].EntryEvidence = 'owner-unreviewed'
assert.throws(() => build(ownerless), /explicit classification/)
const wrongReference = structuredClone(inventory); wrongReference.Abilities[0].TestReferences = [{ AbilityId: 'another-ability' }]
assert.throws(() => build(wrongReference))
const fragment = structuredClone(inventory)
fragment.Abilities[0].Definition.ResolutionText = '阵亡时/'
fragment.Abilities[0].Definition.Presentations.push({ SceneId: 'ordinary-segment', Flow: 'followup', SegmentIndex: 2, SegmentCount: 2, BranchLabel: null, RequiredChoices: null })
const fragmentMatrix = build(fragment)
assert.equal(fragmentMatrix.counts.expectedSemanticsPending, 1)
assert.equal(fragmentMatrix.rows[0].expectedResultText, '阵亡时/')
assert.equal(fragmentMatrix.rows[0].presentations[2].Flow, 'followup')
assert.equal(fragmentMatrix.counts.presentations, 3)
console.log(JSON.stringify({ status: 'passed', checks: 'denominator, order, parameters, stages, branches, dynamic factories, owner/reference refusal, full presentation and fragment semantics', inputNonMutating: true, fullChainPassesInvented: 0 }))
