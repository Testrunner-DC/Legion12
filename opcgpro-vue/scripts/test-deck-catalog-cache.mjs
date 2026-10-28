import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/decks.ts', import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const tree = ts.createSourceFile('decks.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const declaration = name => tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
const state = tree.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(tree) === 'catalogPromise'))
const lookupState = tree.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(tree) === 'lookupCatalogRequest'))
const lookup = declaration('loadLookupCatalog')
const load = declaration('loadDeckCatalog')
const archive = declaration('loadCardArchiveCatalog')
assert(state && load && archive, '真实牌库目录加载函数缺失')
const loadText = load.getText(tree)
assert.match(loadText, /catalogPromise\s*===\s*\w+[\s\S]*catalogPromise\s*=\s*null/,
  '失败清理必须以当前 promise identity 为条件')

const support = `
const deploymentPath = value => value
const normalizeMoraleCatalogCard = value => value
const normalizeCardDimensions = value => value
const withProductInclusions = value => value
const productInclusionsByCardId = new Set(['S01-CARD', 'S01-VARIANT'])
const cardArchiveAssets = []
const lookupDeckCard = value => ({ id: value.cardNo, number: value.cardNo, cardNo: value.cardNo,
  nameZh: value.nameZh || value.cardNo, cardType: value.cardType || 'legion', faction: value.faction || 'fate',
  product: value.product || 'P', effect: value.effect || '' })
`
let moduleId = 0
const fresh = async (supportSource = support) => {
  const javascript = ts.transpileModule([supportSource, state.getText(tree), lookupState?.getText(tree) ?? '',
    lookup?.getText(tree) ?? '', loadText, archive.getText(tree)].join('\n'), {
    compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
  }).outputText
  return import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}#${++moduleId}`)
}
const response = (value, ok = true) => ({ ok, json: async () => value })
const card = { id: 'S01-CARD', cardNo: 'S01-CARD', number: 'S01-CARD', nameZh: '合成卡', cardType: 'legion', faction: 'fate', product: 'P', effect: '' }
const good = url => url.endsWith('cards.s1.json') ? response([card])
  : url.endsWith('cards.lookup.json') ? response([]) : response([])
const originalFetch = globalThis.fetch
const results = []
const deferred = () => {
  let resolve, reject
  const promise = new Promise((accept, fail) => { resolve = accept; reject = fail })
  return { promise, resolve, reject }
}
const flush = async () => { for (let index = 0; index < 8; index++) await Promise.resolve() }

async function retryCase(label, fail) {
  const module = await fresh()
  const calls = []
  globalThis.fetch = async input => {
    const url = String(input); calls.push(url)
    if (calls.length <= 3) return fail(url)
    return good(url)
  }
  await assert.rejects(module.loadDeckCatalog(), undefined, `${label}首次必须失败`)
  const loaded = await module.loadDeckCatalog()
  assert.deepEqual(loaded.map(item => item.id), ['S01-CARD'], `${label}失败后重试未恢复`)
  assert.equal(calls.length, 6, `${label}失败后必须重新发起完整三请求`)
  const beforeArchive = calls.length
  const archived = await module.loadCardArchiveCatalog()
  assert.deepEqual(archived.map(item => item.id), ['S01-CARD'])
  assert.equal(calls.filter(url => url.endsWith('cards.s1.json')).length, 2,
    `${label}图鉴不得绕过共享成功缓存重载S1`)
  assert.equal(calls.length, beforeArchive + 1, `${label}图鉴只可追加自身lookup读取`)
  results.push({ name: `${label}: full catalog retry and later archive refresh`, passed: true })
}

