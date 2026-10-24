import assert from 'node:assert/strict'
import fs from 'node:fs'
import vm from 'node:vm'
import ts from 'typescript'
const read = file => fs.readFileSync(new URL(file, import.meta.url), 'utf8')
const admin = read('../src/l12/site/RankedIntegrityActions.vue')
const notice = read('../src/l12/site/RankedIntegrityNotice.vue')
const history = read('../src/l12/site/RankedPenaltyHistory.vue')
const appeal = read('../src/l12/site/RankedAppealForm.vue')
const api = read('../src/l12/rankedIntegrity.ts')
const app = read('../src/App.vue')
const profile = read('../src/l12/site/ProfilePage.vue')
assert.ok(admin.includes('admin.match-governance.write') && admin.includes('preview.canConfirm') && admin.includes('preview.value.revision'))
assert.ok(admin.includes('formGeneration') && admin.includes("flush: 'sync'"), 'form changes must invalidate delayed preview')
assert.ok(admin.includes('v-model.number="form.days"') && admin.includes('step="1"') && admin.includes('max="3650"'))
assert.ok(notice.includes('notifications(undefined, true)') && notice.includes('integrityApi.acknowledge(item.id)'))
assert.ok(notice.includes('account !== platformState.account?.id') && notice.includes('generation++'))
assert.ok(app.includes('<RankedIntegrityNotice />')
  && profile.includes('class="penalty-history-shell" @toggle="showPenaltyHistory"')
  && profile.includes('if ((event.currentTarget as HTMLDetailsElement).open) visitedPerformance.value = true')
  && profile.includes('<RankedPenaltyHistory v-if="visitedPerformance" />')
  && profile.includes('visitedPerformance.value = false'),
  'private penalty history must remain reachable after user expansion without a default-page read')
assert.ok(history.includes('加载更多记录') && history.includes('加载更多申诉') && history.includes('appeal.reply'))
assert.ok(appeal.includes('requestIds.get(key)') && appeal.includes('maxlength="1000"'))
assert.ok(api.includes("'/api/ranked/integrity/appeals'") && !/notifications.*accountId/.test(api), 'player API must infer own account')
assert.ok(admin.includes('integrityNotificationMessage(preview.playerNotificationAccountIds)')
  && admin.includes('integrityNotificationMessage(decision.playerNotificationAccountIds, true)'),
  'preview and confirmation must use server notification recipients, not disposition or score heuristics')
assert.ok(!admin.includes('对应玩家将收到结果通知') && !admin.includes('确认后会保存处置证据并通知相关玩家'),
  'no-impact decisions must not promise a player notification')
const messageSource = api.match(/export function integrityNotificationMessage\([\s\S]*?\n\}/)?.[0]
assert.ok(messageSource, 'shared notification message must remain testable')
const context = { exports: {} }
vm.runInNewContext(ts.transpileModule(messageSource, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, context)
const message = context.exports.integrityNotificationMessage
assert.equal(message([]), '本次仅保存后台复核记录，不通知玩家。')
assert.equal(message([], true), '本次仅保存后台复核记录，不通知玩家。')
assert.equal(message(['private-a', 'private-b']), '确认后将为 2 名受影响玩家生成结果通知。')
assert.equal(message(['private-a'], true), '已为 1 名受影响玩家生成结果通知。')
assert.equal(message(undefined), '仅在实际影响排位资格或收益时通知相关玩家。', 'old responses must not claim zero notifications')
console.log('Ranked integrity contracts passed: permission, immutable preview, configurable days, private persistent notices/history/appeals and 5 recipient-based message cases.')
