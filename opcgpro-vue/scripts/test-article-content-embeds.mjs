import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import { createRequire } from 'node:module'
import * as articleBlocks from '../src/l12/site/articleBlocks.ts'
import * as deckCover from '../src/l12/deckCover.ts'

const { articlePublicDeckReference, parseArticleBody, serializeArticleBody } = articleBlocks
const require = createRequire(import.meta.url)
const Vue = require('vue')
const ts = require('typescript')
const { parse, compileScript } = require('@vue/compiler-sfc')
const artworkModule = { exports: {} }
vm.runInNewContext(ts.transpileModule(readFileSync(new URL('../src/l12/site/articleCardArt.ts', import.meta.url), 'utf8'),
  { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
{ module: artworkModule, exports: artworkModule.exports, require: id => {
  assert.equal(id, './articleBlocks'); return articleBlocks
}, Map })
const articleCardArt = artworkModule.exports
const publicDeckSummaryModule = { exports: {} }
const publicDeckSummaryOutput = ts.transpileModule(
  readFileSync(new URL('../src/l12/site/publicDeckSummary.ts', import.meta.url), 'utf8'),
  { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } },
).outputText
vm.runInNewContext(publicDeckSummaryOutput, {
  module: publicDeckSummaryModule, exports: publicDeckSummaryModule.exports,
  require: id => {
    if (id === '../deckCover') return deckCover
    assert.fail(`unexpected publicDeckSummary import ${id}`)
  },
  console, Error, Map, Set, Promise, URL, Date,
})
const publicDeckSummary = publicDeckSummaryModule.exports

const source = JSON.stringify({
  format: 'l12-blocks', version: 1,
  blocks: [
    { id: 'copy', type: 'paragraph', text: '正文', marks: [], align: 'left' },
    { id: 'card', type: 'card', cardId: 'S01-0001', caption: '关键回合再使用' },
    { id: 'deck', type: 'publicDeck', reference: '23456789ABCD' },
  ],
})
const parsed = parseArticleBody(source)
assert.deepEqual(parsed.blocks.map(block => block.type), ['paragraph', 'card', 'publicDeck'])
assert.equal(parsed.blocks[1].type === 'card' && parsed.blocks[1].cardId, 'S01-0001')
assert.equal(parsed.blocks[1].type === 'card' && parsed.blocks[1].caption, '关键回合再使用')
assert.equal(parsed.blocks[2].type === 'publicDeck' && parsed.blocks[2].reference, '23456789ABCD')
assert.deepEqual(parseArticleBody(serializeArticleBody(parsed)), parsed, '卡牌与牌库块必须完整往返')
assert.match(articleBlocks.articleBodyText(source), /关键回合再使用/, '单卡备注必须进入资讯摘要文本')
const cleanedCaption = parseArticleBody(JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [
  { id: 'caption', type: 'card', cardId: 'S01-0001', caption: `  ${'注'.repeat(510)}  ` },
  { id: 'empty-caption', type: 'card', cardId: 'S01-0002', caption: '   ' },
] }))
assert.equal(cleanedCaption.blocks[0].type === 'card' && cleanedCaption.blocks[0].caption?.length, 500)
assert.equal(cleanedCaption.blocks[1].type === 'card' && Object.hasOwn(cleanedCaption.blocks[1], 'caption'), false,
  '空备注不得占用正文或展示位置')

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
const picker = read('../src/l12/site/PublicDeckPicker.vue')
const renderer = read('../src/l12/site/ArticleContentRenderer.vue')
assert.match(editor, /import SingleCardPicker/)
assert.match(editor, /import PublicDeckPicker/)
assert.match(editor, /articlePublicDeckReference\(deck\.publicCode \|\| ''\)/)
assert.doesNotMatch(editor, /公共牌库短码或链接|deckInput/)
assert.match(editor, /data-article-embed/)
assert.match(editor, /cleanArticleCardCaption/)
assert.match(editor, /单卡备注（可空）/)
assert.match(editor, /cardEditContext !== currentBody\(\)/)
assert.match(picker, /import DeckCover/)
assert.match(picker, /deckLibraryApi\.summaries\(request\)/)
assert.match(picker, /validateSummaryPage/)
assert.doesNotMatch(picker, /hot-deck|animation:|setInterval|iframe/i)
assert.match(renderer, /import CardImage/)
assert.match(renderer, /import CatalogCardDetails/)
assert.match(renderer, /import DeckCover/)
assert.match(renderer, /const renderItems = computed/)
assert.match(renderer, /class="article-card-row"/)
assert.match(renderer, /v-if="card\.caption"/)
assert.match(renderer, /key: `cards:\$\{cards\[0\]!\.id\}`/)
assert.match(renderer, /key: `block:\$\{block\.id\}`/)
  assert.match(renderer, /flex-wrap:wrap/)
  assert.match(renderer, /justify-content:center/)
  assert.match(renderer, /flex:0 0 calc\(\(100% - var\(--article-card-gap\) - var\(--article-card-gap\)\)\/3\)/)
