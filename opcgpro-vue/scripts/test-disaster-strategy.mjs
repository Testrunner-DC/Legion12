import assert from 'node:assert/strict'
import fs from 'node:fs'
import { createRequire } from 'node:module'
const require = createRequire(import.meta.url)
const ts = require('typescript')
const source = fs.readFileSync(new URL('../src/l12/disasterStrategy.ts', import.meta.url), 'utf8')
const code = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
const exports = {}
new Function('exports', 'require', code)(exports, id => {
  assert.equal(id, './platform')
  return { localDateBoundary: (date, end) => `${date}-${end ? 'to' : 'from'}` }
})
const url = exports.disasterStrategyPath({ masterId: 'A', opponentMasterId: 'B', initiative: 'second',
  from: '2026-10-01', to: '2026-10-18', effectVersion: 'all', disasterOrigin: 'player' })
const query = new URL(url, 'http://127.0.0.1').searchParams
for (const [key, value] of Object.entries({ masterId: 'A', opponentMasterId: 'B', initiative: 'second',
  fromUtc: '2026-10-01-from', toUtc: '2026-10-18-to', effectVersion: 'all', disasterOrigin: 'player' })) assert.equal(query.get(key), value)
assert.equal(exports.disasterStrategyPath({ masterId: 'A', initiative: 'first' }), '')
const rows = [{ cardId: 'one', opportunities: 1, selections: 1, selectionRate: 1 },
  { cardId: 'large', opportunities: 100, selections: 80, selectionRate: .8 }]
assert.deepEqual(exports.strategyRanking(rows, 30).map(x => x.cardId), ['large'])
assert.equal(rows[0].cardId, 'one', 'ranking must not mutate the cached input')
assert.deepEqual(exports.strategyRanking([{ cardId: 'never', opportunities: 100, selections: 0, selectionRate: 0 }], 30), [],
  'never chosen candidates are not described as a common response')
globalThis.__strategyAlgorithmCases = [{ id: 'strategy-query-scope-mapping', passed: true },
  { id: 'ranking-does-not-promote-one-of-one-over-supported-observations', passed: true }]
console.log('PASS strategy-query-scope-mapping\nPASS ranking-sample-guard')
await import('./test-disaster-analytics-consumers.mjs')
