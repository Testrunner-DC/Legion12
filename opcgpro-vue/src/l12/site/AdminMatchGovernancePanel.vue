<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { hasPermission } from '@/l12/platform'
import {
  matchGovernanceAdminApi,
  type MatchDrawRecord,
  type PlayerMatchReport,
} from '@/l12/matchGovernance'

const emit = defineEmits<{ notice: [message: string] }>()
const view = ref<'draws' | 'reports'>('draws')
const draws = ref<MatchDrawRecord[]>([])
const reports = ref<PlayerMatchReport[]>([])
const status = ref('')
const search = ref('')
const loading = ref(false)
const notes = reactive<Record<string, string>>({})
const comments = reactive<Record<string, string>>({})
const canWrite = hasPermission('admin.match-governance.write')

async function load() {
  loading.value = true
  try {
    if (view.value === 'draws') {
      draws.value = await matchGovernanceAdminApi.drawRequests({ status: status.value, search: search.value.trim() })
      draws.value.forEach(row => { notes[row.id] ??= row.adminNotes ?? '' })
    } else {
      reports.value = await matchGovernanceAdminApi.playerReports({ status: status.value, search: search.value.trim() })
      reports.value.forEach(row => { notes[row.id] ??= row.adminNotes ?? '' })
    }
  } catch (error) { emit('notice', error instanceof Error ? error.message : '对局治理记录加载失败') }
  finally { loading.value = false }
}

async function saveDraw(row: MatchDrawRecord) {
  try {
    const updated = await matchGovernanceAdminApi.updateDrawRequest(row.id, {
      status: row.adminStatus, adminNotes: notes[row.id], comment: comments[row.id],
    })
    draws.value = draws.value.map(item => item.id === updated.id ? updated : item)
    comments[row.id] = ''
    emit('notice', `平局申请 ${row.id} 已更新并写入审计`)
  } catch (error) { emit('notice', error instanceof Error ? error.message : '平局申请更新失败') }
}

async function saveReport(row: PlayerMatchReport) {
  try {
    const updated = await matchGovernanceAdminApi.updatePlayerReport(row.id, {
      status: row.status, adminNotes: notes[row.id], comment: comments[row.id],
    })
    reports.value = reports.value.map(item => item.id === updated.id ? updated : item)
    comments[row.id] = ''
    emit('notice', `玩家举报 ${row.id} 已更新并写入审计`)
  } catch (error) { emit('notice', error instanceof Error ? error.message : '玩家举报更新失败') }
}

function selectView(next: 'draws' | 'reports') {
  view.value = next
  status.value = ''
  void load()
}

function statusLabel(value: string) {
  return ({ pending: '待对方处理', accepting: '正在确认', accepted: '已接受', rejected: '已拒绝', expired: '已过期', cancelled: '已取消',
    new: '新记录', reviewing: '处理中', resolved: '已处理', closed: '已关闭' } as Record<string, string>)[value] || value
}

onMounted(load)
</script>