assert.match(renderer, /@media\(hover:hover\) and \(pointer:fine\)/)
assert.doesNotMatch(renderer, /<small>单卡<\/small>/)
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
    ...cards.map((card, index) => ({ id: `card-${index}`, type: 'card',
      cardId: typeof card === 'string' ? card : card.cardId,
      ...(typeof card === 'string' || !card.caption ? {} : { caption: card.caption }) })),
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
  '@/l12/platform': { publicDeckReadApi: { current: reference => activeHarness.readDeck(reference) },
    alternateArtApi: { gallery: () => activeHarness.readArts() } },
  '@/l12/CardImage.vue': componentStub,
  '@/l12/CatalogCardDetails.vue': componentStub,
  '@/l12/DeckCover.vue': componentStub,
  './ArticleInlineText.vue': componentStub,
  './articleBlocks': articleBlocks,
  './articleCardArt': articleCardArt,
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

let activePickerHarness = null
const pickerParsed = parse(picker, { filename: 'PublicDeckPicker.vue' })
assert.deepEqual(pickerParsed.errors, [])
const pickerScript = compileScript(pickerParsed.descriptor, { id: 'public-deck-picker-regression' })
const pickerOutput = ts.transpileModule(pickerScript.content, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText
const pickerModule = { exports: {} }
const pickerImports = {
  vue: Vue,
  '@/l12/decks': { loadDeckCatalog: () => activePickerHarness.loadCatalog() },
  '@/l12/platform': { deckLibraryApi: { summaries: query => activePickerHarness.summaries(query) } },
  '@/l12/DeckCover.vue': componentStub,
  '@/l12/mobileViewport': { landscapeTeleportTarget: () => 'body' },
  './articleBlocks': articleBlocks,
  './publicDeckSummary': publicDeckSummary,
}
vm.runInNewContext(pickerOutput, {
  module: pickerModule, exports: pickerModule.exports,
  require: id => { assert.ok(Object.hasOwn(pickerImports, id), `unexpected picker import ${id}`); return pickerImports[id] },
  console, Error, Map, Set, Promise, URLSearchParams,
})
const ActualPicker = pickerModule.exports.default
ActualPicker.render = () => null

const publicSummary = (reference, name = reference) => ({
  id: `public-${reference}`, source: 'public', name, masterId: 'S01-0001', masterName: '测试主宰', faction: 'tianting',
  author: '测试作者', publicCode: reference, publicationVersion: null, createdAt: '2026-10-20T00:00:00Z',
  updatedAt: '2026-10-20T00:00:00Z', counts: { main: 3, uncountedMain: 0, morale: 0, special: 0, bench: 0 },
  legal: true, legalityReason: null, environment: { status: 'known', value: '2.5', reason: null },
  views: 0, likes: 0, copies: 0, viewerLiked: false, canEdit: false, readToken: 'a'.repeat(64),
})
const officialSummary = () => ({ ...publicSummary('', '官方测试牌库'), id: `official:${'b'.repeat(64)}`, source: 'official', publicCode: null, readToken: null })
const summaryPage = (query, items, total = items.length) => ({
  items, total, page: query.page ?? 1, pageSize: query.pageSize ?? 30, generation: 'picker-test', catalogVersion: 'catalog-test',
  policyVersion: 1, sourceAvailability: { public: 'available', official: 'available' },
  facets: { sources: [], masters: [], factions: [], environments: [], cards: [], legal: total, illegal: 0 },
})
function pickerFixture() {
  const harness = {
    catalogCalls: [], summaryCalls: [],
    loadCatalog() { const call = deferred(); this.catalogCalls.push(call); return call.promise },
    summaries(query) { const call = { query: structuredClone(query), ...deferred() }; this.summaryCalls.push(call); return call.promise },
  }
  activePickerHarness = harness
  const events = []
  const root = hostNode('root')
  const app = memoryRenderer.createApp({ render: () => Vue.h(ActualPicker, {
    title: '选择公共牌库', selectedReference: deckCode(0),
    onSelect: (reference, deck) => events.push({ type: 'select', reference, deck }),
    onClose: () => events.push({ type: 'close' }),
  }) })
  app.mount(root)
  return {
    ...harness, events,
    get state() { return app._instance.subTree.component.setupState },
    unmount: () => app.unmount(),
  }
}

const pickerLifecycle = pickerFixture()
await flush()
assert.equal(pickerLifecycle.summaryCalls.length, 1)
assert.deepEqual(pickerLifecycle.summaryCalls[0].query, { source: 'public', page: 1, pageSize: 12, keyword: undefined, sort: 'trend' })
pickerLifecycle.catalogCalls[0].reject(new Error('卡面失败'))
pickerLifecycle.state.searchDraft = '第二代'
pickerLifecycle.state.applySearch()
await flush()
assert.equal(pickerLifecycle.summaryCalls.length, 2)
const firstPickerDeck = publicSummary(deckCode(0), '旧牌库')
const latestPickerDeck = publicSummary(deckCode(1), '新牌库')
pickerLifecycle.summaryCalls[0].resolve(summaryPage(pickerLifecycle.summaryCalls[0].query, [firstPickerDeck]))
await flush()
assert.equal(pickerLifecycle.state.items.length, 0, '旧搜索成功不得覆盖新一代加载状态')
const latestSummaryPage = summaryPage(pickerLifecycle.summaryCalls[1].query, [latestPickerDeck, officialSummary()], 2)
assert.doesNotThrow(() => publicDeckSummary.validateSummaryPage(latestSummaryPage, pickerLifecycle.summaryCalls[1].query))
pickerLifecycle.summaryCalls[1].resolve(latestSummaryPage)
await flush()
assert.equal(pickerLifecycle.state.catalogUnavailable, true, '卡面失败只降级显示，不锁死目录选择')
assert.deepEqual(Array.from(pickerLifecycle.state.items, item => item.name), ['新牌库'],
  `官方或无公开短码条目不可选择；状态=${pickerLifecycle.state.state}；错误=${pickerLifecycle.state.errorText}`)
pickerLifecycle.state.choose(officialSummary())
assert.equal(pickerLifecycle.events.length, 0)
pickerLifecycle.state.choose(latestPickerDeck)
assert.equal(pickerLifecycle.events[0].reference, latestPickerDeck.publicCode)
assert.equal(Object.hasOwn(pickerLifecycle.events[0], 'readToken'), false)
pickerLifecycle.state.page = 2
await flush()
assert.equal(pickerLifecycle.summaryCalls.at(-1).query.page, 2)
pickerLifecycle.state.sort = 'latest'
await flush()
const stalePage = pickerLifecycle.summaryCalls.at(-2), latestSort = pickerLifecycle.summaryCalls.at(-1)
stalePage.resolve(summaryPage(stalePage.query, [publicSummary(deckCode(2), '旧分页')], 13))
latestSort.resolve(summaryPage(latestSort.query, [publicSummary(deckCode(3), '最新排序')], 13))
await flush()
assert.deepEqual(Array.from(pickerLifecycle.state.items, item => item.name), ['最新排序'])
pickerLifecycle.unmount()

const pickerRetry = pickerFixture()
await flush()
pickerRetry.catalogCalls[0].resolve([])
pickerRetry.summaryCalls[0].reject(new Error('目录暂不可用'))
await flush()
assert.equal(pickerRetry.state.state, 'unavailable')
pickerRetry.state.retry()
assert.equal(pickerRetry.summaryCalls.length, 2)
pickerRetry.summaryCalls[1].resolve(summaryPage(pickerRetry.summaryCalls[1].query, [publicSummary(deckCode(4), '重试成功')]))
await flush()
assert.equal(pickerRetry.state.state, 'available')
pickerRetry.state.close()
assert.deepEqual(pickerRetry.events.at(-1), { type: 'close' })
pickerRetry.unmount()

const pickerClosed = pickerFixture()
await flush()
pickerClosed.state.close()
pickerClosed.summaryCalls[0].resolve(summaryPage(pickerClosed.summaryCalls[0].query, [publicSummary(deckCode(5), '关闭后迟到')]))
pickerClosed.catalogCalls[0].resolve([])
await flush()
assert.equal(pickerClosed.state.items.length, 0, '取消后迟到响应不得写回选择器')
pickerClosed.unmount()

const pickerUnmounted = pickerFixture()
await flush()
const pickerUnmountedState = pickerUnmounted.state
pickerUnmounted.unmount()
pickerUnmounted.summaryCalls[0].reject(new Error('卸载后失败'))
pickerUnmounted.catalogCalls[0].resolve([])
await flush()
assert.equal(pickerUnmountedState.items.length, 0, '卸载后迟到响应不得提交')

function fixture(initialBody) {
  const harness = {
    catalogCalls: [], deckCalls: [], artCalls: [], inFlight: 0, maxInFlight: 0,
    readArts() { const call = deferred(); this.artCalls.push(call); return call.promise },
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
    catalogCalls: harness.catalogCalls, deckCalls: harness.deckCalls, artCalls: harness.artCalls, props,
    get maxInFlight() { return harness.maxInFlight },
    get state() { return app._instance.subTree.component.setupState },
    unmount: () => app.unmount(),
  }
}

for (const count of [1, 3, 4, 6]) {
  const grouped = fixture(articleBody({ cards: Array.from({ length: count }, () => 'S01-0001') }))
  await flush()
  assert.deepEqual(Array.from(grouped.state.renderItems, item => item.kind), ['cards'])
  assert.equal(grouped.state.renderItems[0].cards.length, count, `连续 ${count} 张单卡必须保持同一三列换行组`)
  grouped.catalogCalls[0].resolve([testCard('S01-0001')])
  await flush(); grouped.unmount()
}
const interruptedCards = fixture(JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [
  { id: 'card-a', type: 'card', cardId: 'S01-0001', caption: '有备注' },
  { id: 'middle', type: 'paragraph', text: '分隔正文', marks: [], align: 'left' },
  { id: 'card-b', type: 'card', cardId: 'S01-0002' },
] }))
await flush()
assert.deepEqual(Array.from(interruptedCards.state.renderItems, item => item.kind), ['cards', 'block', 'cards'],
  '正文块必须切断连续单卡分组并保持原顺序')
