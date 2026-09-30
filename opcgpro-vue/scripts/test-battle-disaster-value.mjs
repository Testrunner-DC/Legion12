import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const event = (sequence, before, after, extra = {}) => ({
  sequence, type: 'disaster-value', playerIndex: null,
  text: '隐藏来源／伪造天灾值 999→1000', cards: [],
  playerDisasterValue: { before, after }, ...extra,
})
const words = row => row.parts.map(part => part.text).join('')
const rows = events => projectLog(events, 0, [])

assert.equal(words(rows([event(1, 7, 9)])[0]), '天灾值 7→9')
assert.equal(words(rows([event(1, 4, 4)])[0]), '天灾值保持 4')
assert.equal(words(rows([event(1, 4, 5, { text: '虚构增加0张士气' })])[0]),
  '天灾值 4→5', 'audit text cannot suppress a settled fact')
assert.equal(words(rows([event(1, 4, 5, { playerLogGroupId: 'effect:departed-source' })])[0]),
  '天灾值 4→5', 'a public value survives a group whose source is unavailable')
assert.equal(rows([event(1, 7, 9), event(1, 7, 9), event(2, 9, 0)]).length, 2)
for (const copies of [
  [event(1, 4, 5, { playerDisasterValue: {} }), event(1, 4, 5)],
  [event(1, 4, 5, { playerDisasterValue: undefined,
    playerLogGroupId: 'effect:old', playerLogTiming: 'active' }), event(1, 4, 5)],
]) {
  const projected = rows(copies)
  assert.equal(projected.length, 1, 'same-sequence copies produce one row')
  assert.equal(words(projected[0]), '天灾值 4→5',
    'a valid settled fact outranks an empty or metadata-rich duplicate')
}
assert.equal(words(rows([event(1, 4, 5), event(1, 7, 8)])[0]),
  '天灾值变化（详情未记录）', 'conflicting settled facts cannot be spliced or chosen arbitrarily')
for (const bad of [undefined, null, {}, { before: -1, after: 3 },
  { before: 2, after: 2147483648 }, { before: 2.5, after: 3 },
  { before: '2', after: 3 }]) {
  assert.equal(words(rows([event(1, 2, 3, { playerDisasterValue: bad })])[0]),
    '天灾值变化（详情未记录）')
}
assert.equal(words(rows([event(1, 7, 9, { type: 'effect' })])[0]), '发动效果',
  'a fact on the wrong event type cannot claim a disaster change')
assert(!JSON.stringify(rows([event(1, 7, 9)])).includes('隐藏来源'))

const source = { instanceId: 'source', cardId: 'S01-0111', name: '诸葛亮',
  cardType: 'legion', faction: 'universal', cost: 2, baseTroops: 1000, troops: 1000, hidden: false }
