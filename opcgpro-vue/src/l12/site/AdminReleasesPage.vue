<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { adminApi, hasPermission, type ReleaseEnvironment, type ReleaseOperation, type ReleaseRun, type VerifiedReleaseArtifact } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const artifacts = ref<VerifiedReleaseArtifact[]>([]); const environments = ref<ReleaseEnvironment[]>([]); const runs = ref<ReleaseRun[]>([])
const environmentId = ref('staging'); const artifactId = ref(''); const reason = ref(''); const preview = ref<ReleaseOperation | null>(null)
const releasePreviewKey = ref(''); const rollbackPreviewKey = ref('')
const loading = ref(false); const notice = ref('')
const previewing = ref(false)
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const environment = computed(() => environments.value.find(item => item.environment === environmentId.value))
const availableArtifacts = computed(() => artifacts.value.filter(item => item.environments.includes(environmentId.value)))
function adapterReady(target?: ReleaseEnvironment) { return target?.adapterConfigured === true && target.state !== 'unconfigured' }
function compatibleArtifact(id: string, target: string) { return artifacts.value.find(item => item.id === id && item.environments.includes(target)) }
const selectedArtifact = computed(() => compatibleArtifact(artifactId.value, environmentId.value))
const releaseAllowed = computed(() => hasPermission('releases.execute') && adapterReady(environment.value)
  && !!selectedArtifact.value && !!reason.value.trim())
const canPreview = computed(() => releaseAllowed.value && !loading.value && !riskBusy.value && !previewing.value)
const releaseInputKey = computed(() => JSON.stringify([environmentId.value, environment.value?.version,
  adapterReady(environment.value), hasPermission('releases.execute'), artifactId.value,
  selectedArtifact.value?.commit, selectedArtifact.value?.releaseSha256, reason.value.trim()]))
const previewReady = computed(() => releaseAllowed.value && preview.value && !preview.value.applied && releasePreviewKey.value === releaseInputKey.value)
function rollbackAllowed(run: ReleaseRun) {
  return hasPermission('releases.execute') && !!reason.value.trim() && run.status === 'succeeded'
    && adapterReady(environments.value.find(item => item.environment === run.environment))
    && !!compatibleArtifact(run.artifactId, run.environment)
}
function canPreviewRollback(run: ReleaseRun) { return rollbackAllowed(run) && !loading.value && !riskBusy.value && !previewing.value }
function rollbackInputKey(run: ReleaseRun) {
  const target = environments.value.find(item => item.environment === run.environment)
  const artifact = compatibleArtifact(run.artifactId, run.environment)
  return JSON.stringify([run.id, run.environment, run.status, target?.version, adapterReady(target),
    hasPermission('releases.execute'), artifact?.id, artifact?.commit, artifact?.releaseSha256, reason.value.trim()])
}
const availabilityKey = computed(() => JSON.stringify([
  environments.value.map(item => [item.environment, item.version, adapterReady(item)]).sort(),
  artifacts.value.map(item => [item.id, item.commit, item.releaseSha256, [...item.environments].sort()]).sort(),
]))
let previewGeneration = 0
function probeLabel(probe: ReleaseEnvironment['health']) {
  return probe.code === 'adapter-unconfigured' ? '未检查（适配器未配置）'
    : probe.code === 'health-unavailable' || probe.code === 'ws-unavailable' ? `未取得检查结果 · ${probe.code}`
    : `${probe.success ? '通过' : '失败'} · ${probe.code} · ${probe.durationMs} ms`
}

