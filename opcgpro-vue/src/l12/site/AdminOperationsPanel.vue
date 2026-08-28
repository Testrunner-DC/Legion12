<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  adminApi,
  type OperationsCardRestriction,
  type OperationsConfigPayload,
  type OperationsConfigPreview,
  type OperationsConfigVersion,
  type RuntimeStatus,
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
const disasterCards = ref('')
const restrictions = ref('')
const presetDecks = ref('')
const featureFlags = ref('')
const form = reactive<OperationsConfigPayload>({
  season: { id: '', name: '', status: 'upcoming' },
  disasterPool: { cardIds: [], annihilationLocked: true },
  cardRestrictions: [],
  defaultPresetDeckIds: [],
  matchModes: [],
  featureFlags: {},
  maintenance: { enabled: false, message: '' },
})

const observedAt = computed(() => runtime.value ? new Date(runtime.value.observedAt).toLocaleString() : '未加载')

function lines(value: string) {
  return value.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
}
function parseRestrictions(value: string): OperationsCardRestriction[] {
  return lines(value).map((line, index) => {
    const [cardId, copies = '3', ...rest] = line.split('|').map(item => item.trim())
    if (!cardId) throw new Error(`禁限卡第 ${index + 1} 行缺少卡号`)
    const maxCopies = Number(copies)
    if (!Number.isInteger(maxCopies) || maxCopies < 0 || maxCopies > 3) throw new Error(`禁限卡第 ${index + 1} 行数量须为 0 至 3`)
    return { cardId, maxCopies, reason: rest.join('|') || undefined }
  })
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
  disasterCards.value = form.disasterPool.cardIds.join('\n')
  restrictions.value = form.cardRestrictions.map(item => `${item.cardId}|${item.maxCopies}${item.reason ? `|${item.reason}` : ''}`).join('\n')
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
  form.featureFlags = copy.featureFlags
  Object.assign(form.maintenance, copy.maintenance)
  syncTextFields()
}
function serialize(): OperationsConfigPayload {
  return {
    season: { ...form.season, startsAt: toIsoDateTime(form.season.startsAt), endsAt: toIsoDateTime(form.season.endsAt) },
    disasterPool: { cardIds: lines(disasterCards.value), annihilationLocked: true },
    cardRestrictions: parseRestrictions(restrictions.value),
    defaultPresetDeckIds: lines(presetDecks.value),
    matchModes: form.matchModes.map(item => ({ ...item })),
    featureFlags: parseFlags(featureFlags.value),
    maintenance: { ...form.maintenance, startsAt: toIsoDateTime(form.maintenance.startsAt), endsAt: toIsoDateTime(form.maintenance.endsAt) },
  }
}
async function load() {
  loading.value = true
  try {
    const [current, versions, status] = await Promise.all([
      adminApi.operationsConfig(), adminApi.operationsHistory(), adminApi.runtimeStatus(),
    ])
    version.value = current.version
    versionId.value = current.versionId
    updatedBy.value = current.updatedBy
    updatedAt.value = current.updatedAt
    history.value = versions
    runtime.value = status
    hydrate(current.config)
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置加载失败') }
  finally { loading.value = false }
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

onMounted(load)
</script>

<template>
  <div class="operations-workbench">
    <section class="panel runtime-panel">
      <header><div><h2>运行状态</h2><p>仅展示后端能够权威读取的实时状态；缺少探针时明确标记为不可用。</p></div><button :disabled="loading" @click="load">刷新</button></header>
      <div v-if="runtime" class="runtime-grid">
        <article><small>服务版本</small><b>{{ runtime.serviceVersion }}</b><span>{{ observedAt }}</span></article>
        <article><small>在线账号 / WS</small><b>{{ runtime.onlineAccountCount }} / {{ runtime.webSocketConnectionCount }}</b><span>账号 / 连接</span></article>
        <article><small>房间 / 对局</small><b>{{ runtime.roomCount }} / {{ runtime.activeGameCount }}</b><span>房间 / 进行中</span></article>
        <article><small>卡牌数据</small><b>{{ runtime.cardCount }}</b><span>当前加载卡牌</span></article>
        <article><small>卡图 CDN</small><b>{{ runtime.cdn.state }}</b><span>{{ runtime.cdn.configured ? '已配置' : '未配置' }} · {{ runtime.cdn.detail || runtime.cdn.name }}</span></article>
      </div>
    </section>

    <section class="panel config-panel">
      <header><div><h2>游戏运营配置</h2><p>当前 v{{ version }} · {{ versionId }} · {{ updatedBy || '系统' }} {{ updatedAt ? new Date(updatedAt).toLocaleString() : '' }}</p></div><span class="version-badge">版本化配置</span></header>
      <div class="config-grid">
        <fieldset><legend>赛季</legend><label>赛季 ID<input v-model="form.season.id"/></label><label>名称<input v-model="form.season.name"/></label><label>状态<select v-model="form.season.status"><option value="upcoming">待开始</option><option value="active">进行中</option><option value="archived">已归档</option></select></label><label>开始时间<input v-model="form.season.startsAt" type="datetime-local"/></label><label>结束时间<input v-model="form.season.endsAt" type="datetime-local"/></label></fieldset>
        <fieldset><legend>天灾池</legend><label class="wide">每行一个卡号；堙灭由服务端强制置于最后<textarea v-model="disasterCards" rows="8"/></label><span>堙灭锁定：开启（不可关闭）</span></fieldset>
        <fieldset><legend>禁限卡</legend><label class="wide">格式：卡号 | 上限(0-3) | 原因<textarea v-model="restrictions" rows="8"/></label></fieldset>
        <fieldset><legend>默认预组</legend><label class="wide">每行一个牌库 ID<textarea v-model="presetDecks" rows="8"/></label></fieldset>
        <fieldset><legend>对战模式</legend><article v-for="mode in form.matchModes" :key="mode.id" class="toggle-row"><span><b>{{ mode.name }}</b><small>{{ mode.id }}</small></span><input v-model="mode.enabled" type="checkbox"/></article><span v-if="!form.matchModes.length">暂无服务端定义的模式</span></fieldset>
        <fieldset><legend>功能开关</legend><label class="wide">格式：key=true/false<textarea v-model="featureFlags" rows="8"/></label></fieldset>
        <fieldset class="wide"><legend>维护公告</legend><label class="toggle-row">保存维护启用配置<input v-model="form.maintenance.enabled" type="checkbox"/></label><label class="wide">公告<textarea v-model="form.maintenance.message" rows="3"/></label><label>开始时间<input v-model="form.maintenance.startsAt" type="datetime-local"/></label><label>结束时间<input v-model="form.maintenance.endsAt" type="datetime-local"/></label></fieldset>
      </div>
      <footer class="config-actions"><input v-model="reason" placeholder="变更或回滚理由（必填）"/><button @click="previewChanges">预览差异</button><button class="confirm" @click="applyChanges">保存配置</button></footer>
      <div v-if="preview" class="preview-box"><b>{{ preview.valid ? '预览通过' : '预览未通过' }} · v{{ preview.currentVersion }} → v{{ preview.nextVersion }}</b><ul><li v-for="item in preview.changes" :key="item">{{ item }}</li></ul><p v-for="item in preview.warnings" :key="item">警告：{{ item }}</p></div>
    </section>

    <section class="panel history-panel">
      <header><div><h2>配置历史</h2><p>每次应用和回滚均保存操作者、理由、版本与完整快照。</p></div></header>
      <article v-for="item in history" :key="item.id"><span><b>v{{ item.version }} · {{ item.action }}</b><small>{{ item.actorName }} · {{ new Date(item.createdAt).toLocaleString() }}</small></span><p>{{ item.reason || '无备注' }}</p><button :disabled="item.version === version" @click="rollback(item)">回滚到此版本</button></article>
      <span v-if="!history.length">暂无配置历史</span>
    </section>
  </div>
</template>

<style scoped>
.operations-workbench{display:grid;grid-template-columns:1.45fr .75fr;gap:14px}.panel{border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel p,.panel span{color:#87949a;font-size:11px}.panel button,.panel select,.panel input,.panel textarea{box-sizing:border-box;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 11px 'Microsoft YaHei';padding:9px}.runtime-panel{grid-column:1/-1}.runtime-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:9px;margin-top:14px}.runtime-grid article{display:flex;flex-direction:column;gap:5px;padding:13px;border:1px solid #34424a;background:#0b1218}.runtime-grid small{color:#8c999f}.runtime-grid b{font-size:18px}.config-panel{grid-column:1/2}.version-badge{padding:6px 9px;border:1px solid #b7953f;color:#e6ca77!important}.config-grid{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:14px}.config-grid fieldset{display:grid;grid-template-columns:1fr 1fr;gap:10px;border:1px solid #334049;padding:14px}.config-grid legend{padding:0 6px;color:#e0c36e;font-weight:900}.config-grid label{display:flex;flex-direction:column;gap:6px;color:#b5bfc3;font-size:10px}.config-grid .wide{grid-column:1/-1}.toggle-row{display:flex!important;flex-direction:row!important;align-items:center;justify-content:space-between;padding:8px;border:1px solid #2f3b42}.toggle-row span{display:flex;flex-direction:column}.toggle-row input{width:auto}.config-actions{display:grid;grid-template-columns:1fr auto auto;gap:8px;margin-top:14px}.confirm{border-color:#b9953f!important;background:#2c2411!important;color:#f0d582!important}.preview-box{margin-top:12px;padding:12px;border:1px solid #866f35;background:#1f1a0d}.preview-box li,.preview-box p{font-size:10px}.history-panel{grid-column:2/3}.history-panel>article{display:grid;grid-template-columns:1fr auto;gap:8px;padding:12px 0;border-bottom:1px solid #303c43}.history-panel>article span{display:flex;flex-direction:column}.history-panel>article p{grid-column:1/-1;margin:0}.history-panel button{grid-row:1;grid-column:2}.history-panel small{color:#748087}.panel button:disabled{cursor:not-allowed;opacity:.45}
@media(max-width:1100px){.operations-workbench,.config-grid{grid-template-columns:1fr}.config-panel,.history-panel{grid-column:1}.runtime-grid{grid-template-columns:repeat(2,1fr)}}@media(max-width:650px){.runtime-grid,.config-grid fieldset,.config-actions{grid-template-columns:1fr}.config-grid .wide{grid-column:auto}}
</style>
