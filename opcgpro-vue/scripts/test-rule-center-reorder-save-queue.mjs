import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import ts from 'typescript'
import { parse } from '@vue/compiler-sfc'

const source = readFileSync(new URL('../src/l12/site/AdminRuleRulingsPanel.vue', import.meta.url), 'utf8')
const moveStart = source.indexOf('async function moveCenterItem')
const moveEnd = source.indexOf('function deleteCenterItem', moveStart)
assert.notEqual(moveStart, -1, 'rule-center move handler missing')
assert.notEqual(moveEnd, -1, 'rule-center move handler boundary missing')
const moveHandler = source.slice(moveStart, moveEnd)

assert(source.includes('const centerReorderPending = ref(false)'), 'reorder pending state missing')
assert(source.includes('function scheduleCenterReorderSave(message: string): Promise<void>'),
  'reorder save queue entry missing')
assert(source.includes('async function flushCenterReorderSave()'), 'reorder save flush missing')
assert(moveHandler.includes('await scheduleCenterReorderSave('),
  'rapid reorder must use the coalescing save queue')
assert(!moveHandler.includes('await saveCenter('),
  'rapid reorder must not issue one full save per click')
assert(source.includes('centerReorderPending.value = false'),
  'reorder queue must release the pending guard after the final save')
assert(source.includes('centerReorderDirty = true'),
  'reorder queue must retain changes that arrive during an in-flight save')

const descriptor = parse(source).descriptor
assert(descriptor.scriptSetup, 'rule-center script setup missing')
const tree = ts.createSourceFile('AdminRuleRulingsPanel.ts', descriptor.scriptSetup.content,
  ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
const statement = name => tree.statements.find(node => ts.isVariableStatement(node)
  && node.declarationList.declarations.some(item => item.name.getText(tree) === name))
const fn = name => tree.statements.find(node => ts.isFunctionDeclaration(node) && node.name?.text === name)
const stateNames = ['centerReorderPending', 'centerReorderTimer', 'centerReorderInFlight',
  'centerReorderDirty', 'centerReorderMessage', 'centerReorderWaiters']
const functions = ['settleCenterReorderWaiters', 'scheduleCenterReorderFlush',
  'scheduleCenterReorderSave', 'flushCenterReorderSave']
const states = stateNames.map(statement)
const handlers = functions.map(fn)
assert(states.every(Boolean) && handlers.every(Boolean), 'reorder queue runtime surface missing')
const code = ts.transpileModule([
  ...states.map(node => node.getText(tree)),
  ...handlers.map(node => node.getText(tree)),
].join('\n'), { compilerOptions: { target: ts.ScriptTarget.ES2022 } }).outputText
const notices = []
let saveCalls = 0
let releaseSave
const saveCenter = async () => {
  saveCalls += 1
  await new Promise(resolve => { releaseSave = resolve })
}
const environment = {
  ref: value => ({ value }), saveCenter, notice: value => notices.push(value),
}
const runtime = new Function(...Object.keys(environment), `${code};return {
  centerReorderPending,scheduleCenterReorderSave
}`)(...Object.values(environment))
const first = runtime.scheduleCenterReorderSave('first')
const second = runtime.scheduleCenterReorderSave('second')
await new Promise(resolve => setTimeout(resolve, 260))
assert.equal(saveCalls, 1, 'rapid reorder clicks must coalesce into one save')
releaseSave()
await Promise.all([first, second])
assert.equal(runtime.centerReorderPending.value, false, 'coalesced save must release pending state')
assert.deepEqual(notices, ['second'], 'coalesced save should announce the latest reorder once')

const third = runtime.scheduleCenterReorderSave('third')
await new Promise(resolve => setTimeout(resolve, 260))
assert.equal(saveCalls, 2, 'the first queued save should start exactly once')
const fourth = runtime.scheduleCenterReorderSave('fourth')
releaseSave()
await new Promise(resolve => setTimeout(resolve, 260))
assert.equal(saveCalls, 3, 'changes arriving during a save must schedule one follow-up save')
releaseSave()
await Promise.all([third, fourth])
assert.equal(runtime.centerReorderPending.value, false, 'follow-up save must release pending state')
assert.deepEqual(notices, ['second', 'fourth'], 'follow-up save should announce the latest reorder once')

console.log('Rule-center reorder save queue contract passed')
