import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { compile } from '@vue/compiler-dom'
import * as Vue from 'vue'

// Source-derived reactive integration probe. Native template fragments execute
// with Vue's in-memory renderer; this is NOT browser geometry/hit-test evidence.
// Only synthetic public cards/recipient frames are used. No HTTP/WS is opened.
const paths = {
  board: '../src/l12/game/GameBoard.vue', overlay: '../src/l12/game/PromptOverlay.vue',
  zone: '../src/l12/game/ZoneMovementPresentationLayer.vue',
  coordinator: '../src/l12/game/presentationSequenceCoordinator.ts',
  net: '../src/l12/net.ts', clock: '../src/l12/game/PlayerTurnClock.vue',
}
const sources = Object.fromEntries(Object.entries(paths).map(([key, path]) =>
  [key, readFileSync(new URL(path, import.meta.url), 'utf8')]))
const boardRef = process.argv.find(arg => arg.startsWith('--board-ref='))?.slice('--board-ref='.length)
if (boardRef) {
  assert.match(boardRef, /^[0-9a-f]{7,40}$/i, 'baseline is a pinned local commit')
  const repo = new URL('../../', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
  sources.board = execFileSync('git', ['-c', `safe.directory=${repo}`, '-C', repo,
    'show', `${boardRef}:opcgpro-vue/src/l12/game/GameBoard.vue`], { encoding: 'utf8' })
}
const trees = Object.fromEntries(Object.entries(sources).map(([key, text]) => [key,
  ts.createSourceFile(key + '.ts', key === 'coordinator' || key === 'net'
    ? text : parse(text).descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)]))
const javascript = text => ts.transpileModule(text, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
}).outputText
function run(text, bindings) {
  return Function(...Object.keys(bindings), javascript(text))(...Object.values(bindings))
}
function initializer(kind, name) {
  for (const statement of trees[kind].statements) {
    if (!ts.isVariableStatement(statement)) continue
    for (const declaration of statement.declarationList.declarations)
      if (ts.isIdentifier(declaration.name) && declaration.name.text === name && declaration.initializer)
        return declaration.initializer.getText(trees[kind])
  }
  assert.fail(`missing real ${kind}.${name}`)
}
function nestedInitializer(kind, name, marker) {
  const found = []
  function walk(node) {
    if (ts.isVariableDeclaration(node) && node.name.getText(trees[kind]) === name
      && node.initializer?.getText(trees[kind]).includes(marker)) found.push(node.initializer.getText(trees[kind]))
    ts.forEachChild(node, walk)
  }
  walk(trees[kind]); assert.equal(found.length, 1)
  return found[0]
}
function value(kind, name, bindings) {
  return run(`return (${initializer(kind, name)});`, bindings)
}
function actualFunction(kind, name, bindings) {
  const node = trees[kind].statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
  assert.ok(node, `missing real ${kind}.${name}`)
  return run(`${node.getText(trees[kind])}; return ${name}`, bindings)
}
function watchCall(kind, marker) {
  const matches = trees[kind].statements.filter(node => ts.isExpressionStatement(node)
    && ts.isCallExpression(node.expression) && node.expression.expression.getText(trees[kind]) === 'watch'
    && node.getText(trees[kind]).includes(marker))
  assert.equal(matches.length, 1, `exactly one actual ${kind} watcher: ${marker}`)
  return matches[0].getText(trees[kind])
}
const coordinatorModule = { exports: {} }
Function('module', 'exports', javascript(sources.coordinator))(coordinatorModule, coordinatorModule.exports)
const { createPresentationSequenceCoordinator } = coordinatorModule.exports

