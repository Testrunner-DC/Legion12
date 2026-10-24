<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { adminApi, hasPermission, platformState, type AdminAudit, type PlatformAccount, type PlatformSession, type AccountRankedRestriction } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
const route = useRoute(); const router = useRouter()
const tabs = [{ id: 'profile', label: '基本资料' }, { id: 'access', label: '身份与状态' }, { id: 'sessions', label: '登录会话' }, { id: 'audit', label: '审计记录' }] as const
const tab = computed(() => tabs.some(item => item.id === route.query.tab) ? route.query.tab as typeof tabs[number]['id'] : 'profile')
const account = ref<PlatformAccount | null>(null); const sessions = ref<PlatformSession[]>([]); const audits = ref<AdminAudit[]>([])
const loading = ref(false); const notice = ref(''); const reason = ref('')
const rankedRestriction = ref<AccountRankedRestriction | null>(null)
const restrictionDays = ref<number | string>(7); const restrictionReason = ref(''); const restrictionError = ref('')
let loadGeneration = 0
const canRestrictRanked = computed(() => hasPermission('admin.accounts.status.write') && !!account.value
  && account.value.username !== 'Admin' && account.value.id !== platformState.account?.id)
const manualRestrictionActive = computed(() => !!rankedRestriction.value?.restrictionUntil
  && !rankedRestriction.value.clearedAt && Date.parse(rankedRestriction.value.restrictionUntil) > Date.now())
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const accountId = computed(() => String(route.params.accountId || ''))
function selectTab(next: string) { void router.push({ query: { ...route.query, tab: next } }) }
async function load() {
  const generation = ++loadGeneration; const id = accountId.value
  rankedRestriction.value = null; restrictionError.value = ''
  loading.value = true; notice.value = ''
  try {
    const found = await adminApi.account(id)
    if (generation !== loadGeneration || id !== accountId.value) return
    account.value = found
    if (tab.value === 'access') {
      try {
        const restriction = await adminApi.accountRankedRestriction(found.id)
        if (generation === loadGeneration && id === accountId.value) rankedRestriction.value = restriction
      } catch (error) {
        if (generation === loadGeneration) restrictionError.value = error instanceof Error ? error.message : '排位禁赛读取失败'
      }
    }
    if (tab.value === 'sessions' && hasPermission('admin.sessions.read')) sessions.value = await adminApi.sessions(found.id)
    if (tab.value === 'audit' && hasPermission('admin.audit.read')) audits.value = await adminApi.audit({ actorId: found.id })
  } catch (error) { notice.value = error instanceof Error ? error.message : '账号详情读取失败' }
  finally { if (generation === loadGeneration) loading.value = false }
}
function changeRankedRestriction(clear: boolean) {
  if (!canRestrictRanked.value || !account.value || !rankedRestriction.value || riskBusy.value) return
  const days = Number(restrictionDays.value)
  if (!clear && (!Number.isInteger(days) || days < 1 || days > 3650)) { restrictionError.value = '禁赛天数必须为1至3650的整数'; return }
  const comment = restrictionReason.value.trim()
  if (!comment || comment.length > 500) { restrictionError.value = '请填写1至500字的禁赛变更理由'; return }
  const frozen = Object.freeze({ accountId: account.value.id, username: account.value.username, days,
    reason: comment, expectedVersion: rankedRestriction.value.version,
    idempotencyKey: `ranked-restriction-${globalThis.crypto?.randomUUID?.() ?? `${Date.now().toString(16)}-${Math.random().toString(16).slice(2)}`}` })
  restrictionError.value = ''
  requestRiskAction({ title: clear ? '解除手工排位禁赛' : '设置排位禁赛', target: frozen.username, targetLabel: '账号',
    impact: clear ? '只解除账号管理设置的手工禁赛；完整性处置禁赛和其他排位限制仍独立生效。'
      : `从服务端确认成功起禁赛 ${frozen.days} 天（每天24小时）。只限制新排位，不影响登录、休闲和进行中的对局，不撤回收益。`,
    confirmLabel: clear ? '确认解除手工禁赛' : `确认禁赛 ${frozen.days} 天`, severity: clear ? 'warning' : 'danger',
    run: async () => {
      if (accountId.value !== frozen.accountId) throw new Error('账号已切换，请关闭后重新确认')
      const command = { reason: frozen.reason, expectedVersion: frozen.expectedVersion, idempotencyKey: frozen.idempotencyKey }
      if (clear) await adminApi.clearAccountRankedRestriction(frozen.accountId, command)
      else await adminApi.setAccountRankedRestriction(frozen.accountId, frozen.days, command)
      restrictionReason.value = ''; await load(); notice.value = '手工排位禁赛已更新并写入审计'
    },
  })
}
function saveRole() {
  if (!account.value || !hasPermission('admin.accounts.roles.write')) return
  const target = account.value; const role = target.role as 'player' | 'admin'
  requestRiskAction({ title: '确认账号身份变更', target: target.username, targetLabel: '账号', impact: role === 'admin' ? '账号会立即获得管理员权限。' : '账号会立即失去管理员权限。', confirmLabel: '确认变更', run: async () => { await adminApi.setRole(target.id, role, target.permissionVersion); notice.value = '身份已更新并写入审计'; await load() } })
}
function saveStatus() {
  if (!account.value || !hasPermission('admin.accounts.status.write')) return
  if (!reason.value.trim()) { notice.value = '请填写状态变更理由'; return }
  const target = account.value; const disable = !target.disabled
  requestRiskAction({ title: disable ? '禁用账号' : '启用账号', target: target.username, targetLabel: '账号', impact: disable ? '账号会停止登录，现有会话立即失效。' : '账号会恢复登录资格。', confirmLabel: disable ? '确认禁用' : '确认启用', severity: disable ? 'danger' : 'warning', run: async () => { await adminApi.setAccountStatus(target.id, disable, reason.value.trim(), target.permissionVersion); reason.value = ''; notice.value = '账号状态已更新并写入审计'; await load() } })
}
function revokeSession(session: PlatformSession) {
  if (!account.value || !hasPermission('admin.sessions.revoke')) return
  const target = account.value
  requestRiskAction({ title: '撤销登录会话', target: target.username, targetLabel: '账号', impact: '对应设备需要重新登录。', confirmLabel: '确认撤销', run: async () => { await adminApi.revokeSession(target.id, session.id); sessions.value = sessions.value.filter(item => item.id !== session.id); notice.value = '会话已撤销并写入审计' } })
}
watch([tab, accountId], () => { cancelRiskAction(); restrictionDays.value = 7; restrictionReason.value = ''; void load() })
onMounted(load)
</script>
<template>
  <section class="account-detail">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><router-link to="/admin/users/accounts">← 返回账号列表</router-link><h2>{{ account?.username || '账号详情' }}</h2><code>{{ accountId }}</code></div><button :disabled="loading" @click="load">{{ loading ? '读取中…' : '刷新' }}</button></header>
    <nav aria-label="账号详情分区"><button v-for="item in tabs" :key="item.id" :aria-current="tab === item.id ? 'page' : undefined" @click="selectTab(item.id)">{{ item.label }}</button></nav>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <section v-if="account && tab === 'profile'" class="panel"><h3>基本资料</h3><dl><dt>用户名</dt><dd>{{ account.username }}</dd><dt>账号 ID</dt><dd><code>{{ account.id }}</code></dd><dt>建立时间</dt><dd>{{ new Date(account.createdAt).toLocaleString() }}</dd><dt>邮箱</dt><dd>{{ account.emailMasked || '未绑定' }}</dd></dl></section>
    <section v-else-if="account && tab === 'access'" class="panel"><h3>身份与状态</h3><label>长期身份<select v-model="account.role" :disabled="!hasPermission('admin.accounts.roles.write') || account.username === 'Admin'"><option value="player">玩家</option><option value="admin">管理员</option></select></label><label>变更理由<input v-model="reason" maxlength="500" placeholder="状态变更会写入审计"/></label><div class="actions"><button :disabled="account.username === 'Admin' || !hasPermission('admin.accounts.roles.write')" @click="saveRole">保存身份</button><button class="danger" :disabled="account.username === 'Admin' || account.id === platformState.account?.id || !hasPermission('admin.accounts.status.write')" @click="saveStatus">{{ account.disabled ? '启用账号' : '禁用账号' }}</button></div><p>有效权限 {{ account.permissions?.length ?? 0 }} 项 · 版本 {{ account.permissionVersion ?? 0 }}</p></section>
    <section v-if="account && tab === 'access'" class="panel ranked-restriction" aria-labelledby="ranked-restriction-title">
      <h3 id="ranked-restriction-title">排位禁赛</h3>
      <p v-if="restrictionError" role="alert">{{ restrictionError }}</p>
      <template v-if="rankedRestriction">
        <p>手工禁赛：{{ manualRestrictionActive ? `至 ${new Date(rankedRestriction.restrictionUntil || '').toLocaleString()}` : '无生效中的手工禁赛' }}</p>
        <p v-if="rankedRestriction.effectiveRestrictionUntil">含完整性处置的禁赛截止：{{ new Date(rankedRestriction.effectiveRestrictionUntil).toLocaleString() }}</p>
        <p v-if="rankedRestriction.reason">最近设置理由：{{ rankedRestriction.reason }}</p>
        <label>禁赛天数<input v-model.number="restrictionDays" type="number" inputmode="numeric" min="1" max="3650" step="1" :disabled="!canRestrictRanked || riskBusy"/></label>
        <label>禁赛变更理由<input v-model="restrictionReason" maxlength="500" :disabled="!canRestrictRanked || riskBusy" placeholder="设置或解除禁赛均须填写理由"/></label>
        <div class="actions"><button :disabled="!canRestrictRanked || riskBusy || loading" @click="changeRankedRestriction(false)">设置排位禁赛</button><button :disabled="!canRestrictRanked || !manualRestrictionActive || riskBusy || loading" @click="changeRankedRestriction(true)">解除手工禁赛</button></div>
        <p>不影响登录、休闲或正在进行的对局；不会撤回排位收益。到期自动恢复，本入口解除不影响其他处罚。</p>
      </template>
    </section>
    <section v-else-if="account && tab === 'sessions'" class="panel"><h3>登录会话</h3><article v-for="session in sessions" :key="session.id"><span><b>{{ session.current ? '当前管理会话' : '其他设备' }}</b><code>{{ session.id }}</code></span><span>{{ new Date(session.createdAt).toLocaleString() }}<small>到期 {{ new Date(session.expiresAt).toLocaleString() }}</small></span><button :disabled="!hasPermission('admin.sessions.revoke')" @click="revokeSession(session)">撤销</button></article><p v-if="!sessions.length">暂无有效会话</p></section>
    <section v-else-if="account && tab === 'audit'" class="panel"><h3>审计记录</h3><article v-for="audit in audits" :key="audit.id"><span>{{ new Date(audit.createdAt).toLocaleString() }} · {{ audit.actorName }}</span><b>{{ audit.category }} / {{ audit.action }}</b><code>{{ audit.target }}</code></article><p v-if="!audits.length">暂无可见审计记录</p></section>
  </section>
