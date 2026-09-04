import type { ActionEvent, Card, GameState, Phase, PlayerView } from './types'

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
    currentCost: value(raw, 'CurrentCost', 'currentCost', value(raw, 'Cost', 'cost', 0)),
    baseTroops: value(raw, 'BaseTroops', 'baseTroops', 0), troops: value(raw, 'Troops', 'troops', 0),
    disasterLevel: value(raw, 'DisasterLevel', 'disasterLevel', 0), trialValue: value(raw, 'TrialValue', 'trialValue', 0),
    attachedCards: value<any[]>(raw, 'AttachedCards', 'attachedCards', []).map(replayCard).filter(Boolean) as Card[],
    tapped: value(raw, 'Tapped', 'tapped', false), hidden: value(raw, 'Hidden', 'hidden', false),
    identityKnown: value(raw, 'IdentityKnown', 'identityKnown', false), summonRound: value(raw, 'SummonRound', 'summonRound', 0),
    hasCharge: value(raw, 'HasCharge', 'hasCharge', false), hasStrongAttack: value(raw, 'HasStrongAttack', 'hasStrongAttack', false),
    hasSureHit: value(raw, 'HasSureHit', 'hasSureHit', false), cannotAttack: value(raw, 'CannotAttack', 'cannotAttack', false),
    cannotSupport: value(raw, 'CannotSupport', 'cannotSupport', false), immortalUses: value(raw, 'ImmortalUses', 'immortalUses', 0),
  }
}

function replayPlayer(raw: any): PlayerView {
  const zones = value<any>(raw, 'SpecialZones', 'specialZones', {})
  const fieldRaw = value<any[][]>(raw, 'Field', 'field', [[], []])
  return {
    playerIndex: value(raw, 'PlayerIndex', 'playerIndex', 0), name: value(raw, 'Name', 'name', '玩家'),
    deckName: value(raw, 'DeckName', 'deckName', ''), faction: value(raw, 'Faction', 'faction', ''),
    master: {
      masterId: value(raw, 'MasterId', 'masterId', ''), masterName: value(raw, 'MasterName', 'masterName', '主宰'),
      masterImageUrl: value(raw, 'MasterImageUrl', 'masterImageUrl', undefined), hp: value(raw, 'Hp', 'hp', 0),
      maxHp: value(raw, 'MaxHp', 'maxHp', 0), tapped: value(raw, 'MasterTapped', 'masterTapped', false),
    },
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

export function replayGameAt(detail: MatchDetail, step: number): GameState | null {
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
    players: value<any[]>(raw, 'Players', 'players', []).map(replayPlayer),
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
