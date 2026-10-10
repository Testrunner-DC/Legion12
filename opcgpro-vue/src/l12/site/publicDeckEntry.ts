import type { SavedL12Deck } from '@/l12/decks'
import type { PublishedDeck, PublicDeckCounterResult, PublicDeckSummary } from '@/l12/platform'

interface PublicDeckCounterTarget {
  id: string
  publicCode?: string | null
  official?: boolean
  source?: string
  deck?: unknown
  details?: unknown
  readToken?: unknown
}

export function isOfficialPublicDeckCounterTarget(entry: PublicDeckCounterTarget) {
  return entry.official === true || entry.source === 'official'
}

export function mergePublicDeckCounters<T extends PublicDeckCounterTarget>(current: T | null,
  counters: PublicDeckCounterResult): T & { viewerLiked: boolean; canEdit: boolean; liked: boolean } {
  const fields = ['id', 'publicCode', 'views', 'likes', 'copies', 'viewerLiked', 'canEdit']
  if (!current || isOfficialPublicDeckCounterTarget(current) || !counters || typeof counters !== 'object'
    || Array.isArray(counters) || Object.keys(counters).length !== fields.length
    || fields.some(field => !Object.hasOwn(counters, field)) || typeof counters.id !== 'string'
    || !counters.id.trim() || counters.id !== current.id
    || typeof counters.publicCode !== 'string' || !counters.publicCode.trim()
    || current.publicCode && counters.publicCode !== current.publicCode
    || [counters.views, counters.likes, counters.copies].some(value => !Number.isInteger(value) || value < 0 || value > 2147483647)
    || typeof counters.viewerLiked !== 'boolean' || typeof counters.canEdit !== 'boolean')
    throw new Error('公开牌库计数响应无效或不属于当前牌库')
  // Only these scalar fields cross the boundary. The body, guide, versions,
  // statistics and consistency token retain their actual captured references.
  return { ...current, publicCode: counters.publicCode, views: counters.views, likes: counters.likes,
    copies: counters.copies, viewerLiked: counters.viewerLiked, liked: counters.viewerLiked, canEdit: counters.canEdit }
}

export function capturePublicDeckCounterGuard<T extends PublicDeckCounterTarget>(initial: T, context: {
  actorCurrent: () => boolean
  document: () => string
  entry: () => T | null
  actionCurrent: () => boolean
}) {
  const document = context.document(), id = initial.id, code = initial.publicCode
  const body = initial.deck, details = initial.details, token = initial.readToken
  return () => {
    const current = context.entry()
    return context.actorCurrent() && context.actionCurrent() && context.document() === document
      && current?.id === id && current.publicCode === code && current.deck === body
      && current.details === details && current.readToken === token
  }
}

export function preservePublicDeckDetails<T extends { details?: unknown }>(current: T | null, updated: T): T {
  return { ...updated, details: updated.details ?? current?.details }
}

export function matchesPublishedDeckReference(deck: SavedL12Deck, published: PublishedDeck, ownerId?: string) {
  const publicationId = deck.publicationId?.trim()
  const publicationVersion = deck.publicationVersion
  return Boolean(publicationId && publicationVersion
    && published.id === publicationId
    && published.deck.publicationId === publicationId
    && published.deck.publicationVersion === publicationVersion
    && (!ownerId || published.ownerId === ownerId))
}

export function publicDeckRouteReference(published: PublishedDeck) {
  // 官方预组由本地目录提供详情，不经过社区牌库的短码接口。
  if (published.official && published.ownerId === 'official' && /^official-\d+$/.test(published.id))
    return published.id
  return published.publicCode?.trim() || ''
}

export function publicDeckSummaryReference(item: PublicDeckSummary) {
  if (item.source === 'official') return /^official:[a-f0-9]{64}$/.test(item.id) && item.readToken === null ? item.id : ''
  return item.publicCode && /^[a-f0-9]{64}$/.test(item.readToken ?? '') ? item.publicCode : ''
}
