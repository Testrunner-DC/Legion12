import type { ActionEvent, Card, GameState, Phase, PlayerView } from './types'

export interface ReplayCardDefinition {
  id: string
  nameZh: string
  cardType: string
  faction: string
  imageUrl?: string
  effect?: string
}

export interface MatchSummary {
  matchId: string
  roomCode: string
  player0: string
  player1: string
  deck0: string
  deck1: string
  startedUtc: string
  endedUtc?: string | null
  winner?: number | null
  finalHash?: string | null
  error?: string | null
  commandCount: number
}

export interface RecordedCommand {
  sequence: number
  receivedUtc: string
  playerIndex: number
  command: Record<string, unknown>
  accepted: boolean
  error?: string | null
  revision: number
  stateHash: string
  state: Record<string, any>
}

export interface MatchDetail {
  match: MatchSummary
  commands: RecordedCommand[]
  viewerPlayerIndex?: number | null
}

export interface AdminReplaySource {
  summary: {
    matchId: string
    status: string
    players: Array<{ displayName: string; deckName?: string; result?: string }>
    startedUtc: string
    endedUtc?: string | null
    commandCount: number
    error?: string | null
  }
  replay: RecordedCommand[]
}

let importedReplay: MatchDetail | null = null

export function rememberImportedReplay(detail: MatchDetail) { importedReplay = detail }
export function consumeImportedReplay() { return importedReplay }

export function parseReplayPayload(raw: unknown): MatchDetail {
  const candidate = (raw as any)?.format === 'legion12-replay' ? (raw as any).detail : raw
  if (!candidate?.match?.matchId || !Array.isArray(candidate.commands))
    throw new Error('文件不是有效的十二军团回放')
  if (candidate.commands.some((command: any) => !command || typeof command.state !== 'object'))
    throw new Error('回放缺少可播放的对局状态')
  return candidate as MatchDetail
}

export function exportReplayPayload(detail: MatchDetail) {
  return { format: 'legion12-replay', version: 1, exportedAt: new Date().toISOString(), detail }
}

export function adminReplayDetail(source: AdminReplaySource): MatchDetail {
  if (source.summary.status !== 'completed') throw new Error('进行中或无效对局不能播放回放')
  if (!Array.isArray(source.replay)) throw new Error('后台对局缺少有效的回放命令')
  const firstState = source.replay[0]?.state
  const winner = source.summary.players.findIndex(player => player.result === 'win')
  return {
    match: {
      matchId: source.summary.matchId,
      roomCode: value(firstState, 'RoomCode', 'roomCode', ''),
      player0: source.summary.players[0]?.displayName || '玩家1',
      player1: source.summary.players[1]?.displayName || '玩家2',
      deck0: source.summary.players[0]?.deckName || '',
      deck1: source.summary.players[1]?.deckName || '',
      startedUtc: source.summary.startedUtc,
      endedUtc: source.summary.endedUtc,
      winner: winner >= 0 ? winner : null,
      finalHash: source.replay.at(-1)?.stateHash || null,
      error: source.summary.error,
      commandCount: source.replay.length,
    },
    commands: source.replay,
    viewerPlayerIndex: 0,
  }
}

function value<T>(raw: any, pascal: string, camel: string, fallback: T): T {
  return (raw?.[pascal] ?? raw?.[camel] ?? fallback) as T
}

