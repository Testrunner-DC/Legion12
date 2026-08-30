<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { loadDeckCatalog, syncSavedDecksFromAccount, type DeckCard, type SavedL12Deck } from '@/l12/decks'
import { connect, enterTournamentMatch, l12State, spectateTournamentMatch } from '@/l12/net'
import ConstructionRuleEditor from './ConstructionRuleEditor.vue'
import DisasterPoolPicker from './DisasterPoolPicker.vue'
import {
  friendApi, getEffectiveOperationsPolicy, hasPermission, platformState, tournamentApi,
  type LegacyTournamentInput, type PlatformFriend,
  type OperationsCardRestriction,
  type Tournament, type TournamentCreateInput, type TournamentDeckVisibility, type TournamentDisasterMode,
  type TournamentLegacyImport, type TournamentMatch, type TournamentParticipant, type TournamentRound,
  type TournamentStatus,
} from '@/l12/platform'

const legacyStorageKeys = ['l12-tournaments-v2', 'l12-tournaments-v1'] as const
const route = useRoute()
const router = useRouter()
const tab = ref<'current' | 'completed' | 'mine' | 'create'>('current')
const search = ref('')
const detailId = ref<string | null>(null)
const notice = ref('')
const busy = ref(false)
const loading = ref(false)
const platformVersion = ref(0)
const tournaments = ref<Tournament[]>([])
const friends = ref<PlatformFriend[]>([])
const catalog = ref<DeckCard[]>([])
const savedDecks = ref<SavedL12Deck[]>([])
const legacyCandidates = ref<LegacyTournamentInput[]>([])
const legacyPreview = ref<TournamentLegacyImport | null>(null)
const deckDrafts = reactive<Record<string, { name: string; code: string }>>({})
const rulingReasons = reactive<Record<string, string>>({})
const staffDraft = ref<string[]>([])
const staffReason = ref('')
const roundReason = ref('')
const form = reactive({
  name: '', format: 'swiss' as Tournament['format'], visibility: 'public' as Tournament['visibility'], maxPlayers: 16,
  startAt: '', ruleset: '现行规则', description: '', deckVisibility: 'after' as TournamentDeckVisibility,
  disasterMode: 'season' as TournamentDisasterMode, banList: '', roundMinutes: 50, checkInMinutes: 5,
  swissRounds: 3, cutSize: 8, registrationVisibility: 'public' as 'public' | 'staff', lateGraceMinutes: 5,
  disasterCardIds: [] as string[], cardRestrictions: [] as OperationsCardRestriction[],
  referees: [] as string[],
})

const accountId = computed(() => platformState.account?.id ?? '')
const detail = computed(() => tournaments.value.find(item => item.id === detailId.value) ?? null)
const currentRound = computed(() => detail.value?.rounds.at(-1) ?? null)
const canImportLegacy = computed(() => hasPermission('tournaments.import-legacy'))
const visibleTournaments = computed(() => tournaments.value.filter(item => {
  if (tab.value === 'current' && item.status === 'completed') return false
  if (tab.value === 'completed' && item.status !== 'completed') return false
  if (tab.value === 'mine' && !isStaff(item) && !item.participants.some(person => person.accountId === accountId.value)) return false
  const query = search.value.trim().toLowerCase()
  return !query || [item.name, item.code, item.organizerName].some(value => value.toLowerCase().includes(query))
}))

const isStaff = (item: Tournament) => item.organizerAccountId === accountId.value
  || item.referees.some(person => person.accountId === accountId.value)
const isOrganizer = (item: Tournament) => item.organizerAccountId === accountId.value
const isParticipant = (item: Tournament) => item.participants.some(person => person.accountId === accountId.value && !person.dropped)
const statusText = (status: TournamentStatus) => ({ registration: '报名中', running: '进行中', completed: '已结束' }[status])
const formatText = (format: Tournament['format']) => ({ single: '单败淘汰', swiss: '纯瑞士轮', 'swiss-cut': '瑞士后 Cut 淘汰', league: '旧版循环赛' }[format])
const disasterText = (mode: TournamentDisasterMode) => ({ all: '全部天灾', random: '随机天灾', season: '赛季天灾', none: '不使用天灾' }[mode])
const deckVisibilityText = (mode: TournamentDeckVisibility) => ({ always: '全程公开牌库', after: '赛后公开牌库', private: '不公开牌库' }[mode])
const resultText = (result?: string) => ({
  'player-a': 'A 方获胜', 'player-b': 'B 方获胜', draw: '平局', 'no-show-a': 'A 方未入场判负',
  'no-show-b': 'B 方未入场判负', bye: '轮空胜',
}[result ?? ''] ?? result ?? '')
const standingSnapshots = computed(() => detail.value?.rounds.filter(round => round.standings.length) ?? [])
const canCreateNextRound = (item: Tournament, round: TournamentRound) => {
  if (round.stage === 'elimination') return round.matches.length > 1
  if (item.format === 'swiss-cut') return round.number <= item.swissRounds
  if (item.format === 'swiss') return round.number < item.swissRounds
  return true
}

