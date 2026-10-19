import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import * as articleBlocks from '../src/l12/site/articleBlocks.ts'

const { articlePublicDeckReference, parseArticleBody, serializeArticleBody } = articleBlocks
const require = createRequire(import.meta.url)
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript } = require('@vue/compiler-sfc')

const source = JSON.stringify({
  format: 'l12-blocks', version: 1,
  blocks: [
    { id: 'copy', type: 'paragraph', text: '正文', marks: [], align: 'left' },
    { id: 'card', type: 'card', cardId: 'S01-0001' },
    { id: 'deck', type: 'publicDeck', reference: '23456789ABCD' },
  ],
})
const parsed = parseArticleBody(source)
assert.deepEqual(parsed.blocks.map(block => block.type), ['paragraph', 'card', 'publicDeck'])
assert.equal(parsed.blocks[1].type === 'card' && parsed.blocks[1].cardId, 'S01-0001')
assert.equal(parsed.blocks[2].type === 'publicDeck' && parsed.blocks[2].reference, '23456789ABCD')
assert.deepEqual(parseArticleBody(serializeArticleBody(parsed)), parsed, '卡牌与牌库块必须完整往返')

assert.equal(articlePublicDeckReference('2345-6789-ABCD'), '23456789ABCD')
assert.equal(articlePublicDeckReference('/decks/2345-6789-ABCD?from=news'), '23456789ABCD')
assert.equal(articlePublicDeckReference('https://example.com/site/decks/23456789ABCD'), '23456789ABCD')
for (const unsafe of ['/news/23456789ABCD', 'https://example.com/private/23456789ABCD',
  'official:' + 'a'.repeat(64), '../decks/23456789ABCD', 'javascript:alert(1)'])
  assert.equal(articlePublicDeckReference(unsafe), '', `必须拒绝不安全牌库引用：${unsafe}`)

const dirty = parseArticleBody(JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [
  { id: 'bad-card', type: 'card', cardId: '<script>' },
  { id: 'bad-deck', type: 'publicDeck', reference: '/private/23456789ABCD' },
] }))
assert.deepEqual(dirty.blocks.map(block => block.type), ['paragraph'], '非法嵌入不得进入结构化正文')

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8').replace(/\r\n?/g, '\n')
const editor = read('../src/l12/site/ArticleDocumentEditor.vue')
const renderer = read('../src/l12/site/ArticleContentRenderer.vue')
assert.match(editor, /import SingleCardPicker/)
assert.match(editor, /articlePublicDeckReference\(deckInput\.value\)/)
assert.match(editor, /data-article-embed/)
assert.match(renderer, /import CardImage/)
assert.match(renderer, /import CatalogCardDetails/)
assert.match(renderer, /import DeckCover/)
assert.match(renderer, /name: 'public-deck-detail'/)
assert.match(renderer, /MAX_EMBEDDED_DECK_READS = 12/)
assert.doesNotMatch(source, /readToken|ownerId|cardIds/, '文章正文不得存储牌库正文、私人标识或读取凭据')

const deferred = () => {
  let resolve, reject
  const promise = new Promise((accept, decline) => { resolve = accept; reject = decline })
  return { promise, resolve, reject }
}
const flush = async () => {
  for (let index = 0; index < 6; index++) { await Promise.resolve(); await Vue.nextTick() }
}
const alphabet = '23456789ABCDEFGHJKMNPQRSTVWXYZ'
const deckCode = offset => Array.from({ length: 12 }, (_, index) => alphabet[(offset + index) % alphabet.length]).join('')
const articleBody = ({ cards = [], decks = [] } = {}) => JSON.stringify({
  format: 'l12-blocks', version: 1,
  blocks: [
    ...cards.map((cardId, index) => ({ id: `card-${index}`, type: 'card', cardId })),
    ...decks.map((reference, index) => ({ id: `deck-${index}`, type: 'publicDeck', reference })),
  ],
})
const testCard = id => ({ id, number: id, nameZh: `测试卡 ${id}`, imageUrl: '' })

