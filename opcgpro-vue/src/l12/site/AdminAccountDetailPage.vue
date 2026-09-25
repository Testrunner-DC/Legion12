<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { adminApi, hasPermission, platformState, type AdminAudit, type PlatformAccount, type PlatformSession } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
const route = useRoute(); const router = useRouter()
const tabs = [{ id: 'profile', label: '基本资料' }, { id: 'access', label: '身份与状态' }, { id: 'sessions', label: '登录会话' }, { id: 'audit', label: '审计记录' }] as const
const tab = computed(() => tabs.some(item => item.id === route.query.tab) ? route.query.tab as typeof tabs[number]['id'] : 'profile')
const account = ref<PlatformAccount | null>(null); const sessions = ref<PlatformSession[]>([]); const audits = ref<AdminAudit[]>([])
const loading = ref(false); const notice = ref(''); const reason = ref('')
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const accountId = computed(() => String(route.params.accountId || ''))
function selectTab(next: string) { void router.push({ query: { ...route.query, tab: next } }) }
async function load() {
  loading.value = true; notice.value = ''
  try {
    const found = await adminApi.account(accountId.value)
    account.value = found
    if (tab.value === 'sessions' && hasPermission('admin.sessions.read')) sessions.value = await adminApi.sessions(found.id)
    if (tab.value === 'audit' && hasPermission('admin.audit.read')) audits.value = await adminApi.audit({ actorId: found.id })
  } catch (error) { notice.value = error instanceof Error ? error.message : '账号详情读取失败' }
  finally { loading.value = false }
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
watch([tab, accountId], load)
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
    <section v-else-if="account && tab === 'sessions'" class="panel"><h3>登录会话</h3><article v-for="session in sessions" :key="session.id"><span><b>{{ session.current ? '当前管理会话' : '其他设备' }}</b><code>{{ session.id }}</code></span><span>{{ new Date(session.createdAt).toLocaleString() }}<small>到期 {{ new Date(session.expiresAt).toLocaleString() }}</small></span><button :disabled="!hasPermission('admin.sessions.revoke')" @click="revokeSession(session)">撤销</button></article><p v-if="!sessions.length">暂无有效会话</p></section>
    <section v-else-if="account && tab === 'audit'" class="panel"><h3>审计记录</h3><article v-for="audit in audits" :key="audit.id"><span>{{ new Date(audit.createdAt).toLocaleString() }} · {{ audit.actorName }}</span><b>{{ audit.category }} / {{ audit.action }}</b><code>{{ audit.target }}</code></article><p v-if="!audits.length">暂无可见审计记录</p></section>
  </section>
</template>
<style scoped>
.account-detail{display:grid;gap:14px}.account-detail>header{display:flex;align-items:flex-start;justify-content:space-between;gap:16px}.account-detail h2{margin:8px 0}.account-detail a{color:#d9bc68;text-decoration:none}.account-detail button,.account-detail input,.account-detail select{min-height:40px;border:1px solid #4c5961;background:#080e13;color:#fff;padding:8px}.account-detail>nav{display:flex;gap:7px;overflow-x:auto}.account-detail>nav button{white-space:nowrap}.account-detail>nav button[aria-current="page"]{border-color:#c1a44e;background:#211b0e;color:#f0d579}.panel{display:grid;gap:14px;padding:20px;border:1px solid #35424a;background:#101821}.panel h3{margin:0}.panel dl{display:grid;grid-template-columns:130px minmax(0,1fr);gap:10px;margin:0}.panel dt{color:#849198}.panel dd{margin:0;overflow-wrap:anywhere}.panel label{display:grid;gap:6px}.panel article{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr) auto;align-items:center;gap:12px;padding:12px 0;border-top:1px solid #303c43}.panel article span{display:grid;gap:4px}.panel small,.panel p{color:#849198}.actions{display:flex;gap:8px}.danger{border-color:#84424b!important;background:#291116!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584}
@media(max-width:650px){.account-detail>header{flex-direction:column}.account-detail>header>button{width:100%}.panel dl{grid-template-columns:1fr}.panel article{grid-template-columns:1fr}.panel article button{width:100%}.actions{display:grid}}
</style>
