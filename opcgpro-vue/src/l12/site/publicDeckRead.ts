import type {
  PublicDeckContentCurrent, PublicDeckCurrentRead, PublicDeckGuide, PublicDeckMatchup, PublicDeckReadGeneration,
  PublicDeckStatisticsPage, PublicDeckSummary, PublicDeckVersionMetadata, PublicDeckVersionPage,
  PublicDeckVersionRead,
} from '../platform'
import type { SavedL12Deck } from '../decks'

const lowerToken = /^[a-f0-9]{64}$/
const integer = (value: unknown, minimum = 0) => typeof value === 'number'
  && Number.isSafeInteger(value) && value >= minimum
const record = (value: unknown): Record<string, unknown> | null => value !== null && typeof value === 'object'
  && !Array.isArray(value) ? value as Record<string, unknown> : null
const dateText = (value: unknown) => typeof value === 'string' && value.length > 0 && Number.isFinite(Date.parse(value))

function exact(value: Record<string, unknown>, keys: readonly string[], label: string) {
  const actual = Object.keys(value).sort(), expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    throw new Error(`${label}字段无效`)
}

function validateCounts(value: unknown) {
  const counts = record(value)
  if (!counts) throw new Error('牌库数量摘要无效')
  const keys = ['main', 'uncountedMain', 'morale', 'special', 'bench'] as const
  exact(counts, keys, '牌库数量摘要')
  if (keys.some(key => !integer(counts[key]))) throw new Error('牌库数量摘要无效')
  return counts as unknown as PublicDeckSummary['counts']
}

function validateEnvironment(value: unknown) {
  const environment = record(value)
  if (!environment) throw new Error('牌库环境摘要无效')
  exact(environment, ['status', 'value', 'reason'], '牌库环境摘要')
  if (typeof environment.status !== 'string' || !environment.status
    || ![null, '1.0', '2.0', '2.5'].includes(environment.value as null | string)
    || environment.reason !== null && typeof environment.reason !== 'string')
    throw new Error('牌库环境摘要无效')
  return environment as unknown as PublicDeckSummary['environment']
}

function validateSummary(value: unknown) {
  const summary = record(value)
  if (!summary) throw new Error('公开牌库摘要无效')
  exact(summary, ['id', 'source', 'name', 'masterId', 'masterName', 'faction', 'author', 'publicCode',
    'publicationVersion', 'createdAt', 'updatedAt', 'counts', 'legal', 'legalityReason', 'environment',
    'views', 'likes', 'copies', 'viewerLiked', 'canEdit', 'readToken'], '公开牌库摘要')
  if (typeof summary.id !== 'string' || !summary.id || summary.source !== 'public'
    || ['name', 'masterId', 'masterName', 'faction', 'author', 'publicCode'].some(key =>
      typeof summary[key] !== 'string' || !summary[key])
    || !dateText(summary.createdAt) || !dateText(summary.updatedAt)
    || summary.readToken !== null && !lowerToken.test(String(summary.readToken))
    || !integer(summary.views) || Number(summary.views) > 2147483647
    || !integer(summary.likes) || Number(summary.likes) > 2147483647
    || !integer(summary.copies) || Number(summary.copies) > 2147483647
    || typeof summary.viewerLiked !== 'boolean' || typeof summary.canEdit !== 'boolean'
    || typeof summary.legal !== 'boolean'
    || summary.legalityReason !== null && typeof summary.legalityReason !== 'string'
    || summary.publicationVersion !== null && !integer(summary.publicationVersion, 1))
    throw new Error('公开牌库摘要无效')
  validateCounts(summary.counts)
  validateEnvironment(summary.environment)
  return summary as unknown as PublicDeckSummary
}

function validateStringArray(value: unknown, label: string) {
  if (!Array.isArray(value) || value.some(item => typeof item !== 'string' || !item)) throw new Error(`${label}无效`)
  return value as string[]
}