function replayCard(raw: any): Card | null {
  if (!raw) return null
  const traits = value<string[]>(raw, 'Traits', 'traits', [])
  const replayCardType = value<string>(raw, 'CardType', 'cardType', '')
  const derivedSpecial = replayCardType === 'token' || traits.some(trait => trait.endsWith('专属'))
  return {
    instanceId: value(raw, 'InstanceId', 'instanceId', ''), cardId: value(raw, 'CardId', 'cardId', ''),
    name: value(raw, 'Name', 'name', '未知卡牌'), cardType: replayCardType, faction: value(raw, 'Faction', 'faction', ''),
    traits, profession: value(raw, 'Profession', 'profession', undefined), imageUrl: value(raw, 'ImageUrl', 'imageUrl', undefined),
    effectText: value(raw, 'EffectText', 'effectText', undefined), cost: value(raw, 'Cost', 'cost', 0),
    hasPrintedCost: value(raw, 'HasPrintedCost', 'hasPrintedCost', !derivedSpecial),
    currentCost: Math.max(0, value(raw, 'CurrentCost', 'currentCost', value(raw, 'Cost', 'cost', 0))),
    baseTroops: Math.max(0, value(raw, 'BaseTroops', 'baseTroops', 0)), troops: Math.max(0, value(raw, 'Troops', 'troops', 0)),
    disasterLevel: value(raw, 'DisasterLevel', 'disasterLevel', 0), trialValue: value(raw, 'TrialValue', 'trialValue', 0),
    attachedCards: value<any[]>(raw, 'AttachedCards', 'attachedCards', []).map(replayCard).filter(Boolean) as Card[],
    tapped: value(raw, 'Tapped', 'tapped', false), hidden: value(raw, 'Hidden', 'hidden', false),
    identityKnown: value(raw, 'IdentityKnown', 'identityKnown', false), summonRound: value(raw, 'SummonRound', 'summonRound', 0),
    hasCharge: value(raw, 'HasCharge', 'hasCharge', false), hasStrongAttack: value(raw, 'HasStrongAttack', 'hasStrongAttack', false),
    hasSureHit: value(raw, 'HasSureHit', 'hasSureHit', false), cannotAttack: value(raw, 'CannotAttack', 'cannotAttack', false),
    cannotSupport: value(raw, 'CannotSupport', 'cannotSupport', false), immortalUses: value(raw, 'ImmortalUses', 'immortalUses', 0),
  }
}

function replayAbility(raw: any) {
  return {
    id: value(raw, 'Id', 'id', ''), label: value(raw, 'Label', 'label', ''),
    enabled: value(raw, 'Enabled', 'enabled', undefined), disabledReason: value(raw, 'DisabledReason', 'disabledReason', undefined),
    triggerOnly: value(raw, 'TriggerOnly', 'triggerOnly', undefined),
  }
}

function replayPlayer(raw: any, catalog?: ReadonlyMap<string, ReplayCardDefinition>): PlayerView {
  const zones = value<any>(raw, 'SpecialZones', 'specialZones', {})
  const fieldRaw = value<any[][]>(raw, 'Field', 'field', [[], []])
  const masterId = value(raw, 'MasterId', 'masterId', '')
  const masterDefinition = catalog?.get(masterId)
  const rawMasterAbilities = value<any[]>(raw, 'MasterAbilities', 'masterAbilities', [])
  const factionRaw = value<any>(raw, 'FactionEffect', 'factionEffect', null)
  const factionCardId = value(factionRaw, 'CardId', 'cardId', '')
  const factionDefinition = catalog?.get(factionCardId)
  const rawFactionAbilities = value<any[]>(factionRaw, 'Abilities', 'abilities', [])
  return {
    playerIndex: value(raw, 'PlayerIndex', 'playerIndex', 0), name: value(raw, 'Name', 'name', '玩家'),
    deckName: value(raw, 'DeckName', 'deckName', ''), faction: value(raw, 'Faction', 'faction', ''),
    master: {
      masterId, masterName: value(raw, 'MasterName', 'masterName', masterDefinition?.nameZh ?? '主宰'),
      masterImageUrl: value(raw, 'MasterImageUrl', 'masterImageUrl', masterDefinition?.imageUrl),
      effectText: value(raw, 'MasterEffectText', 'masterEffectText', masterDefinition?.effect),
      abilities: rawMasterAbilities.length ? rawMasterAbilities.map(replayAbility) : undefined,
      hp: Math.max(0, value(raw, 'Hp', 'hp', 0)),
      maxHp: value(raw, 'MaxHp', 'maxHp', 0), tapped: value(raw, 'MasterTapped', 'masterTapped', false),
    },
    factionEffect: factionRaw ? {
      cardId: factionCardId,
      name: value(factionRaw, 'Name', 'name', factionDefinition?.nameZh ?? '阵营效果'),
      imageUrl: value(factionRaw, 'ImageUrl', 'imageUrl', factionDefinition?.imageUrl),
      effectText: value(factionRaw, 'EffectText', 'effectText', factionDefinition?.effect ?? ''),
      abilities: rawFactionAbilities.length ? rawFactionAbilities.map(replayAbility) : undefined,
    } : undefined,
    libraryCount: value<any[]>(raw, 'Library', 'library', []).length,
    hand: value<any[]>(raw, 'Hand', 'hand', []).map(replayCard).filter(Boolean) as Card[],
    handCount: value<any[]>(raw, 'Hand', 'hand', []).length,
    moraleDeck: value<any[]>(raw, 'MoraleDeck', 'moraleDeck', []).map(card => ({
      instanceId: value(card, 'InstanceId', 'instanceId', ''), cardId: value(card, 'CardId', 'cardId', ''),
      tapped: value(card, 'Tapped', 'tapped', false),
    })),
    morale: value<any[]>(raw, 'Morale', 'morale', []).map(card => ({
      instanceId: value(card, 'InstanceId', 'instanceId', ''), cardId: value(card, 'CardId', 'cardId', ''),
      tapped: value(card, 'Tapped', 'tapped', false),
    })),
    field: [0, 1].map(row => [0, 1, 2].map(slot => replayCard(fieldRaw?.[row]?.[slot]))),
    relic: replayCard(value(raw, 'Relic', 'relic', null)),
    extraRelics: value<any[]>(raw, 'ExtraRelics', 'extraRelics', []).map(replayCard).filter(Boolean) as Card[],
    graveyard: value<any[]>(raw, 'Graveyard', 'graveyard', []).map(replayCard).filter(Boolean) as Card[],
    resolving: value<any[]>(raw, 'Resolving', 'resolving', []).map(replayCard).filter(Boolean) as Card[],
    specialZones: {
      runes: value(zones, 'Runes', 'runes', 0), trialLevel: value(zones, 'TrialLevel', 'trialLevel', 0),
      godPower: value<any[]>(zones, 'GodPower', 'godPower', []).map(replayCard).filter(Boolean) as Card[],
      trials: value<any[]>(zones, 'Trials', 'trials', []).map(replayCard).filter(Boolean) as Card[],
    },
    temporaryMorale: value(raw, 'TemporaryMorale', 'temporaryMorale', 0),
    mulliganDone: value(raw, 'MulliganDone', 'mulliganDone', false),
  }
}

