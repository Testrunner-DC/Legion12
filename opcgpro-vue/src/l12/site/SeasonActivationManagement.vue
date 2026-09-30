<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
import {
  adminApi,
  PlatformRequestError,
  type SeasonActivationImpactPreview,
  type SeasonActivationPlan,
  type SeasonCatalog,
} from '@/l12/platform'

const props = defineProps<{
  catalog: SeasonCatalog | null
  canWrite: boolean
  configurationDirty: boolean
}>()
const emit = defineEmits<{
  notice: [message: string]
  authorityConflict: []
  changed: []
}>()

interface ActionIntent { fingerprint: string; key: string }
const impact = ref<SeasonActivationImpactPreview | null>(null)
const reason = ref('')
const error = ref('')
const previewing = ref(false)
let actionIntent: ActionIntent | undefined
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction }
  = useAdminRiskAction()

const current = computed(() => props.catalog?.current)
const next = computed(() => props.catalog?.next)
const nextPlan = computed<SeasonActivationPlan | null>(() => next.value?.activationPlan ?? null)
const completedCurrentPlan = computed<SeasonActivationPlan | null>(() =>
  current.value?.activationPlan?.status === 'completed' ? current.value.activationPlan : null)
const plan = computed<SeasonActivationPlan | null>(() => nextPlan.value ?? completedCurrentPlan.value)
const planOwner = computed<'next' | 'current' | null>(() =>
  nextPlan.value ? 'next' : completedCurrentPlan.value ? 'current' : null)
const planDefinition = computed(() => planOwner.value === 'next' ? next.value : current.value)
const planStatus = computed(() => plan.value?.status ?? 'unarmed')
const authorityBinding = computed(() => {
  if (!current.value || !props.catalog) return ''
  return [current.value.definitionId, current.value.revision,
    next.value?.definitionId ?? 'no-next', next.value?.revision ?? 0,
    props.catalog.operationsVersion, planOwner.value ?? 'no-plan', plan.value?.status ?? 'unarmed',
    plan.value?.generation ?? 0, plan.value?.disarmGuardToken ?? 'no-guard'].join('|')
})
const impactCurrent = computed(() => Boolean(impact.value && next.value && current.value
  && props.catalog
  && impact.value.definitionId === next.value.definitionId
  && impact.value.currentRevision === current.value.revision
  && impact.value.draftRevision === next.value.revision
  && impact.value.operationsVersion === props.catalog.operationsVersion))
const canDisarm = computed(() => Boolean(planOwner.value === 'next' && plan.value?.disarmGuardToken
  && !['unarmed', 'disarmed', 'completed'].includes(plan.value.status)))

watch(authorityBinding, () => {
  const confirmationWasOpen = Boolean(riskAction.value)
  if (confirmationWasOpen) {
    const submitting = riskBusy.value
    if (!submitting) cancelRiskAction()
    error.value = submitting
      ? '权威赛季或切季计划已变化，本次请求将由服务端按弹框打开时的冻结计划校验'
      : '权威赛季或切季计划已变化，高风险确认已关闭，请刷新后重新预览'
    emit('notice', error.value)
  } else error.value = ''
  impact.value = null
  actionIntent = undefined
})
watch(reason, () => { actionIntent = undefined })

