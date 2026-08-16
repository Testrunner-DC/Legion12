<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { l12State } from '@/l12/net'

type TournamentStatus = 'registration' | 'running' | 'completed'
interface TournamentMatch { table: number; playerA: string; playerB: string; result?: string }
interface Tournament {
  id: string
  code: string
  name: string
  organizer: string
  referees: string[]
  status: TournamentStatus
  format: 'single' | 'swiss' | 'league'
  visibility: 'public' | 'code'
  maxPlayers: number
  startAt: string
  ruleset: string
  description: string
  participants: string[]
  matches: TournamentMatch[]
  updatedAt: string
}

const storageKey = 'l12-tournaments-v1'
const tab = ref<'discover' | 'mine' | 'create'>('discover')
const search = ref('')
const statusFilter = ref<'all' | TournamentStatus>('all')
const detailId = ref<string | null>(null)
const notice = ref('')
const tournaments = ref<Tournament[]>(load())
const form = reactive({ name: '', format: 'swiss' as Tournament['format'], visibility: 'public' as Tournament['visibility'], maxPlayers: 16, startAt: '', ruleset: 'S1 / S2 当前规则', referees: '', description: '' })

function load(): Tournament[] {
  try { return (JSON.parse(localStorage.getItem(storageKey) || '[]') as Tournament[]).map(item => ({ ...item, referees: item.referees ?? [] })) } catch { return [] }
}
function persist() { localStorage.setItem(storageKey, JSON.stringify(tournaments.value)) }
function code() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  return Array.from(crypto.getRandomValues(new Uint8Array(6)), value => alphabet[value % alphabet.length]).join('')
}
const nickname = computed(() => l12State.nickname.trim() || '本机玩家')
const visibleTournaments = computed(() => tournaments.value.filter(item => {
  if (tab.value === 'mine' && item.organizer !== nickname.value && !item.participants.includes(nickname.value)) return false
  if (statusFilter.value !== 'all' && item.status !== statusFilter.value) return false
  const query = search.value.trim().toLowerCase()
  return !query || item.name.toLowerCase().includes(query) || item.code.toLowerCase().includes(query) || item.organizer.toLowerCase().includes(query)
}))
const detail = computed(() => tournaments.value.find(item => item.id === detailId.value) ?? null)
const statusText = (status: TournamentStatus) => ({ registration: '报名中', running: '进行中', completed: '已结束' }[status])
const formatText = (format: Tournament['format']) => ({ single: '单败淘汰', swiss: '瑞士轮', league: '循环赛' }[format])

function createTournament() {
  if (!form.name.trim()) { notice.value = '请输入赛事名称'; return }
  const now = new Date().toISOString()
  const item: Tournament = {
    id: crypto.randomUUID(), code: code(), name: form.name.trim(), organizer: nickname.value,
    status: 'registration', format: form.format, visibility: form.visibility,
    referees: form.referees.split(/[，,]/).map(name => name.trim()).filter(Boolean),
    maxPlayers: Math.max(2, Math.min(256, Number(form.maxPlayers) || 16)), startAt: form.startAt,
    ruleset: form.ruleset.trim() || '当前规则', description: form.description.trim(),
    participants: [nickname.value], matches: [], updatedAt: now,
  }
  tournaments.value.unshift(item); persist(); detailId.value = item.id; tab.value = 'mine'; notice.value = '赛事已建立，可复制赛事代码邀请玩家'
  Object.assign(form, { name: '', format: 'swiss', visibility: 'public', maxPlayers: 16, startAt: '', ruleset: 'S1 / S2 当前规则', referees: '', description: '' })
}
function join(item: Tournament) {
  if (item.status !== 'registration' || item.participants.length >= item.maxPlayers || item.participants.includes(nickname.value)) return
  item.participants.push(nickname.value); item.updatedAt = new Date().toISOString(); persist(); notice.value = `已报名「${item.name}」`
}
function start(item: Tournament) {
  if (item.organizer !== nickname.value || item.status !== 'registration' || item.participants.length < 2) return
  item.status = 'running'; item.matches = []
  for (let index = 0; index < item.participants.length; index += 2) item.matches.push({ table: index / 2 + 1, playerA: item.participants[index], playerB: item.participants[index + 1] ?? '轮空' })
  item.updatedAt = new Date().toISOString(); persist()
}
function finish(item: Tournament) { if (item.organizer === nickname.value && item.status === 'running') { item.status = 'completed'; item.updatedAt = new Date().toISOString(); persist() } }
async function copyCode(item: Tournament) { await navigator.clipboard.writeText(item.code); notice.value = `赛事代码 ${item.code} 已复制` }
</script>

