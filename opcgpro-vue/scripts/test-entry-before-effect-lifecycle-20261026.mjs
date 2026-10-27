import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const scriptPath = fileURLToPath(import.meta.url)
const gameRoot = path.resolve(path.dirname(scriptPath), '../src/l12/game')
const boardSource = fs.readFileSync(path.join(gameRoot, 'GameBoard.vue'), 'utf8')
const movementSource = fs.readFileSync(path.join(gameRoot, 'ZoneMovementPresentationLayer.vue'), 'utf8')
const coordinatorSource = fs.readFileSync(path.join(gameRoot, 'presentationSequenceCoordinator.ts'), 'utf8')

function scriptTree(fileName, source) {
  const parsed = parse(source)
  assert.deepEqual(parsed.errors, [], `${fileName} must remain a valid SFC`)
  return ts.createSourceFile(fileName, parsed.descriptor.scriptSetup.content,
    ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
}

function variableInitializer(tree, name) {
  for (const statement of tree.statements) {
    if (!ts.isVariableStatement(statement)) continue
    for (const declaration of statement.declarationList.declarations) {
      if (ts.isIdentifier(declaration.name) && declaration.name.text === name && declaration.initializer)
        return declaration.initializer
    }
  }
  assert.fail(`${name} must remain implemented in the real component`)
}

function compileExpression(tree, expression, bindings) {
  const output = ts.transpileModule(`const value = ${expression.getText(tree)}; value`, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
  }).outputText
  return Function(...Object.keys(bindings), `${output}; return value`)(...Object.values(bindings))
}

function pausedWatcherCallback(tree) {
  for (const statement of tree.statements) {
    if (!ts.isExpressionStatement(statement) || !ts.isCallExpression(statement.expression)) continue
    const call = statement.expression
    if (call.expression.getText(tree) !== 'watch' || call.arguments.length < 2) continue
    if (call.arguments[0].getText(tree).replaceAll(' ', '') !== '()=>props.paused') continue
    return call.arguments[1]
  }
  assert.fail('the real zone paused watcher must remain discoverable')
}

function watchCallbackContaining(tree, marker) {
  for (const statement of tree.statements) {
    if (!ts.isExpressionStatement(statement) || !ts.isCallExpression(statement.expression)) continue
    const call = statement.expression
    if (call.expression.getText(tree) !== 'watch' || call.arguments.length < 2) continue
    if (call.arguments[1].getText(tree).includes(marker)) return call.arguments[1]
  }
  assert.fail(`watch callback containing ${marker} must remain discoverable`)
}

function actualFunction(tree, name, bindings = {}) {
  const declaration = tree.statements.find(statement => ts.isFunctionDeclaration(statement)
    && statement.name?.text === name)
  assert.ok(declaration, `${name} must remain implemented in the real component`)
  const output = ts.transpileModule(declaration.getText(tree), {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
  }).outputText
  return Function(...Object.keys(bindings), `${output}; return ${name}`)(...Object.values(bindings))
}

const boardTree = scriptTree('GameBoard.ts', boardSource)
const movementTree = scriptTree('ZoneMovementPresentationLayer.ts', movementSource)
const coordinatorModule = { exports: {} }
Function('module', 'exports', ts.transpileModule(coordinatorSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText)(coordinatorModule, coordinatorModule.exports)
const { createPresentationSequenceCoordinator } = coordinatorModule.exports
const projectionOutput = ts.transpileModule(
  fs.readFileSync(path.join(gameRoot, 'visualTransitionProjection.ts'), 'utf8'),
  { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } },
).outputText
const projection = await import(`data:text/javascript;base64,${Buffer.from(projectionOutput).toString('base64')}`)

const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const results = []
async function test(name, run) {
  try {
    await run()
    results.push({ name, passed: true })
  } catch (error) {
    results.push({ name, passed: false, error: error?.message ?? String(error) })
  }
}

async function firstGrant(reservations) {
  const order = []
  const releases = new Map()
  for (const reservation of reservations) {
    void reservation.handle.waitUntilGranted().then(release => {
      order.push(reservation.label)
      releases.set(reservation.label, release)
    })
  }
  await delay(15)
  return { order, releases }
}