function errorText(error: unknown) { return error instanceof Error ? error.message : '赛事请求失败，请稍后重试' }
function hydrateDrafts(items: Tournament[]) {
  for (const item of items) {
    const own = item.participants.find(person => person.accountId === accountId.value)
    if (own) deckDrafts[item.id] = { name: own.deck?.name ?? '', code: '' }
    else if (!deckDrafts[item.id] && savedDecks.value.length)
      deckDrafts[item.id] = { name: savedDecks.value[0].name, code: '' }
  }
}
async function refreshTournaments() {
  if (!platformState.account) return
  loading.value = true
  try {
    const result = await tournamentApi.list()
    platformVersion.value = result.platformVersion
    tournaments.value = result.items
    hydrateDrafts(result.items)
    if (detailId.value && !result.items.some(item => item.id === detailId.value)) detailId.value = null
  } finally { loading.value = false }
}
async function syncAfterWrite(_result: Tournament, success: string) {
  notice.value = success
  await refreshTournaments()
}
async function runAction(work: () => Promise<void>) {
  if (busy.value) return
  busy.value = true
  try { await work() } catch (error) { notice.value = errorText(error) } finally { busy.value = false }
}
async function loadFriends() {
  if (!platformState.account) return
  try { friends.value = await friendApi.friends() } catch (error) { notice.value = errorText(error) }
}
async function openDetail(item: Tournament) {
  detailId.value = item.id
  staffDraft.value = item.referees.map(person => person.accountId)
  staffReason.value = ''
  roundReason.value = ''
  try {
    const current = await tournamentApi.getByCode(item.code)
    const index = tournaments.value.findIndex(candidate => candidate.id === current.id)
    if (index >= 0) tournaments.value[index] = current
    hydrateDrafts([current])
    staffDraft.value = current.referees.map(person => person.accountId)
  } catch (error) { notice.value = errorText(error) }
}
function toggleReferee(account: PlatformFriend) {
  const index = form.referees.indexOf(account.accountId)
  if (index >= 0) form.referees.splice(index, 1); else form.referees.push(account.accountId)
}
function toggleStaffDraft(account: PlatformFriend) {
  const index = staffDraft.value.indexOf(account.accountId)
  if (index >= 0) staffDraft.value.splice(index, 1); else staffDraft.value.push(account.accountId)
}

function createTournament() { void runAction(async () => {
  if (!form.name.trim()) throw new Error('请输入赛事名称')
  const tournament: TournamentCreateInput = {
    name: form.name.trim(), format: form.format, visibility: form.visibility,
    maxPlayers: Math.max(2, Math.min(256, Number(form.maxPlayers) || 16)),
    startAt: form.startAt ? new Date(form.startAt).toISOString() : undefined,
    ruleset: form.ruleset.trim() || '现行规则', description: form.description.trim(),
    deckVisibility: form.deckVisibility, disasterMode: form.disasterMode, banList: form.banList.trim(),
    disasterCardIds: form.disasterMode === 'none' ? [] : [...form.disasterCardIds],
    cardRestrictions: form.cardRestrictions.map(item => ({ ...item })),
    roundMinutes: Math.max(5, Number(form.roundMinutes) || 50),
    checkInMinutes: Math.max(1, Number(form.checkInMinutes) || 5), refereeAccountIds: [...form.referees],
    swissRounds: form.format === 'single' ? 0 : Math.max(1, Math.min(20, Number(form.swissRounds) || 3)),
    cutSize: form.format === 'swiss-cut' ? Math.max(2, Number(form.cutSize) || 8) : undefined,
    registrationVisibility: form.registrationVisibility,
    lateGraceMinutes: Math.max(0, Math.min(60, Number(form.lateGraceMinutes) || 0)),
  }
  const created = await tournamentApi.create(tournament, platformVersion.value)
  detailId.value = created.id
  tab.value = 'mine'
  await syncAfterWrite(created, `赛事 ${created.code} 已建立，请提交参赛牌库快照`)
}) }
function join(item: Tournament) { void runAction(async () => {
  const existing = item.participants.find(person => person.accountId === accountId.value)
  const draft = deckDrafts[item.id] ?? { name: '', code: '' }
  if (!draft.name || !savedDecks.value.some(deck => deck.name === draft.name)) throw new Error('请先从账号牌库选择报名牌库')
  const updated = existing?.dropped
    ? await tournamentApi.updateRegistration(item.id, item.version, draft.name, draft.code)
    : await tournamentApi.register(item.id, item.version, draft.name, draft.code)
  await syncAfterWrite(updated, `已报名「${item.name}」，请在详情中确认牌库快照`)
}) }
function saveDeck(item: Tournament, person: TournamentParticipant) { void runAction(async () => {
  if (person.accountId !== accountId.value) throw new Error('只能提交自己的牌库快照')
  const draft = deckDrafts[item.id] ?? { name: '', code: '' }
  if (!draft.name || !savedDecks.value.some(deck => deck.name === draft.name)) throw new Error('请从账号牌库选择牌库')
  const updated = await tournamentApi.updateRegistration(item.id, item.version, draft.name, '')
  await syncAfterWrite(updated, '牌库快照已更新')
}) }
function dropRegistration(item: Tournament) { void runAction(async () => {
  if (item.status === 'running' && !window.confirm('退赛后不会进入后续配对；当前桌仍需由裁判记录赛果。确定退赛吗？')) return
  await syncAfterWrite(await tournamentApi.drop(item.id, item.version), '已退出该赛事')
}) }
function saveStaff(item: Tournament) { void runAction(async () => {
  if (!staffReason.value.trim()) throw new Error('请填写工作人员变更理由')
  await syncAfterWrite(await tournamentApi.setStaff(item.id, item.version, staffDraft.value, staffReason.value.trim()), '工作人员已更新')
}) }
function startTournament(item: Tournament) { void runAction(async () => {
  await syncAfterWrite(await tournamentApi.start(item.id, item.version, '主办者请求正式开启赛事'), '赛事已开启')
}) }
function toggleReady(item: Tournament, round: TournamentRound, match: TournamentMatch, side: 'A' | 'B') { void runAction(async () => {
  const targetId = side === 'A' ? match.playerAAccountId : match.playerBAccountId
  const ready = side === 'A' ? match.readyA : match.readyB
  if (!targetId) return
  if (targetId !== accountId.value && !isStaff(item)) throw new Error('只能为自己签到')
  const updated = await tournamentApi.checkIn(item.id, round.number, item.version,
    targetId === accountId.value ? undefined : targetId, !ready)
  await syncAfterWrite(updated, !ready ? '已完成本轮签到' : '已取消本轮签到')
}) }
function startRound(item: Tournament, round: TournamentRound) { void runAction(async () => {
  const reason = roundReason.value.trim() || `第 ${round.number} 轮已核对签到与房间准备`
  await syncAfterWrite(await tournamentApi.startRound(item.id, round.number, item.version, reason), `第 ${round.number} 轮已开始`)
}) }
function pauseRound(item: Tournament, round: TournamentRound) { void runAction(async () => {
  if (!roundReason.value.trim()) throw new Error('暂停或恢复计时必须填写理由')
  const updated = await tournamentApi.pauseRound(item.id, round.number, item.version, !round.paused, roundReason.value.trim())
  await syncAfterWrite(updated, round.paused ? '本轮计时已恢复' : '本轮计时已暂停')
}) }
function nextRound(item: Tournament) { void runAction(async () => {
  await syncAfterWrite(await tournamentApi.nextRound(item.id, item.version, roundReason.value.trim() || '上一轮结果已复核'), '下一轮配对已建立')
}) }
function addTime(item: Tournament, match: TournamentMatch, minutes: number) { void runAction(async () => {
  const reason = rulingReasons[match.id]?.trim()
  if (!reason) throw new Error('补时必须填写裁判理由')
  await syncAfterWrite(await tournamentApi.extendMatch(item.id, match.id, item.version, minutes, reason), `本桌已补时 ${minutes} 分钟`)
}) }
function ruleMatch(item: Tournament, match: TournamentMatch, kind: 'result' | 'penalty' | 'no-show', decision: string, targetAccountId?: string) { void runAction(async () => {
  const reason = rulingReasons[match.id]?.trim()
  if (!reason) throw new Error('赛果与判罚必须填写裁判理由')
  const result = await tournamentApi.ruleMatch(item.id, match.id, item.version, { kind, decision, targetAccountId, reason })
  await syncAfterWrite(result, '裁判决定已记录')
}) }
function noShowLoss(item: Tournament, match: TournamentMatch) {
  if (!match.readyA && match.readyB) ruleMatch(item, match, 'no-show', 'no-show-a', match.playerAAccountId)
  else if (match.readyA && !match.readyB && match.playerBAccountId) ruleMatch(item, match, 'no-show', 'no-show-b', match.playerBAccountId)
  else notice.value = '仅有一方未准备时才可提交未入场判负'
}
function requestRematch(item: Tournament, match: TournamentMatch) { void runAction(async () => {
  const reason = rulingReasons[match.id]?.trim()
  if (!reason) throw new Error('重赛必须填写裁判理由')
  await syncAfterWrite(await tournamentApi.rematch(item.id, match.id, item.version, reason), '重赛已记录，本桌需重新签到')
}) }
function finishTournament(item: Tournament) { void runAction(async () => {
  await syncAfterWrite(await tournamentApi.complete(item.id, item.version, '全部轮次与赛果已复核，结束并归档'), '赛事已结束并归档')
}) }