<template>
  <div class="tournament-page">
    <header class="tournament-head"><div><small>TOURNAMENT CENTER</small><h1>赛事中心</h1><p>发现公开赛事、使用赛事代码加入，或建立并管理自己的比赛。</p></div><button class="gold" @click="tab = 'create'">举办赛事</button></header>
    <div class="prototype-note"><b>赛事编排已可在当前浏览器使用</b><span>账户体系与服务器赛事库接入后再开放跨设备发布；当前不会伪造在线报名数据。</span></div>
    <nav class="tournament-tabs"><button :class="{ active: tab === 'discover' }" @click="tab = 'discover'">发现赛事</button><button :class="{ active: tab === 'mine' }" @click="tab = 'mine'">我的赛事</button><button :class="{ active: tab === 'create' }" @click="tab = 'create'">创建向导</button></nav>

    <section v-if="tab !== 'create'" class="registry">
      <header><input v-model="search" placeholder="搜索赛事名称、主办人或赛事代码"/><select v-model="statusFilter"><option value="all">全部状态</option><option value="registration">报名中</option><option value="running">进行中</option><option value="completed">已结束</option></select></header>
      <article v-for="item in visibleTournaments" :key="item.id">
        <div><small>{{ item.code }}</small><h2>{{ item.name }}</h2><p>{{ item.description || '赛事方尚未发布说明。' }}</p></div>
        <dl><div><dt>状态</dt><dd :class="item.status">{{ statusText(item.status) }}</dd></div><div><dt>赛制</dt><dd>{{ formatText(item.format) }}</dd></div><div><dt>人数</dt><dd>{{ item.participants.length }} / {{ item.maxPlayers }}</dd></div><div><dt>主办人</dt><dd>{{ item.organizer }}</dd></div></dl>
        <div class="event-actions"><button @click="detailId = item.id">赛事详情</button><button v-if="item.status === 'registration' && !item.participants.includes(nickname)" class="gold" @click="join(item)">报名参赛</button><button v-else @click="copyCode(item)">复制代码</button></div>
      </article>
      <div v-if="!visibleTournaments.length" class="empty"><b>{{ tab === 'mine' ? '还没有我的赛事' : '暂无符合条件的赛事' }}</b><span>可以使用右上角“举办赛事”建立第一场比赛。</span></div>
    </section>

    <section v-else class="create-panel">
      <header><small>ORGANIZER WORKFLOW</small><h2>创建赛事</h2><p>先建立赛事资料，随后通过赛事详情页管理报名、轮次与结束状态。</p></header>
      <div class="form-grid"><label class="wide">赛事名称<input v-model="form.name" maxlength="40" placeholder="例如：十二军团周末交流赛"/></label><label>赛制<select v-model="form.format"><option value="swiss">瑞士轮</option><option value="single">单败淘汰</option><option value="league">循环赛</option></select></label><label>人数上限<input v-model.number="form.maxPlayers" type="number" min="2" max="256"/></label><label>加入方式<select v-model="form.visibility"><option value="public">公开发现与代码均可加入</option><option value="code">仅赛事代码加入</option></select></label><label>计划开始时间<input v-model="form.startAt" type="datetime-local"/></label><label class="wide">使用规则<input v-model="form.ruleset"/></label><label class="wide">裁判昵称（用逗号分隔）<input v-model="form.referees" placeholder="裁判甲，裁判乙"/><small>裁判全知视角必须由服务器核验身份，普通观战入口不能切换为裁判。</small></label><label class="wide">赛事说明<textarea v-model="form.description" rows="5" maxlength="500" placeholder="报名条件、轮次安排、联络方式等"/></label></div>
      <button class="gold create-action" @click="createTournament">建立赛事</button>
    </section>

    <div v-if="detail" class="detail-mask" @click.self="detailId = null"><section class="detail-panel"><header><div><small>{{ detail.code }}</small><h2>{{ detail.name }}</h2></div><button @click="detailId = null">×</button></header><div class="detail-summary"><span>{{ statusText(detail.status) }}</span><span>{{ formatText(detail.format) }}</span><span>{{ detail.participants.length }}/{{ detail.maxPlayers }} 人</span><span>{{ detail.ruleset }}</span></div><p>{{ detail.description || '赛事方尚未发布说明。' }}</p><h3>赛事工作人员</h3><div class="participants"><span>主办者 · {{ detail.organizer }}</span><span v-for="referee in detail.referees" :key="referee">裁判 · {{ referee }}</span></div><h3>参赛人员</h3><div class="participants"><span v-for="player in detail.participants" :key="player">{{ player }}</span></div><template v-if="detail.matches.length"><h3>当前轮次</h3><div class="matches"><div v-for="match in detail.matches" :key="match.table"><b>桌 {{ match.table }}</b><span>{{ match.playerA }} <i>VS</i> {{ match.playerB }}</span></div></div></template><footer><button @click="copyCode(detail)">复制赛事代码</button><button v-if="detail.organizer === nickname && detail.status === 'registration'" class="gold" :disabled="detail.participants.length < 2" @click="start(detail)">开始赛事</button><button v-if="detail.organizer === nickname && detail.status === 'running'" class="gold" @click="finish(detail)">结束赛事</button></footer></section></div>
    <button v-if="notice" class="tournament-toast" @click="notice = ''">{{ notice }}</button>
  </div>
