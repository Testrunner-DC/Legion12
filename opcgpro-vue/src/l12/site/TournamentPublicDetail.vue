<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { publicTournamentApi, type PublicTournamentDetail } from '@/l12/platform'
import { tournamentDisasterModeText, tournamentFormatText, tournamentMatchStatusText, tournamentPhaseText, tournamentResultText, tournamentRoundStatusText, tournamentStatusText } from '@/l12/tournamentLabels'
import { tournamentTimeControlLabel } from '@/l12/tournamentTime'

const route = useRoute(); const router = useRouter()
const tournament = ref<PublicTournamentDetail | null>(null)
const tab = ref('overview'); const loading = ref(true); const notice = ref('')
const tabs = [['overview','概览'],['schedule','赛程'],['standings','排名'],['participants','参赛者'],['rules','规则']] as const
const standings = computed(() => tournament.value?.finalStandings.length ? tournament.value.finalStandings : [...(tournament.value?.rounds || [])].reverse().find(round => round.standings.length)?.standings || [])
const participantStatus = (value: string) => ({ active: '参赛中', registered: '已报名', checkedIn: '已签到', 'checked-in': '已签到', waitlisted: '候补', dropped: '已退赛', eliminated: '已淘汰', removed: '已退出' } as Record<string,string>)[value] || '已报名'
let disposed = false; let generation = 0
async function load() {
  const current = ++generation; loading.value = true; notice.value = ''
  // Revalidation clears even the old public payload so withdrawal cannot leave stale tables visible.
  tournament.value = null
  try {
    const value = await publicTournamentApi.getByCode(String(route.params.code))
    if (!disposed && generation === current) { tournament.value = value; document.title = `${value.name} · 赛事中心` }
  } catch (error) { if (!disposed && generation === current) notice.value = error instanceof Error ? error.message : '赛事暂未公开或不存在' }
  finally { if (!disposed && generation === current) loading.value = false }
}
function login() { void router.push({ name: 'me', query: { redirect: route.fullPath } }) }
async function copyLink() {
  try { await navigator.clipboard.writeText(location.href); notice.value = '赛事链接已复制' }
  catch { notice.value = '无法复制，请复制浏览器地址栏中的链接' }
}
const onResource = () => { void load() }
const onVisibility = () => { if (document.visibilityState === 'visible') void load() }
let refreshTimer: number | undefined
function refresh() { void load(); refreshTimer = window.setTimeout(refresh, 60_000) }
onMounted(() => { void load(); refreshTimer = window.setTimeout(refresh, 60_000); window.addEventListener('l12-resource-tournaments', onResource); document.addEventListener('visibilitychange', onVisibility) })
onBeforeUnmount(() => { disposed = true; ++generation; window.clearTimeout(refreshTimer); window.removeEventListener('l12-resource-tournaments', onResource); document.removeEventListener('visibilitychange', onVisibility) })
</script>

