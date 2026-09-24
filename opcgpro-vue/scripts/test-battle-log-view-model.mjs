import assert from 'node:assert/strict'
import { PLAYER_LOG_REDLINE_TERMS, playerLogContainsForbiddenTerms, projectLog } from '../src/l12/game/logViewModel.ts'

const card = (name, instanceId = name, hidden = false) => ({
  instanceId, cardId: `ID-${instanceId}`, name, cardType: 'legion', faction: '秩序', cost: 2,
  baseTroops: 4000, troops: 4000, disasterLevel: 0, hidden,
})
const a = card('甲军团', 'a')
const b = card('乙军团', 'b')
const source = card('来源卡', 'source')
const event = (sequence, type, text, cards = [a], playerIndex = 0, extra = {}) => ({ sequence, type, text, cards, playerIndex, ...extra })

const representative = [
  event(1, 'turn-start', '第 2 回合'),
  event(2, 'play', '打出甲军团'),
  event(3, 'counter-set', '盖伏'),
  event(4, 'move', '位移2格'),
  event(5, 'put', '登场'),
  event(6, 'promotion', '晋升'),
  event(7, 'draw', '抽取2张牌', []),
  event(8, 'discard', '弃置'),
  event(9, 'grave', '进入墓地'),
  event(10, 'return', '从墓地返回手牌'),
  event(11, 'damage', '受到3点伤害'),
  event(12, 'heal', '恢复2点'),
  event(13, 'morale', '士气增加2'),
  event(14, 'runes', '符文增加1'),
  event(15, 'dice', '掷骰：5点'),
  event(16, 'effect', '兵力+1000', [source]),
  event(17, 'faction-effect', '发动阵营效果', [source]),
  event(18, 'initiative-choice', '选择后手', []),
  event(19, 'mulligan', '调度3张手牌', []),
  event(20, 'disaster', '本局天灾', [source]),
  event(21, 'disaster-active', '天灾生效', [source]),
  event(22, 'game-over', '胜利', [], 0),
]
const rows = projectLog(representative, 0, ['测试甲', '测试乙'])
assert.equal(rows.length, representative.length, 'each representative whitelist event must project exactly once')
assert.equal(rows[0].kind, 'turn')
assert.equal(rows.at(-1).kind, 'line')
assert.equal(rows.at(-1).parts[0].text, '我方胜利')
const movedRow = rows.find(row => row.kind === 'line' && row.sequence === 4)
assert(movedRow?.kind === 'line' && movedRow.parts.some(part => part.text === '已移动'))
assert(movedRow?.kind === 'line' && movedRow.badges.length === 0, 'battlefield movement must omit distance and coordinates')

const cancelled = projectLog([
  event(1, 'cost', '消耗2士气', [], 0),
  event(2, 'effect', '兵力+1000', [source], 0),
  event(3, 'effect-cancelled', '效果取消，未入栈', [source], 0),
], 0, [])
assert.equal(cancelled.length, 1, 'effect mother event must remain while cancellation noise is hidden')
assert(cancelled[0].kind === 'line' && cancelled[0].badges.some(item => item.value === '士气 −2'), 'cost must merge into its effect row')

const otherworldRune = projectLog([
  event(1, 'cost', '消耗2士气', [], 0),
  event(2, 'runes', '彼界阵营效果使我方获得1符文', [source], 0),
], 0, [])
assert.equal(otherworldRune.length, 1, 'resource result stays merged into the compact effect row')
assert(otherworldRune[0].kind === 'line' && otherworldRune[0].badges.some(item => item.value === '+1符文'),
  'otherworld faction rune gain must remain visible after compact projection')
assert(otherworldRune[0].kind === 'line' && otherworldRune[0].badges.some(item => item.value === '士气 −2'),
  'the compact faction result must retain its paid resource')

const discardedCost = card('公开费用牌', 'cost-card')
const effectWithCardCost = projectLog([
  event(1, 'cost', '〈来源卡〉弃置1张手牌作为发动费用', [source, discardedCost], 0),
  event(2, 'effect', '〈来源卡〉发动效果', [source], 0),
], 0, [])
assert.equal(effectWithCardCost.length, 1, 'a public card cost stays merged into one compact effect row')
assert(effectWithCardCost[0].kind === 'line'
  && effectWithCardCost[0].parts.some(part => part.card?.instanceId === discardedCost.instanceId),
  'the merged effect row must retain a publicly moved cost card')

const trial = projectLog([
  event(1, 'trial-action', '〈甲军团〉休整并发动试炼', [a], 0),
  event(2, 'trial', '试炼进度 0 → 2', [a], 0),
], 0, [])
assert.equal(trial.length, 1, 'usual trial action and its immediate progress must collapse into one row')
assert(trial[0].kind === 'line' && trial[0].badges.some(item => item.value === '休整'))
assert(trial[0].kind === 'line' && trial[0].badges.some(item => item.value === '试炼 0→2'),
  'trial progress must remain visible after compact projection')