function readLegacyCandidates(): LegacyTournamentInput[] {
  for (const key of legacyStorageKeys) {
    try {
      const parsed: unknown = JSON.parse(localStorage.getItem(key) || '[]')
      if (!Array.isArray(parsed) || parsed.length === 0) continue
      return parsed.filter(entry => entry && typeof entry === 'object').map((entry, index) => {
        const source = entry as Partial<LegacyTournamentInput>
        const fallbackId = `local-${index}-${source.code || source.createdAt || source.name || 'tournament'}`
        return {
          ...source, id: String(source.id || fallbackId).slice(0, 128), name: String(source.name || '未命名赛事').slice(0, 100),
          maxPlayers: Number(source.maxPlayers) || 16, roundMinutes: Number(source.roundMinutes) || 50,
          checkInMinutes: Number(source.checkInMinutes) || 5,
        } as LegacyTournamentInput
      })
    } catch { /* 损坏的本机旧数据不影响服务端赛事 */ }
  }
  return []
}
function previewLegacyImport() { void runAction(async () => {
  if (legacyCandidates.value.length > 20) throw new Error('一次最多导入 20 个旧赛事，请先在本机整理旧数据')
  legacyPreview.value = await tournamentApi.importLegacy(legacyCandidates.value, platformVersion.value, undefined, true)
  await refreshTournaments()
  notice.value = `预览完成：服务端可接收 ${legacyPreview.value.tournaments.length} 个赛事，尚未写入`
}) }
function confirmLegacyImport() { void runAction(async () => {
  if (!legacyPreview.value) throw new Error('请先生成导入预览')
  const imported = await tournamentApi.importLegacy(legacyCandidates.value, platformVersion.value, legacyPreview.value.previewHash, false)
  if (!imported.applied) throw new Error('服务端未确认导入')
  for (const key of legacyStorageKeys) localStorage.removeItem(key)
  legacyCandidates.value = []
  legacyPreview.value = null
  await refreshTournaments()
  notice.value = `已确认导入 ${imported.tournaments.length} 个旧赛事，本机旧副本已清理`
}) }
async function copyShareLink(item: Tournament) {
  const link = new URL('/battle/tournaments', location.origin)
  link.searchParams.set('code', item.code)
  await navigator.clipboard.writeText(link.toString())
  notice.value = '赛事分享链接已复制；登录后会自动打开该赛事'
}
function enterMatch(item: Tournament, match: TournamentMatch, spectate = false) { void runAction(async () => {
  await connect()
  if (spectate) spectateTournamentMatch(item.id, match.id)
  else enterTournamentMatch(item.id, match.id)
  await router.push('/game')
}) }

