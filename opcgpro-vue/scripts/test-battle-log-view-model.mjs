import assert from 'node:assert/strict'
import './test-battle-troops-modifier.mjs'
import { PLAYER_LOG_REDLINE_TERMS, playerLogContainsForbiddenTerms, projectLog } from '../src/l12/game/logViewModel.ts'
import { replayGameAt } from '../src/l12/replayModel.ts'

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

const immortal = card('免死军团', 'immortal')
const immortalTriggered = projectLog([
  event(1, 'effect', '免死生效并将当前兵力设为1000', [immortal], 0, {
    playerLogSemantic: {
      sourceInstanceId: immortal.instanceId,
      sourceName: immortal.name,
      targetInstanceId: immortal.instanceId,
      targetName: immortal.name,
      actionLabel: '触发 免死',
      outcomeLabel: '兵力变为1000',
    },
  }),
], 0, [])
assert.equal(immortalTriggered.length, 1)
assert.deepEqual(immortalTriggered[0].parts.map(part => part.text), [
  '〈免死军团〉', '触发 免死', '，兵力变为1000',
])
assert.equal(immortalTriggered[0].badges.length, 0,
  'an authoritative set operation must not be presented as a positive troop delta')

const lethalReplacement = projectLog([
  event(1, 'replacement', '内部致命替代审计文本', [immortal], 0, {
    playerLogSemantic: {
      sourceInstanceId: immortal.instanceId,
      sourceName: immortal.name,
      targetInstanceId: immortal.instanceId,
      targetName: immortal.name,
      actionLabel: '触发 致命代替',
      outcomeLabel: '未阵亡且保持原状态',
    },
  }),
], 0, [])
assert.equal(lethalReplacement.length, 1, 'a semantic lethal replacement must remain visible although raw replacement events are hidden')
assert.deepEqual(lethalReplacement[0].parts.map(part => part.text), [
  '〈免死军团〉', '触发 致命代替', '，未阵亡且保持原状态',
])

const masterDamageReplacement = projectLog([
  event(1, 'effect', '内部伤害替换审计文本', [], 0, {
    playerLogSemantic: {
      sourceName: '平阳昭公主',
      targetName: '杨戬',
      actionLabel: '触发 主宰效果',
      outcomeLabel: '〈杨戬〉受到的本次伤害变为2',
    },
  }),
], 0, [])
assert.deepEqual(masterDamageReplacement[0].parts.map(part => part.text), [
  '〈平阳昭公主〉', '触发 主宰效果', '，〈杨戬〉受到的本次伤害变为2',
])
const hiddenSemanticSource = projectLog([
  event(1, 'effect', '隐藏来源不得公开', [card('未公开响应卡', 'secret-source', true)], 0, {
    playerLogSemantic: {
      sourceInstanceId: 'secret-source',
      sourceName: '未公开响应卡',
      actionLabel: '触发 效果',
      outcomeLabel: '状态改变',
    },
  }),
], 0, [])
assert.equal(hiddenSemanticSource.length, 0, 'semantic metadata must not reveal a source that is still hidden')

const structuredTrialEvents = [
  event(1, 'play', '权威打出事件', [card('加拉哈德', 'galahad')], 0, {
    playerLogGroupId: 'trial:galahad',
    playerLogTiming: 'enter',
  }),
  event(2, 'trial', '不包含中文进度格式的权威事件', [card('加拉哈德', 'galahad')], 0, {
    playerLogGroupId: 'trial:galahad',
    playerLogTiming: 'enter',
    playerLogSemantic: {
      sourceInstanceId: 'galahad',
      sourceName: '加拉哈德',
      actionLabel: '推进试炼',
      outcomeLabel: '试炼 0→2',
    },
  }),
]
const structuredTrialGroup = projectLog(structuredTrialEvents, 0, [])
assert.equal(structuredTrialGroup.length, 1)
assert.deepEqual(structuredTrialGroup[0].parts.map(part => part.text), [
  '打出', '〈加拉哈德〉', '并发动登场时效果，推进试炼，试炼 0→2',
], 'new grouped trial events must render only structured semantics instead of parsing audit text')
const opponentTrialGroup = projectLog(structuredTrialEvents, 1, [])
assert.equal(opponentTrialGroup[0].actor, '对方', 'trial ownership must follow the acting player for the opponent view')
assert.deepEqual(opponentTrialGroup[0].parts.map(part => part.text),
  structuredTrialGroup[0].parts.map(part => part.text), 'both players must receive the same public trial result words')

