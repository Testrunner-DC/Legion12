/** The only writable store for the new deck cache. Legacy Web Storage is an import source only. */
export interface DeckDatabaseScope { environment: string; owner: string }
export interface LegacyDeckImport {
  id: string
  source: 'cache' | 'draft'
  sourceKey: string
  raw: string
  fingerprint: string
  provenance: 'unknown'
  session?: string
}
export interface DeckDatabaseState {
  schema: 1
  environment: string
  owner: string
  revision: number
  cacheRaw: string | null
  activity: string
  selections: Record<string, string>
  seeded: boolean
  drafts: Record<string, string | null>
  imports: LegacyDeckImport[]
}
export class DeckDatabaseError extends Error {
  constructor(message: string, readonly kind: 'unavailable' | 'conflict' | 'persistence' = 'unavailable') { super(message) }
}
export function deckDatabaseScope(environment: string, owner: string): DeckDatabaseScope {
  if (!environment.startsWith('/') || !environment.endsWith('/') || environment.includes('..') || !owner)
    throw new DeckDatabaseError('牌库存储环境或归属无效')
  return { environment, owner }
}
const scopeKey = (scope: DeckDatabaseScope) => JSON.stringify([scope.environment, scope.owner])
const clone = <T>(value: T): T => structuredClone(value)
function empty(scope: DeckDatabaseScope): DeckDatabaseState {
  return { schema: 1, ...scope, revision: 0, cacheRaw: null, activity: '', selections: {}, seeded: false, drafts: {}, imports: [] }
}
function plainRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && Object.getPrototypeOf(value) === Object.prototype
}
function checked(value: unknown, scope: DeckDatabaseScope): DeckDatabaseState {
  if (value === undefined) return empty(scope)
  const item = value as DeckDatabaseState
  const fields = ['schema', 'environment', 'owner', 'revision', 'cacheRaw', 'activity', 'selections', 'seeded', 'drafts', 'imports']
  if (!plainRecord(item) || Reflect.ownKeys(item).length !== fields.length || fields.some(key => !Object.hasOwn(item, key))
    || item.schema !== 1 || item.environment !== scope.environment || item.owner !== scope.owner
    || !Number.isSafeInteger(item.revision) || item.revision < 0
    || item.cacheRaw !== null && typeof item.cacheRaw !== 'string' || typeof item.activity !== 'string'
    || typeof item.seeded !== 'boolean' || !plainRecord(item.selections) || !plainRecord(item.drafts)
    || !Array.isArray(item.imports) || Object.getPrototypeOf(item.imports) !== Array.prototype
    || Reflect.ownKeys(item.imports).length !== item.imports.length + 1
    || Object.values(item.selections).some(value => typeof value !== 'string')
    || Object.values(item.drafts).some(value => value !== null && typeof value !== 'string')
    || item.imports.some(value => !plainRecord(value) || !['cache', 'draft'].includes(value.source)
      || Reflect.ownKeys(value).length !== (Object.hasOwn(value, 'session') ? 7 : 6)
      || Object.hasOwn(value, 'session') && (typeof value.session !== 'string' || !value.session)
      || value.provenance !== 'unknown' || ['id', 'sourceKey', 'raw', 'fingerprint'].some(key => typeof Reflect.get(value, key) !== 'string')
      || !value.id || !value.sourceKey || !/^[a-f0-9]{64}$/.test(value.fingerprint)))
    throw new DeckDatabaseError('本机事务牌库格式或归属不正确；原数据已保留')
  return item
}

