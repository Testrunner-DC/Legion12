<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { l12State } from '@/l12/net'

type TournamentStatus = 'registration' | 'running' | 'completed'
type RoundStatus = 'pending' | 'checkin' | 'running' | 'completed'
type DeckVisibility = 'always' | 'after' | 'private'
type DisasterMode = 'all' | 'random' | 'season' | 'none'

interface Friend { id: string; name: string }
interface Participant { name: string; deckName: string; deckCode: string; checkedIn: boolean; dropped: boolean }
interface TournamentMatch {
  id: string; table: number; playerA: string; playerB: string; roomCode: string
  readyA: boolean; readyB: boolean; status: 'waiting' | 'running' | 'completed'
  result: string; ruling: string; timeExtension: number; startedAt?: string; deadline?: string
}
interface TournamentRound { number: number; status: RoundStatus; paused: boolean; startedAt?: string; matches: TournamentMatch[] }
interface Tournament {
  id: string; code: string; name: string; organizer: string; referees: string[]; status: TournamentStatus
  format: 'single' | 'swiss' | 'league'; visibility: 'public' | 'code'; maxPlayers: number; startAt: string
  ruleset: string; description: string; deckVisibility: DeckVisibility; disasterMode: DisasterMode
  banList: string; roundMinutes: number; checkInMinutes: number; participants: Participant[]; rounds: TournamentRound[]
  createdAt: string; updatedAt: string; completedAt?: string
}

const storageKey = 'l12-tournaments-v2'
const tab = ref<'current' | 'completed' | 'mine' | 'create'>('current')
const search = ref('')
const detailId = ref<string | null>(null)
const notice = ref('')
const tournaments = ref<Tournament[]>(load())
const friends = ref<Friend[]>(loadFriends())
const form = reactive({
  name: '', format: 'swiss' as Tournament['format'], visibility: 'public' as Tournament['visibility'], maxPlayers: 16,
  startAt: '', ruleset: '现行规则', description: '', deckVisibility: 'after' as DeckVisibility,
  disasterMode: 'season' as DisasterMode, banList: '', roundMinutes: 50, checkInMinutes: 5, referees: [] as string[],
})

function loadFriends(): Friend[] { try { return JSON.parse(localStorage.getItem('l12-friends-v1') || '[]') } catch { return [] } }
function normalize(item: Partial<Tournament>): Tournament {
  const people = Array.isArray(item.participants) ? item.participants : []
  return {
    id: item.id || crypto.randomUUID(), code: item.code || makeCode(), name: item.name || '未命名赛事',
    organizer: item.organizer || '本机玩家', referees: item.referees ?? [], status: item.status ?? 'registration',
    format: item.format ?? 'swiss', visibility: item.visibility ?? 'public', maxPlayers: item.maxPlayers ?? 16,
    startAt: item.startAt ?? '', ruleset: item.ruleset ?? '现行规则', description: item.description ?? '',
    deckVisibility: item.deckVisibility ?? 'after', disasterMode: item.disasterMode ?? 'season', banList: item.banList ?? '',
    roundMinutes: item.roundMinutes ?? 50, checkInMinutes: item.checkInMinutes ?? 5,
    participants: people.map(person => typeof person === 'string'
      ? { name: person, deckName: '', deckCode: '', checkedIn: false, dropped: false }
      : { ...person, deckName: person.deckName ?? '', deckCode: person.deckCode ?? '', checkedIn: person.checkedIn ?? false, dropped: person.dropped ?? false }),
    rounds: item.rounds ?? [], createdAt: item.createdAt ?? new Date().toISOString(), updatedAt: item.updatedAt ?? new Date().toISOString(),
    completedAt: item.completedAt,
  }
}
function load(): Tournament[] {
  try {
    const current = JSON.parse(localStorage.getItem(storageKey) || '[]') as Tournament[]
    if (current.length) return current.map(normalize)
    const legacy = JSON.parse(localStorage.getItem('l12-tournaments-v1') || '[]') as Tournament[]
    return legacy.map(normalize)
  } catch { return [] }
}
function persist() { localStorage.setItem(storageKey, JSON.stringify(tournaments.value)) }
function makeCode(length = 6) {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'
  return Array.from(crypto.getRandomValues(new Uint8Array(length)), value => alphabet[value % alphabet.length]).join('')
}
function touch(item: Tournament) { item.updatedAt = new Date().toISOString(); persist() }