await test('actual coordinator: later effect sequence waits for complete entry landing', async () => {
  const coordinator = createPresentationSequenceCoordinator()
  const entry = coordinator.reserve(70, 20, { visualKey: 'generic-entry-instance' })
  const effect = coordinator.reserve(71, 10)
  const observed = await firstGrant([
    { label: 'entry-flight', handle: entry },
    { label: 'entry-effect', handle: effect },
  ])
  assert.deepEqual(observed.order, ['entry-flight'])
  observed.releases.get('entry-flight')(true)
  await delay(15)
  assert.deepEqual(observed.order, ['entry-flight', 'entry-effect'])
  observed.releases.get('entry-effect')(true)
  coordinator.reset()
})

const entryCard = {
  instanceId: 'generic-entry-instance', cardId: 'generic-legion', name: '通用登场军团',
  cardType: 'legion', hidden: false, identityKnown: true, tapped: false,
  cost: 2, baseTroops: 3000, troops: 3000, disasterLevel: 0,
}
const entryGroupId = 'play:generic-entry-instance'
const entryEvent = {
  sequence: 80, type: 'play', playerIndex: 0, text: '玩家打出通用登场军团',
  playerLogGroupId: entryGroupId, playerLogTiming: 'enter', cards: [entryCard],
}
const entryEffectEvent = {
  sequence: 81, type: 'effect-result', playerIndex: 0, text: '通用登场效果结算',
  effectText: '执行登场效果。', effectSceneId: 'generic-entry-effect', effectResultStatus: 'resolved',
  playerLogGroupId: entryGroupId, playerLogTiming: 'enter', cards: [entryCard],
}
function playerWithEntryOnField() {
  return {
    playerIndex: 0, name: '玩家', faction: 'neutral', hand: [], handCount: 0,
    graveyard: [], resolving: [], field: [[entryCard, null, null], [null, null, null]],
    relic: null, extraRelics: [], libraryTop: null, morale: [],
    master: { masterId: 'generic-master', masterName: '通用主宰', hp: 20, tapped: false },
  }
}

class Element {
  constructor() {
    this.dataset = {}
    this.style = {}
    this.classList = { contains: () => false, add: () => {}, remove: () => {} }
  }
  cloneNode() { return new Element() }
}

function reserveEntryWithActualZoneWatcher({ coordinator, paused, effectEvent = entryEffectEvent }) {
  const trace = []
  const observedCoordinator = {
    ...coordinator,
    reserve(sequence, priority, options) {
      trace.push(`zone-reserve:${sequence}:${priority}:${options?.visualKey ?? ''}`)
      const reservation = coordinator.reserve(sequence, priority, options)
      return {
        ...reservation,
        setPaused(value) { trace.push(`zone-pause:${value}`); reservation.setPaused(value) },
      }
    },
  }
  const movementClaims = projection.createMovementClaimState()
  projection.resetMovementClaimState(movementClaims, 79, 1, new Map([[entryCard.instanceId, 'hand']]))
  const props = {
    revision: 2, synchronizing: false, playbackSpeed: null, matchId: 'generic-match',
    events: [entryEvent, effectEvent], players: [playerWithEntryOnField()], prompts: [],
    viewerPlayerIndex: 0, paused, sequenceCoordinator: observedCoordinator,
  }
  const sourceZoneHints = new Map()
  const authoritativeSourceZone = actualFunction(movementTree, 'authoritativeSourceZone', {
    resolveMovementSourceZone: projection.resolveMovementSourceZone,
    movementClaims,
    sourceZoneHints,
  })
  const isLegionEntryMovement = actualFunction(movementTree, 'isLegionEntryMovement')
  const movementFromEvent = actualFunction(movementTree, 'movementFromEvent', {
    props, movementClaims, sourceZoneHints, authoritativeSourceZone,
    resolveMovementSourceZone: projection.resolveMovementSourceZone,
    isMovementCardConcealed: projection.isMovementCardConcealed,
    isAuthoritativePublicFaceMovement: projection.isAuthoritativePublicFaceMovement,
    isLegionEntryMovement,
    legionEntryImpact: () => ({ strength: 0 }),
    movementFactKey: projection.movementFactKey,
    leaveMovementDestination: projection.leaveMovementDestination,
    simplifiedEntryMotion: () => false,
    publicHandAddCaption: () => undefined,
    textSource: () => 'hand',
  })
  const pendingReservations = new Set()
  const pendingMovementHolders = new Set()
  const source = new Element()
  const callback = compileExpression(movementTree,
    watchCallbackContaining(movementTree, 'claimFreshMovementEvents(props.events, movementClaims)'), {
      awaitingMatchBaseline: false, props, movementClaims, reset: () => trace.push('zone-reset'),
      claimFreshMovementEvents: projection.claimFreshMovementEvents,
      collectKnownCardZones: projection.collectKnownCardZones,
      beginMovementTransactionBatch: projection.beginMovementTransactionBatch,
      isSupersededLeaveEvent: projection.isSupersededLeaveEvent,
      isCombatDefeatLeaveEvent: projection.isCombatDefeatLeaveEvent,
      movementCardsForEvent: projection.movementCardsForEvent,
      isMovementCardConcealed: projection.isMovementCardConcealed,
      movementFromEvent,
      fallbackRect: () => ({ x: 0, y: 0, width: 72, height: 101 }),
      claimMovementTransaction: projection.claimMovementTransaction,
      entryMovementTransactionKey: projection.entryMovementTransactionKey,
      finalizeMovementTransactionBatch: projection.finalizeMovementTransactionBatch,
      pendingReservations, reservationCount: { value: 0 }, viewportGeneration: 1,
      beginPreparationBatch: () => ({ controller: { signal: { aborted: false } } }),
      cardElementInZone: () => source, attachmentElement: () => null,
      disasterDestinationElement: () => null, publicSourceHolder: () => null,
      genericPublicSourceGhost: () => null, disasterPublicSourceGhost: () => null,
      snapshotDecodedAuthoritativeCardVisual: () => true,
      elementRect: () => ({ x: 10, y: 10, width: 72, height: 101 }),
      resolveRect: () => ({ x: 20, y: 20, width: 72, height: 101 }),
      elementRotation: () => 0, createMovementHolder: () => true, HTMLElement: Element,
      nextTick: () => new Promise(() => {}),
    })
  void callback([props.revision, props.synchronizing])
  return { trace, pendingReservations, coordinator, observedCoordinator }
}

