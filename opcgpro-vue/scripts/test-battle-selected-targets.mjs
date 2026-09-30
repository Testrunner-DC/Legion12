import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const card = { instanceId: 'public-source', cardId: 'S01-0004', name: '公开来源',
  cardType: 'legion', faction: '测试', cost: 2, baseTroops: 6000, troops: 6000, hidden: false }
const fact = { id: 'chosen-target', owner: 1, zone: 'field', row: 1, slot: 2,
  publicName: '公开目标', currentCost: 2, tapped: false, isGodPower: null }
const selected = { sequence: 1, type: 'target-selected', playerIndex: 0,
  text: '秘密审计文字与玩家姓名', cards: [card], playerLogGroupId: 'stack-1',
  playerLogTiming: 'active', playerSelectedTargets: { sourceInstanceId: card.instanceId, facts: [fact] } }
const activated = { sequence: 2, type: 'effect-activation', playerIndex: 0,
  text: '公开来源发动主动效果', cards: [card], playerLogGroupId: 'stack-1',
  playerLogTiming: 'active' }
const words = row => row.parts.map(part => part.text).join('')

for (const [you, expected] of [[0, '对方后排右格'], [1, '我方后排右格']]) {
  const rows = projectLog([activated, selected, selected], you, ['绝不能显示的甲', '绝不能显示的乙'])
  assert.equal(rows.length, 1, 'target selection and effect belong to one reviewable record')
  assert.equal(rows[0].detail.length, 1)
  assert.equal(words(rows[0].detail[0]), `已选目标：${expected}〈公开目标〉，声明时费用2`)
  assert(!JSON.stringify(rows).includes('秘密审计文字'))
  assert(!JSON.stringify(rows).includes('绝不能显示'))
}
const neutral = projectLog([selected, activated], 0, [], true)
assert(words(neutral[0].detail[0]).includes('上方后排右格〈公开目标〉'))
const covered = projectLog([{ ...selected, playerSelectedTargets: {
  sourceInstanceId: card.instanceId, facts: [{ ...fact, publicName: null, currentCost: null }],
} }], 0, [])
assert.equal(words(covered[0]), '〈公开来源〉：已选目标：对方后排右格盖伏卡牌')
assert(!JSON.stringify(covered).includes('公开目标'))
const morale = projectLog([{ ...selected, playerSelectedTargets: {
  sourceInstanceId: card.instanceId, facts: [{ id: 'morale', owner: 0, zone: 'morale',
    row: -1, slot: -1, publicName: null, currentCost: null, tapped: true, isGodPower: true }],
} }], 1, [])
assert(words(morale[0]).includes('对方士气区的休整神力面'))
const oldFace = projectLog([{ ...selected, playerSelectedTargets: {
  sourceInstanceId: card.instanceId, facts: [{ id: 'morale', owner: 0, zone: 'morale',
    row: -1, slot: -1, publicName: null, currentCost: null, tapped: true }],
} }], 1, [])
assert(words(oldFace[0]).includes('休整资源'), 'old V2 face must stay neutral')
assert.equal(projectLog([{ ...selected, playerSelectedTargets: undefined }], 0, []).length, 0,
  'old replay without a frozen choice must not infer it from the current board or Text')
assert.equal(projectLog([{ ...selected, cards: [{ ...card, hidden: true }] }], 1, []).length, 0)
assert.equal(projectLog([{ ...selected, playerSelectedTargets: {
  sourceInstanceId: card.instanceId, facts: [{ ...fact, zone: 'hand' }],
} }], 1, []).length, 0, 'private zone cannot become a public selected-target record')

const replay = replayGameAt({ match: { matchId: 'selected', roomCode: 'TARGET' },
  viewerPlayerIndex: 1, commands: [{ revision: 1, state: { Events: [{
    Sequence: 1, Type: 'target-selected', PlayerIndex: 0, Text: '秘密审计文字',
    Cards: [card], PlayerSelectedTargets: { SourceInstanceId: card.instanceId,
      Facts: [{ Id: fact.id, Owner: fact.owner, Zone: fact.zone, Row: fact.row, Slot: fact.slot,
        PublicName: fact.publicName, CurrentCost: fact.currentCost, Tapped: fact.tapped }] },
  }] } }] }, 0)
assert.equal(replay.recentEvents[0].playerSelectedTargets.facts[0].publicName, '公开目标')
assert(words(projectLog(replay.recentEvents, replay.you, [])[0]).includes('我方后排右格〈公开目标〉'))
console.log('Selected-target records: both views, neutral, covered, morale face, legacy, privacy and replay passed')
