<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { adminApi, authState, hasPermission, platformState, playerApi, type TournamentReputationHistoryItem, type TournamentReputationPage } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const props = withDefaults(defineProps<{ mode?: 'self' | 'admin'; accountId?: string }>(), { mode: 'self' })
const route = useRoute()
const data = ref<TournamentReputationPage | null>(null)
const loading = ref(false)
const error = ref(false)
const historyOpen = ref(false)
const correction = ref<TournamentReputationHistoryItem | null>(null)
const correctionReason = ref('')
const correctionError = ref('')
const correctionBusy = ref(false)
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
let correctionGeneration = 0
let generation = 0
let disposed = false
const canRead = computed(() => authState.verified && !!platformState.token && !!platformState.account?.id
  && !platformState.account.disabled && !platformState.account.deleted
  && !platformState.account.mustChangePassword && !platformState.account.mustChangeUsername
  && (props.mode === 'self' || (hasPermission('admin.accounts.read') && !!props.accountId
    && String(route.params.accountId || '') === props.accountId)))
const qualification = computed(() => data.value?.qualification === 'blocked' ? '暂不能新报名或加入候补'
  : data.value?.qualification === 'warning' ? '报名可用，请留意履约' : '报名可用')
const canCorrect = computed(() => canRead.value && props.mode === 'admin' && props.accountId !== platformState.account?.id
  && hasPermission('admin.accounts.status.write'))
const assessmentLabels = { deducted: '扣分', 'no-additional-deduction': '不重复扣分', revoked: '已撤销', exempt: '免责', correction: '重新核算' } as const
const exemptionLabels: Record<string, string> = {
  'before-check-in': '签到开始前取消', 'unpromoted-waitlist': '候补未转正',
  'organizer-cancelled': '主办取消', 'organizer-postponed': '主办延期',
}
const time = (value: string) => new Date(value).toLocaleString('zh-CN')
const kind = (item: TournamentReputationHistoryItem) => item.kind === 'withdrawal' ? '本人退赛' : item.kind === 'no-show' ? '未到场' : '记录纠正'
const change = (item: TournamentReputationHistoryItem) => item.effectiveScoreChange === null ? '按有效事实重新核算'
  : item.effectiveScoreChange < 0 ? `${item.effectiveScoreChange} 分` : '0 分'