function queueEffectWithActualGameBoardWatcher({ coordinator, events, paused }) {
  const publicRevealQueue = []
  const trace = []
  const observedCoordinator = {
    ...coordinator,
    reserve(sequence, priority, options) {
      trace.push(`effect-reserve:${sequence}:${priority}`)
      return coordinator.reserve(sequence, priority, options)
    },
  }
  const callback = compileExpression(boardTree,
    watchCallbackContaining(boardTree, 'entryPresentationTransactionKey(event)'), {
      props: { game: { recentEvents: events, you: 0 } },
      isDisasterRevealEvent: () => false,
      isCardEffectPresentationEvent: projection.isCardEffectPresentationEvent,
      lastPublicRevealSequence: { value: 79 },
      publicRevealPresentationTracker: { classify: () => ({ kind: 'full-card' }) },
      findLinkedPublicContinuationParent: () => null,
      publicRevealQueue,
      presentationCards: projection.cardEffectPresentationCards,
      publicRevealText: event => event.effectText || event.text,
      entryPresentationTransactionKey: projection.entryPresentationTransactionKey,
      cardPresentationCoordinator: observedCoordinator,
      modalPresentationPaused: { value: paused },
      showNextPublicReveal: () => trace.push('effect-show-next'),
    })
  callback()
  return { publicRevealQueue, trace }
}

await test('actual GameBoard queue: a same-sequence entry effect waits for the Zone reservation', async () => {
  const coordinator = createPresentationSequenceCoordinator()
  const sameSequenceEffect = { ...entryEffectEvent, sequence: entryEvent.sequence }
  const zone = reserveEntryWithActualZoneWatcher({ coordinator, paused: false, effectEvent: sameSequenceEffect })
  const effect = queueEffectWithActualGameBoardWatcher({
    coordinator, events: [sameSequenceEffect], paused: false,
  })
  assert.deepEqual(zone.trace.slice(0, 2), [
    `zone-reserve:${entryEvent.sequence}:20:${entryCard.instanceId}`,
    'zone-pause:false',
  ])
  assert.deepEqual(effect.trace, [`effect-reserve:${entryEvent.sequence}:10`, 'effect-show-next'])
  const order = []
  const entryReservation = [...zone.pendingReservations][0]
  assert.ok(entryReservation, 'the actual Zone watcher must retain its entry reservation')
  const entryGrant = entryReservation.waitUntilGranted().then(release => {
    order.push('entry-flight')
    return release
  })
  const effectGrant = effect.publicRevealQueue[0].reservation.waitUntilGranted().then(release => {
    order.push('entry-effect')
    return release
  })
  await delay(15)
  assert.deepEqual(order, ['entry-flight'])
  ;(await entryGrant)(true)
  await delay(15)
  assert.deepEqual(order, ['entry-flight', 'entry-effect'])
  ;(await effectGrant)(true)
  coordinator.reset()
})

