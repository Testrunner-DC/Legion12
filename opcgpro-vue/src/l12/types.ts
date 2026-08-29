export type Phase = 'Initiative' | 'DisasterPreparation' | 'Mulligan' | 'Disaster' | 'Reset' | 'Draw' | 'Morale' | 'Main' | 'End' | 'Defense' | 'GameOver'

export interface ActionEvent {
  sequence: number
  type: string
  playerIndex?: number
  text: string
  effectText?: string
  cards?: Card[]
}

export type CardStatusIconKind = 'lock' | 'power-up' | 'power-down' | 'disabled' | 'shield' | 'discard-end' | 'extra-attack'

export interface CardStatusEffect {
  kind: CardStatusIconKind | string
  label: string
  source?: string
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
  hasPrintedCost?: boolean
  baseTroops: number
  troops: number
  disasterLevel: number
  trialValue?: number
  traits?: string[]
  profession?: string
  ownerIndex?: number
  isMasterLegion?: boolean
  activeKeywords?: string[]
  statusIcons?: string[]
  statusEffects?: CardStatusEffect[]
  displayBaseTroops?: number
  lastCavalryMoveTurn?: number
  hasCharge?: boolean
  hasStrongAttack?: boolean
  hasSureHit?: boolean
  hasShock?: boolean
  hasRangeBonus?: boolean
  hasRangedNoLoss?: boolean
  cannotBeRanged?: boolean
  hidden?: boolean
  identityKnown?: boolean
  currentCost?: number
  playCost?: number
  playBlockedReason?: string
  tapped: boolean
  summonRound: number
  cannotAttack?: boolean
  cannotSupport?: boolean
  canAttackBackAndMasterUntilTurn?: number
  immortalUses?: number
  immortalUntilTurn?: number
  suppressDeathUntilTurn?: number
  timedModifiers?: Array<{ troopsDelta: number; costDelta: number; expiresAfterTurn: number; source: string }>
  abilities?: Array<{ id: string; label: string; enabled?: boolean; disabledReason?: string; triggerOnly?: boolean }>
  attachedCards?: Card[]
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
  factionEffect?: { cardId: string; name: string; imageUrl?: string; effectText: string; abilities?: Array<{ id: string; label: string; enabled?: boolean; disabledReason?: string; triggerOnly?: boolean }> }
  master: { masterId: string; masterName: string; masterImageUrl?: string; effectText?: string; tapped?: boolean; hp: number; maxHp: number; statusIcons?: string[]; statusEffects?: CardStatusEffect[]; abilities?: Array<{ id: string; label: string; enabled?: boolean; disabledReason?: string; triggerOnly?: boolean }> }
  libraryCount: number
  libraryTop?: Card | null
  hand?: Card[]
  handCount?: number
  moraleDeck?: Array<{ instanceId: string; cardId: string; tapped: boolean; isGodPower?: boolean }>
  moraleDeckCount?: number
  morale: Array<{ instanceId: string; cardId: string; tapped: boolean; isGodPower?: boolean }>
  field: Array<Array<Card | null>>
  relic?: Card | null
  extraRelics?: Card[]
  graveyard?: Card[]
  graveyardCount?: number
  resolving?: Card[]
  specialZones?: {
    runes: number
    trialLevel: number
    trialCapacity?: number
    godPower: Card[]
    trials: Array<Card & { trialProgress?: number; trialCompleted?: boolean }>
    canopicProgress?: Card[]
    canopicTrack?: Array<Card & { completed: boolean }>
  }
  temporaryMorale?: number
  nextLegionChargeMaxCost?: number | null
  nextS2PromotionGodPowerDiscount?: number
  mulliganDone: boolean
}

export interface GameState {
  matchId: string
  roomCode: string
  operationsPolicyVersion?: number
  you: number
  revision: number
  activePlayer: number
  firstPlayer: number
  diceWinner: number
  initiativeRolls: number[]
  phase: Phase
  round: number
  turnSerial?: number
  disasterMode: 'all' | 'random' | 'season' | 'none' | 'custom'
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
  pendingDefense?: {
    attackerPlayer: number
    attackerInstanceId: string
    target: { type: string; instanceId?: string }
    stage: 'AttackerAttackTiming' | 'DefenderAttackTiming' | 'DefenseChoice' | 'CombatDamage'
      | 'KillTriggers' | 'AttackerDeathTriggers' | 'DefenderDeathTriggers' | 'FinalizeDeaths'
      | 'AttackerAfterAttack' | 'DefenderAfterAttack' | 'Complete'
    attackValue: number
  } | null
  winner?: number | null
  winnerReason?: string | null
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
  options?: { matchModeId: string; spectating: 'public' | 'friends' | 'disabled'; handVisibility: 'request' | 'public'; disasterMode: 'all' | 'random' | 'custom' | 'none'; useCardRestrictions?: boolean }
  operationsPolicyVersion?: number
  started: boolean
  sandbox?: boolean
}