function clearCorrection() {
  correctionGeneration++; correction.value = null; correctionReason.value = ''; correctionError.value = ''
  correctionBusy.value = false; riskAction.value = null
}
async function previewCorrection() {
  const original = correction.value, snapshot = data.value, reason = correctionReason.value.trim()
  if (!canCorrect.value || !original || !snapshot || correctionBusy.value || riskBusy.value) return
  if (!reason || reason.length > 500) { correctionError.value = '请填写1至500字的撤销原因'; return }
  const epoch = ++correctionGeneration, actor = platformState.account?.id, token = platformState.token
  const path = route.fullPath, target = props.accountId!, revision = snapshot.revision
  const current = () => !disposed && epoch === correctionGeneration && canCorrect.value
    && actor === platformState.account?.id && token === platformState.token && target === props.accountId
    && path === route.fullPath && data.value?.revision === revision && correction.value?.id === original.id
  correctionBusy.value = true; correctionError.value = ''
  try {
    const preview = await adminApi.previewAccountTournamentReputationRevoke(target, original.id, reason, revision)
    if (!current()) return
    if (preview.accountId !== target || preview.eventId !== original.id || preview.reputationRevision !== revision
      || !Number.isSafeInteger(preview.platformVersion) || !Number.isSafeInteger(preview.tournamentVersion)
      || ![preview.before?.score, preview.after?.score].every(score => Number.isInteger(score) && score >= 0 && score <= 100))
      throw new Error('预演结果无效，请重新读取')
    if (preview.alreadyRevoked) { correctionError.value = '原处置已撤销，请刷新记录'; return }
    const frozen = Object.freeze({ reason, expectedVersion: preview.platformVersion,
      expectedTournamentVersion: preview.tournamentVersion, expectedReputationRevision: revision,
      idempotencyKey: `reputation-revoke-${globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`}` })
    const recovery = (value: string | null) => value ? time(value) : '无待恢复'
    requestRiskAction({ title: '撤销赛事信誉处置', target, targetLabel: '账号', severity: 'warning', confirmLabel: '确认撤销',
      impact: `撤销「${original.tournamentName}」的${kind(original)}事实并重新核算，不删除原记录、不改赛事赛果。当前预演 ${preview.before.score} → ${preview.after.score} 分；恢复时间 ${recovery(preview.before.nextRecoveryAt)} → ${recovery(preview.after.nextRecoveryAt)}。执行时按有效事实重算，不是固定返还分数。原因：${reason}`,
      run: async () => {
        if (!current() || correctionReason.value.trim() !== reason) throw new Error('账号、记录或原因已变化，请重新预演')
        await adminApi.revokeAccountTournamentReputation(target, original.id, frozen)
        if (!current()) return
        clearCorrection(); await load()
      },
    })
  } catch (failure) { if (current()) correctionError.value = failure instanceof Error ? failure.message : '预演失败，请重试' }
  finally { if (current()) correctionBusy.value = false }
}
function valid(value: TournamentReputationPage): boolean {
  const integer = (n: unknown) => typeof n === 'number' && Number.isSafeInteger(n)
  const date = (n: unknown) => typeof n === 'string' && Number.isFinite(Date.parse(n))
  return !!value && value.ruleVersion === 1 && integer(value.score) && value.score >= 0 && value.score <= 100
    && ['normal', 'warning', 'blocked'].includes(value.qualification) && date(value.asOf)
    && integer(value.revision) && value.revision >= 0 && (value.nextRecoveryAt === null || date(value.nextRecoveryAt))
    && integer(value.page) && value.page >= 1 && value.pageSize === 20 && integer(value.total) && value.total >= 0
    && Array.isArray(value.items) && value.items.length <= 20 && value.items.every(item => item
      && typeof item.id === 'string' && integer(item.sequence) && typeof item.tournamentId === 'string'
      && typeof item.tournamentName === 'string' && ['withdrawal', 'no-show', 'revoke'].includes(item.kind)
      && date(item.effectiveAt) && date(item.recordedAt) && Object.hasOwn(assessmentLabels, item.assessment)
      && (item.effectiveScoreChange === null ? item.assessment === 'correction'
        : integer(item.effectiveScoreChange) && item.effectiveScoreChange >= -20 && item.effectiveScoreChange <= 0))
}
async function load(page = 1, revision?: number) {
  if (!canRead.value || disposed) return
  const epoch = ++generation, actor = platformState.account?.id, token = platformState.token
  const path = route.fullPath, mode = props.mode, target = props.accountId
  const current = () => !disposed && epoch === generation && canRead.value
    && actor === platformState.account?.id && token === platformState.token && path === route.fullPath
    && mode === props.mode && target === props.accountId
  clearCorrection(); data.value = null; error.value = false; loading.value = true
  try {
    const query = { page, pageSize: 20, ...(revision === undefined ? {} : { expectedRevision: revision }) }
    const result = mode === 'admin' ? await adminApi.accountTournamentReputation(target!, query) : await playerApi.tournamentReputation(query)
    if (!current()) return
    if (!valid(result) || result.page !== page) throw new Error('Invalid reputation response')
    data.value = result
  } catch { if (current()) { data.value = null; error.value = true } }
  finally { if (current()) loading.value = false }
}
function move(direction: number) {
  const snapshot = data.value
  if (!snapshot || loading.value) return
  void load(snapshot.page + direction, snapshot.revision)
}
watch(() => [platformState.account?.id, platformState.token, authState.verified, canRead.value, canCorrect.value, route.fullPath, props.mode, props.accountId], () => {
  clearCorrection()
  generation++; data.value = null; loading.value = false; error.value = false; historyOpen.value = false
  if (canRead.value) void load()
}, { immediate: true, flush: 'sync' })
onBeforeUnmount(() => { disposed = true; generation++; clearCorrection(); data.value = null; error.value = false; historyOpen.value = false })
</script>

<template>
  <section v-if="canRead" class="reputation-panel" aria-label="赛事履约信誉">
    <AdminRiskActionDialog v-if="canCorrect && riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction" />
    <header><div><h2>{{ mode === 'admin' ? '该账号的赛事信誉' : '我的赛事信誉' }}</h2><p>独立赛事履约分，不影响七曜、段位或胜率。</p></div><button type="button" :disabled="loading" @click="load()">{{ error ? '重试' : '刷新' }}</button></header>
    <p v-if="loading" role="status">正在读取信誉…</p>
    <p v-else-if="error" role="alert">信誉未知，暂时无法读取；请稍后重试。</p>
    <template v-else-if="data">
      <div class="reputation-score"><strong>{{ data.score }}<small> / 100</small></strong><span>{{ qualification }}</span></div>
      <p>已有报名不会因后来降分被清退。每满 7 天无新有效违规恢复 5 分，上限 100。</p>
      <p v-if="data.nextRecoveryAt">下次恢复时间：{{ time(data.nextRecoveryAt) }}</p>
      <small>读取于 {{ time(data.asOf) }}</small>
      <details :open="historyOpen" @toggle="historyOpen = ($event.currentTarget as HTMLDetailsElement).open">
        <summary>信誉记录（{{ data.total }} 条） · 展开 / 收起</summary>
        <p>以下为撤销、免责与单赛事上限核算后的当前有效变化；纠正不等于固定返还分数。</p>
        <ol><li v-for="item in data.items" :key="item.id"><div><b>{{ item.tournamentName || '赛事' }}</b><span>{{ kind(item) }} · {{ assessmentLabels[item.assessment] }}</span></div><strong>{{ change(item) }}</strong><small>事实时间 {{ time(item.effectiveAt) }}</small><small v-if="item.exemption">{{ exemptionLabels[item.exemption] || '已免责' }}</small><button v-if="canCorrect && ['deducted', 'no-additional-deduction'].includes(item.assessment)" type="button" :disabled="correctionBusy || riskBusy" @click="correction = item; correctionReason = ''; correctionError = ''">撤销此处置</button></li></ol>
        <form v-if="canCorrect && correction" class="correction-form" @submit.prevent="previewCorrection"><label>撤销原因（仅纠正已有处置）<textarea v-model="correctionReason" rows="3" maxlength="500" :disabled="correctionBusy || riskBusy" /></label><p v-if="correctionError" role="alert">{{ correctionError }}</p><button type="submit" :disabled="correctionBusy || riskBusy" @click.prevent="previewCorrection">预演撤销</button><p>需核实原事实；涉及本人或自己的桌次请由无利益冲突的其他管理员处理。</p></form>
        <p v-if="!data.items.length">暂无信誉记录。</p>
        <nav v-if="data.total > data.pageSize" aria-label="信誉记录分页"><button type="button" :disabled="loading || data.page <= 1" @click="move(-1)">上一页</button><span>第 {{ data.page }} / {{ Math.max(1, Math.ceil(data.total / data.pageSize)) }} 页</span><button type="button" :disabled="loading || data.page * data.pageSize >= data.total" @click="move(1)">下一页</button></nav>
      </details>
    </template>
  </section>
</template>

<style scoped>
.reputation-panel{min-width:0;margin:14px 0;padding:18px;border:1px solid #35424a;background:#101821;color:#e3e9ec;font-family:inherit;overflow-wrap:anywhere}.reputation-panel header{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.reputation-panel h2{margin:0;font-size:18px}.reputation-panel p{color:#9aa9b2;line-height:1.65}.reputation-panel small{color:#9aa9b2}.reputation-panel button{flex-shrink:0;min-height:44px;padding:8px 14px;border:1px solid #53646e;background:#0b141d;color:#e6d199;font:inherit;cursor:pointer}.reputation-panel button:disabled{opacity:.5;cursor:default}.reputation-panel button:focus-visible,.reputation-panel summary:focus-visible{outline:2px solid #e1c16c;outline-offset:3px}.reputation-score{display:flex;flex-wrap:wrap;align-items:center;gap:14px;margin-top:10px}.reputation-score>strong{color:#e6d199;font-size:30px}.reputation-score small{font-size:15px;font-weight:400}.reputation-panel details{margin-top:16px;border-top:1px solid #35424a;padding-top:12px}.reputation-panel summary{min-height:44px;align-content:center;cursor:pointer}.reputation-panel ol{padding:0;list-style:none}.reputation-panel li{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:8px;padding:12px 0;border-bottom:1px solid #303c43}.reputation-panel li div{display:grid;gap:5px;min-width:0}.reputation-panel li small{grid-column:1/-1}.reputation-panel nav{display:flex;align-items:center;justify-content:space-between;gap:8px}.reputation-panel nav span{text-align:center}@media(max-width:480px){.reputation-panel{padding:14px}.reputation-panel header p{margin-bottom:0}.reputation-panel nav{flex-wrap:wrap}.reputation-panel nav span{flex:1;min-width:80px}.reputation-panel li{grid-template-columns:minmax(0,1fr)}.reputation-panel li strong{font-size:15px}}
.correction-form{display:grid;gap:10px;margin-top:12px;padding-top:12px;border-top:1px solid #35424a}.correction-form label{display:grid;gap:8px}.correction-form textarea{box-sizing:border-box;width:100%;max-width:100%;padding:10px;border:1px solid #53646e;background:#0b141d;color:#e3e9ec;font:inherit;resize:vertical}.correction-form button{justify-self:start}.correction-form p{margin:0}
</style>