watch(() => l12State.notice, value => { if (value) notice.value = value })

onMounted(async () => {
  legacyCandidates.value = readLegacyCandidates()
  if (!platformState.account) { notice.value = '请先登录账号后使用赛事中心'; return }
  try {
    const [, , cards, policy, decks] = await Promise.all([refreshTournaments(), loadFriends(), loadDeckCatalog(), getEffectiveOperationsPolicy(), syncSavedDecksFromAccount()])
    catalog.value = cards
    savedDecks.value = Object.values(decks)
    hydrateDrafts(tournaments.value)
    form.disasterCardIds = [...policy.disasterCardIds]
    form.cardRestrictions = policy.cardRestrictions.map(item => ({ ...item }))
    const sharedCode = typeof route.query.code === 'string' ? route.query.code.trim() : ''
    if (sharedCode) {
      const shared = await tournamentApi.getByCode(sharedCode)
      const index = tournaments.value.findIndex(item => item.id === shared.id)
      if (index >= 0) tournaments.value[index] = shared; else tournaments.value.unshift(shared)
      await openDetail(shared)
    }
  } catch (error) { notice.value = errorText(error) }
})
</script>

<template>
  <div class="tournament-page">
    <header class="page-head"><div><small>TOURNAMENT OPERATIONS</small><h1>赛事中心</h1><p>服务端保存报名、牌库可见性快照、配对、判罚与审计记录；主办者与裁判权限仅在当前赛事内生效。</p></div><button v-if="hasPermission('tournaments.create')" class="gold" @click="tab = 'create'">举办赛事</button></header>
    <section v-if="legacyCandidates.length" class="migration"><div><b>检测到 {{ legacyCandidates.length }} 个本机旧赛事</b><p>旧 localStorage 仅作为待导入来源，不再参与赛事展示或写入。必须先预览，再明确确认导入。</p></div><button v-if="canImportLegacy" :disabled="busy" @click="previewLegacyImport">预览导入（dry-run）</button><button v-if="legacyPreview" class="gold" :disabled="busy" @click="confirmLegacyImport">确认导入 {{ legacyPreview.tournaments.length }} 个赛事</button><span v-else-if="!canImportLegacy">当前账号没有旧赛事导入权限</span></section>
    <section v-if="legacyPreview" class="migration-preview"><b>导入预览未写入</b><span>摘要 {{ legacyPreview.previewHash }}</span><span v-for="item in legacyPreview.tournaments" :key="item.id">{{ item.code }} · {{ item.name }} · {{ item.participants.length }} 人</span></section>
    <nav class="tabs"><button :class="{active:tab==='current'}" @click="tab='current'">当前赛事</button><button :class="{active:tab==='completed'}" @click="tab='completed'">结束赛事</button><button :class="{active:tab==='mine'}" @click="tab='mine'">我的赛程</button><button :class="{active:tab==='create'}" @click="tab='create'">创建向导</button></nav>

    <section v-if="tab !== 'create'" class="registry">
      <input v-model="search" placeholder="搜索赛事名称、主办者或赛事代码"/>
      <div v-if="loading" class="empty">正在从服务端读取赛事…</div>
      <article v-for="item in visibleTournaments" v-else :key="item.id"><div><small>{{ item.code }}</small><h2>{{ item.name }}</h2><p>{{ item.description || '赛事方尚未发布说明。' }}</p></div><dl><div><dt>状态</dt><dd>{{ statusText(item.status) }}</dd></div><div><dt>赛制</dt><dd>{{ formatText(item.format) }}</dd></div><div><dt>人数</dt><dd>{{ item.participants.filter(person=>!person.dropped).length }}/{{ item.maxPlayers }}</dd></div><div><dt>主办者</dt><dd>{{ item.organizerName }}</dd></div></dl><div class="actions"><button @click="openDetail(item)">赛事详情</button><template v-if="item.status==='registration'&&!isParticipant(item)"><select v-if="deckDrafts[item.id]" v-model="deckDrafts[item.id].name"><option disabled value="">选择账号牌库</option><option v-for="deck in savedDecks" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><button class="gold" :disabled="busy||!deckDrafts[item.id]?.name" @click="join(item)">报名参赛</button></template><button @click="copyShareLink(item)">复制分享链接</button></div></article>
      <div v-if="!loading&&!visibleTournaments.length" class="empty">暂无符合条件的服务端赛事</div>
    </section>

    <section v-else class="create-panel">
      <header><small>ORGANIZER WORKFLOW</small><h2>创建赛事</h2><p>创建者自动成为本场主办者；裁判只能从自己的好友中指定，两种身份均随赛事结束失效。</p></header>
      <div class="form-grid">
        <label class="wide">赛事名称<input v-model="form.name" maxlength="40"/></label><label>赛制<select v-model="form.format"><option value="swiss">纯瑞士轮</option><option value="single">纯单败淘汰</option><option value="swiss-cut">瑞士后 Cut 淘汰</option></select></label><label>人数上限<input v-model.number="form.maxPlayers" type="number" min="2" max="256"/></label><label v-if="form.format!=='single'">瑞士轮数<input v-model.number="form.swissRounds" type="number" min="1" max="20"/></label><label v-if="form.format==='swiss-cut'">Cut 人数<input v-model.number="form.cutSize" type="number" min="2" :max="form.maxPlayers"/></label><label>加入方式<select v-model="form.visibility"><option value="public">公开发现或分享链接</option><option value="code">仅分享链接</option></select></label><label>报名名单<select v-model="form.registrationVisibility"><option value="public">公开报名名单</option><option value="staff">仅工作人员与本人</option></select></label><label>计划时间<input v-model="form.startAt" type="datetime-local"/></label><label>每轮分钟<input v-model.number="form.roundMinutes" type="number" min="5" max="240"/></label><label>每轮签到窗口<input v-model.number="form.checkInMinutes" type="number" min="1" max="60"/></label><label>迟到宽限分钟<input v-model.number="form.lateGraceMinutes" type="number" min="0" max="60"/></label><label>天灾模式<select v-model="form.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="season">赛季天灾</option><option value="none">不使用天灾</option></select></label><label>牌库公开<select v-model="form.deckVisibility"><option value="always">全程公开牌库</option><option value="after">赛后公开牌库</option><option value="private">不公开牌库</option></select></label><label class="wide">规则版本<input v-model="form.ruleset"/></label><fieldset v-if="form.disasterMode !== 'none'" class="wide"><legend>本场天灾池</legend><DisasterPoolPicker v-model="form.disasterCardIds" :cards="catalog" locked-id="S01-DS10"/></fieldset><fieldset class="wide"><legend>本场构筑规则</legend><ConstructionRuleEditor v-model="form.cardRestrictions" :cards="catalog"/></fieldset><label class="wide">补充规则说明<textarea v-model="form.banList" rows="2" placeholder="结构化规则以外的说明（可留空）"/></label>
        <fieldset class="wide"><legend>从好友中选择本场裁判</legend><button v-for="friend in friends" :key="friend.accountId" type="button" :class="{selected:form.referees.includes(friend.accountId)}" @click="toggleReferee(friend)">{{ friend.username }}</button><span v-if="!friends.length">暂无可选好友；可先到好友页面添加裁判。</span></fieldset><label class="wide">赛事说明<textarea v-model="form.description" rows="4"/></label>
      </div><button class="gold create-action" :disabled="busy||!hasPermission('tournaments.create')" @click="createTournament">建立服务端赛事</button>
    </section>

    <div v-if="detail" class="mask" @click.self="detailId=null"><section class="detail">
      <header><div><small>{{ detail.code }} · v{{ detail.version }}</small><h2>{{ detail.name }}</h2></div><button @click="detailId=null">×</button></header><div class="summary"><span>{{ statusText(detail.status) }}</span><span>{{ formatText(detail.format) }}</span><span v-if="detail.swissRounds">计划 {{ detail.swissRounds }} 轮瑞士</span><span v-if="detail.cutSize">Cut {{ detail.cutSize }}</span><span>{{ disasterText(detail.rules.disasterMode) }}</span><span>{{ deckVisibilityText(detail.rules.deckVisibility) }}</span><span>{{ detail.registrationVisibility==='public'?'报名公开':'报名名单限定' }}</span><span>每轮 {{ detail.roundMinutes }} 分钟 · 迟到宽限 {{ detail.lateGraceMinutes }} 分钟</span><span v-if="detail.legacyImported">已导入旧赛事</span></div><p>{{ detail.description || '赛事方尚未发布说明。' }}</p><section class="rules"><b>规则快照：{{ detail.rules.ruleset }}</b><span>天灾池 {{ detail.rules.disasterCardIds.length }} 张</span><span>构筑规则 {{ detail.rules.cardRestrictions.length }} 条（含通用/主宰专属）</span><span>补充说明：{{ detail.rules.banList || '无' }}</span><span>快照 {{ detail.rules.hash.slice(0,12) }}</span><span>计划开始：{{ detail.startAt ? new Date(detail.startAt).toLocaleString() : '由主办者通知' }}</span></section>
      <h3>本场工作人员</h3><div class="chips"><span>主办者 · {{ detail.organizerName }}</span><span v-for="person in detail.referees" :key="person.accountId">裁判 · {{ person.username }}</span></div><section v-if="detail.status !== 'completed' && isOrganizer(detail)" class="staff-editor"><button v-for="friend in friends" :key="friend.accountId" type="button" :class="{selected:staffDraft.includes(friend.accountId)}" @click="toggleStaffDraft(friend)">{{ friend.username }}</button><input v-model="staffReason" placeholder="本场裁判变更理由（写入审计）"/><button :disabled="busy" @click="saveStaff(detail)">保存本场裁判</button></section>
      <h3>参赛人员与牌库快照</h3><div class="participants"><div v-for="person in detail.participants" :key="person.accountId"><b>#{{ person.seed || '—' }} · {{ person.username }}<em v-if="person.dropped"> · 已退赛</em><em v-else-if="person.eliminated"> · 已淘汰</em></b><span>{{ person.checkedIn ? '已准备' : '未准备' }}</span><template v-if="person.accountId===accountId&&detail.status==='registration'&&deckDrafts[detail.id]"><select v-model="deckDrafts[detail.id].name"><option disabled value="">选择账号牌库</option><option v-for="deck in savedDecks" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><button :disabled="busy||!deckDrafts[detail.id].name" @click="saveDeck(detail,person)">保存报名快照</button></template><template v-else-if="person.deck"><span>{{ person.deck.name }}</span><code>{{ person.deck.masterId }} · {{ person.deck.hash.slice(0,12) }}</code></template><em v-else>牌库未公开或尚未提交</em></div></div>
      <template v-if="standingSnapshots.length"><h3>瑞士排名快照</h3><details v-for="snapshotRound in standingSnapshots" :key="snapshotRound.id" class="standing-snapshot" :open="snapshotRound.number===standingSnapshots.at(-1)?.number"><summary>第 {{ snapshotRound.number }} 轮排名 · {{ snapshotRound.standingsCapturedAt ? new Date(snapshotRound.standingsCapturedAt).toLocaleString() : '已存档' }}<b v-if="detail.finalSwissStandings.length&&snapshotRound.number===standingSnapshots.at(-1)?.number"> · 最终瑞士排名</b></summary><div class="standings"><div class="standing-head"><b>名次</b><b>玩家</b><b>胜-负-和</b><b>对手分</b><b>对手的对手分</b><b>种子</b></div><div v-for="entry in snapshotRound.standings" :key="entry.accountId"><b>#{{ entry.rank }}</b><span>{{ entry.username }}</span><span>{{ entry.wins }}-{{ entry.losses }}-{{ entry.draws }}<small v-if="entry.byes"> · 轮空 {{ entry.byes }}</small></span><span>{{ entry.opponentScore }}</span><span>{{ entry.opponentsOpponentScore }}</span><span>{{ entry.seed }}</span></div></div></details></template>
      <template v-if="detail.eliminationBracket.length"><h3>淘汰树</h3><div class="bracket"><section v-for="round in detail.eliminationBracket" :key="round.number"><b>淘汰第 {{ round.number }} 轮</b><article v-for="match in round.matches" :key="match.id"><span>{{ match.playerAName }}</span><i>VS</i><span>{{ match.playerBName }}</span><em>{{ resultText(match.result) || '待定' }}</em></article></section></div></template>
      <template v-if="currentRound"><h3>第 {{ currentRound.number }} 轮 · {{ currentRound.stage==='elimination'?'淘汰赛':'瑞士轮' }} · {{ currentRound.status==='checkin'?'签到/准备':currentRound.status==='running'?'对局中':'已完成' }}</h3><div class="round-controls" v-if="isStaff(detail)&&detail.status!=='completed'"><input v-model="roundReason" placeholder="开轮、暂停或下一轮的操作理由（写入审计）"/><button v-if="currentRound.status==='checkin'" class="gold" :disabled="busy" @click="startRound(detail,currentRound)">开始本轮（已准备桌启动）</button><button v-if="currentRound.status==='running'" :disabled="busy" @click="pauseRound(detail,currentRound)">{{ currentRound.paused?'恢复计时':'暂停计时' }}</button><button v-if="currentRound.status==='completed'&&canCreateNextRound(detail,currentRound)" :disabled="busy" @click="nextRound(detail)">生成下一轮配对</button></div>
        <div class="matches"><article v-for="match in currentRound.matches" :key="match.id"><header><b>桌 {{ match.table }}</b><span>规则 {{ match.rulesHash.slice(0,10) }} · 重赛 {{ match.replayNumber }}</span><button v-if="match.canEnter" class="gold" :disabled="busy" @click="enterMatch(detail,match)">进入我的对局</button><button v-if="match.canSpectate" :disabled="busy" @click="enterMatch(detail,match,true)">工作人员观战</button></header><div><button :class="{ready:match.readyA}" :disabled="busy||currentRound.status!=='checkin'&&(match.status!=='waiting')||(match.playerAAccountId!==accountId&&!isStaff(detail))" @click="toggleReady(detail,currentRound,match,'A')">{{ match.playerAName }} · {{ match.readyA?'已准备':'准备' }}</button><i>VS</i><button :class="{ready:match.readyB}" :disabled="busy||!match.playerBAccountId||currentRound.status!=='checkin'&&(match.status!=='waiting')||(match.playerBAccountId!==accountId&&!isStaff(detail))" @click="toggleReady(detail,currentRound,match,'B')">{{ match.playerBName }} · {{ match.readyB?'已准备':'准备' }}</button></div><p v-if="match.deadline">本桌截止 {{ new Date(match.deadline).toLocaleTimeString() }} · 补时 {{ match.timeExtensionMinutes }} 分钟</p><p v-else-if="match.graceDeadline">未到齐；宽限至 {{ new Date(match.graceDeadline).toLocaleTimeString() }}，不阻塞其他桌</p><p v-if="match.result">{{ resultText(match.result) }}<span v-if="match.recordedMatchId"> · 权威记录 {{ match.recordedMatchId.slice(0,12) }}</span></p><ul v-if="match.rulings.length"><li v-for="ruling in match.rulings" :key="ruling.id">{{ ruling.actorName }}：{{ ruling.reason }}（{{ ruling.decision }}）</li></ul><ul v-if="match.events.length"><li v-for="event in match.events" :key="event.id">{{ new Date(event.createdAt).toLocaleTimeString() }} · {{ event.detail }}</li></ul><template v-if="isStaff(detail)&&detail.status!=='completed'"><textarea v-model="rulingReasons[match.id]" placeholder="裁判理由（补时、赛果、判罚或重赛必填）"/><footer v-if="match.status!=='completed'"><button :disabled="busy" @click="addTime(detail,match,5)">补时 5 分钟</button><button :disabled="busy" @click="noShowLoss(detail,match)">宽限期后未入场判负</button><button :disabled="busy" @click="ruleMatch(detail,match,'result','player-a')">A 胜</button><button :disabled="busy||!match.playerBAccountId" @click="ruleMatch(detail,match,'result','player-b')">B 胜</button><button v-if="currentRound.stage==='swiss'" :disabled="busy||!match.playerBAccountId" @click="ruleMatch(detail,match,'result','draw')">平局</button><button :disabled="busy" @click="ruleMatch(detail,match,'penalty','warning',match.playerAAccountId)">警告 A</button><button :disabled="busy||!match.playerBAccountId" @click="ruleMatch(detail,match,'penalty','warning',match.playerBAccountId)">警告 B</button></footer><footer v-else-if="match.playerBAccountId"><button :disabled="busy" @click="requestRematch(detail,match)">裁判发起重赛</button></footer></template></article></div>
      </template><footer class="detail-actions"><template v-if="detail.status==='registration'&&!isParticipant(detail)&&deckDrafts[detail.id]"><select v-model="deckDrafts[detail.id].name"><option disabled value="">选择账号牌库</option><option v-for="deck in savedDecks" :key="deck.name" :value="deck.name">{{ deck.name }}</option></select><button class="gold" :disabled="busy||!deckDrafts[detail.id].name" @click="join(detail)">选择牌库并报名</button></template><button @click="copyShareLink(detail)">复制稳定分享链接</button><button v-if="detail.status!=='completed'&&isParticipant(detail)&&!isOrganizer(detail)" class="danger" :disabled="busy" @click="dropRegistration(detail)">{{ detail.status==='running'?'退赛':'退出赛事' }}</button><button v-if="isOrganizer(detail)&&detail.status==='registration'" class="gold" :disabled="busy||detail.participants.filter(person=>!person.dropped).length<2" @click="startTournament(detail)">正式开启赛事</button><button v-if="isOrganizer(detail)&&detail.status==='running'" class="danger" :disabled="busy" @click="finishTournament(detail)">结束并归档赛事</button></footer>
    </section></div><button v-if="notice" class="toast" @click="notice=''">{{ notice }}</button>
  </div>