const nickname = computed(() => l12State.nickname.trim() || '本机玩家')
const detail = computed(() => tournaments.value.find(item => item.id === detailId.value) ?? null)
const currentRound = computed(() => detail.value?.rounds.at(-1) ?? null)
const visibleTournaments = computed(() => tournaments.value.filter(item => {
  if (tab.value === 'current' && item.status === 'completed') return false
  if (tab.value === 'completed' && item.status !== 'completed') return false
  if (tab.value === 'mine' && item.organizer !== nickname.value && !item.participants.some(p => p.name === nickname.value)) return false
  const q = search.value.trim().toLowerCase()
  return !q || [item.name, item.code, item.organizer].some(value => value.toLowerCase().includes(q))
}))
const isStaff = (item: Tournament) => item.organizer === nickname.value || item.referees.includes(nickname.value)
const isOrganizer = (item: Tournament) => item.organizer === nickname.value
const statusText = (status: TournamentStatus) => ({ registration: '报名中', running: '进行中', completed: '已结束' }[status])
const formatText = (format: Tournament['format']) => ({ single: '单败淘汰', swiss: '瑞士轮', league: '循环赛' }[format])
const disasterText = (mode: DisasterMode) => ({ all: '全部天灾', random: '随机天灾', season: '赛季天灾', none: '不使用天灾' }[mode])
const deckVisibilityText = (mode: DeckVisibility) => ({ always: '全程公开牌库', after: '赛后公开牌库', private: '不公开牌库' }[mode])