function validateStringMap(value: unknown, copies: boolean) {
  if (value === undefined || value === null) return
  const map = record(value)
  if (!map || Object.entries(map).some(([key, item]) => !key || (copies
    ? !Array.isArray(item) || item.some(copy => typeof copy !== 'string' || !copy)
    : typeof item !== 'string' || !item))) throw new Error('牌库异画字段无效')
}

function validateDeck(value: unknown) {
  const deck = record(value)
  if (!deck) throw new Error('牌库正文无效')
  const allowed = new Set(['id', 'revision', 'publicationId', 'publicationVersion', 'name', 'masterId', 'cardIds',
    'moraleIds', 'specialIds', 'benchIds', 'alternateArtSelections', 'alternateArtCopies', 'updatedAt'])
  if (Object.keys(deck).some(key => !allowed.has(key)) || typeof deck.name !== 'string' || !deck.name
    || typeof deck.masterId !== 'string' || !deck.masterId || !dateText(deck.updatedAt)
    || deck.id !== undefined && typeof deck.id !== 'string'
    || deck.revision !== undefined && !integer(deck.revision)
    || deck.publicationId !== undefined && deck.publicationId !== null && typeof deck.publicationId !== 'string'
    || deck.publicationVersion !== undefined && deck.publicationVersion !== null && !integer(deck.publicationVersion, 1))
    throw new Error('牌库正文无效')
  validateStringArray(deck.cardIds, '主牌正文')
  validateStringArray(deck.moraleIds, '士气正文')
  validateStringArray(deck.specialIds, '额外牌正文')
  if (deck.benchIds !== undefined && deck.benchIds !== null) validateStringArray(deck.benchIds, '备牌正文')
  validateStringMap(deck.alternateArtSelections, false)
  validateStringMap(deck.alternateArtCopies, true)
  return deck as unknown as SavedL12Deck
}

function validateGuide(value: unknown) {
  const guide = record(value)
  if (!guide) throw new Error('牌库指南无效')
  const keys = ['buildIdea', 'opening', 'keyCards', 'commonSequence', 'substitutions'] as const
  exact(guide, keys, '牌库指南')
  if (keys.some(key => typeof guide[key] !== 'string')) throw new Error('牌库指南无效')
  return guide as unknown as PublicDeckGuide
}

function validateMatchups(value: unknown) {
  if (!Array.isArray(value)) throw new Error('牌库对局建议无效')
  const seen = new Set<string>()
  for (const item of value) {
    const matchup = record(item)
    if (!matchup) throw new Error('牌库对局建议无效')
    exact(matchup, ['opponentMasterId', 'notes', 'keyCards', 'suggestedSwaps'], '牌库对局建议')
    if (typeof matchup.opponentMasterId !== 'string' || !matchup.opponentMasterId || seen.has(matchup.opponentMasterId)
      || ['notes', 'keyCards', 'suggestedSwaps'].some(key => typeof matchup[key] !== 'string'))
      throw new Error('牌库对局建议无效')
    seen.add(matchup.opponentMasterId)
  }
  return value as PublicDeckMatchup[]
}

function validateChange(value: unknown) {
  const change = record(value)
  if (!change) throw new Error('公开牌库版本变更无效')
  exact(change, ['section', 'cardId', 'previousQuantity', 'currentQuantity'], '公开牌库版本变更')
  if (!['master', 'main', 'morale', 'special'].includes(String(change.section))
    || typeof change.cardId !== 'string' || !change.cardId
    || !integer(change.previousQuantity) || !integer(change.currentQuantity)
    || change.previousQuantity === change.currentQuantity) throw new Error('公开牌库版本变更无效')
  return change
}

