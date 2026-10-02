import assert from 'node:assert/strict'
import { performance } from 'node:perf_hooks'
import { createServer } from 'vite'

const server = await createServer({ configLoader: 'runner', server: { middlewareMode: true }, appType: 'custom', optimizeDeps: { noDiscovery: true } })
const card = (name, hidden = false) => ({ InstanceId: 'source', CardId: 'CARD', Name: name, Hidden: hidden })
const event = (sequence, type = 'play', extra = {}) => ({ Sequence: sequence, Type: type, Cards: [card('公开卡')], ...extra })
const frame = (round, active, events, extra = {}) => ({ state: { Round: round, ActivePlayer: active, Phase: 'Main', Events: events, ...extra }, command: { sourceCardId: 'SECRET-COMMAND' }, accepted: true })
const detail = commands => ({ viewerPlayerIndex: 0, commands, match: {} })
try {
  const { buildReplayIndex } = await server.ssrLoadModule('/src/l12/replayIndex.ts')
  const first = event(1)
  const result = buildReplayIndex(detail([frame(1, 0, [first]), frame(1, 0, [first, event(2, 'cost')]), frame(2, 1, [first, event(2, 'cost'), event(3, 'effect-result', { EffectResultStatus: 'negated' })])]))
  assert.deepEqual(result.rounds.map(x => [x.step, x.round, x.activePlayer]), [[0, 1, 0], [2, 2, 1]])
  assert.deepEqual(result.events.map(x => [x.step, x.category]), [[0, 'action'], [1, 'cost'], [2, 'effect']])
  assert.match(result.events[2].label, /被无效/)
  const secrets = buildReplayIndex(detail([
    frame(1, 0, [event(4, 'play', { Cards: [card('未来才公开', true)], Text: '隐藏名称私密正文' }), event(5, 'cost', { PrivateTo: 1, Text: '不应见到' }), event(6, 'effect-result', { EffectResultStatus: 'failed', Cards: [], PlayerLogSemantic: { SourceName: '秘密' } })]),
    frame(2, 1, [event(4, 'play', { Cards: [card('未来才公开')] })]),
  ]))
  assert(!JSON.stringify(secrets).includes('未来才公开'))
  assert(!JSON.stringify(secrets).includes('私密正文'))
  assert(!JSON.stringify(secrets).includes('秘密'))
  assert(!JSON.stringify(secrets).includes('SECRET-COMMAND'))
  assert.equal(secrets.events.length, 2)
  const privileged = buildReplayIndex(detail([frame(1, 0, [event(7, 'counter-set', { Cards: [card('盖伏卡', true)] })])]), true)
  assert.match(privileged.events[0].label, /盖伏卡/)
  const legacyA = event(0, 'play')
  const legacyB = event(0, 'cost')
  const legacy = buildReplayIndex(detail([frame(1, 0, [legacyA]), frame(1, 0, [legacyA, legacyB]), frame(2, 1, [legacyB, legacyA])]))
  assert.deepEqual(legacy.events.map(x => x.step), [0, 1, 2], 'rolling legacy windows retain a new identical action, not a global text dedupe')
  assert.deepEqual(buildReplayIndex(detail([{ state: {} }, frame(1, 0, [], { Round: undefined })])).rounds, [])
  const camel = buildReplayIndex(detail([{ state: { round: 3, activePlayer: 0, events: [{ sequence: 1, type: 'attack', cards: [] }] } }]))
  assert.equal(camel.rounds[0].round, 3)
  assert.equal(camel.events.length, 1)
  for (const phase of [10, 'GameOver']) assert.equal(buildReplayIndex(detail([frame(1, 0, [], { Phase: phase })])).rounds.length, 0, 'terminal frame alone is not a turn start')
  const rejected = buildReplayIndex(detail([frame(1, 0, [event(10)]), { ...frame(1, 0, [event(10)]), accepted: false, error: 'SECRET-ERROR' }]))
  assert.equal(rejected.events.length, 1)
  const many = Array.from({ length: 10000 }, (_, step) => frame(1 + Math.floor(step / 20), step % 2, Array.from({ length: Math.min(64, step + 1) }, (_, offset) => event(step - Math.min(63, step) + offset + 1))))
  const started = performance.now()
  const large = buildReplayIndex(detail(many))
  const elapsed = performance.now() - started
  assert.equal(large.events.length, 10000)
  assert(elapsed < 2500, `10000-frame index exceeded budget: ${elapsed.toFixed(1)}ms`)
  console.log(`Replay index: frame privacy, legacy rolling windows, exact positions, outcome/cost, admin display and 10000 frames passed (${elapsed.toFixed(1)}ms).`)
} finally { await server.close() }
