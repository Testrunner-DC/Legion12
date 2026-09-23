import type { ActionEvent, Card } from '../types'

export type IconKind = 'attack' | 'defense' | 'support' | 'effect' | 'disaster'
  | 'dice' | 'hand-add' | 'draw' | 'play' | 'info' | 'game-over'
export type LogPart = { text: string; card?: Card }
export type LogBadge = { value: string; tone: 'pos' | 'neg' | 'info' }
export type LogLineRow = {
  kind: 'line'
  sequence: number
  icon: IconKind
  actor: '我方' | '对方' | null
  parts: LogPart[]
  badges: LogBadge[]
  effectText?: string
}
export type LogTurnRow = { kind: 'turn'; sequence: number; round: number; side: '我方' | '对方' }
export type LogCombatRow = {
  kind: 'combat'
  sequence: number
  attacker: Card
  defender: Card | '主宰'
  attackTroops: number
  defendTroops?: number
  result: '击破' | '被抵挡' | '造成伤害' | '未击破'
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
  'initiative-choice', 'mulligan', 'disaster', 'disaster-active',
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
  'counter-displaced', 'counter-replaced', 'attach', 'activation-declare', 'activation-reconciled',
  'trigger-order', 'trial', 'trial-order', 'trial-action', 'continuous', 'enter', 'kill-source',
  'mill', 'library', 'library-orientation', 'private-return', 'private-disaster-reveal',
  'disaster-trigger-source', 'disaster-public', 'setup', 'setup-timeout', 'extra-turn',
  'piercing', 'game-invalid', 'game-draw', 'gm', 'response', 'effect-announced', 'effect-result',
  'prompt-resolved', 'disaster-value', 'disaster-banned', 'cost',
])

const changePattern = /(?:增加|减少|追加|抽取|弃置|失去|恢复|位移|获得|受到)\s*(-?\d+)\s*(?:张|点|格|士气|神力|符文)?/g
const forbiddenPattern = new RegExp(PLAYER_LOG_REDLINE_TERMS.join('|'))

