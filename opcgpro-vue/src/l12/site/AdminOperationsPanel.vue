<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import ConstructionRuleEditor from './ConstructionRuleEditor.vue'
import DisasterPoolPicker from './DisasterPoolPicker.vue'
import {
  adminApi,
  rankedApi,
  type OperationsConfigPayload,
  type OperationsConfigPreview,
  type OperationsConfigVersion,
  type RuntimeStatus,
  type RankedConfig,
  type RankedBroadcast,
} from '@/l12/platform'

const emit = defineEmits<{ notice: [message: string] }>()
const loading = ref(false)
const version = ref(0)
const versionId = ref('')
const updatedBy = ref('')
const updatedAt = ref('')
const preview = ref<OperationsConfigPreview | null>(null)
const history = ref<OperationsConfigVersion[]>([])
const runtime = ref<RuntimeStatus | null>(null)
const reason = ref('')
const catalog = ref<DeckCard[]>([])
const presetDecks = ref('')
const featureFlags = ref('')
type OperationsSection = 'season' | 'ranked' | 'construction' | 'room' | 'features' | 'maintenance' | 'versions'
const activeSection = ref<OperationsSection>('season')
const loadError = ref('')
const sections: Array<{ id: OperationsSection; title: string; summary: string }> = [
  { id: 'season', title: '赛季与天灾', summary: '赛季周期、赛季天灾池与堙灭锁定' },
  { id: 'ranked', title: '排位与七曜', summary: '派系、五段位、七曜结算、称号与广播' },
  { id: 'construction', title: '构筑规则', summary: '禁限卡与新账号默认预组' },
  { id: 'room', title: '对战与房间', summary: '模式开关与默认房间规则' },
  { id: 'features', title: '功能开关', summary: '大厅、沙盒、观战、赛事等模块' },
  { id: 'maintenance', title: '维护与公告', summary: '维护窗口、玩家提示与生效状态' },
  { id: 'versions', title: '版本与状态', summary: '配置历史、回滚及后端运行状态' },
]
const currentSection = computed(() => sections.find(item => item.id === activeSection.value) ?? sections[0])
const form = reactive<OperationsConfigPayload>({
  season: { id: '', name: '', status: 'upcoming' },
  disasterPool: { cardIds: [], annihilationLocked: true },
  cardRestrictions: [],
  defaultPresetDeckIds: [],
  matchModes: [],
  defaultRoomConfig: { matchModeId: 'casual', spectating: 'public', handVisibility: 'request', disasterMode: 'all' },
  featureFlags: {},
  maintenance: { enabled: false, message: '', advanceBroadcastHours: 2, expectedDurationHours: 2 },
})
const rankedConfig = ref<RankedConfig | null>(null)
const rankedBroadcasts = ref<RankedBroadcast[]>([])
const rankedReason = ref('')

const observedAt = computed(() => runtime.value ? new Date(runtime.value.observedAt).toLocaleString() : '未加载')

function lines(value: string) {
  return value.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}
