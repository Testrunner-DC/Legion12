<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, platformState, type PlatformAccount } from '@/l12/platform'
import PagedCollection from './PagedCollection.vue'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const accounts = ref<PlatformAccount[]>([])
const search = ref('')
const notice = ref('')
const loading = ref(false)
const showDeleted = ref(false)
const reasons = reactive<Record<string, string>>({})
const temporaryPassword = ref('')
const temporaryPasswordAccount = ref('')
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const normalizedSearch = computed(() => search.value.trim().toLocaleLowerCase('zh-CN'))
const matchesSearch = (account: PlatformAccount) => !normalizedSearch.value || [account.username, account.id, account.emailMasked, account.disabledReason].some(value => value?.toLocaleLowerCase('zh-CN').includes(normalizedSearch.value))
const activeAccounts = computed(() => accounts.value.filter(account => !account.deleted && matchesSearch(account)))
const deletedAccounts = computed(() => accounts.value.filter(account => account.deleted && matchesSearch(account)))
const deletedCount = computed(() => accounts.value.filter(account => account.deleted).length)

async function load() {
  loading.value = true
  try { accounts.value = await adminApi.accounts() }
  catch (cause) { notice.value = cause instanceof Error ? cause.message : '账号列表读取失败' }
  finally { loading.value = false }
}
function requireReason(account: PlatformAccount) {
  const reason = reasons[account.id]?.trim()
  if (!reason) notice.value = '请先填写操作理由'
  return reason
}
function saveRole(account: PlatformAccount) {
  const nextRole = account.role as 'player'|'admin'
  requestRiskAction({ title: '确认账号身份变更', target: account.username, targetLabel: '账号', impact: nextRole === 'admin' ? '账号会立即获得全部管理员权限。' : '账号会立即失去管理员权限。', confirmLabel: '确认变更', run: async () => { await adminApi.setRole(account.id, nextRole, account.permissionVersion); notice.value = '身份已更新并写入审计'; await load() } })
}
function saveStatus(account: PlatformAccount) {
  const reason = requireReason(account); if (!reason) return
  const disable = !account.disabled
  requestRiskAction({ title: disable ? '禁用账号' : '启用账号', target: account.username, targetLabel: '账号', impact: disable ? '账号会停止登录，全部有效会话立即失效。' : '账号会恢复登录资格，旧会话不会恢复。', confirmLabel: disable ? '确认禁用' : '确认启用', severity: disable ? 'danger' : 'warning', run: async () => { const result = await adminApi.setAccountStatus(account.id, disable, reason, account.permissionVersion); reasons[account.id] = ''; notice.value = `状态已更新，撤销 ${result.revokedSessions} 个会话`; await load() } })
}
function revokeSessions(account: PlatformAccount) {
  requestRiskAction({ title: '撤销全部会话', target: account.username, targetLabel: '账号', impact: '该账号所有设备需要重新登录，撤销不可恢复。', confirmLabel: '确认撤销', run: async () => { const result = await adminApi.revokeSessions(account.id); notice.value = `已撤销 ${result.revokedCount} 个会话` } })
}
function resetPassword(account: PlatformAccount) {
  const reason = requireReason(account); if (!reason) return
  requestRiskAction({ title: '生成一次性临时密码', target: account.username, targetLabel: '账号', impact: '新密码只显示一次，同时撤销该账号全部会话。', confirmLabel: '生成并撤销会话', run: async () => { const result = await adminApi.resetAccountPassword(account.id, reason, account.permissionVersion); if (!result.temporaryPassword) throw new Error('服务端未返回临时密码，请检查命令记录'); temporaryPassword.value = result.temporaryPassword; temporaryPasswordAccount.value = account.username; reasons[account.id] = ''; await load() } })
}
function deleteAccount(account: PlatformAccount) {
  const reason = requireReason(account); if (!reason) return
  requestRiskAction({ title: '删除账号与清理数据', target: account.username, targetLabel: '账号', impact: '账号将被逻辑删除，个人数据被清理，全部会话立即撤销。后台不提供恢复。', confirmLabel: '确认删除与清理', severity: 'danger', run: async () => { const result = await adminApi.deleteAccount(account.id, reason, account.permissionVersion); notice.value = `已清理 ${result.removedPrivateRecords} 条私有记录并撤销 ${result.revokedSessions} 个会话`; reasons[account.id] = ''; await load() } })
}
async function copyPassword() { try { await navigator.clipboard.writeText(temporaryPassword.value); notice.value = '临时密码已复制' } catch { notice.value = '复制失败，请手动选择' } }
onMounted(load)
</script>

