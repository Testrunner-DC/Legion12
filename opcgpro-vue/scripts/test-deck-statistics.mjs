import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
const editor=fs.readFileSync(new URL('../src/l12/L12DeckEditor.vue',import.meta.url),'utf8')
assert(editor.includes('统计/代表'), 'workspace label must be 统计/代表')
assert(!editor.includes('Math.min(8, card.cost ?? 0)'), 'unknown cost must not become zero')
const source=fs.readFileSync(new URL('../src/l12/deckStatistics.ts',import.meta.url),'utf8')
const module={exports:{}}
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{module,exports:module.exports,Number,Math,Array})
const build=module.exports.buildDeckCostStatistics
const entries=[{card:{cost:0},count:3},{card:{cost:3},count:2},{card:{cost:8},count:1},{card:{cost:14},count:2},{card:{cost:null},count:4},{card:{},count:1},{card:{cost:-1},count:2},{card:{cost:NaN},count:1}]
const actual=build(entries)
assert.deepEqual(Array.from(actual.curve),[3,0,0,2,0,0,0,0,3])
assert.equal(actual.unknown,8)
assert.equal(actual.curve.reduce((a,b)=>a+b,0)+actual.unknown,16)
assert.deepEqual(Array.from(build([]).curve),[0,0,0,0,0,0,0,0,0])
assert.equal(build([]).unknown,0)
console.log('Deck cost statistics: zero, 8+, missing, invalid and empty cases passed')