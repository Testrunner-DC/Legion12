import type { ActionEvent, Card } from '../types'
import { battlefieldSlotLabel } from './battlefieldTargetPresentation.ts'

export type IconKind = 'attack' | 'defense' | 'support' | 'effect' | 'disaster'
  | 'dice' | 'hand-add' | 'draw' | 'play' | 'info' | 'game-over'
export type LogPart = { text: string; card?: Card }
export type LogBadge = { value: string; tone: 'pos' | 'neg' | 'info' }
export type LogSide = '我方' | '对方' | '下方' | '上方'
export type LogLineRow = {
  kind: 'line'
  sequence: number
  icon: IconKind
  actor: LogSide | null
  parts: LogPart[]
  badges: LogBadge[]
  effectText?: string
  detail?: LogLineRow[]
}
export type LogTurnRow = { kind: 'turn'; sequence: number; round: number; side: LogSide }
export type LogCombatRow = {
  kind: 'combat'
  sequence: number
  attacker: Card
  defender: Card | '主宰' | '目标'
  attackTroops?: number
  defendTroops?: number
  result: string
  damage?: number
  detail: LogLineRow[]
}
export type LogRow = LogTurnRow | LogLineRow | LogCombatRow

export const PLAYER_LOG_REDLINE_TERMS = [
  '入栈', '堆叠', '校验', '声明', '结算步骤', '事务', '派生', '重新校验', '空堆叠', '失效',
] as const

export const PLAYER_LOG_VISIBLE_TYPES = new Set([
  'play', 'attack', 'defense', 'support', 'counter-set', 'move', 'effect', 'faction-effect',
  'promotion', 'put', 'search', 'reveal', 'game-over', 'special-victory', 'damage', 'heal',
  'draw', 'discard', 'grave', 'return', 'leave', 'morale', 'runes', 'dice', 'turn-start',
  'initiative-choice', 'mulligan', 'disaster', 'disaster-active', 'disaster-value',
  'trial', 'trial-action', 'enter', 'attach', 'counter-displaced', 'counter-replaced',
  'mill', 'library', 'reorder', 'continuous', 'extra-turn', 'cost', 'troops-modifier',
])

export const PLAYER_LOG_HIDDEN_TYPES = new Set([
  'effect-cancelled', 'effect-failed', 'effect-noop', 'effect-declined', 'effect-negated',
  'effect-prevented', 'heal-prevented', 'ability-rejected', 'ability-cancelled', 'defense-invalid',
  'effect-skipped', 'effect-skip', 'effect-rejected', 'effect-abandoned', 'attack-aborted',
  'phase', 'phase-detail', 'draw-skipped', 'prompt', 'priority-pass', 'stack-push', 'stack-deferred',
  'stack-open', 'stack-resolve', 'end-turn', 'disaster-removed', 'match-created', 'initiative',
  'disaster-selected', 'disaster-deck-ready', 'mulligan-start', 'shuffle', 'combat-stage',
  'combat-resume', 'attack-ended', 'effect-order', 'authority-event', 'promotion-stack-leave',
  'replacement-transaction', 'derived-vanished', 'support-skipped', 'replacement',
  'activation-declare', 'activation-reconciled', 'trigger-order', 'trial-order', 'kill-source',
  'library-orientation', 'private-return', 'private-disaster-reveal',
  'disaster-trigger-source', 'disaster-public', 'setup', 'setup-timeout',
  'piercing', 'game-invalid', 'game-draw', 'gm', 'response', 'effect-announced', 'effect-result',
  'prompt-resolved', 'disaster-banned',
])

const COST_MERGE_RESULT_TYPES = new Set([
  'effect', 'faction-effect', 'runes', 'morale', 'trial-action', 'trial', 'enter',
])

const changePattern = /(?:增加|减少|追加|抽取|弃置|失去|恢复|位移|获得|受到)\s*(-?\d+)\s*(?:张|点|格|士气|神力|符文)?/g
const forbiddenPattern = new RegExp(PLAYER_LOG_REDLINE_TERMS.join('|'))

function side(playerIndex: number | undefined, you: number, neutralView = false): LogSide | null {
  return playerIndex == null ? null
    : playerIndex === you ? neutralView ? '下方' : '我方' : neutralView ? '上方' : '对方'
}

function publicCards(event: ActionEvent) {
  return [...new Map((event.cards ?? [])
    .filter(card => !card.hidden && Boolean(card.name))
    .map(card => [card.instanceId || `${card.cardId}:${card.name}`, card])).values()]
}

function firstPublicCard(event: ActionEvent) { return publicCards(event)[0] }
function numberAfter(text: string, pattern: RegExp, fallback = 0) {
  const match = pattern.exec(text)
  return match ? Number(match[1]) : fallback
}
function countFrom(text: string, fallback = 1) {
  return numberAfter(text, /(-?\d+)\s*(?:张|点|格|枚|士气|神力|符文)/, fallback)
}
function badge(value: number, unit: string, invert = false): LogBadge {
  const signed = value > 0 ? `+${value}` : `${value}`
  const tone = (invert ? value < 0 : value > 0) ? 'pos' : value === 0 ? 'info' : 'neg'
  return { value: `${signed}${unit}`, tone }
}
function cardPart(card: Card | undefined, fallback = '隐藏卡牌'): LogPart {
  return card ? { text: `〈${card.name}〉`, card } : { text: fallback }
}
function cardParts(cards: Card[]) {
  return cards.flatMap((card, index) => [
    ...(index ? [{ text: '、' } satisfies LogPart] : []),
    cardPart(card),
  ])
}
function containsOnlyZeroChange(event: ActionEvent) {
  const changes = [...event.text.matchAll(changePattern)]
  return changes.length > 0 && changes.every(item => Number(item[1]) === 0)
}
function safeEffectText(event: ActionEvent) {
  const value = event.effectText?.trim()
  return value && !forbiddenPattern.test(value) ? value : undefined
}
function sourceCard(event: ActionEvent, excluded?: Card) {
  return publicCards(event).find(card => card.instanceId !== excluded?.instanceId && card.cardId !== excluded?.cardId)
}
function sourceNameFromText(text: string, target?: Card) {
  const names = [...text.matchAll(/〈([^〉]+)〉/g)].map(match => match[1])
  const bracketed = names.find(name => name !== target?.name)
  if (bracketed) return bracketed
  return text.match(/^([^〈〉：]{1,24}?)(?:展示|确认|将)/)?.[1]?.trim()
}
function line(sequence: number, icon: IconKind, actor: LogLineRow['actor'], parts: LogPart[], badges: LogBadge[] = [], effectText?: string): LogLineRow {
  return { kind: 'line', sequence, icon, actor, parts, badges, effectText }
}

function groupedIndexes(events: ActionEvent[], groupId: string) {
  const indexes: number[] = []
  events.forEach((event, index) => {
    if (event.playerLogGroupId === groupId) indexes.push(index)
  })
  return indexes
}

