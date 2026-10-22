import type { MatchDetail, RecordedCommand } from './replayModel'

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

type Claim = Record<string, string | number | boolean>
type SequenceClaim = {
  identity: Claim; words: Claim; cards: Claim[][]; cardKeys: Set<string>
  facts: Map<string, { value: Claim; usable: boolean; conflict: boolean }>
  conflict: boolean; wordConflict: boolean; fingerprint: string
  item?: ReplayEventIndex; position: number; visible: boolean
}
const present = (value: unknown): value is string | number | boolean =>
  typeof value === 'string' ? value.length > 0 : typeof value === 'number' || typeof value === 'boolean'
function knownFields(raw: Raw, keys: string[]): Claim {
  return Object.fromEntries(keys.flatMap(key => {
    const value = read(raw, key[0]!.toUpperCase() + key.slice(1), key)
    return present(value) ? [[key, value]] : []
  }))
}
// Missing legacy fields are not competing claims. Only overlapping known facts
// can contradict one another; arbitrary extension fields never identify an event.
function compatible(left: Claim, right: Claim): boolean {
  return Object.keys(right).every(key => left[key] == null || left[key] === right[key])
}
function mergeClaim(left: Claim, right: Claim): boolean {
  const agrees = compatible(left, right)
  for (const key of Object.keys(right)) if (left[key] == null) left[key] = right[key]!
  return agrees
}
function sourceClaims(event: Raw, privileged: boolean): Claim[] {
  const cards = read(event, 'Cards', 'cards')
  return Array.isArray(cards) ? cards.flatMap((card: Raw | null) => {
    if (!card || typeof card !== 'object') return []
    // An opaque card is a wildcard, not a contradictory shorter card list.
    // Do not retain its hidden identity unless this archive viewer may show it.
    if (read(card, 'CardId', 'cardId') === 'hidden-card' || read(card, 'Hidden', 'hidden') && !privileged) return [{}]
    return [knownFields(card, ['instanceId', 'cardId', 'name'])]
  }) : []
}
function compatibleCards(left: Claim[], right: Claim[]): boolean {
  if (left.length !== right.length) return false
  // This only compares recorded identities, not card rules or current state.
  // Ignore a field absent from every card on one side, and count equal known
  // claims: thousands of duplicate cards must not become thousands of nodes.
  const fields = ['instanceId', 'cardId', 'name'].filter(key => left.some(card => card[key] != null) && right.some(card => card[key] != null))
  function grouped(cards: Claim[]) {
    const groups = new Map<string, { value: Claim; count: number }>()
    for (const card of cards) {
      const value = Object.fromEntries(fields.flatMap(key => card[key] == null ? [] : [[key, card[key]]]))
      const key = JSON.stringify(value)
      const previous = groups.get(key)
      if (previous) previous.count++
      else groups.set(key, { value, count: 1 })
    }
    return groups
  }
  const leftGroups = grouped(left), rightGroups = grouped(right)
  if ([...leftGroups.values(), ...rightGroups.values()].every(group => Object.keys(group.value).length === fields.length)) {
    return leftGroups.size === rightGroups.size && [...leftGroups].every(([key, group]) => rightGroups.get(key)?.count === group.count)
  }
  const sources = [...leftGroups.values()], targets = [...rightGroups.values()]
  const available = targets.map(target => target.count)
  const owners = targets.map(() => new Map<number, number>())
  for (let source = 0; source < sources.length; source++) {
    let remaining = sources[source]!.count
    while (remaining > 0) {
      // Breadth-first residual matching, with capacities for identical cards.
      // Missing fields remain wildcards; no greedy pairing or recursion.
      const pending = [source], reached = new Set([source])
      const fromTarget = new Map<number, number>(), fromSource = new Map<number, number>()
      let freeTarget = -1
      for (let cursor = 0; cursor < pending.length && freeTarget < 0; cursor++) {
        const current = pending[cursor]!
        for (let target = 0; target < targets.length; target++) {
          if (fromTarget.has(target) || !compatible(sources[current]!.value, targets[target]!.value)) continue
          fromTarget.set(target, current)
          if (available[target]! > 0) { freeTarget = target; break }
          for (const owner of owners[target]!.keys()) if (!reached.has(owner)) {
            reached.add(owner); fromSource.set(owner, target); pending.push(owner)
          }
        }
      }
      if (freeTarget < 0) return false
      let amount = Math.min(remaining, available[freeTarget]!)
      const path: { source: number; target: number; previous?: number }[] = []
      for (let target = freeTarget; ;) {
        const owner = fromTarget.get(target)!, previous = fromSource.get(owner)
        path.push({ source: owner, target, previous })
        if (previous == null) break
        amount = Math.min(amount, owners[previous]!.get(owner)!)
        target = previous
      }
      for (const edge of path) {
        owners[edge.target]!.set(edge.source, (owners[edge.target]!.get(edge.source) ?? 0) + amount)
        if (edge.previous == null) continue
        const count = owners[edge.previous]!.get(edge.source)! - amount
        if (count) owners[edge.previous]!.set(edge.source, count)
        else owners[edge.previous]!.delete(edge.source)
      }
      available[freeTarget]! -= amount
      remaining -= amount
    }
  }
  return true
}
function addFact(claim: SequenceClaim, key: string, value: Claim, usable: boolean) {
  const previous = claim.facts.get(key)
  if (previous) {
    previous.conflict = !mergeClaim(previous.value, value) || previous.conflict
    previous.usable ||= usable
  } else claim.facts.set(key, { value, usable, conflict: false })
}
function updateClaim(claim: SequenceClaim, event: Raw, privileged: boolean) {
  if (claim.conflict) return
  const identity = knownFields(event, ['type', 'playerIndex'])
  const status = read(event, 'EffectResultStatus', 'effectResultStatus')
  if (Object.hasOwn(outcomes, status)) identity.effectResultStatus = status
  claim.conflict = !mergeClaim(claim.identity, identity) || claim.conflict
  if (claim.conflict) return
  claim.wordConflict = !mergeClaim(claim.words, knownFields(event, ['text', 'effectText'])) || claim.wordConflict
  const cards = sourceClaims(event, privileged)
  const cardKey = JSON.stringify(cards)
  if (cards.length && !claim.cardKeys.has(cardKey)) {
    claim.conflict = claim.cards.some(previous => !compatibleCards(previous, cards)) || claim.conflict
    if (claim.conflict) return
    claim.cards.push(cards)
    claim.cardKeys.add(cardKey)
  }
  // These are the usable structured receipts for indexed actions. They may
  // settle old/new wording, but never settle different actors/types/results or
  // borrow a later card name for the original index frame.
  for (const [key, fields, required] of [
    ['playerLogSemantic', ['actionLabel', 'outcomeLabel', 'sourceInstanceId', 'sourceName', 'targetInstanceId', 'targetName'], ['actionLabel', 'outcomeLabel']],
    ['playerCombat', ['combatId', 'eventKind', 'outcomeCode', 'publicReasonCode', 'attackerInstanceId', 'targetInstanceId', 'attackerTroops', 'defenderTroops', 'masterDamage'], ['combatId', 'eventKind']],
  ] as const) {
    const raw = read(event, key[0]!.toUpperCase() + key.slice(1), key)
    if (!raw || typeof raw !== 'object') continue
    const value = knownFields(raw, [...fields])
    const usable = required.every(field => typeof value[field] === 'string' && value[field])
    addFact(claim, key, value, usable)
  }
  // Movement is the other structured receipt usable by an indexed action.
  // The remaining log facts require non-indexed event types (put, troops-
  // modifier, target-selected, disaster-value), so cannot settle these labels.
  const movement = read(event, 'PlayerBattlefieldMovement', 'playerBattlefieldMovement')
  const rawFacts = movement && typeof movement === 'object' ? read(movement, 'Facts', 'facts') : undefined
  if ((identity.type === 'move' || identity.type === 'faction-effect') && Array.isArray(rawFacts)) {
    const rawCards = read(event, 'Cards', 'cards')
    const publicCards: Raw[] = Array.isArray(rawCards) ? rawCards.filter((card: Raw | null) => card && typeof card === 'object'
      && !read(card, 'Hidden', 'hidden') && read(card, 'CardId', 'cardId') !== 'hidden-card'
      && Boolean(read(card, 'Name', 'name') ?? '未知卡牌')) : []
    const facts = rawFacts.flatMap((raw: Raw | null) => {
      if (!raw || typeof raw !== 'object') return []
      const value = knownFields(raw, ['instanceId', 'battlefieldPlayerIndex', 'fromRow', 'fromSlot', 'toRow', 'toSlot'])
      if (typeof value.instanceId !== 'string' || !publicCards.some(card => read(card, 'InstanceId', 'instanceId') === value.instanceId)
        || ![0, 1].includes(value.battlefieldPlayerIndex as number)
        || ![0, 1].includes(value.fromRow as number) || ![0, 1].includes(value.toRow as number)
        || ![0, 1, 2].includes(value.fromSlot as number) || ![0, 1, 2].includes(value.toSlot as number)
        || value.fromRow === value.toRow && value.fromSlot === value.toSlot) return []
      return [value]
    }).sort((left, right) => JSON.stringify(left).localeCompare(JSON.stringify(right)))
    if (facts.length) addFact(claim, 'playerBattlefieldMovement', { facts: JSON.stringify(facts) }, true)
  }
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

export function createReplayIndexAccumulator(viewerPlayerIndex?: number | null, privileged = false) {
  const index: ReplayIndex = { rounds: [], events: [] }
  const seenMatches = new Map<string, Map<number, SequenceClaim>>()
  let previousKeys: string[] = []
  let previousTurn = ''
  let previousMatch = ''
  let nextStep = 0
  function reconcile(claim: SequenceClaim) {
    const blocked = claim.conflict || claim.wordConflict
      && ![...claim.facts.values()].some(fact => fact.usable && !fact.conflict)
    if (!claim.item || claim.visible === !blocked) return
    if (blocked) index.events.splice(index.events.indexOf(claim.item), 1)
    else {
      const before = index.events.findIndex(item => item.step > claim.item!.step
        || item.step === claim.item!.step && Number(item.id.slice(item.id.lastIndexOf('-') + 1)) > claim.position)
      index.events.splice(before < 0 ? index.events.length : before, 0, claim.item)
    }
    claim.visible = !blocked
  }
  function append(commands: RecordedCommand[], start: number) {
  if (start !== nextStep) throw new Error('回放索引页顺序不一致')
  commands.forEach((command, offset) => {
    const step = start + offset
    const raw = command.state
    if (!raw || typeof raw !== 'object') return
    const rawMatch = read(raw, 'MatchId', 'matchId')
    const match = typeof rawMatch === 'string' && rawMatch ? rawMatch : previousMatch
    // A newly supplied identity completes legacy frames; only two known,
    // different match IDs establish a new scope. Omitted IDs inherit the scope.
    if (!previousMatch && match && seenMatches.has('')) {
      seenMatches.set(match, seenMatches.get('')!)
      seenMatches.delete('')
    } else if (previousMatch && match !== previousMatch) { previousKeys = []; previousTurn = '' }
    previousMatch = match
    let seenSequences = seenMatches.get(match)
    if (!seenSequences) { seenSequences = new Map(); seenMatches.set(match, seenSequences) }
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
    // Fingerprints skip byte-identical copies and find legacy overlap; they are
    // not authority identities. Raw text never becomes an index label.
    const keys = events.map(event => JSON.stringify(event))
    const shared = overlap(previousKeys, keys)
    events.forEach((event, position) => {
      const sequence = read(event, 'Sequence', 'sequence')
      const sequenced = Number.isSafeInteger(sequence) && sequence > 0
      let claim: SequenceClaim | undefined
      if (sequenced) {
        const previous = seenSequences!.get(sequence)
        if (previous) {
          if (previous.fingerprint !== keys[position]) {
            updateClaim(previous, event, privileged)
            previous.fingerprint = keys[position]!
            reconcile(previous)
          }
          return
        }
        claim = { identity: {}, words: {}, cards: [], cardKeys: new Set(), facts: new Map(),
          conflict: false, wordConflict: false, fingerprint: keys[position]!, position, visible: false }
        updateClaim(claim, event, privileged)
        seenSequences!.set(sequence, claim)
      } else if (position < shared) return
      const privateTo = read(event, 'PrivateTo', 'privateTo')
      // Privileged archive display does not make private prompts/events public index material.
      if (privateTo != null && privateTo !== viewerPlayerIndex) return
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
      const item = { id: `event-${step}-${position}`, step,
        round: validTurn ? round : undefined, category,
        label: [action, source, outcome].filter(Boolean).join(' · '),
      }
      index.events.push(item)
      if (claim) { claim.item = item; claim.visible = true }
    })
    previousKeys = keys
  })
  nextStep += commands.length
  }
  return { index, append }
}

export function buildReplayIndex(detail: MatchDetail, privileged = false): ReplayIndex {
  const builder = createReplayIndexAccumulator(detail.viewerPlayerIndex, privileged)
  builder.append(detail.commands, 0)
  return builder.index
}