<template>
  <section class="accounts-page panel">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <AdminRiskActionDialog v-if="temporaryPassword" title="一次性临时密码已生成" :target="temporaryPasswordAccount" target-label="账号" impact="该密码只显示这一次，请通过受控渠道交付。" confirm-label="我已安全保存" severity="warning" :allow-cancel="false" :busy="false" @confirm="temporaryPassword = ''"><div class="secret"><code>{{ temporaryPassword }}</code><button @click="copyPassword">复制</button></div></AdminRiskActionDialog>
    <header><div><h2>账号、权限与会话</h2><p>账号变更立即执行并完整审计；根 Admin 与操作者自身受保护。列表只请求账号集合，详情使用单账号接口。</p></div><div class="toolbar"><input v-model="search" type="search" placeholder="搜索用户名 / ID / 邮箱"><button @click="showDeleted = true">已删除 {{ deletedCount }}</button><button :disabled="loading" @click="load">刷新</button></div></header>
    <p v-if="notice" class="notice" role="status">{{ notice }}</p>
    <PagedCollection :items="activeAccounts" v-slot="{ items }"><article v-for="account in items" :key="account.id" class="account-row"><span><router-link :to="`/admin/users/accounts/${encodeURIComponent(account.id)}`"><b>{{ account.username }}</b></router-link><small>{{ account.disabled ? '已禁用' : '正常' }}<template v-if="account.mustChangeUsername"> · 待修改用户名</template> · {{ account.permissions?.length ?? 0 }} 项权限</small><code>{{ account.id }}</code></span><label>长期身份<select v-model="account.role" :disabled="account.username === 'Admin' || !hasPermission('admin.accounts.roles.write')"><option value="player">玩家</option><option value="admin">管理员</option></select></label><div class="actions"><input v-if="hasPermission('admin.accounts.status.write')" v-model="reasons[account.id]" placeholder="状态 / 重置 / 删除理由"><button v-if="hasPermission('admin.accounts.roles.write')" :disabled="account.username === 'Admin'" @click="saveRole(account)">保存身份</button><button v-if="hasPermission('admin.accounts.status.write')" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="saveStatus(account)">{{ account.disabled ? '启用' : '禁用' }}</button><button v-if="hasPermission('admin.sessions.revoke')" @click="revokeSessions(account)">撤销会话</button><button v-if="hasPermission('admin.accounts.status.write')" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="resetPassword(account)">重置密码</button><button v-if="hasPermission('admin.accounts.status.write')" class="danger" :disabled="account.username === 'Admin' || account.id === platformState.account?.id" @click="deleteAccount(account)">删除与清理</button></div></article></PagedCollection>
    <p v-if="!activeAccounts.length && !loading" class="empty">没有符合条件的有效账号。</p>
    <Teleport to="body"><div v-if="showDeleted" class="overlay" @click.self="showDeleted = false"><section class="deleted-dialog" role="dialog" aria-modal="true" aria-label="已删除账号"><header><h2>已删除账号</h2><button @click="showDeleted = false">×</button></header><article v-for="account in deletedAccounts" :key="account.id"><b>{{ account.username }}</b><code>{{ account.id }}</code><span>{{ account.deletedAt ? new Date(account.deletedAt).toLocaleString() : '未记录删除时间' }}</span></article><p v-if="!deletedAccounts.length">没有符合条件的记录。</p></section></div></Teleport>
  </section>
</template>

<style scoped>
.panel{padding:20px;border:1px solid #35424a;background:#0e161d}.panel>header,.toolbar,.actions,.deleted-dialog>header{display:flex;align-items:center;justify-content:space-between;gap:10px}.panel h2{margin:0 0 6px}.panel p,.panel small{color:#8d9ba0}.panel button,.panel input,.panel select{min-height:38px;padding:7px 10px;border:1px solid #4b5961;background:#080e13;color:#fff}.account-row{display:grid;grid-template-columns:minmax(180px,.8fr) 150px minmax(300px,1.4fr);align-items:center;gap:14px;padding:14px 0;border-top:1px solid #303c43}.account-row>span{display:grid;gap:4px}.account-row a{color:#e1c36e;text-decoration:none}.account-row code{color:#77858b;overflow-wrap:anywhere}.account-row label{display:grid;gap:5px;color:#87949a}.actions{justify-content:flex-start;flex-wrap:wrap}.actions input{flex:1 1 190px}.danger{border-color:#84424b!important;color:#ef8994!important}.notice{padding:10px;border-left:3px solid #d1b25c;background:#241c0a;color:#edd584!important}.secret{display:grid;gap:10px}.secret code{user-select:all;color:#ffe09a;font-size:18px;overflow-wrap:anywhere}.overlay{position:fixed;z-index:200;inset:0;display:grid;place-items:center;padding:16px;background:rgba(1,4,6,.82)}.deleted-dialog{box-sizing:border-box;width:min(800px,100%);max-height:80vh;overflow:auto;padding:18px;border:1px solid #65737a;background:#0b1218}.deleted-dialog article{display:grid;grid-template-columns:1fr 1.3fr 1fr;gap:12px;padding:12px 0;border-top:1px solid #35424a}.empty{padding:18px;text-align:center}
@media(max-width:900px){.panel>header{align-items:stretch;flex-direction:column}.toolbar{align-items:stretch;flex-wrap:wrap}.account-row{grid-template-columns:1fr}.actions{display:grid;grid-template-columns:1fr 1fr}.actions input{grid-column:1/-1}.deleted-dialog article{grid-template-columns:1fr}}@media(max-width:560px){.toolbar,.actions{display:grid;grid-template-columns:1fr}.actions input{grid-column:auto}.toolbar>*{width:100%;box-sizing:border-box}}
</style>
