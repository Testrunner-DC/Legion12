import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'
import { computed, effectScope, nextTick, proxyRefs, reactive, ref, watch } from 'vue'

// Actual SFC computed expressions, template event handlers and lifecycle watches.
// Synthetic public zones only; no browser, server/WS, or product source mutation.
const files = { board: 'GameBoard.vue', grave: 'GraveyardOverlay.vue', overlay: 'PromptOverlay.vue' }
const sources = Object.fromEntries(Object.entries(files).map(([key, file]) => [key,
  readFileSync(new URL('../src/l12/game/' + file, import.meta.url), 'utf8')]))
const descriptors = Object.fromEntries(Object.entries(sources).map(([key, source]) => [key, parse(source).descriptor]))
const trees = Object.fromEntries(Object.entries(descriptors).map(([key, descriptor]) => [key,
  ts.createSourceFile(key + '.ts', descriptor.scriptSetup.content, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)]))
function run(code, env) {
  return Function(...Object.keys(env), ts.transpileModule(code, {
    compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS },
  }).outputText)(...Object.values(env))
}
function initializer(kind, name) {
  for (const node of trees[kind].statements) if (ts.isVariableStatement(node))
    for (const declaration of node.declarationList.declarations)
      if (declaration.name.getText(trees[kind]) === name && declaration.initializer)
        return declaration.initializer.getText(trees[kind])
  return null
}
function value(kind, name, env) {
  const text = initializer(kind, name); assert.ok(text, `${kind}.${name}`)
  return run('return (' + text + ')', env)
}
function actualFunction(kind, name, env) {
  const node = trees[kind].statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
  assert.ok(node, `${kind}.${name}`)
  return run(node.getText(trees[kind]) + '; return ' + name, env)
}
function actualWatch(kind, marker, env) {
  const nodes = trees[kind].statements.filter(node => ts.isExpressionStatement(node)
    && ts.isCallExpression(node.expression) && node.expression.expression.getText(trees[kind]) === 'watch'
    && node.getText(trees[kind]).includes(marker))
  assert.equal(nodes.length, 1, `${kind} watcher ${marker}`)
  run(nodes[0].getText(trees[kind]), env)
}
function templateNodes(kind, predicate) {
  const result = []
  function walk(node) {
    if (predicate(node)) result.push(node)
    for (const child of node.children ?? []) walk(child)
  }
  walk(descriptors[kind].template.ast)
  return result
}
const handlers = templateNodes('board', node => node.type === 1 && node.tag === 'PlayerMat')
  .flatMap(node => node.props.filter(prop => prop.type === 7 && prop.name === 'on' && prop.arg?.content === 'graveyard')
    .map(prop => prop.exp.content))
assert.equal(handlers.length, 2, 'both real player mats retain a graveyard handler')
const component = templateNodes('board', node => node.type === 1 && node.tag === 'GraveyardOverlay')
assert.equal(component.length, 1, 'both entrances share the existing graveyard component')
const inspectionBinding = component[0].props.find(prop => prop.type === 7 && prop.name === 'bind'
  && prop.arg?.content === 'inspection-only')?.exp?.content
assert.ok(inspectionBinding, 'read-only binding must remain present')
const evaluateTemplate = (expression, scope, event) =>
  Function('scope', '$event', 'with (scope) { return (' + expression + ') }')(proxyRefs(scope), event)

const ownPrompt = owner => ({ promptId: 'synthetic-owner-choice', playerIndex: owner, activationId: 'synthetic-activation',
  step: 0, createdRevision: 1, kind: 'response', validChoices: ['candidate-a', 'pass'], minChoose: 1, maxChoose: 1, data: {} })
