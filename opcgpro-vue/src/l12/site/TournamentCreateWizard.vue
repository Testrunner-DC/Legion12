<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import {
  friendApi, getEffectiveOperationsPolicy, tournamentApi,
  type EffectiveOperationsPolicy, type PlatformFriend, type Tournament, type TournamentCreateInput,
} from '@/l12/platform'
import {
  buildTournamentCreateInput, normalizeTournamentFormatInPlace, tournamentDisasterSnapshot,
} from '@/l12/tournamentCreatePolicy'
import { tournamentTimeControlLabel, tournamentTimeControlMinutes, tournamentTimeControlSeconds } from '@/l12/tournamentTime'

const props = defineProps<{ platformVersion: number }>()
const emit = defineEmits<{ created: [value: Tournament]; version: [value: number] }>()
const step = ref(1)
const busy = ref(false)
const notice = ref('')
const friends = ref<PlatformFriend[]>([])
const operationsPolicy = ref<EffectiveOperationsPolicy | null>(null)
const policyLoading = ref(true)
const policyError = ref('')
const draftKey = 'l12-tournament-create-draft-v3'
const form = reactive<TournamentCreateInput>({
  name: '', format: 'swiss', visibility: 'public', maxPlayers: 16, startAt: undefined,
  ruleset: '现行规则', description: '', deckVisibility: 'after', disasterMode: 'season', banList: '',
  disasterCardIds: [], cardRestrictions: [], roundMinutes: 50, checkInMinutes: 5,
  timeControl: { totalTimeSeconds: 1500, operationTimeSeconds: 240, reconnectGraceSeconds: 240, disasterDecisionSeconds: 60, mulliganDecisionSeconds: 60 },
  refereeAccountIds: [], swissRounds: 4, cutSize: undefined, registrationVisibility: 'public', lateGraceMinutes: 5,
})
const timeControlMinutes = reactive(tournamentTimeControlMinutes(form.timeControl))
const templates = [
  { id: 'single', label: '单败淘汰', format: 'single' as const, swissRounds: 1, cutSize: undefined },
  { id: 'swiss', label: '标准瑞士轮', format: 'swiss' as const, swissRounds: 4, cutSize: undefined },
  { id: 'swiss-cut', label: '瑞士轮 + Cut 8', format: 'swiss-cut' as const, swissRounds: 4, cutSize: 8 },
]
const stepTitle = computed(() => ['基础信息', '赛制与名额', '规则与牌组', '工作人员与计时', '预览与创建'][step.value - 1])
const timerSummary = computed(() => tournamentTimeControlLabel(tournamentTimeControlSeconds(timeControlMinutes)))

function applyTemplate(id: string) {
  const value = templates.find(item => item.id === id)
  if (!value) return
  form.format = value.format; form.swissRounds = value.swissRounds; form.cutSize = value.cutSize
  normalizeTournamentFormatInPlace(form)
}
function applyDisasterPolicy() {
  if (form.disasterMode === 'none') { form.disasterCardIds = []; policyError.value = ''; return }
  if (!operationsPolicy.value) { form.disasterCardIds = []; return }
  try { form.disasterCardIds = tournamentDisasterSnapshot(form.disasterMode, operationsPolicy.value); policyError.value = '' }
  catch (error) { form.disasterCardIds = []; policyError.value = error instanceof Error ? error.message : '当前运营策略天灾池不可用' }
}
async function loadOperationsPolicy() {
  policyLoading.value = true
  try { operationsPolicy.value = await getEffectiveOperationsPolicy(); applyDisasterPolicy() }
  catch (error) {
    operationsPolicy.value = null; form.disasterCardIds = []
    policyError.value = error instanceof Error ? `权威运营策略加载失败：${error.message}` : '权威运营策略加载失败，请稍后重试'
  } finally { policyLoading.value = false }
}
function clearDraft() { localStorage.removeItem(draftKey); window.location.reload() }
function toggleReferee(accountId: string) { const index = form.refereeAccountIds.indexOf(accountId); if (index >= 0) form.refereeAccountIds.splice(index, 1); else form.refereeAccountIds.push(accountId) }
async function create() {
  if (!form.name.trim()) { notice.value = '请填写赛事名称'; step.value = 1; return }
  busy.value = true; notice.value = '正在执行开赛前校验…'
  try {
    const policy = await getEffectiveOperationsPolicy()
    operationsPolicy.value = policy
    const input = buildTournamentCreateInput({ ...form }, tournamentTimeControlSeconds(timeControlMinutes), policy)
    normalizeTournamentFormatInPlace(form)
    form.disasterCardIds = [...input.disasterCardIds]
    await tournamentApi.create(input, props.platformVersion, true)
    const created = await tournamentApi.create(input, props.platformVersion)
    localStorage.removeItem(draftKey); notice.value = '赛事已创建'; emit('created', created); emit('version', created.version)
  } catch (error) { notice.value = error instanceof Error ? error.message : '创建失败' }
  finally { busy.value = false }
}
watch(form, value => localStorage.setItem(draftKey, JSON.stringify(value)), { deep: true })
watch(timeControlMinutes, value => Object.assign(form.timeControl, tournamentTimeControlSeconds(value)), { deep: true })
watch(() => form.format, () => normalizeTournamentFormatInPlace(form))
watch(() => form.disasterMode, applyDisasterPolicy)
onMounted(async () => {
  try { const saved = JSON.parse(localStorage.getItem(draftKey) || 'null'); if (saved) Object.assign(form, saved) } catch { /* 忽略损坏草稿 */ }
  normalizeTournamentFormatInPlace(form)
  Object.assign(timeControlMinutes, tournamentTimeControlMinutes(form.timeControl))
  void friendApi.friends().then(value => { friends.value = value }).catch(() => undefined)
  await loadOperationsPolicy()
})
</script>

