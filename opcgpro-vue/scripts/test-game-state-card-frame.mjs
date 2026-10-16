import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const root = fileURLToPath(new URL('..', import.meta.url))
const modulePath = join(root, 'src', 'l12', 'gameStateCardFrame.ts')
const netPath = join(root, 'src', 'l12', 'net.ts')
const source = readFileSync(modulePath, 'utf8')
const compiled = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, isolatedModules: true },
  fileName: modulePath,
  reportDiagnostics: true,
})
const errors = (compiled.diagnostics || []).filter(item => item.category === ts.DiagnosticCategory.Error)
assert.deepEqual(errors, [], 'gameStateCardFrame.ts must transpile without diagnostics')
const codec = await import(`data:text/javascript;base64,${Buffer.from(compiled.outputText).toString('base64')}`)

const card = { instanceId: 'synthetic', cardId: 'SYNTHETIC', hidden: false,
  nested: { values: [1, 2] }, text: '纯合成合同数据' }
const payload = { type: 'gameState', requestId: 'request-synthetic', matchGovernanceResult: { status: 'accepted' },
  state: { matchId: 'match-synthetic', revision: 9, stateHash: 'hash-9', phase: 'GameOver',
    recentEvents: [{ sequence: 1, cards: null }, { sequence: 2, cards: null }],
    lastAction: { sequence: 2, cards: null } } }
const wire = { type: 'gameStateCardFrameV1', payload, eventCardRefs: [[0], [0]],
  lastActionCardRefs: [0], cardSnapshots: [card] }
const expected = structuredClone(payload)
for (const event of expected.state.recentEvents) event.cards = [structuredClone(card)]
expected.state.lastAction.cards = [structuredClone(card)]

let checks = 0
const restored = codec.restoreGameStateCardFrame(structuredClone(wire))
assert.deepEqual(restored, expected); checks += 1
assert.equal(restored.requestId, 'request-synthetic'); checks += 1
assert.equal(restored.matchGovernanceResult.status, 'accepted'); checks += 1
restored.state.recentEvents[0].cards[0].nested.values.push(3)
assert.deepEqual(restored.state.recentEvents[1].cards[0].nested.values, [1, 2]); checks += 1
assert.deepEqual(restored.state.lastAction.cards[0].nested.values, [1, 2]); checks += 1

const invalid = []
const reject = mutate => { const value = structuredClone(wire); mutate(value); invalid.push(value) }
reject(value => { value.eventCardRefs[0][0] = -1 })
reject(value => { value.eventCardRefs[0][0] = 1 })
reject(value => { value.eventCardRefs[0][0] = 0.5 })
reject(value => { value.eventCardRefs.pop() })
reject(value => { value.payload.state.recentEvents[0].cards = [] })
reject(value => { value.cardSnapshots[0] = null })
reject(value => { value.extra = true })
reject(value => { value.lastActionCardRefs = null })
reject(value => { value.cardSnapshots.push({ instanceId: 'unused' }) })
for (const value of invalid) {
  const frozen = structuredClone(value)
  assert.equal(codec.restoreGameStateCardFrame(value), null)
  assert.deepEqual(value, frozen, 'invalid input must not be partially reconstructed')
  checks += 1
}
assert.equal(codec.restoreGameStateCardFrame(structuredClone(wire), ' '.repeat(2 * 1024 * 1024 + 1)), null); checks += 1

const recovery = new codec.GameStateCardFrameRecovery()
let plainRequests = 0
let pendingActionResentAttempt = 4
const attempt = 4
if (recovery.begin('same-request-id')) { plainRequests += 1; pendingActionResentAttempt = -1 }
if (recovery.begin('same-request-id')) plainRequests += 1
assert.equal(plainRequests, 1); checks += 1
assert.equal(pendingActionResentAttempt, -1); checks += 1
recovery.recordPublishedPlainFrame(expected)
const completion = recovery.finish()
assert.deepEqual(completion, { ready: true, pendingRequestId: 'same-request-id' }); checks += 1
assert.notEqual(pendingActionResentAttempt, attempt, 'same connection attempt must remain eligible for same-id retry'); checks += 1
assert.equal(recovery.disabled, true); checks += 1
assert.equal(recovery.begin('must-not-restart-on-this-connection'), false); checks += 1

const noPlain = new codec.GameStateCardFrameRecovery()
assert.equal(noPlain.begin('pending'), true)
assert.deepEqual(noPlain.finish(), { ready: false, pendingRequestId: '' }); checks += 1

const netSource = readFileSync(netPath, 'utf8')
assert.equal((netSource.match(/plainFull: true/g) || []).length, 1); checks += 1
assert.match(netSource, /gameStateCardFrameV1: true/); checks += 1
assert.match(netSource, /pendingActionResentAttempt = -1/); checks += 1

console.log(`Game-state card frame contract: ${checks}/${checks} checks passed`)
