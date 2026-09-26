import type {
  EffectiveOperationsPolicy,
  RankedTimeControlConfig,
  TournamentCreateInput,
  TournamentDisasterMode,
} from './platform'

export const TOURNAMENT_ANNIHILATION_CARD_ID = 'S01-DS10'

function boundedInteger(value: number | undefined, fallback: number, minimum: number, maximum: number) {
  const normalized = Number.isFinite(Number(value)) ? Math.trunc(Number(value)) : fallback
  return Math.max(minimum, Math.min(maximum, normalized))
}

export function normalizeTournamentFormatFields<T extends Pick<TournamentCreateInput,
  'format' | 'maxPlayers' | 'swissRounds' | 'cutSize'>>(input: T): T {
  const maxPlayers = boundedInteger(input.maxPlayers, 16, 2, 256)
  if (input.format === 'single') return { ...input, maxPlayers, swissRounds: 0, cutSize: undefined }

  const swissRounds = boundedInteger(input.swissRounds, 4, 1, 20)
  if (input.format === 'swiss-cut') {
    const cutSize = boundedInteger(input.cutSize, Math.min(8, maxPlayers), 2, maxPlayers)
    return { ...input, maxPlayers, swissRounds, cutSize }
  }
  return { ...input, maxPlayers, swissRounds, cutSize: undefined }
}

export function normalizeTournamentFormatInPlace(target: Pick<TournamentCreateInput,
  'format' | 'maxPlayers' | 'swissRounds' | 'cutSize'>) {
  const normalized = normalizeTournamentFormatFields(target)
  target.maxPlayers = normalized.maxPlayers
  target.swissRounds = normalized.swissRounds
  target.cutSize = normalized.cutSize
}

export function tournamentDisasterSnapshot(mode: TournamentDisasterMode,
  policy: Pick<EffectiveOperationsPolicy, 'disasterCardIds'>) {
  if (mode === 'none') return []

  const cardIds = policy.disasterCardIds.map(cardId => cardId.trim()).filter(Boolean)
  if (cardIds.length === 0)
    throw new Error('当前运营策略未提供可用天灾池，无法创建使用天灾的赛事')
  if (cardIds.length < 9 || cardIds.length > 64)
    throw new Error('当前运营策略天灾池必须为 9–64 张，无法创建赛事')
  if (new Set(cardIds.map(cardId => cardId.toUpperCase())).size !== cardIds.length)
    throw new Error('当前运营策略天灾池包含重复卡牌，无法创建赛事')
  if (cardIds.at(-1)?.toUpperCase() !== TOURNAMENT_ANNIHILATION_CARD_ID)
    throw new Error(`当前运营策略天灾池必须以堙灭（${TOURNAMENT_ANNIHILATION_CARD_ID}）作为最后一张`)
  return [...cardIds]
}

export function buildTournamentCreateInput(form: TournamentCreateInput,
  timeControl: RankedTimeControlConfig, policy: Pick<EffectiveOperationsPolicy, 'disasterCardIds'>) {
  const normalized = normalizeTournamentFormatFields(form)
  return {
    ...normalized,
    name: normalized.name.trim(),
    ruleset: normalized.ruleset.trim() || '现行规则',
    description: normalized.description.trim(),
    banList: normalized.banList.trim(),
    refereeAccountIds: [...normalized.refereeAccountIds],
    cardRestrictions: normalized.cardRestrictions.map(item => ({ ...item })),
    disasterCardIds: tournamentDisasterSnapshot(normalized.disasterMode, policy),
    timeControl: { ...timeControl },
  } satisfies TournamentCreateInput
}
