<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { platformState, tournamentApi, type Tournament, type TournamentJudgeCase } from '@/l12/platform'
import { tournamentJudgeCategoryText, tournamentJudgeStatusText, tournamentJudgeUrgencyText } from '@/l12/tournamentLabels'

const props = defineProps<{ tournament: Tournament; canJudge: boolean }>()
const emit = defineEmits<{ updated: [value: Tournament]; notice: [value: string] }>()
const selectedMatch = ref(''); const category = ref('rules'); const urgency = ref('normal'); const message = ref('')
const assignees = reactive<Record<string,string>>({}); const resolutions = reactive<Record<string,string>>({})
const appealReasons = reactive<Record<string,string>>({}); const busy = ref(false)
const accountId = computed(() => platformState.account?.id || '')
const myMatches = computed(() => props.tournament.rounds.flatMap(round => round.matches)
  .filter(match => match.playerBAccountId && (match.playerAAccountId === accountId.value || match.playerBAccountId === accountId.value)))
const caseMatch = (item: TournamentJudgeCase) => props.tournament.rounds.flatMap(round => round.matches).find(match => match.id === item.matchId)
const availableAssignees = (item: TournamentJudgeCase) => {
  const match = caseMatch(item)
  return [{ accountId: props.tournament.organizerAccountId, username: props.tournament.organizerName }, ...props.tournament.referees]
    .filter(person => person.accountId !== match?.playerAAccountId && person.accountId !== match?.playerBAccountId)
}
const canAppeal = (item: TournamentJudgeCase) => {
  const match = caseMatch(item)
  return !!match && (match.playerAAccountId === accountId.value || match.playerBAccountId === accountId.value)
    && ['ruled', 'closed'].includes(item.status) && !item.appealedAt
}
async function act(work: () => Promise<Tournament>, success: string) { busy.value = true; try { emit('updated', await work()); emit('notice', success) } catch (error) { emit('notice', error instanceof Error ? error.message : '操作失败') } finally { busy.value = false } }
function callJudge() { void act(() => tournamentApi.createJudgeCase(props.tournament.id, props.tournament.version, { matchId: selectedMatch.value, category: category.value, urgency: urgency.value, message: message.value }), '裁判请求已提交') }
function assign(item: TournamentJudgeCase) { const assignee = assignees[item.id]; if (assignee) void act(() => tournamentApi.assignJudgeCase(props.tournament.id, props.tournament.version, item.id, assignee, '裁判台分派案件'), '案件已分派') }
function resolve(item: TournamentJudgeCase) { const resolution = resolutions[item.id]?.trim(); if (resolution) void act(() => tournamentApi.resolveJudgeCase(props.tournament.id, props.tournament.version, item.id, 'ruled', resolution), '裁定已记录') }
function appeal(item: TournamentJudgeCase) { const reason = appealReasons[item.id]?.trim(); if (reason) void act(() => tournamentApi.appealJudgeCase(props.tournament.id, props.tournament.version, item.id, reason), '申诉已提交') }
</script>

<template>
  <section class="judge-desk">
    <h2>裁判台</h2>
    <form v-if="myMatches.length" class="call" @submit.prevent="callJudge"><select v-model="selectedMatch" required><option value="">选择本人桌次</option><option v-for="match in myMatches" :key="match.id" :value="match.id">第 {{ match.table }} 桌 · {{ match.playerAName }} 对 {{ match.playerBName }}</option></select><select v-model="category"><option value="rules">规则问题</option><option value="technical">技术问题</option><option value="late">迟到处理</option><option value="result">赛果争议</option></select><select v-model="urgency"><option value="normal">普通</option><option value="urgent">紧急</option></select><input v-model.trim="message" required maxlength="1000" placeholder="说明问题"><button :disabled="busy">呼叫裁判</button></form>
    <p v-else class="empty">你当前没有可以呼叫裁判的本人桌次。</p>
    <div v-if="!tournament.judgeCases.length" class="empty">暂无可见案件</div>
    <article v-for="item in tournament.judgeCases" :key="item.id" class="case"><header><b>第 {{ item.table }} 桌 · {{ tournamentJudgeCategoryText(item.category) }}</b><span>{{ tournamentJudgeStatusText(item.status) }} · {{ tournamentJudgeUrgencyText(item.urgency) }}</span></header><p>{{ item.playerMessage }}</p><p v-if="item.resolution">结论：{{ item.resolution }}</p><p v-if="item.appealReason">申诉：{{ item.appealReason }}</p><div v-if="canJudge && item.canManage" class="actions"><select v-model="assignees[item.id]"><option value="">选择无利益冲突的受理人</option><option v-for="person in availableAssignees(item)" :key="person.accountId" :value="person.accountId">{{ person.username }}</option></select><button :disabled="!assignees[item.id] || busy" @click="assign(item)">分派</button><input v-model.trim="resolutions[item.id]" placeholder="裁定结论"><button :disabled="!resolutions[item.id] || busy" @click="resolve(item)">作出裁定</button></div><div v-if="canAppeal(item)" class="actions"><input v-model.trim="appealReasons[item.id]" maxlength="1000" placeholder="填写申诉理由"><button :disabled="!appealReasons[item.id] || busy" @click="appeal(item)">提交申诉</button></div></article>
  </section>
</template>

<style scoped>
.judge-desk{display:grid;gap:14px}.call,.actions{display:flex;flex-wrap:wrap;gap:8px}.call input,.actions input{flex:1;min-width:180px}.case{padding:14px;border:1px solid var(--border);border-radius:10px}.case header{display:flex;justify-content:space-between;gap:12px}.case p{color:var(--muted)}.empty{color:var(--muted);padding:20px;text-align:center}
</style>
