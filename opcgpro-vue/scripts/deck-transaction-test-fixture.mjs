import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { idbModel } from './test-deck-cache-database.mjs'
const read = name => readFileSync(new URL('../src/l12/' + name, import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const moduleUrl = source => 'data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
}).outputText).toString('base64')
export const databaseUrl = moduleUrl(read('deckCacheDatabase.ts'))
export const draftUrl = moduleUrl(read('site/deckEditorDraft.ts')
  .replace("from '../deckCacheDatabase'", `from '${databaseUrl}'`)
  .replace("import { deploymentBasePath } from '../deploymentBase'", "const deploymentBasePath = '/'"))
export const database = (await import(databaseUrl)).deckCacheDatabase
export const transactionModel = idbModel()
globalThis.indexedDB = transactionModel.factory
function blank(owner) { return { schema: 1, environment: '/', owner, revision: 0, cacheRaw: null, activity: '', selections: {}, seeded: false, drafts: {}, imports: [] } }
function key(owner) { return JSON.stringify(['/', owner]) }
function cacheOwner(name) { const match = /^l12-custom-decks-v[12](?::(.+))?$/.exec(name); return match ? match[1] ? 'account:' + match[1] : 'guest' : null }
function selectionOwner(name) {
  if (!name.startsWith('l12-selected-custom-deck')) return null
  const suffix = name.slice('l12-selected-custom-deck'.length).replace(/^:/, '')
  const scopes = ['ranked', 'casual', 'friendly', 'sandbox-player', 'sandbox-opponent']
  const parts = suffix.split(':'); if (scopes.includes(parts.at(-1))) parts.pop()
  return parts.join(':') ? 'account:' + parts.join(':') : 'guest'
}
// These old user-scenario suites seed logical durable records using their
// historical fixture names. This is NOT a browser localStorage implementation
// or an automatic legacy importer: the independent new-protocol suite uses real
// separate legacy source ports and explicitly proves no automatic adoption.
export class TransactionFixtureStorage {
  values = new Map(); rejectWrite = null; writes = 0
  clear() { this.values.clear(); this.rejectWrite = null; this.writes = 0; transactionModel.persisted.clear() }
  snapshot() { return structuredClone([...this.values.entries(), ['__transactionRecords', [...transactionModel.persisted.entries()]]]) }
  get length() { return this.values.size }
  key(index) { return [...this.values.keys()][index] ?? null }
  getItem(name) {
    const owner = cacheOwner(name), selectedOwner = selectionOwner(name)
    if (owner) return transactionModel.persisted.get(key(owner))?.cacheRaw ?? this.values.get(name) ?? null
    if (selectedOwner) return transactionModel.persisted.get(key(selectedOwner))?.selections[name] ?? this.values.get(name) ?? null
    return this.values.get(name) ?? null
  }
  setItem(name, value) {
    if (this.rejectWrite?.(name, String(value))) throw Error('QuotaExceededError: focused transaction rejection')
    this.values.set(name, String(value))
    this.writes++
    const owner = cacheOwner(name), selectedOwner = selectionOwner(name)
    if (owner || selectedOwner) {
      const current = structuredClone(transactionModel.persisted.get(key(owner ?? selectedOwner)) ?? blank(owner ?? selectedOwner))
      if (owner) current.cacheRaw = String(value)
      else current.selections[name] = String(value)
      transactionModel.persisted.set(key(current.owner), current)
    }
  }
  removeItem(name) { this.values.delete(name) }
}
// Synchronous presentation readers in the historical suites always see their
// fixture's latest committed record. IDB/CAS correctness is tested with separate
// clients/independent views in the adapter and new-protocol suites.
database.snapshot = scope => structuredClone(transactionModel.persisted.get(JSON.stringify([scope.environment, scope.owner])) ?? blank(scope.owner))
globalThis.sessionStorage ??= new TransactionFixtureStorage()
export function bindTransactionFaults(storage) {
  transactionModel.setWriteGuard((_key, state, previous) => {
    const account = state.owner === 'guest' ? '' : ':' + state.owner.slice('account:'.length)
    const cacheKey = 'l12-custom-decks-v1' + account
    const activityKey = 'l12-deck-cache-activity-v1:' + (state.owner === 'guest' ? 'guest' : state.owner.slice('account:'.length))
    if (state.cacheRaw !== previous?.cacheRaw && storage.rejectWrite?.(cacheKey, state.cacheRaw)) throw Error('本机缓存写入失败：QuotaExceededError')
    if (state.activity !== (previous?.activity ?? '') && storage.rejectWrite?.(activityKey, state.activity)) throw Error('QuotaExceededError')
    for (const [name, value] of Object.entries(state.selections)) if (value !== previous?.selections[name] && storage.rejectWrite?.(name, value)) throw Error('QuotaExceededError')
  })
}