function actualPromptRouting({ prompt = null, readOnly = false, minimized = false, modalInspectorVisible = false,
  coordinator = createPresentationSequenceCoordinator() } = {}) {
  const ref = value => ({ value })
  const computed = getter => ({ value: getter() })
  const props = {
    readOnly,
    game: {
      phase: 'Main', prompts: prompt ? [prompt] : [], waitingPrompt: null,
      players: [playerWithEntryOnField()],
    },
  }
  const promptPresentationSourceInstanceId = actualFunction(boardTree, 'promptPresentationSourceInstanceId')
  const deferredEntryPromptIds = compileExpression(boardTree,
    variableInitializer(boardTree, 'deferredEntryPromptIds'), {
      computed, props, presentationReservationEpoch: ref(0),
      promptPresentationSourceInstanceId, cardPresentationCoordinator: coordinator,
    })
  const presentationPrompts = compileExpression(boardTree,
    variableInitializer(boardTree, 'presentationPrompts'), {
      computed, props, deferredEntryPromptIds,
    })
  const boardTargetPrompt = compileExpression(boardTree, variableInitializer(boardTree, 'boardTargetPrompt'), {
    computed, props, presentationPrompts,
  })
  const boardSlotPrompt = compileExpression(boardTree, variableInitializer(boardTree, 'boardSlotPrompt'), {
    computed, props, presentationPrompts,
  })
  const resourceSelectionPrompt = compileExpression(boardTree, variableInitializer(boardTree, 'resourceSelectionPrompt'), {
    computed, props, presentationPrompts,
  })
  const visibleInlinePromptIds = compileExpression(boardTree, variableInitializer(boardTree, 'visibleInlinePromptIds'), {
    computed, boardTargetPrompt, boardSlotPrompt, resourceSelectionPrompt,
  })
  const activeBoardPromptIds = compileExpression(boardTree, variableInitializer(boardTree, 'activeBoardPromptIds'), {
    computed, boardTargetPrompt, boardSlotPrompt, resourceSelectionPrompt, deferredEntryPromptIds,
  })
  const promptOverlayMounted = compileExpression(boardTree, variableInitializer(boardTree, 'promptOverlayMounted'), {
    computed, props,
  })
  const visibleBoardPromptIds = compileExpression(boardTree, variableInitializer(boardTree, 'visibleBoardPromptIds'), {
    computed, props, boardControlMinimized: ref(minimized), activeBoardPromptIds, deferredEntryPromptIds,
  })
  const visibleBoardPromptId = compileExpression(boardTree, variableInitializer(boardTree, 'visibleBoardPromptId'), {
    computed, visibleBoardPromptIds,
  })
  const routedPromptModal = compileExpression(boardTree, variableInitializer(boardTree, 'routedPromptModal'), {
    computed, props, activeBoardPromptIds, presentationPrompts,
  })
  const visiblePromptModal = compileExpression(boardTree, variableInitializer(boardTree, 'visiblePromptModal'), {
    computed, promptOverlayMounted, routedPromptModal, promptMinimized: ref(minimized),
  })
  const modalPresentationPaused = compileExpression(boardTree, variableInitializer(boardTree, 'modalPresentationPaused'), {
    computed,
    visibleBoardPromptId,
    visiblePromptModal,
    graveyardPlayer: ref(null),
    masterPlayerIndex: ref(null),
    customDisasterSlot: ref(null),
    mobileRecordOpen: ref(false),
    mobileMoralePickerOpen: ref(false),
    mobileTrialAbilityCard: ref(null),
    mobileTrialAbilityMinimized: ref(false),
    modalInspectorVisible: ref(modalInspectorVisible),
  })
  return {
    boardTargetPrompt: boardTargetPrompt.value,
    boardSlotPrompt: boardSlotPrompt.value,
    resourceSelectionPrompt: resourceSelectionPrompt.value,
    deferredEntryPromptIds: [...deferredEntryPromptIds.value],
    visibleInlinePromptIds: visibleInlinePromptIds.value,
    activeBoardPromptIds: activeBoardPromptIds.value,
    suppressedPromptIds: activeBoardPromptIds.value,
    visibleBoardPromptId: visibleBoardPromptId.value,
    routedPromptModal: routedPromptModal.value,
    visiblePromptModal: visiblePromptModal.value,
    modalPresentationPaused: modalPresentationPaused.value,
  }
}

