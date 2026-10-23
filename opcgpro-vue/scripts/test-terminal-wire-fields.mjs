import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { fixedPropertyTransform as transform, deltaFieldRanges } from './measure-terminal-wire-fields.mjs'

const names = ['cards', 'name', 'payload', 'state', 'type']
let cases = 0
for (const value of [
  { name: '中文😀', '@0': 1, '~x': { name: 'private' } },
  { name: '"name": "never touch values"', state: { array: [null, true, { type: 'full' }] } },
  JSON.parse('{"__proto__":null,"constructor":"plain JSON","payload":{"name":"<script>&"}}'),
  { '未知字段': { cards: [], '@zz': 3 }, '~@0': { name: 'x' } },
]) {
  const raw = JSON.stringify(value), packed = transform(raw, names)
  assert.deepEqual(JSON.parse(transform(packed, names, true)), JSON.parse(raw))
  assert.equal(transform(packed, names, true), raw); cases++
}
for (const invalid of ['{', '{"name":}', '{"@unknown":1}']) {
  assert.throws(() => transform(invalid, names, true)); cases++
}
// Fixed property substitutions have no public/private string-equality interaction.
const bytes = (publicText, privateText) => Buffer.byteLength(transform(JSON.stringify({
  state: { name: publicText, payload: privateText }, cards: [{ name: privateText }],
}), names))
assert.equal(bytes('PUBLIC-A', 'SECRET-X') - bytes('PUBLIC-A', 'SECRET-Y')
  - bytes('PUBLIC-B', 'SECRET-X') + bytes('PUBLIC-B', 'SECRET-Y'), 0); cases++

// The real card-frame reducer intentionally fills payload cards in place. Analyze
// the immutable raw wire separately; do not misclassify this as a transport error.
const source = fs.readFileSync(new URL('../src/l12/gameStateCardFrame.ts', import.meta.url), 'utf8')
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS } }).outputText
const exports = {}; new Function('exports', compiled)(exports)
const wire = { type: 'gameStateCardFrameV1', payload: { type: 'gameState', state: {
  recentEvents: [{ cards: null }], lastAction: null,
} }, cardSnapshots: [{ name: '合成卡' }], eventCardRefs: [[0]], lastActionCardRefs: null }
const raw = JSON.stringify(wire)
const restored = exports.restoreGameStateCardFrame(structuredClone(wire), raw)
assert.deepEqual(restored.state.recentEvents[0].cards, [{ name: '合成卡' }])
assert.equal(JSON.stringify(wire), raw)
assert.deepEqual(JSON.parse(transform(transform(raw, names), names, true)), wire); cases++
const escaped = '{"changes":[{"path":["state","name"],"value":"\\u003C中文\\u003E","remove":false}],"eventWindow":{"append":[]}}'
const exact = deltaFieldRanges(escaped)
assert.equal(exact.changes[0].valueBytes, Buffer.byteLength('"\\u003C中文\\u003E"'))
assert.equal(exact.changes[0].changeBytes, Buffer.byteLength('{"path":["state","name"],"value":"\\u003C中文\\u003E","remove":false}'))
assert.equal(exact.eventWindowBytes, Buffer.byteLength('{"append":[]}'))
assert(exact.changes[0].valueBytes > Buffer.byteLength(JSON.stringify(JSON.parse(escaped).changes[0].value))); cases++
assert.throws(() => deltaFieldRanges('{"changes":[{}]}')); cases++
console.log(JSON.stringify({ status: 'passed', cases, networkCalls: 0, productChanges: 0 }))
