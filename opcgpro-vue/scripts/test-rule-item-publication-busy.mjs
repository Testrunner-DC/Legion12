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
const queueState = statement('pendingPublishTargets')
const publishBusyState = statement('publishPendingBusy')
const queueCount = statement('pendingPublishCount')
const reorderState = statement('centerReorderPending')
const publishTargetKey = fn('publishTargetKey')
const togglePublishQueue = fn('togglePublishQueue')
const publishPending = fn('publishPending')
assert(queueState && publishBusyState && queueCount && reorderState && publishTargetKey && togglePublishQueue && publishPending,
  '批量发布必须保留队列状态、切换入口和统一发布handler')

const code = ts.transpileModule([
  queueState.getText(tree), publishBusyState.getText(tree), queueCount.getText(tree),
  reorderState.getText(tree), publishTargetKey.getText(tree), togglePublishQueue.getText(tree),
  publishPending.getText(tree),
].join('\n'), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const deferred = () => {
  let resolve, reject
  const promise = new Promise((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
const calls = [], notices = []
const pendingApi = deferred()
const adminApi = {
  saveContentDraft: async key => ({ version: key === 'rules.center' ? 8 : 6 }),
  publishRuleItemsBatch: (items, rulingsVersion, centerVersion) => {
    calls.push({ items, rulingsVersion, centerVersion })
    return pendingApi.promise
  },
  contentBatches: async () => [],
}
const environment = {
  ref: value => ({ value }), computed: fn => ({ get value() { return fn() } }), adminApi, notices,
  entries: { value: [] }, rulingVersion: { value: 5 }, publishedRulings: { value: [] },
  centerVersion: { value: 7 }, ruleCenterDraft: { value: '{}' }, ruleCenterDocument: { value: {} },
  publishedCenter: { value: {} }, history: { value: [] },
  notice: value => notices.push(value), normalizeAllRulingProducts: () => {},
  serializeRulingDocument: () => '{}', parseRulingDocument: () => [], parsePublishedRuleCenter: () => ({}),
  save: async () => true, load: async () => {}, mutationBusy: { value: false },
}
const runtime = new Function(...Object.keys(environment), `${code};return {
  pendingPublishTargets,togglePublishQueue,publishPending}`)(...Object.values(environment))

runtime.togglePublishQueue('rules.center', 'coreBlocks', 'CENTER-A')
runtime.togglePublishQueue('rules.rulings', 'entries', 'RULING-A')
assert.deepEqual([...runtime.pendingPublishTargets.value].sort(), [
  'rules.center/coreBlocks/CENTER-A', 'rules.rulings/entries/RULING-A',
])

const publishing = runtime.publishPending()
for (let index = 0; index < 10 && calls.length < 1; index++) await Promise.resolve()
assert.equal(calls.length, 1, 'queued items must be published with one API call')
assert.equal(calls[0].rulingsVersion, 5)
assert.equal(calls[0].centerVersion, 7)
assert.deepEqual(calls[0].items, [
  { key: 'rules.center', collection: 'coreBlocks', itemId: 'CENTER-A' },
  { key: 'rules.rulings', collection: 'entries', itemId: 'RULING-A' },
])
pendingApi.resolve({ entries: [], publishedCount: 2 })
await publishing
assert.equal(runtime.pendingPublishTargets.value.size, 0, 'successful batch clears the queue')
assert(notices.some(value => value.includes('一次性发布')), 'batch success remains visible to the operator')

console.log('Rule item queue toggles and one-shot publication dispatch passed')
