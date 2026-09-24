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

const automaticTurn = projectLog([
  event(1, 'turn-start', '第 2 回合', [], 0, { playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
  event(2, 'draw', '回合开始时抽取1张牌', [], 0, { playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
  event(3, 'morale', '回合开始时追加2张士气', [], 0, { playerLogGroupId: 'turn:2', playerLogTiming: 'turn-start' }),
], 0, [])
assert.equal(automaticTurn.length, 2, 'turn divider and automatic public changes must remain visible without phase noise')
assert.equal(automaticTurn[1].kind, 'line')
assert.notEqual(automaticTurn[0].sequence, automaticTurn[1].sequence,
  'turn divider and automatic changes must keep unique render keys')
assert.deepEqual(automaticTurn[1].parts.map(part => part.text), ['回合开始，抽取1张牌，追加2张士气'])

const peace = { ...card('议和谈判', 'peace'), cardType: 'tactic' }
const peaceAgreed = projectLog([
  event(1, 'play', '我方打出议和谈判', [peace], 0, { playerLogGroupId: 'play:peace', playerLogTiming: 'play' }),
  event(2, 'draw', '议和谈判使我方抽取1张牌', [peace], 0, { playerLogGroupId: 'play:peace', playerLogTiming: 'play' }),
  event(3, 'effect-decision', '对方同意议和', [peace], 1, {
    playerLogGroupId: 'play:peace', playerLogTiming: 'play', playerLogDecisionLabel: '同意议和',
  }),
  event(4, 'draw', '议和谈判使我方额外抽取1张牌', [peace], 0, { playerLogGroupId: 'play:peace', playerLogTiming: 'play' }),
  event(5, 'draw', '议和谈判使对方抽取1张牌', [peace], 1, { playerLogGroupId: 'play:peace', playerLogTiming: 'play' }),
  event(6, 'effect-result', '议和谈判效果结算完成', [peace], 0, {
    playerLogGroupId: 'play:peace', playerLogTiming: 'play', effectResultStatus: 'resolved',
  }),
], 0, [])
assert.equal(peaceAgreed.length, 1, 'a played tactic and its public choice/results must collapse into one readable action')
assert.equal(peaceAgreed[0].kind, 'line')
assert.deepEqual(peaceAgreed[0].parts.map(part => part.text), [
  '打出', '〈议和谈判〉', '，对方同意议和，我方抽取2张牌，对方抽取1张牌',
])
const reconnectedPeace = projectLog([
  event(6, 'effect-result', '议和谈判效果结算完成', [peace], 0, {
    playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play', effectResultStatus: 'resolved',
  }),
  event(1, 'play', '我方打出议和谈判', [peace], 0, { playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play' }),
  event(2, 'draw', '议和谈判使我方抽取1张牌', [peace], 0, { playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play' }),
  event(3, 'effect-decision', '对方同意议和', [peace], 1, {
    playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play', playerLogDecisionLabel: '同意议和',
  }),
  event(4, 'draw', '议和谈判使我方额外抽取1张牌', [peace], 0, { playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play' }),
  event(5, 'draw', '议和谈判使对方抽取1张牌', [peace], 1, { playerLogGroupId: 'play:peace-reconnect', playerLogTiming: 'play' }),
], 0, [])
assert.equal(reconnectedPeace.length, 1, 'out-of-order replay events must regroup deterministically')
const duplicatedReconnect = projectLog([
  event(1, 'play', '我方打出议和谈判', [peace], 0),
  event(1, 'play', '我方打出议和谈判', [peace], 0, { playerLogGroupId: 'play:dedupe', playerLogTiming: 'play' }),
  event(2, 'effect-decision', '对方不同意议和', [peace], 1, {
    playerLogGroupId: 'play:dedupe', playerLogTiming: 'play', playerLogDecisionLabel: '不同意议和',
  }),
], 0, [])
assert.equal(duplicatedReconnect.length, 1, 'reconnect delivery must deduplicate a repeated event sequence')
assert(duplicatedReconnect[0].kind === 'line'
  && duplicatedReconnect[0].parts.some(part => part.text.includes('不同意议和')),
'reconnect deduplication must prefer the enriched copy of an event sequence')
const hiddenGroupedSource = projectLog([
  event(1, 'effect-announced', '对方声明一张尚未公开的响应卡', [{ ...peace, hidden: true }], 1, {
    playerLogGroupId: 'effect:hidden-response', playerLogTiming: 'response',
  }),
], 0, [])
assert.equal(hiddenGroupedSource.length, 0,
  'group metadata must not make a deliberately hidden response source visible in the player log')

const peaceRefused = projectLog([
  event(1, 'play', '我方打出议和谈判', [peace], 0, { playerLogGroupId: 'play:peace-refused', playerLogTiming: 'play' }),
  event(2, 'draw', '议和谈判使我方抽取1张牌', [peace], 0, { playerLogGroupId: 'play:peace-refused', playerLogTiming: 'play' }),
  event(3, 'effect-decision', '对方不同意议和', [peace], 1, {
    playerLogGroupId: 'play:peace-refused', playerLogTiming: 'play', playerLogDecisionLabel: '不同意议和',
  }),
  event(4, 'effect-result', '议和谈判效果结算完成', [peace], 0, {
    playerLogGroupId: 'play:peace-refused', playerLogTiming: 'play', effectResultStatus: 'resolved',
  }),
], 0, [])
assert.equal(peaceRefused.length, 1)
assert.deepEqual(peaceRefused[0].parts.map(part => part.text), [
  '打出', '〈议和谈判〉', '，对方不同意议和，我方抽取1张牌',
])

const galahad = card('加拉哈德', 'galahad')
const galahadResolved = projectLog([
  event(1, 'play', '我方打出加拉哈德', [galahad], 0, { playerLogGroupId: 'play:galahad-ok', playerLogTiming: 'enter' }),
  event(2, 'cost', '加拉哈德入栈前休整以发动试炼', [galahad], 0, { playerLogGroupId: 'play:galahad-ok', playerLogTiming: 'enter' }),
  event(3, 'trial', '试炼进度 0 → 2', [galahad], 0, { playerLogGroupId: 'play:galahad-ok', playerLogTiming: 'enter' }),
  event(4, 'effect-result', '加拉哈德的效果结算完成', [galahad], 0, {
    playerLogGroupId: 'play:galahad-ok', playerLogTiming: 'enter', effectResultStatus: 'resolved',
  }),
], 0, [])
assert.equal(galahadResolved.length, 1)
assert.deepEqual(galahadResolved[0].parts.map(part => part.text), [
  '打出', '〈加拉哈德〉', '并发动登场时效果，推进试炼 0→2',
])

const galahadNegated = projectLog([
  event(1, 'play', '我方打出加拉哈德', [galahad], 0, { playerLogGroupId: 'play:galahad', playerLogTiming: 'enter' }),
  event(2, 'cost', '加拉哈德入栈前休整以发动试炼', [galahad], 0, { playerLogGroupId: 'play:galahad', playerLogTiming: 'enter' }),
  event(3, 'effect-result', '加拉哈德的效果被无效', [galahad], 0, {
    playerLogGroupId: 'play:galahad', playerLogTiming: 'enter', effectResultStatus: 'negated',
  }),
], 0, [])
assert.equal(galahadNegated.length, 1)
assert.deepEqual(galahadNegated[0].parts.map(part => part.text), [
  '打出', '〈加拉哈德〉', '，休整该军团并发动登场时效果；登场时效果被无效',
])
const unrelated = card('其他军团', 'unrelated')
const unrelatedRest = projectLog([
  event(1, 'play', '我方打出加拉哈德', [galahad], 0, { playerLogGroupId: 'play:unrelated-rest', playerLogTiming: 'enter' }),
  event(2, 'cost', '休整其他军团支付费用', [unrelated], 0, { playerLogGroupId: 'play:unrelated-rest', playerLogTiming: 'enter' }),
  event(3, 'effect-result', '加拉哈德的效果被无效', [galahad], 0, {
    playerLogGroupId: 'play:unrelated-rest', playerLogTiming: 'enter', effectResultStatus: 'negated',
  }),
], 0, [])
assert(unrelatedRest[0].kind === 'line'
  && !unrelatedRest[0].parts.some(part => part.text.includes('休整该军团')),
'an unrelated rested card must not be described as resting the source legion')

const allOut = { ...card('全军出击', 'all-out'), cardType: 'tactic' }
const activeTacticNegated = projectLog([
  event(1, 'play', '我方打出全军出击', [allOut], 0, { playerLogGroupId: 'play:all-out', playerLogTiming: 'play' }),
  event(2, 'effect-result', '全军出击的效果被无效', [allOut], 0, {
    playerLogGroupId: 'play:all-out', playerLogTiming: 'play', effectResultStatus: 'negated',
  }),
], 0, [])
assert.equal(activeTacticNegated.length, 1)
assert.deepEqual(activeTacticNegated[0].parts.map(part => part.text), [
  '打出', '〈全军出击〉', '；该战术的效果被无效',
])

const sharedDisasterChange = projectLog([
  event(1, 'play', '我方打出来源卡', [source], 0, { playerLogGroupId: 'play:disaster', playerLogTiming: 'play' }),
  event(2, 'disaster-value', '来源卡调整天灾值；天灾值 4 → 6', [source], undefined, {
    playerLogGroupId: 'play:disaster', playerLogTiming: 'play',
  }),
], 0, [])
assert.equal(sharedDisasterChange.length, 1)
assert.deepEqual(sharedDisasterChange[0].parts.map(part => part.text), [
  '打出', '〈来源卡〉', '，天灾值 4→6',
])

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
const publicDisasterValue = projectLog([
  event(1, 'disaster-value', '天灾值 3 → 4', [source], 0),
], 1, [])[0]
assert(publicDisasterValue.kind === 'line' && publicDisasterValue.actor === null,
  'the shared disaster value must never be attributed to either player')

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
  event(2, 'leave', '乙军团离场', [b], 1),
], 0, [])
assert.equal(combat.length, 1, 'a contiguous attack chain must collapse into one combat summary')
assert.equal(combat[0].kind, 'combat')
assert.equal(combat[0].result, '击破')
assert.equal(combat[0].detail.length, 1, 'combat detail must retain the defeated target departure')

const masterNotDefended = projectLog([
  event(1, 'attack', '〈甲军团〉2000 vs 主宰', [a], 0),
  event(2, 'damage', '对方主宰受到1点伤害', [], 1),
  event(3, 'defense', '对方的主宰受到1点伤害', [], 1),
], 0, [])
assert.equal(masterNotDefended[0].kind, 'combat')
assert.equal(masterNotDefended[0].result, '造成伤害')
assert(masterNotDefended[0].kind === 'combat'
  && masterNotDefended[0].detail.some(row => row.parts.some(part => part.text === '未抵挡')),
  'an unblocked master attack must say the opponent did not block')
assert.equal(JSON.stringify(masterNotDefended).includes('完成抵挡'), false)

const blockOne = card('抵挡军团甲', 'block-a')
const blockTwo = card('抵挡军团乙', 'block-b')
const masterDefended = projectLog([
  event(1, 'attack', '〈甲军团〉6000 vs 主宰', [a], 0),
  event(2, 'defense', '对方弃置2张军团抵挡', [blockOne, blockTwo], 1),
], 0, [])
assert.equal(masterDefended[0].kind, 'combat')
assert.equal(masterDefended[0].result, '被抵挡')
assert(masterDefended[0].kind === 'combat'
  && [blockOne, blockTwo].every(card => masterDefended[0].detail[0].parts.some(part => part.card?.instanceId === card.instanceId)),
  'all public blocking cards must remain visible in the compact defense detail')

const invalidDefense = projectLog([
  event(1, 'attack', '〈甲军团〉6000 vs 〈乙军团〉4000', [a, b], 0),
  event(2, 'defense-invalid', '防御结算前重新校验失败，本次抵挡/支援无效', [], 1),
  event(3, 'defense', '未支付额外弃牌费用，本次抵挡/支援无效', [], 1),
], 0, [])
assert.equal(invalidDefense[0].kind, 'combat')
assert.notEqual(invalidDefense[0].result, '被抵挡')
assert(invalidDefense[0].kind === 'combat'
  && invalidDefense[0].detail.filter(row => row.parts.some(part => part.text === '抵挡/支援无效')).length === 1,
  'invalid defense events must collapse to one compact invalid result')

const supporter = card('支援军团', 'supporter')
const supported = projectLog([
  event(1, 'attack', '〈甲军团〉6000 vs 〈乙军团〉4000', [a, b], 0),
  event(2, 'leave', '支援军团作为支援军团阵亡', [supporter], 1),
  event(3, 'support', '支援军团联合支援乙军团，支援者阵亡', [supporter, b, a], 1),
], 0, [])
assert.equal(supported[0].kind, 'combat')
assert.equal(supported[0].result, '被抵挡', 'a supporter leaving must not be mistaken for the target being defeated')
assert(supported[0].kind === 'combat'
  && supported[0].detail.at(-1)?.parts.filter(part => part.card).every(part => part.card?.instanceId === supporter.instanceId),
  'support detail must list supporters without mislabeling the attacker or target as support cards')

const attackerDeparture = projectLog([
  event(1, 'attack', '〈甲军团〉2000 vs 〈乙军团〉4000', [a, b], 0),
  event(2, 'leave', '甲军团离场', [a], 0),
  event(3, 'attack-ended', '本次进攻结束', [], 0),
  event(4, 'grave', '乙军团因后续效果进入墓地', [b], 1),
], 0, [])
assert.equal(attackerDeparture.length, 2, 'events after attack-ended must remain outside the previous combat summary')
assert.equal(attackerDeparture[0].kind, 'combat')
assert.notEqual(attackerDeparture[0].result, '击破', 'attacker departure must not count as defeating the defender')
assert.equal(attackerDeparture[1].kind, 'line')

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

console.log(`battle log view model: ${rows.length + cancelled.length + otherworldRune.length + effectWithCardCost.length + trial.length + stateChanges.length + standaloneCost.length + fieldEffect.length + combat.length + masterNotDefended.length + masterDefended.length + invalidDefense.length + supported.length + attackerDeparture.length + publicHandAdd.length + privateHandAdd.length + deduplicatedPublicHandAdd.length + safeRows.length} projected rows verified`)
