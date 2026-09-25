<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { adminApi, hasPermission, type ReleaseEnvironment, type ReleaseOperation, type ReleaseRun, type VerifiedReleaseArtifact } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const artifacts = ref<VerifiedReleaseArtifact[]>([]); const environments = ref<ReleaseEnvironment[]>([]); const runs = ref<ReleaseRun[]>([])
const environmentId = ref('staging'); const artifactId = ref(''); const reason = ref(''); const preview = ref<ReleaseOperation | null>(null)
const releasePreviewKey = ref(''); const rollbackPreviewKey = ref('')
const loading = ref(false); const notice = ref('')
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const environment = computed(() => environments.value.find(item => item.environment === environmentId.value))
const availableArtifacts = computed(() => artifacts.value.filter(item => item.environments.includes(environmentId.value)))
const releaseInputKey = computed(() => [environmentId.value, environment.value?.version ?? '', artifactId.value, reason.value.trim()].join('\n'))
const previewReady = computed(() => preview.value && !preview.value.applied && releasePreviewKey.value === releaseInputKey.value)
function rollbackInputKey(run: ReleaseRun) { return [run.id, environments.value.find(item => item.environment === run.environment)?.version ?? '', reason.value.trim()].join('\n') }

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
  if (!environment.value || !artifactId.value || !reason.value.trim()) { notice.value = '请选择环境和工件并填写理由'; return }
  const result = await adminApi.deployRelease(artifactId.value, environment.value.environment, environment.value.version, dryRun, reason.value.trim())
  if ('commandId' in result) notice.value = `发布命令已提交：${result.commandId}`
  else { preview.value = result; releasePreviewKey.value = dryRun ? releaseInputKey.value : ''; notice.value = result.applied ? '发布已执行并写入审计' : '预演完成，请核对后执行' }
  await load()
}
function execute() {
  if (!previewReady.value || !environment.value || !artifactId.value) { notice.value = '必须先对当前环境和工件完成预演'; return }
  requestRiskAction({ title: '执行版本发布', target: `${environment.value.environment} · ${artifactId.value}`, targetLabel: '环境与工件', impact: '系统会激活已验证工件并执行健康检查。', confirmLabel: '确认执行发布', severity: 'danger', run: () => perform(false) })
}
async function previewRollback(run: ReleaseRun) {
  if (!reason.value.trim()) { notice.value = '请填写回滚理由'; return }
  const target = environments.value.find(item => item.environment === run.environment); if (!target) return
  const result = await adminApi.rollbackRelease(run.id, target.version, true, reason.value.trim())
  if ('commandId' in result) notice.value = `回滚预演命令已提交：${result.commandId}`
  else { rollbackPreviewKey.value = rollbackInputKey(run); notice.value = `回滚预演完成：${result.plan.steps.length} 个步骤，未执行激活` }
}
function rollback(run: ReleaseRun) {
  if (!reason.value.trim()) { notice.value = '请填写回滚理由'; return }
  if (rollbackPreviewKey.value !== rollbackInputKey(run)) { notice.value = '必须先对当前版本和理由完成回滚预演'; return }
  const target = environments.value.find(item => item.environment === run.environment); if (!target) return
  requestRiskAction({ title: '生成并执行回滚', target: `${run.environment} · ${run.id}`, targetLabel: '发布记录', impact: '系统会先按当前版本前置执行回滚命令并保留审计。', confirmLabel: '确认回滚', severity: 'danger', run: async () => { const result = await adminApi.rollbackRelease(run.id, target.version, false, reason.value.trim()); notice.value = 'commandId' in result ? `回滚命令已提交：${result.commandId}` : '回滚已执行'; await load() } })
}
watch(releaseInputKey, () => { preview.value = null; releasePreviewKey.value = ''; rollbackPreviewKey.value = '' })
onMounted(load)
</script>

<template>
  <section class="releases-page">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><h2>发布与环境</h2><p>Web 端没有注册入口；执行发布前必须对当前环境、工件和版本完成预演。</p></div><button :disabled="loading" @click="load">刷新</button></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <section class="panel compose"><h3>发布计划</h3><label>目标环境<select v-model="environmentId"><option v-for="item in environments" :key="item.environment" :value="item.environment">{{ item.environment }} · v{{ item.version }}</option></select></label><label>已验证工件<select v-model="artifactId"><option v-for="item in availableArtifacts" :key="item.id" :value="item.id">{{ item.id }} · {{ item.commit }}</option></select></label><label>变更理由<textarea v-model="reason" rows="3" maxlength="500"></textarea></label><div class="actions"><button @click="perform(true)">1. 预演</button><button class="danger" :disabled="!previewReady || !hasPermission('releases.execute')" @click="execute">2. 执行发布</button></div><p v-if="preview">{{ preview.applied ? '已应用' : '预演未应用' }} · {{ preview.plan.steps.length }} 个步骤</p></section>
    <section class="panel"><h3>运行态只读快照</h3><p>环境健康与活动工件来自发布适配器，不由浏览器自报。</p><article v-for="item in environments" :key="item.environment"><b>{{ item.environment }}</b><span>v{{ item.version }} · {{ item.state }}</span><code>{{ item.activeArtifactId || '无活动工件' }}</code></article></section>
    <section class="panel"><h3>发布、失败与回滚记录</h3><article v-for="run in runs" :key="run.id"><span><b>{{ run.environment }} · {{ run.status }}</b><small>{{ new Date(run.startedAt).toLocaleString() }}</small><small>健康与 WebSocket 冒烟：{{ run.checks.length ? run.checks.map(check => `${check.kind} ${check.success ? '通过' : '失败'}`).join(' · ') : '无检查记录' }}</small></span><code>{{ run.artifactId }}</code><div v-if="hasPermission('releases.execute')" class="rollback-actions"><button @click="previewRollback(run)">回滚预演</button><button :disabled="rollbackPreviewKey !== rollbackInputKey(run)" @click="rollback(run)">执行回滚</button></div></article><p v-if="!runs.length">暂无发布记录。</p></section>
  </section>
</template>

<style scoped>
.releases-page{display:grid;grid-template-columns:minmax(280px,.8fr) minmax(0,1fr);gap:14px}.releases-page>header,.releases-page>.notice{grid-column:1/-1}.releases-page>header{display:flex;justify-content:space-between;gap:16px}.releases-page h2,.releases-page h3{margin:0 0 8px}.releases-page p,.releases-page small{color:#89979d}.releases-page button,.releases-page input,.releases-page select,.releases-page textarea{box-sizing:border-box;min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.panel{display:grid;align-content:start;gap:12px;padding:18px;border:1px solid #35424a;background:#0e161d}.compose{grid-row:2/span 2}.panel label{display:grid;gap:6px}.panel article{display:grid;grid-template-columns:1fr 1fr auto;align-items:center;gap:10px;padding:10px 0;border-top:1px solid #303c43}.panel article span{display:grid;gap:3px}.actions,.rollback-actions{display:flex;gap:8px}.rollback-actions{flex-wrap:wrap}.danger{border-color:#84424b!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
@media(max-width:900px){.releases-page{grid-template-columns:1fr}.compose{grid-row:auto}.releases-page>header{flex-direction:column}.releases-page>header button{width:100%}.panel article{grid-template-columns:1fr}.actions{display:grid}}
</style>