<template>
  <main class="public-detail detail-page">
    <button class="back" @click="router.push('/battle/tournaments')">← 返回赛事中心</button>
    <p v-if="loading" class="panel" role="status">正在加载赛事…</p>
    <section v-else-if="!tournament" class="panel"><p class="notice" role="alert">{{ notice }}</p><button class="primary" @click="login">登录后继续</button></section>
    <template v-else>
      <header class="page-head"><div><small>{{ tournament.code }} · {{ tournamentStatusText(tournament.status) }}</small><h1>{{ tournament.name }}</h1><p>{{ tournament.description || '暂无赛事简介' }}</p></div><div class="header-actions"><button @click="copyLink">复制链接</button><button class="primary" @click="login">登录参与赛事</button></div></header>
      <nav class="tabs" aria-label="赛事详情"><button v-for="item in tabs" :key="item[0]" :class="{ active: tab === item[0] }" :aria-current="tab === item[0] ? 'page' : undefined" @click="tab = item[0]">{{ item[1] }}</button></nav>
      <p v-if="notice" class="notice" role="status">{{ notice }}</p>
      <section v-if="tab === 'overview'" class="panel facts"><p><small>主办者</small><b>{{ tournament.organizerName }}</b></p><p><small>赛制</small><b>{{ tournamentFormatText(tournament.format) }}</b></p><p><small>人数</small><b>{{ tournament.counts.active }}/{{ tournament.maxPlayers }} · 候补 {{ tournament.counts.waitlisted }}</b></p><p><small>计划时间</small><b>{{ tournament.startAt ? new Date(tournament.startAt).toLocaleString('zh-CN') : '待定' }}</b></p><p><small>赛事阶段</small><b>{{ tournamentPhaseText(tournament.phase) }}</b></p><p><small>报名状态</small><b>{{ tournament.registrationOpen ? '报名开放' : '报名关闭' }}</b></p></section>
      <section v-else-if="tab === 'schedule'" class="rounds">
        <p v-if="!tournament.rounds.length" class="panel empty">赛程尚未公布</p>
        <section v-for="round in tournament.rounds" :key="round.number" class="panel round"><header><h2>第 {{ round.number }} 轮</h2><span>{{ round.stage === 'elimination' ? '淘汰赛' : '瑞士轮' }} · {{ tournamentRoundStatusText(round.status) }}</span></header><article v-for="match in round.matches" :key="match.table" class="match"><b>第 {{ match.table }} 桌</b><span>{{ match.playerAName || '参赛者' }} <i>vs</i> {{ match.playerBName || (match.result === 'bye' ? '轮空' : '参赛者') }}</span><span>{{ match.status === 'completed' ? tournamentResultText(match.result) : tournamentMatchStatusText(match.status) }}</span></article></section>
      </section>
      <section v-else-if="tab === 'standings'" class="panel"><h2>赛事排名</h2><p v-if="tournament.registrationVisibility === 'staff'" class="empty">参赛名单和排名仅工作人员可见</p><p v-else-if="!standings.length" class="empty">排名尚未公布</p><div v-else class="standings"><div class="thead"><span>名次</span><span>参赛者</span><span>胜-负-平</span><span>对手胜场和</span></div><article v-for="person in standings" :key="person.rank"><b data-label="名次">{{ person.rank }}</b><span data-label="参赛者">{{ person.name }}</span><span data-label="胜-负-平">{{ person.wins }}-{{ person.losses }}-{{ person.draws }}</span><span data-label="对手胜场和">{{ person.opponentScore }}</span></article></div></section>
      <section v-else-if="tab === 'participants'" class="panel"><h2>参赛者</h2><p v-if="tournament.registrationVisibility === 'staff'" class="empty">参赛名单仅工作人员可见</p><p v-else-if="!tournament.participants.length" class="empty">暂无参赛者</p><div v-else class="participants"><article v-for="(person,index) in tournament.participants" :key="index"><b>{{ person.name }}</b><span>{{ participantStatus(person.status) }}</span></article></div></section>
      <section v-else class="panel rules"><h2>赛事规则</h2><p><small>规则版本</small>{{ tournament.rules.ruleset }}</p><p><small>天灾</small>{{ tournamentDisasterModeText(tournament.rules.disasterMode) }}</p><p><small>对局计时</small>{{ tournamentTimeControlLabel(tournament.rules.timeControl) }}</p><p><small>签到窗口</small>{{ tournament.checkInMinutes }} 分钟</p><p><small>迟到宽限</small>{{ tournament.rules.lateGraceMinutes }} 分钟</p><p v-if="tournament.rules.banList"><small>禁限说明</small>{{ tournament.rules.banList }}</p><p v-for="(restriction,index) in tournament.rules.cardRestrictions" :key="index"><small>{{ restriction.cardId }}</small>最多 {{ restriction.maxCopies }} 张</p></section>
    </template>
  </main>
</template>