function validateVersionMetadata(value: unknown) {
  const metadata = record(value)
  if (!metadata) throw new Error('公开牌库版本元数据无效')
  exact(metadata, ['version', 'name', 'masterId', 'createdAt', 'counts', 'legal', 'legalityReason', 'environment', 'changes'],
    '公开牌库版本元数据')
  if (!integer(metadata.version, 1) || typeof metadata.name !== 'string' || !metadata.name
    || typeof metadata.masterId !== 'string' || !metadata.masterId || !dateText(metadata.createdAt)
    || typeof metadata.legal !== 'boolean'
    || metadata.legalityReason !== null && typeof metadata.legalityReason !== 'string'
    || !Array.isArray(metadata.changes)) throw new Error('公开牌库版本元数据无效')
  validateCounts(metadata.counts)
  validateEnvironment(metadata.environment)
  metadata.changes.forEach(validateChange)
  return metadata as unknown as PublicDeckVersionMetadata
}

function validateGeneration(value: Record<string, unknown>) {
  if (typeof value.id !== 'string' || !value.id || typeof value.publicCode !== 'string' || !value.publicCode
    || !lowerToken.test(String(value.readToken ?? '')) || typeof value.catalogVersion !== 'string' || !value.catalogVersion
    || !integer(value.policyVersion)) throw new Error('公开牌库读取代际无效')
  return value as unknown as PublicDeckReadGeneration
}

export function currentReadGeneration(value: PublicDeckCurrentRead): PublicDeckReadGeneration {
  return { id: value.summary.id, publicCode: value.summary.publicCode!, readToken: value.readToken,
    catalogVersion: value.catalogVersion, policyVersion: value.policyVersion }
}

export function samePublicDeckReadGeneration(left: PublicDeckReadGeneration, right: PublicDeckReadGeneration) {
  return left.id === right.id && left.publicCode === right.publicCode && left.readToken === right.readToken
    && left.catalogVersion === right.catalogVersion && left.policyVersion === right.policyVersion
}

export function validatePublicDeckCurrent(value: unknown) {
  const current = record(value)
  if (!current) throw new Error('公开牌库当前正文无效')
  exact(current, ['summary', 'version', 'deck', 'guide', 'matchups', 'contentRevision', 'contentUpdatedAt',
    'readToken', 'catalogVersion', 'policyVersion'], '公开牌库当前正文')
  const summary = validateSummary(current.summary), deck = validateDeck(current.deck)
  if (!integer(current.version, 1) || !integer(current.contentRevision)
    || current.contentUpdatedAt !== null && !dateText(current.contentUpdatedAt)
    || !lowerToken.test(String(current.readToken ?? '')) || summary.readToken !== null
    || typeof current.catalogVersion !== 'string' || !current.catalogVersion || !integer(current.policyVersion)
    || summary.canEdit && (summary.publicationVersion !== current.version
      || deck.publicationId !== summary.id || deck.publicationVersion !== current.version)
    || !summary.canEdit && (summary.publicationVersion !== null
      || deck.publicationId != null || deck.publicationVersion != null))
    throw new Error('公开牌库当前正文代际或权限字段无效')
  validateGuide(current.guide)
  validateMatchups(current.matchups)
  return current as unknown as PublicDeckCurrentRead
}

export function validatePublicDeckVersionPage(value: unknown, expected: PublicDeckReadGeneration,
  page: number, pageSize: number, canEdit: boolean) {
  const result = record(value)
  if (!result) throw new Error('公开牌库版本目录无效')
  exact(result, ['id', 'publicCode', 'items', 'total', 'page', 'pageSize', 'readToken', 'catalogVersion', 'policyVersion', 'canEdit'],
    '公开牌库版本目录')
  const generation = validateGeneration(result)
  if (!samePublicDeckReadGeneration(expected, generation) || result.page !== page || result.pageSize !== pageSize
    || result.canEdit !== canEdit || !integer(result.total) || !Array.isArray(result.items)
    || result.items.length > pageSize || result.items.length > Number(result.total)) throw new Error('公开牌库版本目录代际无效')
  let previous = Number.POSITIVE_INFINITY
  const seen = new Set<number>()
  for (const item of result.items) {
    const metadata = validateVersionMetadata(item)
    if (metadata.version >= previous || seen.has(metadata.version)) throw new Error('公开牌库版本目录顺序无效')
    previous = metadata.version; seen.add(metadata.version)
  }
  return result as unknown as PublicDeckVersionPage
}

