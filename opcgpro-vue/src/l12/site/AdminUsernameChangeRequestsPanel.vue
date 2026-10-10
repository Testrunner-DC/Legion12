<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { adminApi, hasPermission, type UsernameChangeRequest } from '@/l12/platform'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'

const emit = defineEmits<{ notice: [value: string] }>()
const requests = ref<UsernameChangeRequest[]>([])
const filter = ref('pending')
const busy = ref(false)
const notes = reactive<Record<string, string>>({})
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()

function notice(value: string) { emit('notice', value) }
async function load() {
  busy.value = true
  try { requests.value = await adminApi.usernameChangeRequests(filter.value) }
  catch (error) { notice(error instanceof Error ? error.message : '改名申请读取失败') }
  finally { busy.value = false }
}
function review(request: UsernameChangeRequest, approve: boolean) {
  if (!hasPermission('admin.accounts.status.write')) return
  if (!approve && !notes[request.id]?.trim()) { notice('驳回时请填写处理说明'); return }
  const action = approve ? '通过' : '驳回'
  requestRiskAction({
    title: `${action}改名申请`, target: `${request.currentUsername} → ${request.requestedUsername}`, targetLabel: '用户名',
    impact: approve ? '通过后用户名立即变更，目标名称会再次检查占用情况并写入审计。' : '驳回后本次申请结束，处理说明会对审核记录永久留痕。',
    confirmLabel: `确认${action}`, severity: approve ? 'warning' : 'danger',
    run: async () => {
      const updated = await adminApi.reviewUsernameChangeRequest(request.id, approve, notes[request.id] || '')
      requests.value = requests.value.map(item => item.id === updated.id ? updated : item)
      notes[request.id] = ''
      notice(approve ? `已通过 ${updated.requestedUsername} 的改名申请` : '已驳回改名申请')
    },
  })
}
onMounted(load)
</script>

<template>
  <section class="username-request-admin">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header><div><small>USERNAME REVIEW</small><h2>改名审核</h2><p>玩家首次自助改名后，才可在“我的”提交再次改名申请。通过前会再次确认目标用户名未被占用；所有决定均写入审计记录。</p></div><div><select v-model="filter" @change="load"><option value="pending">待审核</option><option value="approved">已通过</option><option value="rejected">已驳回</option><option value="">全部</option></select><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button></div></header>
    <article v-for="request in requests" :key="request.id" :class="request.status"><div class="request-main"><b>{{ request.currentUsername }} <i>→</i> {{ request.requestedUsername }}</b><span>申请人 ID：{{ request.accountId }}</span><p>{{ request.reason }}</p><small>提交于 {{ new Date(request.createdAt).toLocaleString() }}<template v-if="request.reviewedAt"> · {{ request.reviewedByUsername || '管理员' }} 于 {{ new Date(request.reviewedAt).toLocaleString() }}处理</template></small><em v-if="request.reviewNote">处理说明：{{ request.reviewNote }}</em></div><div v-if="request.status === 'pending' && hasPermission('admin.accounts.status.write')" class="review-actions"><textarea v-model.trim="notes[request.id]" rows="3" maxlength="400" placeholder="处理说明；驳回时必填"></textarea><div><button class="reject" @click="review(request, false)">驳回</button><button class="approve" @click="review(request, true)">通过并改名</button></div></div><strong v-else>{{ request.status === 'approved' ? '已通过' : '已驳回' }}</strong></article>
    <p v-if="!requests.length" class="empty">暂无符合当前筛选条件的改名申请。</p>
  </section>
</template>

<style scoped>
.username-request-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.username-request-admin>header{display:flex;align-items:flex-start;justify-content:space-between;gap:20px;margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.username-request-admin h2{margin:5px 0 8px}.username-request-admin p,.username-request-admin span,.username-request-admin small,.username-request-admin em{color:#94a1a6;font-size:14px;line-height:1.65}.username-request-admin>header small{color:#55c4cb;font:900 13px monospace;letter-spacing:.15em}.username-request-admin>header>div:last-child{display:flex;gap:8px}.username-request-admin select,.username-request-admin button,.review-actions textarea{box-sizing:border-box;border:1px solid #4a5860;background:#070d12;color:#fff;padding:9px 10px;font-size:14px}.username-request-admin article{display:grid;grid-template-columns:minmax(0,1fr) minmax(250px,.5fr);gap:18px;align-items:center;margin-top:10px;padding:14px;border:1px solid #4b5142;background:#111a20}.username-request-admin article.approved{border-color:#397a60}.username-request-admin article.rejected{border-color:#7d4149;opacity:.72}.request-main{display:grid;gap:4px;min-width:0}.request-main>b{font-size:17px}.request-main i{color:#d9bc66;font-style:normal}.request-main p{margin:2px 0;color:#d2d7d6;white-space:pre-wrap}.request-main em{color:#e1c47a;font-style:normal}.review-actions{display:grid;gap:8px}.review-actions textarea{width:100%;resize:vertical}.review-actions>div{display:flex;justify-content:flex-end;gap:8px}.review-actions .approve{border-color:#53b889;background:#0f2a20;color:#a1ecc8;font-weight:900}.review-actions .reject{border-color:#974952;background:#2b1117;color:#f0abb1}.username-request-admin article>strong{justify-self:end;padding:7px 10px;border:1px solid #59646a}.empty{padding:20px;text-align:center}@media(max-width:760px){.username-request-admin>header{flex-direction:column}.username-request-admin article{grid-template-columns:1fr}.username-request-admin article>strong{justify-self:start}.review-actions>div button{flex:1}}
</style>