function newIdempotencyKey() {
  return globalThis.crypto?.randomUUID?.()
    ?? `season-activation-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}
function intentKey(fingerprint: string) {
  if (actionIntent?.fingerprint !== fingerprint)
    actionIntent = { fingerprint, key: newIdempotencyKey() }
  return actionIntent.key
}
function formatTime(value?: string | null) {
  return value ? new Date(value).toLocaleString() : '暂无'
}
function statusLabel(status: string) {
  return ({ unarmed: '未预约', armed: '已预约', waiting: '等待条件', executing: '正在切换',
    failed: '已阻断', disarmed: '已取消', completed: '已完成' } as Record<string, string>)[status] ?? status
}
function leaseLabel(state?: string) {
  const labels: Record<string, string> = {
    held: '执行权已持有', expired: '执行权已过期', free: '无执行权占用',
  }
  return labels[state ?? 'free'] ?? '状态未知'
}
function codeLabel(code: string) {
  return ({
    audit_unavailable: '独立审计不可用，禁止预约切季',
    season_draft_incomplete: '下赛季定义未填写完整',
    season_link_conflict: '当前与下赛季的衔接关系已变化',
    season_activation_time_invalid: '下赛季开始时间必须晚于当前时间',
    season_activation_already_armed: '已有生效中的自动切季计划',
    season_cutover_not_ready: '尚有排位对局或结算未完成',
    'restore-audit-before-arm': '先恢复独立审计，再重新预览',
    'complete-next-season-definition': '补全下赛季信息并保存',
    'refresh-season-authority': '刷新权威赛季状态',
    'set-future-cutover-time': '将下赛季开始时间调整到未来',
    'resolve-blocker-then-disarm-rearm': '处理阻断后取消预约，再重新预约',
    'wait-for-current-attempt': '等待当前切换尝试完成',
    'wait-for-automatic-retry': '等待系统自动重试',
    'wait-for-scheduled-cutover': '等待预定切换时间',
    'preview-and-arm': '预览切季影响后确认预约',
    'no-action-required': '无需操作',
  } as Record<string, string>)[code] ?? code
}
function admissionLabel(value: string) {
  return ({ fenced: '新排位已暂停进入', 'fence-scheduled': '到达切换时间后暂停新排位',
    'fence-after-arm-at-scheduled-time': '预约后将在切换时间暂停新排位' } as Record<string, string>)[value]
    ?? value
}
function readinessSummary(value: SeasonActivationImpactPreview) {
  const item = value.readiness
  return `进行中 ${item.activeMatches} 场，待结算 ${item.pendingSettlements} 项，`
    + `对账失败 ${item.appliedReconciliationFailures} 项，隔离 ${item.quarantinedSettlements} 项`
}
function transitionLabel(value: { seasonId: string; seasonName: string }) {
  return `${value.seasonName || value.seasonId}（${value.seasonId}）`
}

async function previewImpact() {
  if (!current.value || !next.value || !props.catalog) return
  const requestedBinding = authorityBinding.value
  previewing.value = true
  impact.value = null
  error.value = ''
  actionIntent = undefined
  try {
    const result = await adminApi.previewSeasonActivation(next.value.definitionId,
      current.value.revision, next.value.revision, props.catalog.operationsVersion)
    if (requestedBinding !== authorityBinding.value) {
      error.value = '预览返回前权威赛季状态已变化，本次结果已忽略。'
      return
    }
    impact.value = result
    emit('notice', result.valid ? '切季影响预览已更新' : '切季当前被阻断，请按建议处理')
  } catch (caught) {
    error.value = caught instanceof Error ? caught.message : '切季影响预览失败'
    if (caught instanceof PlatformRequestError && caught.status === 409) emit('authorityConflict')
  } finally { previewing.value = false }
}

function arm() {
  if (!props.canWrite || !current.value || !next.value || !props.catalog) return
  const actionReason = reason.value.trim()
  if (!actionReason) { emit('notice', '预约切季前请填写理由'); return }
  if (props.configurationDirty) { emit('notice', '请先保存下赛季配置，再预览切季影响'); return }
  const snapshot = impact.value
  if (!snapshot?.valid || !impactCurrent.value) {
    emit('notice', '切季影响已变化或尚未通过预览'); return
  }
  const frozen = Object.freeze({
    definitionId: next.value.definitionId,
    expectedCurrentRevision: current.value.revision,
    expectedDraftRevision: next.value.revision,
    expectedVersion: props.catalog.operationsVersion,
    previewToken: snapshot.previewToken,
    binding: authorityBinding.value,
  })
  const key = intentKey(`arm|${frozen.binding}|${frozen.previewToken}|${actionReason}`)
  requestRiskAction({
    title: '预约自动切换赛季',
    target: `${transitionLabel(snapshot.currentToHistory)} → ${transitionLabel(snapshot.nextToCurrent)}`,
    targetLabel: '当前 → 下赛季',
    impact: `将结算 ${snapshot.settlementParticipantCount} 名参与者，生成 ${snapshot.summaryNotificationCount} 份赛季总结；${admissionLabel(snapshot.rankedAdmissionImpact)}。理由：${actionReason}`,
    confirmLabel: '确认预约',
    severity: 'warning',
    run: async () => {
      try {
        await adminApi.armSeasonActivation(frozen.definitionId,
          frozen.expectedCurrentRevision, frozen.expectedDraftRevision, frozen.expectedVersion,
          frozen.previewToken, actionReason, key)
        actionIntent = undefined
        reason.value = ''
        impact.value = null
        emit('notice', '自动切季已预约')
        emit('changed')
      } catch (caught) {
        error.value = caught instanceof Error ? caught.message : '预约切季失败'
        if (caught instanceof PlatformRequestError && caught.status === 409) {
          impact.value = null
          emit('authorityConflict')
        }
        throw caught
      }
    },
  })
}

function disarm() {
  if (!props.canWrite || !next.value || !props.catalog || !canDisarm.value) return
  const actionReason = reason.value.trim()
  if (!actionReason) { emit('notice', '取消预约前请填写理由'); return }
  const activePlan = plan.value!
  const frozen = Object.freeze({
    definitionId: next.value.definitionId,
    expectedDraftRevision: next.value.revision,
    expectedPlanGeneration: activePlan.generation,
    disarmGuardToken: activePlan.disarmGuardToken,
    expectedVersion: props.catalog.operationsVersion,
    intentMask: activePlan.intentMask,
    binding: authorityBinding.value,
  })
  const key = intentKey(`disarm|${frozen.binding}|${frozen.expectedPlanGeneration}|${actionReason}`)
  requestRiskAction({
    title: '取消自动切季',
    target: `${next.value.name}（${next.value.seasonId}）· 计划 g${frozen.expectedPlanGeneration} · ${frozen.intentMask}`,
    targetLabel: '下赛季 / 计划版本',
    impact: `停止当前自动切季计划，不删除下赛季草稿。理由：${actionReason}`,
    confirmLabel: '确认取消预约',
    run: async () => {
      try {
        await adminApi.disarmSeasonActivation(frozen.definitionId, frozen.expectedDraftRevision,
          frozen.expectedPlanGeneration, frozen.disarmGuardToken, frozen.expectedVersion,
          actionReason, key)
        actionIntent = undefined
        reason.value = ''
        impact.value = null
        emit('notice', '自动切季预约已取消')
        emit('changed')
      } catch (caught) {
        error.value = caught instanceof Error ? caught.message : '取消预约失败'
        if (caught instanceof PlatformRequestError && caught.status === 409) {
          impact.value = null
          emit('authorityConflict')
        }
        throw caught
      }
    },
  })
}
</script>

<template>
  <section class="activation-management" data-ui-contract="season-activation-management">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target"
      :target-label="riskAction.targetLabel" :impact="riskAction.impact"
      :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity"
      :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header>
      <div><small>AUTOMATIC CUTOVER</small><h3>自动切季计划</h3>
        <p v-if="planOwner === 'current'">最近一次自动切季已完成；完成计划不可取消。</p>
        <p v-else>此处只显示和操作切季计划，不会修改上方的赛季配置草稿。</p></div>
      <strong :data-status="planStatus">{{ statusLabel(planStatus) }}</strong>
    </header>

    <div v-if="next || plan" class="plan-grid">
      <article><small>计划版本 / 意图</small><b>{{ plan ? `g${plan.generation}` : '未生成' }}</b><span>{{ plan?.intentMask || '未预约' }}</span></article>
      <article><small>预约 / 切换时间</small><b>{{ formatTime(plan?.armedAt) }}</b><span>{{ formatTime(plan?.scheduledAt || planDefinition?.startsAt) }}</span></article>
      <article><small>执行权</small><b>{{ leaseLabel(plan?.leaseState) }}</b><span>{{ plan?.leaseExpiresAt ? `有效至 ${formatTime(plan.leaseExpiresAt)}` : '当前无执行者占用' }}</span></article>
      <article><small>最近尝试</small><b>{{ plan?.attemptCount ?? 0 }} 次</b><span>{{ formatTime(plan?.lastAttemptAt) }}</span></article>
      <article v-if="plan?.status === 'completed'" data-ui-contract="completed-season-activation"><small>完成时间</small><b>{{ formatTime(plan.completedAt) }}</b><span>最近切季已完成</span></article>
      <article class="wide"><small>等待 / 阻断</small><b>{{ plan?.lastErrorCode ? codeLabel(plan.lastErrorCode) : '当前无阻断' }}</b><span>{{ plan ? codeLabel(plan.suggestedActionCode) : '先预览切季影响，再确认预约' }}</span></article>
    </div>
    <p v-else class="empty-state">创建并保存下赛季草稿后，才能预览和预约自动切季。</p>

    <div v-if="next" class="action-row">
      <input v-model="reason" :disabled="!canWrite" placeholder="预约或取消预约的理由（必填）"/>
      <button type="button" :disabled="previewing || configurationDirty" @click="previewImpact">
        {{ previewing ? '正在预览…' : '预览切季影响' }}
      </button>
      <button class="confirm" type="button" :disabled="!canWrite || !impact?.valid || !impactCurrent || configurationDirty || canDisarm" @click="arm">确认预约</button>
      <button class="danger" type="button" :disabled="!canWrite || !canDisarm" @click="disarm">取消预约</button>
    </div>
    <p v-if="configurationDirty" class="notice">下赛季尚有未保存的配置或理由；保存后才能预览切季影响。</p>
    <p v-if="error" class="error" role="alert">{{ error }}。本地双槽编辑未被覆盖。</p>

    <section v-if="impact" class="impact-preview" :class="{ blocked: !impact.valid }">
      <header><b>{{ impact.valid && impactCurrent ? '切季影响已锁定' : '切季当前被阻断' }}</b><small>观测于 {{ formatTime(impact.observedAt) }}</small></header>
      <div class="transition">
        <span><small>当前 → 历史</small><b>{{ transitionLabel(impact.currentToHistory) }}</b></span>
        <span><small>下赛季 → 当前</small><b>{{ transitionLabel(impact.nextToCurrent) }}</b></span>
      </div>
      <div class="impact-counts">
        <span><b>{{ impact.settlementParticipantCount }}</b><small>结算参与者</small></span>
        <span><b>{{ impact.historyRecordCount }}</b><small>历史档案</small></span>
        <span><b>{{ impact.summaryNotificationCount }}</b><small>赛季总结通知</small></span>
      </div>
      <p><b>排位准入：</b>{{ admissionLabel(impact.rankedAdmissionImpact) }}；{{ readinessSummary(impact) }}。</p>
      <ul v-if="impact.blockingCodes.length"><li v-for="code in impact.blockingCodes" :key="code">{{ codeLabel(code) }}</li></ul>
      <p v-if="impact.suggestedActionCodes.length"><b>建议动作：</b>{{ impact.suggestedActionCodes.map(codeLabel).join('；') }}</p>
    </section>
  </section>
</template>

<style scoped>
.activation-management{box-sizing:border-box;display:grid;min-width:0;max-width:100%;gap:12px;margin-top:16px;padding:16px;border:1px solid #4a4030;background:#0b1218;overflow-wrap:anywhere}.activation-management>header,.impact-preview>header{display:flex;min-width:0;align-items:center;justify-content:space-between;gap:12px}.activation-management h3{margin:2px 0;font-size:20px}.activation-management p{margin:0;color:#93a0a5;font-size:14px}.activation-management header small{color:#c8a84f;letter-spacing:.08em}.activation-management header strong{flex:none;padding:6px 9px;border:1px solid #756535;color:#efd16f}.plan-grid{display:grid;min-width:0;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}.plan-grid article{display:flex;min-width:0;flex-direction:column;gap:5px;padding:10px;border:1px solid #334049;background:#101821}.plan-grid .wide{grid-column:1/-1}.plan-grid small,.plan-grid span{color:#87949a;font-size:13px}.plan-grid b{overflow-wrap:anywhere}.action-row{display:grid;min-width:0;grid-template-columns:minmax(180px,1fr) auto auto auto;gap:8px}.action-row input,.action-row button{box-sizing:border-box;min-width:0;max-width:100%;min-height:44px;border:1px solid #4c5961;background:#080e13;padding:9px;color:#fff;font:700 14px 'Microsoft YaHei'}.action-row button:disabled{opacity:.45}.action-row .confirm{border-color:#b9953f;background:#2c2411;color:#f0d582}.action-row .danger{border-color:#9c3e47;background:#2a1014;color:#ffc8ce}.notice,.error,.empty-state{padding:10px;border:1px solid #5a512f}.error{border-color:#9c3e47!important;background:#2a1014;color:#ffc8ce!important}.impact-preview{display:grid;min-width:0;gap:10px;padding:12px;border:1px solid #866f35;background:#17150d}.impact-preview.blocked{border-color:#9c3e47;background:#211014}.impact-preview header small{letter-spacing:0;color:#87949a}.transition{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.transition span{display:flex;min-width:0;flex-direction:column;gap:4px;padding:9px;border:1px solid #39464d}.transition small{color:#87949a}.impact-counts{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.impact-counts span{display:flex;min-width:0;flex-direction:column;align-items:center;padding:10px;border:1px solid #39464d;text-align:center}.impact-counts b{font-size:20px;color:#efd16f}.impact-counts small{color:#87949a}.impact-preview ul{margin:0;padding-left:20px;color:#ffc8ce}.impact-preview li+li{margin-top:5px}
@media(max-width:1000px){.plan-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.action-row{grid-template-columns:1fr 1fr}.action-row input{grid-column:1/-1}}
@media(max-width:650px){.activation-management{padding:12px}.activation-management>header,.impact-preview>header{align-items:flex-start;flex-direction:column}.plan-grid,.action-row,.transition,.impact-counts{grid-template-columns:1fr}.plan-grid .wide,.action-row input{grid-column:auto}.action-row button{width:100%}}
</style>
