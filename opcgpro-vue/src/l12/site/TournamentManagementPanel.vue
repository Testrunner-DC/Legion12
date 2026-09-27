<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { friendApi, tournamentApi, type PlatformFriend, type Tournament, type TournamentStartCheck } from '@/l12/platform'

const props = defineProps<{ tournament: Tournament; accountId: string; canOrganize: boolean }>()
const emit = defineEmits<{ updated: [value: Tournament]; notice: [value: string] }>()
const busy = ref(false)
const reason = ref('')
const postponeAt = ref('')
const transferAccountId = ref('')
const transferReason = ref('')
const startCheck = ref<TournamentStartCheck | null>(null)
const friends = ref<PlatformFriend[]>([])
const cancelArmed = ref(false)
const transferCandidates = computed(() => {
  const candidates = [...friends.value, ...props.tournament.referees]
  return [...new Map(candidates.map(item => [item.accountId, item])).values()]
    .filter(item => item.accountId !== props.tournament.organizerAccountId)
})

async function mutate(work: (item: Tournament) => Promise<Tournament>, success: string) {
  if (busy.value) return
  busy.value = true
  try { emit('updated', await work(props.tournament)); emit('notice', success) }
  catch (error) { emit('notice', error instanceof Error ? error.message : '操作失败') }
  finally { busy.value = false }
}
const operationReason = () => reason.value.trim() || '主办者调整赛事阶段'
function setPhase(phase: 'registration-open' | 'registration-closed' | 'pre-check-in' | 'result-confirmation' | 'running', success: string) {
  void mutate(item => tournamentApi.setPhase(item.id, item.version, phase, operationReason()), success)
}
async function checkStart() {
  try {
    startCheck.value = await tournamentApi.startCheck(props.tournament.id)
    emit('notice', startCheck.value.canStart ? '开赛检查通过' : '仍有阻塞项')
  } catch (error) { emit('notice', error instanceof Error ? error.message : '检查失败') }
}
function start() { void mutate(item => tournamentApi.start(item.id, item.version, '开赛检查完成'), '赛事已开始') }
function postpone() {
  if (!postponeAt.value) return
  void mutate(item => tournamentApi.postpone(item.id, item.version, new Date(postponeAt.value).toISOString(),
    reason.value.trim() || '赛事延期'), '新的开赛时间已发布')
}
function cancel() {
  if (!cancelArmed.value) { cancelArmed.value = true; emit('notice', '请再次点击确认取消；进行中的赛事房间会安全终止'); return }
  void mutate(item => tournamentApi.cancel(item.id, item.version, reason.value.trim() || '主办者取消赛事'), '赛事已取消并归档')
}
function complete() { void mutate(item => tournamentApi.complete(item.id, item.version, '全部赛果已复核'), '赛事已结束并归档') }
function transferOrganizer() {
  if (transferAccountId.value && transferReason.value)
    void mutate(item => tournamentApi.requestOrganizerTransfer(item.id, item.version,
      transferAccountId.value, transferReason.value), '交接邀请已发送，需由接任者确认')
}
function decideTransfer(accept: boolean) {
  if (props.tournament.pendingOrganizerTransfer)
    void mutate(item => tournamentApi.decideOrganizerTransfer(item.id, item.version,
      item.pendingOrganizerTransfer!.id, accept), accept ? '已接任主办身份' : '已拒绝主办交接')
}
onMounted(() => { void friendApi.friends().then(value => { friends.value = value }).catch(() => undefined) })
</script>

<template>
  <section class="management-panel">
    <template v-if="canOrganize">
      <header class="management-head"><div><small>TOURNAMENT OPERATIONS</small><h2>赛事生命周期</h2><p>按顺序管理报名、签到、开赛与成绩确认。</p></div><span>{{ tournament.status }}</span></header>
      <label class="reason-field"><span>本次操作理由</span><input v-model.trim="reason" maxlength="500" placeholder="供工作人员审计与后续复核"></label>
      <section v-if="tournament.status === 'registration'" class="operation-group"><header><b>报名与签到</b><span>调整当前阶段或发布新的开赛时间</span></header><div class="actions"><button v-if="tournament.phase === 'registration-open'" @click="setPhase('registration-closed','报名已关闭')">关闭报名</button><button v-if="tournament.phase !== 'registration-open'" @click="setPhase('registration-open','报名已开放')">重新开放报名</button><button v-if="tournament.phase !== 'pre-check-in'" class="primary" @click="setPhase('pre-check-in','已进入赛前签到')">进入赛前签到</button><button v-if="tournament.phase === 'pre-check-in'" @click="setPhase('registration-closed','已退出赛前签到')">退出赛前签到</button></div><div class="postpone"><label><span>新的计划时间</span><input v-model="postponeAt" type="datetime-local"></label><button :disabled="!postponeAt" @click="postpone">发布延期</button></div></section>
      <section class="operation-group"><header><b>开赛与结算</b><span>开赛前必须先完成检查</span></header><div class="actions"><button v-if="tournament.status === 'registration'" @click="checkStart">执行开赛检查</button><button v-if="tournament.status === 'registration'" class="primary" :disabled="!startCheck?.canStart" @click="start">正式开赛</button><button v-if="tournament.status === 'running' && tournament.phase === 'running'" @click="setPhase('result-confirmation','进入成绩确认')">进入成绩确认</button><button v-if="tournament.phase === 'result-confirmation'" @click="setPhase('running','返回赛事进行中')">返回赛事进行中</button><button v-if="tournament.phase === 'result-confirmation'" class="primary" @click="complete">确认成绩并归档</button></div><article v-if="startCheck" class="start-check" :class="{passed:startCheck.canStart}"><b>{{ startCheck.canStart ? '开赛检查通过' : '暂不可开赛' }}</b><p v-for="item in startCheck.blockers" :key="item"><span>阻塞</span>{{ item }}</p><p v-for="item in startCheck.warnings" :key="item"><span>提醒</span>{{ item }}</p></article></section>
    </template>

    <section class="transfer-group"><header><div><small>ORGANIZER TRANSFER</small><h2>主办身份交接</h2></div><p>接任者确认后才会取得主办权限。</p></header><div v-if="tournament.pendingOrganizerTransfer" class="pending-transfer"><p><small>等待确认</small><b>{{ tournament.pendingOrganizerTransfer.toUsername }}</b></p><div v-if="tournament.pendingOrganizerTransfer.toAccountId === accountId" class="actions"><button class="primary" @click="decideTransfer(true)">接受交接</button><button @click="decideTransfer(false)">拒绝</button></div></div><div v-else-if="canOrganize" class="transfer-form"><label><span>接任者</span><select v-model="transferAccountId"><option value="">选择本场裁判或主办者好友</option><option v-for="candidate in transferCandidates" :key="candidate.accountId" :value="candidate.accountId">{{ candidate.username }}</option></select></label><label><span>交接理由</span><input v-model.trim="transferReason" placeholder="说明交接原因"></label><button :disabled="!transferAccountId || !transferReason || busy" @click="transferOrganizer">发起交接</button></div></section>

    <section v-if="canOrganize && !['completed','canceled'].includes(tournament.status)" class="danger-zone"><header><div><small>DANGER ZONE</small><h2>取消赛事</h2></div><p>进行中的赛事房间会安全终止，赛事随后归档。</p></header><button class="danger" @click="cancel">{{ cancelArmed ? '确认取消赛事' : '取消赛事' }}</button></section>
  </section>