function runActualZonePause(paused) {
  const callback = pausedWatcherCallback(movementTree)
  const active = { value: { key: 'generic-entry-flight' } }
  const trace = []
  const pendingReservations = new Set([{
    setPaused: value => trace.push(`reservation-paused:${value}`),
  }])
  const fn = compileExpression(movementTree, callback, {
    active,
    cancelActiveMovement: () => { trace.push('entry-flight-cancelled'); active.value = null },
    pendingReservations,
    showNext: () => trace.push('show-next'),
  })
  fn(paused)
  return trace
}

function entryPrompt(kind, data = {}) {
  return {
    promptId: `entry-effect-${kind}`, playerIndex: 0, kind, text: '选择登场效果',
    validChoices: kind === 'target' ? [entryCard.instanceId] : ['resolve'],
    minChoose: 1, maxChoose: 1, choiceLabels: {},
    sourceInstanceId: entryCard.instanceId, sourceCardId: entryCard.cardId,
    data: { sourceInstanceId: entryCard.instanceId, ...data },
  }
}

await test('actor: actual entry-linked modal prompt is deferred across registration and restored on abort', () => {
  const prompt = entryPrompt('option')
  assert.equal(prompt.sourceInstanceId, entryEvent.cards[0].instanceId)
  assert.equal(prompt.data.sourceInstanceId, entryEvent.cards[0].instanceId)
  const before = actualPromptRouting({ prompt })
  assert.equal(before.routedPromptModal, true)
  assert.equal(before.visiblePromptModal, true)
  assert.equal(before.modalPresentationPaused, true)
  const coordinator = createPresentationSequenceCoordinator()
  const zone = reserveEntryWithActualZoneWatcher({ coordinator, paused: before.modalPresentationPaused })
  assert.deepEqual(zone.trace.slice(0, 2), [
    `zone-reserve:${entryEvent.sequence}:20:${entryCard.instanceId}`,
    'zone-pause:true',
  ])
  const pending = actualPromptRouting({ prompt, coordinator })
  assert.deepEqual(pending.deferredEntryPromptIds, [prompt.promptId])
  assert.deepEqual(pending.suppressedPromptIds, [prompt.promptId])
  assert.equal(pending.visiblePromptModal, false)
  assert.equal(pending.modalPresentationPaused, false)
  assert.equal(coordinator.ownsEntryMovementTransaction(
    projection.entryPresentationTransactionKey(entryEvent)), false,
    'deferring the prompt must not pretend the physical entry already started')
  for (const reservation of zone.pendingReservations) reservation.cancel()
  const aborted = actualPromptRouting({ prompt, coordinator })
  assert.deepEqual(aborted.deferredEntryPromptIds, [])
  assert.equal(aborted.visiblePromptModal, true)
  assert.equal(aborted.modalPresentationPaused, true)
  coordinator.reset()
})

await test('actor: actual source-bound inline target prompt is deferred across registration and restored on abort', () => {
  const prompt = entryPrompt('target', { choiceMode: 'board-target' })
  assert.equal(prompt.data.sourceInstanceId, entryEvent.cards[0].instanceId)
  const before = actualPromptRouting({ prompt })
  assert.equal(before.boardTargetPrompt?.promptId, prompt.promptId)
  assert.equal(before.visibleBoardPromptId, prompt.promptId)
  assert.equal(before.visiblePromptModal, false)
  assert.equal(before.modalPresentationPaused, true)
  const coordinator = createPresentationSequenceCoordinator()
  const zone = reserveEntryWithActualZoneWatcher({ coordinator, paused: before.modalPresentationPaused })
  assert.equal(zone.trace.includes('zone-pause:true'), true)
  const pending = actualPromptRouting({ prompt, coordinator })
  assert.deepEqual(pending.deferredEntryPromptIds, [prompt.promptId])
  assert.deepEqual(pending.suppressedPromptIds, [prompt.promptId])
  assert.equal(pending.boardTargetPrompt, null)
  assert.equal(pending.visibleBoardPromptId, null)
  assert.equal(pending.modalPresentationPaused, false)
  assert.equal(coordinator.ownsEntryMovementTransaction(
    projection.entryPresentationTransactionKey(entryEvent)), false)
  for (const reservation of zone.pendingReservations) reservation.cancel()
  const aborted = actualPromptRouting({ prompt, coordinator })
  assert.equal(aborted.boardTargetPrompt?.promptId, prompt.promptId)
  assert.equal(aborted.visibleBoardPromptId, prompt.promptId)
  coordinator.reset()
})

