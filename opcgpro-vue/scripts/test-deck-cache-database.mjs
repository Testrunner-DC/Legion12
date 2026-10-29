import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/deckCacheDatabase.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText
const { createDeckCacheDatabase, deckDatabaseScope } = await import('data:text/javascript;base64,' + Buffer.from(code).toString('base64'))
// Deterministic IDB event/transaction model: independent clients, serialized
// transactions, private write sets, and completion/abort controlled separately.
// This is not evidence of a native browser implementation.
export function idbModel() {
  const persisted = new Map(), queue = []
  let active = false, failCommit = false, blocked = false, pauseCommit = null, pauseWriteCommit = null, writeGuard = null
  function pump() { if (!active && queue.length) { active = true; queue.shift()() } }
  const db = {
    objectStoreNames: { contains: () => true }, close() {},
    transaction(_store, mode) {
      const tx = { aborted: false, abort() {
        if (tx.aborted) return
        tx.aborted = true
        queueMicrotask(() => { tx.onabort?.(); active = false; pump() })
      } }
      let request, key, pending
      tx.objectStore = () => ({
        get(value) { key = value; request = {}; queue.push(() => queueMicrotask(() => {
          if (tx.aborted) return
          request.result = structuredClone(persisted.get(key))
          request.onsuccess?.()
          const complete = () => { if (tx.aborted) return
            if (failCommit) { failCommit = false; tx.abort(); return }
            if (mode === 'readwrite') persisted.set(key, structuredClone(pending))
            tx.oncomplete?.(); active = false; pump()
          }
          if (mode === 'readwrite' && pauseWriteCommit) { const pause = pauseWriteCommit; pauseWriteCommit = null; pause(complete) }
          else if (pauseCommit) { const pause = pauseCommit; pauseCommit = null; pause(complete) }
          else queueMicrotask(complete)
        })); pump(); return request },
        put(value) { writeGuard?.(key, value, persisted.get(key)); pending = structuredClone(value); return {} },
      })
      return tx
    },
  }
  return { persisted, get transactionActive() { return active }, factory: { open() { const request = {}; queueMicrotask(() => {
    if (blocked) request.onblocked?.()
    else { request.result = db; request.onsuccess?.() }
  }); return request } }, failNextCommit() { failCommit = true }, blockOpen() { blocked = true },
  pauseNextCommit(callback) { pauseCommit = callback }, pauseNextWriteCommit(callback) { pauseWriteCommit = callback }, setWriteGuard(callback) { writeGuard = callback } }
}
const model = idbModel(), first = createDeckCacheDatabase(() => model.factory), second = createDeckCacheDatabase(() => model.factory)
const formalA = deckDatabaseScope('/', 'account:a'), testA = deckDatabaseScope('/testrun/', 'account:a'), formalB = deckDatabaseScope('/', 'account:b')
const initial = await first.read(formalA)
assert.equal(initial.revision, 0)
await first.update(formalA, 0, state => { state.activity = 'first' })
assert.equal((await first.read(testA)).activity, '')
assert.equal((await first.read(formalB)).activity, '')
const races = await Promise.allSettled([
  first.update(formalA, 1, state => { state.activity = 'winner' }),
  second.update(formalA, 1, state => { state.activity = 'loser' }),
])
assert.equal(races.filter(item => item.status === 'fulfilled').length, 1)
assert.equal(races.find(item => item.status === 'rejected').reason.kind, 'conflict')
assert.equal((await second.read(formalA)).activity, 'winner')
let releaseCommit
model.pauseNextCommit(complete => { releaseCommit = complete })
let reportedSuccess = false
const pending = first.update(formalA, 2, state => { state.activity = 'not-yet-committed' }).then(() => { reportedSuccess = true })
while (!releaseCommit) await new Promise(resolve => setImmediate(resolve))
assert.equal(reportedSuccess, false)
assert.equal(first.snapshot(formalA).activity, 'winner')
releaseCommit(); await pending
assert.equal(reportedSuccess, true)
model.failNextCommit()
await assert.rejects(first.update(formalA, 3, state => { state.activity = 'aborted' }), error => error.kind === 'persistence')
assert.equal((await second.read(formalA)).activity, 'not-yet-committed')
await assert.rejects(first.update(formalA, 3, async () => 'network'), /事务内禁止异步/)
await assert.rejects(first.update(formalA, 3, () => {}, () => { throw new Error('account switched') }), /account switched/)
assert.equal((await second.read(formalA)).revision, 3)
const denied = createDeckCacheDatabase(() => { throw new Error('policy') })
await assert.rejects(denied.read(formalA), /浏览器无法打开/)
assert.equal(denied.ready(formalA), false)
const blockedModel = idbModel(); blockedModel.blockOpen()
await assert.rejects(createDeckCacheDatabase(() => blockedModel.factory).read(formalA), /阻塞/)
const imported = { id: 'old:sha', source: 'cache', sourceKey: 'l12-custom-decks-v1:a', raw: '{"old":true}', fingerprint: 'a'.repeat(64), provenance: 'unknown' }
await first.update(formalA, 3, state => { state.imports.push(imported); state.drafts.a = 'old draft'; state.drafts.b = 'other tab' })
assert.deepEqual((await second.read(formalA)).imports, [imported])
assert.deepEqual((await second.read(formalA)).drafts, { a: 'old draft', b: 'other tab' })
assert.equal((await first.read(testA)).imports.length, 0)
const validRecord = structuredClone(model.persisted.get(JSON.stringify(['/', 'account:a'])))
for (const corrupt of [
  state => { state.selections = [] }, state => { state.selections = new Date(0) }, state => { state.selections = new Map() },
  state => { state.drafts = [] }, state => { state.drafts = new Map() }, state => { state.imports = [[]] },
  state => { state.imports = new Array(1) }, state => { state.imports[0].raw = null },
  state => { state.imports[0].fingerprint = 'bad' }, state => { state.unknown = true },
]) {
  const broken = structuredClone(validRecord); corrupt(broken)
  model.persisted.set(JSON.stringify(['/', 'account:a']), broken)
  await assert.rejects(second.read(formalA), /格式或归属/)
  await assert.rejects(second.update(formalA, null, state => { state.activity = 'must-not-repair' }), /格式或归属/)
  assert.deepEqual(model.persisted.get(JSON.stringify(['/', 'account:a'])), broken, 'Corruption must not trigger repair or write')
}
model.persisted.set(JSON.stringify(['/', 'account:a']), validRecord)
console.log('Deck IndexedDB adapter: completion, abort, concurrent CAS, account/environment isolation, denied/blocked, legacy preservation and separate drafts passed (deterministic IDB model; native browser pending).')