export function validatePublicDeckVersion(value: unknown, expected: PublicDeckReadGeneration,
  version: number, canEdit: boolean) {
  const result = record(value)
  if (!result) throw new Error('公开牌库历史正文无效')
  exact(result, ['id', 'publicCode', 'metadata', 'deck', 'readToken', 'catalogVersion', 'policyVersion', 'canEdit'],
    '公开牌库历史正文')
  const generation = validateGeneration(result), metadata = validateVersionMetadata(result.metadata), deck = validateDeck(result.deck)
  if (!samePublicDeckReadGeneration(expected, generation) || metadata.version !== version || result.canEdit !== canEdit
    || canEdit && (deck.publicationId !== expected.id || deck.publicationVersion !== version)
    || !canEdit && (deck.publicationId != null || deck.publicationVersion != null))
    throw new Error('公开牌库历史正文代际或权限字段无效')
  return result as unknown as PublicDeckVersionRead
}

export function validatePublicDeckContentCurrent(value: unknown, expected: PublicDeckReadGeneration,
  previousRevision: number) {
  const result = record(value)
  if (!result) throw new Error('公开牌库内容保存响应无效')
  exact(result, ['id', 'publicCode', 'guide', 'matchups', 'contentRevision', 'contentUpdatedAt',
    'readToken', 'catalogVersion', 'policyVersion', 'canEdit'], '公开牌库内容保存响应')
  const next = validateGeneration(result)
  if (next.id !== expected.id || next.publicCode !== expected.publicCode
    || next.catalogVersion !== expected.catalogVersion || next.policyVersion !== expected.policyVersion
    || result.canEdit !== true || !integer(previousRevision) || !integer(result.contentRevision)
    || Number(result.contentRevision) < previousRevision
    || result.contentUpdatedAt !== null && !dateText(result.contentUpdatedAt))
    throw new Error('公开牌库内容保存代际或权限字段无效')
  validateGuide(result.guide)
  validateMatchups(result.matchups)
  return result as unknown as PublicDeckContentCurrent
}

export function validatePublicDeckStatistics(value: unknown, expected: PublicDeckReadGeneration,
  page: number, pageSize: number) {
  const result = record(value)
  if (!result) throw new Error('公开牌库统计分页无效')
  exact(result, ['id', 'publicCode', 'readToken', 'catalogVersion', 'policyVersion', 'from', 'to', 'recentDays',
    'games', 'sampleStatus', 'groups', 'total', 'page', 'pageSize'], '公开牌库统计分页')
  const generation = validateGeneration(result)
  if (!samePublicDeckReadGeneration(expected, generation) || result.page !== page || result.pageSize !== pageSize
    || result.recentDays !== 90 || !dateText(result.from) || !dateText(result.to)
    || Date.parse(String(result.from)) > Date.parse(String(result.to)) || !integer(result.games)
    || !integer(result.total) || !['available', 'insufficient', 'empty'].includes(String(result.sampleStatus))
    || !Array.isArray(result.groups) || result.groups.length > pageSize || result.groups.length > Number(result.total))
    throw new Error('公开牌库统计分页代际或范围无效')
  const keys = new Set<string>()
  for (const item of result.groups) {
    const group = record(item)
    if (!group) throw new Error('公开牌库统计分组无效')
    exact(group, ['version', 'masterId', 'opponentMasterId', 'games', 'wins', 'losses', 'draws', 'winRate'], '公开牌库统计分组')
    const key = `${group.version}\0${group.masterId}\0${group.opponentMasterId}`
    if (!integer(group.version, 1) || typeof group.masterId !== 'string' || !group.masterId
      || typeof group.opponentMasterId !== 'string' || !group.opponentMasterId || !integer(group.games, 3)
      || !integer(group.wins) || !integer(group.losses) || !integer(group.draws)
      || Number(group.wins) + Number(group.losses) + Number(group.draws) !== group.games
      || typeof group.winRate !== 'number' || !Number.isFinite(group.winRate) || group.winRate < 0 || group.winRate > 1
      || Math.abs(group.winRate - Number(group.wins) / Number(group.games)) > 0.000051 || keys.has(key))
      throw new Error('公开牌库统计分组无效')
    keys.add(key)
  }
  if (result.sampleStatus !== 'available' && (result.games !== 0 || result.total !== 0 || result.groups.length !== 0)
    || result.sampleStatus === 'available' && (Number(result.games) < 3 || Number(result.total) < 1))
    throw new Error('公开牌库统计样本状态无效')
  return result as unknown as PublicDeckStatisticsPage
}