await test('all inline entry prompt surfaces share the same deferred source gate', () => {
  const coordinator = createPresentationSequenceCoordinator()
  const transactionKey = projection.entryPresentationTransactionKey(entryEvent)
  const reservation = coordinator.reserve(entryEvent.sequence, 20, {
    role: 'entry-movement', sourceInstanceId: entryCard.instanceId, transactionKey,
  })
  const prompts = [
    entryPrompt('target', { choiceMode: 'board-target' }),
    { ...entryPrompt('slot', { choiceMode: 'board-slot' }), promptId: 'entry-effect-slot', validChoices: ['0:1'] },
    { ...entryPrompt('resource-payment', { choiceMode: 'resource-payment' }), promptId: 'entry-effect-resource', validChoices: ['morale:1'] },
  ]
  for (const prompt of prompts) {
    const route = actualPromptRouting({ prompt, coordinator })
    assert.deepEqual(route.deferredEntryPromptIds, [prompt.promptId])
    assert.equal(route.boardTargetPrompt, null)
    assert.equal(route.boardSlotPrompt, null)
    assert.equal(route.resourceSelectionPrompt, null)
    assert.equal(route.visibleBoardPromptId, null)
    assert.equal(route.visiblePromptModal, false)
    assert.equal(route.modalPresentationPaused, false)
  }
  reservation.cancel()
  coordinator.reset()
})

await test('optional skip remains authoritative but cannot appear before landing', () => {
  const coordinator = createPresentationSequenceCoordinator()
  const transactionKey = projection.entryPresentationTransactionKey(entryEvent)
  const reservation = coordinator.reserve(entryEvent.sequence, 20, {
    role: 'entry-movement', sourceInstanceId: entryCard.instanceId, transactionKey,
  })
  const prompt = { ...entryPrompt('option'), promptId: 'entry-effect-optional',
    validChoices: ['skip', 'resolve'], minChoose: 1, maxChoose: 1 }
  const pending = actualPromptRouting({ prompt, coordinator })
  assert.deepEqual(pending.deferredEntryPromptIds, [prompt.promptId])
  assert.equal(pending.visiblePromptModal, false)
  assert.deepEqual(prompt.validChoices, ['skip', 'resolve'], 'presentation deferral may not mutate authority choices')
  reservation.cancel()
  const restored = actualPromptRouting({ prompt, coordinator })
  assert.equal(restored.visiblePromptModal, true)
  assert.deepEqual(prompt.validChoices, ['skip', 'resolve'])
  coordinator.reset()
})

await test('missing or conflicting prompt identity never creates a presentation hang', () => {
  const coordinator = createPresentationSequenceCoordinator()
  const transactionKey = projection.entryPresentationTransactionKey(entryEvent)
  const reservation = coordinator.reserve(entryEvent.sequence, 20, {
    role: 'entry-movement', sourceInstanceId: entryCard.instanceId, transactionKey,
  })
  const missing = { ...entryPrompt('option'), promptId: 'missing-source', sourceInstanceId: null, data: {} }
  const conflicting = { ...entryPrompt('option'), promptId: 'conflicting-source',
    sourceInstanceId: entryCard.instanceId, data: { sourceInstanceId: 'different-instance' } }
  for (const prompt of [missing, conflicting]) {
    const route = actualPromptRouting({ prompt, coordinator })
    assert.deepEqual(route.deferredEntryPromptIds, [])
    assert.equal(route.visiblePromptModal, true)
    assert.equal(route.modalPresentationPaused, true)
  }
  reservation.cancel()
  coordinator.reset()
})

await test('reservation ABA cancel cannot release a newer entry gate', () => {
  const coordinator = createPresentationSequenceCoordinator()
  const transactionKey = projection.entryPresentationTransactionKey(entryEvent)
  const options = { role: 'entry-movement', sourceInstanceId: entryCard.instanceId, transactionKey }
  const first = coordinator.reserve(entryEvent.sequence, 20, options)
  assert.equal(coordinator.reservationState(options), 'pending')
  first.cancel()
  assert.equal(coordinator.reservationState(options), null)
  const second = coordinator.reserve(entryEvent.sequence + 1, 20, options)
  assert.equal(coordinator.reservationState(options), 'pending')
  first.cancel()
  assert.equal(coordinator.reservationState(options), 'pending')
  second.cancel()
  assert.equal(coordinator.reservationState(options), null)
  coordinator.reset()
})