function playerLogMetadataScore(event: ActionEvent) {
  return Number(Boolean(event.playerLogGroupId))
    + Number(Boolean(event.playerLogTiming))
    + Number(Boolean(event.playerLogDecisionLabel))
    + Number(Boolean(event.effectResultStatus))
}

function disasterValueText(event: ActionEvent): string | null {
  if (event.type !== 'disaster-value') return null
  const before = event.playerDisasterValue?.before
  const after = event.playerDisasterValue?.after
  if (before == null || after == null || !Number.isInteger(before) || !Number.isInteger(after)
    || before < 0 || after < 0 || before > 2147483647 || after > 2147483647) return null
  return before === after ? `天灾值保持 ${after}` : `天灾值 ${before}→${after}`
}

const structuredFactKeys = [
  'playerLogSemantic', 'playerCombat', 'playerBattlefieldMovement',
  'playerPublicPlacement', 'playerTroopsModifier', 'playerDisasterValue',
] as const
type StructuredFactKey = typeof structuredFactKeys[number]

function hasUsableStructuredFact(event: ActionEvent, key: StructuredFactKey) {
  const fact = event[key]
  if (!fact) return false
  if (key === 'playerDisasterValue') return disasterValueText(event) !== null
  if (key === 'playerBattlefieldMovement') return projectBattlefieldMovement(event, 0, false) !== null
  if (key === 'playerPublicPlacement') return projectPublicPlacement(event, 0, false) !== null
  if (key === 'playerTroopsModifier') return event.type === 'troops-modifier'
    && projectTroopsModifier(event, 0, false).parts[0]?.text !== '兵力修正详情未记录'
  if (key === 'playerCombat') return Boolean(event.playerCombat?.combatId && event.playerCombat.eventKind)
  return Boolean(event.playerLogSemantic?.actionLabel && event.playerLogSemantic.outcomeLabel)
}

