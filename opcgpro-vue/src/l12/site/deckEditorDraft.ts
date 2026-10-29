import type { SavedL12Deck } from '../decks'
import { deckCacheDatabase, deckDatabaseScope, DeckDatabaseError } from '../deckCacheDatabase'
import { deploymentBasePath } from '../deploymentBase'

const PREFIX = 'l12:deck-editor-draft:v1:'
const MAX_CHARACTERS = 64_000

export interface DeckEditorDraft {
  schema: 1 | 2
  owner: string
  savedAt: string
  baseDeckName: string | null
  baseDeckId?: string | null
  baseDeckRevision?: number | null
  deck: SavedL12Deck
}

export function draftOwner(accountId: string | null | undefined): string {
  return accountId ? `account:${accountId}` : 'guest:session'
}

export function draftStorageKey(owner: string, version: 1 | 2 = 1): string {
  return `${version === 2 ? 'l12:deck-editor-draft:v2:' : PREFIX}${encodeURIComponent(owner)}`
}

function validCardIds(value: unknown, maximum: number): value is string[] {
  return Array.isArray(value) && value.length <= maximum
    && value.every(id => typeof id === 'string' && id.length <= 80)
}

function validDeck(value: unknown): value is SavedL12Deck {
  if (!value || typeof value !== 'object') return false
  const deck = value as Record<string, unknown>
  return typeof deck.name === 'string' && deck.name.length <= 24
    && typeof deck.masterId === 'string' && deck.masterId.length <= 80
    && validCardIds(deck.cardIds, 200)
    && validCardIds(deck.moraleIds, 20)
    && validCardIds(deck.specialIds, 30)
    && (deck.benchIds === undefined || validCardIds(deck.benchIds, 200))
    && (deck.representativeCardIds === undefined || validCardIds(deck.representativeCardIds, 3)
      && new Set(deck.representativeCardIds.map(id => id.toLowerCase())).size === deck.representativeCardIds.length
      && deck.representativeCardIds.every(id => !!id && (deck.cardIds as string[]).some(main => main.toLowerCase() === id.toLowerCase())))
}

export function readDeckEditorDraft(storage: Pick<Storage, 'getItem'>, owner: string, version: 1 | 2 = 1): DeckEditorDraft | null {
  const text = storage.getItem(draftStorageKey(owner, version)) ?? (version === 2 ? storage.getItem(draftStorageKey(owner)) : null)
  if (text === null) return null
  if (text.length > MAX_CHARACTERS) throw new Error('本地草稿超出容量限制')
  let parsed: unknown
  try { parsed = JSON.parse(text) } catch { throw new Error('本地草稿格式损坏') }
  if (!parsed || typeof parsed !== 'object') throw new Error('本地草稿格式损坏')
  const draft = parsed as Record<string, unknown>
  if (version === 2 && draft.schema === 'cleared' && draft.owner === owner && Object.keys(draft).length === 2) return null
  if (![1, 2].includes(Number(draft.schema)) || draft.owner !== owner || typeof draft.savedAt !== 'string'
    || (draft.baseDeckName !== null && typeof draft.baseDeckName !== 'string') || !validDeck(draft.deck)
    || (draft.schema === 2 && (draft.baseDeckId !== null && typeof draft.baseDeckId !== 'string'
      || draft.baseDeckRevision !== null && (typeof draft.baseDeckRevision !== 'number'
        || !Number.isSafeInteger(draft.baseDeckRevision) || draft.baseDeckRevision < 1)))) {
    throw new Error('本地草稿格式或归属不正确')
  }
  return draft as unknown as DeckEditorDraft
}

export function writeDeckEditorDraft(storage: Pick<Storage, 'setItem'>, owner: string,
  deck: SavedL12Deck, baseDeckName: string | null, savedAt = new Date().toISOString(),
  baseDeckId: string | null = null, baseDeckRevision: number | null = null, version: 1 | 2 = 1): DeckEditorDraft {
  if (!validDeck(deck)) throw new Error('当前构筑无法暂存')
  const draft: DeckEditorDraft = { schema: 2, owner, savedAt, baseDeckName, baseDeckId, baseDeckRevision, deck }
  const text = JSON.stringify(draft)
  if (text.length > MAX_CHARACTERS) throw new Error('本地草稿超出容量限制')
  storage.setItem(draftStorageKey(owner, version), text)
  return draft
}

export function clearDeckEditorDraft(storage: Pick<Storage, 'removeItem' | 'setItem'>, owner: string, version: 1 | 2 = 1): void {
  // Preserve the v1 draft for old tabs while preventing a restored draft from reappearing in v2.
  if (version === 2) storage.setItem(draftStorageKey(owner, version), JSON.stringify({ schema: 'cleared', owner }))
  else storage.removeItem(draftStorageKey(owner, version))
}

/** An opaque per-tab-session key is not a second copy of any draft or deck. */
export function deckDraftSessionId() {
  const key = `l12:deck-draft-session@${deploymentBasePath}`
  let id = globalThis.sessionStorage.getItem(key)
  if (!id) { id = crypto.randomUUID(); globalThis.sessionStorage.setItem(key, id) }
  return id
}
export function transactionalDraftScope(accountId: string | null | undefined) {
  return { database: deckDatabaseScope(deploymentBasePath, accountId ? `account:${accountId}` : 'guest'),
    owner: draftOwner(accountId), prefix: accountId ? 'account' : `session:${deckDraftSessionId()}` }
}
export interface TransactionalDeckDraft { slot: string; raw: string; draft: DeckEditorDraft }
export async function readTransactionalDeckDrafts(scope: ReturnType<typeof transactionalDraftScope>): Promise<TransactionalDeckDraft[]> {
  const state = await deckCacheDatabase.read(scope.database)
  return Object.entries(state.drafts).filter(([slot, raw]) => raw !== null
    && (slot === scope.prefix || slot.startsWith(`${scope.prefix}:recovery:`)))
    .flatMap(([slot, raw]) => {
      const draft = readDeckEditorDraft({ getItem: () => raw! }, scope.owner, 2)
      return draft ? [{ slot, raw: raw!, draft }] : []
    })
}
export async function writeTransactionalDeckDraft(scope: ReturnType<typeof transactionalDraftScope>, deck: SavedL12Deck,
  baseDeckName: string | null, baseDeckId: string | null, baseDeckRevision: number | null,
  expected: TransactionalDeckDraft | null, recovery = false): Promise<TransactionalDeckDraft> {
  let raw = ''
  const draft = writeDeckEditorDraft({ setItem: (_key, value) => { raw = value } }, scope.owner,
    deck, baseDeckName, new Date().toISOString(), baseDeckId, baseDeckRevision, 2)
  const slot = recovery ? `${scope.prefix}:recovery:${crypto.randomUUID()}` : expected?.slot ?? scope.prefix
  await deckCacheDatabase.update(scope.database, null, state => {
    if (!recovery && (state.drafts[slot] ?? null) !== (expected?.raw ?? null))
      throw new DeckDatabaseError('另一个页面已暂存此草稿；当前修改仍保留，可另存为恢复草稿', 'conflict')
    state.drafts[slot] = raw
  })
  return { slot, raw, draft }
}
export async function clearTransactionalDeckDraft(scope: ReturnType<typeof transactionalDraftScope>, expected: TransactionalDeckDraft) {
  await deckCacheDatabase.update(scope.database, null, state => {
    if (state.drafts[expected.slot] !== expected.raw)
      throw new DeckDatabaseError('草稿已由其他页面更新；新草稿已保留', 'conflict')
    state.drafts[expected.slot] = null
  })
}