async function load() {
  loading.value = true
  try {
    const reads: Promise<void>[] = []
    if (hasPermission('releases.read')) {
      reads.push(adminApi.releaseArtifacts().then(value => { artifacts.value = value }))
      reads.push(adminApi.releaseRuns().then(value => { runs.value = value }))
    }
    if (hasPermission('releases.runtime.read')) reads.push(adminApi.releaseEnvironments().then(value => { environments.value = value }))
    await Promise.all(reads.slice(0, 3))
    if (!environments.value.some(item => item.environment === environmentId.value)) environmentId.value = environments.value[0]?.environment || 'staging'
    if (!availableArtifacts.value.some(item => item.id === artifactId.value)) artifactId.value = availableArtifacts.value[0]?.id || ''
  } catch (cause) { notice.value = cause instanceof Error ? cause.message : '发布工作区读取失败' }
  finally { loading.value = false }
}
async function perform(dryRun: boolean) {
  if (!releaseAllowed.value || !environment.value || (dryRun ? !canPreview.value : !previewReady.value)) {
    notice.value = '发布能力不可用，或当前权限、兼容工件、理由／预演条件不满足'; return
  }
  const generation = previewGeneration; const inputKey = releaseInputKey.value
  if (dryRun) previewing.value = true
  try {
    const result = await adminApi.deployRelease(artifactId.value, environment.value.environment, environment.value.version, dryRun, reason.value.trim())
    if (dryRun && (generation !== previewGeneration || inputKey !== releaseInputKey.value || !releaseAllowed.value)) return
    if ('commandId' in result) notice.value = `发布命令已提交：${result.commandId}`
    else { preview.value = result; releasePreviewKey.value = dryRun ? inputKey : ''; notice.value = result.applied ? '发布已执行并写入审计' : '预演完成，请核对后执行' }
    await load()
  } finally { if (dryRun) previewing.value = false }
}
function execute() {
  if (!previewReady.value || !environment.value || loading.value || riskBusy.value || previewing.value) { notice.value = '必须先对当前可用环境和工件完成预演'; return }
  requestRiskAction({ title: '执行版本发布', target: `${environment.value.environment} · ${artifactId.value}`, targetLabel: '环境与工件', impact: '系统会激活已验证工件并执行健康检查。', confirmLabel: '确认执行发布', severity: 'danger', run: () => perform(false) })
}
async function previewRollback(run: ReleaseRun) {
  if (!canPreviewRollback(run)) { notice.value = '回滚需要已接入的目标环境、成功记录、兼容工件、执行权限及理由'; return }
  const target = environments.value.find(item => item.environment === run.environment); if (!target) return
  const generation = previewGeneration; const inputKey = rollbackInputKey(run)
  previewing.value = true
  try {
    const result = await adminApi.rollbackRelease(run.id, target.version, true, reason.value.trim())
    if (generation !== previewGeneration || inputKey !== rollbackInputKey(run) || !rollbackAllowed(run)) return
    if ('commandId' in result) notice.value = `回滚预演命令已提交：${result.commandId}`
    else { rollbackPreviewKey.value = inputKey; notice.value = `回滚预演完成：${result.plan.steps.length} 个步骤，未执行激活` }
  } finally { previewing.value = false }
}
function rollback(run: ReleaseRun) {
  if (!canPreviewRollback(run) || rollbackPreviewKey.value !== rollbackInputKey(run)) { notice.value = '必须先对当前可用环境、工件、版本和理由完成回滚预演'; return }
  const target = environments.value.find(item => item.environment === run.environment); if (!target) return
  const inputKey = rollbackInputKey(run)
  requestRiskAction({ title: '生成并执行回滚', target: `${run.environment} · ${run.id}`, targetLabel: '发布记录', impact: '系统会先按当前版本前置执行回滚命令并保留审计。', confirmLabel: '确认回滚', severity: 'danger', run: async () => {
    if (!rollbackAllowed(run) || rollbackPreviewKey.value !== inputKey || inputKey !== rollbackInputKey(run)) throw new Error('回滚能力或输入已变化，请重新预演')
    const result = await adminApi.rollbackRelease(run.id, target.version, false, reason.value.trim()); notice.value = 'commandId' in result ? `回滚命令已提交：${result.commandId}` : '回滚已执行'; await load()
  } })
}
watch([releaseInputKey, availabilityKey], () => { ++previewGeneration; preview.value = null; releasePreviewKey.value = ''; rollbackPreviewKey.value = '' }, { flush: 'sync' })
onMounted(load)
</script>