<template>
  <section class="wizard">
    <header class="wizard-head"><div><small>CREATE TOURNAMENT · 第 {{ step }}/5 步</small><h2>{{ stepTitle }}</h2><p>草稿会自动保存在当前设备。</p></div><button type="button" @click="clearDraft">清空草稿</button></header>
    <ol class="stepper" aria-label="创建赛事进度"><li v-for="(title, index) in ['基础信息','赛制名额','规则牌组','人员计时','预览创建']" :key="title" :class="{active: step === index + 1, done: step > index + 1}" :aria-current="step === index + 1 ? 'step' : undefined"><b>{{ index + 1 }}</b><span>{{ title }}</span></li></ol>
    <div v-if="step === 1" class="fields"><label>赛事名称<input v-model.trim="form.name" maxlength="100" placeholder="为参赛者提供易识别的名称"></label><label>可见性<select v-model="form.visibility"><option value="public">公开</option><option value="code">凭代码</option></select></label><label>计划开赛<input v-model="form.startAt" type="datetime-local"></label><label class="wide">简介<textarea v-model.trim="form.description" rows="4" maxlength="2000" placeholder="说明赛事定位、适合人群与重要安排" /></label></div>
    <div v-else-if="step === 2" class="fields"><label>套用模板<select @change="applyTemplate(($event.target as HTMLSelectElement).value)"><option value="">自定义</option><option v-for="item in templates" :key="item.id" :value="item.id">{{ item.label }}</option></select></label><label>赛制<select v-model="form.format"><option value="single">单败淘汰</option><option value="swiss">瑞士轮</option><option value="swiss-cut">瑞士轮 + Cut</option></select></label><label>人数上限<input v-model.number="form.maxPlayers" type="number" min="2" max="256"></label><label v-if="form.format !== 'single'">瑞士轮数<input v-model.number="form.swissRounds" type="number" min="1" max="20"></label><label v-if="form.format === 'swiss-cut'">Cut 人数<input v-model.number="form.cutSize" type="number" min="2" :max="form.maxPlayers"></label><p class="wide info-note"><b>候补规则</b><span>满额后新报名自动进入候补；正式席位释放后按候补顺序递补。</span></p></div>
    <div v-else-if="step === 3" class="fields"><label>规则版本<input v-model.trim="form.ruleset"></label><label>牌组公开<select v-model="form.deckVisibility"><option value="always">始终公开</option><option value="after">赛后公开</option><option value="private">仅裁判可见</option></select></label><label>天灾范围<select v-model="form.disasterMode"><option value="season">赛季天灾</option><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="none">不使用</option></select></label><label>签到窗口（分钟）<input v-model.number="form.checkInMinutes" type="number" min="1" max="60"></label><p class="wide info-note"><b>牌组锁定</b><span>报名不要求牌组；玩家只在赛前签到时选择牌组并锁定快照。</span></p><p v-if="policyLoading" class="wide status-note">正在读取权威运营策略…</p><p v-else-if="policyError" class="wide policy-error" role="alert">{{ policyError }}</p><p v-else class="wide status-note">本场将冻结当前运营策略中的 {{ form.disasterCardIds.length }} 张天灾；不使用天灾时快照为空。</p></div>
    <div v-else-if="step === 4" class="fields timing-fields"><fieldset class="wide referee-field"><legend>从好友中选择裁判</legend><label v-for="person in friends" :key="person.accountId"><input type="checkbox" :checked="form.refereeAccountIds.includes(person.accountId)" @change="toggleReferee(person.accountId)"><span>{{ person.username }}</span></label><p v-if="!friends.length">暂无可选好友，创建后仍可在管理页调整工作人员。</p></fieldset><div class="wide timing-intro"><small>RANKED TIME CONTROL</small><h3>与排位一致的五项计时</h3><p>每一项分别控制对应的对局阶段，不使用赛事总时间代替。</p></div><label>总时限（分钟）<input v-model.number="timeControlMinutes.total" type="number" min="5" max="120" step="0.25"></label><label>单次操作（分钟）<input v-model.number="timeControlMinutes.operation" type="number" min="0.25" :max="Math.min(15, timeControlMinutes.total)" step="0.25"></label><label>断线宽限（分钟）<input v-model.number="timeControlMinutes.reconnect" type="number" min="0.25" max="15" step="0.25"></label><label>天灾决定（分钟）<input v-model.number="timeControlMinutes.disaster" type="number" min="0.25" max="5" step="0.25"></label><label>调度决定（分钟）<input v-model.number="timeControlMinutes.mulligan" type="number" min="0.25" max="5" step="0.25"></label><p class="wide timer-summary">{{ timerSummary }}</p></div>
    <div v-else class="preview"><small>READY TO CREATE</small><h3>{{ form.name || '未命名赛事' }}</h3><dl><div><dt>赛制与人数</dt><dd>{{ templates.find(item => item.format === form.format)?.label || form.format }} · 上限 {{ form.maxPlayers }} 人</dd></div><div><dt>可见性</dt><dd>{{ form.visibility === 'public' ? '公开' : '凭代码' }}</dd></div><div><dt>规则版本</dt><dd>{{ form.ruleset }}</dd></div><div><dt>计时规范</dt><dd>{{ timerSummary }}</dd></div><div><dt>牌组提交</dt><dd>报名时无需提交，赛前签到时锁定</dd></div></dl><p>创建时先执行不落库校验，通过后再正式创建。</p></div>
    <footer class="wizard-footer"><button type="button" :disabled="step === 1 || busy" @click="step--">上一步</button><span role="status" aria-live="polite">{{ notice }}</span><button v-if="step < 5" class="primary" type="button" @click="step++">下一步</button><button v-else class="primary" type="button" :disabled="busy" @click="create">{{ busy ? '创建中…' : '确认创建' }}</button></footer>
  </section>
