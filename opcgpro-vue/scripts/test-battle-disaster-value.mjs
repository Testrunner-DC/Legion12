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
