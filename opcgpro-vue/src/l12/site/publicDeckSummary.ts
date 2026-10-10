import type { OwnPublicDeckReference, OwnPublicDeckReferences, PublicDeckSummary, PublicDeckSummaryPage, PublicDeckSummaryQuery } from '../platform'
import type { SavedL12Deck } from '../decks'

const integer = (value: number) => Number.isSafeInteger(value) && value >= 0
export function validateSummaryPage(page: PublicDeckSummaryPage, query: PublicDeckSummaryQuery) {
  if (!page || !Array.isArray(page.items) || !integer(page.total) || page.page !== (query.page ?? 1)
    || page.pageSize !== (query.pageSize ?? 30) || page.items.length > page.pageSize || page.items.length > page.total
    || typeof page.generation !== 'string' || !page.generation || typeof page.catalogVersion !== 'string' || !page.catalogVersion
    || !integer(page.policyVersion) || !page.sourceAvailability || !['available', 'disabled'].includes(page.sourceAvailability.public)
    || page.sourceAvailability.official !== 'available' || !page.facets) throw new Error('牌库摘要分页响应无效')
  for (const facet of ['sources', 'masters', 'factions', 'environments', 'cards'] as const)
    if (!Array.isArray(page.facets[facet]) || page.facets[facet].some(item => typeof item.value !== 'string' || !integer(item.count)))
      throw new Error('牌库摘要筛选统计无效')
  if (!integer(page.facets.legal) || !integer(page.facets.illegal) || new Set(page.items.map(item => item.id)).size !== page.items.length)
    throw new Error('牌库摘要统计或身份无效')
  for (const item of page.items) {
    if (!item || typeof item.id !== 'string' || !item.id || !['public', 'official'].includes(item.source)
      || ['name', 'masterId', 'masterName', 'faction', 'author'].some(key => typeof item[key as keyof PublicDeckSummary] !== 'string')
      || !item.name || !item.masterId || !item.counts || typeof item.counts !== 'object'
      || ['main', 'uncountedMain', 'morale', 'special', 'bench'].some(key => !Object.hasOwn(item.counts, key)
        || !integer(item.counts[key as keyof PublicDeckSummary['counts']]))
      || !item.environment || typeof item.environment.status !== 'string' || !item.environment.status
      || ![null, '1.0', '2.0', '2.5'].includes(item.environment.value)
      || item.environment.reason !== null && typeof item.environment.reason !== 'string'
      || item.legalityReason !== null && typeof item.legalityReason !== 'string'
      || [item.createdAt, item.updatedAt].some(value => value !== null && typeof value !== 'string')
      || [item.views, item.likes, item.copies].some(value => !integer(value) || value > 2147483647)
      || typeof item.legal !== 'boolean' || typeof item.viewerLiked !== 'boolean' || typeof item.canEdit !== 'boolean'
      || ['deck', 'cardIds', 'moraleIds', 'specialIds', 'ownerId', 'payloadHash'].some(key => Object.hasOwn(item, key)))
      throw new Error('牌库摘要条目无效或混入正文')
    if (item.source === 'public' && (!item.publicCode || !/^[a-f0-9]{64}$/.test(item.readToken ?? ''))
      || item.source === 'official' && (!/^official:[a-f0-9]{64}$/.test(item.id) || item.readToken !== null || item.publicCode !== null)
      || !item.canEdit && item.publicationVersion !== null) throw new Error('牌库摘要引用或权限字段无效')
  }
  return page
}

export function validateOwnReferences(result: OwnPublicDeckReferences, ids: readonly string[], owner: string) {
  if (!result || result.status !== 'available' || !Array.isArray(result.items) || result.items.length > ids.length)
    throw new Error('公开牌库引用当前无法确认')
  let previous = -1
  for (const item of result.items) {
    const index = ids.indexOf(item.id)
    if (index <= previous || !item.publicCode || item.ownerId !== owner || !Number.isSafeInteger(item.publicationVersion)
      || item.publicationVersion < 1 || ['deck', 'payloadHash', 'sourceDeckId'].some(key => Object.hasOwn(item, key)))
      throw new Error('公开牌库引用身份、顺序或权限无效')
    previous = index
  }
  return result.items
}

export function matchesOwnPublication(deck: Pick<SavedL12Deck, 'publicationId' | 'publicationVersion'>,
  reference: OwnPublicDeckReference | undefined, owner: string | undefined) {
  return Boolean(owner && deck.publicationId && deck.publicationVersion && reference && reference.ownerId === owner
    && reference.id === deck.publicationId && reference.publicationVersion === deck.publicationVersion)
}

export function createSummaryRequestGate(readContext: () => string) {
  let sequence = 0, disposed = false
  return {
    begin() { const request = ++sequence, context = readContext(); return () => !disposed && request === sequence && context === readContext() },
    invalidate() { sequence++ },
    dispose() { disposed = true; sequence++ },
  }
}

export function summaryRouteReference(item: PublicDeckSummary) {
  if (item.source === 'official') {
    if (!/^official:[a-f0-9]{64}$/.test(item.id) || item.readToken !== null) throw new Error('官方牌库稳定引用无效')
    return item.id
  }
  if (!item.publicCode || !/^[a-f0-9]{64}$/.test(item.readToken ?? '')) throw new Error('公开牌库详情引用无法确认')
  return item.publicCode
}

interface SummaryOpenIntent { reference: string; readToken: string; actorCurrent: () => boolean; expiresAt: number }
const openIntents = new Map<string, SummaryOpenIntent>()
export function rememberSummaryOpen(item: PublicDeckSummary, actorCurrent: () => boolean) {
  const reference = summaryRouteReference(item)
  if (item.source === 'official') return { reference, query: {} }
  if (!actorCurrent()) throw new Error('账号已切换，请重新打开牌库')
  const ticket = globalThis.crypto.randomUUID()
  openIntents.set(ticket, { reference, readToken: item.readToken!, actorCurrent, expiresAt: Date.now() + 300_000 })
  while (openIntents.size > 32) openIntents.delete(openIntents.keys().next().value!)
  return { reference, query: { expectedReadToken: item.readToken!, summaryOpen: ticket } }
}
export function summaryOpenActorCurrent(ticket: string, reference: string, readToken: string) {
  const intent = openIntents.get(ticket)
  return Boolean(intent && intent.expiresAt > Date.now() && intent.reference === reference
    && intent.readToken === readToken && intent.actorCurrent())
}

export function consumeSummaryOpen(ticket: string, reference: string, readToken: string) {
  const intent = openIntents.get(ticket)
  if (!intent) return null
  openIntents.delete(ticket)
  if (intent.expiresAt <= Date.now() || intent.reference !== reference || intent.readToken !== readToken
    || !intent.actorCurrent()) return null
  // The ticket is a one-use directory-to-detail handoff. Once consumed, the
  // detail page retains only the original actor guard; its five-minute lookup
  // lifetime must not expire an already verified, open document.
  return { actorCurrent: intent.actorCurrent }
}