<style scoped>
.public-detail{box-sizing:border-box;max-width:1320px;margin:auto;padding:30px clamp(18px,3vw,42px) 64px;display:grid;gap:18px;color:var(--l12-ui-text);font-family:'Microsoft YaHei','微软雅黑',sans-serif}.public-detail *{box-sizing:border-box;min-width:0}.public-detail button{min-height:40px;padding:10px 16px;border:1px solid var(--l12-ui-line-strong);border-radius:var(--l12-ui-radius-sm);background:var(--l12-ui-control);color:var(--l12-ui-text-soft);font:800 14px 'Microsoft YaHei',sans-serif;cursor:pointer}.back{justify-self:start}.page-head{display:flex;align-items:start;justify-content:space-between;gap:20px}.page-head>div:first-child{flex:1}.page-head h1{margin:8px 0;font-size:clamp(26px,3vw,38px);overflow-wrap:anywhere}.page-head small{color:var(--l12-ui-info)}.page-head p{margin:0;color:var(--l12-ui-text-muted);white-space:pre-wrap;overflow-wrap:anywhere}.header-actions{display:flex;flex-wrap:wrap;gap:8px}.tabs{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:5px}.public-detail button.active{background:#302713;border-color:var(--l12-ui-accent-line);color:var(--l12-ui-accent)}.panel{padding:18px;border:1px solid var(--l12-ui-line);border-radius:var(--l12-ui-radius-md);background:var(--l12-ui-panel)}.panel h2{margin:0 0 16px;font-size:19px}.facts{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:18px}.facts p{display:grid;gap:5px;margin:0;overflow-wrap:anywhere}.facts small,.rules small{color:var(--l12-ui-text-muted);font-size:12px}.rounds{display:grid;gap:14px}.round>header{display:flex;align-items:start;justify-content:space-between;gap:12px}.round>header>span{color:var(--l12-ui-text-muted);font-size:13px}.match{display:grid;grid-template-columns:100px minmax(0,1fr) 130px;gap:12px;padding:14px 0;border-top:1px solid var(--l12-ui-line);overflow-wrap:anywhere}.match i{font-style:normal;color:var(--l12-ui-text-muted);padding:0 8px}.standings .thead,.standings article{display:grid;grid-template-columns:70px minmax(0,1fr) 110px 110px;gap:10px;padding:14px 0;border-top:1px solid var(--l12-ui-line)}.thead{color:var(--l12-ui-text-muted);font-size:12px}.standings article{overflow-wrap:anywhere}.participants{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.participants article{display:flex;justify-content:space-between;gap:10px;padding:14px;border:1px solid var(--l12-ui-line);overflow-wrap:anywhere}.participants span{color:var(--l12-ui-text-muted);font-size:12px}.rules p{display:grid;grid-template-columns:100px minmax(0,1fr);gap:12px;overflow-wrap:anywhere;white-space:pre-wrap}.empty{margin:0;padding:20px 0;color:var(--l12-ui-text-muted);text-align:center}.notice{margin:0;color:var(--l12-ui-accent);overflow-wrap:anywhere}
@media(max-width:700px){.public-detail{padding:18px 10px calc(42px + env(safe-area-inset-bottom));gap:12px}.public-detail button{min-height:44px}.page-head{flex-direction:column}.header-actions{display:grid;width:100%;grid-template-columns:1fr 1fr}.tabs{grid-template-columns:repeat(2,minmax(0,1fr))}.tabs>button:last-child{grid-column:1/-1}.panel{padding:14px}.facts,.participants{grid-template-columns:1fr 1fr}.round>header{flex-direction:column;gap:0}.match{grid-template-columns:1fr}.standings .thead{display:none}.standings article{grid-template-columns:1fr 1fr;gap:12px}.standings article>*::before{content:attr(data-label);display:block;margin-bottom:4px;font-size:11px;color:var(--l12-ui-text-muted)}.rules p{grid-template-columns:1fr;gap:4px}}
@media(max-width:390px){.facts,.participants{grid-template-columns:1fr}.header-actions{grid-template-columns:1fr}}
</style>
