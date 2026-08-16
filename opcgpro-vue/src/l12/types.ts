export type Phase = 'Initiative' | 'DisasterPreparation' | 'Mulligan' | 'Disaster' | 'Reset' | 'Draw' | 'Morale' | 'Main' | 'End' | 'Defense' | 'GameOver'

export interface ActionEvent {
  sequence: number
  type: string
  playerIndex?: number
  text: string
  cards?: Card[]
}

export interface Card {
  instanceId: string
  cardId: string
  name: string
  cardType: string
  faction: string
  imageUrl?: string
  effectText?: string
  cost: number
  baseTroops: number
  troops: number
  disasterLevel: number
  trialValue?: number
  hasCharge?: boolean
  hasStrongAttack?: boolean
  hasSureHit?: boolean
  hasRangeBonus?: boolean
  hasRangedNoLoss?: boolean
  cannotBeRanged?: boolean
  hidden?: boolean
  currentCost?: number
  tapped: boolean
  summonRound: number
  cannotAttack?: boolean
  cannotSupport?: boolean
  canAttackBackAndMasterUntilTurn?: number
  immortalUses?: number
  immortalUntilTurn?: number
  suppressDeathUntilTurn?: number
  timedModifiers?: Array<{ troopsDelta: number; costDelta: number; expiresAfterTurn: number; source: string }>
  abilities?: Array<{ id: string; label: string }>
  attachedCards?: Card[]
  trialProgress?: number
  trialCompleted?: boolean
  nextAttackNoLossUses?: number
}

export interface DisasterCardView extends Partial<Card> {
  instanceId: string
  hidden?: boolean
  ownerIndex?: number
}

export interface PlayerView {
  playerIndex: number
  name: string
  deckName: string
  faction: string
  factionEffect?: { cardId: string; name: string; imageUrl?: string; effectText: string; abilities?: Array<{ id: string; label: string }> }
  master: { masterId: string; masterName: string; masterImageUrl?: string; effectText?: string; tapped?: boolean; hp: number; maxHp: number; abilities?: Array<{ id: string; label: string }> }
  libraryCount: number
  hand?: Card[]
  handCount?: number
  moraleDeck?: Array<{ instanceId: string; cardId: string; tapped: boolean }>
  moraleDeckCount?: number
  morale: Array<{ instanceId: string; cardId: string; tapped: boolean }>
  field: Array<Array<Card | null>>
  relic?: Card | null
  extraRelics?: Card[]
  graveyard?: Card[]
  graveyardCount?: number
  resolving?: Card[]
  specialZones?: { runes: number; trialLevel: number; godPower: Card[]; trials: Card[] }
  temporaryMorale?: number
  nextLegionChargeMaxCost?: number | null
  mulliganDone: boolean
}

export interface GameState {
  matchId: string
  roomCode: string
  you: number
  revision: number
  activePlayer: number
  firstPlayer: number
  diceWinner: number
  initiativeRolls: number[]
  phase: Phase
  round: number
  disasterValue: number
  activeDisaster?: Card | null
  disasterDeck?: Array<{ hidden: boolean }>
  bannedDisasters?: Card[]
  removedDisasters?: Card[]
  revealedDisasters?: Card[]
  chosenDisasters?: DisasterCardView[]
  sessionDisasters?: DisasterCardView[]
  disasterPreparationStep?: number
  waitingPrompt?: { playerIndex: number; playerName: string; kind: string } | null
  prompts?: Prompt[]
  effectStack?: StackItem[]
  pendingDefense?: { attackerPlayer: number; attackerInstanceId: string; target: { type: string; instanceId?: string } } | null
  winner?: number | null
  players: PlayerView[]
  lastAction?: ActionEvent | null
  recentEvents?: ActionEvent[]
  legalAttackTargets?: Record<string, string[]>
  stateHash: string
}

export interface Prompt {
  promptId: string
  playerIndex: number
  kind: string
  text: string
  validChoices: string[]
  minChoose: number
  maxChoose: number
  data: Record<string, string>
}

export interface StackItem {
  stackItemId: string
  controller: number
  sourceInstanceId: string
  sourceCardId: string
  sourceName: string
  trigger: string
  text: string
  negated: boolean
  targets: string[]
}

export interface RoomState {
  roomCode: string
  yourPlayerIndex: number
  players: Array<{ name: string; playerIndex: number; connected: boolean; ready: boolean; deckIndex: number; customDeck?: boolean; deckName: string; masterName: string; faction: string }>
  decks: Array<{ index: number; name: string; masterId: string; masterName: string; faction: string }>
  started: boolean
}