</template>

<style scoped>
.management-panel{display:grid;gap:14px;color:var(--l12-ui-text)}.management-head,.operation-group>header,.transfer-group>header,.danger-zone>header{display:flex;align-items:flex-end;justify-content:space-between;gap:16px}.management-head small,.transfer-group header small,.danger-zone header small{color:var(--l12-ui-info);font:900 11px ui-monospace,monospace;letter-spacing:.14em}.management-head h2,.transfer-group h2,.danger-zone h2{margin:4px 0;font-size:20px}.management-head p,.transfer-group header>p,.danger-zone header p{margin:0;color:var(--l12-ui-text-muted);font-size:12px}.management-head>span{padding:4px 7px;border:1px solid var(--l12-ui-line-strong);border-radius:999px;color:var(--l12-ui-text-muted);font-size:11px}.reason-field,.postpone label,.transfer-form label{display:grid;gap:5px}.reason-field>span,.postpone label>span,.transfer-form label>span{color:var(--l12-ui-text-muted);font-size:10px;font-weight:800}.reason-field input,.postpone input,.transfer-form input,.transfer-form select{width:100%;min-height:40px;padding:8px}.operation-group,.transfer-group{display:grid;gap:12px;padding:14px;border:1px solid var(--l12-ui-line);background:rgba(7,13,18,.5)}.operation-group>header{align-items:flex-start}.operation-group>header span{color:var(--l12-ui-text-muted);font-size:12px}.actions{display:flex;align-items:center;flex-wrap:wrap;gap:8px}.postpone{display:grid;grid-template-columns:minmax(220px,1fr) auto;align-items:end;gap:8px;padding-top:12px;border-top:1px solid var(--l12-ui-line)}.start-check{display:grid;gap:7px;padding:12px;border-left:3px solid var(--l12-ui-danger);background:#271218}.start-check.passed{border-color:var(--l12-ui-success);background:#10231c}.start-check p{display:flex;gap:8px;margin:0;color:var(--l12-ui-text-soft);font-size:12px}.start-check p span{flex:0 0 auto;color:var(--l12-ui-text-muted);font-weight:900}.transfer-group>header{align-items:flex-start}.pending-transfer{display:flex;align-items:center;justify-content:space-between;gap:12px}.pending-transfer p{display:grid;gap:3px;margin:0}.pending-transfer small{color:var(--l12-ui-text-muted)}.transfer-form{display:grid;grid-template-columns:minmax(220px,.8fr) minmax(220px,1fr) auto;align-items:end;gap:8px}.danger-zone{display:flex;align-items:center;justify-content:space-between;gap:20px;padding:15px;border:1px solid #71343c;background:#211016}.danger-zone header small{color:var(--l12-ui-danger)}.danger-zone .danger{flex:0 0 auto;min-width:132px}
@media(max-width:800px){.transfer-form{grid-template-columns:1fr 1fr}.transfer-form button{grid-column:1/-1}.danger-zone{align-items:flex-start;flex-direction:column}.danger-zone .danger{width:100%}}
@media(max-width:700px){.management-head{align-items:flex-start}.management-head>span{display:none}.operation-group,.transfer-group{padding:12px}.operation-group>header,.transfer-group>header{display:grid}.actions{display:grid;grid-template-columns:1fr 1fr}.actions button{min-height:44px}.postpone,.transfer-form{grid-template-columns:1fr}.reason-field input,.postpone input,.postpone button,.transfer-form input,.transfer-form button,.danger-zone button{min-height:44px}.transfer-form select{height:44px;min-height:44px!important}.transfer-form button{grid-column:auto}.pending-transfer{align-items:flex-start;flex-direction:column}.pending-transfer .actions{width:100%}.danger-zone{padding:12px}.danger-zone>header{display:grid}}
@media(max-width:390px){.actions{grid-template-columns:1fr}}
</style>