// Execute the actual net.ts accepted gameState branch, including its revision
// guard and publication of game and rankedClock, rather than setting two refs.
let receiveBranch
function findReceive(node) {
  if (ts.isIfStatement(node) && node.expression.getText(trees.net) === "message.type === 'gameState'")
    receiveBranch = node.thenStatement.getText(trees.net)
  ts.forEachChild(node, findReceive)
}
findReceive(trees.net)
assert.ok(receiveBranch)
function receiver(l12State) {
  let baseline = null
  // Recreate the closure for each baseline; Function arguments capture values.
  return message => {
    if (message.type === 'gameStateDelta') {
      message = actualFunction('net', 'rebuildDeltaGameState', {
        lastGameStateEnvelope: baseline, negotiatedEventWindowDelta: true, negotiatedHandArrayDelta: false,
      })(message)
      if (!message) return false
    }
    const before = l12State.game
    baseline = run(`let lastGameStateEnvelope = baseline; ${receiveBranch}; return lastGameStateEnvelope`, {
      message: structuredClone(message), l12State, baseline, wireWasPlainGameState: true,
      chatResource: { retireOpponent() {} }, gameStateCardFrameRecovery: { recordPublishedPlainFrame() {} },
      tutorialProjection: () => null, completePendingAction() {}, clearSnapshotRecovery() {},
      clearMatchmakingRecovery() {}, syncGameReentry() {},
    }) ?? baseline
    return l12State.game !== before
  }
}

function findTemplateNode(predicate) {
  let found
  function walk(node) {
    if (predicate(node)) { assert.ok(!found, 'native template fragment must be unique'); found = node }
    for (const child of node.children ?? []) walk(child)
  }
  walk(parse(sources.overlay).descriptor.template.ast)
  assert.ok(found)
  return found
}
const classHas = (node, name) => node.type === 1 && node.props.some(prop =>
  prop.type === 6 && prop.name === 'class' && prop.value?.content.split(' ').includes(name))
const root = findTemplateNode(node => classHas(node, 'l12-prompt-overlay'))
const mini = findTemplateNode(node => classHas(node, 'prompt-minimized-bar'))
const panel = findTemplateNode(node => classHas(node, 'prompt-choice-panel'))
const choice = findTemplateNode(node => node.type === 1 && node.tag === 'button'
  && node.props.some(prop => prop.type === 7 && prop.name === 'bind' && prop.arg?.content === 'title'
    && prop.exp?.content === 'disabledChoiceReason(choice)'))
const confirm = findTemplateNode(node => node.type === 1 && node.tag === 'button'
  && node.props.some(prop => prop.type === 7 && prop.name === 'bind' && prop.arg?.content === 'disabled'
    && prop.exp?.content === 'l12State.pendingAction || !isPromptSelectionSubmittable(prompt, activeSelected)'))
const opening = node => node.loc.source.slice(0, node.loc.source.indexOf('>') + 1)
// Preserve the actual visibility/minimize gates and real pass/confirm handlers.
// Main-phase text-only response branch: card preview, art and geometry omitted.
const nativeTemplate = opening(root) + mini.loc.source + opening(panel)
  + '<template v-for="choice in primaryChoices" :key="choice">'
  + choice.loc.source.replace(/\s+v-else(?=\s|>)/, '') + '</template>'
  + confirm.loc.source + '</section></div>'
const templateCode = compile(nativeTemplate, { mode: 'function' }).code
const render = Function('Vue', templateCode)(Vue)
render._rc = true

function memoryHost() {
  const node = (type, text = '') => ({ type, text, props: {}, children: [], parent: null })
  const renderer = Vue.createRenderer({
    createElement: type => node(type), createText: text => node('#text', text),
    createComment: text => node('#comment', text), setText: (n, text) => { n.text = text },
    setElementText: (n, text) => { n.text = text; n.children = [] },
    parentNode: n => n.parent, nextSibling: n => n.parent?.children[n.parent.children.indexOf(n) + 1] ?? null,
    patchProp: (n, key, _previous, next) => { n.props[key] = next },
    insert(n, parent, anchor = null) {
      if (n.parent) n.parent.children.splice(n.parent.children.indexOf(n), 1)
      n.parent = parent
      const index = anchor ? parent.children.indexOf(anchor) : -1
      if (index < 0) parent.children.push(n); else parent.children.splice(index, 0, n)
    },
    remove(n) { if (n.parent) n.parent.children.splice(n.parent.children.indexOf(n), 1); n.parent = null },
  })
  return { renderer, root: node('root') }
}
function descendants(node) { return [node, ...node.children.flatMap(descendants)] }
const card = (id, owner = 0) => ({ instanceId: id, cardId: 'synthetic-public', name: id,
  cardType: 'legion', faction: 'neutral', ownerIndex: owner, hidden: false })
