import assert from 'node:assert/strict'
import fs from 'node:fs'
import {
  attackConnectionGeometry,
  attackConnectionSourceColor,
  claimAttackConnection,
  createAttackConnectionClaimState,
} from '../src/l12/game/attackConnectionPresentation.ts'

const attack = (sequence, attacker = 'attacker', target = 'target') => ({
  sequence,
  type: 'attack',
  playerIndex: 0,
  text: '进攻',
  cards: [
    { instanceId: attacker },
    ...(target ? [{ instanceId: target }] : []),
  ],
})
const pending = (target = 'target') => ({
  attackerPlayer: 0,
  attackerInstanceId: 'attacker',
  target: target ? { type: 'legion', instanceId: target } : { type: 'master' },
  stage: 'DefenseChoice',
  attackValue: 3000,
})
const claim = (state, overrides = {}) => claimAttackConnection(state, {
  matchId: 'match-a',
  revision: 1,
  synchronizing: false,
  events: [],
  pending: null,
  ...overrides,
})

{
  const geometry = attackConnectionGeometry(
    { left: 100, top: 300, width: 100, height: 140 },
    { left: 420, top: 80, width: 100, height: 140 },
    800,
  )
  assert.ok(geometry, 'valid objects must produce an arc')
  assert.ok(geometry.startX >= 100 && geometry.startX <= 200, 'arc starts on the source edge')
  assert.ok(geometry.endX >= 420 && geometry.endX <= 520, 'arc ends on the target edge')
  assert.match(geometry.path, /^M [-\d.]+ [-\d.]+ Q [-\d.]+ [-\d.]+ [-\d.]+ [-\d.]+$/)
  assert.equal(geometry.path.includes('NaN'), false)
  assert.ok(Math.hypot(geometry.controlX - (geometry.startX + geometry.endX) / 2,
    geometry.controlY - (geometry.startY + geometry.endY) / 2) <= 20.01,
  'the relationship line must retain only a slight bend')
  const reverse = attackConnectionGeometry(
    { left: 420, top: 80, width: 100, height: 140 },
    { left: 100, top: 300, width: 100, height: 140 },
    800,
  )
  assert.ok(reverse && reverse.controlX < (reverse.startX + reverse.endX) / 2,
    'the reverse perspective must still bow away from the central lane')
  const crossing = attackConnectionGeometry(
    { left: 640, top: 510, width: 100, height: 140 },
    { left: 640, top: 300, width: 100, height: 140 },
    1440,
  )
  assert.ok(crossing?.path.includes(' Q ') && !crossing.path.includes(' C '),
    'the line must pass through the information lane without an obstacle detour')
}

for (const [source, target] of [
  [{ left: 0, top: 0, width: 10, height: 10 }, { left: 0, top: 0, width: 10, height: 10 }],
  [{ left: Number.NaN, top: 0, width: 10, height: 10 }, { left: 20, top: 20, width: 10, height: 10 }],
  [{ left: 0, top: 0, width: -1, height: 10 }, { left: 20, top: 20, width: 10, height: 10 }],
]) assert.equal(attackConnectionGeometry(source, target, 800), null, 'zero or invalid geometry must draw nothing')

{
  const state = createAttackConnectionClaimState()
  assert.deepEqual(claim(state, { revision: 10, events: [attack(10)], pending: pending() }), { kind: 'baseline' },
    'an initial live/reconnect snapshot retains context without replaying history')
  assert.deepEqual(claim(state, { revision: 10, events: [attack(10)], pending: pending() }), { kind: 'retain' },
    'the same authority envelope cannot replay the draw-on motion')
  assert.deepEqual(claim(state, { revision: 11, events: [attack(10), attack(11, 'other', 'target')], pending: pending() }), { kind: 'retain' },
    'an unrelated attack event cannot animate the pending combat')
  assert.deepEqual(claim(state, { revision: 12, events: [attack(10), attack(12)], pending: pending() }), { kind: 'intro', sequence: 12 },
    'one fresh matching authority attack owns one intro')
  assert.deepEqual(claim(state, { revision: 13, events: [attack(10), attack(12)], pending: pending() }), { kind: 'retain' },
    'stage-only revisions retain the connection without replay')
  assert.deepEqual(claim(state, { revision: 14, events: [attack(12)], pending: null }), { kind: 'none' },
    'target invalidation removes the connection')
}

{
  const state = createAttackConnectionClaimState()
  claim(state, { revision: 20, events: [attack(20)], pending: pending() })
  assert.deepEqual(claim(state, { revision: 21, synchronizing: true, events: [attack(20), attack(21)], pending: pending() }), { kind: 'baseline' },
    'authority recovery establishes a baseline instead of replaying')
  assert.deepEqual(claim(state, { revision: 8, events: [attack(8)], pending: pending() }), { kind: 'baseline' },
    'a backward replay seek establishes a baseline instead of replaying')
  assert.deepEqual(claim(state, { revision: 9, events: [attack(8), attack(9)], pending: pending() }), { kind: 'intro', sequence: 9 },
    'forward replay presents the next fresh authority event once')
}

{
  const state = createAttackConnectionClaimState()
  claim(state, { revision: 1, events: [], pending: null })
  assert.deepEqual(claim(state, { revision: 2, events: [attack(2, 'attacker', null)], pending: pending(null) }), { kind: 'intro', sequence: 2 },
    'master attacks use the same authority sequence')
  assert.equal(attackConnectionSourceColor(0, 0), '#65dce4')
  assert.equal(attackConnectionSourceColor(0, 1), '#f06f78')
}

const layer = fs.readFileSync(new URL('../src/l12/game/AttackConnectionPresentationLayer.vue', import.meta.url), 'utf8')
const board = fs.readFileSync(new URL('../src/l12/game/GameBoard.vue', import.meta.url), 'utf8')
assert.ok(layer.includes('if (!(source instanceof HTMLElement) || !(target instanceof HTMLElement))'),
  'an attacker leaving or a target disappearing must suppress the overlay')
assert.ok(layer.includes("window.addEventListener('l12-viewport-change', refreshGeometry)"),
  'the connection must remeasure in the logical mobile canvas')
assert.ok(layer.includes('@media(prefers-reduced-motion:reduce)'), 'reduced motion must keep a static relation without moving effects')
assert.ok(layer.includes('.attack-connection-layer.is-paused .attack-connection-orb{visibility:hidden}'),
  'blocking modals must suppress the SMIL travel marker while the authority relation remains visible')
assert.ok(layer.includes('.attack-connection-core{stroke-width:4;')
  && layer.includes('.attack-connection-glow{stroke-width:12;'), 'the connection must remain visibly substantial')
assert.ok(layer.includes('data-ui-contract="authoritative-attack-connection"'))
assert.ok(!board.includes('class="combat-trace"') && !board.includes('.combat-trace{'),
  'the fixed decorative slash must not remain beside the real connection')

console.log('L12 attack connection presentation passed')