function side(playerIndex: number | undefined, you: number): '我方' | '对方' | null {
  return playerIndex == null ? null : playerIndex === you ? '我方' : '对方'
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

function costBadges(events: ActionEvent[], index: number, event: ActionEvent) {
  const found: LogBadge[] = []
  for (let cursor = index - 1; cursor >= 0 && index - cursor <= 4; cursor--) {
    const candidate = events[cursor]
    if (candidate.type !== 'cost') break
    if (candidate.playerIndex !== event.playerIndex) continue
    const resources: Array<[RegExp, string]> = [
      [/(?:消耗|支付)\s*(\d+)\s*士气/, '士气'],
      [/(?:消耗|支付)\s*(\d+)\s*符文/, '符文'],
      [/(?:消耗|支付)\s*(\d+)\s*神力/, '神力'],
      [/(?:弃置|支付)\s*(\d+)\s*张?手牌/, '手牌'],
    ]
    for (const [pattern, unit] of resources) {
      const value = numberAfter(candidate.text, pattern)
      if (value) found.push({ value: `${unit} −${value}`, tone: 'neg' })
    }
    if (/休整/.test(candidate.text)) found.push({ value: '休整', tone: 'info' })
  }
  return found
}

function projectLine(event: ActionEvent, you: number, costs: LogBadge[] = []): LogLineRow | null {
  if (!PLAYER_LOG_VISIBLE_TYPES.has(event.type) || containsOnlyZeroChange(event)) return null
  const actor = side(event.playerIndex, you)
  const card = firstPublicCard(event)
  const effectText = safeEffectText(event)
  switch (event.type) {
    case 'play': return line(event.sequence, 'play', actor, [{ text: '打出' }, cardPart(card)], [], effectText)
    case 'counter-set': return line(event.sequence, 'play', actor, [{ text: '盖伏 1 张反击战术' }])
    case 'put': return line(event.sequence, 'play', actor, [cardPart(card), { text: '登场' }], [], effectText)
    case 'promotion': return line(event.sequence, 'play', actor, [cardPart(card), { text: '晋升登场' }], [], effectText)
    case 'draw': {
      const amount = numberAfter(event.text, /抽取\s*(\d+)\s*张/, countFrom(event.text))
      return line(event.sequence, 'draw', actor, [{ text: '抽取' }], [{ value: `${amount}张`, tone: 'info' }])
    }
    case 'move': {
      const amount = numberAfter(event.text, /位移\s*(\d+)\s*格/, countFrom(event.text))
      return line(event.sequence, 'info', actor, [cardPart(card), { text: '位移' }], [{ value: `${amount}格`, tone: 'info' }])
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
    case 'grave':
    case 'leave': return line(event.sequence, 'info', actor, [cardPart(card), { text: event.type === 'grave' ? '进入墓地' : '离场' }])
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
      return line(event.sequence, 'effect', actor, card ? [cardPart(card), { text: '：士气变化' }] : [{ text: '士气变化' }], [badge(amount, '士气')])
    }
    case 'runes': {
      const amount = numberAfter(event.text, /符文[^-+\d]*([+-]?\d+)/, countFrom(event.text))
      return line(event.sequence, 'effect', actor, card ? [cardPart(card), { text: '：符文变化' }] : [{ text: '符文变化' }], [badge(amount, '符文')])
    }
    case 'dice': {
      const amount = numberAfter(event.text, /(?:掷骰|点数)[^\d]*(\d+)/, countFrom(event.text))
      return line(event.sequence, 'dice', actor, card ? [cardPart(card), { text: '掷骰' }] : [{ text: '掷骰' }], [{ value: `${amount}点`, tone: 'info' }])
    }
    case 'effect':
    case 'faction-effect': {
      const parts = card ? [cardPart(card), { text: event.type === 'faction-effect' ? '发动阵营效果' : '发动效果' }] : [{ text: event.type === 'faction-effect' ? '发动阵营效果' : '发动效果' }]
      const badges = [...costs]
      const troop = event.text.match(/兵力[^-+\d]*([+-]?\d+)/)?.[1]
      const movement = event.text.match(/位移\s*(\d+)\s*格/)?.[1]
      const draw = event.text.match(/抽取\s*(\d+)\s*张/)?.[1]
      if (troop && Number(troop)) badges.unshift(badge(Number(troop), '兵力'))
      if (movement) badges.unshift({ value: `${movement}格`, tone: 'info' })
      if (draw) badges.unshift({ value: `${draw}张`, tone: 'info' })
      return line(event.sequence, 'effect', actor, parts, badges, effectText)
    }
    case 'defense': return line(event.sequence, 'defense', actor, card ? [cardPart(card), { text: '进行抵挡' }] : [{ text: '抵挡' }])
    case 'support': return line(event.sequence, 'support', actor, card ? [cardPart(card), { text: '进行支援' }] : [{ text: '支援' }])
    case 'initiative-choice': return line(event.sequence, 'info', actor, [{ text: `选择${/后手/.test(event.text) ? '后手' : '先手'}` }])
    case 'mulligan': return line(event.sequence, 'info', actor, [{ text: '调度手牌' }], [{ value: `${countFrom(event.text)}张`, tone: 'info' }])
    case 'disaster':
    case 'disaster-active': return line(event.sequence, 'disaster', null, [event.type === 'disaster' ? { text: '本局天灾：' } : { text: '天灾效果：' }, cardPart(card)])
    case 'game-over':
    case 'special-victory': return line(event.sequence, 'game-over', null, [{ text: event.playerIndex === you ? '我方胜利' : '对方胜利' }])
    default: return null
  }
}

function projectCombat(events: ActionEvent[], start: number, you: number) {
  const attack = events[start]
  const cards = publicCards(attack)
  const attacker = cards[0]
  if (!attacker) return null
  const defenderIsMaster = cards.length < 2
  const defender: Card | '主宰' = defenderIsMaster ? '主宰' : cards[1]
  const versus = attack.text.match(/】?\s*(\d+)\s*(?:vs|VS|对)/)
  const defend = attack.text.match(/(?:vs|VS|对)[^\d]*(\d+)/)
  const attackTroops = versus ? Number(versus[1]) : attacker.troops ?? attacker.baseTroops ?? 0
  const defendTroops = defenderIsMaster ? undefined : defend ? Number(defend[1]) : (defender as Card).troops ?? (defender as Card).baseTroops ?? 0
  const detail: LogLineRow[] = []
  const consumed = new Set<number>([start])
  let damage: number | undefined
  let defended = false
  let defeated = false
  for (let index = start + 1; index < events.length; index++) {
    const event = events[index]
    if (event.type === 'attack' || event.type === 'turn-start' || event.type === 'game-over') break
    if (!['defense', 'support', 'damage', 'leave', 'grave'].includes(event.type)) continue
    if (event.type === 'defense' || event.type === 'support') defended = true
    if (event.type === 'leave' || event.type === 'grave') defeated = true
    if (event.type === 'damage') damage = Math.abs(numberAfter(event.text, /(?:受到|失去|伤害)\s*(\d+)\s*点/, countFrom(event.text)))
    const row = projectLine(event, you)
    if (row) detail.push(row)
    consumed.add(index)
  }
  const result: LogCombatRow['result'] = defenderIsMaster && damage ? '造成伤害' : defeated ? '击破' : defended ? '被抵挡' : '未击破'
  return { row: { kind: 'combat', sequence: attack.sequence, attacker, defender, attackTroops, defendTroops, result, damage, detail } satisfies LogCombatRow, consumed }
}

export function projectLog(events: ActionEvent[], you: number, _names: string[]): LogRow[] {
  const ordered = [...events].sort((a, b) => a.sequence - b.sequence)
  const consumed = new Set<number>()
  const rows: LogRow[] = []
  for (let index = 0; index < ordered.length; index++) {
    if (consumed.has(index)) continue
    const event = ordered[index]
    if (event.type === 'turn-start') {
      rows.push({ kind: 'turn', sequence: event.sequence, round: numberAfter(event.text, /第\s*(\d+)\s*回合/, 0), side: side(event.playerIndex, you) ?? '对方' })
      continue
    }
    if (event.type === 'attack') {
      const combat = projectCombat(ordered, index, you)
      if (combat) {
        combat.consumed.forEach(item => consumed.add(item))
        rows.push(combat.row)
      }
      continue
    }
    if (PLAYER_LOG_HIDDEN_TYPES.has(event.type)) continue
    const row = projectLine(event, you, event.type === 'effect' || event.type === 'faction-effect' ? costBadges(ordered, index, event) : [])
    if (row) rows.push(row)
  }
  return rows
}

export function playerLogContainsForbiddenTerms(rows: LogRow[]) {
  const text = JSON.stringify(rows.map(row => {
    if (row.kind === 'turn') return `${row.round}${row.side}`
    if (row.kind === 'combat') return [row.result, ...row.detail.flatMap(item => item.parts.map(part => part.text))].join('')
    return [...row.parts.map(part => part.text), ...row.badges.map(item => item.value), row.effectText ?? ''].join('')
  }))
  return PLAYER_LOG_REDLINE_TERMS.filter(term => text.includes(term))
}