try {
  {
    const module = await fresh(); const calls = []
    globalThis.fetch = async input => { calls.push(String(input)); return good(String(input)) }
    const editor = module.loadDeckCatalog(); const shared = module.loadDeckCatalog()
    assert.strictEqual(editor, shared, '并发消费者必须取得同一promise')
    const first = await editor
    assert.equal(calls.length, 3, '并发消费者只能启动一组三请求')
    assert.strictEqual(await module.loadDeckCatalog(), first, '成功结果必须保持缓存')
    assert.equal(calls.length, 3, '成功重复读取不得fetch')
    results.push({ name: 'playable catalog keeps existing successful promise cache', passed: true })
  }

  await retryCase('网络拒绝', url => url.endsWith('cards.s1.json')
    ? Promise.reject(new Error('network rejected')) : good(url))
  await retryCase('非200', url => url.endsWith('cards.lookup.json') ? response([], false) : good(url))
  await retryCase('JSON失败', url => url.endsWith('cards.lookup.json')
    ? { ok: true, json: async () => { throw new Error('invalid json') } } : good(url))

  {
    const module = await fresh(); const calls = []
    globalThis.fetch = async input => { calls.push(String(input)); return good(String(input)) }
    const [editor, archiveCards] = await Promise.all([module.loadDeckCatalog(), module.loadCardArchiveCatalog()])
    assert.deepEqual(editor.map(item => item.id), ['S01-CARD'])
    assert.deepEqual(archiveCards.map(item => item.id), ['S01-CARD'])
    assert.equal(calls.filter(url => url.endsWith('cards.s1.json')).length, 1)
    assert.equal(calls.filter(url => url.endsWith('cards.st.json')).length, 1)
    console.log('Cold catalog request counts: ' + JSON.stringify({ total: calls.length,
      lookup: calls.filter(url => url.endsWith('cards.lookup.json')).length }))
    assert.equal(calls.filter(url => url.endsWith('cards.lookup.json')).length, 1,
      '同轮图鉴与构筑目录必须复用一次原始lookup读取')
    results.push({ name: 'cold editor and archive share three requests', passed: true, total: calls.length, lookup: 1 })
  }

  for (const order of ['archive-first', 'two-archives']) {
    const module = await fresh(); const calls = []; const body = deferred(); let jsonReads = 0
    globalThis.fetch = async (input, init) => {
      const url = String(input); calls.push(url)
      assert.equal(init.cache, 'no-store', '读取仍须保留no-store刷新语义')
      if (url.endsWith('cards.lookup.json')) return { ok: true, json: async () => { jsonReads++; return body.promise } }
      return good(url)
    }
    const first = module.loadCardArchiveCatalog()
    const second = order === 'archive-first' ? module.loadDeckCatalog() : module.loadCardArchiveCatalog()
    await flush()
    assert.equal(calls.length, 3, `${order}只能启动一组三请求`)
    assert.equal(jsonReads, 1, `${order}同一响应正文只解析一次`)
    body.resolve([])
    const values = await Promise.all([first, second])
    assert.ok(values.every(value => value.length === 1))
    results.push({ name: `${order}: delayed JSON shares one fetch and parse`, passed: true, total: 3, lookup: 1 })
  }

  {
    const module = await fresh(); const calls = []; let version = 1
    globalThis.fetch = async input => {
      const url = String(input); calls.push(url)
      return url.endsWith('cards.lookup.json') ? response([{ cardNo: 'S01-VARIANT',
        nameZh: `展示版本${version}`, effect: `展示效果${version}` }]) : good(url)
    }
    const first = await module.loadCardArchiveCatalog()
    const playable = await module.loadDeckCatalog()
    assert.equal(playable.some(item => item.id === 'S01-VARIANT'), false, '展示版本不得进入构筑目录')
    version = 2
    const count = calls.length
    const [second, concurrent] = await Promise.all([module.loadCardArchiveCatalog(), module.loadCardArchiveCatalog()])
    assert.equal(calls.length, count + 1, '热目录两次并发图鉴刷新只读一次最新lookup')
    assert.equal(first.find(item => item.id === 'S01-VARIANT').nameZh, '展示版本1')
    assert.equal(second.find(item => item.id === 'S01-VARIANT').nameZh, '展示版本2')
    assert.equal(concurrent.find(item => item.id === 'S01-VARIANT').effect, '展示效果2')
    assert.strictEqual(await module.loadDeckCatalog(), playable, '展示刷新不得改写构筑目录缓存')
    await module.loadCardArchiveCatalog()
    assert.equal(calls.length, count + 2, '已完成的原始lookup不能永久缓存')
    results.push({ name: 'later archive visits refresh presentation-only variants without mutating playable cache', passed: true })
  }

  for (const failure of ['network', 'http', 'json']) {
    const module = await fresh(); const calls = []
    globalThis.fetch = async input => { calls.push(String(input)); return good(String(input)) }
    const playable = await module.loadDeckCatalog()
    let fail = true
    globalThis.fetch = async input => {
      const url = String(input); calls.push(url)
      assert.ok(url.endsWith('cards.lookup.json'), '热图鉴只刷新lookup，不重读S1/ST')
      if (!fail) return response([])
      if (failure === 'network') throw new Error('network failure')
      if (failure === 'http') return response([], false)
      return { ok: true, json: async () => { throw new Error('invalid lookup json') } }
    }
    const failed = await Promise.allSettled([module.loadCardArchiveCatalog(), module.loadCardArchiveCatalog()])
    assert.ok(failed.every(value => value.status === 'rejected'), `${failure}不得静默变成空目录`)
    assert.equal(calls.length, 4, `${failure}并发失败应共享同一次物理lookup请求`)
    assert.strictEqual(await module.loadDeckCatalog(), playable, `${failure}不得退休有效构筑缓存`)
    fail = false
    assert.equal((await module.loadCardArchiveCatalog()).length, 1, `${failure}后必须可重试恢复`)
    assert.equal(calls.length, 5)
    results.push({ name: `archive ${failure} failure shares rejection and permits fresh retry`, passed: true })
  }

  for (const base of ['/', '/testrun/']) {
    const module = await fresh(support.replace('const deploymentPath = value => value',
      `const deploymentPath = value => ${JSON.stringify(base)} + value.replace(/^\\//, '')`))
    const calls = []
    globalThis.fetch = async input => { calls.push(String(input)); return good(String(input)) }
    await module.loadCardArchiveCatalog()
    assert.equal(calls.length, 3)
    assert.ok(calls.every(url => url.startsWith(`${base}data/l12/`)), '目录读取保留部署前缀，不跳外部或正式站')
    results.push({ name: `${base} deployment paths preserved with three static requests`, passed: true })
  }

  {
    const readJson = relative => JSON.parse(readFileSync(new URL(relative, import.meta.url), 'utf8'))
    const data = {
      moraleIdentityData: readJson('../../服务端WebSocket/TwelveLegions/Data/morale-identities.json'),
      cardProductInclusionsData: readJson('../../服务端WebSocket/TwelveLegions/Data/card-product-inclusions.json'),
      cardArchiveAssetsData: readJson('../../服务端WebSocket/TwelveLegions/Data/card-archive-assets.json'),
      seasonTwoRulesData: readJson('../../服务端WebSocket/TwelveLegions/Data/cards.s2.json'),
    }
    const variables = new Set(['seasonTwoIdentityById', 'lookupFactionMap', 'moraleIdentities', 'productInclusions',
      'productInclusionsByCardId', 'cardArchiveAssets', 'moraleIdentityByFaction', 'moraleIdentityByVersion', 'moraleIdentityByGodPower'])
    const functions = new Set(['withProductInclusions', 'normalizeMoraleCatalogCard', 'normalizeCardDimensions',
      'normalizeLookupRarity', 'lookupDeckCard'])
    const actualDeclarations = tree.statements.filter(node =>
      ts.isVariableStatement(node) && node.declarationList.declarations.some(item => variables.has(item.name.getText(tree)))
      || ts.isFunctionDeclaration(node) && functions.has(node.name?.text)).map(node => node.getText(tree))
    const presentation = ts.createSourceFile('cardPresentation.ts', readFileSync(new URL('../src/l12/cardPresentation.ts', import.meta.url), 'utf8'), ts.ScriptTarget.Latest, true)
    const normalizeType = presentation.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === 'normalizeLookupCardType')
    assert.ok(normalizeType)
    const realSupport = ['const deploymentPath = value => value',
      ...Object.entries(data).map(([name, value]) => `const ${name} = ${JSON.stringify(value)}`),
      normalizeType.getText(presentation), ...actualDeclarations].join('\n')
    const s1 = readJson('../public/data/l12/cards.s1.json')
    const lookupRows = readJson('../public/data/l12/cards.lookup.json')
    const st = readJson('../public/data/l12/cards.st.json')
    const module = await fresh(realSupport); const calls = []
    const bytesByFile = Object.fromEntries(['cards.s1.json','cards.lookup.json','cards.st.json'].map(file =>
      [file, readFileSync(new URL(`../public/data/l12/${file}`, import.meta.url)).length]))
    globalThis.fetch = async input => {
      const url = String(input); calls.push(url)
      return response(structuredClone(url.endsWith('cards.s1.json') ? s1 : url.endsWith('cards.st.json') ? st : lookupRows))
    }
    const [playable, archived] = await Promise.all([module.loadDeckCatalog(), module.loadCardArchiveCatalog()])
    const expectedPlayable = [...s1.map(card => card.id), ...lookupRows.filter(card => card.cardNo?.startsWith('S02-')).map(card => card.cardNo), ...st.map(card => card.id)]
    assert.deepEqual(playable.map(card => card.id), expectedPlayable, '实际全卡池构筑身份及顺序不得变化')
    assert.equal(new Set(archived.map(card => card.id)).size, archived.length, '实际图鉴版本不得重复')
    const included = new Set(data.cardProductInclusionsData.cards.map(card => card.cardId))
    const expectedArchive = new Set([...expectedPlayable, ...lookupRows.map(card => card.cardNo)].filter(id => included.has(id)))
    for (const asset of data.cardArchiveAssetsData.cards)
      if (expectedArchive.has(asset.baseCardId) || expectedPlayable.includes(asset.baseCardId)) expectedArchive.add(asset.id)
    assert.deepEqual([...archived.map(card => card.id)].sort(), [...expectedArchive].sort(), '实际图鉴展示版本全量保留')
    for (const original of [...s1, ...st]) {
      const found = playable.find(card => card.id === original.id)
      assert.equal(found.effect, original.effect, `${original.id}卡效文本不得变化`)
    }
    for (const identity of data.moraleIdentityData) {
      for (const id of [...identity.versionCardIds, identity.godPowerCardId].filter(Boolean)) {
        const found = playable.find(card => card.id === id)
        if (found) assert.equal(found.canonicalMoraleId, identity.canonicalCardId, `${id}士气/神力身份不得变化`)
      }
    }
    assert.deepEqual(await module.loadCardArchiveCatalog(), archived, '相同实际源重读后的全部字段必须相同')
    assert.equal(calls.length, 4, '冷并发三请求，后续图鉴刷新仅增加一次lookup')
    results.push({ name: 'actual complete card pool and presentation versions preserve IDs, order, effects and morale',
      passed: true, playable: playable.length, archive: archived.length, bytesByFile,
      initialStaticBytesBefore: bytesByFile['cards.s1.json'] + bytesByFile['cards.st.json'] + bytesByFile['cards.lookup.json'] * 2,
      initialStaticBytesAfter: Object.values(bytesByFile).reduce((sum, bytes) => sum + bytes, 0),
      byteScope: 'local source byte lengths; not measured HTTP transfer or runtime performance' })
  }
} finally { globalThis.fetch = originalFetch }

console.log('Deck catalog cache rejection, retry, inflight sharing, success cache and archive sharing passed')
console.log('RESULT ' + JSON.stringify({ sourceSha256: createHash('sha256').update(source).digest('hex').toUpperCase(),
  actualLoaderFunctions: true, actualFullPoolNormalizers: true, networkCalls: 0, cases: results.length, results }))