assert.equal(interruptedCards.state.renderItems[0].cards[0].caption, '有备注')
assert.equal(Object.hasOwn(interruptedCards.state.renderItems[2].cards[0], 'caption'), false)
interruptedCards.catalogCalls[0].resolve([testCard('S01-0001'), testCard('S01-0002')])
await flush(); interruptedCards.unmount()

const collidingLegacyKeys = fixture(JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [
  { id: 'x', type: 'card', cardId: 'S01-0001' },
  { id: 'cards-x', type: 'paragraph', text: '旧键会与卡组冲突', marks: [], align: 'left' },
] }))
await flush()
assert.deepEqual(Array.from(collidingLegacyKeys.state.renderItems, item => item.key), ['cards:x', 'block:cards-x'])
assert.equal(new Set(Array.from(collidingLegacyKeys.state.renderItems, item => item.key)).size, 2,
  '卡组和普通块必须使用互斥键命名空间')
collidingLegacyKeys.props.body = JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [
  { id: 'y', type: 'card', cardId: 'S01-0002' },
  { id: 'cards-y', type: 'paragraph', text: '更新后的旧文档', marks: [], align: 'left' },
] })
await flush()
assert.deepEqual(Array.from(collidingLegacyKeys.state.renderItems, item => item.key), ['cards:y', 'block:cards-y'])
collidingLegacyKeys.catalogCalls[0].resolve([testCard('S01-0001'), testCard('S01-0002')])
await flush(); collidingLegacyKeys.unmount()

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