const phaseNames: Phase[] = [
  'Initiative', 'DisasterPreparation', 'Mulligan', 'Disaster', 'Reset', 'Draw',
  'Morale', 'Main', 'End', 'Defense', 'GameOver',
]

export function replayGameAt(detail: MatchDetail, step: number, catalog?: ReadonlyMap<string, ReplayCardDefinition>): GameState | null {
  const command = detail.commands[step]
  const raw = command?.state
  if (!raw) return null
  const rawPhase = value<any>(raw, 'Phase', 'phase', 'Main')
  const defense = value<any>(raw, 'PendingDefense', 'pendingDefense', null)
  const events: ActionEvent[] = value<any[]>(raw, 'Events', 'events', []).map(event => ({
    sequence: value(event, 'Sequence', 'sequence', 0), type: value(event, 'Type', 'type', ''),
    playerIndex: value(event, 'PlayerIndex', 'playerIndex', undefined), text: value(event, 'Text', 'text', ''),
    cards: value<any[]>(event, 'Cards', 'cards', []).map(replayCard).filter(Boolean) as Card[],
  }))
  return {
    matchId: value(raw, 'MatchId', 'matchId', detail.match.matchId),
    roomCode: value(raw, 'RoomCode', 'roomCode', detail.match.roomCode),
    you: detail.viewerPlayerIndex ?? 0, revision: command.revision ?? 0,
    activePlayer: value(raw, 'ActivePlayer', 'activePlayer', 0), firstPlayer: value(raw, 'FirstPlayer', 'firstPlayer', 0),
    diceWinner: value(raw, 'DiceWinner', 'diceWinner', 0), initiativeRolls: value(raw, 'InitiativeRolls', 'initiativeRolls', [0, 0]),
    phase: typeof rawPhase === 'number' ? phaseNames[rawPhase] ?? 'Main' : rawPhase,
    round: value(raw, 'Round', 'round', 1), disasterMode: value(raw, 'DisasterMode', 'disasterMode', 'all'),
    disasterValue: value(raw, 'DisasterValue', 'disasterValue', 0),
    activeDisaster: replayCard(value(raw, 'ActiveDisaster', 'activeDisaster', null)),
    players: value<any[]>(raw, 'Players', 'players', []).map(player => replayPlayer(player, catalog)),
    pendingDefense: defense ? {
      attackerPlayer: value(defense, 'AttackerPlayer', 'attackerPlayer', 0),
      attackerInstanceId: value(defense, 'AttackerInstanceId', 'attackerInstanceId', ''),
      stage: value(defense, 'Stage', 'stage', 'DefenseChoice'),
      attackValue: value(defense, 'AttackValue', 'attackValue', 0),
      target: {
        type: value(value(defense, 'Target', 'target', {}), 'Type', 'type', 'master'),
        instanceId: value(value(defense, 'Target', 'target', {}), 'InstanceId', 'instanceId', undefined),
      },
    } : null,
    winner: value(raw, 'Winner', 'winner', null),
    prompts: [], effectStack: [], waitingPrompt: null,
    recentEvents: events, lastAction: events.at(-1) ?? null,
    legalAttackTargets: {}, stateHash: command.stateHash ?? '',
  }
}

