import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { createRequire } from 'node:module'
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const ts = createRequire(import.meta.url)('typescript')
function functions(relative, names, dependencies, controls = '') {
  const raw = fs.readFileSync(path.join(root, relative), 'utf8')
  const source = raw.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)?.[1] ?? raw
  const file = ts.createSourceFile(relative, source, ts.ScriptTarget.Latest, true)
  const selected = names.map(name => {
    const node = file.statements.find(item => ts.isFunctionDeclaration(item) && item.name?.text === name)
    assert.ok(node, `${relative}: ${name}`)
    return node.getText(file).replace(/^export\s+/, '')
  })
  const js = ts.transpileModule(selected.join('\n'), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(dependencies), `${js}\nreturn {${names.join(',')}${controls ? ',' + controls : ''}}`)(...Object.values(dependencies))
}
let passed = 0
async function check(name, work) { await work(); passed++; console.log(`PASS ${passed}: ${name}`) }
function deferred() { let resolve; const promise = new Promise(yes => { resolve = yes }); return { promise, resolve } }
function runtime(options = {}) {
  const state = { calls: [], bodyCalls: [], current: true, accountId: 'metadata-owner', cache: {},
    query: async query => ({ items: [], total: 0, page: 1, pageSize: query.pageSize }), ...options }
  const selected = functions('src/l12/decks.ts', ['uniqueDeckCopyName', 'loadPrivatePublicationSource'], {
    captureDeckStorageContext: () => ({ accountId: state.accountId, token: state.accountId ? 'synthetic-only' : '' }),
    assertCompleteDeckAccount: () => undefined, isCurrentDeckStorageContext: () => state.current,
    operationError: error => error, currentDeckSnapshot: () => ({ decks: state.cache }),
    sameDeckName: (left, right) => left.trim().toLowerCase() === right.trim().toLowerCase(),
    loadPrivateDeckSummaryPage: async query => { state.calls.push(query); return state.query(query) },
    loadPrivateDeckBody: async (target, current) => { assert.equal(current(), true); state.bodyCalls.push(target); return { ...target, cardIds: ['REAL-BODY'] } },
  })
  return { ...selected, state }
}
await check('uncached cloud name is checked exactly, then a bounded free suffix is used', async () => {
  const r = runtime({ query: async query => ({ items: [], total: query.exactName === '未缓存的同名牌库' ? 1 : 0 }) })
  assert.equal(await r.uniqueDeckCopyName('未缓存的同名牌库'), '未缓存的同名牌库 2')
  assert.deepEqual(r.state.calls, [{ exactName: '未缓存的同名牌库', page: 1, pageSize: 1 }, { exactName: '未缓存的同名牌库 2', page: 1, pageSize: 1 }])
  assert.equal(r.state.bodyCalls.length, 0)
})
await check('guest naming keeps local names and no remote read', async () => {
  const r = runtime({ accountId: undefined, cache: { a: { name: 'BASE' }, b: { name: 'base 2' } } })
  assert.equal(await r.uniqueDeckCopyName('base'), 'base 3'); assert.equal(r.state.calls.length, 0)
})
await check('long names always leave room for the numeric suffix', async () => {
  const r = runtime({ query: async query => ({ total: query.exactName.endsWith(' 2') ? 0 : 1 }) })
  const value = await r.uniqueDeckCopyName('长'.repeat(50)); assert.equal(value.length, 24); assert.ok(value.endsWith(' 2'))
})
await check('ambiguous same-name metadata is never treated as available', async () => {
  const r = runtime({ query: async () => ({ total: 2, items: [] }) })
  await assert.rejects(r.uniqueDeckCopyName('same'), /多副同名/); assert.equal(r.state.calls.length, 1)
})
await check('naming has a finite lookup budget and does not iterate directory pages', async () => {
  const r = runtime({ query: async () => ({ total: 1, items: [] }) })
  await assert.rejects(r.uniqueDeckCopyName('same'), /同名牌库较多/)
  assert.equal(r.state.calls.length, 32); assert.ok(r.state.calls.every(query => query.page === 1 && query.pageSize === 1))
})
await check('failed directory lookup is never converted to an unused name', async () => {
  const r = runtime({ query: async () => { throw new Error('HTTP 503') } })
  await assert.rejects(r.uniqueDeckCopyName('same'), /503/); assert.equal(r.state.calls.length, 1)
})
for (const actor of [false, true]) await check(`name lookup rejects late ${actor ? 'account' : 'page'} invalidation`, async () => {
  const wait = deferred(), r = runtime({ query: () => wait.promise }); let current = true
  const result = r.uniqueDeckCopyName('same', () => current)
  if (actor) r.state.current = false; else current = false
  wait.resolve({ total: 0, items: [] }); await assert.rejects(result, /切换/)
})
await check('empty public source metadata is an authoritative absence, without a body read', async () => {
  const r = runtime(); assert.equal(await r.loadPrivatePublicationSource('pub-1'), null)
  assert.deepEqual(r.state.calls, [{ publicationId: 'pub-1', page: 1, pageSize: 2 }]); assert.equal(r.state.bodyCalls.length, 0)
})
await check('renamed private source is selected by publication identity, not a cached or public name', async () => {
  const source = { id: 'renamed-source', revision: 17, name: '全新名称', publicationId: 'pub-1' }
  const r = runtime({ query: async () => ({ total: 1, items: [source] }) })
  assert.deepEqual((await r.loadPrivatePublicationSource('pub-1')).cardIds, ['REAL-BODY'])
  assert.deepEqual(r.state.bodyCalls, [source]); assert.equal(r.state.calls.length, 1)
})
await check('multiple publication sources require explicit user selection, never first-row overwrite', async () => {
  const r = runtime({ query: async () => ({ total: 3, items: [{ id: 'first' }, { id: 'second' }] }) })
  await assert.rejects(r.loadPrivatePublicationSource('pub'), /从我的牌库选择/); assert.equal(r.state.bodyCalls.length, 0)
})
await check('source count/body mismatch and stale page reject before any body/cache action', async () => {
  const r = runtime({ query: async () => ({ total: 1, items: [] }) })
  await assert.rejects(r.loadPrivatePublicationSource('pub'), /暂不可读取/)
  const wait = deferred(), late = runtime({ query: () => wait.promise }); const result = late.loadPrivatePublicationSource('pub')
  late.state.current = false; wait.resolve({ total: 1, items: [{ id: 'old', revision: 1 }] })
  await assert.rejects(result, /切换/); assert.equal(late.state.bodyCalls.length, 0)
})
const queryFunctions = functions('src/l12/decks.ts', ['privateDeckObject', 'privateDeckExactFields', 'privateDeckInteger',
  'privateDeckText', 'privateDeckContractError', 'normalizePrivateDeckSummaryQuery', 'privateDeckSummaryRequestPath', 'validatePrivateDeckSummaryPage'], {})
