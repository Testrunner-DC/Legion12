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
      <h2>赛事生命周期</h2>
      <label>操作理由<input v-model.trim="reason" maxlength="500"></label>
      <div v-if="tournament.status === 'registration'" class="actions">
        <button v-if="tournament.phase === 'registration-open'" @click="setPhase('registration-closed','报名已关闭')">关闭报名</button>
        <button v-if="tournament.phase !== 'registration-open'" @click="setPhase('registration-open','报名已开放')">重新开放报名</button>
        <button v-if="tournament.phase !== 'pre-check-in'" @click="setPhase('pre-check-in','已进入赛前签到')">进入赛前签到</button>
        <button v-if="tournament.phase === 'pre-check-in'" @click="setPhase('registration-closed','已退出赛前签到')">退出赛前签到</button>
        <input v-model="postponeAt" type="datetime-local"><button @click="postpone">延期</button>
      </div>
      <div class="actions">
        <button v-if="tournament.status === 'registration'" @click="checkStart">执行开赛检查</button>
        <button v-if="tournament.status === 'registration'" :disabled="!startCheck?.canStart" @click="start">正式开赛</button>
        <button v-if="tournament.status === 'running' && tournament.phase === 'running'" @click="setPhase('result-confirmation','进入成绩确认')">进入成绩确认</button>
        <button v-if="tournament.phase === 'result-confirmation'" @click="setPhase('running','返回赛事进行中')">返回赛事进行中</button>
        <button v-if="tournament.phase === 'result-confirmation'" @click="complete">确认成绩并归档</button>
        <button v-if="!['completed','canceled'].includes(tournament.status)" class="danger" @click="cancel">{{ cancelArmed ? '确认取消赛事' : '取消赛事' }}</button>
      </div>
      <article v-if="startCheck">
        <b>{{ startCheck.canStart ? '检查通过' : '暂不可开赛' }}</b>
        <p v-for="item in startCheck.blockers" :key="item">阻塞：{{ item }}</p>
        <p v-for="item in startCheck.warnings" :key="item">提醒：{{ item }}</p>
      </article>
    </template>

    <h2>主办身份交接</h2>
    <div v-if="tournament.pendingOrganizerTransfer" class="actions">
      <p>等待 {{ tournament.pendingOrganizerTransfer.toUsername }} 确认接任</p>
      <button v-if="tournament.pendingOrganizerTransfer.toAccountId === accountId" @click="decideTransfer(true)">接受交接</button>
      <button v-if="tournament.pendingOrganizerTransfer.toAccountId === accountId" @click="decideTransfer(false)">拒绝</button>
    </div>
    <div v-else-if="canOrganize" class="actions">
      <select v-model="transferAccountId"><option value="">选择本场裁判或主办者好友</option><option v-for="candidate in transferCandidates" :key="candidate.accountId" :value="candidate.accountId">{{ candidate.username }}</option></select>
      <input v-model.trim="transferReason" placeholder="交接理由">
      <button :disabled="!transferAccountId || !transferReason || busy" @click="transferOrganizer">发起交接</button>
    </div>
  </section>
</template>

<style scoped>
.management-panel{display:grid;gap:14px}.actions{display:flex;gap:8px;align-items:center;flex-wrap:wrap}.danger{color:var(--danger)}
</style>