const artBase = { ...testCard('S01-0001'), cardType: 'master', faction: 'tianting', product: '基础', effect: '基础权威规则' }
const art = { id: 'official:art-one', active: true, kind: 'alternate-art', baseCardId: artBase.id,
  artCode: 'ART-ONE', displayName: '公开异画', builtIn: true, cardImageId: 'S01-0001A', imageUrl: '', products: [] }
const artBlock = { id: 'art', type: 'card', cardId: artBase.id, alternateArtId: art.id, caption: '保持备注' }
const artBody = JSON.stringify({ format: 'l12-blocks', version: 1, blocks: [artBlock] })
assert.deepEqual(parseArticleBody(serializeArticleBody(parseArticleBody(artBody))).blocks[0], artBlock)
assert.equal(Object.hasOwn(parseArticleBody(source).blocks[1], 'alternateArtId'), false)
const choices = articleCardArt.articleCardPickerItems([artBase], [art,
  { ...art, id: 'inactive', active: false }, { ...art, id: 'standalone', kind: 'gallery-card' },
  { ...art, id: 'unknown', baseCardId: 'NOT-A-CARD' },
  { ...art, id: 'unsafe', builtIn: false, imageUrl: 'https://tracker.invalid/art.webp' }])
assert.equal(choices.length, 2, 'Only active public, mapped, safely rendered alternate arts may be selected')
assert.equal(choices[1].cardId, artBase.id, 'Artwork identity must never replace the rules card identity')
assert.equal(choices[1].alternateArtId, art.id)
assert.equal(choices[1].detailCard.effect, artBase.effect)
assert.equal(choices[1].detailCard.imageUrl, 'l12-card-id:S01-0001A')
const customUrl = '/api/site/media/synthetic-art/desktop/' + 'a'.repeat(64) + '.webp'
assert.equal(articleCardArt.articleCardArtwork(artBase, { ...art, builtIn: false, imageUrl: customUrl }).imageUrl, customUrl)
assert.equal(articleCardArt.articleCardFor(artBlock, artBase, []), undefined, 'Missing art must not silently render base artwork')
const artRender = fixture(artBody)
await flush()
assert.equal(artRender.artCalls.length, 1)
artRender.catalogCalls[0].resolve([artBase]); artRender.artCalls[0].resolve([art])
await flush()
assert.equal(artRender.state.cardFor(artBlock).imageUrl, 'l12-card-id:S01-0001A')
await artRender.state.openEmbeddedCard(artBlock)
assert.equal(artRender.state.selectedCard.imageUrl, 'l12-card-id:S01-0001A', 'Detail must preserve selected artwork')
artRender.props.body = JSON.stringify({ format: 'l12-blocks', version: 1,
  blocks: [{ ...artBlock, alternateArtId: 'missing-art' }] })