function createTournament() {
  if (!form.name.trim()) { notice.value = '请输入赛事名称'; return }
  const now = new Date().toISOString()
  const item: Tournament = {
    id: crypto.randomUUID(), code: makeCode(), name: form.name.trim(), organizer: nickname.value,
    referees: [...form.referees], status: 'registration', format: form.format, visibility: form.visibility,
    maxPlayers: Math.max(2, Math.min(256, Number(form.maxPlayers) || 16)), startAt: form.startAt,
    ruleset: form.ruleset.trim() || '现行规则', description: form.description.trim(), deckVisibility: form.deckVisibility,
    disasterMode: form.disasterMode, banList: form.banList.trim(), roundMinutes: Math.max(5, form.roundMinutes || 50),
    checkInMinutes: Math.max(1, form.checkInMinutes || 5), participants: [participant(nickname.value)], rounds: [],
    createdAt: now, updatedAt: now,
  }
  tournaments.value.unshift(item); persist(); detailId.value = item.id; tab.value = 'mine'; notice.value = `赛事 ${item.code} 已建立`
}
function participant(name: string): Participant { return { name, deckName: '', deckCode: '', checkedIn: false, dropped: false } }
function join(item: Tournament) {
  if (item.status !== 'registration' || item.participants.length >= item.maxPlayers || item.participants.some(p => p.name === nickname.value)) return
  item.participants.push(participant(nickname.value)); touch(item); notice.value = `已报名「${item.name}」`
}
function updateDeck(item: Tournament, person: Participant) {
  if (person.name !== nickname.value && !isStaff(item)) return
  person.deckName = person.deckName.trim(); person.deckCode = person.deckCode.trim(); touch(item)
}
function toggleReferee(name: string) {
  const index = form.referees.indexOf(name)
  if (index >= 0) form.referees.splice(index, 1); else form.referees.push(name)
}
function makePairings(item: Tournament, roundNumber: number): TournamentMatch[] {
  const active = item.participants.filter(p => !p.dropped).map(p => p.name)
  if (roundNumber > 1) active.push(active.shift() || '')
  return Array.from({ length: Math.ceil(active.length / 2) }, (_, index) => ({
    id: crypto.randomUUID(), table: index + 1, playerA: active[index * 2], playerB: active[index * 2 + 1] || '轮空',
    roomCode: makeCode(8), readyA: false, readyB: active[index * 2 + 1] ? false : true,
    status: active[index * 2 + 1] ? 'waiting' : 'completed', result: active[index * 2 + 1] ? '' : `${active[index * 2]} 轮空胜`, ruling: '', timeExtension: 0,
  }))
}
function startTournament(item: Tournament) {
  if (!isOrganizer(item) || item.status !== 'registration' || item.participants.length < 2) return
  item.status = 'running'
  item.participants.forEach(p => { p.checkedIn = false })
  item.rounds.push({ number: 1, status: 'checkin', paused: false, matches: makePairings(item, 1) })
  touch(item); notice.value = '赛事已开启，第 1 轮进入签到与房间准备'
}
function toggleReady(item: Tournament, match: TournamentMatch, side: 'A' | 'B') {
  const player = side === 'A' ? match.playerA : match.playerB
  if (player !== nickname.value && !isStaff(item)) return
  if (side === 'A') match.readyA = !match.readyA; else match.readyB = !match.readyB
  const participantItem = item.participants.find(p => p.name === player)
  if (participantItem) participantItem.checkedIn = side === 'A' ? match.readyA : match.readyB
  touch(item)
}
function startRound(item: Tournament, round: TournamentRound) {
  if (!isStaff(item) || round.status !== 'checkin') return
  const now = Date.now(); round.status = 'running'; round.startedAt = new Date(now).toISOString()
  round.matches.forEach(match => {
    if (match.status !== 'completed') {
      match.status = 'running'; match.startedAt = round.startedAt
      match.deadline = new Date(now + (item.roundMinutes + match.timeExtension) * 60000).toISOString()
      if (!match.readyA || !match.readyB) match.ruling = `未进入房间者须在 ${item.checkInMinutes} 分钟内进入，否则判负`
    }
  })
  touch(item)
}
function noShowLoss(item: Tournament, match: TournamentMatch) {
  if (!isStaff(item) || match.status === 'completed') return
  if (!match.readyA && match.readyB) match.result = `${match.playerB} 胜（对手超时未进入）`
  else if (match.readyA && !match.readyB) match.result = `${match.playerA} 胜（对手超时未进入）`
  else { notice.value = '仅有一方未准备时才可执行超时判负'; return }
  match.status = 'completed'; afterMatch(item)
}
function recordResult(item: Tournament, match: TournamentMatch, result: string) {
  if (!isStaff(item)) return
  match.result = result; match.status = 'completed'; afterMatch(item)
}
function afterMatch(item: Tournament) {
  const round = item.rounds.find(r => r.matches.some(m => m.id === currentMatchId.value)) ?? item.rounds.at(-1)
  if (round && round.matches.every(m => m.status === 'completed')) round.status = 'completed'
  touch(item)
}
const currentMatchId = ref('')
function completeMatch(item: Tournament, match: TournamentMatch, result: string) { currentMatchId.value = match.id; recordResult(item, match, result) }
function nextRound(item: Tournament) {
  const previous = item.rounds.at(-1)
  if (!isStaff(item) || !previous || previous.status !== 'completed') return
  const number = previous.number + 1
  item.participants.forEach(p => { p.checkedIn = false })
  item.rounds.push({ number, status: 'checkin', paused: false, matches: makePairings(item, number) }); touch(item)
}
function pauseRound(item: Tournament, round: TournamentRound) { if (isStaff(item)) { round.paused = !round.paused; touch(item) } }
function addTime(item: Tournament, match: TournamentMatch, minutes: number) {
  if (!isStaff(item)) return
  match.timeExtension += minutes
  if (match.deadline) match.deadline = new Date(new Date(match.deadline).getTime() + minutes * 60000).toISOString()
  touch(item)
}
function finishTournament(item: Tournament) {
  if (!isOrganizer(item) || item.status !== 'running') return
  item.status = 'completed'; item.completedAt = new Date().toISOString(); touch(item)
}
function cancelTournament(item: Tournament) {
  if (!isOrganizer(item) || item.status !== 'registration') return
  tournaments.value = tournaments.value.filter(candidate => candidate.id !== item.id)
  detailId.value = null; persist(); notice.value = '未开始的赛事已取消'
}
function canViewDecks(item: Tournament) { return isStaff(item) || item.deckVisibility === 'always' || (item.deckVisibility === 'after' && item.status === 'completed') }
async function copyCode(item: Tournament) { await navigator.clipboard.writeText(item.code); notice.value = `赛事代码 ${item.code} 已复制` }
async function copyRoom(match: TournamentMatch) { await navigator.clipboard.writeText(match.roomCode); notice.value = `房间码 ${match.roomCode} 已复制` }
</script>