function fixture(owner, mode) {
  const scope = effectScope()
  const own = mode === 'own-expanded' || mode === 'own-minimized'
  const props = reactive({ game: { you: owner, revision: 1, turnSerial: 3, activePlayer: 1 - owner,
    phase: mode === 'passive-defense-wait' ? 'Defense' : 'Main',
    pendingDefense: mode === 'passive-defense-wait' ? { attackerPlayer: owner, stage: 'DefenseChoice' } : null,
    prompts: own ? [ownPrompt(owner)] : [],
    waitingPrompt: mode === 'opponent-waiting' ? { playerIndex: 1 - owner, kind: 'option' } : null,
    players: [0, 1].map(playerIndex => ({ playerIndex, name: 'Synthetic ' + playerIndex, mulliganDone: true,
      graveyard: [{ instanceId: 'public-grave-' + playerIndex, cardId: 'synthetic', name: 'Public synthetic card',
        abilities: [{ id: 'synthetic-ability', enabled: true }] }] })) } })
  const l12State = { gmEnabled: false, rankedClock: { operationRemainingMs: 30000 } }
  const board = { computed, watch, props, promptMinimized: ref(false), boardControlMinimized: ref(false),
    mobileMoralePickerMinimized: ref(false), combatDecisionMinimized: ref(false), graveyardPlayer: ref(null),
    selectedId: ref(null), mode: ref('play'), playArmed: ref(false), defenseIds: ref([]), supportIds: ref([]),
    masterPlayerIndex: ref(null), focusCard: ref(null), customDisasterSlot: ref(null), closeMobileTrialAbility() {} }
  board.mobileInspectorOpen = value('board', 'mobileInspectorOpen', { ref })
  for (const name of ['hasBlockingPrompt', 'inspectionLayerMinimized', 'blockingPromptIdentity'])
    board[name] = value('board', name, board)
  if (initializer('board', 'graveyardInspectionAllowed'))
    board.graveyardInspectionAllowed = value('board', 'graveyardInspectionAllowed', board)
  board.clearOrdinaryInteractionState = actualFunction('board', 'clearOrdinaryInteractionState', board)
  const overlay = { computed, watch, props: { get game() { return props.game }, suppressedPromptIds: [] }, l12State,
    selected: ref([]), hoveredChoice: ref(null), minimized: ref(false), placementTop: ref([]), placementBottom: ref([]),
    placementSelected: ref(null), draggedChoice: ref(null), placementOrder: ref([]),
    emit(type, value) { if (type === 'minimizedChange') board.promptMinimized.value = value } }
  for (const name of ['prompt', 'sandboxActorIndex', 'me']) overlay[name] = value('overlay', name, overlay)
  scope.run(() => {
    actualWatch('board', 'watch(blockingPromptIdentity', board)
    actualWatch('overlay', 'placementOrder.value =', overlay)
    actualWatch('overlay', "emit('minimizedChange', value)", overlay)
  })
  overlay.minimized.value = mode === 'own-minimized'
  const events = []
  return { props, board, overlay, l12State, events,
    open(target, handlerIndex = target) {
      evaluateTemplate(handlers[handlerIndex], board, target)
      return board.graveyardPlayer.value === target
    },
    select(target) {
      const graveProps = { get players() { return props.game.players }, initialPlayer: target,
        actorPlayerIndex: owner, mobileLayout: false,
        get inspectionOnly() { return evaluateTemplate(inspectionBinding, board) } }
      const env = { computed, props: graveProps, emit: (...event) => events.push(event) }
      env.player = value('grave', 'player', env)
      actualFunction('grave', 'selectCard', env)(props.game.players[target].graveyard[0])
    },
    close() { scope.stop() },
  }
}
const results = []
async function test(name, body) {
  const observations = []
  try { await body(observations); results.push({ name, passed: true, observations }) }
  catch (error) { results.push({ name, passed: false, error: error.message, observations }) }
}
const modes = ['opponent-turn', 'opponent-waiting', 'own-expanded', 'own-minimized', 'passive-defense-wait']
for (const owner of [0, 1]) for (const mode of modes)
  await test(`entrances owner${owner}: ${mode}`, async observations => {
    const f = fixture(owner, mode)
    try {
      await nextTick()
      for (const target of [0, 1]) {
        f.board.graveyardPlayer.value = null
        const opened = f.open(target)
        observations.push({ owner, mode, target, opened,
          inspectionOnly: evaluateTemplate(inspectionBinding, f.board) })
        assert.equal(opened, mode !== 'own-expanded', 'only an expanded own prompt requires minimizing before inspection')
      }
    } finally { f.close() }
  })
