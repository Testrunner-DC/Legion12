import type { SavedL12Deck } from './decks'
import { decodeDeckCache, encodeDeckCache, type EncodedDeckCache } from './deckCacheCodec'
import { crc32 } from './deckCodeCodec'

export type DeckSelectionAliases = Record<string, { raw: string; resolved: string }>
export interface ReadableDeckCache {
  status: 'missing' | 'legacy' | 'ready'
  raw: string | null
  owner: string
  generation: string | null
  decks: Record<string, SavedL12Deck>
  aliases: DeckSelectionAliases
}
export interface UnavailableDeckCache {
  status: 'unavailable'
  raw: string | null
  owner: string
  decks: null
  error: DeckCacheStorageError
}
export type DeckCacheSnapshot = ReadableDeckCache | UnavailableDeckCache
export class DeckCacheStorageError extends Error {
  constructor(message: string, readonly kind: 'unavailable' | 'conflict' | 'persistence') { super(message) }
}

interface CacheBody {
  format: 'l12-deck-cache'
  schema: 1
  owner: string
  generation: string
  entries: Record<string, EncodedDeckCache>
  aliases: DeckSelectionAliases
}
const encoder = new TextEncoder()
const owns = (value: object, key: PropertyKey) => Object.prototype.hasOwnProperty.call(value, key)
function object(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && Object.getPrototypeOf(value) === Object.prototype
}
function fail(message: string, kind: DeckCacheStorageError['kind'] = 'unavailable'): never {
  throw new DeckCacheStorageError(message, kind)
}
function checksum(body: CacheBody) { return crc32(encoder.encode(JSON.stringify(body))).toString(16).padStart(8, '0') }
function same(left: unknown, right: unknown): boolean {
  if (Object.is(left, right)) return true
  if (!left || !right || typeof left !== 'object' || typeof right !== 'object') return false
  if (Array.isArray(left) !== Array.isArray(right)) return false
  const keys = Reflect.ownKeys(left)
  return keys.length === Reflect.ownKeys(right).length
    && keys.every(key => owns(right, key) && same(Reflect.get(left, key), Reflect.get(right, key)))
}
function checkedDecks(decks: unknown): Record<string, SavedL12Deck> {
  if (!object(decks)) fail('牌库缓存列表格式损坏；原数据已保留')
  return Object.fromEntries(Object.entries(decks).map(([key, value]) => {
    const restored = decodeDeckCache(encodeDeckCache(value as SavedL12Deck))
    if (!same(value, restored)) fail('牌库缓存不能无损转换；原数据已保留')
    return [key, restored]
  }))
}
function checkedAliases(value: unknown): DeckSelectionAliases {
  if (!object(value) || Reflect.ownKeys(value).length > 6) fail('牌库选择解释计划无效')
  for (const alias of Object.values(value)) {
    if (!object(alias) || Reflect.ownKeys(alias).length !== 2 || typeof alias.raw !== 'string'
      || typeof alias.resolved !== 'string' || !alias.resolved) fail('牌库选择解释计划无效')
  }
  return value as DeckSelectionAliases
}
function parse(raw: string | null, owner: string): ReadableDeckCache {
  if (raw === null) return { status: 'missing', raw, owner, generation: null, decks: {}, aliases: {} }
  let value: unknown
  try { value = JSON.parse(raw) } catch { fail('牌库缓存格式损坏；原数据已保留') }
  if (!object(value)) fail('牌库缓存列表格式损坏；原数据已保留')
  // A deck named "schema" or "format" is still a legacy map entry, not an envelope.
  if (typeof value.format !== 'string' && typeof value.schema !== 'number') {
    return { status: 'legacy', raw, owner, generation: null, decks: checkedDecks(value), aliases: {} }
  }
  if (value.format !== 'l12-deck-cache' || value.schema !== 1) fail('牌库缓存版本不受支持；原数据已保留')
  const fields = ['format', 'schema', 'owner', 'generation', 'entries', 'aliases', 'checksum']
  if (Reflect.ownKeys(value).length !== fields.length || fields.some(key => !owns(value, key))
    || value.owner !== owner || typeof value.generation !== 'string' || !value.generation
    || typeof value.checksum !== 'string' || !object(value.entries)) fail('牌库缓存字段或归属不正确；原数据已保留')
  const aliases = checkedAliases(value.aliases)
  const body: CacheBody = { format: 'l12-deck-cache', schema: 1, owner, generation: value.generation,
    entries: value.entries as Record<string, EncodedDeckCache>, aliases }
  if (checksum(body) !== value.checksum) fail('牌库缓存整体校验失败；原数据已保留')
  const decks = Object.fromEntries(Object.entries(body.entries).map(([key, entry]) => [key, decodeDeckCache(entry)]))
  return { status: 'ready', raw, owner, generation: body.generation, decks, aliases }
}

/** Read errors are explicit states. No read performs a migration, repair or write. */
export function readDeckCache(storage: Pick<Storage, 'getItem'>, key: string, owner: string): DeckCacheSnapshot {
  let raw: string | null = null
  try { raw = storage.getItem(key); return parse(raw, owner) }
  catch (error) {
    return { status: 'unavailable', raw, owner, decks: null,
      error: error instanceof DeckCacheStorageError ? error
        : new DeckCacheStorageError(`牌库缓存无法读取：${error instanceof Error ? error.message : '存储不可用'}；原数据已保留`, 'unavailable') }
  }
}
export function requireDeckCache(snapshot: DeckCacheSnapshot): ReadableDeckCache {
  if (snapshot.status === 'unavailable') throw snapshot.error
  return snapshot
}

/** One key is the transaction boundary, including the interpretation of old selection references. */
export function commitDeckCache(storage: Pick<Storage, 'getItem' | 'setItem'>, key: string,
  previous: ReadableDeckCache, decks: Record<string, SavedL12Deck>, generation: string,
  aliases: DeckSelectionAliases = previous.aliases, guard: () => void = () => {}) : ReadableDeckCache {
  guard()
  const current = requireDeckCache(readDeckCache(storage, key, previous.owner))
  if (current.raw !== previous.raw) fail('牌库缓存已由其他操作更新，请刷新后重试', 'conflict')
  const restored = checkedDecks(decks)
  const plan = checkedAliases(aliases)
  const body: CacheBody = { format: 'l12-deck-cache', schema: 1, owner: previous.owner, generation,
    entries: Object.fromEntries(Object.entries(restored).map(([name, deck]) => [name, encodeDeckCache(deck)])), aliases: plan }
  const raw = JSON.stringify({ ...body, checksum: checksum(body) })
  const result = parse(raw, previous.owner)
  if (!same(decks, result.decks) || !same(plan, result.aliases)) fail('牌库缓存写入前核验失败；原数据已保留')
  guard()
  // Callers serialize this compare-and-write under the existing namespace Web Lock.
  if (storage.getItem(key) !== previous.raw) fail('牌库缓存已由其他操作更新，请刷新后重试', 'conflict')
  try { storage.setItem(key, raw) }
  catch (error) { fail(`本机牌库缓存写入失败：${error instanceof Error ? error.message : '存储不可用'}；原数据已保留`, 'persistence') }
  return result
}

export function interpretDeckSelection(snapshot: ReadableDeckCache, key: string, raw: string): string {
  const alias = snapshot.aliases[key]
  return alias?.raw === raw ? alias.resolved : raw
}