</template>
<style scoped>
.account-detail{display:grid;gap:14px}.account-detail>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.account-detail h2{margin:8px 0}.account-detail a{color:#d9bc68;text-decoration:none}.account-detail button,.account-detail input,.account-detail select{min-height:40px;border:1px solid #4c5961;background:#080e13;color:#fff;padding:8px}.account-detail>nav{display:flex;gap:7px;overflow-x:auto}.account-detail>nav button{white-space:nowrap}.account-detail>nav button[aria-current="page"]{border-color:#c1a44e;background:#211b0e;color:#f0d579}.panel{display:grid;gap:14px;padding:20px;border:1px solid #35424a;background:#101821}.panel h3{margin:0}.panel dl{display:grid;grid-template-columns:130px minmax(0,1fr);gap:10px;margin:0}.panel dt{color:#849198}.panel dd{margin:0;overflow-wrap:anywhere}.panel label{display:grid;gap:6px}.panel article{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr) auto;align-items:center;gap:12px;padding:12px 0;border-top:1px solid #303c43}.panel article span{display:grid;gap:4px}.panel small,.panel p{color:#849198}.actions{display:flex;gap:8px}.danger{border-color:#84424b!important;background:#291116!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584}
@media(max-width:650px){.account-detail>header{flex-direction:column}.account-detail>header>button{width:100%}.account-detail>nav{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));overflow:visible}.account-detail>nav button{min-width:0;white-space:normal}.panel dl{grid-template-columns:1fr}.panel article{grid-template-columns:1fr}.panel article button{width:100%}.actions{display:grid}}
</style>
