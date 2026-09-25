<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { adminApi, hasPermission, type AuditArchiveOperation, type AuditArchiveRecovery, type AuditArchiveSegment, type SecurityStatus } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const status = ref<SecurityStatus | null>(null); const archives = ref<AuditArchiveSegment[]>([])
const preview = ref<AuditArchiveOperation | null>(null); const recovery = ref<AuditArchiveRecovery | null>(null)
const archivePreviewKey = ref('')
const retentionDays = ref(365); const reason = ref('定期安全审计归档'); const loading = ref(false); const notice = ref('')
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const archiveInputKey = computed(() => [retentionDays.value, status.value?.platformVersion ?? '', reason.value.trim()].join('\n'))
const previewReady = computed(() => Boolean(preview.value && !preview.value.applied && archivePreviewKey.value === archiveInputKey.value))

async function load() {
  loading.value = true
  try { [status.value, archives.value] = await Promise.all([adminApi.securityStatus(), adminApi.auditArchives()]) }
  catch (cause) { notice.value = cause instanceof Error ? cause.message : '安全治理状态读取失败' }
  finally { loading.value = false }
}
async function performArchive(dryRun: boolean) {
  if (!status.value || !reason.value.trim()) { notice.value = '请填写归档理由'; return }
  const result = await adminApi.archiveAudit(retentionDays.value, status.value.platformVersion, dryRun, reason.value.trim())
  if ('commandId' in result) notice.value = `归档命令已提交：${result.commandId}`
  else { preview.value = result; archivePreviewKey.value = dryRun ? archiveInputKey.value : ''; notice.value = result.applied ? '审计归档已执行' : `预演完成：${result.eligibleEvents} 条可归档` }
  await load()
}
function archive() {
  if (!previewReady.value || !preview.value) { notice.value = '请先对当前保留天数、平台版本和理由完成归档预演'; return }
  requestRiskAction({ title: '执行审计归档', target: `${preview.value.eligibleEvents} 条审计事件`, targetLabel: '范围', impact: '系统会创建校验归档段；主审计事件保持不删除。', confirmLabel: '确认执行归档', severity: 'danger', run: () => performArchive(false) })
}
async function rehearse() { try { recovery.value = await adminApi.rehearseAuditRecovery(); notice.value = recovery.value.success ? '恢复演练通过' : `恢复演练失败：${recovery.value.error || '未知错误'}` } catch (cause) { notice.value = cause instanceof Error ? cause.message : '恢复演练失败' } }
watch(archiveInputKey, () => { preview.value = null; archivePreviewKey.value = '' })
onMounted(load)
</script>

<template>
  <section class="security-page">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><h2>安全治理</h2><p>查看安全结论、审计可用性和恢复边界；归档执行必须先预演。</p></div><button :disabled="loading" @click="load">刷新</button></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <section v-if="status" class="metrics"><article><small>活跃管理员</small><b>{{ status.activeApprovers }}</b><span>具备管理权限的账号</span></article><article><small>高风险审计</small><b>{{ status.highRiskAuditAvailable ? '可用' : '阻断' }}</b><span>保留 {{ status.auditRetentionDays }} 天</span></article><article><small>禁用 / 登录锁定</small><b>{{ status.disabledAccounts }} / {{ status.activeLoginLocks }}</b></article><article><small>平台版本</small><b>v{{ status.platformVersion }}</b></article></section>
    <section v-if="status?.alerts.length" class="panel alerts"><h3>需要关注</h3><article v-for="alert in status.alerts" :key="alert.code" :data-severity="alert.severity"><b>{{ alert.code }} · {{ alert.count }}</b><span>{{ alert.message }}</span></article></section>
    <section class="panel"><h3>恢复边界</h3><p>服务器离线恢复：{{ status?.offlineBootstrapUsed ? '一次性入口已使用' : status?.offlineBootstrapEnabled && status?.offlineBootstrapCredentialConfigured ? '显式启用' : '默认关闭或未配置' }}</p><p>仅服务器 CLI 可用；后台操作不要求另一名管理员批准。</p><p>MFA：{{ status?.mfa.enrollmentEnabled ? '已启用' : '未启用' }}</p><button @click="rehearse">执行只读恢复演练</button><p v-if="recovery">{{ recovery.success ? '通过' : '失败' }} · {{ recovery.segments }} 段 / {{ recovery.events }} 条</p></section>
    <section class="panel archive"><h3>独立审计归档</h3><label>保留天数<input v-model.number="retentionDays" type="number" min="30" max="3650"></label><label>理由<input v-model="reason" maxlength="500"></label><div><button @click="performArchive(true)">1. 预演</button><button class="danger" :disabled="!previewReady || !hasPermission('admin.audit.archive')" @click="archive">2. 执行</button></div><article v-for="item in archives" :key="item.id"><code>{{ item.id }}</code><span>{{ item.eventCount }} 条 · {{ new Date(item.createdAt).toLocaleString() }}</span></article></section>
  </section>
</template>

<style scoped>
.security-page{display:grid;grid-template-columns:1fr 1fr;gap:14px}.security-page>header,.security-page>.notice,.metrics,.alerts{grid-column:1/-1}.security-page>header{display:flex;justify-content:space-between;gap:16px}.security-page h2,.security-page h3{margin:0 0 8px}.security-page p,.security-page span,.security-page small{color:#89979d}.security-page button,.security-page input{box-sizing:border-box;min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:9px}.metrics article,.panel{display:grid;gap:8px;padding:16px;border:1px solid #35424a;background:#0e161d}.metrics b{font-size:22px}.alerts article,.archive article{display:flex;justify-content:space-between;gap:12px;padding:9px 0;border-top:1px solid #303c43}.alerts article[data-severity="critical"]{color:#ef8994}.archive label{display:grid;gap:5px}.archive>div{display:flex;gap:8px}.danger{border-color:#84424b!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}
@media(max-width:800px){.security-page{grid-template-columns:1fr}.security-page>header,.metrics,.alerts{grid-column:auto}.security-page>header{flex-direction:column}.security-page>header button{width:100%}.metrics{grid-template-columns:1fr 1fr}}@media(max-width:520px){.metrics{grid-template-columns:1fr}.archive>div{display:grid}}
</style>