export type PublicDeckReadResult<T> = { status: 'available'; value: T }
  | { status: 'refresh-required'; message: string }
  | { status: 'unavailable'; message: string }

export async function settlePublicDeckRead<T>(request: Promise<unknown>, validate: (value: unknown) => T): Promise<PublicDeckReadResult<T>> {
  try { return { status: 'available', value: validate(await request) } }
  catch (error) {
    const value = record(error), status = Number(value?.status ?? 0), code = String(value?.code ?? '')
    if (status === 409 || code === 'public_deck_read_conflict')
      return { status: 'refresh-required', message: '牌库已更新，请刷新后查看最新内容。' }
    if (status === 401 || code === 'stale_session')
      return { status: 'unavailable', message: '登录状态已变化，请重新登录后再试。' }
    if (status === 403) return { status: 'unavailable', message: '当前账号没有权限修改这个公开牌库。' }
    if (status === 404) return { status: 'unavailable', message: '没有找到这个公开牌库或版本。' }
    if (status === 400) return { status: 'unavailable', message: '指南或对局建议内容无效，请检查后重试。' }
    if (status === 503 && code === 'feature_disabled')
      return { status: 'unavailable', message: '公开牌库当前未开放，请稍后再试。' }
    if (status >= 500 || ['storage_unavailable', 'network_error', 'request_timeout'].includes(code))
      return { status: 'unavailable', message: '公开牌库暂时无法读取，请稍后重试。' }
    return { status: 'unavailable', message: '公开牌库返回内容无法确认，请刷新后重试。' }
  }
}

interface PublicDeckDetailBase {
  id: string; publicCode: string | null; source: 'public' | 'official'; name: string; author: string; deck: SavedL12Deck
  views: number; likes: number; copies: number; viewerLiked: boolean; liked: boolean; canEdit: boolean
  createdAt: string; updatedAt: string; seasonCompliant: boolean; seasonComplianceReason: string | null
  version: number; guide: PublicDeckGuide; matchups: PublicDeckMatchup[]; contentRevision: number
  contentUpdatedAt: string | null; readToken: string | null; official?: boolean
}
export type PublicDeckDetailEntry = PublicDeckDetailBase & PublicDeckReadGeneration
  & { source: 'public'; publicCode: string; readToken: string }
export interface OfficialDeckDetailEntry extends PublicDeckDetailBase {
  source: 'official'; publicCode: null; readToken: null; official: true
}
export type PublicDeckPresentationEntry = PublicDeckDetailEntry | OfficialDeckDetailEntry

export function toPublicDeckCurrentEntry(current: PublicDeckCurrentRead): PublicDeckDetailEntry {
  const value = validatePublicDeckCurrent(current), summary = value.summary
  return { ...currentReadGeneration(value), source: 'public', name: summary.name, author: summary.author,
    deck: value.deck, views: summary.views, likes: summary.likes, copies: summary.copies,
    viewerLiked: summary.viewerLiked, liked: summary.viewerLiked, canEdit: summary.canEdit,
    createdAt: summary.createdAt!, updatedAt: summary.updatedAt!, seasonCompliant: summary.legal,
    seasonComplianceReason: summary.legalityReason, version: value.version, guide: value.guide,
    matchups: value.matchups, contentRevision: value.contentRevision, contentUpdatedAt: value.contentUpdatedAt }
}