<template>
  <div class="tournament-page">
    <header class="page-head"><div><small>TOURNAMENT OPERATIONS</small><h1>赛事中心</h1><p>从报名、签到、配对、裁判执裁到结束赛事档案的完整组织入口。</p></div><button class="gold" @click="tab = 'create'">举办赛事</button></header>
    <nav class="tabs"><button :class="{active:tab==='current'}" @click="tab='current'">当前赛事</button><button :class="{active:tab==='completed'}" @click="tab='completed'">结束赛事</button><button :class="{active:tab==='mine'}" @click="tab='mine'">我的赛程</button><button :class="{active:tab==='create'}" @click="tab='create'">创建向导</button></nav>

    <section v-if="tab !== 'create'" class="registry">
      <input v-model="search" placeholder="搜索赛事名称、主办者或赛事代码"/>
      <article v-for="item in visibleTournaments" :key="item.id">
        <div><small>{{ item.code }}</small><h2>{{ item.name }}</h2><p>{{ item.description || '赛事方尚未发布说明。' }}</p></div>
        <dl><div><dt>状态</dt><dd>{{ statusText(item.status) }}</dd></div><div><dt>赛制</dt><dd>{{ formatText(item.format) }}</dd></div><div><dt>人数</dt><dd>{{ item.participants.length }}/{{ item.maxPlayers }}</dd></div><div><dt>主办者</dt><dd>{{ item.organizer }}</dd></div></dl>
        <div class="actions"><button @click="detailId=item.id">赛事详情</button><button v-if="item.status==='registration'&&!item.participants.some(p=>p.name===nickname)" class="gold" @click="join(item)">报名参赛</button><button v-else @click="copyCode(item)">复制代码</button></div>
      </article>
      <div v-if="!visibleTournaments.length" class="empty">暂无符合条件的赛事</div>
    </section>

    <section v-else class="create-panel">
      <header><small>ORGANIZER WORKFLOW</small><h2>创建赛事</h2><p>赛事建立后仍由主办者决定真正开启时间；每轮房间全部准备后再开始对局。</p></header>
      <div class="form-grid">
        <label class="wide">赛事名称<input v-model="form.name" maxlength="40"/></label>
        <label>赛制<select v-model="form.format"><option value="swiss">瑞士轮</option><option value="single">单败淘汰</option><option value="league">循环赛</option></select></label>
        <label>人数上限<input v-model.number="form.maxPlayers" type="number" min="2" max="256"/></label>
        <label>加入方式<select v-model="form.visibility"><option value="public">公开发现或赛事代码</option><option value="code">仅赛事代码</option></select></label>
        <label>计划时间<input v-model="form.startAt" type="datetime-local"/></label>
        <label>每轮分钟<input v-model.number="form.roundMinutes" type="number" min="5"/></label>
        <label>未进房间判负等待<input v-model.number="form.checkInMinutes" type="number" min="1"/></label>
        <label>天灾模式<select v-model="form.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="season">赛季天灾</option><option value="none">不使用天灾</option></select></label>
        <label>牌库公开<select v-model="form.deckVisibility"><option value="always">全程公开牌库</option><option value="after">赛后公开牌库</option><option value="private">不公开牌库</option></select></label>
        <label class="wide">规则版本<input v-model="form.ruleset"/></label>
        <label class="wide">禁限卡规则<textarea v-model="form.banList" rows="2" placeholder="留空表示按当前官方禁限卡规则"/></label>
        <fieldset class="wide"><legend>从好友中选择裁判</legend><button v-for="friend in friends" :key="friend.id" type="button" :class="{selected:form.referees.includes(friend.name)}" @click="toggleReferee(friend.name)">{{ friend.name }}</button><span v-if="!friends.length">好友列表为空，可稍后在赛事详情中补充裁判。</span></fieldset>
        <label class="wide">赛事说明<textarea v-model="form.description" rows="4"/></label>
      </div>
      <button class="gold create-action" @click="createTournament">建立赛事</button>
    </section>

    <div v-if="detail" class="mask" @click.self="detailId=null"><section class="detail">
      <header><div><small>{{ detail.code }}</small><h2>{{ detail.name }}</h2></div><button @click="detailId=null">×</button></header>
      <div class="summary"><span>{{ statusText(detail.status) }}</span><span>{{ formatText(detail.format) }}</span><span>{{ disasterText(detail.disasterMode) }}</span><span>{{ deckVisibilityText(detail.deckVisibility) }}</span><span>每轮 {{ detail.roundMinutes }} 分钟</span></div>
      <p>{{ detail.description || '赛事方尚未发布说明。' }}</p>
      <section class="rules"><b>规则：{{ detail.ruleset }}</b><span>禁限卡：{{ detail.banList || '当前官方规则' }}</span><span>计划开始：{{ detail.startAt ? new Date(detail.startAt).toLocaleString() : '由主办者通知' }}</span></section>
      <h3>工作人员</h3><div class="chips"><span>主办者 · {{ detail.organizer }}</span><span v-for="name in detail.referees" :key="name">裁判 · {{ name }}</span></div>
      <h3>参赛人员与牌库</h3>
      <div class="participants"><div v-for="person in detail.participants" :key="person.name"><b>{{ person.name }}</b><span>{{ person.checkedIn ? '已准备' : '未准备' }}</span><template v-if="canViewDecks(detail)||person.name===nickname"><input v-model="person.deckName" placeholder="牌库名称" @change="updateDeck(detail,person)"/><input v-model="person.deckCode" placeholder="牌库码" @change="updateDeck(detail,person)"/></template><em v-else>牌库未公开</em></div></div>
      <template v-if="currentRound"><h3>第 {{ currentRound.number }} 轮 · {{ currentRound.status==='checkin'?'签到/准备':currentRound.status==='running'?'对局中':'已完成' }}</h3>
        <div class="round-controls" v-if="isStaff(detail)"><button v-if="currentRound.status==='checkin'" class="gold" @click="startRound(detail,currentRound)">主办/裁判开始本轮</button><button v-if="currentRound.status==='running'" @click="pauseRound(detail,currentRound)">{{ currentRound.paused?'恢复计时':'暂停计时' }}</button><button v-if="currentRound.status==='completed'" @click="nextRound(detail)">自动建立下一轮</button></div>
        <div class="matches"><article v-for="match in currentRound.matches" :key="match.id"><header><b>桌 {{ match.table }}</b><span>房间 {{ match.roomCode }}</span><button @click="copyRoom(match)">复制</button></header><div><button :class="{ready:match.readyA}" @click="toggleReady(detail,match,'A')">{{ match.playerA }} · {{ match.readyA?'已准备':'准备' }}</button><i>VS</i><button :class="{ready:match.readyB}" @click="toggleReady(detail,match,'B')">{{ match.playerB }} · {{ match.readyB?'已准备':'准备' }}</button></div><p v-if="match.deadline">本桌截止 {{ new Date(match.deadline).toLocaleTimeString() }} · 补时 {{ match.timeExtension }} 分钟</p><p v-if="match.result">{{ match.result }}</p><textarea v-if="isStaff(detail)" v-model="match.ruling" placeholder="裁判判罚与备注" @change="touch(detail)"/><footer v-if="isStaff(detail)&&match.status!=='completed'"><button @click="addTime(detail,match,5)">补时 5 分钟</button><button @click="noShowLoss(detail,match)">未入场判负</button><button @click="completeMatch(detail,match,`${match.playerA} 胜`)">A 胜</button><button @click="completeMatch(detail,match,`${match.playerB} 胜`)">B 胜</button><button @click="completeMatch(detail,match,'平局')">平局</button></footer></article></div>
      </template>
      <footer class="detail-actions"><button @click="copyCode(detail)">复制赛事代码</button><button v-if="isOrganizer(detail)&&detail.status==='registration'" class="danger" @click="cancelTournament(detail)">取消赛事</button><button v-if="isOrganizer(detail)&&detail.status==='registration'" class="gold" :disabled="detail.participants.length<2" @click="startTournament(detail)">正式开启赛事</button><button v-if="isOrganizer(detail)&&detail.status==='running'" class="danger" @click="finishTournament(detail)">结束赛事并归档</button></footer>
    </section></div>
    <button v-if="notice" class="toast" @click="notice=''">{{ notice }}</button>
  </div>