const hiddenTrialIdentity = projectLog([
  event(1, 'trial', '试炼进度 2 → 3', [], 0, {
    playerLogSemantic: { actionLabel: '推进试炼', outcomeLabel: '试炼 2→3' },
  }),
], 1, [])
assert.equal(hiddenTrialIdentity[0].actor, '对方')
assert.deepEqual(hiddenTrialIdentity[0].parts.map(part => part.text), ['推进试炼', '，试炼 2→3'],
  'an unrevealed trial remains readable without exposing its hidden identity')

const replayState = {
  MatchId: 'trial-replay', RoomCode: 'TRIAL', Players: [],
  Events: structuredTrialEvents.map(item => ({
    Sequence: item.sequence,
    Type: item.type,
    PlayerIndex: item.playerIndex,
    Text: item.text,
    PlayerLogGroupId: item.playerLogGroupId,
    PlayerLogTiming: item.playerLogTiming,
    PlayerLogSemantic: item.playerLogSemantic && {
      ActionLabel: item.playerLogSemantic.actionLabel,
      OutcomeLabel: item.playerLogSemantic.outcomeLabel,
      SourceInstanceId: item.playerLogSemantic.sourceInstanceId,
      SourceName: item.playerLogSemantic.sourceName,
    },
    Cards: item.cards,
  })),
}
const replay = replayGameAt({
  match: { matchId: 'trial-replay', roomCode: 'TRIAL' },
  commands: [{ state: replayState, revision: 1 }],
  viewerPlayerIndex: 0,
}, 0)
assert(replay)
const logWords = rows => rows.map(row => row.kind === 'line'
  ? { actor: row.actor, parts: row.parts.map(part => part.text), badges: row.badges.map(item => item.value) }
  : row)
assert.deepEqual(logWords(projectLog(replay.recentEvents ?? [], 0, [])), logWords(structuredTrialGroup),
  'live and replay trial semantics must project to exactly the same player log rows')

const completedTrial = projectLog([
  event(1, 'trial', '完成试炼《寻找圣杯之旅》', [card('寻找圣杯之旅', 'grail-trial')], 0),
], 0, [])
assert.deepEqual(completedTrial[0].parts.map(part => part.text), [
  '〈寻找圣杯之旅〉', '：完成试炼',
], 'a completed trial must not be mislabeled as trial advancement')

const legacySet = projectLog([
  event(1, 'effect', '旧回放：本次进攻兵力视为3000', [source], 0),
], 0, [])
assert.equal(legacySet.length, 1)
assert(legacySet[0].kind === 'line' && !legacySet[0].badges.some(item => item.value === '+3000兵力'),
  'a legacy set-value sentence must never be guessed as a troop increase')