<template>
  <section class="governance-panel" data-ui-contract="match-governance-admin">
    <header>
      <div><small>MATCH GOVERNANCE</small><h2>对局治理</h2><p>平局申请与玩家举报独立于普通 Bug；这里只显示处置所需的最小对局与身份信息。</p></div>
      <button :disabled="loading" @click="load">{{ loading ? '加载中…' : '刷新' }}</button>
    </header>
    <nav aria-label="对局治理记录类型"><button :class="{ active: view === 'draws' }" @click="selectView('draws')">平局申请</button><button :class="{ active: view === 'reports' }" @click="selectView('reports')">玩家举报</button></nav>
    <div class="filters"><input v-model="search" placeholder="记录 / 对局 / 房间 / 玩家 / 内容" @keyup.enter="load"/><select v-model="status" @change="load"><option value="">全部状态</option><template v-if="view === 'draws'"><option value="pending">待对方处理</option><option value="accepting">正在确认</option><option value="accepted">已接受</option><option value="rejected">已拒绝</option><option value="expired">已过期</option><option value="cancelled">已取消</option></template><template v-else><option value="new">新记录</option><option value="reviewing">处理中</option><option value="resolved">已处理</option><option value="closed">已关闭</option></template></select><button @click="load">查询</button></div>

    <div v-if="view === 'draws'" class="records">
      <article v-for="row in draws" :key="row.id">
        <div class="summary"><code>{{ row.id }}</code><h3>{{ row.requesterName }} → {{ row.responderName }}</h3><p>{{ row.reason }}</p><small>对局 {{ row.matchId }} · 房间 {{ row.roomCode }} · {{ row.modeId }}</small><small>申请 {{ new Date(row.requestedAt).toLocaleString() }}<template v-if="row.respondedAt"> · 处理 {{ new Date(row.respondedAt).toLocaleString() }}</template></small><span>{{ statusLabel(row.status) }}</span>
          <details><summary>审计记录（{{ row.history.length }}）</summary><ol><li v-for="audit in row.history" :key="audit.id"><b>{{ audit.action }}</b> · {{ audit.actorName }} · {{ new Date(audit.createdAt).toLocaleString() }}<p v-if="audit.comment">{{ audit.comment }}</p></li></ol></details>
        </div>
        <form @submit.prevent="saveDraw(row)"><label>管理状态<select v-model="row.adminStatus" :disabled="!canWrite"><option value="new">新记录</option><option value="reviewing">处理中</option><option value="resolved">已处理</option><option value="closed">已关闭</option></select></label><label>管理备注<textarea v-model="notes[row.id]" :disabled="!canWrite" rows="3" maxlength="5000"/></label><label>本次审计说明<textarea v-model="comments[row.id]" :disabled="!canWrite" rows="2" maxlength="2000"/></label><button v-if="canWrite" type="submit">保存并审计</button></form>
      </article>
      <div v-if="!loading && !draws.length" class="empty">当前筛选下没有平局申请</div>
    </div>

    <div v-else class="records">
      <article v-for="row in reports" :key="row.id">
        <div class="summary"><code>{{ row.id }}</code><h3>{{ row.reporterName }} 举报 {{ row.reportedName }}</h3><p>{{ row.description }}</p><small>对局 {{ row.matchId }} · 房间 {{ row.roomCode }} · {{ row.modeId }}</small><small>提交 {{ new Date(row.createdAt).toLocaleString() }} · 更新 {{ new Date(row.updatedAt).toLocaleString() }}</small><span>{{ statusLabel(row.status) }}</span>
          <details><summary>审计记录（{{ row.history.length }}）</summary><ol><li v-for="audit in row.history" :key="audit.id"><b>{{ audit.action }}</b> · {{ audit.actorName }} · {{ new Date(audit.createdAt).toLocaleString() }}<p v-if="audit.comment">{{ audit.comment }}</p></li></ol></details>
        </div>
        <form @submit.prevent="saveReport(row)"><label>处理状态<select v-model="row.status" :disabled="!canWrite"><option value="new">新记录</option><option value="reviewing">处理中</option><option value="resolved">已处理</option><option value="closed">已关闭</option></select></label><label>管理备注<textarea v-model="notes[row.id]" :disabled="!canWrite" rows="3" maxlength="5000"/></label><label>本次审计说明<textarea v-model="comments[row.id]" :disabled="!canWrite" rows="2" maxlength="2000"/></label><button v-if="canWrite" type="submit">保存并审计</button></form>
      </article>
      <div v-if="!loading && !reports.length" class="empty">当前筛选下没有玩家举报</div>
    </div>
  </section>
</template>

<style scoped>
.governance-panel{border:1px solid #35424a;background:#101821;padding:20px}.governance-panel>header{display:flex;align-items:flex-end;justify-content:space-between;gap:18px;padding-bottom:13px;border-bottom:1px solid #36434a}.governance-panel h2{margin:4px 0}.governance-panel p{color:#8a979c;font-size:14px;line-height:1.7}.governance-panel small{display:block;color:#75838a;font-size:14px}.governance-panel button,.governance-panel input,.governance-panel select,.governance-panel textarea{box-sizing:border-box;padding:9px;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 14px 'Microsoft YaHei','微软雅黑'}.governance-panel>nav{display:flex;margin-top:14px}.governance-panel>nav button{min-width:140px}.governance-panel>nav button.active{border-color:#d2b65d;background:#29220f;color:#edd27b}.filters{display:flex;gap:7px;margin:10px 0}.filters input{min-width:260px;flex:1}.records article{display:grid;grid-template-columns:minmax(0,1fr) 300px;gap:18px;padding:18px 0;border-top:1px solid #303c43}.summary{position:relative;min-width:0;padding-right:92px}.summary code{color:#dfc36f;overflow-wrap:anywhere}.summary h3{margin:7px 0}.summary>p{color:#d0d5d3;white-space:pre-wrap;overflow-wrap:anywhere}.summary>span{position:absolute;top:0;right:0;padding:5px 7px;border:1px solid #706032;background:#251e0d;color:#e8cb72;font-size:14px}.summary details{margin-top:12px}.summary summary{cursor:pointer;color:#d9bf6d;font-size:14px;font-weight:900}.summary ol{max-height:190px;overflow:auto;padding-left:20px;color:#aeb8b8;font-size:14px}.summary li p{margin:3px 0}.records form{display:grid;gap:8px}.records label{display:grid;gap:5px;color:#aab4b6;font-size:14px;font-weight:900}.records textarea{resize:vertical}.records form button{border-color:#397762;background:#0c281e;color:#8ce0bd}.empty{padding:38px;color:#77858a;text-align:center;font-size:14px}@media(max-width:850px){.governance-panel>header{align-items:stretch;flex-direction:column}.filters{flex-direction:column}.records article{grid-template-columns:1fr}.summary{padding-right:0}.summary>span{position:static;display:inline-block;margin-top:8px}}
</style>