</template>

<style scoped>
.tournament-page{width:min(1280px,calc(100% - 42px));min-height:100%;margin:auto;padding:34px 0 70px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.tournament-head{display:flex;align-items:flex-start;justify-content:space-between;padding-bottom:20px;border-bottom:1px solid rgba(210,178,96,.32)}.tournament-head small,.create-panel small,.registry article small,.detail-panel small{color:#d6b55e;font:900 9px monospace;letter-spacing:.18em}.tournament-head h1{margin:5px 0;font-size:31px}.tournament-head p,.create-panel header p{margin:0;color:#818b8b;font-size:12px}.gold{border-color:#d2ae54!important;background:#d2ae54!important;color:#0d0d0b!important;font-weight:900}.tournament-head>button{padding:12px 22px;border:1px solid}.prototype-note{display:flex;align-items:center;gap:16px;margin:14px 0;padding:10px 14px;border-left:3px solid #c9a64f;background:#18160f}.prototype-note b{font-size:11px}.prototype-note span{color:#908b7e;font-size:10px}.tournament-tabs{display:grid;grid-template-columns:repeat(3,1fr);border:1px solid #3b3930;background:#0c0e0e}.tournament-tabs button{padding:13px;border:0;background:transparent;color:#827e72;font-weight:900}.tournament-tabs button.active{background:#292315;color:#f1dfaa}.registry,.create-panel{margin-top:14px;border:1px solid #3d392d;background:#11120f}.registry>header{display:grid;grid-template-columns:1fr 220px;gap:10px;padding:12px;border-bottom:1px solid #3d392d}.registry input,.registry select,.create-panel input,.create-panel select,.create-panel textarea{box-sizing:border-box;width:100%;padding:11px;border:1px solid #4b483d;background:#090b0b;color:#f3eee0;outline:none}.registry input:focus,.registry select:focus,.create-panel input:focus,.create-panel select:focus,.create-panel textarea:focus{border-color:#d2ae54}.registry article{display:grid;grid-template-columns:minmax(260px,1.3fr) minmax(420px,1fr) auto;align-items:center;gap:22px;padding:17px;border-bottom:1px solid #302e26}.registry article h2{margin:4px 0;font-size:18px}.registry article p{margin:0;color:#777c78;font-size:10px}.registry dl{display:grid;grid-template-columns:repeat(4,1fr);gap:8px;margin:0}.registry dt{color:#6e726d;font-size:8px}.registry dd{margin:4px 0 0;font-size:10px;font-weight:900}.registry dd.registration{color:#e2bd5e}.registry dd.running{color:#65c49b}.registry dd.completed{color:#888}.event-actions{display:flex;gap:6px}.event-actions button,.detail-panel footer button{padding:9px 12px;border:1px solid #5a5545;background:#171812;color:#f4ecd8;font-weight:900}.empty{display:flex;min-height:240px;flex-direction:column;align-items:center;justify-content:center;color:#777d79}.empty span{margin-top:7px;font-size:10px}.create-panel{padding:24px}.create-panel header{padding-bottom:17px;border-bottom:1px solid #353229}.create-panel h2{margin:5px 0;font-size:24px}.form-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-top:18px}.form-grid label{color:#b1aa99;font-size:10px;font-weight:900}.form-grid input,.form-grid select,.form-grid textarea{margin-top:6px}.form-grid .wide{grid-column:1/-1}.form-grid textarea{resize:vertical}.create-action{display:block;margin:18px 0 0 auto;padding:12px 30px;border:1px solid}.detail-mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:20px;background:rgba(2,3,3,.82);backdrop-filter:blur(7px)}.detail-panel{width:min(780px,94vw);max-height:88vh;overflow:auto;border:1px solid #8b7540;background:#12130f;box-shadow:0 24px 80px #000}.detail-panel>header{display:flex;align-items:center;justify-content:space-between;padding:20px;border-bottom:1px solid #3d392d}.detail-panel h2{margin:4px 0 0}.detail-panel header button{width:34px;height:34px;border:1px solid #5c584c;background:#0b0d0d;color:#fff}.detail-summary{display:flex;flex-wrap:wrap;gap:7px;padding:14px 20px}.detail-summary span{padding:5px 8px;border:1px solid #454237;color:#d7c99e;font-size:9px}.detail-panel>p,.detail-panel>h3,.participants,.matches{margin-right:20px;margin-left:20px}.detail-panel>p{color:#9a9b92;font-size:11px;line-height:1.7}.detail-panel>h3{margin-top:20px;font-size:13px}.participants{display:flex;flex-wrap:wrap;gap:6px}.participants span{padding:7px 9px;background:#202019;font-size:10px}.matches{display:grid;gap:6px}.matches>div{display:flex;justify-content:space-between;padding:9px 11px;border:1px solid #36342c}.matches b{color:#d1ad54;font-size:10px}.matches span{font-size:10px}.matches i{margin:0 12px;color:#866f3d;font-style:normal}.detail-panel footer{display:flex;justify-content:flex-end;gap:8px;margin-top:22px;padding:16px 20px;border-top:1px solid #3d392d}.detail-panel footer button:disabled{opacity:.35}.tournament-toast{position:fixed;z-index:120;right:24px;bottom:24px;padding:11px 16px;border:1px solid #d1ad54;background:#231d0f;color:#f3d98d;font-weight:900}
@media(max-width:900px){.registry article{grid-template-columns:1fr}.registry dl{grid-template-columns:repeat(2,1fr)}.event-actions{justify-content:flex-end}}@media(max-width:650px){.tournament-page{width:auto;padding:20px 12px 50px}.tournament-head{gap:12px}.tournament-head h1{font-size:25px}.prototype-note{align-items:flex-start;flex-direction:column;gap:5px}.registry>header,.form-grid{grid-template-columns:1fr}.form-grid .wide{grid-column:auto}.registry dl{grid-template-columns:1fr 1fr}.detail-panel footer{flex-direction:column}.detail-panel footer button{width:100%}}
</style>