await test('actual source-bound prompt cannot cancel a flight that already became active', async () => {
  const prompt = entryPrompt('option')
  const coordinator = createPresentationSequenceCoordinator()
  const transactionKey = projection.entryPresentationTransactionKey(entryEvent)
  const reservation = coordinator.reserve(entryEvent.sequence, 20, {
    role: 'entry-movement', sourceInstanceId: entryCard.instanceId, transactionKey,
  })
  const release = await reservation.waitUntilGranted()
  const route = actualPromptRouting({ prompt, coordinator })
  assert.equal(prompt.sourceInstanceId, entryEvent.cards[0].instanceId)
  assert.equal(coordinator.reservationState({ role: 'entry-movement', sourceInstanceId: entryCard.instanceId }), 'active')
  assert.equal(route.visiblePromptModal, false)
  assert.equal(route.modalPresentationPaused, false)
  const trace = runActualZonePause(route.modalPresentationPaused)
  assert.doesNotMatch(trace.join(','), /entry-flight-cancelled/,
    'prompt pre-emption must defer the prompt, not terminate the already visible entry flight')
  release(true)
  const landed = actualPromptRouting({ prompt, coordinator })
  assert.equal(landed.visiblePromptModal, true)
  assert.equal(landed.modalPresentationPaused, true)
  coordinator.reset()
})

for (const [perspective, readOnly] of [['opponent', false], ['spectator', true]]) {
  await test(`${perspective}: absent private prompt cannot pause public entry/effect order`, () => {
    const route = actualPromptRouting({ readOnly })
    assert.equal(route.visibleBoardPromptId, null)
    assert.equal(route.visiblePromptModal, false)
    assert.equal(route.modalPresentationPaused, false)
  })
}

await test('unrelated modal retains the existing pause/cancel priority', () => {
  const unrelated = {
    ...entryPrompt('option'), promptId: 'unrelated-modal',
    sourceInstanceId: 'unrelated-instance', data: { sourceInstanceId: 'unrelated-instance' },
  }
  assert.notEqual(unrelated.sourceInstanceId, entryEvent.cards[0].instanceId)
  const route = actualPromptRouting({ prompt: unrelated })
  assert.equal(route.modalPresentationPaused, true)
  assert.match(runActualZonePause(true).join(','), /entry-flight-cancelled/)
})

await test('an existing inspector pauses a pending entry while its matching response remains deferred', () => {
  const coordinator = createPresentationSequenceCoordinator()
  const reservation = coordinator.reserve(entryEvent.sequence, 20, {
    role: 'entry-movement', sourceInstanceId: entryCard.instanceId,
    transactionKey: projection.entryPresentationTransactionKey(entryEvent),
  })
  try {
    const prompt = { ...entryPrompt('response'), validChoices: ['pass'] }
    const blocked = actualPromptRouting({ prompt, coordinator, modalInspectorVisible: true })
    assert.deepEqual(blocked.deferredEntryPromptIds, [prompt.promptId])
    assert.equal(blocked.visiblePromptModal, false)
    assert.equal(blocked.modalPresentationPaused, true)
    const unpaused = actualPromptRouting({ prompt, coordinator, modalInspectorVisible: false })
    assert.equal(unpaused.modalPresentationPaused, false)
    reservation.cancel()
    assert.equal(actualPromptRouting({ prompt, coordinator, modalInspectorVisible: true }).visiblePromptModal, true)
  } finally { coordinator.reset() }
})

await test('landing completion releases the deferred prompt path exactly once', () => {
  const trace = runActualZonePause(false)
  assert.deepEqual(trace, ['reservation-paused:false', 'show-next'])
})

await test('cancel/reset boundary does not replay a stale entry effect', async () => {
  const coordinator = createPresentationSequenceCoordinator()
  const entry = coordinator.reserve(90, 20, { visualKey: 'generic-entry-instance' })
  const effect = coordinator.reserve(91, 30)
  const observed = await firstGrant([
    { label: 'entry-flight', handle: entry },
    { label: 'entry-effect', handle: effect },
  ])
  assert.deepEqual(observed.order, ['entry-flight'])
  effect.cancel()
  observed.releases.get('entry-flight')(false)
  await delay(15)
  assert.deepEqual(observed.order, ['entry-flight'])
  coordinator.reset()
})