const replaySourceEventTypes = new Set([
  'play', 'attack', 'move', 'reveal', 'disaster-reveal', 'effect-trigger',
  'effect-activation', 'response',
])
const replaySystemEventTypes = new Set([
  'phase', 'phase-detail', 'turn-start', 'turn-end', 'end-turn', 'dice', 'initiative-choice',
  'prompt', 'prompt-resolved', 'priority-pass', 'stack-open', 'stack-push', 'stack-resolve',
])

function publicReplayCards(game: GameState): Card[] {
  return [
    ...(game.activeDisaster ? [game.activeDisaster] : []),
    ...game.players.flatMap(player => [
      ...player.field.flat().filter(Boolean) as Card[], ...(player.graveyard ?? []), ...(player.resolving ?? []),
      ...(player.relic ? [player.relic] : []), ...(player.extraRelics ?? []), ...(player.specialZones?.trials ?? []),
    ]),
  ]
}

function replayDefinitionCard(definition: ReplayCardDefinition, instanceId: string): Card {
  return {
    instanceId, cardId: definition.id, name: definition.nameZh, cardType: definition.cardType,
    faction: definition.faction, imageUrl: definition.imageUrl, effectText: definition.effect,
    cost: 0, baseTroops: 0, troops: 0, disasterLevel: 0, tapped: false, summonRound: 0,
  }
}

function replayMasterSource(game: GameState, playerIndex: number,
  catalog: ReadonlyMap<string, ReplayCardDefinition>): Card | null {
  const player = game.players.find(candidate => candidate.playerIndex === playerIndex)
  if (!player) return null
  const definition = catalog.get(player.master.masterId)
  return {
    instanceId: `master-${player.playerIndex}`, cardId: player.master.masterId,
    name: player.master.masterName || definition?.nameZh || '主宰', cardType: 'master', faction: player.faction,
    imageUrl: player.master.masterImageUrl ?? definition?.imageUrl,
    effectText: player.master.effectText ?? definition?.effect,
    cost: 0, baseTroops: 0, troops: 0, disasterLevel: 0,
    tapped: Boolean(player.master.tapped), summonRound: 0,
  }
}

function replayFactionSource(game: GameState, playerIndex: number,
  catalogDefinitions: readonly ReplayCardDefinition[], catalog: ReadonlyMap<string, ReplayCardDefinition>): Card | null {
  const player = game.players.find(candidate => candidate.playerIndex === playerIndex)
  if (!player) return null
  const factionEffect = player.factionEffect
  const definition = factionEffect?.cardId
    ? catalog.get(factionEffect.cardId)
    : catalogDefinitions.find(card => card.faction === player.faction && card.cardType === 'rune' && card.nameZh.startsWith('士气·'))
  if (!factionEffect && !definition) return null
  return {
    instanceId: `faction-${player.playerIndex}`, cardId: factionEffect?.cardId || definition!.id,
    name: factionEffect?.name || definition!.nameZh, cardType: definition?.cardType || 'rune', faction: player.faction,
    imageUrl: factionEffect?.imageUrl ?? definition?.imageUrl,
    effectText: factionEffect?.effectText || definition?.effect,
    cost: 0, baseTroops: 0, troops: 0, disasterLevel: 0, tapped: false, summonRound: 0,
  }
}

function replayStructuredSource(game: GameState, instanceId: string,
  catalogDefinitions: readonly ReplayCardDefinition[], catalog: ReadonlyMap<string, ReplayCardDefinition>): Card | null {
  const masterMatch = /^master-(\d+)$/.exec(instanceId)
  if (masterMatch) return replayMasterSource(game, Number(masterMatch[1]), catalog)
  const factionMatch = /^faction-(\d+)$/.exec(instanceId)
  if (factionMatch) return replayFactionSource(game, Number(factionMatch[1]), catalogDefinitions, catalog)
  const masterOwner = game.players.find(player => player.master.masterId === instanceId)
  return masterOwner ? replayMasterSource(game, masterOwner.playerIndex, catalog) : null
}