const additiveTroops = projectLog([
  event(1, 'effect', '本回合兵力+1000', [source], 0),
], 0, [])
assert(additiveTroops[0].kind === 'line' && !additiveTroops[0].badges.some(item => item.value.includes('兵力')),
  'legacy signed text without an authoritative modifier fact must not manufacture a troop delta')

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
assert.deepEqual(galahadNegated[0].detail?.map(row => row.parts.map(part => part.text).join('')), [
  '效果被无效',
], 'a negated effect retains an expandable terminal fact without inventing a refund')
const stagedOutcome = [
  event(1, 'play', '打出来源卡', [source], 0, { playerLogGroupId: 'effect:staged', playerLogTiming: 'enter' }),
  event(2, 'effect-result', '第一段完成', [source], 0, {
    playerLogGroupId: 'effect:staged', effectResultStatus: 'resolved',
    effectSegmentIndex: 1, effectSegmentCount: 2,
    playerLogSemantic: { sourceInstanceId: source.instanceId, actionLabel: '效果结果', outcomeLabel: '已支付费用：消耗1士气' },
  }),
  event(3, 'effect-result', '第二段跳过', [source], 0, {
    playerLogGroupId: 'effect:staged', effectResultStatus: 'skipped',
    effectSegmentIndex: 2, effectSegmentCount: 2,
    playerLogSemantic: { sourceInstanceId: source.instanceId, actionLabel: '效果结果', outcomeLabel: '原因：没有合法目标；已支付费用：消耗1士气' },
  }),
]
const stagedRows = projectLog([stagedOutcome[2], stagedOutcome[0], stagedOutcome[1], stagedOutcome[2]], 0, [])
assert.equal(stagedRows.length, 1, 'reconnect replay must group and deduplicate each terminal fact')
assert(stagedRows[0].kind === 'line' && stagedRows[0].parts.some(part => part.text.includes('1段完成、1段跳过')),
  'the summary must represent partial completion instead of treating the final skipped segment as the whole effect')
assert.deepEqual(stagedRows[0].detail?.map(row => row.parts.map(part => part.text).join('')), [
  '第1/2段完成',
  '第2/2段跳过；原因：没有合法目标；已支付费用：消耗1士气',
], 'expanded details must retain authoritative segment order, reason and paid receipt')
const costOnlyOnFirstSegment = projectLog([
  stagedOutcome[0], stagedOutcome[1],
  event(3, 'effect-result', '第二段跳过', [source], 0, {
    playerLogGroupId: 'effect:staged', effectResultStatus: 'skipped',
    effectSegmentIndex: 2, effectSegmentCount: 2,
    playerLogSemantic: { sourceInstanceId: source.instanceId, actionLabel: '效果结果', outcomeLabel: '原因：没有合法目标' },
  }),
], 0, [])
assert.equal(costOnlyOnFirstSegment[0].detail?.flatMap(row => row.parts.map(part => part.text))
  .filter(text => text.includes('已支付费用：')).length, 1,
'a later segment without a copied receipt must not erase the earlier actual payment')
const oldGroupedReplay = projectLog([
  event(1, 'play', '打出来源卡', [source], 0, { playerLogGroupId: 'effect:old-replay', playerLogTiming: 'enter' }),
  event(2, 'effect-result', '旧结果无状态字段', [source], 0, { playerLogGroupId: 'effect:old-replay' }),
], 0, [])
assert.equal(oldGroupedReplay.length, 1)
assert.equal(oldGroupedReplay[0].detail, undefined,
  'older replays without a terminal status must remain readable without an invented outcome')
const ungroupedTerminal = projectLog([
  event(7, 'effect-result', '旧审计文本', [source], 0, {
    effectResultStatus: 'failed',
    playerLogSemantic: { sourceInstanceId: source.instanceId, actionLabel: '效果结果',
      outcomeLabel: '原因：目标不再符合条件；已支付费用：消耗1士气' },
  }),
], 0, [])
assert.deepEqual(ungroupedTerminal[0].parts.map(part => part.text), [
  '〈来源卡〉', '：', '效果未能完成', '；原因：目标不再符合条件', '；已支付费用：消耗1士气',
], 'a terminal event without group metadata must still show its status and authoritative receipt')
const hiddenResult = projectLog([
  event(1, 'effect-result', '不可见来源的结果', [{ ...source, hidden: true }], 1, {
    playerLogGroupId: 'effect:hidden-result', effectResultStatus: 'failed',
    playerLogSemantic: { sourceInstanceId: source.instanceId, sourceName: source.name,
      actionLabel: '效果结果', outcomeLabel: '原因：不可公开的内容' },
  }),
], 0, [])
assert.equal(hiddenResult.length, 0, 'a hidden source and its terminal receipt must remain absent for other viewers')
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
  event(1, 'attack', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'battle-one', eventKind: 'attack', outcomeCode: 'declared',
    attackerInstanceId: a.instanceId, targetInstanceId: b.instanceId,
    attackerTroops: 6000, defenderTroops: 4000,
  } }),
  event(2, 'combat', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'battle-one', eventKind: 'combat', outcomeCode: 'defeated',
  } }),
], 0, [])
assert.equal(combat.length, 1, 'events with the same combat id must produce one summary')
assert.equal(combat[0].kind, 'combat')
assert.equal(combat[0].result, '击破')
assert.equal(combat[0].detail.length, 1, 'combat detail must retain the authoritative defeat')

