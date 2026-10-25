import assert from 'node:assert/strict'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const source = fs.readFileSync(path.join(root, 'src/l12/game/PromptOverlay.vue'), 'utf8')
const start = source.indexOf('function waitingText()')
const end = source.indexOf('function kindLabel()', start)
assert(start >= 0 && end > start, 'real PromptOverlay waiting projection was not found')
const projection = ts.transpileModule(source.slice(start, end), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.None },
}).outputText
const render = new Function('waitingPrompt', 'waitingDefense', `${projection}; return waitingText()`) // eslint-disable-line no-new-func
const old = '对手正在决定是否执行开局效果'
const cases = [
  ['disaster-ban', '对手正在禁用天灾'],
  ['disaster-pick', '对手正在选择天灾'],
  ['disaster-reveal', '对手正在确认随机公开天灾'],
  ['trial-order', '对手正在安排试炼顺序'],
]
for (const [kind, expected] of cases) {
  assert.equal(render({ value: { kind, waitingSummary: old } }, { value: false }), expected,
    `${kind}: legacy checkpoint must not call a preparation step an optional effect`)
  assert.equal(render({ value: { kind, waitingSummary: expected } }, { value: false }), expected,
    `${kind}: new authoritative wording must stay intact`)
  assert.equal(render({ value: { kind, waitingSummary: null } }, { value: false }), expected,
    `${kind}: old checkpoint without a presentation needs the kind fallback`)
}
for (const kind of ['optional', 'disaster-trigger', 'unknown'])
  assert.equal(render({ value: { kind, waitingSummary: old } }, { value: false }), old,
    `${kind}: unrelated authoritative meaning must not be rewritten`)
assert.equal(render({ value: null }, { value: false }), '')
console.log('Preparation waiting summary projection: 16 exact source-function cases passed')