function factSignature(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(factSignature).join(',')}]`
  if (value && typeof value === 'object') return `{${Object.entries(value)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, item]) => `${JSON.stringify(key)}:${factSignature(item)}`).join(',')}}`
  return JSON.stringify(value)
}

function sameEventIdentity(left: ActionEvent, right: ActionEvent) {
  if (left.type !== right.type || left.playerIndex !== right.playerIndex) return false
  const leftIds = publicCards(left).map(card => card.instanceId || `${card.cardId}:${card.name}`).sort()
  const rightIds = publicCards(right).map(card => card.instanceId || `${card.cardId}:${card.name}`).sort()
  return !leftIds.length || !rightIds.length || JSON.stringify(leftIds) === JSON.stringify(rightIds)
}

function coalesceDuplicateEvents(events: ActionEvent[]) {
  if (events.length === 1) return events[0]
  const ranked = [...events].sort((left, right) => {
    const factCount = (event: ActionEvent) => structuredFactKeys
      .filter(key => hasUsableStructuredFact(event, key)).length
    return factCount(right) - factCount(left)
      || playerLogMetadataScore(right) - playerLogMetadataScore(left)
  })
  const base = ranked[0]
  const compatible = events.filter(event => sameEventIdentity(base, event))
  // Preserve lazy fields such as legacy Text accessors: projection only reads
  // the fields relevant to the event, so deduplication must do the same.
  const merged = Object.create(base) as ActionEvent
  const set = (key: keyof ActionEvent, value: unknown) =>
    Object.defineProperty(merged, key, { value, enumerable: true, configurable: true })
  for (const key of structuredFactKeys) {
    const facts = compatible.filter(event => hasUsableStructuredFact(event, key))
      .map(event => event[key])
    const distinct = [...new Map(facts.map(fact => [factSignature(fact), fact])).values()]
    // A conflicting same-sequence receipt is ambiguous; never combine fields
    // from different claims or present an arbitrary value as settled fact.
    if (distinct.length === 1) set(key, distinct[0])
    else if (distinct.length > 1) set(key, undefined)
  }
  for (const key of ['playerLogGroupId', 'playerLogTiming', 'playerLogDecisionLabel',
    'effectResultStatus'] as const) {
    if (merged[key] != null) continue
    const values = [...new Set(compatible.map(event => event[key]).filter(value => value != null))]
    if (values.length === 1) set(key, values[0])
  }
  return merged
}

function orderedUniqueEvents(events: ActionEvent[]) {
  const bySequence = new Map<number, ActionEvent[]>()
  for (const event of [...events].sort((left, right) => left.sequence - right.sequence)) {
    const copies = bySequence.get(event.sequence) ?? []
    copies.push(event)
    bySequence.set(event.sequence, copies)
  }
  return [...bySequence.values()].map(coalesceDuplicateEvents)
}

function timingLabel(timing: string | undefined) {
  if (timing === 'enter' || timing === 'promotion-enter') return '登场时效果'
  if (timing === 'active') return '主动效果'
  if (timing === 'attack') return '进攻时效果'
  if (timing === 'death') return '阵亡时效果'
  if (timing === 'leave') return '离场时效果'
  return '效果'
}

const resultLabels: Record<string, string> = {
  resolved: '完成', negated: '被无效', failed: '未能完成',
  skipped: '跳过', declined: '选择不发动',
}

function groupedResultDetail(event: ActionEvent, you: number, source: Card, showPaidCost: boolean, neutralView: boolean): LogLineRow | null {
  const status = event.effectResultStatus
  if (event.type !== 'effect-result' || !status || !resultLabels[status]) return null
  const matches = (event.cards ?? []).filter(card => card.instanceId === source.instanceId)
  if (matches.length !== 1 || matches[0].hidden || !matches[0].name?.trim()) return null
  const segment = event.effectSegmentIndex != null && event.effectSegmentCount != null
    ? `第${event.effectSegmentIndex}/${event.effectSegmentCount}段` : '效果'
  const receipt = event.playerLogSemantic?.sourceInstanceId === source.instanceId
    ? event.playerLogSemantic.outcomeLabel : undefined
  const processedTarget = event.playerLogSemantic?.sourceInstanceId === source.instanceId
    && event.playerLogSemantic.targetInstanceId && event.playerLogSemantic.targetName
    ? event.playerLogSemantic.targetName : undefined
  // The producer emits these two clauses as one receipt.  A continuation can carry
  // the same cumulative paid-cost summary, so only its latest copy is shown.
  const paidMarker = '已支付费用：'
  const paidAt = receipt?.indexOf(paidMarker) ?? -1
  const reason = paidAt < 0 ? receipt : receipt?.slice(0, paidAt).replace(/；$/, '')
  const paid = showPaidCost ? publicPaidCostReceipt(event, source) : null
  return line(event.sequence, 'effect', side(event.playerIndex, you, neutralView), [
    { text: `${segment}${resultLabels[status]}` },
    ...(processedTarget ? [{ text: `；实际处理目标：〈${processedTarget}〉` }] : []),
    ...(reason ? [{ text: `；${reason}` }] : []),
    ...(paid ? [{ text: `；${paid}` }] : []),
  ])
}

function publicPaidCostReceipt(event: ActionEvent, source: Card): string | null {
  const matches = (event.cards ?? []).filter(card => card.instanceId === source.instanceId)
  if (event.type !== 'effect-result' || !source.instanceId || matches.length !== 1
    || matches[0].hidden || !matches[0].name?.trim()
    || event.playerLogSemantic?.sourceInstanceId !== source.instanceId) return null
  const paid = event.playerLogSemantic.outcomeLabel
    ?.match(/(?:^|；)已支付费用：([^；]*)/)?.[1]?.trim()
  return paid ? `已支付费用：${paid}` : null
}

function projectGroupedAction(events: ActionEvent[], indexes: number[], you: number, neutralView: boolean): LogLineRow | null {
  const group = indexes.map(index => events[index])
  const play = group.find(event => event.type === 'play')
  const first = play ?? group[0]
  if (!first) return null

  if (first.playerLogTiming === 'turn-start' || first.playerLogGroupId?.startsWith('turn:')) {
    const changes: string[] = []
    const draw = group.filter(event => event.type === 'draw')
      .reduce((sum, event) => sum + numberAfter(event.text, /抽取\s*(\d+)\s*张/, countFrom(event.text)), 0)
    const morale = group.filter(event => event.type === 'morale')
      .reduce((sum, event) => sum + Math.abs(numberAfter(event.text, /追加\s*(\d+)\s*张/, countFrom(event.text))), 0)
    const milled = group.filter(event => event.type === 'mill')
      .reduce((sum, event) => sum + numberAfter(event.text, /牌库顶部\s*(\d+)\s*张/, countFrom(event.text)), 0)
    if (group.some(event => event.type === 'draw-skipped')) changes.push('先手首回合不抽牌')
    if (draw) changes.push(`抽取${draw}张牌`)
    if (milled) changes.push(`弃置牌库顶部${milled}张牌`)
    if (morale) changes.push(`追加${morale}张士气`)
    for (const event of group) {
      const value = disasterValueText(event)
      if (value) changes.push(value)
      else if (event.type === 'disaster-value') changes.push('天灾值变化（详情未记录）')
    }
    if (!changes.length) return null
    const changeSequence = group.find(event => event.type !== 'turn-start')?.sequence ?? first.sequence
    return line(changeSequence, 'info', side(first.playerIndex, you, neutralView),
      [{ text: `回合开始，${changes.join('，')}` }])
  }

  const source = firstPublicCard(play ?? first)
  // Group metadata must never turn a deliberately hidden source into a new public log row.
  // Once the card is publicly revealed, the authoritative event carries a public card copy and
  // the same group can be rendered normally.
  if (!source) return null
  const parts: LogPart[] = play
    ? [{ text: '打出' }, cardPart(source)]
    : [cardPart(source), { text: `发动${timingLabel(first.playerLogTiming)}` }]
  let suffix = ''
  const decision = group.find(event => event.type === 'effect-decision' && event.playerLogDecisionLabel)
  const restPaid = group.some(event => event.type === 'cost' && /休整|横置/.test(event.text)
    && publicCards(event).some(card => card.instanceId === source?.instanceId))
  const trial = group.find(event => event.type === 'trial')
  const results = group.filter(event => event.type === 'effect-result')
  const result = results.length === 1 ? results[0] : results.at(-1)

  if (play && restPaid && result?.effectResultStatus === 'negated'
    && timingLabel(first.playerLogTiming) === '登场时效果')
    suffix += '，休整该军团并发动登场时效果'
  else if (play && trial && timingLabel(first.playerLogTiming) === '登场时效果')
    suffix += '并发动登场时效果'

  if (decision) suffix += `，${side(decision.playerIndex, you, neutralView) ?? ''}${decision.playerLogDecisionLabel}`

  const drawCounts = new Map<LogSide, number>()
  for (const event of group.filter(candidate => candidate.type === 'draw')) {
    const eventSide = side(event.playerIndex, you, neutralView)
    if (!eventSide) continue
    const amount = numberAfter(event.text, /抽取\s*(\d+)\s*张/, countFrom(event.text))
    drawCounts.set(eventSide, (drawCounts.get(eventSide) ?? 0) + amount)
  }
  for (const eventSide of (neutralView ? ['下方', '上方'] : ['我方', '对方']) as LogSide[]) {
    const amount = drawCounts.get(eventSide)
    if (amount) suffix += `，${eventSide}抽取${amount}张牌`
  }

  const milled = group.filter(event => event.type === 'mill')
    .reduce((sum, event) => sum + numberAfter(event.text, /牌库顶部\s*(\d+)\s*张/, countFrom(event.text)), 0)
  if (milled) suffix += `，弃置牌库顶部${milled}张牌`

  for (const disaster of group.filter(event => event.type === 'disaster-value')) {
    const progress = disasterValueText(disaster)
    suffix += `，${progress ?? '天灾值变化（详情未记录）'}`
  }

  if (trial) {
    const semantic = trial.playerLogSemantic
    if (semantic?.actionLabel && semantic.outcomeLabel)
      suffix += `，${semantic.actionLabel}，${semantic.outcomeLabel}`
    else {
      const progress = trial.text.match(/(?:《([^》]+)》)?试炼进度\s*(\d+)\s*→\s*(\d+)/)
      suffix += progress
        ? `，推进${progress[1] ? `《${progress[1]}》` : ''}试炼 ${progress[2]}→${progress[3]}`
        : '，推进试炼'
    }
  }

  if (results.length === 1 && result?.effectResultStatus && resultLabels[result.effectResultStatus]
    && result.effectSegmentCount != null && result.effectSegmentCount > 1
    && result.effectSegmentIndex != null) {
    suffix += `；第${result.effectSegmentIndex}/${result.effectSegmentCount}段${resultLabels[result.effectResultStatus]}`
  } else if (results.length === 1 && result?.effectResultStatus === 'negated') {
    suffix += play && source?.cardType === 'tactic' && first.playerLogTiming === 'play'
      ? '；该战术的效果被无效'
      : `；${timingLabel(first.playerLogTiming)}被无效`
  } else if (results.length === 1 && result?.effectResultStatus === 'failed') suffix += `；${timingLabel(first.playerLogTiming)}未能完成`
  else if (results.length === 1 && result?.effectResultStatus === 'declined') suffix += `；未发动${timingLabel(first.playerLogTiming)}`
  else if (results.length === 1 && result?.effectResultStatus === 'skipped') suffix += `；${timingLabel(first.playerLogTiming)}跳过`

  if (results.length > 1) {
    const counts = new Map<string, number>()
    for (const event of results) {
      if (event.effectResultStatus && resultLabels[event.effectResultStatus])
        counts.set(event.effectResultStatus, (counts.get(event.effectResultStatus) ?? 0) + 1)
    }
    if (counts.size) suffix += `；效果段：${[...counts].map(([status, count]) => `${count}段${resultLabels[status]}`).join('、')}`
  }

  if (suffix) parts.push({ text: suffix })
  const negatedAfterPayment = results.length === 1 && result?.effectResultStatus === 'negated'
    && publicPaidCostReceipt(result, source)
  const row = line(first.sequence, play ? 'play' : 'effect', side(first.playerIndex, you, neutralView),
    parts, negatedAfterPayment ? [{ value: '费用已支付', tone: 'info' }] : [], safeEffectText(first))
  const lastPaidResult = results.filter(event => publicPaidCostReceipt(event, source)).at(-1)
  const detail = group.flatMap(event => {
    const resultDetail = groupedResultDetail(event, you, source, event === lastPaidResult, neutralView)
    if (resultDetail) return [resultDetail]
    if (event.type === 'effect-result' || event.type === 'cost' || event === first) return []
    const projected = projectLine(event, you, [], [], neutralView)
    return projected ? [projected] : []
  })
  if (detail.length) row.detail = detail
  return row
}
function isPrivateHandAddEvent(event: ActionEvent) {
  return event.type === 'authority-event' && /因效果将\s*\d+\s*张牌加入手牌/.test(event.text)
}

function addUniqueBadge(badges: LogBadge[], item: LogBadge) {
  if (!badges.some(existing => existing.value === item.value)) badges.push(item)
}

function paymentBadges(event: ActionEvent) {
  const found: LogBadge[] = []
  const resources: Array<[RegExp, string]> = [
    [/(?:消耗|支付)\s*(\d+)\s*士气/, '士气'],
    [/(?:消耗|支付)\s*(\d+)\s*符文/, '符文'],
    [/(?:消耗|支付|翻转)\s*(\d+)\s*神力/, '神力'],
    [/(?:弃置|支付)\s*(\d+)\s*张?手牌/, '手牌'],
    [/返还\s*(\d+)\s*张?士气/, '士气'],
  ]
  for (const [pattern, unit] of resources) {
    const value = numberAfter(event.text, pattern)
    if (value) addUniqueBadge(found, { value: `${unit} −${value}`, tone: 'neg' })
  }
  if (/休整|横置/.test(event.text)) addUniqueBadge(found, { value: '休整', tone: 'info' })
  return found
}

function trialProgressBadge(event: ActionEvent): LogBadge | null {
  const progress = event.text.match(/试炼进度\s*(\d+)\s*→\s*(\d+)/)
  if (!progress) return null
  return { value: `试炼 ${progress[1]}→${progress[2]}`, tone: 'pos' }
}

function effectOutcomeBadges(event: ActionEvent) {
  const badges: LogBadge[] = []
  const numeric: Array<[RegExp, string]> = [
    [/(?:获得|增加|追加)\s*(\d+)\s*(?:枚)?符文/, '符文'],
    [/(?:获得|增加|追加)\s*(\d+)\s*(?:张|枚)?(?:休整)?士气/, '士气'],
    [/试炼(?:进度)?\s*\+\s*(\d+)/, '试炼'],
  ]
  for (const [pattern, unit] of numeric) {
    const value = numberAfter(event.text, pattern)
    if (value) addUniqueBadge(badges, badge(value, unit))
  }
  for (const [pattern, value] of [
    [/转为活跃/, '转为活跃'],
    [/转为休整|横置/, '转为休整'],
    [/获得冲锋/, '获得冲锋'],
    [/获得强攻/, '获得强攻'],
    [/获得震击/, '获得震击'],
  ] as const) {
    if (pattern.test(event.text)) addUniqueBadge(badges, { value, tone: 'info' })
  }
  return badges
}

function projectSemanticPlayerLog(event: ActionEvent, you: number, neutralView: boolean): LogLineRow | null {
  const semantic = event.playerLogSemantic
  if (!semantic?.actionLabel || !semantic.outcomeLabel) return null
  const source = publicCards(event)
    .find(card => card.instanceId === semantic.sourceInstanceId)
  if (semantic.sourceInstanceId && !source) return null
  const sourcePart = source
    ? cardPart(source)
    : semantic.sourceName ? { text: `〈${semantic.sourceName}〉` } satisfies LogPart : null
  if (!sourcePart && event.type !== 'trial') return null
  return line(event.sequence, 'effect', side(event.playerIndex, you, neutralView), [
    ...(sourcePart ? [sourcePart] : []),
    { text: semantic.actionLabel },
    { text: `，${semantic.outcomeLabel}` },
  ])
}

function costBadges(events: ActionEvent[], index: number, event: ActionEvent) {
  const found: LogBadge[] = []
  for (let cursor = index - 1; cursor >= 0 && index - cursor <= 8; cursor--) {
    const candidate = events[cursor]
    if (candidate.type === 'cost') {
      if (candidate.playerIndex !== event.playerIndex) break
      for (const item of paymentBadges(candidate)) addUniqueBadge(found, item)
      continue
    }
    if (!PLAYER_LOG_HIDDEN_TYPES.has(candidate.type)) break
  }
  return found
}

function costParts(events: ActionEvent[], index: number, event: ActionEvent) {
  const effectCardIds = new Set(publicCards(event).map(card => card.instanceId || `${card.cardId}:${card.name}`))
  const seen = new Set<string>()
  const details: LogPart[] = []
  for (let cursor = index - 1; cursor >= 0 && index - cursor <= 8; cursor--) {
    const candidate = events[cursor]
    if (candidate.type === 'cost') {
      if (candidate.playerIndex !== event.playerIndex) break
      const action = /弃置/.test(candidate.text) ? '弃置'
        : /牌库底部/.test(candidate.text) ? '置于牌库底部'
          : null
      if (action) {
        const cards = publicCards(candidate).filter(card => {
          const key = card.instanceId || `${card.cardId}:${card.name}`
          if (effectCardIds.has(key) || seen.has(key)) return false
          seen.add(key)
          return true
        })
        if (cards.length) details.push({ text: `；费用：${action}` }, ...cardParts(cards))
      }
      continue
    }
    if (!PLAYER_LOG_HIDDEN_TYPES.has(candidate.type)) break
  }
  return details
}

function costMergesIntoFollowingResult(events: ActionEvent[], index: number) {
  const event = events[index]
  for (let cursor = index + 1; cursor < events.length && cursor - index <= 8; cursor++) {
    const candidate = events[cursor]
    if (candidate.type === 'cost') {
      if (candidate.playerIndex !== event.playerIndex) break
      continue
    }
    if (PLAYER_LOG_HIDDEN_TYPES.has(candidate.type)) continue
    if (candidate.playerIndex !== event.playerIndex && candidate.playerIndex != null) break
    return COST_MERGE_RESULT_TYPES.has(candidate.type)
  }
  return false
}

function movementSlotLabel(playerIndex: number, row: number, slot: number, you: number, neutralView: boolean) {
  const label = battlefieldSlotLabel(playerIndex === you ? 'self' : 'opponent', row, slot)
  return neutralView ? label.replace(/^我方/, '下方').replace(/^对方/, '上方') : label
}

function projectBattlefieldMovement(event: ActionEvent, you: number, neutralView: boolean): LogLineRow | null {
  if (event.type !== 'move' && event.type !== 'faction-effect') return null
  const cards = publicCards(event)
  const facts = (event.playerBattlefieldMovement?.facts ?? []).flatMap(fact => {
    if (!fact.instanceId || !Number.isInteger(fact.battlefieldPlayerIndex)
      || !Number.isInteger(fact.fromRow) || !Number.isInteger(fact.fromSlot)
      || !Number.isInteger(fact.toRow) || !Number.isInteger(fact.toSlot)
      || (fact.battlefieldPlayerIndex !== 0 && fact.battlefieldPlayerIndex !== 1)
      || (fact.fromRow !== 0 && fact.fromRow !== 1) || (fact.toRow !== 0 && fact.toRow !== 1)
      || fact.fromSlot == null || fact.fromSlot < 0 || fact.fromSlot > 2
      || fact.toSlot == null || fact.toSlot < 0 || fact.toSlot > 2
      || (fact.fromRow === fact.toRow && fact.fromSlot === fact.toSlot)) return []
    const card = cards.find(candidate => candidate.instanceId === fact.instanceId)
    return card ? [{ card, battlefieldPlayerIndex: fact.battlefieldPlayerIndex,
      fromRow: fact.fromRow, fromSlot: fact.fromSlot, toRow: fact.toRow, toSlot: fact.toSlot }] : []
  })
  if (!facts.length) return null
  const row = line(event.sequence, 'info', side(event.playerIndex, you, neutralView),
    [...cardParts(facts.map(fact => fact.card)), { text: '已位移' }])
  row.detail = facts.map(fact => line(event.sequence, 'info', null, [
    cardPart(fact.card),
    { text: `从${movementSlotLabel(fact.battlefieldPlayerIndex, fact.fromRow, fact.fromSlot, you, neutralView)}移动至${movementSlotLabel(fact.battlefieldPlayerIndex, fact.toRow, fact.toSlot, you, neutralView)}` },
  ]))
  return row
}

function projectPublicPlacement(event: ActionEvent, you: number, neutralView: boolean): LogLineRow | null {
  const fact = event.playerPublicPlacement
  if (event.type !== 'put' || !fact?.instanceId
    || !Number.isInteger(fact.ownerPlayerIndex) || !Number.isInteger(fact.controllerPlayerIndex)
    || (fact.ownerPlayerIndex !== 0 && fact.ownerPlayerIndex !== 1)
    || (fact.controllerPlayerIndex !== 0 && fact.controllerPlayerIndex !== 1)
    || fact.ownerPlayerIndex === fact.controllerPlayerIndex
    || !Number.isInteger(fact.row) || !Number.isInteger(fact.slot)
    || (fact.row !== 0 && fact.row !== 1)
    || fact.slot == null || fact.slot < 0 || fact.slot > 2
    || typeof fact.tapped !== 'boolean'
    || (fact.durationCode != null && fact.durationCode !== 'until-owner-next-turn-end')) return null
  const matches = (event.cards ?? []).filter(candidate => candidate.instanceId === fact.instanceId)
  if (matches.length !== 1) return null
  const card = matches[0]
  if (card.hidden || !card.name?.trim() || card.ownerIndex !== fact.ownerPlayerIndex
    || card.tapped !== fact.tapped) return null
  const owner = side(fact.ownerPlayerIndex, you, neutralView)
  const controller = side(fact.controllerPlayerIndex, you, neutralView)
  if (!owner || !controller) return null
  const duration = fact.durationCode === 'until-owner-next-turn-end'
    ? `；直到${owner}下个回合结束` : ''
  return line(event.sequence, 'play', owner, [cardPart(card),
    { text: `置入${movementSlotLabel(fact.controllerPlayerIndex, fact.row, fact.slot, you, neutralView)}，由${controller}控制（${fact.tapped ? '休整' : '活跃'}）${duration}` },
  ])
}

function projectTroopsModifier(event: ActionEvent, you: number, neutralView: boolean): LogLineRow {
  const fact = event.playerTroopsModifier
  const matches = (event.cards ?? []).filter(card => card.instanceId === fact?.targetInstanceId)
  if (typeof fact?.targetInstanceId !== 'string' || !fact.targetInstanceId.trim()
    || (fact.targetControllerPlayerIndex !== 0 && fact.targetControllerPlayerIndex !== 1)
    || fact.targetControllerPlayerIndex !== event.playerIndex
    || !Number.isInteger(fact.troopsDelta) || fact.troopsDelta == null
    || fact.troopsDelta < -2147483648 || fact.troopsDelta > 2147483647
    || fact.durationCode !== 'this-turn' || matches.length !== 1
    || matches[0].hidden || typeof matches[0].name !== 'string' || !matches[0].name.trim())
    return line(event.sequence, 'effect', null, [{ text: '兵力修正详情未记录' }])
  const delta = fact.troopsDelta > 0 ? `+${fact.troopsDelta}` : String(fact.troopsDelta)
  return line(event.sequence, 'effect', side(fact.targetControllerPlayerIndex, you, neutralView),
    [cardPart(matches[0]), { text: `本回合兵力修正${delta}` }])
}

function projectLine(event: ActionEvent, you: number, costs: LogBadge[] = [], costDetails: LogPart[] = [], neutralView = false): LogLineRow | null {
  if (event.type === 'troops-modifier') return projectTroopsModifier(event, you, neutralView)
  const movement = projectBattlefieldMovement(event, you, neutralView)
  if (movement) return movement
  const placement = projectPublicPlacement(event, you, neutralView)
  if (placement) return placement
  if (event.playerLogSemantic && event.type !== 'disaster-value')
    return projectSemanticPlayerLog(event, you, neutralView)
  if ((!PLAYER_LOG_VISIBLE_TYPES.has(event.type) && !isPrivateHandAddEvent(event))
    || (event.type !== 'disaster-value' && containsOnlyZeroChange(event))) return null
  const actor = side(event.playerIndex, you, neutralView)
  const card = firstPublicCard(event)
  const effectText = safeEffectText(event)
  switch (event.type) {
    case 'play': return line(event.sequence, 'play', actor, [{ text: '打出' }, cardPart(card)], [], effectText)
    case 'counter-set': return line(event.sequence, 'play', actor, [{ text: '盖伏 1 张反击战术' }])
    case 'put': return line(event.sequence, 'play', actor, [cardPart(card), { text: '登场' }], [], effectText)
    case 'enter': return line(event.sequence, 'play', actor, [cardPart(card), { text: '登场' }, ...costDetails],
      [...(/休整/.test(event.text) ? [{ value: '休整', tone: 'info' } satisfies LogBadge] : []), ...costs])
    case 'promotion': return line(event.sequence, 'play', actor, [cardPart(card), { text: '晋升登场' }], [], effectText)
    case 'draw': {
      const amount = numberAfter(event.text, /抽取\s*(\d+)\s*张/, countFrom(event.text))
      return line(event.sequence, 'draw', actor, [{ text: '抽取' }], [{ value: `${amount}张`, tone: 'info' }])
    }
    case 'authority-event': return line(event.sequence, 'hand-add', actor,
      [{ text: '因效果加入手牌' }], [{ value: `${countFrom(event.text)}张`, tone: 'info' }])
    case 'move': {
      const cards = publicCards(event)
      if (/置入墓地|进入墓地/.test(event.text))
        return line(event.sequence, 'info', actor, [cardPart(cards.at(-1)), { text: '进入墓地' }])
      const moved = /互换阵地/.test(event.text) ? cards : cards.length > 1 ? cards.slice(-1) : cards
      return line(event.sequence, 'info', actor, [...cardParts(moved), { text: '已移动' }])
    }
    case 'reveal': {
      const handAdd = /加入手牌/.test(event.text)
      if (handAdd && (!card || card.hidden)) return null
      const source = sourceCard(event, card)
      const sourceName = source?.name ?? sourceNameFromText(event.text, card)
      const parts: LogPart[] = []
      if (source) parts.push(cardPart(source), { text: '：' })
      else if (sourceName) parts.push({ text: `〈${sourceName}〉：` })
      parts.push(cardPart(card), { text: handAdd ? '加入手牌' : '公开' })
      return line(event.sequence, handAdd ? 'hand-add' : 'info', actor, parts)
    }
    case 'search':
      return card?.hidden ? null : line(event.sequence, 'hand-add', actor, [cardPart(card), { text: '加入手牌' }])
    case 'return': {
      if (/手牌/.test(event.text) && card && !card.hidden) return line(event.sequence, 'hand-add', actor, [cardPart(card), { text: '返回手牌' }])
      return line(event.sequence, 'info', actor, [cardPart(card), { text: '返回' }])
    }
    case 'discard': return line(event.sequence, 'info', actor, [{ text: '弃置' }, cardPart(card)])
    case 'counter-displaced':
    case 'counter-replaced': return line(event.sequence, 'info', actor, [cardPart(card), { text: '离开盖伏区并进入墓地' }])
    case 'grave':
    case 'leave': return line(event.sequence, 'info', actor, [cardPart(card), { text: event.type === 'grave' ? '进入墓地' : '离场' }])
    case 'mill': {
      const cards = publicCards(event)
      return line(event.sequence, 'info', actor, [{ text: '牌库顶弃置：' }, ...cardParts(cards)],
        [{ value: `${countFrom(event.text, cards.length)}张`, tone: 'info' }])
    }
    case 'library': return line(event.sequence, 'info', actor,
      [cardPart(card), { text: event.text.includes('底部') ? '返回牌库底部' : '返回牌库顶部' }])
    case 'reorder': {
      const top = numberAfter(event.text, /([0-9]+)\s*张牌放回牌库顶部/)
      const bottom = numberAfter(event.text, /([0-9]+)\s*张牌放回牌库底部/)
      return line(event.sequence, 'info', actor, [{ text: '整理牌库' }], [
        { value: `顶部 ${top}张`, tone: 'info' }, { value: `底部 ${bottom}张`, tone: 'info' },
      ])
    }
    case 'attach': {
      const cards = publicCards(event)
      return line(event.sequence, 'effect', actor, cards.length > 1
        ? [cardPart(cards[0]), { text: '与' }, ...cardParts(cards.slice(1)), { text: '叠放' }]
        : [cardPart(card), { text: '叠放' }])
    }
    case 'damage': {
      const amount = Math.abs(numberAfter(event.text, /(?:受到|失去|伤害)\s*(\d+)\s*点/, countFrom(event.text)))
      return line(event.sequence, 'attack', actor, [cardPart(card, /主宰/.test(event.text) ? '主宰' : '目标'), { text: '受到伤害' }], [{ value: `−${amount}点`, tone: 'neg' }])
    }
    case 'heal': {
      const amount = Math.abs(numberAfter(event.text, /恢复\s*(\d+)\s*点/, countFrom(event.text)))
      return line(event.sequence, 'info', actor, [cardPart(card, /主宰/.test(event.text) ? '主宰' : '目标'), { text: '恢复' }], [{ value: `+${amount}点`, tone: 'pos' }])
    }
    case 'morale': {
      const amount = numberAfter(event.text, /士气[^-+\d]*([+-]?\d+)/, countFrom(event.text))
      return line(event.sequence, 'effect', actor,
        [...(card ? [cardPart(card), { text: '：士气变化' } satisfies LogPart] : [{ text: '士气变化' }]), ...costDetails],
        [badge(amount, '士气'), ...costs])
    }
    case 'runes': {
      const amount = numberAfter(event.text, /符文[^-+\d]*([+-]?\d+)/, countFrom(event.text))
      return line(event.sequence, 'effect', actor,
        [...(card ? [cardPart(card), { text: '：符文变化' } satisfies LogPart] : [{ text: '符文变化' }]), ...costDetails],
        [badge(amount, '符文'), ...costs])
    }
    case 'trial': {
      if (/完成试炼/.test(event.text))
        return line(event.sequence, 'effect', actor,
          card ? [cardPart(card), { text: '：完成试炼' }] : [{ text: '完成试炼' }])
      const progress = trialProgressBadge(event)
      return line(event.sequence, 'effect', actor,
        [...(card ? [cardPart(card), { text: '：推进试炼' } satisfies LogPart] : [{ text: '推进试炼' }]), ...costDetails],
        [...(progress ? [progress] : []), ...costs])
    }
    case 'trial-action': return line(event.sequence, 'effect', actor,
      [...(card ? [cardPart(card), { text: '：发动试炼' } satisfies LogPart] : [{ text: '发动试炼' }]), ...costDetails],
      [{ value: '休整', tone: 'info' }, ...costs])
    case 'dice': {
      const amount = numberAfter(event.text, /(?:掷骰|点数)[^\d]*(\d+)/, countFrom(event.text))
      return line(event.sequence, 'dice', actor, card ? [cardPart(card), { text: '掷骰' }] : [{ text: '掷骰' }], [{ value: `${amount}点`, tone: 'info' }])
    }
    case 'effect':
    case 'faction-effect': {
      const cards = publicCards(event)
      const parts: LogPart[] = card
        ? [cardPart(card), { text: event.type === 'faction-effect' ? '发动阵营效果' : '发动效果' }]
        : [{ text: event.type === 'faction-effect' ? '发动阵营效果' : '发动效果' }]
      if (cards.length > 1) parts.push({ text: '：' }, ...cardParts(cards.slice(1)))
      parts.push(...costDetails)
      const badges = [...costs]
      const movement = event.text.match(/位移\s*(\d+)\s*格/)?.[1]
      const draw = event.text.match(/抽取\s*(\d+)\s*张/)?.[1]
      if (movement) badges.unshift({ value: `${movement}格`, tone: 'info' })
      if (draw) badges.unshift({ value: `${draw}张`, tone: 'info' })
      for (const item of effectOutcomeBadges(event)) addUniqueBadge(badges, item)
      return line(event.sequence, 'effect', actor, parts, badges, effectText)
    }
    case 'cost': {
      const cards = publicCards(event)
      return line(event.sequence, 'effect', actor,
        cards.length ? [{ text: '支付费用：' }, ...cardParts(cards)] : [{ text: '支付费用' }], paymentBadges(event))
    }
    case 'continuous': return line(event.sequence, 'effect', actor,
      card ? [cardPart(card), { text: '：相关计数变化（详情未记录）' }]
        : [{ text: '相关计数变化（详情未记录）' }])
    case 'disaster-value': return line(event.sequence, 'disaster', null,
      [{ text: disasterValueText(event) ?? '天灾值变化（详情未记录）' }])
    case 'extra-turn': return line(event.sequence, 'effect', actor, [{ text: '获得额外回合' }], [{ value: '+1回合', tone: 'pos' }])
    case 'defense': return line(event.sequence, 'defense', actor, [{ text: '抵挡记录（结果未记录）' }])
    case 'support': return line(event.sequence, 'support', actor, [{ text: '支援记录（结果未记录）' }])
    case 'initiative-choice': return line(event.sequence, 'info', actor, [{ text: `选择${/后手/.test(event.text) ? '后手' : '先手'}` }])
    case 'mulligan': return line(event.sequence, 'info', actor, [{ text: '调度手牌' }], [{ value: `${countFrom(event.text)}张`, tone: 'info' }])
    case 'disaster':
    case 'disaster-active': return line(event.sequence, 'disaster', null, [event.type === 'disaster' ? { text: '本局天灾：' } : { text: '天灾效果：' }, cardPart(card)])
    case 'game-over':
    case 'special-victory': return line(event.sequence, 'game-over', null,
      [{ text: `${side(event.playerIndex, you, neutralView) ?? (neutralView ? '上方' : '对方')}胜利` }])
    default: return null
  }
}

const combatReasonLabels: Record<string, string> = {
  'attacker-left': '进攻军团已离场',
  'target-left': '被进攻军团已离场',
  'choice-unavailable': '所选抵挡或支援已无法使用',
  'context-unavailable': '本次进攻已结束',
  'extra-cost-unpaid': '未支付额外费用',
  'thunder-roll-failed': '雷霆天怒掷骰未满足进攻条件',
}

function combatResult(members: ActionEvent[]) {
  const outcomes = members.map(event => event.playerCombat?.outcomeCode)
  const has = (outcome: string) => outcomes.includes(outcome)
  const damage = members.find(event => event.playerCombat?.masterDamage != null)?.playerCombat?.masterDamage
  const invalid = has('invalid-support') ? '支援无效' : has('invalid-block') ? '抵挡无效' : ''
  const settled = has('defeated') ? '击破'
    : has('not-defeated') ? '未击破'
      : damage != null && damage > 0 ? '造成伤害'
        : has('supported') ? '完成支援'
          : has('blocked') ? '完成抵挡'
            : has('unblocked') ? '未抵挡'
              : '战斗结果未记录'
  return { result: has('aborted') ? '进攻中止' : invalid ? `${invalid}；${settled}` : settled, damage }
}

function projectCombat(attack: ActionEvent, members: ActionEvent[], you: number, neutralView: boolean): LogCombatRow | null {
  const cards = publicCards(attack)
  const attacker = cards[0]
  if (!attacker) return null
  const declared = attack.playerCombat
  const related = members.filter(event => event.sequence !== attack.sequence)
    .sort((left, right) => left.sequence - right.sequence)
  const finalTargetEvent = [...related].reverse().find(event =>
    ['combat', 'support', 'defense'].includes(event.playerCombat?.eventKind ?? '')
    && !['invalid-block', 'invalid-support'].includes(event.playerCombat?.outcomeCode ?? ''))
  const finalTargetId = finalTargetEvent?.playerCombat?.targetInstanceId
  const defender: Card | '主宰' | '目标' = finalTargetEvent
    ? finalTargetId
      ? publicCards(finalTargetEvent).find(card => card.instanceId === finalTargetId) ?? '目标'
      : finalTargetEvent.playerCombat?.eventKind === 'defense' ? '主宰' : '目标'
    : cards[1] ?? '主宰'
  const { result, damage } = combatResult(related)
  const detail: LogLineRow[] = []
  const shownInvalid = new Set<string>()
  for (const event of related) {
    const combat = event.playerCombat
    if (!combat) continue
    const actor = neutralView ? side(event.playerIndex, you, true) ?? '上方' : event.playerIndex === you ? '你' : '对手'
    const reason = combat.publicReasonCode && combatReasonLabels[combat.publicReasonCode]
    const publicParticipants = publicCards(event)
    switch (combat.eventKind) {
      case 'defense':
        detail.push(line(event.sequence, 'defense', null,
          combat.outcomeCode === 'blocked'
            ? [{ text: `${actor}完成抵挡：` }, ...cardParts(publicParticipants)]
            : [{ text: `${actor}未抵挡` }],
          combat.masterDamage == null ? [] : [{ value: `主宰受到 ${combat.masterDamage} 点伤害`, tone: 'neg' }]))
        break
      case 'support': {
        const supporters = publicParticipants.filter(card => card.instanceId !== combat.attackerInstanceId
          && card.instanceId !== combat.targetInstanceId)
        detail.push(line(event.sequence, 'support', null,
          [{ text: `${actor}完成支援` }, ...(supporters.length ? [{ text: '：' }, ...cardParts(supporters)] : [])]))
        break
      }
      case 'defense-invalid':
        if (shownInvalid.has(`${combat.outcomeCode}:${combat.publicReasonCode}`)) break
        shownInvalid.add(`${combat.outcomeCode}:${combat.publicReasonCode}`)
        detail.push(line(event.sequence, 'defense', null,
          [{ text: `${actor}${combat.outcomeCode === 'invalid-support' ? '支援' : '抵挡'}无效` },
            ...(reason ? [{ text: `：${reason}` }] : [])]))
        break
      case 'combat': {
        const targetCard = publicParticipants.find(card => card.instanceId === combat.targetInstanceId)
        detail.push(line(event.sequence, 'attack', null,
          targetCard
            ? [cardPart(targetCard), { text: combat.outcomeCode === 'defeated' ? '被击破' : '未被击破' }]
            : [{ text: combat.outcomeCode === 'defeated' ? '目标被击破' : '目标未被击破' }],
          [
            ...(combat.attackerTroops == null ? [] : [{ value: `进攻值 ${combat.attackerTroops}`, tone: 'info' as const }]),
            ...(combat.defenderTroops == null ? [] : [{ value: `目标兵力 ${combat.defenderTroops}`, tone: 'info' as const }]),
          ]))
        break
      }
      case 'attack-aborted':
        detail.push(line(event.sequence, 'attack', null,
          [{ text: '进攻中止' }, ...(reason ? [{ text: `：${reason}` }] : [])]))
        break
      default: break
    }
  }
  return { kind: 'combat', sequence: attack.sequence, attacker, defender,
    attackTroops: declared?.attackerTroops,
    defendTroops: finalTargetEvent ? finalTargetEvent.playerCombat?.defenderTroops : declared?.defenderTroops,
    result, damage, detail }
}

export function projectLog(events: ActionEvent[], you: number, _names: string[], neutralView = false): LogRow[] {
  const ordered = orderedUniqueEvents(events)
  const combats = new Map<string, ActionEvent[]>()
  for (const event of ordered) {
    const id = event.playerCombat?.combatId
    if (!id) continue
    const members = combats.get(id) ?? []
    members.push(event)
    combats.set(id, members)
  }
  const consumed = new Set<number>()
  const combatAttacks = new Set(ordered.filter(event => event.playerCombat?.eventKind === 'attack')
    .map(event => event.playerCombat?.combatId).filter((id): id is string => Boolean(id)))
  const shownOrphans = new Set<string>()
  const rows: LogRow[] = []
  for (let index = 0; index < ordered.length; index++) {
    if (consumed.has(index)) continue
    const event = ordered[index]
    if (event.type === 'turn-start') {
      rows.push({ kind: 'turn', sequence: event.sequence, round: numberAfter(event.text, /第\s*(\d+)\s*回合/, 0), side: side(event.playerIndex, you, neutralView) ?? (neutralView ? '上方' : '对方') })
      if (event.playerLogGroupId) {
        const indexes = groupedIndexes(ordered, event.playerLogGroupId)
        const row = projectGroupedAction(ordered, indexes, you, neutralView)
        indexes.forEach(item => consumed.add(item))
        if (row) rows.push(row)
        else for (const item of indexes) if (ordered[item].type === 'disaster-value') {
          const valueRow = projectLine(ordered[item], you, [], [], neutralView)
          if (valueRow) rows.push(valueRow)
        }
      }
      continue
    }
    if (event.playerLogGroupId) {
      const indexes = groupedIndexes(ordered, event.playerLogGroupId)
      if (indexes[0] !== index) continue
      const row = projectGroupedAction(ordered, indexes, you, neutralView)
      indexes.forEach(item => consumed.add(item))
      if (row) rows.push(row)
      else for (const item of indexes) if (ordered[item].type === 'disaster-value') {
        const valueRow = projectLine(ordered[item], you, [], [], neutralView)
        if (valueRow) rows.push(valueRow)
      }
      continue
    }
    if (event.type === 'effect-result' && event.playerLogSemantic) {
      const source = firstPublicCard(event)
      const row = source && groupedResultDetail(event, you, source, true, neutralView)
      if (row && source) {
        row.parts.unshift(cardPart(source), { text: '：' })
        rows.push(row)
      }
      continue
    }
    if (event.playerCombat?.combatId && event.playerCombat.eventKind !== 'attack') {
      const id = event.playerCombat.combatId
      if (!combatAttacks.has(id) && !shownOrphans.has(id)) {
        shownOrphans.add(id)
        const members = combats.get(id) ?? []
        const aborted = members.find(member => member.playerCombat?.eventKind === 'attack-aborted')
        const reasonCode = aborted?.playerCombat?.publicReasonCode
        const reason = reasonCode && combatReasonLabels[reasonCode]
        const participants = aborted ? publicCards(aborted) : []
        const attacker = participants.find(card => card.instanceId === aborted?.playerCombat?.attackerInstanceId)
        const target = participants.find(card => card.instanceId === aborted?.playerCombat?.targetInstanceId)
        rows.push(line(event.sequence, 'attack', null, aborted && reasonCode === 'thunder-roll-failed'
          ? [
              ...(attacker ? [cardPart(attacker)] : [{ text: '本次战斗' }]),
              ...(target ? [{ text: '进攻' }, cardPart(target)]
                : reasonCode === 'thunder-roll-failed' ? [{ text: '进攻主宰' }] : []),
              { text: '：进攻中止' },
              ...(reason ? [{ text: `：${reason}` }] : []),
            ]
          : [{ text: `本次战斗：${combatResult(members).result}` }]))
      }
      continue
    }
    if (event.type === 'attack' || event.playerCombat?.eventKind === 'attack') {
      const members = event.playerCombat?.combatId ? combats.get(event.playerCombat.combatId) ?? [] : []
      const combat = projectCombat(event, members, you, neutralView)
      if (combat) rows.push(combat)
      continue
    }
    if (event.type === 'trial-action') {
      const row = projectLine(event, you, [], [], neutralView)
      const progress = ordered[index + 1]
      if (row && progress?.type === 'trial' && progress.playerIndex === event.playerIndex) {
        const progressBadge = trialProgressBadge(progress)
        if (progressBadge) row.badges.push(progressBadge)
        consumed.add(index + 1)
      }
      if (row) rows.push(row)
      continue
    }
    if (event.type === 'cost' && costMergesIntoFollowingResult(ordered, index)) continue
    if (isPrivateHandAddEvent(event)) {
      const precedingPublicAdd = ordered.slice(Math.max(0, index - 4), index).some(candidate =>
        candidate.playerIndex === event.playerIndex
        && ['reveal', 'search', 'return'].includes(candidate.type)
        && /加入手牌|返回手牌|回到手牌/.test(candidate.text))
      if (precedingPublicAdd) continue
    }
    if (PLAYER_LOG_HIDDEN_TYPES.has(event.type) && !isPrivateHandAddEvent(event) && !event.playerLogSemantic) continue
    const receivesCost = COST_MERGE_RESULT_TYPES.has(event.type)
    const row = projectLine(event, you,
      receivesCost ? costBadges(ordered, index, event) : [],
      receivesCost ? costParts(ordered, index, event) : [], neutralView)
    if (row) rows.push(row)
  }
  return rows
}

export function playerLogContainsForbiddenTerms(rows: LogRow[]) {
  const text = JSON.stringify(rows.map(row => {
    if (row.kind === 'turn') return `${row.round}${row.side}`
    if (row.kind === 'combat') return [row.result, ...row.detail.flatMap(item => item.parts.map(part => part.text))].join('')
    return [...row.parts.map(part => part.text), ...row.badges.map(item => item.value), row.effectText ?? '',
      ...(row.detail?.flatMap(item => item.parts.map(part => part.text)) ?? [])].join('')
  }))
  return PLAYER_LOG_REDLINE_TERMS.filter(term => text.includes(term))
}
