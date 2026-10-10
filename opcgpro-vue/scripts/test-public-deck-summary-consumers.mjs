import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const borrowed = process.env.L12_SUMMARY_DEPENDENCY_ROOT || root
const requireDependency = createRequire(path.join(borrowed, 'package.json'))
const ts = requireDependency('typescript')
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8')
function evaluate(source, dependencies = {}) {
  const code = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
  const exports = {}
  new Function('exports', 'require', ...Object.keys(dependencies), code)(exports,
    name => dependencies[name] ?? requireDependency(name), ...Object.values(dependencies))
  return exports
}
function productionFunction(name) {
  const source = read('src/l12/site/DeckLibraryPage.vue').match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1]
  const file = ts.createSourceFile('page.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  assert.equal(file.parseDiagnostics.length, 0)
  return file.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name).getText(file)
}
function executeFunction(name, dependencies) {
  const code = ts.transpileModule(productionFunction(name), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
  return new Function(...Object.keys(dependencies), code + `;return ${name};`)(...Object.values(dependencies))
}
function pending() { let resolve, reject; const promise = new Promise((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }

if (process.env.L12_SUMMARY_BASELINE_ONLY !== '1') {
  const summary = evaluate(read('src/l12/site/publicDeckSummary.ts'))
  const official = evaluate(read('src/l12/site/officialDeckReference.ts'))
  const entryHelper = evaluate(read('src/l12/site/publicDeckEntry.ts'))
  let count = 0
  async function check(label, run) { await run(); count++; console.log(`PASS ${count}: ${label}`) }
  const item = (id = 'public', source = 'public') => ({ id, source, name: 'name', masterId: 'master', masterName: '主宰', faction: 'fate', author: 'author',
    publicCode: source === 'public' ? 'CODE12' : null, publicationVersion: null, createdAt: null, updatedAt: null,
    counts: { main: 40, uncountedMain: 1, morale: 10, special: 0, bench: 0 }, legal: true, legalityReason: null,
    environment: { status: 'configured', value: '2.5', reason: null }, views: 0, likes: 0, copies: 0, viewerLiked: false, canEdit: false,
    readToken: source === 'public' ? 'b'.repeat(64) : null })
  const page = (items = [item()], total = 90) => ({ items, total, page: 1, pageSize: 30, generation: 'A'.repeat(64), catalogVersion: 'C'.repeat(64), policyVersion: 1,
    sourceAvailability: { public: 'available', official: 'available' }, facets: { sources: [{ value: 'public', count: total }], masters: [{ value: 'outside-page', count: total }],
      factions: [], environments: [], cards: [{ value: 'outside-card', count: total }], legal: total, illegal: 0 } })
  await check('page preserves full total/facets outside visible page without bodies', () => {
    const value = summary.validateSummaryPage(page(), {}); assert.equal(value.total, 90); assert.equal(value.items.length, 1)
    assert.equal(value.facets.masters[0].value, 'outside-page'); assert.equal(value.facets.cards[0].value, 'outside-card')
  })
  await check('available empty page is distinct from unavailable response', () => {
    assert.equal(summary.validateSummaryPage(page([], 0), {}).total, 0)
    assert.throws(() => summary.validateSummaryPage({ status: 'unavailable', items: [] }, {}))
  })
  await check('source disabled still has official metadata and explicit null content pin', () => {
    const value = page([item('official:' + 'a'.repeat(64), 'official')], 1); value.sourceAvailability.public = 'disabled'
    assert.equal(summary.validateSummaryPage(value, {}).items[0].readToken, null)
  })
  for (const [label, mutate] of [['body injection', value => { value.items[0].deck = {} }], ['generation as pin', value => { value.items[0].readToken = value.generation }],
    ['owner leak', value => { value.items[0].publicationVersion = 1 }], ['wrong page', value => { value.page = 2 }],
    ['duplicate id', value => { value.items.push(value.items[0]) }], ['empty counts', value => { value.items[0].counts = {} }],
    ['missing main', value => { delete value.items[0].counts.main }], ['missing morale', value => { delete value.items[0].counts.morale }],
    ['missing author', value => { delete value.items[0].author }], ['wrong name type', value => { value.items[0].name = 1 }],
    ['missing environment', value => { delete value.items[0].environment }], ['negative bench', value => { value.items[0].counts.bench = -1 }]]) await check(`reject ${label}`, () => {
      const value = page(); mutate(value); assert.throws(() => summary.validateSummaryPage(value, {}))
    })
  await check('mixed official and public references retain source separation', () => {
    const value = page([item(), item('official:' + 'a'.repeat(64), 'official')], 2)
    summary.validateSummaryPage(value, {}); assert.equal(summary.summaryRouteReference(value.items[0]), 'CODE12')
    assert.equal(summary.summaryRouteReference(value.items[1]), value.items[1].id)
  })
  for (const transition of ['request reorder', 'account ABA', 'route ABA', 'query change', 'unmount']) await check(`request guard ${transition}`, () => {
    let epoch = 0, query = 'q', route = 'A', owner = 'A'
    const gate = summary.createSummaryRequestGate(() => JSON.stringify({ owner, route, query, epoch })), first = gate.begin()
    if (transition === 'request reorder') gate.begin()
    if (transition === 'account ABA') { owner = 'B'; epoch++; owner = 'A'; epoch++ }
    if (transition === 'route ABA') { route = 'B'; epoch++; route = 'A'; epoch++ }
    if (transition === 'query change') query = 'new'
    if (transition === 'unmount') gate.dispose()
    assert.equal(first(), false)
  })
  const own = { id: 'outside-page', publicCode: 'CODE12', publicationVersion: 3, ownerId: 'owner' }
  await check('owner reference proof works outside the current summary page', () => {
    const rows = summary.validateOwnReferences({ status: 'available', items: [own] }, ['outside-page'], 'owner')
    assert.equal(summary.matchesOwnPublication({ publicationId: 'outside-page', publicationVersion: 3 }, rows[0], 'owner'), true)
  })
  await check('available missing reference is authoritative only for requested ids', () => {
    assert.deepEqual(summary.validateOwnReferences({ status: 'available', items: [] }, ['missing'], 'owner'), [])
    assert.equal(summary.matchesOwnPublication({ publicationId: 'missing', publicationVersion: 3 }, undefined, 'owner'), false)
    assert.throws(() => summary.validateOwnReferences({ status: 'unavailable', items: [] }, ['missing'], 'owner'))
  })
  await check('foreign, unrequested and wrong-version proofs fail closed', () => {
    assert.throws(() => summary.validateOwnReferences({ status: 'available', items: [{ ...own, ownerId: 'other' }] }, ['outside-page'], 'owner'))
    assert.throws(() => summary.validateOwnReferences({ status: 'available', items: [own] }, ['different'], 'owner'))
    assert.equal(summary.matchesOwnPublication({ publicationId: own.id, publicationVersion: 2 }, own, 'owner'), false)
  })
  await check('reference result retains request order without duplicate identities', () => {
    assert.throws(() => summary.validateOwnReferences({ status: 'available', items: [own, own] }, [own.id], 'owner'))
    assert.throws(() => summary.validateOwnReferences({ status: 'available', items: [{ ...own, id: 'B' }, own] }, [own.id, 'B'], 'owner'))
  })
  await check('scalar merge retains summary counts/environment/pin and changes no body field', () => {
    const initial = item(), merged = entryHelper.mergePublicDeckCounters(initial, { id: initial.id, publicCode: initial.publicCode, views: 1, likes: 2, copies: 3, viewerLiked: true, canEdit: false })
    assert.strictEqual(merged.counts, initial.counts); assert.strictEqual(merged.environment, initial.environment)
    assert.equal(merged.readToken, initial.readToken); assert.equal(Object.hasOwn(merged, 'deck'), false)
  })
  const presets = [{ name: '中文 零\0空格 ', masterId: 'M', cardIds: ['A'], moraleIds: [] }, { name: 'other', masterId: 'N', cardIds: [], moraleIds: [] }]
  const id = await official.officialDeckId(presets[0])
  await check('stable Unicode identity matches raw name/master and survives reordering', async () => {
    assert.strictEqual(await official.resolveOfficialDeck(id, async () => [...presets].reverse()), presets[0])
    assert.notEqual(id, await official.officialDeckId({ ...presets[0], name: presets[0].name.trim() }))
    assert.notEqual(id, await official.officialDeckId({ ...presets[0], masterId: 'different' }))
  })
  await check('official missing/duplicate/wrong hash reject without neighboring fallback', async () => {
    await assert.rejects(official.resolveOfficialDeck(id, async () => [presets[1]]))
    await assert.rejects(official.resolveOfficialDeck(id, async () => [presets[0], presets[0]]))
    await assert.rejects(official.resolveOfficialDeck('official:' + '0'.repeat(64), async () => presets))
  })
  await check('existing official index route still selects exact local entry', async () => {
    assert.strictEqual(await official.resolveOfficialDeck('official-1', async () => presets), presets[1])
    await assert.rejects(official.resolveOfficialDeck('official-9', async () => presets))
  })
  await check('summary handoff carries content pin and actor guard without treating generation as authority', () => {
    let epoch = 1; const captured = epoch, value = item(), intent = summary.rememberSummaryOpen(value, () => epoch === captured)
    assert.equal(intent.query.expectedReadToken, value.readToken); assert.equal(Object.values(intent.query).includes('A'.repeat(64)), false)
    assert.equal(summary.summaryOpenActorCurrent(intent.query.summaryOpen, 'CODE12', value.readToken), true)
    epoch += 2; assert.equal(summary.summaryOpenActorCurrent(intent.query.summaryOpen, 'CODE12', value.readToken), false)
  })
  await check('official handoff does not carry public pin or community reference', () => {
    const intent = summary.rememberSummaryOpen(item(id, 'official'), () => true)
    assert.equal(intent.reference, id); assert.deepEqual(intent.query, {})
  })
  function consumers() {
    const vue = requireDependency('vue'), input = { source: 'all', page: 1, pageSize: 30, keyword: 'selected', masterId: 'master', sort: 'name' }
    const rows = { published: vue.ref([]), summaryPage: vue.ref(null), facetPage: vue.ref(null), hotSummaries: vue.ref([]),
      publicLoadState: vue.ref('loading'), publicLoadError: vue.ref(''), plazaPage: vue.ref(1), ownReferences: vue.ref([]),
      referenceState: vue.ref('idle'), referenceError: vue.ref('') }
    let epoch = 0, account = 'owner', route = '/decks', ids = ['outside-page']; const requests = [], referenceRequests = []
    const summaryGate = summary.createSummaryRequestGate(() => JSON.stringify({ epoch, account, route, input }))
    const referenceGate = summary.createSummaryRequestGate(() => JSON.stringify({ epoch, account, route, ids }))
    const platformState = { get account() { return account ? { id: account } : null }, token: 'token' }
    const dependencies = { ...rows, libraryMounted: true, libraryDisposed: false, summaryQuery: () => ({ ...input, page: rows.plazaPage.value }),
      summaryGate, referenceGate, PAGE_SIZE: 30, platformState, visiblePublicationIds: () => ids,
      validateSummaryPage: summary.validateSummaryPage, validateOwnReferences: summary.validateOwnReferences,
      deckLibraryApi: { summaries: query => { const row = { query, ...pending() }; requests.push(row); return row.promise },
        ownReferences: publicationIds => { const row = { ids: publicationIds, ...pending() }; referenceRequests.push(row); return row.promise } },
    }
    return { ...rows, input, requests, referenceRequests, load: executeFunction('loadSummarySources', dependencies), refs: executeFunction('loadOwnReferences', dependencies),
      setActor: value => { account = value; epoch++ }, setRoute: value => { route = value; epoch++ },
      dispose: () => { summaryGate.dispose(); referenceGate.dispose() },
      respond: (index, value) => { const response = structuredClone(value); response.page = requests[index].query.page; response.pageSize = requests[index].query.pageSize; requests[index].resolve(response) } }
  }
  await check('actual consumer uses bounded filtered page plus global hot/facets request', async () => {
    const rows = consumers(), promise = rows.load(); assert.equal(rows.requests.length, 2)
    assert.equal(rows.requests[0].query.keyword, 'selected'); assert.equal(rows.requests[0].query.sort, 'name')
    assert.deepEqual(rows.requests[1].query, { source: 'all', page: 1, pageSize: 16, sort: 'trend' })
    rows.respond(0, page()); rows.respond(1, page()); await promise
    assert.equal(rows.summaryPage.value.total, 90); assert.equal(rows.published.value.length, 1)
    assert.equal(rows.facetPage.value.facets.cards[0].value, 'outside-card'); assert.equal(rows.hotSummaries.value.length, 1)
    assert.equal(rows.publicLoadState.value, 'available')
  })
  for (const transition of ['reorder', 'account ABA', 'route ABA', 'unmount']) await check(`actual page consumer drops ${transition}`, async () => {
    const rows = consumers(), first = rows.load()
    if (transition === 'reorder') {
      const second = rows.load(); rows.respond(2, page([item('new')])); rows.respond(3, page([item('new')])); await second
    }
    if (transition === 'account ABA') { rows.setActor('other'); rows.setActor('owner') }
    if (transition === 'route ABA') { rows.setRoute('/other'); rows.setRoute('/decks') }
    if (transition === 'unmount') rows.dispose()
    rows.respond(0, page()); rows.respond(1, page()); await first
    assert.equal(rows.summaryPage.value?.items[0].id, transition === 'reorder' ? 'new' : undefined)
  })
  await check('actual page rejects differing policy/catalog instead of mixing facets', async () => {
    const rows = consumers(), promise = rows.load(); rows.respond(0, page())
    const global = page(); global.policyVersion = 2; rows.respond(1, global); await promise
    assert.equal(rows.publicLoadState.value, 'unavailable'); assert.equal(rows.summaryPage.value, null)
    assert.match(rows.publicLoadError.value, /规则正在变化/)
  })
  await check('actual page load failure stays unavailable rather than authoritative empty', async () => {
    const rows = consumers(), promise = rows.load(); rows.requests[0].reject(new Error('storage_unavailable')); rows.respond(1, page()); await promise
    assert.equal(rows.publicLoadState.value, 'unavailable'); assert.equal(rows.summaryPage.value, null)
  })
  await check('actual reference consumer preserves available missing vs failed lookup', async () => {
    const rows = consumers(), first = rows.refs(); rows.referenceRequests[0].resolve({ status: 'available', items: [] }); await first
    assert.equal(rows.referenceState.value, 'available')
    const second = rows.refs(); rows.referenceRequests[1].reject(new Error('feature_disabled')); await second
    assert.equal(rows.referenceState.value, 'unavailable'); assert.equal(rows.ownReferences.value.length, 0)
    assert.match(rows.referenceError.value, /feature_disabled/)
  })
  await check('actual reference consumer rejects actor ABA before committing owner result', async () => {
    const rows = consumers(), promise = rows.refs(); rows.setActor('other'); rows.setActor('owner')
    rows.referenceRequests[0].resolve({ status: 'available', items: [own] }); await promise
    assert.equal(rows.ownReferences.value.length, 0)
  })
  await check('plaza code never requests full community list or all local preset bodies', () => {
    const source = read('src/l12/site/DeckLibraryPage.vue')
    assert.equal(source.includes('publicDeckApi.list('), false); assert.equal(source.includes('loadOfficialPresetDecks('), false)
    assert.match(source, /publicLoadState === 'available' && !plazaTotal/)
    assert.equal(source.includes('deckEnvironmentForDeck(entry.deck)'), false)
  })
  await check('actual API encodes all filters and repeated publication references without body endpoints', async () => {
    const source = read('src/l12/platform.ts'), file = ts.createSourceFile('platform.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
    const initializer = file.statements.filter(ts.isVariableStatement).flatMap(node => node.declarationList.declarations)
      .find(node => node.name.getText(file) === 'deckLibraryApi').initializer.getText(file)
    const calls = [], api = evaluate('export const api=' + initializer, { platformRequest: (url, init) => { calls.push({ url, init }); return Promise.resolve({}) } }).api
    await api.summaries({ source: 'all', page: 2, pageSize: 30, legal: false, keyword: '中文 &', cardId: 'C', masterId: 'M', faction: 'fate',
      environment: '2.5', updatedAfter: '2026-10-07T00:00:00.000Z', sort: 'name' })
    const query = new URL(calls[0].url, 'http://owned.invalid').searchParams
    assert.equal(query.get('legal'), 'false'); assert.equal(query.get('keyword'), '中文 &'); assert.equal(query.get('page'), '2')
    assert.equal(query.get('updatedAfter'), '2026-10-07T00:00:00.000Z')
    await api.ownReferences(['A/B', '第二条']); assert.deepEqual(new URL(calls[1].url, 'http://owned.invalid').searchParams.getAll('publicationId'), ['A/B', '第二条'])
    await assert.rejects(api.ownReferences(['same', 'same'])); await assert.rejects(api.ownReferences([])); assert.equal(calls.length, 2)
  })
  await check('light owner proof preserves the legacy valid-id/version/owner matching semantics', () => {
    const saved = { publicationId: 'P', publicationVersion: 2 }
    for (const proof of [
      { id: 'P', publicCode: 'CODE12', publicationVersion: 2, ownerId: 'owner' },
      { id: 'OTHER', publicCode: 'CODE12', publicationVersion: 2, ownerId: 'owner' },
      { id: 'P', publicCode: 'CODE12', publicationVersion: 1, ownerId: 'owner' },
      { id: 'P', publicCode: 'CODE12', publicationVersion: 2, ownerId: 'foreign' },
    ]) {
      const legacy = { id: proof.id, ownerId: proof.ownerId,
        deck: { publicationId: proof.id, publicationVersion: proof.publicationVersion } }
      assert.equal(summary.matchesOwnPublication(saved, proof, 'owner'),
        entryHelper.matchesPublishedDeckReference(saved, legacy, 'owner'))
    }
    assert.equal(summary.matchesOwnPublication(saved, undefined, 'owner'), false)
    assert.equal(summary.matchesOwnPublication({ publicationId: null, publicationVersion: null }, undefined, 'owner'), false)
  })
  console.log(`C-U2 focused helper result: ${count}/${count}, failed=0, skipped=0`)
}

if (process.env.L12_SUMMARY_BASELINE_ONLY === '1') {
  const before = path.resolve(root, '../.tmp/c-u2-summary/before/opcgpro-vue')
  const helper = evaluate(fs.readFileSync(path.join(before, 'src/l12/site/publicDeckEntry.ts'), 'utf8'))
  const stable = 'official:' + 'a'.repeat(64)
  const actual = helper.publicDeckRouteReference({ id: stable, official: true, source: 'official', ownerId: 'official' })
  const library = fs.readFileSync(path.join(before, 'src/l12/site/DeckLibraryPage.vue'), 'utf8')
  const script = library.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)[1]
  const file = ts.createSourceFile('library.ts', script, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const mounted = file.statements.find(node => ts.isExpressionStatement(node) && ts.isCallExpression(node.expression)
    && node.expression.expression.getText(file) === 'onMounted').expression.arguments[0]
  const calls = []
  const visit = node => { if (ts.isCallExpression(node)) calls.push(node.expression.getText(file)); ts.forEachChild(node, visit) }
  visit(mounted)
  const proof = file.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'publishedCopyFor').getText(file)
  const oldProof = new Function('published', 'platformState', ts.transpileModule(proof, { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText + ';return publishedCopyFor;')
    ({ value: [] }, { account: { id: 'owner' } })({ publicationId: 'outside-page', publicationVersion: 1 })
  console.log(JSON.stringify({ actualOldStableRoute: actual, unboundedMountedList: calls.includes('publicDeckApi.list'),
    oldPageOnlyProofMissing: oldProof === undefined }))
  assert.equal(actual, stable, 'A stable official summary must have an exact usable local detail reference')
}