const masterNotDefended = projectLog([
  event(1, 'attack', '审计文本', [a], 0, { playerCombat: {
    combatId: 'master-open', eventKind: 'attack', outcomeCode: 'declared', attackerTroops: 2000,
  } }),
  event(2, 'defense', '审计文本', [], 1, { playerCombat: {
    combatId: 'master-open', eventKind: 'defense', outcomeCode: 'unblocked', masterDamage: 1,
  } }),
], 0, [])
assert.equal(masterNotDefended[0].kind, 'combat')
assert.equal(masterNotDefended[0].result, '造成伤害')
assert(masterNotDefended[0].kind === 'combat'
  && masterNotDefended[0].detail.some(row => row.parts.some(part => part.text.includes('未抵挡'))),
  'an unblocked master attack must say the opponent did not block')
assert.equal(JSON.stringify(masterNotDefended).includes('完成抵挡'), false)

const blockOne = card('抵挡军团甲', 'block-a')
const blockTwo = card('抵挡军团乙', 'block-b')
const masterDefended = projectLog([
  event(1, 'attack', '审计文本', [a], 0, { playerCombat: {
    combatId: 'master-blocked', eventKind: 'attack', outcomeCode: 'declared', attackerTroops: 6000,
  } }),
  event(2, 'defense', '审计文本', [blockOne, blockTwo], 1, { playerCombat: {
    combatId: 'master-blocked', eventKind: 'defense', outcomeCode: 'blocked',
  } }),
], 0, [])
assert.equal(masterDefended[0].kind, 'combat')
assert.equal(masterDefended[0].result, '完成抵挡')
assert(masterDefended[0].kind === 'combat'
  && [blockOne, blockTwo].every(card => masterDefended[0].detail[0].parts.some(part => part.card?.instanceId === card.instanceId)),
  'all public blocking cards must remain visible in the compact defense detail')

const puppet = card('戏法师的傀儡', 'retargeted-puppet')
for (const outcomeCode of ['defeated', 'not-defeated']) {
  const retargeted = projectLog([
    event(10, 'attack', '进攻最初指向主宰', [a], 0, { playerCombat: {
      combatId: `puppet-${outcomeCode}`, eventKind: 'attack', outcomeCode: 'declared',
      attackerInstanceId: a.instanceId, attackerTroops: 2000,
    } }),
    event(12, 'combat', '后台审计文本称主宰', [a, puppet], 0, { playerCombat: {
      combatId: `puppet-${outcomeCode}`, eventKind: 'combat', outcomeCode,
      attackerInstanceId: a.instanceId, targetInstanceId: puppet.instanceId,
      attackerTroops: 2000, defenderTroops: 1000,
    } }),
  ], 0, [])
  assert.equal(retargeted.length, 1)
  assert.equal(retargeted[0].kind, 'combat')
  assert.equal(retargeted[0].defender?.instanceId, puppet.instanceId,
    'final summary must use the authority target rather than the declared master')
  assert.equal(retargeted[0].defendTroops, 1000)
  assert(retargeted[0].detail.some(row => row.parts.some(part => part.card?.instanceId === puppet.instanceId)),
    'final detail and summary must identify the same actual target')
  assert.equal(JSON.stringify(retargeted).includes('后台审计文本'), false)
}
const sameNameOriginal = card('同名军团', 'original-target')
const sameNameFinal = card('同名军团', 'final-target')
const sameNameRetarget = projectLog([
  event(20, 'attack', '原目标', [a, sameNameOriginal], 0, { playerCombat: {
    combatId: 'same-name-retarget', eventKind: 'attack', outcomeCode: 'declared',
    targetInstanceId: sameNameOriginal.instanceId,
  } }),
  event(21, 'combat', '新目标', [a, sameNameFinal], 0, { playerCombat: {
    combatId: 'same-name-retarget', eventKind: 'combat', outcomeCode: 'defeated',
    targetInstanceId: sameNameFinal.instanceId,
  } }),
], 0, [])
assert.equal(sameNameRetarget[0].kind, 'combat')
assert.equal(sameNameRetarget[0].defender?.instanceId, sameNameFinal.instanceId,
  'same-name targets must resolve by the final authority instance id')