await test('animation-off endpoint keeps entry-before-effect authority order', async () => {
  const coordinator = createPresentationSequenceCoordinator()
  const entry = coordinator.reserve(100, 20, { visualKey: 'generic-entry-instance' })
  const effect = coordinator.reserve(101, 10)
  const observed = await firstGrant([
    { label: 'entry-endpoint', handle: entry },
    { label: 'entry-effect', handle: effect },
  ])
  assert.deepEqual(observed.order, ['entry-endpoint'])
  observed.releases.get('entry-endpoint')(true)
  await delay(15)
  assert.deepEqual(observed.order, ['entry-endpoint', 'entry-effect'])
  observed.releases.get('entry-effect')(true)
  coordinator.reset()
})

for (const mode of ['unready', 'paused']) {
  await test(`Main: ${mode} entry still blocks its exact same-sequence effect`, async () => {
    const coordinator = createPresentationSequenceCoordinator()
    const entry = coordinator.reserve(41,20,{role:'entry-movement',sourceInstanceId:'x',transactionKey:'T'})
    const effect = coordinator.reserve(41,10,{role:'entry-effect',sourceInstanceId:'x',transactionKey:'T'})
    const order = [], releases = new Map()
    const ready = (handle,label) => { void handle.waitUntilGranted().then(release => { order.push(label); releases.set(label,release) }) }
    try {
      ready(effect,'effect')
      if (mode === 'paused') { entry.setPaused(true); ready(entry,'entry') }
      await delay(15)
      assert.deepEqual(order,[])
      entry.setPaused(false)
      if (mode === 'unready') ready(entry,'entry')
      await delay(15)
      assert.deepEqual(order,['entry'])
      releases.get('entry')(true)
      await delay(15)
      assert.deepEqual(order,['entry','effect'])
      releases.get('effect')(true)
    } finally { coordinator.reset() }
  })
}
await test('Main: a coincident transaction label cannot bind a different source instance', async () => {
  const coordinator = createPresentationSequenceCoordinator()
  const entry=coordinator.reserve(41,20,{role:'entry-movement',sourceInstanceId:'x',transactionKey:'T'})
  const effect=coordinator.reserve(41,10,{role:'entry-effect',sourceInstanceId:'y',transactionKey:'T'})
  try {
    const observed=await firstGrant([{label:'entry',handle:entry},{label:'effect',handle:effect}])
    assert.deepEqual(observed.order,['effect'])
    observed.releases.get('effect')(true)
    await delay(15)
    assert.deepEqual(observed.order,['effect','entry'])
    observed.releases.get('entry')(true)
  } finally { coordinator.reset() }
})

for (const insertion of ['ABC', 'ACB', 'BAC', 'BCA', 'CAB', 'CBA']) {
  await test(`Main: entry dependency keeps stable priority across insertion ${insertion}`, async () => {
    const coordinator = createPresentationSequenceCoordinator()
    const specs = {
      A: { priority:20, options:{role:'entry-movement',sourceInstanceId:'x',transactionKey:'T'} },
      B: { priority:10, options:{role:'entry-effect',sourceInstanceId:'x',transactionKey:'T'} },
      C: { priority:15, options:{} },
    }
    const reservations = [...insertion].map(label => ({ label,
      handle:coordinator.reserve(42, specs[label].priority, specs[label].options) }))
    try {
      const observed = await firstGrant(reservations)
      for (let index = 0; index < 3; index++) {
        const label = observed.order[index]
        assert.ok(label, 'the existing queue must keep making progress')
        observed.releases.get(label)(true)
        await delay(15)
      }
      assert.deepEqual(observed.order, ['C','A','B'],
        'only the dependent effect is delayed; unrelated priority and entry-before-effect must remain stable')
    } finally { coordinator.reset() }
  })
}

assert.doesNotMatch(`${variableInitializer(boardTree, 'modalPresentationPaused').getText(boardTree)}\n${pausedWatcherCallback(movementTree).getText(movementTree)}`,
  /S\d{2}-\d+|加拉哈德|傀儡/,
  'the lifecycle fix must be shared and may not branch on a reported card')
assert.match(boardSource, /:suppressed-prompt-ids="activeBoardPromptIds"/,
  'PromptOverlay auto-focus and modal content must use the same deferred prompt ids as inline controls')

console.log(JSON.stringify({
  suite: 'entry before entry-effect shared lifecycle',
  contract: ['source flight starts', 'source fully lands', 'entry effect card/text or prompt becomes visible'],
  views: ['actor', 'opponent', 'spectator'],
  modes: ['wide/mobile share the same logical gate', 'animation off keeps endpoint order'],
  results,
}, null, 2))

if (results.some(result => !result.passed)) process.exitCode = 1