await check('wire query encodes exact name/source and rejects malformed explicit filters', () => {
  const url = new URL(queryFunctions.privateDeckSummaryRequestPath({ pageSize: 1, exactName: ' A & B ', publicationId: ' pub-1 ' }), 'http://synthetic.invalid')
  assert.equal(url.searchParams.get('exactName'), 'A & B'); assert.equal(url.searchParams.get('publicationId'), 'pub-1')
  for (const query of [{ exactName: '' }, { exactName: 9 }, { exactName: 'a'.repeat(25) }, { publicationId: '' }, { publicationId: 'x'.repeat(129) }])
    assert.throws(() => queryFunctions.privateDeckSummaryRequestPath(query))
})
await check('metadata response must satisfy the explicit exact-name and source filters', () => {
  const item = { id: 'a', revision: 1, name: 'ALPHA', masterId: 'M', updatedAt: '2026-10-07T00:00:00Z', publicationId: 'pub', publicationVersion: 1,
    counts: { main: 40, uncountedMain: 0, morale: 8, special: 0, bench: 0 }, legal: true, legalityReason: null }
  const page = { items: [item], total: 1, page: 1, pageSize: 30, generation: 1, permissionVersion: 1, catalogVersion: 'A'.repeat(64), policyVersion: 1,
    facets: { masters: [{ masterId: 'M', count: 1 }], legal: 1, illegal: 0 } }
  assert.equal(queryFunctions.validatePrivateDeckSummaryPage(page, { exactName: 'alpha', publicationId: 'pub' }).items.length, 1)
  assert.throws(() => queryFunctions.validatePrivateDeckSummaryPage(page, { exactName: 'other' }))
  assert.throws(() => queryFunctions.validatePrivateDeckSummaryPage(page, { publicationId: 'other' }))
})
for (const [relative, name] of [['src/l12/site/PublicDeckDetailPage.vue', 'uniqueName'], ['src/l12/site/DeckLibraryPage.vue', 'uniqueName'], ['src/l12/site/DeckSnapshotViewer.vue', 'uniqueDeckName']])
  await check(`${relative}: actual naming wrapper delegates the guard to the shared authoritative lookup`, async () => {
    const r = runtime({ query: async query => ({ items: [], total: query.exactName === '存在' ? 1 : 0 }) })
    const wrapper = functions(relative, [name], { uniqueDeckCopyName: r.uniqueDeckCopyName })
    assert.equal(await wrapper[name]('存在', () => true), '存在 2')
  })

