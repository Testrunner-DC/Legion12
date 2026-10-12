import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'

const source = fs.readFileSync(new URL('../src/l12/net.ts', import.meta.url), 'utf8')
const body = source.slice(source.indexOf('function rebuildDeltaGameState('), source.indexOf('function clearReconnectTimer('))
const compiled = ts.transpileModule(body, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const rebuild = (baseline, delta, negotiated = true) => new Function('lastGameStateEnvelope', 'negotiatedEventWindowDelta', `${compiled}; return rebuildDeltaGameState(${JSON.stringify(delta)})`)(baseline, negotiated)
const frame = (revision, sequences) => ({ type: 'gameState', state: { matchId: 'window-client', revision,
  lastAction: { text: 'independent', cards: [] }, recentEvents: sequences.map(sequence => ({ sequence, text: `frozen-${sequence}`,
    cards: [{ instanceId: `i-${sequence}`, effectText: '冻结文本', timedModifiers: [{ troopsDelta: 1000, consumedTroopsBonus: 3 }],
      attachedCards: [{ cardId: 'same', instanceId: 'first' }, { cardId: 'same', instanceId: 'second' }] }] })) } })
const delta = (before, after, removeFirst) => ({ type: 'gameStateDelta', matchId: 'window-client', baseRevision: before.state.revision,
  revision: after.state.revision, changes: [{ path: ['state', 'revision'], value: after.state.revision, remove: false }],
  eventWindow: { baseSequences: before.state.recentEvents.map(e => e.sequence), removeFirst,
    append: after.state.recentEvents.slice(before.state.recentEvents.length - removeFirst), resultSequences: after.state.recentEvents.map(e => e.sequence) } })
let checks = 0
for (const [seqs, result, remove] of [[[1, 4, 8], [4, 8, 12], 1], [[1, 4], [1, 4, 8], 0], [[1, 4, 8], [4, 8], 1]]) {
  const a = frame(1, seqs), b = frame(2, result), d = delta(a, b, remove), frozen = JSON.stringify(a)
  assert.deepEqual(rebuild(a, d), b); assert.equal(JSON.stringify(a), frozen); checks++
  assert.equal(rebuild(a, d, false), null); checks++
}
const before = frame(1, [1, 4, 8]), after = frame(2, [4, 8, 12]), valid = delta(before, after, 1)
const faults = [
  d => { d.baseRevision = 0 }, d => { d.matchId = 'another' }, d => { d.revision = 3 },
  d => { d.eventWindow.baseSequences[0] = 2 }, d => { d.eventWindow.resultSequences[0] = 2 },
  d => { d.eventWindow.removeFirst = -1 }, d => { d.eventWindow.removeFirst = 4 }, d => { d.eventWindow.removeFirst = '1' }, d => { d.eventWindow.removeFirst = 1.5 },
  d => { d.eventWindow.removeFirst = 3; d.eventWindow.resultSequences = [12] },
  d => { d.eventWindow.append[0].sequence = 0 }, d => { d.eventWindow.append[0].sequence = -1 }, d => { d.eventWindow.append[0].sequence = '12' },
  d => { d.eventWindow.append[0].sequence = 12.5 }, d => { d.eventWindow.append[0].sequence = Number.MAX_SAFE_INTEGER + 1 },
  d => { d.eventWindow.append.push(structuredClone(d.eventWindow.append[0])) }, d => { d.eventWindow.append = null },
  d => { d.eventWindow = null }, d => { d.eventWindow = [] }, d => { delete d.eventWindow.resultSequences },
  d => { d.changes.push({ path: ['state', 'recentEvents', 0, 'text'], value: 'conflict' }) },
  d => { d.changes.push({ path: ['state'], value: {} }) }, d => { d.changes.push({ path: ['missing', 'path'], value: 1 }) },
]
for (const fault of faults) {
  const d = structuredClone(valid), frozen = JSON.stringify(before); fault(d)
  assert.equal(rebuild(before, d), null); assert.equal(JSON.stringify(before), frozen); checks++
}
const tooMany = frame(2, Array.from({ length: 129 }, (_, i) => i + 4)); const oversize = delta(before, tooMany, 3)
assert.equal(rebuild(before, oversize), null); checks++
const emptyBase = frame(1, []), emptyDelta = delta(emptyBase, frame(2, [12]), 0)
assert.equal(rebuild(emptyBase, emptyDelta), null); assert.deepEqual(emptyBase.state.recentEvents, []); checks++
const ordinary = { ...valid }; delete ordinary.eventWindow
ordinary.changes.push({ path: ['state', 'recentEvents'], value: after.state.recentEvents, remove: false })
assert.deepEqual(rebuild(before, ordinary, false), after); checks++
console.log(`Real net.ts event window reconstruction: ${checks}/${checks} checks passed`)

// Optional real-engine frames: decode server wire, then compare actual log/animation/recording inputs.
if (process.argv[2]) {
  const directory = process.argv[2]
  const compileTs = (relative, dependencies = {}) => {
    const text = fs.readFileSync(new URL(relative, import.meta.url), 'utf8')
    let output = ts.transpileModule(text, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText
    for (const [name, uri] of Object.entries(dependencies)) output = output.replace(name, uri)
    return `data:text/javascript;base64,${Buffer.from(output).toString('base64')}`
  }
  const movement = await import(compileTs('../src/l12/game/visualTransitionProjection.ts'))
  const targetUri = compileTs('../src/l12/game/battlefieldTargetPresentation.ts')
  const log = await import(compileTs('../src/l12/game/logViewModel.ts', { './battlefieldTargetPresentation.ts': targetUri }))
  let frames = 0, consumers = 0
  for (const actor of [0, 1]) for (const view of ['p0', 'p1', 'public', 'referee', 'gm']) {
    let baseline = null
    const directMovement = movement.createMovementClaimState(), decodedMovement = movement.createMovementClaimState()
    const directState = movement.createCardStateClaimState(), decodedState = movement.createCardStateClaimState()
    for (const label of ['127', '128', 'shift1', 'shift6']) {
      const filename = `${directory}/actor${actor}-${view}-${label}`
      const full = JSON.parse(fs.readFileSync(`${filename}.json`, 'utf8'))
      const wire = JSON.parse(fs.readFileSync(`${filename}-wire.json`, 'utf8'))
      const decoded = wire.type === 'gameState' ? wire : rebuild(baseline, wire)
      assert.deepEqual(decoded, full, `actual wire mismatch ${filename}`)
      const a = full.state, b = decoded.state
      assert.deepEqual(log.projectLog(b.recentEvents, b.you, ['匿名甲', '匿名乙'], view === 'public'),
        log.projectLog(a.recentEvents, a.you, ['匿名甲', '匿名乙'], view === 'public'))
      assert.deepEqual(movement.claimFreshMovementEvents(b.recentEvents, decodedMovement),
        movement.claimFreshMovementEvents(a.recentEvents, directMovement))
      assert.deepEqual(movement.claimCardStateTransitions(decodedState, b.revision, movement.collectVisualFieldState(b.players), b.recentEvents),
        movement.claimCardStateTransitions(directState, a.revision, movement.collectVisualFieldState(a.players), a.recentEvents))
      assert.deepEqual(movement.claimFreshMovementEvents(b.recentEvents, decodedMovement), [], 'duplicate must not replay movement')
      assert.deepEqual(movement.claimCardStateTransitions(decodedState, b.revision, movement.collectVisualFieldState(b.players), b.recentEvents), [], 'duplicate must not replay card rotation')
      for (const e of b.recentEvents) assert.deepEqual(movement.movementCardsForEvent(e), movement.movementCardsForEvent(a.recentEvents.find(original => original.sequence === e.sequence)))
      // Recording/replay input stays complete JSON, not a compact DTO reconstructed from current cards.
      assert.equal(JSON.stringify(decoded.state), JSON.stringify(full.state))
      baseline = decoded; frames++; consumers += 6
    }
  }
  console.log(`Actual engine wire/client consumers: frames=${frames}, consumerGroups=${consumers}, equivalent=true`)
}
