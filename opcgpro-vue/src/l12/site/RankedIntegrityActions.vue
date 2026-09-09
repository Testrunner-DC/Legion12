<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { platformState, type RankedIntegrityAudit } from '../platform'
import { integrityApi, integrityLabel, integrityRequestId, type IntegrityAction, type IntegrityPreview, type IntegrityDecision, type IntegrityAppeal, type IntegrityDisposition } from '../rankedIntegrity'
const props = defineProps<{ rows: RankedIntegrityAudit[]; selected: string[] }>()
const emit = defineEmits<{ refresh: [] }>()
const form = reactive({ disposition: 'review' as IntegrityDisposition, days: 7, restricted: [] as string[], reason: '', evidence: '', revoke: '' })
const preview = ref<IntegrityPreview | null>(null); const frozenInput = ref<IntegrityAction | null>(null)
const busy = ref(false); const notice = ref(''); const decisions = ref<IntegrityDecision[]>([]); const appeals = ref<IntegrityAppeal[]>([])
const decisionCursor = ref<string | null>(); const appealCursor = ref<string | null>()
let formGeneration = 0
const replies = reactive<Record<string, string>>({}); const replyRequests = new Map<string, string>()
const canWrite = computed(() => platformState.account?.permissions?.includes('admin.match-governance.write') ?? false)
const participants = computed(() => {
  const names = new Map<string, string>()
  props.rows.filter(row => props.selected.includes(row.matchId)).forEach(row => { names.set(row.firstAccountId, row.firstPlayer); names.set(row.secondAccountId, row.secondPlayer) })
  return [...names].map(([id, name]) => ({ id, name }))
})
const targets = computed(() => form.revoke ? decisions.value.find(row => row.decisionId === form.revoke)?.matchIds || [] : props.selected)
watch([() => JSON.stringify(form), () => props.selected.join('|'), () => platformState.account?.id], () => { formGeneration++; preview.value = null; frozenInput.value = null }, { flush: 'sync' })
watch(() => props.selected.join('|'), () => { form.restricted = form.restricted.filter(id => participants.value.some(p => p.id === id)); form.revoke = '' })
async function loadHistory() {
  try {
    const [history, incoming] = await Promise.all([integrityApi.decisions(), integrityApi.adminAppeals()])
    decisions.value = history.items; decisionCursor.value = history.nextCursor
    appeals.value = incoming.items; appealCursor.value = incoming.nextCursor
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '加载处置记录失败' }
}
async function makePreview() {
  if (busy.value || !canWrite.value) return
  busy.value = true; notice.value = ''; preview.value = null
  const generation = formGeneration
  const input: IntegrityAction = {
    requestId: integrityRequestId(), disposition: form.revoke ? 'revoked' : form.disposition,
    matchIds: [...targets.value], restrictedAccountIds: !form.revoke && form.disposition === 'confirmed' ? [...form.restricted] : [],
    restrictionDays: !form.revoke && form.disposition === 'confirmed' && form.restricted.length ? Number(form.days) : null,
    evidence: form.evidence.trim(), reason: form.reason.trim(), revokesDecisionId: form.revoke || null,
  }
  try { const result = await integrityApi.preview(input); if (generation === formGeneration) { frozenInput.value = input; preview.value = result } }
  catch (cause) { notice.value = cause instanceof Error ? cause.message : '预览失败' }
  finally { busy.value = false }
}
async function confirm() {
  if (busy.value || !preview.value?.canConfirm || !frozenInput.value || !canWrite.value) return
  busy.value = true; notice.value = ''
  try {
    const decision = await integrityApi.confirm(frozenInput.value, preview.value.revision)
    notice.value = `已登记：${integrityLabel(decision.disposition)}。对应玩家将收到结果通知。`
    preview.value = null; frozenInput.value = null; form.revoke = ''; await loadHistory(); emit('refresh')
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '确认失败；请核对状态后重试' }
  finally { busy.value = false }
}
function prepareRevoke(decision: IntegrityDecision) { form.revoke = decision.decisionId; form.disposition = 'revoked'; form.reason = ''; form.evidence = ''; form.restricted = []; notice.value = '请填写撤销理由与复核证据，再预览影响。' }
async function reply(appeal: IntegrityAppeal, status: string) {
  if (busy.value || !canWrite.value || !replies[appeal.id]?.trim()) return
  busy.value = true; notice.value = ''
  const key = `${appeal.id}:${appeal.revision}:${status}:${replies[appeal.id].trim()}`
  if (!replyRequests.has(key)) replyRequests.set(key, integrityRequestId())
  try { await integrityApi.reply(appeal.id, replyRequests.get(key)!, appeal.revision, status, replies[appeal.id].trim()); notice.value = '申诉回复已保存并通知本人。此操作不会自动解除排位限制。'; await loadHistory() }
  catch (cause) { notice.value = cause instanceof Error ? cause.message : '回复失败，请重试' }
  finally { busy.value = false }
}
async function more(kind: 'decisions' | 'appeals') {
  if (busy.value) return
  busy.value = true
  try {
    if (kind === 'decisions' && decisionCursor.value) { const page = await integrityApi.decisions(decisionCursor.value); decisions.value.push(...page.items); decisionCursor.value = page.nextCursor }
    if (kind === 'appeals' && appealCursor.value) { const page = await integrityApi.adminAppeals(appealCursor.value); appeals.value.push(...page.items); appealCursor.value = page.nextCursor }
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '加载失败' }
  finally { busy.value = false }
}
onMounted(loadHistory)
</script>
<template>
  <section class="integrity-actions" data-ui-contract="ranked-integrity-disposition">
    <h3>复核与处置</h3><p>选中具体对局后按证据处置。风险信号不等于违规；同IP不单独作为处罚依据。积分纠正不安全时服务器会拒绝确认。</p>
    <p v-if="!canWrite">当前账号仅可查看；处置需要对局治理写入权限。</p>
    <form v-if="canWrite" @submit.prevent="makePreview"><fieldset :disabled="busy">
      <p>本次关联 {{ targets.length }} 场（最多50场）<span v-if="form.revoke"> · 撤销原处置 {{ form.revoke }}</span></p>
      <label>结论<select v-model="form.disposition" :disabled="!!form.revoke"><option value="review">待复核</option><option value="normal">复核正常</option><option value="insufficient">证据不足，不处罚</option><option value="system-error">系统异常，纠正结果</option><option value="confirmed">确认违规，撤销相关收益</option><option v-if="form.revoke" value="revoked">撤销原处置</option></select></label>
      <div v-if="form.disposition === 'confirmed' && !form.revoke" class="restriction"><b>另行限制排位的账号（不勾选则不封禁）：</b><label v-for="player in participants" :key="player.id"><input v-model="form.restricted" type="checkbox" :value="player.id"/>{{ player.name }}<small>{{ player.id }}</small></label><label v-if="form.restricted.length">封禁天数<input v-model.number="form.days" type="number" min="1" max="3650" step="1" required/><small>按完整天数设置；预览显示确切解禁时间。只限制排位，不阻断登录及申诉。</small></label></div>
      <label>核查证据<textarea v-model="form.evidence" required maxlength="2000" rows="3" placeholder="填写服务器记录、结算及对局证据，不以玩家猜测直接判罚。"/></label>
      <label>给玩家的处置原因<textarea v-model="form.reason" required maxlength="1000" rows="3" placeholder="说明违规事实或复核结论，不包含IP、设备标识、其他玩家隐私。"/></label>
      <button :disabled="!targets.length || targets.length > 50">预览影响</button><button v-if="form.revoke" type="button" @click="form.revoke = ''; form.disposition = 'review'">取消撤销操作</button>
    </fieldset></form>
    <div v-if="preview" class="preview"><h4>确认前预览</h4><p v-for="reason in preview.blockingReasons" :key="reason" class="error">{{ reason }}</p><article v-for="effect in preview.accountEffects" :key="effect.accountId"><b>{{ effect.username }}</b><span>七曜值调整 {{ effect.scoreDelta > 0 ? '+' : '' }}{{ effect.scoreDelta }}</span><span>{{ effect.rewardOutcome }}</span><span v-if="effect.restrictionUntil">排位限制截至 {{ new Date(effect.restrictionUntil).toLocaleString() }}</span><span v-if="effect.blockedReason" class="error">{{ effect.blockedReason }}</span></article><p>确认后会保存处置证据并通知相关玩家。无需第二名管理员批准。</p><button :disabled="busy || !preview.canConfirm" @click="confirm">{{ busy ? '处理中…' : '确认执行本次处置' }}</button></div>
    <p v-if="notice" role="status" class="notice">{{ notice }}</p>
    <h3>处置历史 <button :disabled="busy" @click="loadHistory">刷新</button></h3>
    <div class="history"><article v-for="decision in decisions" :key="decision.decisionId">
      <h4>{{ integrityLabel(decision.effectiveDisposition) }} · {{ new Date(decision.createdAt).toLocaleString() }}</h4>
      <p>{{ decision.reason }}</p><small>{{ decision.actorName }} · {{ decision.matchIds.length }}场 · {{ decision.decisionId }}</small>
      <details><summary>证据及调整详情</summary><p>{{ decision.evidence }}</p><p v-for="effect in decision.accountEffects" :key="effect.accountId">{{ effect.username }}：七曜值 {{ effect.scoreDelta > 0 ? '+' : '' }}{{ effect.scoreDelta }}；{{ effect.rewardOutcome }}</p></details>
      <button v-if="canWrite && ['confirmed', 'system-error'].includes(decision.disposition) && !decision.revokedByDecisionId" :disabled="busy" @click="prepareRevoke(decision)">复核撤销</button>
    </article></div><button v-if="decisionCursor" :disabled="busy" @click="more('decisions')">加载更多处置</button>
    <h3>玩家申诉</h3><div class="history"><article v-for="appeal in appeals" :key="appeal.id"><h4>{{ appeal.username }} · {{ ({ open: '待处理', reviewing: '复核中', answered: '已回复', closed: '已结案' } as Record<string,string>)[appeal.status] || appeal.status }}</h4><p>{{ appeal.statement }}</p><small>处置 {{ appeal.decisionId }} · {{ new Date(appeal.createdAt).toLocaleString() }}</small><p v-if="appeal.reply">已回复：{{ appeal.reply }}</p><template v-if="canWrite"><textarea v-model="replies[appeal.id]" maxlength="1000" rows="3" :disabled="busy" placeholder="填写给玩家的复核进度或结论；撤销处罚须在处置历史另行预览确认。"/><div class="buttons"><button :disabled="busy || !replies[appeal.id]?.trim()" @click="reply(appeal, 'reviewing')">进入复核</button><button :disabled="busy || !replies[appeal.id]?.trim()" @click="reply(appeal, 'answered')">回复玩家</button><button :disabled="busy || !replies[appeal.id]?.trim()" @click="reply(appeal, 'closed')">回复并结案</button></div></template></article><p v-if="!appeals.length">暂无申诉。</p></div><button v-if="appealCursor" :disabled="busy" @click="more('appeals')">加载更多申诉</button>
  </section>