<template>
  <section class="releases-page">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><h2>发布与环境</h2><p>后台发布能力需单独接入并验收；未接入时继续使用既有外部部署流程。执行前必须对当前环境、工件和版本完成预演。</p></div><button :disabled="loading || previewing || riskBusy" @click="load">刷新</button></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <section class="panel compose"><h3>发布计划</h3><label>目标环境<select v-model="environmentId"><option v-for="item in environments" :key="item.environment" :value="item.environment">{{ item.environment }} · 发布台账版本 v{{ item.version }}</option></select></label><label>已验证工件<select v-model="artifactId"><option v-for="item in availableArtifacts" :key="item.id" :value="item.id">{{ item.id }} · {{ item.commit }}</option></select></label><p v-if="environment && !adapterReady(environment)">未接入后台发布能力；预演、发布及回滚不可用。</p><p v-else-if="!availableArtifacts.length">当前环境没有可用的已验证工件。</p><label>变更理由<textarea v-model="reason" rows="3" maxlength="500"></textarea></label><div class="actions"><button :disabled="!canPreview" @click="perform(true)">1. 预演</button><button class="danger" :disabled="!previewReady || loading || previewing || riskBusy" @click="execute">2. 执行发布</button></div><p v-if="preview">{{ preview.applied ? '已应用' : '预演未应用' }} · {{ preview.plan.steps.length }} 个步骤</p></section>
    <section class="panel"><h3>发布适配器只读快照</h3><p>以下为适配器观测及发布台账记录；台账版本不是运行服务版本。未接入不代表服务未部署，未检查不代表运行服务故障。</p><article v-for="item in environments" :key="item.environment" class="runtime-row"><b>{{ item.environment }}</b><span>发布台账版本 v{{ item.version }} · {{ adapterReady(item) ? item.state : '未接入后台发布能力' }}<small>健康：{{ probeLabel(item.health) }}</small><small>WebSocket：{{ probeLabel(item.webSocket) }}</small><small>活动版本记录：{{ item.activeCommit || '适配器未返回活动版本' }}</small><small>活动工件记录：{{ item.activeArtifactId || '适配器未返回活动工件' }}</small><small>关联运行记录：{{ item.lastRunId || '该台账未返回关联运行记录' }}</small><small>观测于 {{ new Date(item.observedAt).toLocaleString() }}</small></span></article></section>
    <section class="panel"><h3>发布、失败与回滚记录</h3><article v-for="run in runs" :key="run.id"><span><b>{{ run.environment }} · {{ run.status }}</b><small>{{ new Date(run.startedAt).toLocaleString() }}</small><small>健康与 WebSocket 冒烟：{{ run.checks.length ? run.checks.map(check => `${check.kind} ${check.success ? '通过' : '失败'}`).join(' · ') : '无检查记录' }}</small></span><code>{{ run.artifactId }}</code><div v-if="hasPermission('releases.execute')" class="rollback-actions"><button :disabled="!canPreviewRollback(run)" @click="previewRollback(run)">回滚预演</button><button :disabled="!canPreviewRollback(run) || rollbackPreviewKey !== rollbackInputKey(run)" @click="rollback(run)">执行回滚</button></div></article><p v-if="!runs.length">本发布台账暂无记录，不包含既有外部部署流程历史。</p></section>
  </section>
</template>

<style scoped>
.runtime-row{grid-template-columns:minmax(100px,.5fr) minmax(0,1.5fr)!important}.runtime-row>*{min-width:0;overflow-wrap:anywhere}@media(max-width:900px){.runtime-row{grid-template-columns:1fr!important}}
.releases-page{display:grid;grid-template-columns:minmax(280px,.8fr) minmax(0,1fr);gap:14px}.releases-page>header,.releases-page>.notice{grid-column:1/-1}.releases-page>header{display:flex;justify-content:space-between;gap:16px}.releases-page h2,.releases-page h3{margin:0 0 8px}.releases-page p,.releases-page small{color:#89979d}.releases-page button,.releases-page input,.releases-page select,.releases-page textarea{box-sizing:border-box;min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.panel{display:grid;align-content:start;gap:12px;padding:18px;border:1px solid #35424a;background:#0e161d}.compose{grid-row:2/span 2}.panel label{display:grid;gap:6px}.panel article{display:grid;grid-template-columns:1fr 1fr auto;align-items:center;gap:10px;padding:10px 0;border-top:1px solid #303c43}.panel article span{display:grid;gap:3px}.actions,.rollback-actions{display:flex;gap:8px}.rollback-actions{flex-wrap:wrap}.danger{border-color:#84424b!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
@media(max-width:900px){.releases-page{grid-template-columns:1fr}.compose{grid-row:auto}.releases-page>header{flex-direction:column}.releases-page>header button{width:100%}.panel article{grid-template-columns:1fr}.actions{display:grid}}
</style>