function parseFlags(value: string) {
  return Object.fromEntries(lines(value).map((line, index) => {
    const [key, raw = 'false'] = line.split('=').map(item => item.trim())
    if (!key || !['true', 'false'].includes(raw.toLowerCase())) throw new Error(`功能开关第 ${index + 1} 行格式应为 key=true/false`)
    return [key, raw.toLowerCase() === 'true']
  }))
}
function toLocalDateTimeInput(value?: string) {
  if (!value) return undefined
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return undefined
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}
function toIsoDateTime(value?: string) {
  if (!value) return undefined
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) throw new Error(`时间格式无效：${value}`)
  return date.toISOString()
}
function syncTextFields() {
  presetDecks.value = form.defaultPresetDeckIds.join('\n')
  featureFlags.value = Object.entries(form.featureFlags).map(([key, enabled]) => `${key}=${enabled}`).join('\n')
}
function hydrate(payload: OperationsConfigPayload) {
  const copy = structuredClone(payload)
  copy.season.startsAt = toLocalDateTimeInput(copy.season.startsAt)
  copy.season.endsAt = toLocalDateTimeInput(copy.season.endsAt)
  copy.maintenance.startsAt = toLocalDateTimeInput(copy.maintenance.startsAt)
  copy.maintenance.endsAt = toLocalDateTimeInput(copy.maintenance.endsAt)
  Object.assign(form.season, copy.season)
  Object.assign(form.disasterPool, copy.disasterPool)
  form.cardRestrictions.splice(0, form.cardRestrictions.length, ...copy.cardRestrictions)
  form.defaultPresetDeckIds.splice(0, form.defaultPresetDeckIds.length, ...copy.defaultPresetDeckIds)
  form.matchModes.splice(0, form.matchModes.length, ...copy.matchModes)
  Object.assign(form.defaultRoomConfig, copy.defaultRoomConfig)
  form.featureFlags = copy.featureFlags
  Object.assign(form.maintenance, copy.maintenance)
  syncTextFields()
}
function serialize(): OperationsConfigPayload {
  return {
    season: { ...form.season, startsAt: toIsoDateTime(form.season.startsAt), endsAt: toIsoDateTime(form.season.endsAt) },
    disasterPool: { cardIds: [...form.disasterPool.cardIds], annihilationLocked: true },
    cardRestrictions: form.cardRestrictions.map(item => ({ ...item })),
    defaultPresetDeckIds: lines(presetDecks.value),
    matchModes: form.matchModes.map(item => ({ ...item })),
    defaultRoomConfig: { ...form.defaultRoomConfig },
    featureFlags: parseFlags(featureFlags.value),
    maintenance: { ...form.maintenance, startsAt: toIsoDateTime(form.maintenance.startsAt), endsAt: toIsoDateTime(form.maintenance.endsAt) },
  }
}
async function load() {
  loading.value = true
  loadError.value = ''
  try {
    const [current, versions, status, cards, ranked, broadcasts] = await Promise.all([
      adminApi.operationsConfig(), adminApi.operationsHistory(), adminApi.runtimeStatus(), loadDeckCatalog(),
      adminApi.rankedConfig(), rankedApi.broadcasts(30),
    ])
    version.value = current.version
    versionId.value = current.versionId
    updatedBy.value = current.updatedBy
    updatedAt.value = current.updatedAt
    history.value = versions
    runtime.value = status
    catalog.value = cards
    rankedConfig.value = ranked
    rankedBroadcasts.value = broadcasts
    hydrate(current.config)
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : '运营配置加载失败'
    emit('notice', loadError.value)
  }
  finally { loading.value = false }
}
function toggleSeasonEnd(event: Event) {
  form.season.endsAt = (event.target as HTMLInputElement).checked
    ? undefined
    : toLocalDateTimeInput(new Date(Date.now() + 90 * 24 * 60 * 60 * 1000).toISOString())
}
async function previewChanges() {
  try {
    preview.value = await adminApi.previewOperationsConfig(serialize(), version.value)
    if (preview.value.valid) hydrate(preview.value.normalized)
    emit('notice', preview.value.valid ? `预览通过：${preview.value.changes.length} 项变更` : '预览未通过，请检查警告')
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置预览失败') }
}
async function applyChanges() {
  if (!reason.value.trim()) { emit('notice', '应用配置前请填写变更理由'); return }
  try {
    const result = await adminApi.applyOperationsConfig(serialize(), reason.value.trim(), version.value)
    emit('notice', result.applied ? `运营配置 v${result.current.version} 已保存并写入审计` : '运营配置未发生变更')
    reason.value = ''
    preview.value = null
    await load()
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置应用失败') }
}
async function rollback(target: OperationsConfigVersion) {
  if (!reason.value.trim()) { emit('notice', '回滚配置前请填写变更理由'); return }
  try {
    await adminApi.rollbackOperationsConfig(target.id, reason.value.trim(), version.value)
    emit('notice', `已回滚至运营配置 v${target.version}`)
    reason.value = ''
    preview.value = null
    await load()
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置回滚失败') }
}
async function saveRanked() {
  if (!rankedConfig.value || !rankedReason.value.trim()) { emit('notice', '保存排位配置前请填写变更理由'); return }
  try { rankedConfig.value = await adminApi.saveRankedConfig(rankedConfig.value, rankedReason.value.trim()); rankedReason.value = ''; emit('notice', '排位与七曜配置已保存并写入审计') }
  catch (error) { emit('notice', error instanceof Error ? error.message : '排位配置保存失败') }
}
async function deleteBroadcast(id: string) { try { await adminApi.deleteRankedBroadcast(id); rankedBroadcasts.value = rankedBroadcasts.value.filter(item => item.id !== id) } catch (error) { emit('notice', error instanceof Error ? error.message : '广播删除失败') } }

onMounted(load)
</script>

<template>
  <div class="operations-workbench">
    <header class="operations-header">
      <div><small>GAME OPERATIONS</small><h2>游戏运营</h2><p>配置保存后由构筑、房间与对战服务按版本读取。进行中的对局保持创建时规则。</p></div>
      <div class="operations-version"><span>当前生效</span><b>v{{ version }}</b><small>{{ versionId || '等待加载' }}</small><button :disabled="loading" @click="load">{{ loading ? '加载中' : '刷新' }}</button></div>
    </header>
    <p v-if="loadError" class="load-error"><b>运营配置加载失败</b><span>{{ loadError }}</span><button @click="load">重新加载</button></p>
    <nav class="operations-nav" aria-label="运营配置分组">
      <button v-for="item in sections" :key="item.id" :class="{ active: activeSection === item.id }" @click="activeSection = item.id"><b>{{ item.title }}</b><small>{{ item.summary }}</small></button>
    </nav>

    <section v-if="activeSection !== 'versions'" class="panel config-panel">
      <header><div><h2>{{ currentSection.title }}</h2><p>{{ currentSection.summary }}</p></div><span class="version-badge">配置 v{{ version }}</span></header>
      <div class="config-grid section-grid">
        <template v-if="activeSection === 'ranked' && rankedConfig">
          <fieldset><legend>定级与广播</legend><label>定级场次<input v-model.number="rankedConfig.placementMatches" type="number" min="1" max="20"/></label><label>定级七曜上限<input v-model.number="rankedConfig.placementMaximum" type="number" min="0" max="29999"/></label><label class="toggle-row wide"><span><b>启用排位快讯</b><small>五连胜、终结连胜、最高段位、派系与主宰称号变更。</small></span><input v-model="rankedConfig.broadcastEnabled" type="checkbox"/></label></fieldset>
          <fieldset v-for="faction in rankedConfig.factions" :key="faction.id" class="wide"><legend>{{ faction.name }}派系</legend><label>显示名称<input v-model="faction.name"/></label><label>主题色<input v-model="faction.color" type="color"/></label><label>最高段位第一名称号<input v-model="faction.firstTitle"/></label><label>最高段位第二至五名称号<input v-model="faction.topFiveTitle"/></label><p class="wide field-help">只有处于第五段位的玩家才会获得上述派系排名称号。</p><div class="wide ranked-tier-grid"><article v-for="tier in faction.tiers" :key="tier.minimum"><b>{{ tier.minimum.toLocaleString() }}+</b><input v-model="tier.name" aria-label="段位名"/><label>基础胜负<input v-model.number="tier.baseDelta" type="number"/></label><label>连胜上限<input v-model.number="tier.winStreakCap" type="number"/></label><label>连败保护<input v-model.number="tier.lossProtectionCap" type="number"/></label><label>差值修正<input v-model.number="tier.ratingGapCap" type="number"/></label></article></div></fieldset>
          <fieldset class="wide"><legend>主宰最强玩家称号</legend><p class="wide field-help">按本赛季使用该主宰参加过排位的已定级玩家比较七曜值；同分时依次比较该主宰胜场、场次及隐藏匹配分。</p><div class="wide ranked-master-title-grid"><label v-for="master in rankedConfig.masterTitles" :key="master.masterId"><span>{{ master.masterName }}<small>{{ master.masterId }}</small></span><input v-model="master.title" :aria-label="`${master.masterName}最强玩家称号`"/></label></div></fieldset>
          <fieldset class="wide"><legend>近期排位快讯</legend><article v-for="item in rankedBroadcasts" :key="item.id" class="broadcast-row"><span>{{ item.message }}<small>{{ new Date(item.createdAt).toLocaleString() }}</small></span><button @click="deleteBroadcast(item.id)">删除</button></article><span v-if="!rankedBroadcasts.length">暂无排位快讯</span></fieldset>
        </template>
        <template v-else-if="activeSection === 'season'">
          <fieldset><legend>当前赛季</legend><label>赛季 ID<input v-model="form.season.id"/></label><label>名称<input v-model="form.season.name"/></label><label>状态<select v-model="form.season.status"><option value="upcoming">待开始</option><option value="active">进行中</option><option value="archived">已归档</option></select></label><label>开始时间<input v-model="form.season.startsAt" type="datetime-local"/></label><label>结束时间<input v-model="form.season.endsAt" type="datetime-local" :disabled="!form.season.endsAt"/></label><label class="toggle-row wide"><span><b>不设置结束时间</b><small>赛季持续有效，直到管理员归档或设置结束时间。</small></span><input type="checkbox" :checked="!form.season.endsAt" @change="toggleSeasonEnd"/></label></fieldset>
          <fieldset><legend>赛季天灾池</legend><DisasterPoolPicker v-model="form.disasterPool.cardIds" class="wide" :cards="catalog" locked-id="S01-DS10"/><span class="locked-note wide">堙灭固定公开并锁定在最后一张。</span></fieldset>
        </template>
        <template v-else-if="activeSection === 'construction'">
          <fieldset class="wide"><legend>构筑规则</legend><p class="wide field-help">筛选卡牌后设置全局构筑上限，或指定某位主宰的专属上限；主宰专属规则优先于全局规则。</p><ConstructionRuleEditor v-model="form.cardRestrictions" class="wide" :cards="catalog"/></fieldset>
          <fieldset><legend>新账号默认预组</legend><label class="wide">每行一个官方预组的主宰卡号；仅用于新账号初始化，与默认房间设置相互独立。<textarea v-model="presetDecks" rows="14"/></label></fieldset>
        </template>
        <template v-else-if="activeSection === 'room'">
          <fieldset><legend>允许的对战模式</legend><article v-for="mode in form.matchModes" :key="mode.id" class="toggle-row"><span><b>{{ mode.name }}</b><small>{{ mode.id }}</small></span><input v-model="mode.enabled" type="checkbox"/></article><span v-if="!form.matchModes.length">暂无服务端定义的模式</span></fieldset>
          <fieldset class="room-defaults"><legend>默认房间配置</legend><label>默认模式<select v-model="form.defaultRoomConfig.matchModeId"><option v-for="mode in form.matchModes" :key="mode.id" :value="mode.id">{{ mode.name }} · {{ mode.enabled ? '启用' : '停用' }}</option></select></label><label>观战权限<select v-model="form.defaultRoomConfig.spectating"><option value="public">允许所有玩家观战</option><option value="friends">仅好友观战</option><option value="disabled">禁止观战</option></select></label><label>观战手牌<select v-model="form.defaultRoomConfig.handVisibility"><option value="request">查看前申请</option><option value="public">默认公开</option></select></label><label>天灾模式<select v-model="form.defaultRoomConfig.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="season">赛季天灾</option><option value="none">不使用天灾</option></select></label><p class="wide contract-note">这里定义新房间的初始值；房主修改后的配置仍须通过当前模式和维护策略校验。</p></fieldset>
        </template>
        <fieldset v-else-if="activeSection === 'features'" class="wide"><legend>模块功能开关</legend><p class="field-help">格式：key=true/false。关闭后前端入口会灰置，服务端仍进行权威校验。</p><label class="wide"><textarea v-model="featureFlags" rows="16"/></label></fieldset>
        <fieldset v-else-if="activeSection === 'maintenance'" class="wide"><legend>维护状态与玩家公告</legend><label class="toggle-row"><span><b>启用维护计划</b><small>到达开始前1小时自动关闭所有新开局入口；关闭此项即可立即重新开放服务器。</small></span><input v-model="form.maintenance.enabled" type="checkbox"/></label><label class="wide">后台备注/补充提示<textarea v-model="form.maintenance.message" rows="4" placeholder="可填写额外维护说明"/></label><label>开始时间<input v-model="form.maintenance.startsAt" type="datetime-local"/></label><label>结束时间<input v-model="form.maintenance.endsAt" type="datetime-local"/></label><label>提前广播（小时）<input v-model.number="form.maintenance.advanceBroadcastHours" type="number" min="1" max="168"/></label><label>预计维护时长（小时）<input v-model.number="form.maintenance.expectedDurationHours" type="number" min="1" max="168"/></label><p class="wide contract-note">大厅从提前广播时点循环显示维护公告；局内在维护前30分钟及其后每5分钟提醒。正式维护开始时未结束对局按无效处理。</p></fieldset>
      </div>
      <footer v-if="activeSection === 'ranked'" class="config-actions"><input v-model="rankedReason" placeholder="排位配置变更理由（必填）"/><button class="confirm" @click="saveRanked">保存排位配置</button></footer>
      <footer v-else class="config-actions"><input v-model="reason" placeholder="变更或回滚理由（必填）"/><button @click="previewChanges">预览差异</button><button class="confirm" @click="applyChanges">保存配置</button></footer>
      <div v-if="preview" class="preview-box"><b>{{ preview.valid ? '预览通过' : '预览未通过' }} · v{{ preview.currentVersion }} → v{{ preview.nextVersion }}</b><ul><li v-for="item in preview.changes" :key="item">{{ item }}</li></ul><p v-for="item in preview.warnings" :key="item">警告：{{ item }}</p></div>
    </section>

    <template v-else>
      <section class="panel runtime-panel">
        <header><div><h2>后端运行状态</h2><p>运行探针只在此处展示，不与运营配置字段混排。</p></div><button :disabled="loading" @click="load">刷新</button></header>
        <div v-if="runtime" class="runtime-grid"><article><small>服务版本</small><b>{{ runtime.serviceVersion }}</b><span>{{ observedAt }}</span></article><article><small>在线账号 / WS</small><b>{{ runtime.onlineAccountCount }} / {{ runtime.webSocketConnectionCount }}</b><span>账号 / 连接</span></article><article><small>房间 / 对局</small><b>{{ runtime.roomCount }} / {{ runtime.activeGameCount }}</b><span>房间 / 进行中</span></article><article><small>卡牌数据</small><b>{{ runtime.cardCount }}</b><span>当前加载卡牌</span></article><article><small>卡图 CDN</small><b>{{ runtime.cdn.state }}</b><span>{{ runtime.cdn.configured ? '已配置' : '未配置' }} · {{ runtime.cdn.detail || runtime.cdn.name }}</span></article></div>
      </section>
      <section class="panel history-panel">
        <header><div><h2>配置版本历史</h2><p>当前由 {{ updatedBy || '系统' }} 于 {{ updatedAt ? new Date(updatedAt).toLocaleString() : '未知时间' }} 更新。每次应用和回滚均保存完整快照。</p></div></header>
        <article v-for="item in history" :key="item.id"><span><b>v{{ item.version }} · {{ item.action }}</b><small>{{ item.actorName }} · {{ new Date(item.createdAt).toLocaleString() }}</small></span><p>{{ item.reason || '无备注' }}</p><button :disabled="item.version === version" @click="rollback(item)">回滚到此版本</button></article>
        <span v-if="!history.length">暂无配置历史</span>
      </section>
    </template>
  </div>
</template>

<style scoped>
.operations-workbench{display:grid;grid-template-columns:1fr;gap:14px}.operations-header{display:flex;align-items:flex-end;justify-content:space-between;border:1px solid #4a4030;background:linear-gradient(110deg,#14130f,#171b1f);padding:20px}.operations-header h2{margin:3px 0;font-size:24px}.operations-header p{margin:0;color:#8d989e;font-size:11px}.operations-header>div>small{color:#c8a84f;letter-spacing:.16em}.operations-version{display:grid;grid-template-columns:auto auto;gap:3px 10px;align-items:center;text-align:right}.operations-version span,.operations-version small{color:#89959a;font-size:10px}.operations-version b{color:#efd16f;font-size:20px}.operations-version button{grid-column:1/-1}.load-error{display:grid;grid-template-columns:auto 1fr auto;gap:12px;align-items:center;margin:0;border:1px solid #9c3e47;background:#2a1014;padding:12px;color:#ffc8ce}.load-error span{font-size:11px}.operations-nav{display:grid;grid-template-columns:repeat(3,1fr);gap:8px}.operations-nav button{display:flex;min-height:66px;flex-direction:column;gap:5px;align-items:flex-start;border:1px solid #354249;background:#0c1318;padding:12px;color:#d8e0e2;text-align:left}.operations-nav button small{color:#77858b;font-size:9px}.operations-nav button.active{border-color:#c29c3d;background:linear-gradient(120deg,#2c2512,#13191d);color:#f5d775}.panel{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel p,.panel span{color:#87949a;font-size:11px}.panel button,.panel select,.panel input,.panel textarea,.operations-header button,.load-error button{box-sizing:border-box;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.runtime-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:9px;margin-top:14px}.runtime-grid article{display:flex;flex-direction:column;gap:5px;padding:13px;border:1px solid #34424a;background:#0b1218}.runtime-grid small{color:#8c999f}.runtime-grid b{font-size:18px}.version-badge{padding:6px 9px;border:1px solid #b7953f;color:#e6ca77!important}.config-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:14px}.config-grid fieldset{display:grid;grid-template-columns:1fr 1fr;gap:10px;align-content:start;border:1px solid #334049;padding:14px}.config-grid legend{padding:0 6px;color:#e0c36e;font-weight:900}.config-grid label{display:flex;flex-direction:column;gap:6px;color:#b5bfc3;font-size:10px}.config-grid .wide{grid-column:1/-1}.locked-note{color:#e3c76e!important}.field-help,.contract-note{margin:0;color:#8f9da3!important}.toggle-row{display:flex!important;flex-direction:row!important;align-items:center;justify-content:space-between;padding:8px;border:1px solid #2f3b42}.toggle-row span{display:flex;flex-direction:column}.toggle-row input{width:auto}.config-actions{display:grid;grid-template-columns:1fr auto auto;gap:8px;margin-top:14px}.confirm{border-color:#b9953f!important;background:#2c2411!important;color:#f0d582!important}.preview-box{margin-top:12px;padding:12px;border:1px solid #866f35;background:#1f1a0d}.preview-box li,.preview-box p{font-size:10px}.history-panel>article{display:grid;grid-template-columns:1fr auto;gap:8px;padding:12px 0;border-bottom:1px solid #303c43}.history-panel>article span{display:flex;flex-direction:column}.history-panel>article p{grid-column:1/-1;margin:0}.history-panel button{grid-row:1;grid-column:2}.history-panel small{color:#748087}.panel button:disabled{cursor:not-allowed;opacity:.45}
@media(max-width:1100px){.operations-nav,.config-grid{grid-template-columns:1fr 1fr}.runtime-grid{grid-template-columns:repeat(2,1fr)}}@media(max-width:650px){.operations-header{align-items:flex-start;flex-direction:column;gap:12px}.operations-version{text-align:left}.operations-nav,.runtime-grid,.config-grid,.config-grid fieldset,.config-actions{grid-template-columns:1fr}.config-grid .wide{grid-column:auto}.load-error{grid-template-columns:1fr}.operations-nav button{min-height:56px}}
.ranked-tier-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:8px}.ranked-tier-grid article{display:flex;flex-direction:column;gap:6px;padding:10px;border:1px solid #303c43;background:#0a1117}.ranked-tier-grid article>b{color:#e1c36d}.ranked-master-title-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.ranked-master-title-grid label{display:grid;grid-template-columns:minmax(0,1fr) minmax(150px,1fr);align-items:center;gap:8px;padding:9px;border:1px solid #303c43;background:#0a1117}.ranked-master-title-grid label span,.ranked-master-title-grid label small{display:block}.ranked-master-title-grid label small{margin-top:2px;color:#69777d;font:700 9px monospace}.broadcast-row{display:grid;grid-template-columns:1fr auto;align-items:center;gap:8px;padding:8px;border-bottom:1px solid #303c43}.broadcast-row span,.broadcast-row small{display:block}.broadcast-row small{margin-top:4px;color:#69777d}@media(max-width:1200px){.ranked-master-title-grid{grid-template-columns:1fr 1fr}}@media(max-width:1000px){.ranked-tier-grid{grid-template-columns:1fr 1fr}}@media(max-width:760px){.ranked-master-title-grid{grid-template-columns:1fr}.ranked-master-title-grid label{grid-template-columns:1fr}}
</style>