function prompt(id, source, owner) {
  return { promptId: id, playerIndex: owner, kind: 'response', validChoices: ['pass'],
    minChoose: 1, maxChoose: 1, text: '合成响应，请选择是否让过', activationId: id + ':a',
    sourceInstanceId: source, sourceCardId: 'synthetic-public', step: 0, createdRevision: 2, controller: owner,
    data: { sourceInstanceId: source, choiceMode: 'instant' } }
}
function frame(revision, prompts, owner) {
  return { type: 'gameState', state: { matchId: 'synthetic-response-visibility', revision, you: owner,
    roomCode: 'SYNTHETIC', phase: 'Main', activePlayer: 1 - owner, firstPlayer: 0, turnSerial: 9,
    prompts, waitingPrompt: null, recentEvents: [], players: [0, 1].map(playerIndex => ({ playerIndex,
      master: {}, field: [[card('entry-card', playerIndex), null, null], [null, null, null]],
      hand: [card('old-detail', playerIndex)], graveyard: [], morale: [], mulliganDone: true })) },
    rankedClock: { players: [0, 1].map(playerIndex => ({ playerIndex, connected: true,
      acting: playerIndex === owner, totalRemainingMs: 60000, operationRemainingMs: 30000 })) } }
}
function fixture({ owner = 0, mobile = false, initial = frame(1, [], owner) } = {}) {
  const { computed, reactive, ref, watch } = Vue
  const scope = Vue.effectScope()
  const l12State = reactive({ game: null, rankedClock: null, status: 'online', recoveryPhase: 'snapshot-acknowledged',
    pendingAction: false, gmEnabled: false, room: { roomCode: 'SYNTHETIC' }, notice: '', accountId: 'synthetic', sessionId: 'synthetic' })
  const receive = receiver(l12State)
  receive(initial)
  const props = reactive({ get game() { return l12State.game }, readOnly: false })
  const board = { computed, ref, watch, props, l12State,
    focusCard: ref(null), promptAutoFocusCard: ref(null), mobileLandscapeViewport: ref(mobile),
    boardControlMinimized: ref(false), promptMinimized: ref(false), presentationReservationEpoch: ref(0),
    graveyardPlayer: ref(null), masterPlayerIndex: ref(null), customDisasterSlot: ref(null),
    mobileRecordOpen: ref(false), mobileMoralePickerOpen: ref(false), mobileTrialAbilityCard: ref(null),
    mobileTrialAbilityMinimized: ref(false), selectedId: ref(null), mode: ref('play'), playArmed: ref(false),
    defenseIds: ref([]), supportIds: ref([]), inlinePromptInfoOpen: ref(false), closeMobileTrialAbility() {} }
  const coordinator = createPresentationSequenceCoordinator(undefined, undefined, () => board.presentationReservationEpoch.value++)
  board.cardPresentationCoordinator = coordinator
  board.promptPresentationSourceInstanceId = actualFunction('board', 'promptPresentationSourceInstanceId', {})
  for (const name of ['inspectorFocusCard', 'hasBlockingPrompt', 'deferredEntryPromptIds', 'presentationPrompts',
    'boardTargetPrompt', 'boardSlotPrompt', 'resourceSelectionPrompt', 'visibleInlinePromptIds', 'activeBoardPromptIds',
    'activeBoardPromptId', 'promptOverlayMounted', 'visibleBoardPromptIds', 'visibleBoardPromptId',
    'routedPromptModal', 'visiblePromptModal', 'modalInspectorVisible', 'modalPresentationPaused'])
    board[name] = value('board', name, board)
  board.clearOrdinaryInteractionState = actualFunction('board', 'clearOrdinaryInteractionState', board)
  const hasTransactionIdentity = sources.board.includes('const blockingPromptIdentity = computed(')
  if (hasTransactionIdentity) board.blockingPromptIdentity = value('board', 'blockingPromptIdentity', board)
  const overlayProps = reactive({ get game() { return l12State.game },
    get suppressedPromptIds() { return board.activeBoardPromptIds.value }, suppressedPromptId: null, mobileLayout: mobile })
  const sent = []
  const overlay = { computed, ref, watch, props: overlayProps, l12State, document: { querySelector: () => null },
    nextTick: Vue.nextTick, emit(type, value) {
      if (type === 'autoFocusCard') board.promptAutoFocusCard.value = value
      if (type === 'focusCard') board.focusCard.value = value
      if (type === 'minimizedChange') board.promptMinimized.value = value
    }, cardObjectFor: id => [...props.game.players.flatMap(p => p.hand), ...props.game.players.flatMap(p => p.field.flat())]
      .find(c => c?.instanceId === id) ?? null,
    minimized: ref(false), selected: ref([]), hoveredChoice: ref(null), placementTop: ref([]), placementBottom: ref([]),
    placementSelected: ref(null), draggedChoice: ref(null), placementOrder: ref([]), expandButton: ref(null),
    waitingDefense: ref(false), isMulliganPhase: ref(false), isDisasterPreparation: ref(false),
    sendAction: (command, actor) => sent.push({ command, actor }), isPureEffectDecision: ref(false),
    declineChoices: new Set(['no', 'mode:none', 'skip', 'pass', 'decline', 'cancel', 'refuse']),
  }
  for (const name of ['prompt', 'waitingPrompt', 'sandboxActorIndex', 'me', 'visible'])
    overlay[name] = value('overlay', name, overlay)
  for (const name of ['isInternalChoiceValue', 'naturalChoiceLabel', 'isDeclineChoiceForPrompt', 'isDeclineChoice',
    'currentValidSelection', 'isActivationCancellationSelection', 'isPromptSelectionSubmittable',
    'withPromptBinding', 'resolveChoice', 'toggle', 'confirm', 'focusChoice', 'expandPrompt'])
    overlay[name] = actualFunction('overlay', name, overlay)
  overlay.activeSelected = computed(() => overlay.currentValidSelection())
  const pendingReservations = new Set()
  const zone = { active: ref(null), pendingReservations, cancelActiveMovement() {}, showNext() {} }
  const pauseCall = watchCall('zone', 'for (const reservation of pendingReservations) reservation.setPaused(paused)')
  const zoneCallback = run(`return (${trees.zone.statements.find(n => n.getText(trees.zone) === pauseCall).expression.arguments[1].getText(trees.zone)})`, zone)
  scope.run(() => {
    run(watchCall('board', hasTransactionIdentity ? 'watch(blockingPromptIdentity' : 'if (blocking) clearOrdinaryInteractionState()'), board)
    run(watchCall('board', 'if (!promptId) return'), board)
    run(watchCall('board', 'boardControlMinimized.value = false; inlinePromptInfoOpen.value = false'), board)
    run(watchCall('overlay', "props.game.revision}`"), overlay)
    run(watchCall('overlay', 'placementOrder.value ='), overlay)
    run(watchCall('overlay', "emit('minimizedChange', value)"), overlay)
    watch(board.modalPresentationPaused, zoneCallback)
  })
  const view = { ...overlay, primaryChoices: computed(() => overlay.prompt.value?.validChoices ?? []),
    game: computed(() => props.game), setupClockPlayerIndex: owner, setupRoleLabel: () => '我方',
    promptTitle: '合成响应', overlayTitle: '合成响应', minimizedTask: '展开响应', minimizedButtonText: '展开',
    passiveWaiting: false, autoCloseMessage: '', isPreparation: false, isInitiative: false, isDisasterChoice: false,
    isInfoConfirm: false, isMulligan: false, inspectorVisible: false, reservePlacementDetailSpace: false,
    reserveMulliganDetailSpace: false, mobileLayout: mobile, hasCardChoices: false, isSingleCardRow: false,
    isEffectDecision: false, isEffectOptionList: false, choiceConsequence: () => '', triggerOrderHint: () => '',
    disabledChoiceReason: () => '', label: id => id === 'pass' ? '让过' : id, promptConfirmLabel: '确认',
    onCardStripScroll() {}, onCardStripWheel() {}, onCardStripPointerDown() {}, onCardStripPointerMove() {}, endCardStripPointer() {} }
  const host = memoryHost()
  const app = host.renderer.createApp({ setup: () => view, render })
  app.config.warnHandler = message => { throw new Error(message) }
  // Only the real SetupDecisionClock child is omitted from this Main response
  // surface. Its timer is separately evaluated from PlayerTurnClock below.
  app.component('SetupDecisionClock', { render: () => null })
  app.mount(host.root)
  const buttons = () => descendants(host.root).filter(n => n.type === 'button' && !n.props.disabled)
  return { owner, board, overlay, coordinator, l12State, receive, sent, buttons,
    reserve(source = 'entry-card', sequence = 80, entry = true) {
      // A synthetic projected movement draft enters the exact reservation-map
      // initializer from the real Zone watcher; no duplicated reservation code.
      const drafts = [{ event: { sequence }, draft: { card: card(source, owner),
        entryTransactionKey: entry ? 'entry:' + source : undefined } }]
      return run(`return (${nestedInitializer('zone', 'reservations', 'props.sequenceCoordinator.reserve')})`, {
        drafts, pendingReservations, props: { sequenceCoordinator: coordinator, paused: board.modalPresentationPaused.value },
      })[0]
    },
    observe() { return { revision: props.game.revision, ownerPrompt: props.game.prompts[0]?.promptId,
      acting: l12State.rankedClock.players.find(p => p.playerIndex === owner).acting,
      explicitDetail: board.focusCard.value?.instanceId ?? null, automaticDetail: board.promptAutoFocusCard.value?.instanceId ?? null,
      modalInspectorVisible: board.modalInspectorVisible.value, paused: board.modalPresentationPaused.value,
      deferred: [...board.deferredEntryPromptIds.value], overlayVisible: overlay.visible.value,
      nativeButtons: buttons().map(n => n.props['data-tutorial-choice'] ?? n.props['aria-label'] ?? n.text) } },
    close() { app.unmount(); scope.stop(); coordinator.reset() },
  }
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms))
const flush = async () => { await Vue.nextTick(); await delay(20); await Vue.nextTick() }
const results = []
async function test(name, body) {
  const row = { name, passed: false, observations: [] }
  try { await body(row.observations); row.passed = true }
  catch (error) { row.error = error.message; row.stack = error.stack?.split('\n').slice(0, 6) }
  results.push(row)
}
async function oldPrompt(f, { explicit = true, minimized = false } = {}) {
  f.receive(frame(2, [prompt('previous-response', 'old-detail', f.owner)], f.owner))
  await flush()
  if (explicit) f.overlay.focusChoice('old-detail')
  f.overlay.minimized.value = minimized
  await flush()
}
async function newResponse(f, observations) {
  const reservation = f.reserve()
  let granted = false
  void reservation.waitUntilGranted().then(release => { granted = true; release(true) })
  f.receive(frame(3, [prompt('next-response', 'entry-card', f.owner)], f.owner))
  await flush()
  observations.push({ ...f.observe(), entryGranted: granted })
  return { reservation, get granted() { return granted } }
}
for (const owner of [0, 1]) {
  for (const minimized of [false, true]) await test(`seat${owner}: consecutive response with explicit detail, previous minimized=${minimized}`, async observations => {
    const f = fixture({ owner })
    try {
      await oldPrompt(f, { minimized })
      await newResponse(f, observations)
      assert.equal(f.l12State.game.prompts[0].playerIndex, owner)
      assert.equal(f.l12State.rankedClock.players[owner].acting, true)
      const clockProps = { playerIndex: owner, phase: 'Main', rankedClock: f.l12State.rankedClock }
      const nowMs = Vue.ref(clockProps.rankedClock.receivedAtMs)
      const clock = value('clock', 'clock', { computed: Vue.computed, props: clockProps, nowMs })
      const before = clock.value.operation
      nowMs.value += 1000
      observations.push({ clockBefore: before, clockAfter: clock.value.operation })
      assert.equal(clock.value.operation, before - 1000, 'the real clock still deducts while no choice is rendered')
      assert.ok(f.buttons().some(n => n.props['data-tutorial-choice'] === 'pass'),
        'accepted owner response must not remain absent after its entry is paused by a stale explicit inspector')
    } finally { f.close() }
  })
  for (const mode of ['auto-focus-only', 'mobile-explicit-detail', 'no-preceding-prompt'])
    await test(`control seat${owner}: ${mode}`, async observations => {
      const f = fixture({ owner, mobile: mode === 'mobile-explicit-detail' })
      try {
        if (mode !== 'no-preceding-prompt') await oldPrompt(f, { explicit: mode !== 'auto-focus-only' })
        const next = await newResponse(f, observations)
        assert.equal(next.granted, true, 'the entry can finish before showing its response')
        const pass = f.buttons().find(n => n.props['data-tutorial-choice'] === 'pass')
        assert.ok(pass)
        pass.props.onClick()
        assert.equal(f.sent.length, 1)
        assert.equal(f.sent[0].command.promptId, 'next-response')
        assert.equal(f.sent[0].actor, owner)
      } finally { f.close() }
    })
  await test(`control seat${owner}: same-revision refresh restores exactly the same response`, async observations => {
    const f = fixture({ owner })
    let refreshed
    try {
      await oldPrompt(f); const next = await newResponse(f, observations)
      const alreadyVisible = f.overlay.visible.value
      const same = frame(3, [prompt('next-response', 'entry-card', owner)], owner)
      f.receive(same)
      await flush()
      assert.equal(f.overlay.visible.value, alreadyVisible, 'same-revision data does not invent a new presentation lifecycle')
      refreshed = fixture({ owner, initial: same })
      await flush()
      assert.equal(refreshed.l12State.game.revision, f.l12State.game.revision)
      assert.deepEqual(refreshed.l12State.game.prompts, f.l12State.game.prompts)
      observations.push({ refreshed: refreshed.observe() })
      assert.ok(refreshed.buttons().some(n => n.props['data-tutorial-choice'] === 'pass'))
      next.reservation.cancel(); await flush()
      assert.ok(f.buttons().some(n => n.props['data-tutorial-choice'] === 'pass'), 'cancel releases the existing prompt without changing authority')
    } finally { refreshed?.close(); f.close() }
  })
  await test(`control seat${owner}: valid delta retains response and clock; invalid base cannot publish`, async observations => {
    const f = fixture({ owner })
    try {
      const after = frame(2, [prompt('delta-response', 'entry-card', owner)], owner)
      const delta = { type: 'gameStateDelta', matchId: after.state.matchId, baseRevision: 1, revision: 2,
        changes: [{ path: ['state', 'revision'], value: 2 }, { path: ['state', 'prompts'], value: after.state.prompts },
          { path: ['rankedClock'], value: after.rankedClock }] }
      assert.equal(f.receive(delta), true); await flush()
      assert.ok(f.buttons().some(n => n.props['data-tutorial-choice'] === 'pass'))
      observations.push(f.observe())
      assert.equal(f.receive({ ...delta, baseRevision: 0, revision: 3 }), false)
      assert.equal(f.l12State.game.revision, 2)
      f.receive(frame(1, [], owner)); await flush()
      assert.equal(f.l12State.game.prompts[0].promptId, 'delta-response')
    } finally { f.close() }
  })
  await test(`control seat${owner}: lower-sequence movement drains before entry when no explicit inspector persists`, async observations => {
    const f = fixture({ owner })
    try {
      await oldPrompt(f, { explicit: false })
      const first = f.reserve('unrelated-movement', 79, false)
      const order = []
      void first.waitUntilGranted().then(release => { order.push('lower'); release(true) })
      const next = await newResponse(f, observations)
      await flush()
      assert.deepEqual(order, ['lower'])
      assert.equal(next.granted, true)
      assert.ok(f.buttons().some(n => n.props['data-tutorial-choice'] === 'pass'))
    } finally { f.close() }
  })
  await test(`control seat${owner}: same prompt identity copy update retains a usable minimized expand button`, async observations => {
    const f = fixture({ owner })
    try {
      await oldPrompt(f, { minimized: true })
      const updated = structuredClone(frame(2, [prompt('previous-response', 'old-detail', owner)], owner))
      updated.state.revision = 3
      updated.state.prompts[0].text = '同一响应内容更新'
      f.receive(updated); await flush()
      observations.push(f.observe())
      assert.equal(f.overlay.minimized.value, true)
      const expand = f.buttons().find(n => n.props['aria-label'] === '展开：合成响应')
      assert.ok(expand)
      await expand.props.onClick(); await flush()
      const pass = f.buttons().find(n => n.props['data-tutorial-choice'] === 'pass')
      assert.ok(pass); pass.props.onClick()
      assert.equal(f.sent.length, 1)
      assert.equal(f.sent[0].command.promptId, 'previous-response')
    } finally { f.close() }
  })
  await test(`control seat${owner}: same-prompt revision and clock updates preserve selection and explicit detail`, async observations => {
    const f = fixture({ owner })
    try {
      const stable = frame(2, [{ ...prompt('stable-selection', 'old-detail', owner),
        validChoices: ['one', 'two'], minChoose: 1, maxChoose: 2, data: { sourceInstanceId: 'old-detail' } }], owner)
      f.receive(stable); await flush()
      f.overlay.focusChoice('old-detail'); f.overlay.toggle('one')
      f.overlay.minimized.value = true; await flush()
      const updated = structuredClone(stable)
      updated.state.revision = 3
      updated.rankedClock.players[owner].operationRemainingMs -= 1000
      updated.state.prompts[0].validChoices = ['one', 'two', 'three']
      f.receive(updated); await flush()
      assert.deepEqual(f.overlay.selected.value, [], 'existing overlay choice-refresh behavior remains in force')
      // Restore a selection, then change only revision/clock: it must survive.
      f.overlay.toggle('one'); f.overlay.minimized.value = true; await flush()
      const clockOnly = structuredClone(updated); clockOnly.state.revision = 4
      clockOnly.rankedClock.players[owner].operationRemainingMs -= 1000
      f.receive(clockOnly); await flush()
      observations.push(f.observe())
      assert.deepEqual(f.overlay.selected.value, ['one'])
      assert.equal(f.overlay.minimized.value, true)
      assert.equal(f.board.focusCard.value?.instanceId, 'old-detail')
      await f.overlay.expandPrompt(); await flush()
      f.overlay.confirm()
      assert.equal(f.sent.length, 1)
      assert.deepEqual(f.sent[0].command.cardInstanceIds, ['one'])
    } finally { f.close() }
  })
  await test(`control seat${owner}: two authority prompts switching directly clear prior detail once`, async observations => {
    const f = fixture({ owner })
    try {
      await oldPrompt(f)
      const both = frame(3, [prompt('next-response', 'entry-card', owner), prompt('second-response', 'old-detail', owner)], owner)
      f.receive(both); await flush()
      assert.equal(f.board.focusCard.value, null)
      f.overlay.focusChoice('old-detail')
      const removeFirst = structuredClone(both); removeFirst.state.revision = 4; removeFirst.state.prompts.shift()
      f.receive(removeFirst); await flush()
      observations.push(f.observe())
      assert.equal(f.board.focusCard.value, null)
      assert.equal(f.overlay.prompt.value.promptId, 'second-response')
      const pass = f.buttons().find(n => n.props['data-tutorial-choice'] === 'pass')
      assert.ok(pass); pass.props.onClick()
      assert.equal(f.sent.length, 1); assert.equal(f.sent[0].command.promptId, 'second-response')
    } finally { f.close() }
  })
}
console.log(JSON.stringify({ suite: 'response prompt visibility, source-derived Vue reactive integration',
  scope: 'synthetic accepted net.ts frame -> same-instance SFC graph -> native response fragments in memory host', boardRef: boardRef ?? 'working-tree',
  limitations: ['No real browser geometry or center hit test', 'No production match replay', 'No engine-generated frame claim'],
  sourceHashes: Object.fromEntries(Object.entries(sources).map(([key, source]) => [key, createHash('sha256').update(source).digest('hex')])),
  nativeTemplateHash: createHash('sha256').update(nativeTemplate).digest('hex'), results,
}, null, 2))
if (results.some(row => !row.passed)) process.exitCode = 1