</template>

<style scoped>
.tournament-page{width:min(1320px,calc(100% - 40px));min-height:100%;margin:auto;padding:32px 0 70px;font-family:'Microsoft YaHei',sans-serif}.page-head{display:flex;justify-content:space-between;gap:20px;padding-bottom:20px;border-bottom:1px solid #54482b}.page-head small,.create-panel small,.registry small,.detail small{color:#d6b55e;font:900 9px monospace;letter-spacing:.16em}.page-head h1{margin:5px 0;font-size:31px}.page-head p,.create-panel p{margin:0;color:#85877f;font-size:11px}.gold{border-color:#d0ad54!important;background:#d0ad54!important;color:#0c0c0a!important}.page-head button,.create-action{padding:12px 22px}.tabs{display:grid;grid-template-columns:repeat(4,1fr);margin-top:14px;border:1px solid #3d392d}.tabs button{padding:13px;border:0;background:#0e100f;color:#817c70;font-weight:900}.tabs button.active{background:#292315;color:#f2dda2}.registry,.create-panel{margin-top:14px;border:1px solid #3d392d;background:#11120f}.registry>input{box-sizing:border-box;width:calc(100% - 24px);margin:12px;padding:12px;border:1px solid #4b483d;background:#090b0b;color:#fff}.registry>article{display:grid;grid-template-columns:1.3fr 1fr auto;align-items:center;gap:20px;padding:17px;border-top:1px solid #302e26}.registry h2{margin:4px 0}.registry p{margin:0;color:#777;font-size:10px}.registry dl{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.registry dt{color:#777;font-size:8px}.registry dd{margin:4px 0;font-size:10px;font-weight:900}.actions{display:flex;gap:6px}.actions button,.detail button,.round-controls button{padding:9px 11px;border:1px solid #585342;background:#181913;color:#f1ead8;font-weight:900}.empty{display:grid;min-height:250px;place-items:center;color:#777}.create-panel{padding:24px}.create-panel>header{padding-bottom:16px;border-bottom:1px solid #353229}.form-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:13px;margin-top:18px}.form-grid label{font-size:10px;font-weight:900;color:#b2aa98}.form-grid input,.form-grid select,.form-grid textarea,.participants input,.matches textarea{box-sizing:border-box;width:100%;margin-top:6px;padding:10px;border:1px solid #4b483d;background:#080a0a;color:#fff}.wide{grid-column:1/-1}.form-grid fieldset{display:flex;flex-wrap:wrap;gap:7px;border:1px solid #4b483d}.form-grid fieldset button{padding:8px;border:1px solid #514b3b;background:#12130f;color:#aaa}.form-grid fieldset button.selected{border-color:#d1af55;color:#efd888}.form-grid fieldset span{color:#777;font-size:10px}.create-action{display:block;margin:18px 0 0 auto;border:1px solid;font-weight:900}.mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:18px;background:rgba(1,2,2,.84);backdrop-filter:blur(7px)}.detail{width:min(980px,96vw);max-height:92vh;overflow:auto;border:1px solid #8a743e;background:#11120f;box-shadow:0 20px 80px #000}.detail>header{display:flex;align-items:center;justify-content:space-between;padding:18px;border-bottom:1px solid #3d392d}.detail h2{margin:4px 0}.detail>header button{font-size:20px}.summary,.chips{display:flex;flex-wrap:wrap;gap:7px;padding:14px 18px}.summary span,.chips span{padding:6px 9px;border:1px solid #494436;color:#ddca96;font-size:9px}.detail>p,.detail>h3,.rules,.participants,.round-controls,.matches{margin-right:18px;margin-left:18px}.detail>p{color:#999;font-size:11px}.detail>h3{margin-top:22px}.rules{display:flex;flex-wrap:wrap;gap:10px;padding:11px;background:#1a1912;font-size:10px}.rules span{color:#aaa}.participants{display:grid;gap:6px}.participants>div{display:grid;grid-template-columns:160px 75px 1fr 1fr;align-items:center;gap:8px;padding:8px;border:1px solid #343229}.participants span{color:#68c9a3;font-size:9px}.participants input{margin:0}.participants em{grid-column:3/5;color:#777;font-size:9px}.round-controls{display:flex;gap:7px;margin-bottom:10px}.matches{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.matches>article{padding:11px;border:1px solid #3a372e;background:#0c0e0d}.matches article>header{display:flex;align-items:center;gap:8px}.matches article>header span{margin-left:auto;color:#d5b55d;font-size:9px}.matches article>div{display:grid;grid-template-columns:1fr auto 1fr;align-items:center;gap:8px;margin-top:10px}.matches article>div button{min-width:0}.matches button.ready{border-color:#4fbc91;background:#123325;color:#8de0bd}.matches i{color:#8d7540;font-style:normal}.matches p{color:#b8ae91;font-size:9px}.matches textarea{min-height:54px;resize:vertical}.matches footer{display:flex;flex-wrap:wrap;gap:5px;margin-top:8px}.detail-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:22px;padding:15px 18px;border-top:1px solid #3d392d}.detail-actions .danger{border-color:#8e343d;background:#321319;color:#f3b3ba}.toast{position:fixed;z-index:130;right:22px;bottom:22px;padding:11px 16px;border:1px solid #d1ad54;background:#251e0e;color:#f2d98b;font-weight:900}
@media(max-width:900px){.registry>article{grid-template-columns:1fr}.matches{grid-template-columns:1fr}.participants>div{grid-template-columns:1fr 1fr}.participants input,.participants em{grid-column:1/-1}}@media(max-width:650px){.tournament-page{width:auto;padding:20px 12px 50px}.page-head{align-items:flex-start}.tabs{grid-template-columns:1fr 1fr}.form-grid{grid-template-columns:1fr}.wide{grid-column:auto}.registry dl{grid-template-columns:1fr 1fr}.detail-actions{flex-direction:column}.detail-actions button{width:100%}}
</style>