/** Injectable IDBFactory is a test seam, not an alternate storage implementation. */
export function createDeckCacheDatabase(resolve: () => IDBFactory = () => globalThis.indexedDB) {
  let opening: Promise<IDBDatabase> | null = null
  const snapshots = new Map<string, DeckDatabaseState>()
  function open() {
    if (opening) return opening
    opening = new Promise<IDBDatabase>((resolveOpen, reject) => {
      let request: IDBOpenDBRequest
      let settled = false
      const failed = (message: string) => { settled = true; reject(new DeckDatabaseError(message)) }
      try {
        const factory = resolve()
        if (!factory) throw new Error('IndexedDB不可用')
        request = factory.open('l12-deck-transactions', 1)
      } catch { failed('浏览器无法打开事务牌库；请允许网站存储，旧数据未改动'); return }
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains('scopes')) request.result.createObjectStore('scopes')
      }
      request.onerror = () => failed('事务牌库无法打开；旧数据已保留')
      request.onblocked = () => failed('事务牌库升级被其他页面阻塞；请更新相关页面后重试')
      request.onsuccess = () => {
        const db = request.result
        if (settled) { db.close(); return }
        settled = true
        db.onversionchange = () => { db.close(); opening = null; snapshots.clear() }
        resolveOpen(db)
      }
    })
    void opening.catch(() => { opening = null })
    return opening
  }
  async function transact<T>(scope: DeckDatabaseScope, mode: IDBTransactionMode,
    expectedRevision: number | null, mutate: (state: DeckDatabaseState) => T, guard: () => void) {
    const db = await open()
    return new Promise<{ state: DeckDatabaseState; result: T }>((resolveTransaction, reject) => {
      let transaction: IDBTransaction
      let final: DeckDatabaseState | undefined, result: T, failure: unknown
      try { guard() } catch (error) { reject(error); return }
      try { transaction = db.transaction('scopes', mode) }
      catch { reject(new DeckDatabaseError('事务牌库无法读取或写入')); return }
      const abort = (error: unknown) => { failure = error; try { transaction.abort() } catch { /* Already aborted. */ } }
      transaction.onabort = () => reject(failure ?? new DeckDatabaseError('本机牌库事务未完成；未报告保存成功', 'persistence'))
      transaction.onerror = () => { /* onabort owns the single failure result. */ }
      transaction.oncomplete = () => {
        if (!final) { reject(new DeckDatabaseError('本机牌库事务缺少结果')); return }
        snapshots.set(scopeKey(scope), clone(final))
        resolveTransaction({ state: clone(final), result })
      }
      let request: IDBRequest
      try { request = transaction.objectStore('scopes').get(scopeKey(scope)) }
      catch (error) { abort(error); return }
      request.onsuccess = () => {
        try {
          guard()
          const state = clone(checked(request.result, scope))
          if (expectedRevision !== null && state.revision !== expectedRevision)
            throw new DeckDatabaseError('牌库已被其他页面更新；当前修改保留，请重新载入或另存', 'conflict')
          result = mutate(state)
          if (result && typeof Reflect.get(Object(result), 'then') === 'function')
            throw new DeckDatabaseError('牌库事务内禁止异步或网络操作')
          guard()
          if (mode === 'readwrite') {
            state.revision++
            checked(state, scope)
            transaction.objectStore('scopes').put(state, scopeKey(scope))
          }
          final = state
        } catch (error) { abort(error) }
      }
    })
  }
  return {
    read: (scope: DeckDatabaseScope) => transact(scope, 'readonly', null, () => undefined, () => {}).then(value => value.state),
    update: <T>(scope: DeckDatabaseScope, expectedRevision: number | null,
      mutate: (state: DeckDatabaseState) => T, guard: () => void = () => {}) => transact(scope, 'readwrite', expectedRevision, mutate, guard),
    // This is a disposable presentation snapshot, never a compare-and-write authority.
    snapshot: (scope: DeckDatabaseScope) => { const value = snapshots.get(scopeKey(scope)); return value ? clone(value) : null },
    ready: (scope: DeckDatabaseScope) => snapshots.has(scopeKey(scope)),
  }
}
export const deckCacheDatabase = createDeckCacheDatabase()

export async function fingerprintLegacyDeckRaw(raw: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(raw))
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('')
}