</template>

<style scoped>
.tournament-page{width:min(1320px,calc(100% - 40px));min-height:100%;margin:auto;padding:32px 0 70px;font-family:'Microsoft YaHei',sans-serif}.page-head{display:flex;justify-content:space-between;gap:20px;padding-bottom:20px;border-bottom:1px solid #54482b}.page-head small,.create-panel small,.registry small,.detail small{color:#d6b55e;font:900 9px monospace;letter-spacing:.16em}.page-head h1{margin:5px 0;font-size:31px}.page-head p,.create-panel p,.migration p,.pending p{margin:0;color:#85877f;font-size:11px}.gold{border-color:#d0ad54!important;background:#d0ad54!important;color:#0c0c0a!important}.page-head button,.create-action{padding:12px 22px}.migration,.migration-preview{display:flex;align-items:center;gap:10px;margin-top:14px;padding:14px;border:1px solid #8a743e;background:#211b0d}.migration>div{margin-right:auto}.migration button{padding:9px;border:1px solid #66552c;background:#11120f;color:#ead69b}.migration-preview{align-items:flex-start;flex-direction:column;font-size:10px}.migration-preview>span:first-of-type{font-family:monospace;color:#8d856f}.tabs{display:grid;grid-template-columns:repeat(4,1fr);margin-top:14px;border:1px solid #3d392d}.tabs button{padding:13px;border:0;background:#0e100f;color:#817c70;font-weight:900}.tabs button.active{background:#292315;color:#f2dda2}.registry,.create-panel{margin-top:14px;border:1px solid #3d392d;background:#11120f}.registry>input{box-sizing:border-box;width:calc(100% - 24px);margin:12px;padding:12px;border:1px solid #4b483d;background:#090b0b;color:#fff}.registry>article{display:grid;grid-template-columns:1.3fr 1fr auto;align-items:center;gap:20px;padding:17px;border-top:1px solid #302e26}.registry h2{margin:4px 0}.registry p{margin:0;color:#777;font-size:10px}.registry dl{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.registry dt{color:#777;font-size:8px}.registry dd{margin:4px 0;font-size:10px;font-weight:900}.actions{display:flex;align-items:center;gap:6px}.actions select,.participants select{padding:9px;border:1px solid #4b483d;background:#080a0a;color:#fff}.actions button,.detail button,.round-controls button,.staff-editor button,.pending button{padding:9px 11px;border:1px solid #585342;background:#181913;color:#f1ead8;font-weight:900}.empty{display:grid;min-height:250px;place-items:center;color:#777}.create-panel{padding:24px}.create-panel>header{padding-bottom:16px;border-bottom:1px solid #353229}.form-grid{display:grid;grid-template-columns:repeat(2,1fr);gap:13px;margin-top:18px}.form-grid label{font-size:10px;font-weight:900;color:#b2aa98}.form-grid input,.form-grid select,.form-grid textarea,.participants input,.matches textarea,.round-controls input,.staff-editor input,.pending input,.reference input{box-sizing:border-box;width:100%;margin-top:6px;padding:10px;border:1px solid #4b483d;background:#080a0a;color:#fff}.wide{grid-column:1/-1}.form-grid fieldset{display:flex;flex-wrap:wrap;gap:7px;border:1px solid #4b483d}.form-grid fieldset button,.staff-editor button{padding:8px;border:1px solid #514b3b;background:#12130f;color:#aaa}.form-grid fieldset button.selected,.staff-editor button.selected{border-color:#d1af55;color:#efd888}.form-grid fieldset span{color:#777;font-size:10px}.create-action{display:block;margin:18px 0 0 auto;border:1px solid;font-weight:900}.mask{position:fixed;z-index:100;inset:0;display:grid;place-items:center;padding:18px;background:rgba(1,2,2,.84);backdrop-filter:blur(7px)}.detail{width:min(1040px,96vw);max-height:92vh;overflow:auto;border:1px solid #8a743e;background:#11120f;box-shadow:0 20px 80px #000}.detail>header{display:flex;align-items:center;justify-content:space-between;padding:18px;border-bottom:1px solid #3d392d}.detail h2{margin:4px 0}.detail>header button{font-size:20px}.summary,.chips{display:flex;flex-wrap:wrap;gap:7px;padding:14px 18px}.summary span,.chips span{padding:6px 9px;border:1px solid #494436;color:#ddca96;font-size:9px}.detail>p,.detail>h3,.rules,.participants,.round-controls,.matches,.staff-editor,.pending,.standing-snapshot,.bracket{margin-right:18px;margin-left:18px}.detail>p{color:#999;font-size:11px}.detail>h3{margin-top:22px}.rules{display:flex;flex-wrap:wrap;gap:10px;padding:11px;background:#1a1912;font-size:10px}.rules span{color:#aaa}.staff-editor{display:flex;flex-wrap:wrap;gap:7px;padding:10px;border:1px solid #343229}.staff-editor input{flex:1 1 260px;margin:0}.pending{margin-top:16px;padding:12px;border:1px solid #5a4b29;background:#18150d}.pending h3{margin:0}.pending article{display:grid;grid-template-columns:1.4fr 1fr auto auto;align-items:center;gap:7px;margin-top:8px}.pending input{margin:0}.participants{display:grid;gap:6px}.participants>div{display:grid;grid-template-columns:170px 70px 1fr 1fr auto;align-items:center;gap:8px;padding:8px;border:1px solid #343229}.participants span{color:#68c9a3;font-size:9px}.participants input{margin:0}.participants em{color:#777;font-size:9px}.participants>div>em{grid-column:3/6}.participants code{overflow:hidden;color:#aaa;text-overflow:ellipsis}.standing-snapshot{margin-top:7px;border:1px solid #3a372e}.standing-snapshot summary{padding:9px;color:#d5b55d;cursor:pointer}.standing-snapshot .standings{margin:0;border-width:1px 0 0}.standings>div{display:grid;grid-template-columns:60px 1.4fr repeat(4,1fr);gap:8px;padding:8px;border-top:1px solid #2d2b24;font-size:10px}.standings>div:first-child{border-top:0;background:#1b1912;color:#d5b55d}.standings small{color:#8e846c}.bracket{display:flex;gap:10px;overflow-x:auto;padding-bottom:8px}.bracket>section{min-width:230px;padding:10px;border:1px solid #3a372e;background:#0c0e0d}.bracket article{display:grid;grid-template-columns:1fr auto 1fr;gap:6px;margin-top:8px;padding:7px;border:1px solid #2c2b25;font-size:9px}.bracket article em{grid-column:1/-1;color:#d5b55d}.round-controls{display:flex;gap:7px;margin-bottom:10px}.round-controls input{flex:1;margin:0}.matches{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.matches>article{padding:11px;border:1px solid #3a372e;background:#0c0e0d}.matches article>header{display:flex;align-items:center;flex-wrap:wrap;gap:8px}.matches article>header span{margin-left:auto;color:#d5b55d;font-size:9px}.matches article>div{display:grid;grid-template-columns:1fr auto 1fr;align-items:center;gap:8px;margin-top:10px}.matches article>div button{min-width:0}.matches button.ready{border-color:#4fbc91;background:#123325;color:#8de0bd}.matches i{color:#8d7540;font-style:normal}.matches p,.matches li{color:#b8ae91;font-size:9px}.matches textarea{min-height:54px;resize:vertical}.matches footer{display:flex;flex-wrap:wrap;gap:5px;margin-top:8px}.reference{display:flex!important;grid-template-columns:none!important;gap:6px!important}.reference input{margin:0}.detail-actions{display:flex;justify-content:flex-end;gap:8px;margin-top:22px;padding:15px 18px;border-top:1px solid #3d392d}.detail-actions .danger{border-color:#8e343d;background:#321319;color:#f3b3ba}.toast{position:fixed;z-index:130;right:22px;bottom:22px;max-width:min(560px,calc(100vw - 44px));padding:11px 16px;border:1px solid #d1ad54;background:#251e0e;color:#f2d98b;font-weight:900}button:disabled{cursor:not-allowed;opacity:.45}
@media(max-width:900px){.registry>article{grid-template-columns:1fr}.matches{grid-template-columns:1fr}.participants>div{grid-template-columns:1fr 1fr}.participants input,.participants em,.participants code,.participants button{grid-column:1/-1}.pending article{grid-template-columns:1fr}.migration{align-items:flex-start;flex-direction:column}.migration>div{margin:0}}@media(max-width:650px){.tournament-page{width:auto;padding:20px 12px 50px}.page-head{align-items:flex-start}.tabs{grid-template-columns:1fr 1fr}.form-grid{grid-template-columns:1fr}.wide{grid-column:auto}.registry dl{grid-template-columns:1fr 1fr}.detail-actions,.round-controls{flex-direction:column}.detail-actions button{width:100%}}
.detail-actions select{padding:9px;border:1px solid #4b483d;background:#080a0a;color:#fff}
</style>
