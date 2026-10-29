import assert from 'node:assert/strict'
import fs from 'node:fs'
import ts from 'typescript'
import { createHash } from 'node:crypto'

const file = new URL('../src/l12/site/alternateArtRuleExplanation.ts', import.meta.url)
const source = fs.readFileSync(file, 'utf8'), module = { exports: {} }
new Function('module', 'exports', ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText)(module, module.exports)
const explain = module.exports.alternateArtRuleExplanation
const kinds = ['rank-reached', 'season-final', 'master-champion-season-final', 'event']
let passed = 0
for (const kind of kinds) for (const active of [true, false]) for (let tier = 0; tier < 5; tier++) {
  const rule = { kind, active, seasonId: 'season-qa', eventId: 'event-qa', minimumTierIndex: tier, masterId: '' }
  const before = JSON.stringify(rule), result = explain(rule)
  assert.equal(JSON.stringify(rule), before)
  assert.equal(result.timing.startsWith('当前已停用'), !active)
  assert.match(result.prerequisite, /来源和赛季／活动.*不会重复/)
  if (kind === 'rank-reached') { assert.match(result.condition, /至少完成一场/); assert.match(result.condition, /不要求完成定级/); assert.match(result.timing, /立即检查.*之后每场排位结算/); assert(result.condition.includes(`第 ${tier + 1} 档`)) }
  if (kind === 'season-final') { assert.match(result.condition, /最终七曜值/); assert(result.condition.includes(`第 ${tier + 1} 档`)); assert.match(result.timing, /归档.*保存规则本身不会发放/) }
  if (kind === 'master-champion-season-final') { assert.match(result.condition, /所有主宰的得主/); assert.match(explain({ ...rule, masterId: 'unknown-master' }).condition, /unknown-master/); assert.match(explain({ ...rule, masterId: 'known' }, '吕布').condition, /吕布/) }
  if (kind === 'event') { assert.match(result.condition, /event-qa/); assert.match(result.timing, /选择玩家并执行派发/); assert(!result.condition.includes(`第 ${tier + 1} 档`)) }
  passed++
}
const base = { kind: 'rank-reached', active: true, seasonId: '', eventId: '', masterId: '', minimumTierIndex: -1 }
for (const minimumTierIndex of [-1, 5, 1.5, NaN]) { const r = explain({ ...base, minimumTierIndex }); assert.match(r.condition, /所选赛季/); assert.match(r.condition, /所选最低段位/); assert(!/NaN|第 0 档|第 6 档/.test(r.condition)); passed++ }
assert.match(explain({ ...base, kind: 'unknown' }).timing, /无法确认/); passed++
console.log(JSON.stringify({ suite: 'alternate-art-rule-explanation', passed, failed: 0, sourceSha256: createHash('sha256').update(source).digest('hex') }))
