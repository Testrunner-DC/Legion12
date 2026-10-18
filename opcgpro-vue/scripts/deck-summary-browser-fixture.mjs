import { selectDeckCoverCardIds } from '../src/l12/deckCover.ts'
import { deckEnvironmentForDeck } from '../src/l12/site/deckEnvironment.ts'

// Browser-only synthetic fixture: the current directory API returns metadata,
// not the retired full-body publicDeckApi.list payload.
export function publicSummaryFixture(rows, catalog) {
  const byId = new Map(catalog.map(card => [card.id, card]))
  const items = rows.filter(row => row.publicCode).map(row => {
    const deck = row.deck, environment = deckEnvironmentForDeck(deck)
    return {
      id: row.id, source: 'public', name: deck.name, masterId: deck.masterId || 'QA-MISSING-MASTER',
      masterName: byId.get(deck.masterId)?.nameZh || deck.masterId || '未知主宰',
      faction: byId.get(deck.masterId)?.faction || 'unknown', author: row.author,
      publicCode: row.publicCode, publicationVersion: null, createdAt: row.createdAt, updatedAt: row.updatedAt,
      counts: { main: deck.cardIds.length, uncountedMain: 0, morale: deck.moraleIds.length,
        special: deck.specialIds?.length || 0, bench: deck.benchIds?.length || 0 },
      coverCardIds: selectDeckCoverCardIds(deck.cardIds, byId),
      legal: Boolean(row.seasonCompliant), legalityReason: row.seasonCompliant ? null : '合成构筑提示',
      environment: { status: environment === 'pending' ? 'unconfigured' : 'configured',
        value: environment === 'pending' ? null : environment, reason: null },
      views: row.views, likes: row.likes, copies: row.copies, viewerLiked: false, canEdit: false,
      readToken: 'b'.repeat(64),
    }
  })
  const facets = values => [...new Set(values)].map(value => ({ value, count: values.filter(item => item === value).length }))
  return async (query = {}) => {
    const filtered = items.filter(item => (!query.source || query.source === 'all' || item.source === query.source)
      && (!query.keyword || [item.name, item.author, item.masterName].some(text => text.toLocaleLowerCase().includes(query.keyword.toLocaleLowerCase())))
      && (!query.environment || item.environment.value === query.environment)
      && (!query.masterId || item.masterId === query.masterId)
      && (!query.faction || item.faction === query.faction)
      && (query.legal === undefined || item.legal === query.legal))
    const score = item => item.copies * 4 + item.likes * 3 + item.views
    filtered.sort((a, b) => score(b) - score(a) || a.id.localeCompare(b.id))
    const page = query.page ?? 1, pageSize = query.pageSize ?? 30
    return { items: filtered.slice((page - 1) * pageSize, page * pageSize), total: filtered.length, page, pageSize,
      generation: 'A'.repeat(64), catalogVersion: 'C'.repeat(64), policyVersion: 1,
      sourceAvailability: { public: 'available', official: 'available' },
      facets: { sources: facets(filtered.map(item => item.source)), masters: facets(filtered.map(item => item.masterId)),
        factions: facets(filtered.map(item => item.faction)), environments: facets(filtered.map(item => item.environment.value || 'unconfigured')),
        cards: [], legal: filtered.filter(item => item.legal).length, illegal: filtered.filter(item => !item.legal).length } }
  }
}
