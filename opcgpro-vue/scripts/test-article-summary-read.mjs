import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
import { parse, compileScript } from '@vue/compiler-sfc'
import * as Vue from 'vue'

const flush = async () => { for (let i = 0; i < 15; i++) { await Promise.resolve(); await Vue.nextTick() } }
const pending = () => { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b }); return { promise, resolve, reject } }
const renderer = Vue.createRenderer({ createElement: () => ({ children: [] }), createText: () => ({}), createComment: () => ({}), insert(c, p) { p.children.push(c) }, remove() {}, setText() {}, setElementText() {}, parentNode: () => null, nextSibling: () => null, patchProp() {} })
const source = readFileSync(new URL('../src/l12/site/NewsPage.vue', import.meta.url), 'utf8')
const compiled = compileScript(parse(source).descriptor, { id: 'article-summary-read' })
const code = ts.transpileModule(compiled.content, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText
function fixture(options = {}) {
  const calls = [], timers = new Map()
  let timerId = 0
  const route = Vue.reactive({ name: options.id ? 'news-detail' : 'news', params: { articleId: options.id || '' }, hash: '' })
  const platform = {
    articleApi: {
      list: () => { throw Error('Full list must never be requested') },
      summaries: query => { calls.push(['summaries', { ...query }]); return options.summaries?.(query) ?? Promise.resolve([{ id: 'a', title: '文章', categoryId: 'cat', summary: '摘要', bodyPreview: '' }]) },
      get: id => { calls.push(['detail', id]); return options.get?.(id) ?? Promise.resolve({ id, body: '完整正文', title: '单篇' }) },
    },
    siteContentApi: { categories: kind => { calls.push(['categories', kind]); return Promise.resolve([{ id: 'cat', name: kind }]) } },
  }
  const module = { exports: {} }
  const imports = { vue: Vue, 'vue-router': { useRoute: () => route }, '@/l12/platform': platform, '../horizontalScrollArrows': { vHorizontalScrollArrows: {} }, './ArticleContentRenderer.vue': { default: {} } }
  vm.runInNewContext(code, { module, exports: module.exports, Error, require: id => { if (!(id in imports)) throw Error(id); return imports[id] }, setTimeout: run => { timers.set(++timerId, run); return timerId }, clearTimeout: id => timers.delete(id), console })
  module.exports.default.render = () => null
  const vnode = Vue.h(module.exports.default), root = { children: [] }
  renderer.render(vnode, root)
  return { state: vnode.component.setupState, route, calls, unmount: () => renderer.render(null, root), timers: async () => { const tasks = [...timers.values()]; timers.clear(); tasks.forEach(run => run()); await flush() } }
}
let count = 0
async function test(name, run) { await run(); console.log('PASS', name); count++ }
await test('list fetches only summaries, not all bodies', async () => {
  const f = fixture(); await flush(); assert.equal(f.calls.filter(c => c[0] === 'summaries').length, 1); assert.equal(f.calls.filter(c => c[0] === 'detail').length, 0); assert.equal(f.state.entries[0].id, 'a'); f.unmount()
})
await test('direct detail fetches one item without list or categories', async () => {
  const f = fixture({ id: 'outside-first-200' }); await flush(); assert.deepEqual(f.calls, [['detail', 'outside-first-200']]); assert.equal(f.state.selectedArticle.body, '完整正文'); f.unmount()
})
await test('late previous detail cannot replace new route', async () => {
  const old = pending(), f = fixture({ id: 'old', get: id => id === 'old' ? old.promise : Promise.resolve({ id, body: 'new body' }) }); await flush(); f.route.params.articleId = 'new'; await flush(); old.resolve({ id: 'old', body: 'old body' }); await flush(); assert.equal(f.state.selectedArticle.id, 'new'); f.unmount()
})
await test('return to list ignores late detail and clears body', async () => {
  const old = pending(), f = fixture({ id: 'old', get: () => old.promise }); await flush(); f.route.params.articleId = ''; await flush(); old.resolve({ id: 'old', body: 'old body' }); await flush(); assert.equal(f.state.selectedArticle, null); assert.equal(f.state.entries[0].id, 'a'); f.unmount()
})
await test('body search is debounced and remains server-side', async () => {
  const f = fixture(); await flush(); f.state.search = 'first'; await flush(); f.state.search = 'body-only'; await flush(); await f.timers(); const queries = f.calls.filter(c => c[0] === 'summaries').map(c => c[1].search); assert.deepEqual(queries, ['', 'body-only']); assert.equal(f.calls.filter(c => c[0] === 'categories').length, 1); f.unmount()
})
await test('late search cannot replace cleared-search result', async () => {
  const old = pending(), f = fixture({ summaries: q => q.search ? old.promise : Promise.resolve([{ id: 'current' }]) }); await flush(); f.state.search = 'delayed'; await flush(); await f.timers(); f.state.search = ''; await flush(); old.resolve([{ id: 'old' }]); await flush(); assert.equal(f.state.entries[0].id, 'current'); f.unmount()
})
await test('switching kinds reloads proper categories including return', async () => {
  const f = fixture(); await flush(); f.route.name = 'videos'; await flush(); assert.equal(f.state.categories[0].name, 'video'); f.route.name = 'news'; await flush(); assert.equal(f.state.categories[0].name, 'news'); assert.equal(f.calls.filter(c => c[0] === 'categories').length, 3); f.unmount()
})
await test('failed new detail never displays previous body', async () => {
  const f = fixture({ id: 'old', get: id => id === 'old' ? Promise.resolve({ id, body: 'old' }) : Promise.reject(Error('not found')) }); await flush(); f.route.params.articleId = 'missing'; await flush(); assert.equal(f.state.selectedArticle, null); assert.equal(f.state.loadError, 'not found'); f.unmount()
})
await test('unmounted requests cannot publish state', async () => {
  const old = pending(), f = fixture({ id: 'old', get: () => old.promise }); await flush(); f.unmount(); old.resolve({ id: 'old', body: 'old' }); await flush(); assert.equal(f.state.selectedArticle, null)
})
console.log(`Article summary actual SFC: ${count}/${count} passed`)
