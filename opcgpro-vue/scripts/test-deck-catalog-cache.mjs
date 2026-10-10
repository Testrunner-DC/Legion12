import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'

const source = readFileSync(new URL('../src/l12/decks.ts', import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const tree = ts.createSourceFile('decks.ts', source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const declaration = name => tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
const state = tree.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(tree) === 'catalogPromise'))
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
const productInclusionsByCardId = new Set(['S01-CARD'])
const cardArchiveAssets = []
const lookupDeckCard = value => ({ id: value.cardNo, number: value.cardNo, cardNo: value.cardNo,
  nameZh: value.nameZh || value.cardNo, cardType: value.cardType || 'legion', faction: value.faction || 'fate',
  product: value.product || 'P', effect: value.effect || '' })
`
const javascript = ts.transpileModule([support, state.getText(tree), loadText, archive.getText(tree)].join('\n'), {
  compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 },
}).outputText
let moduleId = 0
const fresh = () => import(`data:text/javascript;base64,${Buffer.from(javascript).toString('base64')}#${++moduleId}`)
const response = (value, ok = true) => ({ ok, json: async () => value })
const card = { id: 'S01-CARD', cardNo: 'S01-CARD', number: 'S01-CARD', nameZh: '合成卡', cardType: 'legion', faction: 'fate', product: 'P', effect: '' }
const good = url => url.endsWith('cards.s1.json') ? response([card])
  : url.endsWith('cards.lookup.json') ? response([]) : response([])
const originalFetch = globalThis.fetch

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
    assert.equal(calls.filter(url => url.endsWith('cards.lookup.json')).length, 2,
      '图鉴自身lookup与共享目录lookup必须各一次')
  }
} finally { globalThis.fetch = originalFetch }

console.log('Deck catalog cache rejection, retry, inflight sharing, success cache and archive sharing passed')