</template>
<style scoped>
.integrity-actions{margin-top:24px;border-top:1px solid #52616a;padding-top:14px;color:#dbe5e6;overflow-wrap:anywhere}.integrity-actions p{line-height:1.6;white-space:pre-wrap}.integrity-actions fieldset{border:0;padding:0;display:grid;gap:12px}.integrity-actions label{display:grid;gap:6px}.integrity-actions select,.integrity-actions input,.integrity-actions textarea{box-sizing:border-box;width:100%;max-width:100%;padding:10px;border:1px solid #54636b;background:#0c141c;color:#edf2ef;font:inherit}.integrity-actions textarea{resize:vertical}.restriction{padding:12px;border:1px solid #6f583b;display:grid;gap:10px}.restriction label{display:flex;align-items:center;flex-wrap:wrap;gap:8px}.restriction input[type="checkbox"]{width:auto}.restriction input[type="number"]{width:120px}.integrity-actions small{display:block;color:#a3b3be}.integrity-actions button{min-height:40px;padding:8px 14px;border:1px solid #897546;background:#332c1a;color:#f1dfac;font:inherit;cursor:pointer}.integrity-actions button:disabled{opacity:.5;cursor:default}.preview{margin:18px 0;padding:16px;border:1px solid #d0ac5c;background:#262216}.preview article{display:grid;gap:6px}.history{max-height:500px;overflow:auto;overscroll-behavior:contain}.integrity-actions article{padding:12px 0;border-top:1px solid #384952}.integrity-actions h4{margin:5px 0}.notice,.error{color:#edb4a9}.notice{padding:12px;border-left:3px solid #bc8f49;background:#271c12}.buttons{display:flex;gap:8px;flex-wrap:wrap;margin-top:8px}
</style>
