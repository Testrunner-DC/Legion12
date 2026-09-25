<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { friendApi, tournamentApi, type PlatformFriend, type Tournament, type TournamentCreateInput } from '@/l12/platform'

const props = defineProps<{ platformVersion: number }>()
const emit = defineEmits<{ created: [value: Tournament]; version: [value: number] }>()
const step = ref(1)
const busy = ref(false)
const notice = ref('')
const friends = ref<PlatformFriend[]>([])
const draftKey = 'l12-tournament-create-draft-v3'
const form = reactive<TournamentCreateInput>({
  name: '', format: 'swiss', visibility: 'public', maxPlayers: 16, startAt: undefined,
  ruleset: '现行规则', description: '', deckVisibility: 'after', disasterMode: 'season', banList: '',
  disasterCardIds: [], cardRestrictions: [], roundMinutes: 50, checkInMinutes: 5,
  timeControl: { totalTimeSeconds: 1500, operationTimeSeconds: 240, reconnectGraceSeconds: 240, disasterDecisionSeconds: 60, mulliganDecisionSeconds: 60 },
  refereeAccountIds: [], swissRounds: 4, cutSize: 8, registrationVisibility: 'public', lateGraceMinutes: 5,
})
const templates = [
  { id: 'single', label: '单败淘汰', format: 'single' as const, swissRounds: 1, cutSize: undefined },
  { id: 'swiss', label: '标准瑞士轮', format: 'swiss' as const, swissRounds: 4, cutSize: undefined },
  { id: 'swiss-cut', label: '瑞士轮 + Cut 8', format: 'swiss-cut' as const, swissRounds: 4, cutSize: 8 },
]
const stepTitle = computed(() => ['基础信息', '赛制与名额', '规则与牌组', '工作人员与计时', '预览与创建'][step.value - 1])
const timerSummary = computed(() => `总时限 ${Math.round(form.timeControl.totalTimeSeconds / 60)} 分钟；单次操作 ${Math.round(form.timeControl.operationTimeSeconds / 60)} 分钟；断线宽限 ${Math.round(form.timeControl.reconnectGraceSeconds / 60)} 分钟；天灾/调度各 ${form.timeControl.disasterDecisionSeconds}/${form.timeControl.mulliganDecisionSeconds} 秒`)

function applyTemplate(id: string) {
  const value = templates.find(item => item.id === id)
  if (!value) return
  form.format = value.format; form.swissRounds = value.swissRounds; form.cutSize = value.cutSize
}
function clearDraft() { localStorage.removeItem(draftKey); window.location.reload() }
function toggleReferee(accountId: string) { const index = form.refereeAccountIds.indexOf(accountId); if (index >= 0) form.refereeAccountIds.splice(index, 1); else form.refereeAccountIds.push(accountId) }
async function create() {
  if (!form.name.trim()) { notice.value = '请填写赛事名称'; step.value = 1; return }
  busy.value = true; notice.value = '正在执行开赛前校验…'
  try {
    await tournamentApi.create({ ...form, name: form.name.trim() }, props.platformVersion, true)
    const created = await tournamentApi.create({ ...form, name: form.name.trim() }, props.platformVersion)
    localStorage.removeItem(draftKey); notice.value = '赛事已创建'; emit('created', created); emit('version', created.version)
  } catch (error) { notice.value = error instanceof Error ? error.message : '创建失败' }
  finally { busy.value = false }
}
watch(form, value => localStorage.setItem(draftKey, JSON.stringify(value)), { deep: true })
onMounted(() => {
  try { const saved = JSON.parse(localStorage.getItem(draftKey) || 'null'); if (saved) Object.assign(form, saved) } catch { /* 忽略损坏草稿 */ }
  void friendApi.friends().then(value => { friends.value = value }).catch(() => undefined)
})
</script>

