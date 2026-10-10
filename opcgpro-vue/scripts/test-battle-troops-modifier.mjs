import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const card = (instanceId = 'target') => ({ instanceId, cardId: 'same-card', name: '同名军团',
  cardType: 'legion', faction: 'universal', cost: 2, baseTroops: 1000, troops: 0, hidden: false })
const fact = { targetInstanceId: 'target', targetControllerPlayerIndex: 0,
  troopsDelta: -2000, durationCode: 'this-turn' }
const event = { sequence: 1, type: 'troops-modifier', playerIndex: 0,
  text: '秘密来源/并未发生的击杀/兵力+999999', cards: [card()], playerTroopsModifier: fact }
const rows = (events = [event], you = 0, neutral = false) => projectLog(events, you, [], neutral)
const words = row => row.parts.map(part => part.text).join('')
const omitted = '兵力修正详情未记录'

for (const you of [0, 1]) for (const owner of [0, 1]) for (const neutral of [false, true]) {
  const row = rows([{ ...event, playerIndex: owner,
    playerTroopsModifier: { ...fact, targetControllerPlayerIndex: owner } }], you, neutral)[0]
  assert.equal(row.actor, owner === you ? neutral ? '下方' : '我方' : neutral ? '上方' : '对方')
  assert.equal(words(row), '〈同名军团〉本回合兵力修正-2000')
  assert.equal(row.parts[0].card.instanceId, 'target')
  assert.equal(row.parts[0].card.troops, 0, 'public clamped snapshot is not a damage/kill result')
  assert.equal(row.detail, undefined)
}
for (const delta of [0, 1000, -2000, -2147483648, 2147483647]) {
  assert.equal(words(rows([{ ...event, playerTroopsModifier: { ...fact, troopsDelta: delta } }])[0]),
    `〈同名军团〉本回合兵力修正${delta > 0 ? '+' : ''}${delta}`)
}
for (const invalid of [undefined, null, {}, false, 1, 'forged',
  ...[null, '', 12, 'different'].map(targetInstanceId => ({ ...fact, targetInstanceId })),
  ...[null, undefined, '2000', 0.5, NaN, Infinity, 2147483648, -2147483649]
    .map(troopsDelta => ({ ...fact, troopsDelta })),
  ...[-1, 1, 2, null, '0'].map(targetControllerPlayerIndex => ({ ...fact, targetControllerPlayerIndex })),
  ...[null, 'combat', 'permanent'].map(durationCode => ({ ...fact, durationCode })),
]) {
  const row = rows([{ ...event, playerTroopsModifier: invalid }])[0]
  assert.equal(words(row), omitted)
  assert.equal(row.actor, null)
  assert(!row.parts.some(part => part.card), 'invalid fact never links a card')
}
for (const cards of [[], [card('other')], [card(), card()],
  [{ ...card(), hidden: true }], [{ ...card(), name: '' }], [{ ...card(), name: 123 }]]) {
  assert.equal(words(rows([{ ...event, cards }])[0]), omitted)
}
const sameName = rows([{ ...event, cards: [card('twin'), card()] }])[0]
assert.equal(sameName.parts[0].card.instanceId, 'target', 'same name never picks the first card')
const independent = rows([event, { ...event, sequence: 2,
  playerTroopsModifier: { ...fact, troopsDelta: 1000 } }, event])
assert.equal(independent.length, 2, 'retransmit deduplicates by sequence; successive writes remain')
assert.equal(words(independent[1]), '〈同名军团〉本回合兵力修正+1000')
assert.equal(rows([{ ...event, type: 'effect' }]).some(row => words(row).includes('本回合兵力修正')), false,
  'payload on unrelated event cannot claim authoritative modifier semantics')
assert(!JSON.stringify(rows()).includes('秘密来源'))
assert(!JSON.stringify(rows()).includes('击杀'))
assert.equal(words(rows([{ ...event, get text() { throw new Error('troops facts must never inspect Text') } }])[0]),
  '〈同名军团〉本回合兵力修正-2000')

const replay = events => replayGameAt({ match: { matchId: 'troops', roomCode: 'TROOPS' },
  viewerPlayerIndex: 1, commands: [{ state: { Events: events }, revision: 1 }] }, 0)
const pascal = { Sequence: 1, Type: event.type, PlayerIndex: 0, Text: event.text, Cards: event.cards,
  PlayerTroopsModifier: { TargetInstanceId: 'target', TargetControllerPlayerIndex: 0,
    TroopsDelta: -2000, DurationCode: 'this-turn' } }
for (const source of [event, pascal]) {
  const game = replay([source])
  assert.deepEqual(game.recentEvents[0].playerTroopsModifier, fact)
  assert.equal(words(rows(game.recentEvents, game.you)[0]), '〈同名军团〉本回合兵力修正-2000')
  assert.equal(rows(game.recentEvents, game.you)[0].actor, '对方')
}
for (const payload of [null, undefined, { TroopsDelta: 0 }, { TargetInstanceId: 3 }]) {
  assert.equal(words(rows(replay([{ ...pascal, PlayerTroopsModifier: payload }]).recentEvents)[0]), omitted)
}
const zero = replay([{ ...pascal, PlayerTroopsModifier: { ...pascal.PlayerTroopsModifier, TroopsDelta: 0 } }])
assert.equal(words(rows(zero.recentEvents)[0]), '〈同名军团〉本回合兵力修正0')
const pascalKeys = value => Array.isArray(value) ? value.map(pascalKeys)
  : value && typeof value === 'object' ? Object.fromEntries(Object.entries(value)
    .map(([key, item]) => [key[0].toUpperCase() + key.slice(1), pascalKeys(item)])) : value
for (const casing of ['camel', 'pascal']) for (const name of [undefined, null, '', '  ', 123, '真实名称', '未知卡牌']) {
  const recordedCard = { ...card() }
  if (name === undefined) delete recordedCard.name
  else recordedCard.name = name
  const recordedEvent = { ...event, cards: [recordedCard] }
  const game = replay([casing === 'pascal' ? pascalKeys(recordedEvent) : recordedEvent])
  const row = rows(game.recentEvents)[0]
  const valid = typeof name === 'string' && name.trim().length > 0
  assert.equal(words(row), valid ? `〈${name}〉本回合兵力修正-2000` : omitted,
    `${casing} raw name ${String(name)} cannot gain authority from replay display defaults`)
  assert.equal(row.parts.some(part => part.card), valid, 'incomplete recorded name must have no card link')
  if (!valid) assert.equal(game.recentEvents[0].playerTroopsModifier, undefined)
  if (name == null) assert.equal(game.recentEvents[0].cards[0].name, '未知卡牌',
    'generic legacy replay card fallback remains unchanged outside the authoritative fact')
}
console.log('Single-target this-turn troops facts: role, identity, zero, malformed, legacy, replay and duplicate boundaries passed')