let activeHarness = null
const componentStub = { __esModule: true, default: {} }
const { descriptor, errors } = parse(renderer, { filename: 'ArticleContentRenderer.vue' })
assert.deepEqual(errors, [])
const script = compileScript(descriptor, { id: 'article-content-embed-regression' })
const output = ts.transpileModule(script.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
const actualModule = { exports: {} }
const imports = {
  vue: Vue,
  '@/l12/decks': { loadDeckCatalog: () => activeHarness.loadCatalog() },
  '@/l12/platform': { publicDeckReadApi: { current: reference => activeHarness.readDeck(reference) } },
  '@/l12/CardImage.vue': componentStub,
  '@/l12/CatalogCardDetails.vue': componentStub,
  '@/l12/DeckCover.vue': componentStub,
  './ArticleInlineText.vue': componentStub,
  './articleBlocks': articleBlocks,
  './publicDeckRead': {
    validatePublicDeckCurrent: value => value,
    settlePublicDeckRead: async (request, validate) => {
      try { return { status: 'available', value: validate(await request) } }
      catch (error) { return { status: 'unavailable', message: error instanceof Error ? error.message : '读取失败' } }
    },
  },
}
vm.runInNewContext(output, {
  module: actualModule, exports: actualModule.exports,
  require: id => { assert.ok(Object.hasOwn(imports, id), `unexpected renderer import ${id}`); return imports[id] },
  console, Error, Map, Set, Promise,
  window: { document: { activeElement: null }, addEventListener() {}, removeEventListener() {} },
})
const ActualRenderer = actualModule.exports.default
ActualRenderer.render = () => null

function hostNode(tag) { return { tag, text: '', children: [], parent: null, props: {} } }
function detach(node) {
  if (!node.parent) return
  node.parent.children.splice(node.parent.children.indexOf(node), 1)
  node.parent = null
}
const memoryRenderer = Vue.createRenderer({
  createElement: hostNode,
  createText(text) { const node = hostNode('#text'); node.text = text; return node },
  createComment(text) { const node = hostNode('#comment'); node.text = text; return node },
  insert(node, parent, anchor = null) {
    detach(node); node.parent = parent
    const index = anchor ? parent.children.indexOf(anchor) : -1
    if (index < 0) parent.children.push(node); else parent.children.splice(index, 0, node)
  },
  remove: detach,
  parentNode: node => node.parent,
  nextSibling: node => node.parent?.children[node.parent.children.indexOf(node) + 1] ?? null,
  setText: (node, text) => { node.text = text },
  setElementText(node, text) { node.children = []; node.text = text },
  patchProp: (node, key, previous, value) => { node.props[key] = value },
  setScopeId() {},
})

function fixture(initialBody) {
  const harness = {
    catalogCalls: [], deckCalls: [], inFlight: 0, maxInFlight: 0,
    loadCatalog() { const call = deferred(); this.catalogCalls.push(call); return call.promise },
    readDeck(reference) {
      const gate = deferred()
      const call = {
        reference, promise: gate.promise,
        resolve: value => { this.inFlight -= 1; gate.resolve(value) },
        reject: error => { this.inFlight -= 1; gate.reject(error) },
      }
      this.inFlight += 1
      this.maxInFlight = Math.max(this.maxInFlight, this.inFlight)
      this.deckCalls.push(call)
      return call.promise
    },
  }
  activeHarness = harness
  const props = Vue.reactive({ body: initialBody, media: [] })
  const root = hostNode('root')
  const app = memoryRenderer.createApp({ render: () => Vue.h(ActualRenderer, props) })
  app.mount(root)
  return {
    catalogCalls: harness.catalogCalls, deckCalls: harness.deckCalls, props,
    get maxInFlight() { return harness.maxInFlight },
    get state() { return app._instance.subTree.component.setupState },
    unmount: () => app.unmount(),
  }
}

const firstReferences = Array.from({ length: 12 }, (_, index) => deckCode(index))
const latestReferences = Array.from({ length: 12 }, (_, index) => deckCode(index + 12))
const finalReferences = Array.from({ length: 3 }, (_, index) => deckCode(index + 24))
const lifecycle = fixture(articleBody({ decks: firstReferences }))
await flush()
assert.equal(lifecycle.catalogCalls.length, 1)
assert.equal(lifecycle.deckCalls.length, 0, '目录未完成时不得提前读取牌库')
lifecycle.props.body = articleBody({ decks: latestReferences })
await flush()
lifecycle.catalogCalls[0].resolve([testCard('S01-0001')])
await flush()
assert.deepEqual(lifecycle.deckCalls.map(call => call.reference), latestReferences,
  '目录延迟期间只允许读取最终一代冻结引用')
assert.equal(lifecycle.maxInFlight, 12, '一个实例最多只能有一波十二个牌库读取在途')
lifecycle.props.body = articleBody({ decks: finalReferences })
await flush()
assert.equal(lifecycle.deckCalls.length, 12, '旧一波在途时不得叠加新一波')
for (const call of lifecycle.deckCalls.slice(0, 12)) call.resolve({ reference: call.reference })
await flush()
assert.deepEqual(lifecycle.deckCalls.slice(12).map(call => call.reference), finalReferences,
  '旧一波结算后只读取最新需求')
for (const call of lifecycle.deckCalls.slice(12)) call.reject(new Error(`暂不可用 ${call.reference}`))
await flush()
assert.deepEqual([...lifecycle.state.deckReads.keys()], [])
assert.deepEqual([...lifecycle.state.deckErrors.keys()], finalReferences, '正常失败必须结算为状态且不锁死刷新泵')
const recoveredReference = deckCode(27)
lifecycle.props.body = articleBody({ decks: [recoveredReference] })
await flush()
assert.equal(lifecycle.deckCalls.at(-1).reference, recoveredReference)
lifecycle.deckCalls.at(-1).resolve({ reference: recoveredReference })
await flush()
assert.deepEqual([...lifecycle.state.deckReads.keys()], [recoveredReference])
lifecycle.unmount()

const unmounted = fixture(articleBody({ decks: [deckCode(28)] }))
await flush()
unmounted.unmount()
unmounted.catalogCalls[0].resolve([testCard('S01-0001')])
await flush()
assert.equal(unmounted.deckCalls.length, 0, '卸载后目录完成不得再发公共牌库请求')

const retry = fixture(articleBody({ cards: ['S01-0001'] }))
await flush()
retry.catalogCalls[0].reject(new Error('目录失败'))
await flush()
assert.match(retry.state.catalogError, /点击重试/)
const retryOpen = retry.state.openEmbeddedCard({ id: 'card-0', type: 'card', cardId: 'S01-0001' })
assert.equal(retry.catalogCalls.length, 2, '点击失败单卡必须显式重试目录')
retry.catalogCalls[1].resolve([testCard('S01-0001'), testCard('S01-0002')])
await retryOpen; await flush()
assert.equal(retry.state.selectedCard.id, 'S01-0001', '重试成功仍进入现有单卡详情')
retry.props.body = articleBody({ cards: ['S01-0002'] })
await flush()
assert.equal(retry.state.selectedCard, null, '正文切换必须关闭旧单卡详情')
await retry.state.openEmbeddedCard({ id: 'card-0', type: 'card', cardId: 'S01-0002' })
assert.equal(retry.state.selectedCard.id, 'S01-0002')
retry.unmount()

const staleCard = fixture(articleBody({ cards: ['S01-0001'] }))
await flush()
const staleOpen = staleCard.state.openEmbeddedCard({ id: 'card-0', type: 'card', cardId: 'S01-0001' })
staleCard.props.body = articleBody({ cards: ['S01-0002'] })
await flush()
staleCard.catalogCalls[0].resolve([testCard('S01-0001'), testCard('S01-0002')])
await staleOpen; await flush()
assert.equal(staleCard.state.selectedCard, null, '正文切换后旧点击不得打开过期单卡详情')
staleCard.unmount()

console.log('article content embed regression passed')