export function replayFocusCardAt(detail: MatchDetail, step: number,
  catalogDefinitions: readonly ReplayCardDefinition[] = []): Card | null {
  const catalog = new Map(catalogDefinitions.map(card => [card.id, card]))
  const game = replayGameAt(detail, step, catalog)
  const recorded = detail.commands[step]
  if (!game || !recorded) return null
  const command = recorded.command ?? {}
  const commandType = String(command.type ?? command.Type ?? '').toLocaleLowerCase()
  const previousRaw = detail.commands[step - 1]?.state
  const promptId = String(command.promptId ?? command.PromptId ?? '')
  const previousPrompt = promptId
    ? value<any[]>(previousRaw, 'Prompts', 'prompts', []).find(prompt => value(prompt, 'PromptId', 'promptId', '') === promptId)
    : null
  const promptSourceInstanceId = previousPrompt ? value(previousPrompt, 'SourceInstanceId', 'sourceInstanceId', '') : ''
  const explicitInstanceId = [promptSourceInstanceId, ...['sourceInstanceId', 'cardInstanceId', 'attackerInstanceId']
    .map(key => command[key] ?? command[key[0].toUpperCase() + key.slice(1)])
    ].find(value => typeof value === 'string' && value) as string | undefined
  if (explicitInstanceId) {
    const previousGame = step > 0 ? replayGameAt(detail, step - 1, catalog) : null
    const structured = replayStructuredSource(game, explicitInstanceId, catalogDefinitions, catalog)
      ?? (previousGame ? replayStructuredSource(previousGame, explicitInstanceId, catalogDefinitions, catalog) : null)
    if (structured) return structured
    const match = [...publicReplayCards(game), ...(previousGame ? publicReplayCards(previousGame) : [])]
      .find(card => card.instanceId === explicitInstanceId)
    if (match) return match
  }
  const explicitCardId = String(command.sourceCardId ?? command.SourceCardId
    ?? (previousPrompt ? value(previousPrompt, 'SourceCardId', 'sourceCardId', '') : ''))
  const explicitDefinition = catalog.get(explicitCardId)
  if (explicitDefinition) return replayDefinitionCard(explicitDefinition, explicitInstanceId || `replay-source-${explicitCardId}`)
  const actor = game.players[recorded.playerIndex]
  if (actor && commandType.includes('master')) {
    const definition = catalog.get(actor.master.masterId)
    return {
      instanceId: `master-${actor.playerIndex}`, cardId: actor.master.masterId, name: actor.master.masterName,
      cardType: 'master', faction: actor.faction, imageUrl: actor.master.masterImageUrl,
      effectText: actor.master.effectText ?? definition?.effect, cost: 0, baseTroops: 0, troops: 0,
      disasterLevel: 0, tapped: Boolean(actor.master.tapped), summonRound: 0,
    }
  }
  if (actor && commandType.includes('faction')) {
    const definition = catalogDefinitions.find(card => card.cardType === 'faction' && card.faction === actor.faction)
    if (definition) return {
      instanceId: `faction-${actor.playerIndex}`, cardId: definition.id, name: definition.nameZh,
      cardType: definition.cardType, faction: definition.faction, imageUrl: definition.imageUrl,
      effectText: definition.effect, cost: 0, baseTroops: 0, troops: 0, disasterLevel: 0, tapped: false, summonRound: 0,
    }
  }
  const previousSequence = step > 0
    ? Math.max(0, ...value<any[]>(detail.commands[step - 1]?.state, 'Events', 'events', []).map(event => value(event, 'Sequence', 'sequence', 0)))
    : 0
  const sourceEvents = [...(game.recentEvents ?? [])]
    .filter(event => event.sequence > previousSequence && !replaySystemEventTypes.has(event.type)
      && replaySourceEventTypes.has(event.type) && event.cards?.length)
  const actorSourceEvents = sourceEvents.filter(event => event.playerIndex === recorded.playerIndex)
  const sourceEvent = (actorSourceEvents.length ? actorSourceEvents : sourceEvents)
    .sort((left, right) => right.sequence - left.sequence)[0]
  return sourceEvent?.cards?.[0] ?? null
}