await flush()
assert.equal(artRender.state.selectedCard, null, 'Same base card but another artwork invalidates old detail')
assert.equal(artRender.state.cardFor({ ...artBlock, alternateArtId: 'missing-art' }), undefined)
artRender.unmount()
const artRetry = fixture(artBody)
await flush()
artRetry.catalogCalls[0].resolve([artBase]); artRetry.artCalls[0].reject(new Error('gallery failed'))
await flush()
assert.match(artRetry.state.cardError(artBlock), /点击重试/)
const artRetryOpen = artRetry.state.openEmbeddedCard(artBlock)
artRetry.artCalls[1].resolve([art]); await artRetryOpen; await flush()
assert.equal(artRetry.state.selectedCard.imageUrl, 'l12-card-id:S01-0001A')
artRetry.unmount()
const lateArt = fixture(artBody)
await flush()
const lateOpen = lateArt.state.openEmbeddedCard(artBlock)
lateArt.props.body = articleBody({ cards: [artBase.id] }); await flush()
lateArt.catalogCalls[0].resolve([artBase]); lateArt.artCalls[0].resolve([art])
await lateOpen; await flush()
assert.equal(lateArt.state.selectedCard, null, 'Late artwork click cannot reopen after article change')
lateArt.unmount()
// Mount the actual editor setup, controlling only its two asynchronous readers.
let activeEditorHarness
const editorModule = { exports: {} }
const editorDescriptor = parse(editor, { filename: 'ArticleDocumentEditor.vue' }).descriptor
const editorImports = { vue: Vue, './articleBlocks': articleBlocks, './articleCardArt': articleCardArt,
  '@/l12/platform': { alternateArtApi: { gallery: () => activeEditorHarness.readArts() } },
  '@/l12/decks': { loadDeckCatalog: () => activeEditorHarness.loadCatalog() } }