</template>

<style scoped>
.wizard{display:grid;gap:20px;color:var(--l12-ui-text)}.wizard button{min-height:40px;padding:0 14px;border:1px solid var(--l12-ui-line-strong);border-radius:var(--l12-ui-radius-sm);background:var(--l12-ui-control);color:var(--l12-ui-text-soft);font-weight:800}.wizard-head,.wizard-footer{display:flex;align-items:center;justify-content:space-between;gap:16px}.wizard-head h2{margin:4px 0;font-size:23px}.wizard-head small,.timing-intro small,.preview>small{color:var(--l12-ui-info);font:900 11px ui-monospace,monospace;letter-spacing:.14em}.wizard-head p{margin:0;color:var(--l12-ui-text-muted);font-size:12px}.stepper{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));margin:0;padding:0;list-style:none}.stepper li{position:relative;display:grid;justify-items:center;gap:6px;color:var(--l12-ui-text-muted);font-size:11px;font-weight:800;text-align:center}.stepper li::before{content:'';position:absolute;z-index:0;top:15px;right:50%;left:-50%;height:1px;background:var(--l12-ui-line-strong)}.stepper li:first-child::before{display:none}.stepper b{z-index:1;display:grid;width:30px;height:30px;place-items:center;border:1px solid var(--l12-ui-line-strong);border-radius:50%;background:var(--l12-ui-control);color:var(--l12-ui-text-muted)}.stepper .active,.stepper .done{color:var(--l12-ui-text-soft)}.stepper .active b{border-color:var(--l12-ui-accent);background:#3b3017;color:#f4db86}.stepper .done b{border-color:#32767a;background:#10282c;color:#83d9de}.fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px;padding:20px;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-md);background:rgba(7,13,18,.54)}.fields>label{display:grid;align-content:start;gap:7px;color:var(--l12-ui-text-soft);font-size:13px;font-weight:800}.fields input,.fields select,.fields textarea{width:100%;min-height:42px;padding:9px 10px}.fields textarea{resize:vertical}.wide{grid-column:1/-1}.info-note,.status-note,.policy-error,.timer-summary{display:flex;align-items:flex-start;gap:12px;margin:0;padding:12px 14px;border-left:3px solid var(--l12-ui-info);background:#0b2025;color:var(--l12-ui-text-soft);font-size:13px}.info-note b{flex:0 0 auto;color:#8ee0e4}.policy-error{border-color:var(--l12-ui-danger);background:#271218;color:#efb1b7}.status-note{color:var(--l12-ui-text-muted)}.referee-field{display:flex;min-width:0;flex-wrap:wrap;gap:8px;padding:14px;border:1px solid var(--l12-ui-line)}.referee-field legend{padding:0 6px;color:var(--l12-ui-text-soft);font-size:13px;font-weight:900}.referee-field label{display:flex;min-height:40px;align-items:center;gap:7px;padding:0 10px;border:1px solid var(--l12-ui-line);background:var(--l12-ui-control);font-size:13px}.referee-field input{width:auto;min-height:0}.referee-field p{width:100%;margin:0;color:var(--l12-ui-text-muted);font-size:13px}.timing-intro{padding-bottom:4px;border-bottom:1px solid var(--l12-ui-line)}.timing-intro h3{margin:4px 0}.timing-intro p{margin:0 0 10px;color:var(--l12-ui-text-muted);font-size:13px}.timer-summary{border-color:var(--l12-ui-accent-line);background:#211c0f;color:#eed583}.preview{padding:22px;border:1px solid var(--l12-ui-accent-line);border-radius:var(--l12-ui-radius-md);background:linear-gradient(145deg,#171b18,var(--l12-ui-panel));box-shadow:var(--l12-ui-shadow-panel)}.preview h3{margin:5px 0 18px;font-size:24px}.preview dl{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin:0}.preview dl>div{padding:12px;border:1px solid var(--l12-ui-line);background:var(--l12-ui-control)}.preview dt{color:var(--l12-ui-text-muted);font-size:11px;font-weight:800}.preview dd{margin:5px 0 0;color:var(--l12-ui-text-soft);font-size:13px;font-weight:800}.preview p{margin:16px 0 0;color:var(--l12-ui-text-muted);font-size:13px}.wizard-footer{padding-top:16px;border-top:1px solid var(--l12-ui-line)}.wizard-footer span{flex:1;color:var(--l12-ui-text-muted);font-size:13px;text-align:center}.wizard-footer button{min-width:104px}
@media(max-width:700px){.wizard{gap:14px}.wizard-head{align-items:flex-start}.wizard-head h2{font-size:20px}.wizard-head button{min-height:44px}.stepper{overflow-x:auto;justify-content:start;padding-bottom:4px}.stepper li{min-width:72px}.fields{grid-template-columns:1fr;padding:14px}.wide{grid-column:auto}.fields input,.fields textarea,.wizard button{min-height:44px}.fields select{height:44px;min-height:44px!important}.preview{padding:16px}.preview dl{grid-template-columns:1fr}.wizard-footer{display:grid;grid-template-columns:1fr 1fr}.wizard-footer span{grid-column:1/-1;grid-row:1;min-height:18px}.wizard-footer button{width:100%}}
@media(max-width:390px){.wizard-head{display:grid}.wizard-head button{width:100%}.stepper li{min-width:66px}.fields{padding:12px}.info-note,.status-note,.policy-error,.timer-summary{display:grid;gap:5px}}
</style>
