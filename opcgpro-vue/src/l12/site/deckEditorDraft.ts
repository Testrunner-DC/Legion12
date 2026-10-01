import type { SavedL12Deck } from '../decks'

const PREFIX = 'l12:deck-editor-draft:v1:'
const MAX_CHARACTERS = 64_000

export interface DeckEditorDraft {
  schema: 1
  owner: string
  savedAt: string
  baseDeckName: string | null
  deck: SavedL12Deck
}

export function draftOwner(accountId: string | null | undefined): string {
  return accountId ? `account:${accountId}` : 'guest:session'
}

export function draftStorageKey(owner: string): string {
  return `${PREFIX}${encodeURIComponent(owner)}`
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
}

export function readDeckEditorDraft(storage: Pick<Storage, 'getItem'>, owner: string): DeckEditorDraft | null {
  const text = storage.getItem(draftStorageKey(owner))
  if (text === null) return null
  if (text.length > MAX_CHARACTERS) throw new Error('本地草稿超出容量限制')
  let parsed: unknown
  try { parsed = JSON.parse(text) } catch { throw new Error('本地草稿格式损坏') }
  if (!parsed || typeof parsed !== 'object') throw new Error('本地草稿格式损坏')
  const draft = parsed as Record<string, unknown>
  if (draft.schema !== 1 || draft.owner !== owner || typeof draft.savedAt !== 'string'
    || (draft.baseDeckName !== null && typeof draft.baseDeckName !== 'string') || !validDeck(draft.deck)) {
    throw new Error('本地草稿格式或归属不正确')
  }
  return draft as unknown as DeckEditorDraft
}

export function writeDeckEditorDraft(storage: Pick<Storage, 'setItem'>, owner: string,
  deck: SavedL12Deck, baseDeckName: string | null, savedAt = new Date().toISOString()): DeckEditorDraft {
  if (!validDeck(deck)) throw new Error('当前构筑无法暂存')
  const draft: DeckEditorDraft = { schema: 1, owner, savedAt, baseDeckName, deck }
  const text = JSON.stringify(draft)
  if (text.length > MAX_CHARACTERS) throw new Error('本地草稿超出容量限制')
  storage.setItem(draftStorageKey(owner), text)
  return draft
}

export function clearDeckEditorDraft(storage: Pick<Storage, 'removeItem'>, owner: string): void {
  storage.removeItem(draftStorageKey(owner))
}
