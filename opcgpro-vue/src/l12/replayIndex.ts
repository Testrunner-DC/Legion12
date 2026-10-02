import type { MatchDetail } from './replayModel'

export type ReplayIndexCategory = 'action' | 'cost' | 'effect' | 'disaster' | 'result'
export interface ReplayRoundIndex { id: string; step: number; round: number; activePlayer: number }
export interface ReplayEventIndex { id: string; step: number; round?: number; category: ReplayIndexCategory; label: string }
export interface ReplayIndex { rounds: ReplayRoundIndex[]; events: ReplayEventIndex[] }
type Raw = Record<string, any>
const read = (raw: Raw, pascal: string, camel: string) => raw[pascal] ?? raw[camel]
const eventLabels: Record<string, [ReplayIndexCategory, string]> = {
  play: ['action', '打出卡牌'], attack: ['action', '进攻'], move: ['action', '移动'],
  enter: ['action', '登场'], promotion: ['action', '晋升'], 'counter-set': ['action', '盖放战术'],
  damage: ['action', '造成伤害'], discard: ['action', '弃牌'], mill: ['action', '牌库弃牌'],
  return: ['action', '回牌'], search: ['action', '检索'], cost: ['cost', '支付费用'],
  'effect-activation': ['effect', '发动效果'], 'effect-result': ['effect', '效果处理'],
  effect: ['effect', '效果处理'], 'faction-effect': ['effect', '阵营效果'],
  'effect-negated': ['effect', '效果被无效'], 'effect-failed': ['effect', '效果未能完成'],
  'effect-skipped': ['effect', '效果未能完成'], 'attack-aborted': ['effect', '进攻未能完成'],
  'disaster-reveal': ['disaster', '揭示天灾'], 'disaster-active': ['disaster', '天灾生效'],
  disaster: ['disaster', '天灾效果'], 'game-over': ['result', '对局结束'],
  'game-invalid': ['result', '对局无效'], 'game-draw': ['result', '平局'],
}
const outcomes: Record<string, string> = {
  resolved: '完成', negated: '被无效', failed: '未能完成', skipped: '跳过', declined: '不发动', unavailable: '未能发动',
}

// Longest previous-suffix/current-prefix overlap, linear in the bounded event window.
// Legacy sequence-less snapshots may roll and contain repeated legitimate actions.
function overlap(previous: string[], current: string[]): number {
  if (!current.length || !previous.length) return 0
  const combined: (string | null)[] = [...current, null, ...previous]
  const prefix = new Array<number>(combined.length).fill(0)
  for (let i = 1; i < combined.length; i++) {
    let j = prefix[i - 1]!
    while (j > 0 && combined[i] !== combined[j]) j = prefix[j - 1]!
    if (combined[i] === combined[j]) j++
    prefix[i] = j
  }
  return Math.min(current.length, prefix.at(-1) ?? 0)
}

export function buildReplayIndex(detail: MatchDetail, privileged = false): ReplayIndex {
  const index: ReplayIndex = { rounds: [], events: [] }
  const seenSequences = new Set<number>()
  let previousKeys: string[] = []
  let previousTurn = ''
  detail.commands.forEach((command, step) => {
    const raw = command.state
    if (!raw || typeof raw !== 'object') return
    const round = read(raw, 'Round', 'round')
    const activePlayer = read(raw, 'ActivePlayer', 'activePlayer')
    const validTurn = Number.isSafeInteger(round) && round > 0 && (activePlayer === 0 || activePlayer === 1)
    const phase = read(raw, 'Phase', 'phase')
    if (validTurn && !['Initiative', 'DisasterPreparation', 'Mulligan', 'GameOver', 0, 1, 2, 10].includes(phase)) {
      const turn = `${round}:${activePlayer}`
      if (turn !== previousTurn) index.rounds.push({ id: `round-${step}`, step, round, activePlayer })
      previousTurn = turn
    }
    const events: Raw[] = Array.isArray(read(raw, 'Events', 'events'))
      ? read(raw, 'Events', 'events').filter((event: unknown) => event && typeof event === 'object') : []
    // Fingerprints are only for overlap. Raw text never becomes an index label.
    const keys = events.map(event => JSON.stringify(event))
    const shared = overlap(previousKeys, keys)
    events.forEach((event, position) => {
      const sequence = read(event, 'Sequence', 'sequence')
      const sequenced = Number.isSafeInteger(sequence) && sequence > 0
      if (sequenced) {
        if (seenSequences.has(sequence)) return
        seenSequences.add(sequence)
      } else if (position < shared) return
      const privateTo = read(event, 'PrivateTo', 'privateTo')
      // Privileged archive display does not make private prompts/events public index material.
      if (privateTo != null && privateTo !== detail.viewerPlayerIndex) return
      const type = read(event, 'Type', 'type')
      const descriptor = Object.hasOwn(eventLabels, type) ? eventLabels[type] : undefined
      if (!descriptor) return
      const [category, action] = descriptor
      const cards = read(event, 'Cards', 'cards')
      const names: string[] = Array.isArray(cards) ? cards.flatMap((card: Raw | null) => {
        if (!card || typeof card !== 'object') return []
        const name = read(card, 'Name', 'name')
        const id = read(card, 'CardId', 'cardId')
        const hidden = read(card, 'Hidden', 'hidden')
        return typeof name === 'string' && name.trim() && id !== 'hidden-card'
          && (!hidden || privileged) ? [name.trim()] : []
      }) : []
      // Only this frame's event cards identify a source: never a command, current/future
      // board, catalog, audit text or arbitrary semantic sourceName.
      const source = [...new Set(names)].slice(0, 2).map(name => `〈${name}〉`).join('、')
      const status = read(event, 'EffectResultStatus', 'effectResultStatus')
      const outcome = Object.hasOwn(outcomes, status) ? outcomes[status] : undefined
      index.events.push({ id: `event-${step}-${position}`, step,
        round: validTurn ? round : undefined, category,
        label: [action, source, outcome].filter(Boolean).join(' · '),
      })
    })
    previousKeys = keys
  })
  return index
}
