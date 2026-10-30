import assert from 'node:assert/strict'
import { projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

const card = (name, id, hidden = false) => ({ instanceId: id, cardId: id, name, hidden,
  cardType: 'legion', faction: '秩序', cost: 2, troops: 2000, baseTroops: 2000 })
const source = card('伊丽莎白一世', 'elizabeth')
const a = card('同名军团', 'mill-a')
const b = card('同名军团', 'mill-b')
const secret = card('不可泄露的私牌', 'private', true)
const ev = (sequence, type, text, cards = [], playerIndex = 0, extra = {}) =>
  ({ sequence, type, text, cards, playerIndex, ...extra })
const words = row => row.parts.map(p => p.text).join('')
const visible = rows => rows.map(row => row.kind !== 'line' ? row : ({ ...row, parts: row.parts.map(p => ({text:p.text, id:p.card?.instanceId})), detail: row.detail ? visible(row.detail) : undefined }))
const allRows = rows => rows.flatMap(row => row.kind === 'line' ? [row, ...allRows(row.detail ?? [])] : [])
let scenarios = 0
const failures = []
function check(name, body) { scenarios++; try { body() } catch (error) { failures.push({ name, error: error.message }) } }

for (const text of ['所选士气下个重置阶段无法转为活跃', '此军团无法因效果转为活跃',
  '所选军团下个对方重置阶段不能转为活跃', '下次击杀后转为活跃',
  '获得本回合下一次击杀对方军团后转为活跃效果']) {
  check(`no positive readiness: ${text}`, () => {
    const rows = projectLog([ev(1, 'effect', text)], 0, [])
    assert.equal(rows.length, 1)
    assert(!rows[0].badges.some(b => b.value === '转为活跃'), 'a restriction or future grant is not a completed ready action')
  })
}
check('actual positive readiness retained', () => {
  const row = projectLog([ev(1, 'effect', '来源卡使目标军团转为活跃', [source, a])], 0, [])[0]
  assert(row.badges.some(b => b.value === '转为活跃'))
})
check('semantic lock retains actual source and count', () => {
  const rows = projectLog([ev(1, 'effect', 'audit text', [source], 0, { playerLogSemantic: {
    actionLabel: '发动登场时效果', outcomeLabel: '对方2张士气下个重置阶段无法转为活跃',
    sourceInstanceId: source.instanceId, sourceName: source.name,
  } })], 0, [])
  assert.equal(rows.length, 1)
  assert.equal(words(rows[0]), '〈伊丽莎白一世〉发动登场时效果，对方2张士气下个重置阶段无法转为活跃')
  assert.equal(rows[0].badges.length, 0)
})

for (const actor of [0, 1]) for (const you of [0, 1, -1]) for (const grouped of ['turn', 'effect', 'standalone']) {
  check(`mill actor=${actor} viewer=${you} grouping=${grouped}`, () => {
    const extra = grouped === 'standalone' ? {} : { playerLogGroupId: `${grouped}:2`, playerLogTiming: grouped === 'turn' ? 'turn-start' : 'enter' }
    const events = [
      ...(grouped === 'turn' ? [ev(1, 'turn-start', '第 13 回合', [], actor, extra)] : []),
      ...(grouped === 'effect' ? [ev(1, 'play', '打出来源', [source], actor, extra)] : []),
      ev(2, 'mill', '弃置牌库顶部2张牌', [a, b], actor, extra),
    ]
    const rows = projectLog(events, you, [], you === -1)
    const row = rows.find(r => r.kind === 'line')
    assert(row, 'one public action must remain')
    assert.deepEqual(row.parts.filter(p => p.card).map(p => p.card.instanceId), grouped === 'effect' ? [source.instanceId, a.instanceId, b.instanceId] : [a.instanceId, b.instanceId], 'the primary row keeps both public instances in authoritative order')
    assert.equal(allRows(rows).flatMap(r => r.parts).filter(p => p.card?.instanceId === a.instanceId).length, 1, 'mill card must not be repeated in child details')
    const duplicates = projectLog([...events.slice().reverse(), ...events], you, [], you === -1)
    assert.deepEqual(duplicates, rows, 'reconnect duplicates and ordering must be stable')
    const replay = replayGameAt({ match: { matchId: 'log-mill', roomCode: 'LOG-MILL' },
      commands: [{ revision: 1, state: { MatchId: 'log-mill', RoomCode: 'LOG-MILL', Players: [], Events: events } }], viewerPlayerIndex: you }, 0)
    assert(replay)
    assert.deepEqual(visible(projectLog(replay.recentEvents ?? [], you, [], you === -1)), visible(rows), 'replay normalization preserves public wording and card identities')
  })
}
for (const cards of [[a, secret], [], [secret]]) for (const grouping of ['turn', 'effect']) {
  check(`private or missing cards ${cards.length} ${grouping}`, () => {
    const extra = { playerLogGroupId: `${grouping}:private`, playerLogTiming: grouping === 'turn' ? 'turn-start' : 'enter' }
    const rows = projectLog([
      ev(1, grouping === 'turn' ? 'turn-start' : 'play', '第 13 回合', grouping === 'turn' ? [] : [source], 0, extra),
      ev(2, 'mill', '弃置牌库顶部2张牌，私牌名字不能从审计文本提取', cards, 0, extra),
    ], 1, [])
    assert(!JSON.stringify(rows).includes(secret.name))
    assert(!JSON.stringify(rows).includes('私牌名字不能从审计文本提取'))
    const row = rows.find(r => r.kind === 'line')
    assert.match(words(row), /2张牌/)
    assert.deepEqual(row.parts.filter(p => p.card && p.card.instanceId !== source.instanceId).map(p => p.card.instanceId), cards.includes(a) ? [a.instanceId] : [])
  })
}
check('opponent mill retains actor in an ordinary effect group', () => {
  const extra = { playerLogGroupId: 'play:opponent', playerLogTiming: 'play' }
  const row = projectLog([ev(1, 'play', 'source', [source], 0, extra), ev(2, 'mill', '弃置牌库顶部2张牌', [a,b], 1, extra)], 0, [])[0]
  assert.match(words(row), /对方弃置牌库顶部2张牌/)
})
check('multiple mill receipts retain event and card order', () => {
  const extra = { playerLogGroupId: 'turn:multi', playerLogTiming: 'turn-start' }
  const row = projectLog([ev(1, 'turn-start', '第 13 回合', [], 0, extra),
    ev(2, 'mill', '弃置牌库顶部1张牌', [b], 0, extra), ev(3, 'mill', '弃置牌库顶部1张牌', [a], 0, extra)], 0, [])[1]
  assert.deepEqual(row.parts.filter(p=>p.card).map(p=>p.card.instanceId), [b.instanceId, a.instanceId])
})
for (const grouping of ['turn', 'effect']) for (const count of [0, 1, 2, 3]) {
  check(`contradictory/missing identities ${grouping} ${count}`, () => {
    const extra = { playerLogGroupId: `${grouping}:counts`, playerLogTiming: grouping === 'turn' ? 'turn-start' : 'enter' }
    const rows = projectLog([
      ev(1, grouping === 'turn' ? 'turn-start' : 'play', '第 13 回合', grouping === 'turn' ? [] : [source], 0, extra),
      ev(2, 'mill', `弃置牌库顶部${count}张牌`, [a,b], 0, extra),
    ], 0, [])
    const row = rows.find(r => r.kind === 'line')
    const visibleCards = row.parts.filter(p => p.card && p.card.instanceId !== source.instanceId)
    assert.equal(visibleCards.length, count >= 2 ? 2 : 0, 'an inconsistent count cannot select a guessed subset of cards')
  })
}
for (const text of ['未能转为活跃', '转为活跃失败', '转为活跃被无效',
  '不能转为休整', '无法横置', '未能转为休整', '转为休整失败']) {
  check(`negative and failed state wording ${text}`, () => {
    const row = projectLog([ev(1, 'effect', text, [source])], 0, [])[0]
    assert(!row.badges.some(b => ['转为活跃', '转为休整'].includes(b.value)))
  })
}
for (const text of ['目标转为休整', '目标横置']) check(`actual positive rest ${text}`, () => {
  assert(projectLog([ev(1, 'effect', text, [source])], 0, [])[0].badges.some(b=>b.value==='转为休整'))
})
check('negative clause never suppresses a distinct completed ready action', () => {
  const row = projectLog([ev(1, 'effect', '另一张卡无法转为活跃，目标转为活跃', [source,a])], 0, [])[0]
  assert(row.badges.some(b => b.value === '转为活跃'))
})
check('hidden semantic source never supplies public identity', () => {
  const rows = projectLog([ev(1, 'effect', 'private text', [secret], 0, { playerLogSemantic: {
    sourceInstanceId: secret.instanceId, sourceName: secret.name, actionLabel: '发动效果', outcomeLabel: '所选1张士气下个重置阶段无法转为活跃',
  } })], 1, [])
  assert.equal(rows.length, 0)
})
for (const grouping of ['turn', 'effect', 'standalone']) check(`same event duplicate instance ${grouping}`, () => {
  const extra = grouping === 'standalone' ? {} : { playerLogGroupId: `${grouping}:duplicate`, playerLogTiming: grouping === 'turn' ? 'turn-start' : 'enter' }
  const events = [...(grouping === 'standalone' ? [] : [ev(1, grouping === 'turn' ? 'turn-start' : 'play', '第 13 回合', grouping === 'turn' ? [] : [source], 0, extra)]),
    ev(2, 'mill', '弃置牌库顶部2张牌', [a,a], 0, extra)]
  const row = projectLog(events, 0, []).find(r=>r.kind==='line')
  assert.equal(row.parts.filter(p=>p.card?.instanceId===a.instanceId).length, 1)
})
check('same sequence contradictory mill text does not invent a receipt', () => {
  const extra = { playerLogGroupId: 'turn:conflict', playerLogTiming: 'turn-start' }
  const rows = projectLog([ev(1, 'turn-start', '第 13 回合', [], 0, extra),
    ev(2, 'mill', '弃置牌库顶部1张牌', [a], 0, extra), ev(2, 'mill', '弃置牌库顶部2张牌', [a], 0, extra)], 0, [])
  assert.equal(allRows(rows).flatMap(r=>r.parts).filter(p=>p.card).length, 0)
})
check('same sequence hidden/public identity conflict does not disclose a card', () => {
  const rows = projectLog([ev(2, 'mill', '弃置牌库顶部1张牌', [a]), ev(2, 'mill', '弃置牌库顶部1张牌', [{...a, hidden:true}])], 1, [])
  assert(!JSON.stringify(rows).includes(a.name))
})
check('standalone count conflict keeps no guessed public identities', () => {
  const row = projectLog([ev(2, 'mill', '弃置牌库顶部1张牌', [a,b])], 0, [])[0]
  assert.equal(row.parts.filter(p=>p.card).length, 0)
})
for (const text of ['未能使目标下个重置阶段无法转为活跃', '使目标下个重置阶段无法转为活跃失败'])
  check(`failed restriction is not a successful restriction ${text}`, () => {
    const row = projectLog([ev(1, 'effect', text, [source])], 0, [])[0]
    assert(!row.badges.some(b => b.value.includes('转为活跃')))
  })
console.log(JSON.stringify({ scenarios, failed: failures.length, failures }, null, 2))
if (failures.length) process.exitCode = 1