for (const owner of [0, 1]) {
  await test(`lifecycle owner${owner}: arriving own prompt closes a waiting graveyard inspection`, async observations => {
    const f = fixture(owner, 'opponent-waiting')
    try {
      await nextTick(); assert.equal(f.open(1 - owner), true)
      f.props.game = { ...f.props.game, revision: 2, prompts: [ownPrompt(owner)] }
      await nextTick()
      observations.push({ graveyardPlayer: f.board.graveyardPlayer.value, promptId: f.overlay.prompt.value.promptId })
      assert.equal(f.board.graveyardPlayer.value, null)
      assert.equal(f.overlay.prompt.value.promptId, 'synthetic-owner-choice')
      assert.equal(f.open(1 - owner), false, 'new own choice cannot be covered by reopening the graveyard')
    } finally { f.close() }
  })
  await test(`lifecycle owner${owner}: same own-prompt snapshot retains choice and minimized inspection`, async observations => {
    const f = fixture(owner, 'own-minimized')
    try {
      await nextTick(); f.overlay.selected.value = ['candidate-a']
      assert.equal(f.open(owner), true)
      f.props.game = JSON.parse(JSON.stringify(f.props.game)); f.props.game.revision++
      f.l12State.rankedClock.operationRemainingMs -= 1000
      await nextTick()
      observations.push({ selected: [...f.overlay.selected.value], minimized: f.overlay.minimized.value,
        graveyardPlayer: f.board.graveyardPlayer.value })
      assert.deepEqual(f.overlay.selected.value, ['candidate-a'])
      assert.equal(f.overlay.minimized.value, true)
      assert.equal(f.board.graveyardPlayer.value, owner)
    } finally { f.close() }
  })
  for (const mode of ['opponent-waiting', 'own-minimized'])
    await test(`read-only owner${owner}: ${mode} never requests either player's graveyard ability`, async observations => {
      const f = fixture(owner, mode)
      try {
        await nextTick()
        for (const target of [0, 1]) {
          assert.equal(f.open(target), true)
          assert.equal(evaluateTemplate(inspectionBinding, f.board), true)
          f.select(target)
        }
        observations.push({ eventTypes: f.events.map(event => event[0]) })
        assert.deepEqual(f.events.map(event => event[0]), ['focus', 'inspect', 'focus', 'inspect'])
        assert.equal(f.events.filter(event => event[0] === 'ability').length, 0)
      } finally { f.close() }
    })
  await test(`positive ability control owner${owner}: inspection-only is not replaced by an inert event sink`, async observations => {
    const f = fixture(owner, 'opponent-turn')
    try {
      await nextTick(); assert.equal(f.open(owner), true)
      assert.equal(evaluateTemplate(inspectionBinding, f.board), false)
      f.select(owner)
      observations.push({ eventTypes: f.events.map(event => event[0]) })
      assert.deepEqual(f.events.map(event => event[0]), ['focus', 'inspect', 'ability'])
    } finally { f.close() }
  })
}
console.log(JSON.stringify({ suite: 'passive wait public graveyard inspection', scope: 'actual SFC expressions, both template handlers and lifecycle watches',
  limitations: ['No native browser hit test', 'No live player, HTTP or WebSocket', 'Ability-event boundary, not a server authorization claim'],
  handlers, inspectionBinding,
  sourceSha256: Object.fromEntries(Object.entries(sources).map(([key, source]) => [key, createHash('sha256').update(source).digest('hex')])), results,
}, null, 2))
if (results.some(row => !row.passed)) process.exitCode = 1
