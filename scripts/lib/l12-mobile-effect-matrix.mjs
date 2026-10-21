import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import { presentationBranches } from './l12-presentation-branches.mjs'

function stable(value) {
  if (Array.isArray(value)) return value.map(stable)
  if (value && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(key => [key, stable(value[key])]))
  return value
}
const digest = value => crypto.createHash('sha256').update(JSON.stringify(stable(value))).digest('hex')
const unique = values => [...new Set(values)].sort()

export function buildMobileEffectMatrix(inventory, catalog, promptRows) {
  assert.equal(inventory.Schema, 3)
  assert.equal(catalog.length, inventory.CardCount)
  const cards = new Map(catalog.map(card => [card.id, card]))
  assert.equal(cards.size, catalog.length, 'Catalog IDs must be unique')
  const ids = inventory.Abilities.map(row => row.Definition.AbilityId)
  assert.equal(new Set(ids).size, ids.length, 'Stable ability IDs must be unique')
  const rows = inventory.Abilities.map((row, sourceAbilityIndex) => {
    const definition = row.Definition
    assert(cards.has(row.CardId), 'Ability must belong to the current complete catalog')
    assert.equal(row.CardId, definition.CardId)
    const orderedAtoms = [...definition.Atoms].sort((a, b) => a.Order - b.Order)
      .map(atom => ({ order: atom.Order, stage: atom.Stage, kind: atom.Kind, parameters: atom.Parameters }))
    const branches = presentationBranches(definition.Presentations)
    const expectedResultText = definition.ResolutionText || definition.Text
    const expectedSemanticsPending = /^(进攻时|阵亡时|登场时)[/／：:\s]*$/.test(expectedResultText || '')
    const profile = row.Profile
    assert(profile || ['fine-definition', 'composite-definition'].includes(row.EntryEvidence), 'Missing owner requires explicit classification, not implicit coverage')
    for (const reference of row.TestReferences) assert.equal(reference.AbilityId, definition.AbilityId)
    return {
      abilityId: definition.AbilityId, sourceAbilityIndex, cardId: row.CardId, cardName: row.Name,
      cardType: cards.get(row.CardId).cardType, executionModel: definition.ExecutionModel, trigger: definition.Trigger,
      representative: { cardId: row.CardId, name: row.Name, equivalenceGroupConfirmed: false },
      preconditions: orderedAtoms.filter(atom => atom.stage === 'condition' || atom.kind === 'condition.expression'),
      costText: definition.CostText, expectedResultText,
      expectedSemanticsStatus: expectedSemanticsPending ? 'EXPECTED_SEMANTICS_PENDING_EXACT_RUNTIME_MAPPING' : 'CATALOG_DECLARATION_NOT_EXECUTION_ORACLE',
      originalCardTextContext: cards.get(row.CardId).effect ?? null,
      cardTextContextIsNotAbilitySpecificExpectation: true,
      orderedAtoms, presentations: definition.Presentations, branches, profileId: profile?.Id ?? null, runtimeOwners: profile?.RuntimeOwners ?? null,
      semanticIndex: digest({ model: definition.ExecutionModel, trigger: definition.Trigger, orderedAtoms, owners: profile?.RuntimeOwners ?? null, presentations: definition.Presentations, branches }),
      semanticIndexIsEquivalenceProof: false,
      declaredNotApplicable: { sharedProfile: profile?.NotApplicable ?? {}, exactAbility: profile?.AbilityNotApplicable?.[definition.AbilityId] ?? {} },
      boundaryPlan: {
        common: ['normal', 'no-target', 'negated', 'target-invalidated', 'duplicate-submit', 'reconnect', 'privacy-opponent-spectator', 'portrait-landscape-touch', 'state-zone-value-status-log-consistency'],
        additional: profile?.AdditionalChecks ?? [],
        paymentAndExit: orderedAtoms.some(atom => atom.kind.startsWith('cost.') || atom.stage === 'cost') ? 'Check real payment, refusal/cancel and no refund after commit; retain exact declared exceptions' : 'Classify exact applicability; do not invent a payment prompt',
        branchCases: branches.map(branch => ({ ...branch, actual: 'EVIDENCE_NOT_YET_MAPPED' })),
      },
      evidenceLayers: {
        staticOwnership: { entryEvidence: row.EntryEvidence, routeCandidates: row.RouteCandidates },
        backendNamedReferences: { status: 'LINKS_NOT_EXECUTION_RECEIPTS', references: row.TestReferences },
        componentTouch: { status: 'EXACT_ABILITY_MAPPING_PENDING', existingTypeConsumerEvidenceMayBeReused: true },
        authoritativeEngineTouchAckSettlement: { status: 'EVIDENCE_NOT_YET_MAPPED', receipts: [], failed: null },
      },
    }
  })
  const actualCards = new Set(rows.map(row => row.cardId))
  const without = inventory.CardsWithoutAbilities
  assert.deepEqual(unique([...actualCards, ...without]), unique(cards.keys()), 'Every card must remain in the denominator')
  assert(without.every(cardId => !actualCards.has(cardId)), 'No-ability cards cannot be counted as effect-covered')
  const axis = getter => unique(rows.flatMap(getter)).map(value => ({ value, members: rows.filter(row => getter(row).includes(value)).map(row => row.abilityId) }))
  return {
    schemaVersion: 1, status: 'COVERAGE_PLAN_NOT_EXECUTION_RESULT',
    counts: { cards: cards.size, abilities: rows.length, noAbilityCards: without.length, branches: rows.reduce((sum, row) => sum + row.branches.length, 0), presentations: rows.reduce((sum, row) => sum + row.presentations.length, 0), expectedSemanticsPending: rows.filter(row => row.expectedSemanticsStatus === 'EXPECTED_SEMANTICS_PENDING_EXACT_RUNTIME_MAPPING').length, fullChainPassesClaimed: 0 },
    classificationPolicy: 'Each stable ability remains a row. Semantic indices preserve order/stage/parameters and exact branch conditions, but are not proof that distinct cards share one representative. No old linked test or mock UI result is promoted to a real engine-touch-ACK-settlement pass.',
    rows, noAbilityCards: without.map(cardId => ({ cardId, name: cards.get(cardId).nameZh, effectApplicability: 'No printed ability; ordinary card play/visibility still separate' })),
    axes: { executionModels: axis(row => [row.executionModel]), triggers: axis(row => [row.trigger]), atomKinds: axis(row => unique(row.orderedAtoms.map(atom => atom.kind))) },
    promptCallSites: promptRows.map(row => ({ ...row, actualRuntimeChoicePrivacyMapping: row.kind ? 'BIND_CONTINUATION_MODE_CHOICES_AND_VIEW' : 'DYNAMIC_FACTORY_REQUIRES_RUNTIME_MAPPING', realEngineTouchReceipt: null })),
  }
}
