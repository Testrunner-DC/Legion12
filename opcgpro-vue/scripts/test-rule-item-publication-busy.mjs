import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const source = readFileSync(new URL('../src/l12/site/AdminRuleRulingsPanel.vue', import.meta.url), 'utf8')
const descriptor = parse(source).descriptor
assert(descriptor.scriptSetup, 'AdminRuleRulingsPanel script setup missing')
const tree = ts.createSourceFile('AdminRuleRulingsPanel.ts', descriptor.scriptSetup.content,
  ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const statement = name => tree.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(tree) === name))
const fn = name => tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
const centerState = statement('publishingCenterIds')
const rulingState = statement('publishingRulingIds')
const publishCenterItem = fn('publishCenterItem')
const publishRuling = fn('publishRuling')
assert(centerState && rulingState && publishCenterItem && publishRuling,
  '发布忙碌状态必须按center/ruling文档分别建Set并保留两个真实handler')

const code = ts.transpileModule([
  centerState.getText(tree), rulingState.getText(tree),
  publishCenterItem.getText(tree), publishRuling.getText(tree),
].join('\n'), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const deferred = () => {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const calls = [], notices = []
const adminApi = {
  saveContentDraft: async key => ({ version: key === 'rules.center' ? 8 : 6 }),
  publishRuleItem: (key, collection, id) => {
    const pending = deferred(); calls.push({ key, collection, id, ...pending }); return pending.promise
  },
  contentBatches: async () => [],
}
const environment = {
  ref: value => ({ value }), adminApi, notices,
  entries: { value: [] }, rulingVersion: { value: 5 }, publishedRulings: { value: [] },
  centerVersion: { value: 7 }, ruleCenterDraft: { value: '{}' }, ruleCenterDocument: { value: {} },
  publishedCenter: { value: {} }, history: { value: [] },
  notice: value => notices.push(value), normalizeAllRulingProducts: () => {},
  serializeRulingDocument: () => '{}', parseRulingDocument: () => [], parsePublishedRuleCenter: () => ({}),
}
const runtime = new Function(...Object.keys(environment), `${code};return {
  publishingCenterIds,publishingRulingIds,publishCenterItem,publishRuling}`)(...Object.values(environment))
const settle = async count => {
  for (let index = 0; index < 10 && calls.length < count; index++) await Promise.resolve()
  assert.equal(calls.length, count, `expected ${count} publish API calls`)
}
const published = { version: 9, draftValue: '{}', publishedValue: '{}' }

const first = runtime.publishCenterItem('coreBlocks', 'CENTER-A')
const second = runtime.publishCenterItem('terms', 'CENTER-B')
await settle(2)
assert.deepEqual([...runtime.publishingCenterIds.value].sort(), ['CENTER-A', 'CENTER-B'])
await runtime.publishCenterItem('coreBlocks', 'CENTER-A')
assert.equal(calls.length, 2, 'same center item must no-op while pending')
calls[0].resolve(published); await first
assert.deepEqual([...runtime.publishingCenterIds.value], ['CENTER-B'], 'success cleared another center item')
calls[1].reject(new Error('synthetic center failure')); await second
assert.equal(runtime.publishingCenterIds.value.size, 0, 'failed center item did not clear itself')

const sameCenter = runtime.publishCenterItem('coreBlocks', 'SAME-ID')
const sameRuling = runtime.publishRuling({ id: 'SAME-ID' })
await settle(4)
assert(runtime.publishingCenterIds.value.has('SAME-ID'))
assert(runtime.publishingRulingIds.value.has('SAME-ID'), 'same ID across documents must not collide')
await runtime.publishRuling({ id: 'SAME-ID' })
assert.equal(calls.length, 4, 'same ruling item must no-op while pending')
calls[2].resolve(published); await sameCenter
assert(runtime.publishingRulingIds.value.has('SAME-ID'), 'center success cleared ruling busy state')
calls[3].reject(new Error('版本冲突')); await sameRuling
assert.equal(runtime.publishingRulingIds.value.size, 0, 'ruling failure did not clear itself')
assert(notices.some(value => value.includes('版本冲突')), 'existing version conflict must remain visible')

console.log('Rule item per-document busy sets, duplicate guards, independent finally cleanup and conflict reporting passed')