const missingFinalTarget = projectLog([
  event(30, 'attack', '原目标是主宰', [a], 0, { playerCombat: {
    combatId: 'missing-final-target', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(31, 'combat', '旧记录缺少最终目标', [a], 0, { playerCombat: {
    combatId: 'missing-final-target', eventKind: 'combat', outcomeCode: 'defeated',
  } }),
], 0, [])
assert.equal(missingFinalTarget[0].kind, 'combat')
assert.equal(missingFinalTarget[0].defender, '目标',
  'a historical result without its final target must not fall back to the original master')
assert(missingFinalTarget[0].detail.some(row => row.parts.some(part => part.text === '目标被击破')))

const thunderLowRoll = projectLog([
  event(40, 'dice', '〈雷霆天怒〉：甲军团进攻时掷骰结果为 1', [a], 0),
  event(41, 'attack-ended', '后台原因包含私有信息', [a, b], 0, { playerCombat: {
    combatId: 'thunder-low-roll', eventKind: 'attack-aborted', outcomeCode: 'aborted',
    publicReasonCode: 'thunder-roll-failed', attackerInstanceId: a.instanceId,
    targetInstanceId: b.instanceId, attackerTroops: 3000, defenderTroops: 3000,
  } }),
], 0, [])
assert.equal(thunderLowRoll.length, 2, 'the dice and one stopped attack must remain separate records')
assert(thunderLowRoll[0].kind === 'line' && thunderLowRoll[0].icon === 'dice')
assert(thunderLowRoll[1].kind === 'line'
  && thunderLowRoll[1].parts.some(part => part.text.includes('进攻中止'))
  && thunderLowRoll[1].parts.some(part => part.text.includes('雷霆天怒掷骰未满足进攻条件')))
assert.equal(JSON.stringify(thunderLowRoll).includes('后台原因'), false)

const invalidDefense = projectLog([
  event(1, 'attack', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'invalid-block', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(2, 'defense-invalid', '后台原因不可公开', [], 1, { playerCombat: {
    combatId: 'invalid-block', eventKind: 'defense-invalid', outcomeCode: 'invalid-block',
    publicReasonCode: 'choice-unavailable',
  } }),
], 0, [])
assert.equal(invalidDefense[0].kind, 'combat')
assert.equal(invalidDefense[0].result, '抵挡无效；战斗结果未记录')
assert(invalidDefense[0].kind === 'combat'
  && invalidDefense[0].detail.some(row => row.parts.some(part => part.text.includes('抵挡无效'))),
  'invalid defense must retain its separate result')
assert.equal(JSON.stringify(invalidDefense).includes('后台原因'), false)

const supporter = card('支援军团', 'supporter')
const supported = projectLog([
  event(1, 'attack', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'supported', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(2, 'leave', '支援军团作为支援军团阵亡', [supporter], 1),
  event(3, 'support', '审计文本', [supporter, b, a], 1, { playerCombat: {
    combatId: 'supported', eventKind: 'support', outcomeCode: 'supported',
    attackerInstanceId: a.instanceId, targetInstanceId: b.instanceId,
  } }),
], 0, [])
assert.equal(supported[0].kind, 'combat')
assert.equal(supported[0].result, '完成支援', 'support must not be called a block')
assert(supported[0].kind === 'combat'
  && supported[0].detail.at(-1)?.parts.filter(part => part.card).every(part => part.card?.instanceId === supporter.instanceId),
  'support detail must list supporters without mislabeling the attacker or target as support cards')

const attackerDeparture = projectLog([
  event(1, 'attack', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'aborted', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(2, 'leave', '甲军团离场', [a], 0),
  event(3, 'attack-aborted', '后台边界说明', [], 0, { playerCombat: {
    combatId: 'aborted', eventKind: 'attack-aborted', outcomeCode: 'aborted',
    publicReasonCode: 'attacker-left',
  } }),
  event(4, 'grave', '乙军团因后续效果进入墓地', [b], 1),
], 0, [])
assert.equal(attackerDeparture.length, 3, 'unlinked departures remain outside the combat summary')
assert.equal(attackerDeparture[0].kind, 'combat')
assert.equal(attackerDeparture[0].result, '进攻中止')
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

const sameNameTargetA = card('同名军团', 'same-a')
const sameNameTargetB = card('同名军团', 'same-b')
const mixedCombats = [
  event(700, 'attack', '私密审计文本', [a, sameNameTargetA], 0, { playerCombat: {
    combatId: 'battle-a', eventKind: 'attack', outcomeCode: 'declared', attackerTroops: 0,
  } }),
  event(701, 'attack', '私密审计文本', [a, sameNameTargetB], 0, { playerCombat: {
    combatId: 'battle-b', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(702, 'combat', '不属于 battle-a', [a, sameNameTargetB], 0, { playerCombat: {
    combatId: 'battle-b', eventKind: 'combat', outcomeCode: 'defeated',
    targetInstanceId: sameNameTargetB.instanceId,
  } }),
  event(703, 'attack-aborted', '后台 reason 含私有手牌', [], 0, { playerCombat: {
    combatId: 'battle-a', eventKind: 'attack-aborted', outcomeCode: 'aborted',
    publicReasonCode: 'target-left',
  } }),
]
const mixedRows = projectLog([mixedCombats[3], mixedCombats[1], mixedCombats[2], mixedCombats[0], mixedCombats[2]], 1, [])
assert.deepEqual(mixedRows.map(row => row.kind === 'combat' ? [row.defender === '主宰' ? '' : row.defender.instanceId, row.result] : []),
  [['same-a', '进攻中止'], ['same-b', '击破']],
  'out-of-order, duplicate and same-name combat events must group by combat id')
assert.equal(mixedRows[0].attackTroops, 0, 'authoritative zero is a real value')
assert.equal(mixedRows[1].attackTroops, undefined, 'missing historical value must stay missing')
assert.equal(JSON.stringify(mixedRows).includes('私有手牌'), false, 'backend audit text must not enter combat rows')
assert(mixedRows[0].detail.some(row => row.parts.some(part => part.text.includes('被进攻军团已离场'))))

const invalidSupport = projectLog([
  event(710, 'attack', '审计文本', [a, b], 0, { playerCombat: {
    combatId: 'support-invalid', eventKind: 'attack', outcomeCode: 'declared',
  } }),
  event(711, 'defense-invalid', '错误：私有候选', [], 1, { playerCombat: {
    combatId: 'support-invalid', eventKind: 'defense-invalid', outcomeCode: 'invalid-support',
    publicReasonCode: 'extra-cost-unpaid',
  } }),
], 0, [])
assert.equal(invalidSupport[0].result, '支援无效；战斗结果未记录')
assert.equal(JSON.stringify(invalidSupport).includes('私有候选'), false)

const legacyBattle = projectLog([event(720, 'attack', '旧记录 9999 vs 8888', [a, b], 0)], 0, [])[0]
assert.equal(legacyBattle.result, '战斗结果未记录')
assert.equal(legacyBattle.attackTroops, undefined)
assert.equal(legacyBattle.defendTroops, undefined)
const unrelatedDamage = projectLog([
  event(721, 'attack', '旧记录', [a, b], 0),
  event(722, 'damage', '对手的主宰受到1点非战斗伤害', [], 1),
], 0, [])
assert.equal(unrelatedDamage[0].result, '战斗结果未记录')
assert.equal(unrelatedDamage[0].damage, undefined, 'non-combat damage must not attach to a battle')

const combatReplay = replayGameAt({
  match: { matchId: 'combat-replay', roomCode: 'COMBAT' },
  commands: [{ state: { Events: mixedCombats.map(item => ({
    Sequence: item.sequence, Type: item.type, PlayerIndex: item.playerIndex,
    Text: item.text, Cards: item.cards,
    PlayerCombat: item.playerCombat && {
      CombatId: item.playerCombat.combatId, EventKind: item.playerCombat.eventKind,
      OutcomeCode: item.playerCombat.outcomeCode, PublicReasonCode: item.playerCombat.publicReasonCode,
      AttackerTroops: item.playerCombat.attackerTroops,
      TargetInstanceId: item.playerCombat.targetInstanceId,
    },
  })) }, revision: 1 }],
  viewerPlayerIndex: 1,
}, 0)
assert(combatReplay)
assert.deepEqual(projectLog(combatReplay.recentEvents ?? [], 1, []).map(row => row.kind === 'combat' ? row.result : ''),
  ['进攻中止', '击破'], 'replay must preserve combat id and authoritative outcome')
const truncatedCombatWindow = projectLog([
  event(800, 'attack-aborted', '内部安全边界', [], 0, { playerCombat: {
    combatId: 'older-attack', eventKind: 'attack-aborted', outcomeCode: 'aborted',
    publicReasonCode: 'attacker-left',
  } }),
], 0, [])
assert.equal(truncatedCombatWindow.length, 1, 'a bounded recent-event window must retain its orphaned outcome')
assert.deepEqual(truncatedCombatWindow[0].parts.map(part => part.text), ['本次战斗：进攻中止'])

// Stage 4B-1 red matrix: legacy events do not carry a stable combat id or a
// structured outcome. Nearby events may be caused by another effect, so they
// cannot establish the result of this attack.
const legacyCombatChecks = [
  ['unlinked departure must not assert a kill', () => {
    const projected = projectLog([
      event(300, 'attack', '甲军团进攻乙军团', [a, b]),
      event(301, 'leave', '乙军团因其他效果离场', [b], 1),
    ], 0, [])
    return projected[0]?.kind === 'combat' && projected[0].result !== '击破'
  }],
  ['unlinked defense must not assert a block', () => {
    const projected = projectLog([
      event(310, 'attack', '甲军团进攻主宰', [a]),
      event(311, 'defense', '弃置军团抵挡另一次进攻', [blockOne], 1),
    ], 0, [])
    return projected[0]?.kind === 'combat' && projected[0].result !== '被抵挡'
  }],
  ['unlinked support must not be labeled a block', () => {
    const projected = projectLog([
      event(320, 'attack', '甲军团进攻乙军团', [a, b]),
      event(321, 'support', '支援本次进攻', [supporter, b, a], 1),
    ], 0, [])
    return projected[0]?.kind === 'combat' && projected[0].result !== '被抵挡'
  }],
  ['attack abort must not be labeled a completed combat', () => {
    const projected = projectLog([
      event(330, 'attack', '甲军团进攻乙军团', [a, b]),
      event(331, 'attack-aborted', '进攻军团已离场，返回主要阶段', [], 0),
    ], 0, [])
    return projected[0]?.kind === 'combat' && projected[0].result !== '未击破'
  }],
]
const legacyCombatFailures = legacyCombatChecks.filter(([, check]) => !check()).map(([name]) => name)
assert.deepEqual(legacyCombatFailures, [], 'Stage 4B-1 legacy combat facts must not be inferred from adjacency')

const movementFact = (instanceId, battlefieldPlayerIndex, fromRow, fromSlot, toRow, toSlot) => ({
  instanceId, battlefieldPlayerIndex, fromRow, fromSlot, toRow, toSlot,
})
const movementEvent = (facts, cards = [a], type = 'move', actor = 0) =>
  event(900, type, '不应从此文本推断位置', cards, actor, { playerBattlefieldMovement: { facts } })
const detailText = row => row?.detail?.flatMap(detail => detail.parts.map(part => part.text)).join('') ?? ''
for (const viewer of [0, 1]) {
  for (const owner of [0, 1]) {
    for (const fromRow of [0, 1]) {
      for (const fromSlot of [0, 1, 2]) {
        const toRow = 1 - fromRow
        const projected = projectLog([movementEvent([
          movementFact('a', owner, fromRow, fromSlot, toRow, fromSlot),
        ], [source, a], 'faction-effect', 1 - owner)], viewer, [])
        const label = owner === viewer ? '我方' : '对方'
        const columns = ['左格', '中格', '右格']
        const expected = `〈甲军团〉从${label}${fromRow === 0 ? '前排' : '后排'}${columns[fromSlot]}移动至${label}${toRow === 0 ? '前排' : '后排'}${columns[fromSlot]}`
        assert.equal(detailText(projected[0]), expected, 'movement location must follow fact owner, not event actor or current board')
      }
    }
  }
}
const neutralMovement = projectLog([movementEvent([movementFact('a', 0, 0, 0, 1, 2)])], 0, [], true)[0]
assert.equal(detailText(neutralMovement), '〈甲军团〉从下方前排左格移动至下方后排右格')
assert.equal(neutralMovement.actor, '下方')
const swapped = projectLog([movementEvent([
  movementFact('a', 0, 0, 0, 1, 1), movementFact('b', 0, 1, 1, 0, 0),
], [a, b])], 0, [])[0]
assert.equal(swapped.detail.length, 2, 'one swap event retains two ordered movement facts')
assert.equal(detailText(swapped), '〈甲军团〉从我方前排左格移动至我方后排中格〈乙军团〉从我方后排中格移动至我方前排左格')
const sameName = card('甲军团', 'same-name')
const sameNameMove = projectLog([movementEvent([
  movementFact('same-name', 0, 0, 1, 1, 1),
], [a, sameName])], 0, [])[0]
assert.equal(sameNameMove.detail[0].parts[0].card.instanceId, 'same-name', 'same-name cards bind by instance id')
for (const invalid of [
  movementEvent([movementFact('missing', 0, 0, 0, 1, 0)]),
  movementEvent([movementFact('a', 2, 0, 0, 1, 0)]),
  movementEvent([movementFact('a', 0, 0, 3, 1, 0)]),
  movementEvent([movementFact('a', 0, 0, 0, 0, 0)]),
  movementEvent([movementFact('a', 0, 0, 0, 1, 0)], [card('甲军团', 'a', true)]),
  movementEvent([movementFact('a', 0, 0, 0, 1, 0)], [a], 'put'),
  event(900, 'move', '旧记录前排左格到后排右格', [a]),
]) {
  assert.equal(detailText(projectLog([invalid], 0, [])[0]), '', 'unsafe or legacy movement must not invent a location')
}
assert.equal(detailText(projectLog([event(901, 'move', '进入墓地', [a])], 0, [])[0]), '',
  'hand-to-grave and other non-battlefield move events must stay untagged')
const movementReplay = replayGameAt({
  match: { matchId: 'movement-replay', roomCode: 'MOVE' },
  commands: [{ state: { Events: [{ Sequence: 1, Type: 'move', PlayerIndex: 0, Text: '移动', Cards: [a],
    PlayerBattlefieldMovement: { Facts: [{ InstanceId: 'a', BattlefieldPlayerIndex: 0,
      FromRow: 0, FromSlot: 0, ToRow: 1, ToSlot: 2 }] },
  }] }, revision: 1 }], viewerPlayerIndex: 0,
}, 0)
assert.equal(detailText(projectLog(movementReplay?.recentEvents ?? [], 0, [])[0]),
  '〈甲军团〉从我方前排左格移动至我方后排右格', 'replay restores structured movement facts')

console.log(`battle log view model: ${rows.length + cancelled.length + otherworldRune.length + effectWithCardCost.length + trial.length + stateChanges.length + standaloneCost.length + fieldEffect.length + combat.length + masterNotDefended.length + masterDefended.length + invalidDefense.length + supported.length + attackerDeparture.length + publicHandAdd.length + privateHandAdd.length + deduplicatedPublicHandAdd.length + safeRows.length} projected rows verified`)