const grouped = rows([
  { sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果', cards: [source],
    playerLogGroupId: 'effect:source:1', playerLogTiming: 'active' },
  event(2, 7, 8, { playerLogGroupId: 'effect:source:1', playerLogTiming: 'active' }),
])
assert.equal(grouped.length, 1)
assert(words(grouped[0]).includes('天灾值 7→8'))
assert.equal(words(grouped[0].detail.find(row => row.sequence === 2)), '天灾值 7→8')
const turn = rows([
  { sequence: 1, type: 'turn-start', playerIndex: 0, text: '第2回合', cards: [],
    playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' },
  event(2, 4, 5, { playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
])
assert.equal(turn.length, 2)
assert.equal(words(turn[1]), '回合开始，天灾值 4→5')
const duplicatedGroupedTurn = rows([
  { sequence: 1, type: 'turn-start', playerIndex: 0, text: '第2回合', cards: [],
    playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' },
  event(2, 4, 5, { playerDisasterValue: undefined,
    playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
  event(2, 4, 5),
])
assert.equal(duplicatedGroupedTurn.length, 2)
assert.equal(words(duplicatedGroupedTurn[1]), '回合开始，天灾值 4→5',
  'deduplication retains the valid fact and the compatible turn grouping metadata')
for (const copies of [
  [event(2, 4, 5, { playerLogGroupId: 'effect:A' }),
    event(2, 4, 5, { playerDisasterValue: undefined,
      playerLogGroupId: 'turn:B', playerLogTiming: 'turn-start' })],
  [event(2, 4, 5, { playerDisasterValue: undefined,
    playerLogGroupId: 'turn:B', playerLogTiming: 'turn-start' }),
    event(2, 4, 5, { playerLogGroupId: 'effect:A' })],
]) {
  const projected = rows(copies)
  assert.equal(projected.length, 1)
  assert.equal(words(projected[0]), '天灾值 4→5')
  assert(!words(projected[0]).includes('回合开始'),
    'conflicting groups cannot graft turn-start timing onto an effect receipt')
}
for (const incompatible of [
  event(1, 4, 5, { type: 'effect', playerDisasterValue: undefined,
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' }),
  event(1, 4, 5, { playerIndex: 0, playerDisasterValue: undefined,
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' }),
]) {
  const projected = rows([event(1, 4, 5), incompatible])
  assert.equal(projected.length, 1)
  assert.equal(words(projected[0]), '天灾值 4→5',
    'different event type or player cannot donate grouping metadata')
}
const revealed = { instanceId: 'revealed', cardId: 'S01-0111', name: '公开卡',
  cardType: 'legion', faction: 'universal', cost: 2, baseTroops: 1000, troops: 1000, hidden: false }
for (const copies of [
  [{ sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果', cards: [revealed],
    playerLogSemantic: { actionLabel: '发动效果', outcomeLabel: '已完成' } },
  { sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果',
    cards: [{ ...revealed, hidden: true, name: '隐藏身份' }],
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' }],
  [{ sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果',
    cards: [{ ...revealed, hidden: true, name: '隐藏身份' }],
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' },
  { sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果', cards: [revealed],
    playerLogSemantic: { actionLabel: '发动效果', outcomeLabel: '已完成' } }],
]) {
  const projected = rows(copies)
  assert.equal(projected.length, 1)
  assert.equal(words(projected[0]), '发动效果',
    'a hidden-card copy must not inherit a public card or semantic fact')
  assert(!JSON.stringify(projected).includes('公开卡'))
  assert(!JSON.stringify(projected).includes('隐藏身份'))
  assert(!JSON.stringify(projected).includes('已完成'))
}
const differentCard = rows([
  { sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果', cards: [revealed],
    playerLogSemantic: { actionLabel: '发动效果', outcomeLabel: '已完成' } },
  { sequence: 1, type: 'effect', playerIndex: 0, text: '发动效果',
    cards: [{ ...revealed, instanceId: 'other', name: '另一张卡' }],
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' },
])
assert(!JSON.stringify(differentCard).includes('另一张卡'),
  'different public card identities cannot donate metadata or card facts')
const otherCard = { ...revealed, instanceId: 'other', name: '另一张卡' }
for (const copies of [
  [event(1, 4, 5, { cards: [revealed] }), event(1, 7, 8, { cards: [otherCard] })],
  [event(1, 7, 8, { cards: [otherCard] }), event(1, 4, 5, { cards: [revealed] })],
]) {
  const projected = rows(copies)
  assert.equal(words(projected[0]), '天灾值变化（详情未记录）',
    'different valid facts across incompatible card identities must degrade in either order')
  assert(!JSON.stringify(projected).includes('公开卡'))
  assert(!JSON.stringify(projected).includes('另一张卡'))
}
for (const copies of [
  [event(1, 4, 5, { cards: [revealed] }), event(1, 4, 5, { cards: [otherCard] })],
  [event(1, 4, 5, { cards: [otherCard] }), event(1, 4, 5, { cards: [revealed] })],
]) {
  const projected = rows(copies)
  assert.equal(words(projected[0]), '天灾值 4→5',
    'matching public scalar facts remain stable without choosing a card identity')
  assert(!JSON.stringify(projected).includes('公开卡'))
  assert(!JSON.stringify(projected).includes('另一张卡'))
}
for (const copies of [
  [event(1, 4, 5, { cards: [revealed],
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' }),
  event(1, 4, 5, { cards: [otherCard] })],
  [event(1, 4, 5, { cards: [otherCard] }),
  event(1, 4, 5, { cards: [revealed],
    playerLogGroupId: 'turn:foreign', playerLogTiming: 'turn-start' })],
]) {
  const projected = rows(copies)
  assert.equal(projected.length, 1)
  assert.equal(words(projected[0]), '天灾值 4→5',
    'same scalar value across different card identities cannot inherit a turn group')
}
for (const copies of [
  [event(1, 4, 5, { cards: [revealed] }),
    event(1, 4, 5, { cards: [{ ...otherCard, hidden: true, name: '隐藏身份' }],
      playerDisasterValue: undefined })],
  [event(1, 4, 5, { cards: [{ ...otherCard, hidden: true, name: '隐藏身份' }],
      playerDisasterValue: undefined }),
    event(1, 4, 5, { cards: [revealed] })],
]) {
  const projected = rows(copies)
  assert.equal(words(projected[0]), '天灾值变化（详情未记录）',
    'a hidden-card receipt must not inherit a fact from a different public card')
  assert(!JSON.stringify(projected).includes('公开卡'))
  assert(!JSON.stringify(projected).includes('隐藏身份'))
}
for (const fact of [undefined, {}, { before: 4, after: '5' }]) {
  const mixedTurn = rows([
    { sequence: 1, type: 'turn-start', playerIndex: 0, text: '第2回合', cards: [],
      playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' },
    { sequence: 2, type: 'draw', playerIndex: 0, text: '抽取 1 张牌', cards: [],
      playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' },
    event(3, 4, 5, { playerDisasterValue: fact,
      playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
  ])
  assert.equal(mixedTurn.length, 2)
  assert.equal(words(mixedTurn[1]), '回合开始，抽取1张牌，天灾值变化（详情未记录）',
    'turn grouping must retain a degraded disaster receipt alongside other summaries')
}

const replay = events => replayGameAt({ match: { matchId: 'disaster', roomCode: 'DISASTER' },
  viewerPlayerIndex: 1, commands: [{ state: { Events: events }, revision: 1 }] }, 0)
for (const raw of [event(1, 4, 5),
  { Sequence: 1, Type: 'disaster-value', PlayerIndex: null, Text: '秘密', Cards: [],
    PlayerDisasterValue: { Before: 4, After: 5 } }]) {
  const restored = replay([raw])
  assert.deepEqual(restored.recentEvents[0].playerDisasterValue, { before: 4, after: 5 })
  assert.equal(words(projectLog(restored.recentEvents, restored.you, [])[0]), '天灾值 4→5')
}
assert.equal(words(projectLog(replay([{ Sequence: 1, Type: 'disaster-value',
  Text: '天灾值 4→5', Cards: [] }]).recentEvents, 0, [])[0]),
  '天灾值变化（详情未记录）', 'legacy text does not become an authoritative value')
console.log('Public disaster value: settled numbers, grouping, replay, duplicates and legacy degradation passed')