<template>
  <section class="wizard">
    <header><div><small>第 {{ step }}/5 步</small><h2>{{ stepTitle }}</h2></div><button type="button" @click="clearDraft">清空草稿</button></header>
    <div v-if="step === 1" class="fields"><label>赛事名称<input v-model.trim="form.name" maxlength="100"></label><label>可见性<select v-model="form.visibility"><option value="public">公开</option><option value="code">凭代码</option></select></label><label>计划开赛<input v-model="form.startAt" type="datetime-local"></label><label class="wide">简介<textarea v-model.trim="form.description" rows="4" maxlength="2000" /></label></div>
    <div v-else-if="step === 2" class="fields"><label>套用模板<select @change="applyTemplate(($event.target as HTMLSelectElement).value)"><option value="">自定义</option><option v-for="item in templates" :key="item.id" :value="item.id">{{ item.label }}</option></select></label><label>赛制<select v-model="form.format"><option value="single">单败淘汰</option><option value="swiss">瑞士轮</option><option value="swiss-cut">瑞士轮 + Cut</option></select></label><label>人数上限<input v-model.number="form.maxPlayers" type="number" min="2" max="256"></label><label>瑞士轮数<input v-model.number="form.swissRounds" type="number" min="1" max="20"></label><label v-if="form.format === 'swiss-cut'">Cut 人数<input v-model.number="form.cutSize" type="number" min="2" :max="form.maxPlayers"></label><p class="wide">满额后新报名自动进入候补；正式席位释放后按候补顺序递补。</p></div>
    <div v-else-if="step === 3" class="fields"><label>规则版本<input v-model.trim="form.ruleset"></label><label>牌组公开<select v-model="form.deckVisibility"><option value="always">始终公开</option><option value="after">赛后公开</option><option value="private">仅裁判可见</option></select></label><label>天灾范围<select v-model="form.disasterMode"><option value="season">赛季天灾</option><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="none">不使用</option></select></label><label>签到窗口（分钟）<input v-model.number="form.checkInMinutes" type="number" min="1" max="60"></label><p class="wide">报名不要求牌组；玩家在赛前签到时选择牌组并锁定快照。</p></div>
    <div v-else-if="step === 4" class="fields"><fieldset class="wide"><legend>从好友中选择裁判</legend><label v-for="person in friends" :key="person.accountId"><input type="checkbox" :checked="form.refereeAccountIds.includes(person.accountId)" @change="toggleReferee(person.accountId)">{{ person.username }}</label><p v-if="!friends.length">暂无可选好友，创建后仍可在管理页调整工作人员。</p></fieldset><label>总时限（秒）<input v-model.number="form.timeControl.totalTimeSeconds" type="number" min="300" max="7200"></label><label>单次操作（秒）<input v-model.number="form.timeControl.operationTimeSeconds" type="number" min="15" :max="Math.min(900, form.timeControl.totalTimeSeconds)"></label><label>断线宽限（秒）<input v-model.number="form.timeControl.reconnectGraceSeconds" type="number" min="15" max="900"></label><label>天灾决定（秒）<input v-model.number="form.timeControl.disasterDecisionSeconds" type="number" min="10" max="300"></label><label>调度决定（秒）<input v-model.number="form.timeControl.mulliganDecisionSeconds" type="number" min="10" max="300"></label><p class="wide">采用与排位一致的五项计时规范：{{ timerSummary }}</p></div>
    <div v-else class="preview"><h3>{{ form.name || '未命名赛事' }}</h3><p>{{ templates.find(item => item.format === form.format)?.label || form.format }} · 上限 {{ form.maxPlayers }} 人 · {{ form.visibility === 'public' ? '公开' : '凭代码' }}</p><p>{{ form.ruleset }} · {{ timerSummary }}</p><p>创建时先执行不落库校验，通过后再正式创建。</p></div>
    <footer><button type="button" :disabled="step === 1 || busy" @click="step--">上一步</button><span>{{ notice }}</span><button v-if="step < 5" type="button" @click="step++">下一步</button><button v-else type="button" :disabled="busy" @click="create">{{ busy ? '创建中…' : '确认创建' }}</button></footer>
  </section>
</template>

<style scoped>
.wizard{display:grid;gap:18px}.wizard header,.wizard footer{display:flex;align-items:center;justify-content:space-between;gap:12px}.wizard h2{margin:4px 0}.fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}.fields label{display:grid;gap:6px}.fields input,.fields select,.fields textarea{width:100%}.wide{grid-column:1/-1;color:var(--muted)}.preview{padding:18px;border:1px solid var(--border);border-radius:12px}.wizard footer span{color:var(--muted);font-size:13px}@media(max-width:700px){.fields{grid-template-columns:1fr}}
</style>