const target = card('目标军团', 'target')
const stateChanges = projectLog([
  event(1, 'enter', '〈甲军团〉在前排休整登场', [a]),
  event(2, 'attach', '〈来源卡〉叠放至〈目标军团〉下方', [source, target]),
  event(3, 'counter-replaced', '〈甲军团〉被顶替并置入墓地', [a]),
  event(4, 'mill', '弃置牌库顶部2张牌', [a, b]),
  event(5, 'library', '〈甲军团〉返回牌库底部', [a]),
  event(6, 'reorder', '将 2 张牌放回牌库顶部、1 张牌放回牌库底部', []),
  event(7, 'disaster-value', '天灾值调整为 4', [source]),
  event(8, 'extra-turn', '本回合后追加1个回合', []),
], 0, [])
assert.equal(stateChanges.length, 8, 'public zone, deck, disaster and turn changes must each keep one compact row')
assert(stateChanges.every(row => row.kind === 'line'))
assert(stateChanges.some(row => row.kind === 'line' && row.badges.some(item => item.value === '天灾值 4')))

const standaloneCost = projectLog([event(1, 'cost', '弃置1张手牌作为费用', [a])], 0, [])
assert.equal(standaloneCost.length, 1, 'a paid cost without a mergeable result must not disappear')
assert(standaloneCost[0].kind === 'line' && standaloneCost[0].badges.some(item => item.value === '手牌 −1'))

const fieldEffect = projectLog([
  event(1, 'effect', '来源卡使目标军团转为活跃', [source, target]),
], 0, [])
assert(fieldEffect[0].kind === 'line' && fieldEffect[0].parts.some(part => part.card?.instanceId === target.instanceId),
  'a field-changing effect must retain its public target')
assert(fieldEffect[0].kind === 'line' && fieldEffect[0].badges.some(item => item.value === '转为活跃'))

const combat = projectLog([
  event(1, 'attack', '〈甲军团〉6000 vs 〈乙军团〉4000', [a, b], 0),
  event(2, 'defense', '乙军团进行抵挡', [b], 1),
  event(3, 'damage', '乙军团受到2点伤害', [b], 1),
  event(4, 'leave', '乙军团离场', [b], 1),
], 0, [])
assert.equal(combat.length, 1, 'a contiguous attack chain must collapse into one combat summary')
assert.equal(combat[0].kind, 'combat')
assert.equal(combat[0].result, '击破')
assert.equal(combat[0].detail.length, 3, 'combat detail must retain defense, damage and leave')

assert.equal(projectLog([event(1, 'damage', '兵力增加0点')], 0, []).length, 0, 'standalone zero change must disappear')
assert.equal(projectLog([event(1, 'effect-failed', '校验失败')], 0, []).length, 0, 'failed effects must disappear')

const publicHandAdd = projectLog([
  event(1, 'reveal', '〈来源卡〉展示〈甲军团〉并加入手牌', [a, source]),
], 0, [])
assert.equal(publicHandAdd.length, 1)
assert.equal(publicHandAdd[0].kind, 'line')
assert.equal(publicHandAdd[0].icon, 'hand-add')
assert(publicHandAdd[0].parts.some(part => part.card?.name === '甲军团'), 'publicly revealed hand-add card must stay focusable')

const hidden = card('绝密卡名', 'hidden', true)
assert.equal(projectLog([event(1, 'reveal', '〈绝密卡名〉加入手牌', [hidden])], 0, []).length, 0, 'private hand-add must expose neither log nor card name')
const ordinaryDraw = projectLog([event(1, 'draw', '抽取1张牌', [hidden])], 0, [])[0]
assert(ordinaryDraw.kind === 'line' && ordinaryDraw.parts.every(part => !part.text.includes('绝密卡名')), 'ordinary draw must not reveal card names')

const privateHandAdd = projectLog([
  event(1, 'authority-event', '测试甲因效果将1张牌加入手牌，进入响应时点', [], 0),
], 0, ['测试甲', '测试乙'])
assert.equal(privateHandAdd.length, 1, 'a private effect hand change must retain a generic count-only row')
assert.equal(JSON.stringify(privateHandAdd).includes('绝密卡名'), false)
assert.equal(JSON.stringify(privateHandAdd).includes('响应时点'), false)

const deduplicatedPublicHandAdd = projectLog([
  event(1, 'reveal', '〈来源卡〉展示〈甲军团〉并加入手牌', [a, source]),
  event(2, 'authority-event', '测试甲因效果将1张牌加入手牌，进入响应时点', [], 0),
], 0, [])
assert.equal(deduplicatedPublicHandAdd.length, 1, 'a public hand add and its authority timing must not render twice')

const safeRows = projectLog([
  ...representative,
  ...PLAYER_LOG_REDLINE_TERMS.map((term, index) => event(100 + index, 'effect-failed', `${term}失败`, [source])),
  event(200, 'effect', '发动公开效果', [source], 0, { effectText: '重新校验后失效' }),
], 0, [])
assert.deepEqual(playerLogContainsForbiddenTerms(safeRows), [], 'player projection must not contain engine terminology')
assert.equal(JSON.stringify(safeRows).includes('测试甲'), false, 'player nicknames must never enter projected rows')

console.log(`battle log view model: ${rows.length + cancelled.length + otherworldRune.length + effectWithCardCost.length + trial.length + stateChanges.length + standaloneCost.length + fieldEffect.length + combat.length + publicHandAdd.length + privateHandAdd.length + deduplicatedPublicHandAdd.length + safeRows.length} projected rows verified`)