vm.runInNewContext(ts.transpileModule(compileScript(editorDescriptor, { id: 'article-editor-availability' }).content,
  { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText,
{ module: editorModule, exports: editorModule.exports,
  require: id => editorImports[id] ?? (id.endsWith('.vue') ? componentStub : assert.fail('Unexpected editor import ' + id)),
  window: { getSelection: () => null }, Element: class Element {}, HTMLElement: class HTMLElement {},
  Map, Set, Promise, console, setTimeout, clearTimeout })
const ActualEditor = editorModule.exports.default
ActualEditor.render = () => null
function editorFixture() {
  const harness = { catalogCalls: [], artCalls: [],
    loadCatalog() { const call = deferred(); this.catalogCalls.push(call); return call.promise },
    readArts() { const call = deferred(); this.artCalls.push(call); return call.promise } }
  activeEditorHarness = harness
  const notices = [], root = hostNode('root')
  const app = memoryRenderer.createApp({ render: () => Vue.h(ActualEditor, {
    modelValue: '旧文章正文', onNotice: value => notices.push(value) }) })
  app.mount(root)
  return { ...harness, notices, get state() { return app._instance.subTree.component.setupState }, unmount: () => app.unmount() }
}
const degradedEditor = editorFixture()
degradedEditor.state.openCard()
degradedEditor.catalogCalls[0].resolve([artBase]); degradedEditor.artCalls[0].resolve([art])
await flush()
assert.equal(degradedEditor.state.pickerItems.length, 2)
degradedEditor.state.closeCardPicker()
degradedEditor.state.openCard()
assert.equal(degradedEditor.state.cardPicker, false, 'Old picker must retire before retry starts')
degradedEditor.catalogCalls[1].resolve([artBase]); degradedEditor.artCalls[1].reject(new Error('gallery unavailable'))
await flush()
assert.equal(degradedEditor.state.cardPicker, true, 'Gallery outage must not block the healthy base catalog')
assert.equal(degradedEditor.state.pickerItems.length, 1, 'Failure must remove previous artwork choices')
assert.equal(degradedEditor.state.pickerArtsUnavailable, true)
assert.equal(degradedEditor.state.pickerItems[0].alternateArtId, undefined)
assert.match(degradedEditor.notices.at(-1), /基础卡/)
degradedEditor.state.closeCardPicker(); degradedEditor.state.openCard()
degradedEditor.catalogCalls[2].resolve([artBase]); degradedEditor.artCalls[2].resolve([art])
await flush()
assert.equal(degradedEditor.state.pickerItems.length, 2, 'Reopening retries gallery successfully')
assert.equal(degradedEditor.state.pickerArtsUnavailable, false)
degradedEditor.state.closeCardPicker(); degradedEditor.state.openCard()
degradedEditor.catalogCalls[3].reject(new Error('catalog failed')); degradedEditor.artCalls[3].resolve([art])
await flush()
assert.equal(degradedEditor.state.cardPicker, false, 'Broken rules catalog must not present old or artwork-only choices')
assert.equal(degradedEditor.state.pickerItems.length, 0)
assert.match(degradedEditor.notices.at(-1), /基础卡/)
degradedEditor.unmount()
const closedEditor = editorFixture()
closedEditor.state.openCard(); closedEditor.state.closeCardPicker()
closedEditor.catalogCalls[0].resolve([artBase]); closedEditor.artCalls[0].resolve([art])
await flush()
assert.equal(closedEditor.state.cardPicker, false)
assert.equal(closedEditor.state.pickerItems.length, 0, 'Late closed response must not populate choices')
closedEditor.unmount()
const overlappedEditor = editorFixture()
overlappedEditor.state.openCard(); overlappedEditor.state.openCard()
overlappedEditor.catalogCalls[1].resolve([artBase]); overlappedEditor.artCalls[1].reject(new Error('latest gallery failure'))
await flush()
assert.equal(overlappedEditor.state.pickerItems.length, 1)
overlappedEditor.catalogCalls[0].resolve([artBase]); overlappedEditor.artCalls[0].resolve([art])
await flush()
assert.equal(overlappedEditor.state.pickerItems.length, 1, 'Older gallery success must not refill a degraded latest picker')
assert.equal(overlappedEditor.state.pickerArtsUnavailable, true)
overlappedEditor.unmount()
const disposedEditor = editorFixture()
disposedEditor.state.openCard()
const disposedState = disposedEditor.state
disposedEditor.unmount()
disposedEditor.catalogCalls[0].resolve([artBase]); disposedEditor.artCalls[0].resolve([art])
await flush()
assert.equal(disposedState.cardPicker, false)
assert.equal(disposedState.pickerItems.length, 0)
console.log('Article editor availability actual SFC: success, gallery fallback, retry, catalog failure, close, stale response and unmount passed')
console.log('article content embed regression passed (including alternate-art model, mapping, detail, retry and stale-response contracts)')
await import('./test-article-summary-read.mjs')