function importRuntime() {
  const state = { current: true, saved: [], closeCount: 0, directoryReads: 0 }
  const refs = Object.fromEntries(['importCode', 'importError', 'showImport', 'importBusy', 'saved', 'minePage', 'notice'].map(name => [name, { value: '' }]))
  Object.assign(refs.importCode, { value: 'SYNTHETIC-CODE' }); refs.showImport.value = true; refs.importBusy.value = false
  const wait = deferred()
  const selected = functions('src/l12/site/DeckLibraryPage.vue', ['importFromCode', 'closeImportModal'], {
    ...refs, libraryDisposed: false, importRequestSequence: 0, libraryCounterDocumentEpoch: 0,
    libraryContext: () => ({}), libraryContextCurrent: () => state.current,
    decodeDeckCode: () => ({ name: 'BASE', masterId: 'M', cardIds: ['CARD'] }),
    uniqueName: async (_name, current) => { await wait.promise; if (!current()) throw new Error('stale import'); return 'BASE 2' },
    validateDeck: () => '', catalog: { value: [] },
    saveDeck: async deck => { state.saved.push(deck); return deck }, loadSavedDecks: () => ({}),
    loadMineDirectory: async () => { state.directoryReads++ }, deckErrorBelongsToCurrentAccount: () => true,
    nextTick: async callback => { state.closeCount++; callback() }, importTrigger: { value: null },
  }, 'invalidateDocument:()=>{libraryCounterDocumentEpoch++}')
  return { ...selected, refs, state, wait }
}
await check('real import consumer cannot submit twice while exact-name lookup is pending', async () => {
  const r = importRuntime(), pending = r.importFromCode(); await r.importFromCode()
  r.wait.resolve(); await pending
  assert.equal(r.state.saved.length, 1); assert.equal(r.state.saved[0].name, 'BASE 2')
  assert.equal(r.refs.showImport.value, false); assert.equal(r.refs.importBusy.value, false)
  assert.equal(r.state.directoryReads, 1)
})
for (const [name, cancel] of [['close and reopen', r => { r.closeImportModal(); r.refs.showImport.value = true; r.refs.importCode.value = 'SYNTHETIC-CODE' }],
  ['changed input', r => { r.refs.importCode.value = 'NEW-CODE' }], ['changed account/page', r => { r.state.current = false }],
  ['route A-B-A with unchanged inputs and identity', r => { r.invalidateDocument(); r.invalidateDocument() }]])
  await check(`real import consumer rejects ${name} during lookup before any save`, async () => {
    const r = importRuntime(), pending = r.importFromCode(); cancel(r); r.wait.resolve(); await pending
    assert.equal(r.state.saved.length, 0); assert.equal(r.state.directoryReads, 0)
    assert.equal(r.refs.importError.value, ''); assert.equal(r.refs.importBusy.value, false)
  })
console.log(`Private metadata consumers: ${passed}/${passed}, failed=0, skipped=0`)
