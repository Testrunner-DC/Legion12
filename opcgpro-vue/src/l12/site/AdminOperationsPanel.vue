<script setup lang="ts">
import PagedCollection from './PagedCollection.vue'
import { computed, onMounted, reactive, ref, shallowRef, watch } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import ImmediateMaintenancePanel from './ImmediateMaintenancePanel.vue'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import SeasonConfigurationEditor from './SeasonConfigurationEditor.vue'
import SeasonActivationManagement from './SeasonActivationManagement.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
import {
  freezeOperationsConfigPreview,
  operationsConfigPreviewMatches,
  operationsConfigPreviewSubmission,
  type FrozenOperationsConfigPreview,
} from './operationsConfigPreview'
import {
  freezeSeasonDefinitionPreview,
  seasonDefinitionFingerprint,
  seasonDefinitionPreviewMatches,
  seasonDefinitionPreviewSubmission,
  type FrozenSeasonDefinitionPreview,
} from './seasonDefinitionPreview'
import {
  adminApi,
  hasPermission,
  rankedApi,
  type OperationsConfigPayload,
  type OperationsConfigPreview,
  type OperationsConfigVersion,
  type RuntimeStatus,
  type RankedBroadcast,
  PlatformRequestError,
  type SeasonCatalog,
  type SeasonDefinitionDraft,
  type SeasonDefinitionPreview,
  type SeasonDefinitionView,
} from '@/l12/platform'

const emit = defineEmits<{ notice: [message: string] }>()
const loading = ref(false)
const version = ref(0)
const versionId = ref('')
const updatedBy = ref('')
const updatedAt = ref('')
const preview = ref<OperationsConfigPreview | null>(null)
const previewGuard = shallowRef<FrozenOperationsConfigPreview | null>(null)
const history = ref<OperationsConfigVersion[]>([])
const runtime = ref<RuntimeStatus | null>(null)
const reason = ref('')
const catalog = ref<DeckCard[]>([])
const presetDecks = ref('')
const featureFlags = ref('')
type OperationsSection = 'season' | 'ranked' | 'construction' | 'room' | 'features' | 'announcements' | 'maintenance' | 'versions'
const activeSection = ref<OperationsSection>('season')
const loadError = ref('')
const sections: Array<{ id: OperationsSection; title: string; summary: string }> = [
  { id: 'season', title: '赛季与天灾', summary: '赛季周期、赛季天灾池与堙灭锁定' },
  { id: 'ranked', title: '排位与七曜', summary: '派系、五段位、七曜结算、称号与广播' },
  { id: 'construction', title: '构筑规则', summary: '禁限卡与新账号默认预组' },
  { id: 'room', title: '对战与房间', summary: '模式开关与默认房间规则' },
  { id: 'features', title: '功能开关', summary: '大厅、沙盒、观战、赛事等模块' },
  { id: 'announcements', title: '长期公告', summary: '大厅固定公告、顺序与生效时间' },
  { id: 'maintenance', title: '维护与启服', summary: '维护窗口、结束时间与显式启服' },
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
  announcements: [],
})
type SeasonSlot = 'current' | 'next'
type SeasonSection = 'season' | 'ranked' | 'construction'
interface SeasonSubmitIntent { fingerprint: string; key: string }
interface SeasonSlotState {
  definition: SeasonDefinitionView
  draft: SeasonDefinitionDraft
  baselineFingerprint: string
  operationsVersion: number
  rawStartsAt?: string | null
  rawEndsAt?: string | null
  displayedStartsAt?: string | null
  displayedEndsAt?: string | null
  reason: string
  preview: SeasonDefinitionPreview | null
  guard: FrozenSeasonDefinitionPreview | null
  error: string
  remote: SeasonDefinitionView | null
  remoteMissing: boolean
  submitIntent?: SeasonSubmitIntent
}
const seasonCatalog = ref<SeasonCatalog | null>(null)
const currentSeason = ref<SeasonSlotState | null>(null)
const nextSeason = ref<SeasonSlotState | null>(null)
const selectedSeasonSlot = ref<SeasonSlot>('current')
const createDraftReason = ref('')
let draftLifecycleIntent: SeasonSubmitIntent | undefined
const rankedBroadcasts = ref<RankedBroadcast[]>([])
const startingServer = ref(false)
const loadedMaintenanceEnabled = ref(false)
const canWrite = computed(() => hasPermission('admin.operations.write'))
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()

const observedAt = computed(() => runtime.value ? new Date(runtime.value.observedAt).toLocaleString() : '未加载')
const httpBudgetState = computed(() => {
  const performance = runtime.value?.httpPerformance
  if (!performance?.sampleSufficient) return '采样不足'
  return performance.withinBudget ? '预算内' : '超出预算'
})
const previewMatchesForm = computed(() => {
  if (!preview.value?.valid || !previewGuard.value) return false
  try { return operationsConfigPreviewMatches(previewGuard.value, serialize()) }
  catch { return false }
})
const isSeasonSection = computed(() => ['season', 'ranked', 'construction'].includes(activeSection.value))
const selectedSeasonState = computed(() => selectedSeasonSlot.value === 'current' ? currentSeason.value : nextSeason.value)
const selectedSeasonPreviewMatches = computed(() => {
  const state = selectedSeasonState.value
  return Boolean(state?.guard && state.preview?.valid
    && seasonDefinitionPreviewMatches(state.guard, serializeSeasonDraft(state)))
})

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
function toLocalDateTimeInput(value?: string | null) {
  if (!value) return undefined
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return undefined
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}
function toIsoDateTime(value?: string | null) {
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
  copy.announcements ??= []
  copy.season.startsAt = toLocalDateTimeInput(copy.season.startsAt)
  copy.season.endsAt = toLocalDateTimeInput(copy.season.endsAt)
  copy.maintenance.startsAt = toLocalDateTimeInput(copy.maintenance.startsAt)
  copy.maintenance.endsAt = toLocalDateTimeInput(copy.maintenance.endsAt)
  copy.announcements = (copy.announcements ?? []).map(item => ({ ...item,
    startsAt: toLocalDateTimeInput(item.startsAt), endsAt: toLocalDateTimeInput(item.endsAt),
  }))
  Object.assign(form.season, copy.season)
  Object.assign(form.disasterPool, copy.disasterPool)
  form.cardRestrictions.splice(0, form.cardRestrictions.length, ...copy.cardRestrictions)
  form.defaultPresetDeckIds.splice(0, form.defaultPresetDeckIds.length, ...copy.defaultPresetDeckIds)
  form.matchModes.splice(0, form.matchModes.length, ...copy.matchModes)
  Object.assign(form.defaultRoomConfig, copy.defaultRoomConfig)
  form.featureFlags = copy.featureFlags
  Object.assign(form.maintenance, copy.maintenance)
  form.announcements.splice(0, form.announcements.length, ...copy.announcements)
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
    announcements: form.announcements.map((item, index) => ({ ...item, sortOrder: index,
      startsAt: toIsoDateTime(item.startsAt), endsAt: toIsoDateTime(item.endsAt) })),
  }
}
function displaySeasonDraft(source: SeasonDefinitionView | SeasonDefinitionDraft): SeasonDefinitionDraft {
  return {
    seasonId: source.seasonId,
    name: source.name,
    startsAt: toLocalDateTimeInput(source.startsAt),
    endsAt: toLocalDateTimeInput(source.endsAt),
    configuration: JSON.parse(JSON.stringify(source.configuration)),
  }
}
function setSeasonAuthority(state: SeasonSlotState, source: SeasonDefinitionView | SeasonDefinitionDraft) {
  const draft = displaySeasonDraft(source)
  state.draft = draft
  state.rawStartsAt = source.startsAt
  state.rawEndsAt = source.endsAt
  state.displayedStartsAt = draft.startsAt
  state.displayedEndsAt = draft.endsAt
}
function serializeSeasonDraft(state: SeasonSlotState): SeasonDefinitionDraft {
  const current = state.definition.lifecycleStatus === 'active'
  const startsAt = current || state.draft.startsAt === state.displayedStartsAt
    ? state.rawStartsAt : toIsoDateTime(state.draft.startsAt)
  const endsAt = state.draft.endsAt === state.displayedEndsAt
    ? state.rawEndsAt : toIsoDateTime(state.draft.endsAt)
  return {
    seasonId: current ? state.definition.seasonId : state.draft.seasonId,
    name: state.draft.name,
    startsAt,
    endsAt,
    configuration: JSON.parse(JSON.stringify(state.draft.configuration)),
  }
}
function makeSeasonState(definition: SeasonDefinitionView, operationsVersion: number): SeasonSlotState {
  const draft = displaySeasonDraft(definition)
  const state: SeasonSlotState = {
    definition, draft, baselineFingerprint: '', operationsVersion,
    rawStartsAt: definition.startsAt, rawEndsAt: definition.endsAt,
    displayedStartsAt: draft.startsAt, displayedEndsAt: draft.endsAt,
    reason: '', preview: null, guard: null, error: '', remote: null, remoteMissing: false,
  }
  state.baselineFingerprint = seasonDefinitionFingerprint(serializeSeasonDraft(state))
  return state
}
function seasonStateDirty(state: SeasonSlotState) {
  return Boolean(state.reason.trim())
    || state.baselineFingerprint !== seasonDefinitionFingerprint(serializeSeasonDraft(state))
}
function reconcileSeasonSlot(slot: SeasonSlot, incoming: SeasonDefinitionView | undefined,
  operationsVersion: number, discard = false) {
  const target = slot === 'current' ? currentSeason : nextSeason
  const existing = target.value
  if (!incoming) {
    if (discard || !existing || !seasonStateDirty(existing)) target.value = null
    else {
      existing.remote = null
      existing.remoteMissing = true
      existing.error = '服务器已删除下赛季草稿，本地编辑已保留（含未保存内容与理由）。'
      existing.preview = null
      existing.guard = null
    }
    return
  }
  if (!existing || discard || !seasonStateDirty(existing)) {
    target.value = makeSeasonState(incoming, operationsVersion)
    return
  }
  const identityChanged = existing.definition.definitionId !== incoming.definitionId
  if (identityChanged || existing.definition.revision !== incoming.revision
      || existing.operationsVersion !== operationsVersion) {
    existing.remote = incoming
    existing.remoteMissing = false
    existing.error = identityChanged
      ? `服务器已用新定义 ${incoming.definitionId} 替换此槽位，本地编辑仍属于旧定义 ${existing.definition.definitionId}，已保留并停止提交。`
      : `服务器版本已变化（定义 r${incoming.revision} / 运营 v${operationsVersion}），本地编辑已保留（含未保存内容与理由）。`
    existing.preview = null
    existing.guard = null
  }
}
function invalidateSeasonPreview(state: SeasonSlotState | null) {
  if (state?.guard && !seasonDefinitionPreviewMatches(state.guard, serializeSeasonDraft(state))) {
    state.preview = null
    state.guard = null
    state.submitIntent = undefined
  }
}
watch(() => currentSeason.value?.draft, () => invalidateSeasonPreview(currentSeason.value), { deep: true })
watch(() => nextSeason.value?.draft, () => invalidateSeasonPreview(nextSeason.value), { deep: true })
function newIdempotencyKey() {
  return globalThis.crypto?.randomUUID?.() ?? `season-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
}
function seasonIntentKey(state: SeasonSlotState, fingerprint: string) {
  if (state.submitIntent?.fingerprint !== fingerprint)
    state.submitIntent = { fingerprint, key: newIdempotencyKey() }
  return state.submitIntent.key
}
function lifecycleIntentKey(fingerprint: string) {
  if (draftLifecycleIntent?.fingerprint !== fingerprint)
    draftLifecycleIntent = { fingerprint, key: newIdempotencyKey() }
  return draftLifecycleIntent.key
}
function seasonError(error: unknown, fallback: string) {
  return error instanceof PlatformRequestError && error.status === 409
    ? `${error.message}。本地编辑已保留（含未保存内容与理由），请刷新查看冲突版本，或明确丢弃本地编辑。`
    : error instanceof Error ? error.message : fallback
}
async function captureSeasonConflict(state: SeasonSlotState, error: unknown) {
  state.error = seasonError(error, '赛季定义冲突')
  state.preview = null
  state.guard = null
  if (error instanceof PlatformRequestError && error.status === 409) {
    try {
      const fresh = await adminApi.seasonCatalog()
      seasonCatalog.value = fresh
      const incoming = state.definition.lifecycleStatus === 'active' ? fresh.current : fresh.next
      state.remote = incoming ?? null
      state.remoteMissing = !incoming
    } catch {
      state.remote = state.definition
    }
  }
}
async function refreshSeasonAuthority() {
  try {
    const fresh = await adminApi.seasonCatalog()
    seasonCatalog.value = fresh
    reconcileSeasonSlot('current', fresh.current, fresh.operationsVersion)
    reconcileSeasonSlot('next', fresh.next, fresh.operationsVersion)
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : '赛季权威状态刷新失败'
  }
}
async function previewSeasonDefinition() {
  const state = selectedSeasonState.value
  if (!canWrite.value || !state) return
  const draft = serializeSeasonDraft(state)
  const requestFingerprint = seasonDefinitionFingerprint(draft)
  state.preview = null
  state.guard = null
  state.error = ''
  state.submitIntent = undefined
  try {
    const result = await adminApi.previewSeasonDefinition(state.definition.definitionId, draft,
      state.definition.revision, state.operationsVersion)
    if (seasonDefinitionFingerprint(serializeSeasonDraft(state)) !== requestFingerprint) {
      state.error = '预览返回前草稿已继续编辑，本次迟到响应已忽略，请重新预览。'
      return
    }
    state.preview = result
    if (result.valid) {
      state.guard = freezeSeasonDefinitionPreview(result)
      setSeasonAuthority(state, result.normalized)
    }
    emit('notice', result.valid ? `预览通过：${result.changes.length} 项赛季定义变更` : '赛季定义预览未通过')
  } catch (error) { await captureSeasonConflict(state, error) }
}
function applySeasonDefinition() {
  const state = selectedSeasonState.value
  if (!canWrite.value || !state) return
  const applyReason = state.reason.trim()
  if (!applyReason) { emit('notice', '保存赛季定义前请填写变更理由'); return }
  if (!state.guard || !state.preview?.valid || !selectedSeasonPreviewMatches.value) {
    emit('notice', '赛季定义已变化或尚未通过预览，请重新预览'); return
  }
  const submission = seasonDefinitionPreviewSubmission(state.guard)
  const key = seasonIntentKey(state, `${state.guard.fingerprint}|${state.guard.previewToken}|${applyReason}`)
  requestRiskAction({
    title: selectedSeasonSlot.value === 'current' ? '应用当前赛季配置' : '保存下赛季草稿',
    target: `${state.definition.seasonId} · ${selectedSeasonSlot.value} · r${state.definition.revision}/v${state.operationsVersion}`,
    targetLabel: '目标 / 槽位 / 版本',
    impact: `提交冻结预览；${selectedSeasonSlot.value === 'current' ? '立即影响之后创建的对局' : '仅保存草稿，不切换赛季'}。理由：${applyReason}`,
    confirmLabel: '确认保存', severity: 'warning', run: async () => {
      try {
        const result = await adminApi.applySeasonDefinition(state.definition.definitionId, submission.draft,
          submission.expectedRevision, submission.expectedVersion, submission.previewToken, applyReason, key)
        state.submitIntent = undefined
        emit('notice', `${result.slot === 'current' ? '当前赛季' : '下赛季草稿'} r${result.definition.revision} 已保存`)
        await load(result.slot)
      } catch (error) { await captureSeasonConflict(state, error); throw new Error(state.error) }
    },
  })
}
function createNextSeasonDraft() {
  const current = seasonCatalog.value?.current
  const expectedVersion = seasonCatalog.value?.operationsVersion
  const applyReason = createDraftReason.value.trim()
  if (!current || expectedVersion === undefined || !applyReason) { emit('notice', '创建草稿前请填写理由'); return }
  const key = lifecycleIntentKey(`create|${current.revision}|${expectedVersion}|${applyReason}`)
  requestRiskAction({ title: '创建下赛季草稿',
    target: `${current.seasonId} · next · r${current.revision}/v${expectedVersion}`,
    targetLabel: '来源 / 槽位 / 版本', impact: `由服务器创建真实草稿，不静默复制当前配置。理由：${applyReason}`,
    confirmLabel: '确认创建草稿', severity: 'warning', run: async () => {
      await adminApi.createSeasonDraft(current.revision, expectedVersion, applyReason, key)
      draftLifecycleIntent = undefined
      createDraftReason.value = ''
      await load('next')
    },
  })
}
function deleteNextSeasonDraft() {
  const state = nextSeason.value
  const applyReason = state?.reason.trim()
  if (!state || !applyReason) { emit('notice', '删除草稿前请填写理由'); return }
  const key = lifecycleIntentKey(`delete|${state.definition.definitionId}|${state.definition.revision}|${state.operationsVersion}|${applyReason}`)
  requestRiskAction({ title: '删除下赛季草稿',
    target: `${state.definition.seasonId} · next · r${state.definition.revision}/v${state.operationsVersion}`,
    targetLabel: '目标 / 槽位 / 版本', impact: `只删除草稿，当前赛季不受影响。理由：${applyReason}`,
    confirmLabel: '确认删除', run: async () => {
      await adminApi.deleteSeasonDraft(state.definition.definitionId, state.definition.revision,
        state.operationsVersion, applyReason, key)
      draftLifecycleIntent = undefined
      await load('next')
    },
  })
}
async function load(discardSlot?: SeasonSlot) {
  loading.value = true
  loadError.value = ''
  preview.value = null
  previewGuard.value = null
  try {
    const [current, versions, status] = await Promise.all([
      adminApi.operationsConfig(), adminApi.operationsHistory(), adminApi.runtimeStatus(),
    ])
    const [cards, broadcasts] = await Promise.all([
      loadDeckCatalog(), rankedApi.broadcasts(30),
    ])
    version.value = current.version
    versionId.value = current.versionId
    updatedBy.value = current.updatedBy
    updatedAt.value = current.updatedAt
    history.value = versions
    runtime.value = status
    catalog.value = cards
    rankedBroadcasts.value = broadcasts
    hydrate(current.config)
    loadedMaintenanceEnabled.value = current.config.maintenance.enabled
    try {
      const seasons = await adminApi.seasonCatalog()
      seasonCatalog.value = seasons
      reconcileSeasonSlot('current', seasons.current, seasons.operationsVersion, discardSlot === 'current')
      reconcileSeasonSlot('next', seasons.next, seasons.operationsVersion, discardSlot === 'next')
    } catch (error) {
      loadError.value = error instanceof Error ? error.message : '赛季定义加载失败'
    }
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
function toggleMaintenanceEnd(event: Event) {
  form.maintenance.endsAt = (event.target as HTMLInputElement).checked
    ? undefined
    : toLocalDateTimeInput(new Date(Date.now() + Math.max(1, form.maintenance.expectedDurationHours) * 60 * 60 * 1000).toISOString())
}
function addAnnouncement() {
  const id = globalThis.crypto?.randomUUID?.() ?? `announcement-${Date.now().toString(36)}`
  form.announcements.push({ id, content: '', enabled: false, sortOrder: form.announcements.length })
}
function moveAnnouncement(index: number, direction: -1 | 1) {
  const target = index + direction
  if (target < 0 || target >= form.announcements.length) return
  const [item] = form.announcements.splice(index, 1)
  if (item) form.announcements.splice(target, 0, item)
}
function removeAnnouncement(index: number) { form.announcements.splice(index, 1) }
async function performStartServer() {
  if (!canWrite.value) return
  if (!reason.value.trim()) { emit('notice', '启服前请填写变更理由'); return }
  startingServer.value = true
  try {
    const result = await adminApi.startServer(reason.value.trim(), version.value)
    emit('notice', result.current.immediateMaintenance?.enabled
      ? '预约维护已解除；即时维护仍在生效，新对局尚未开放。请在上方结束即时维护。'
      : result.alreadyStarted ? '预约维护未开启，未重复变更配置' : '预约维护已解除，新对局门禁已开放')
    reason.value = ''
    await load()
  } catch (error) { emit('notice', error instanceof Error ? error.message : '启服失败'); throw error }
  finally { startingServer.value = false }
}
function startServer() {
  if (!canWrite.value) return
  if (!reason.value.trim()) { emit('notice', '启服前请填写变更理由'); return }
  requestRiskAction({ title: '解除预约维护', target: `运营配置 v${version.value}`, targetLabel: '当前版本', impact: '预约维护门禁会被解除；若即时维护未开启，新对局入口将立即恢复。', confirmLabel: '确认启动服务器', severity: 'warning', run: performStartServer })
}
async function previewChanges() {
  if (!canWrite.value) return
  preview.value = null
  previewGuard.value = null
  try {
    const result = await adminApi.previewOperationsConfig(serialize(), version.value)
    preview.value = result
    if (result.valid) {
      previewGuard.value = freezeOperationsConfigPreview(result)
      hydrate(previewGuard.value.snapshot)
    }
    emit('notice', result.valid ? `预览通过：${result.changes.length} 项变更` : '预览未通过，请检查警告')
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置预览失败') }
}
async function performApplyChanges(config: OperationsConfigPayload, applyReason: string, expectedVersion: number) {
  if (!canWrite.value) return
  try {
    const result = await adminApi.applyOperationsConfig(config, applyReason, expectedVersion)
    emit('notice', result.applied ? `运营配置 v${result.current.version} 已保存并写入审计` : '运营配置未发生变更')
    reason.value = ''
    preview.value = null
    previewGuard.value = null
    await load()
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置应用失败'); throw error }
}
function applyChanges() {
  if (!canWrite.value) return
  const applyReason = reason.value.trim()
  if (!applyReason) { emit('notice', '应用配置前请填写变更理由'); return }
  const guard = previewGuard.value
  if (!preview.value?.valid || !guard || !previewMatchesForm.value) {
    emit('notice', '配置已变化或尚未通过预览，请重新预览后再应用'); return
  }
  const submission = operationsConfigPreviewSubmission(guard)
  requestRiskAction({ title: '应用运营配置', target: `v${guard.currentVersion} → v${guard.nextVersion}`, targetLabel: '配置版本', impact: `${preview.value.changes.length} 项已冻结预览将立即生效；进行中对局继续使用创建时规则。`, confirmLabel: '确认应用配置', run: () => performApplyChanges(submission.config, applyReason, submission.expectedVersion) })
}
async function performRollback(target: OperationsConfigVersion) {
  if (!canWrite.value) return
  if (!reason.value.trim()) { emit('notice', '回滚配置前请填写变更理由'); return }
  try {
    await adminApi.rollbackOperationsConfig(target.id, reason.value.trim(), version.value)
    emit('notice', `已回滚至运营配置 v${target.version}`)
    reason.value = ''
    preview.value = null
    await load()
  } catch (error) { emit('notice', error instanceof Error ? error.message : '运营配置回滚失败'); throw error }
}
function rollback(target: OperationsConfigVersion) {
  if (!canWrite.value) return
  if (!reason.value.trim()) { emit('notice', '回滚配置前请填写变更理由'); return }
  requestRiskAction({ title: '回滚运营配置', target: `v${target.version} · ${target.id}`, targetLabel: '目标版本', impact: '系统将以该历史快照生成新的生效版本，不会覆盖历史记录。', confirmLabel: '确认回滚', run: () => performRollback(target) })
}
function deleteBroadcast(id: string) {
  if (!canWrite.value) return
  const broadcast = rankedBroadcasts.value.find(item => item.id === id)
  requestRiskAction({ title: '删除排位快讯', target: broadcast?.message || id, targetLabel: '快讯', impact: '该快讯会从待播放与历史展示中删除，不能在后台恢复。', confirmLabel: '确认删除', run: async () => { await adminApi.deleteRankedBroadcast(id); rankedBroadcasts.value = rankedBroadcasts.value.filter(item => item.id !== id) } })
}

onMounted(load)
</script>

<template>
  <div class="operations-workbench">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :target-label="riskAction.targetLabel" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction"/>
    <header class="operations-header">
      <div><small>GAME OPERATIONS</small><h2>游戏运营</h2><p>配置保存后由构筑、房间与对战服务按版本读取。进行中的对局保持创建时规则。</p></div>
      <div class="operations-version"><span>当前生效</span><b>v{{ version }}</b><small>{{ versionId || '等待加载' }}</small><button :disabled="loading" @click="load()">{{ loading ? '加载中' : '刷新' }}</button></div>
    </header>
    <p v-if="loadError" class="load-error"><b>运营配置加载失败</b><span>{{ loadError }}</span><button @click="load()">重新加载</button></p>
    <nav class="operations-nav" aria-label="运营配置分组">
      <button v-for="item in sections" :key="item.id" :class="{ active: activeSection === item.id }" @click="activeSection = item.id"><b>{{ item.title }}</b><small>{{ item.summary }}</small></button>
    </nav>

    <section v-if="activeSection !== 'versions'" class="panel config-panel">
      <header><div><h2>{{ currentSection.title }}</h2><p>{{ currentSection.summary }}</p></div><span class="version-badge">配置 v{{ version }}</span></header>
      <nav v-if="isSeasonSection" class="season-slot-switcher" aria-label="赛季配置槽位">
        <button type="button" :class="{ active: selectedSeasonSlot === 'current' }" @click="selectedSeasonSlot = 'current'"><b>当前赛季</b><small>{{ currentSeason?.definition.seasonId || '未加载' }} · 立即生效</small></button>
        <button type="button" :class="{ active: selectedSeasonSlot === 'next' }" @click="selectedSeasonSlot = 'next'"><b>下赛季草稿</b><small>{{ nextSeason?.definition.seasonId || '空槽' }} · 不立即生效</small></button>
      </nav>
      <div v-if="isSeasonSection && selectedSeasonState" class="season-slot-meta">
        <span class="slot-badge">{{ selectedSeasonSlot === 'current' ? '当前生效槽' : '下赛季草稿槽' }}</span>
        <b>{{ selectedSeasonState.definition.seasonId }} · {{ selectedSeasonState.definition.name }}</b>
        <small>定义 r{{ selectedSeasonState.definition.revision }} · 运营 v{{ selectedSeasonState.operationsVersion }} · {{ seasonStateDirty(selectedSeasonState) ? '本地有未保存内容' : '已同步' }}</small>
        <button v-if="selectedSeasonState.remote || selectedSeasonState.remoteMissing" type="button" @click="load(selectedSeasonSlot)">刷新并丢弃本地编辑</button>
        <button v-if="selectedSeasonSlot === 'next'" type="button" :disabled="!canWrite" @click="deleteNextSeasonDraft">删除下赛季草稿</button>
      </div>
      <p v-if="isSeasonSection && selectedSeasonState?.error" class="slot-error" role="alert">{{ selectedSeasonState.error }}</p>
      <SeasonActivationManagement v-if="activeSection === 'season'" :catalog="seasonCatalog"
        :can-write="canWrite" :configuration-dirty="nextSeason ? seasonStateDirty(nextSeason) : false"
        @notice="emit('notice', $event)" @authority-conflict="refreshSeasonAuthority"
        @changed="refreshSeasonAuthority"/>
      <fieldset class="operations-write-scope" :disabled="!canWrite">
      <ImmediateMaintenancePanel v-if="activeSection === 'maintenance'"/>
      <div class="config-grid section-grid">
        <SeasonConfigurationEditor v-if="isSeasonSection && selectedSeasonState" v-model="selectedSeasonState.draft" class="wide"
          :slot="selectedSeasonSlot" :section="activeSection as SeasonSection" :cards="catalog" :broadcasts="rankedBroadcasts"
          @delete-broadcast="deleteBroadcast"/>
        <div v-else-if="isSeasonSection && selectedSeasonSlot === 'next'" class="wide empty-next" data-ui-contract="empty-next-season">
          <b>尚未创建下赛季草稿</b><p>创建动作由服务器生成真实初始草稿，不会在浏览器中静默复制当前赛季。</p>
          <input v-model="createDraftReason" placeholder="创建草稿理由（必填）"/><button class="confirm" type="button" @click="createNextSeasonDraft">创建下赛季草稿</button>
        </div>
        <template v-else-if="activeSection === 'room'">
          <fieldset><legend>允许的对战模式</legend><article v-for="mode in form.matchModes" :key="mode.id" class="toggle-row"><span><b>{{ mode.name }}</b><small>{{ mode.id }}</small></span><input v-model="mode.enabled" type="checkbox"/></article><span v-if="!form.matchModes.length">暂无服务端定义的模式</span></fieldset>
          <fieldset class="room-defaults"><legend>默认房间配置</legend><label>默认模式<select v-model="form.defaultRoomConfig.matchModeId"><option v-for="mode in form.matchModes" :key="mode.id" :value="mode.id">{{ mode.name }} · {{ mode.enabled ? '启用' : '停用' }}</option></select></label><label>观战权限<select v-model="form.defaultRoomConfig.spectating"><option value="public">允许所有玩家观战</option><option value="friends">仅好友观战</option><option value="disabled">禁止观战</option></select></label><label>观战手牌<select v-model="form.defaultRoomConfig.handVisibility"><option value="request">查看前申请</option><option value="public">默认公开</option></select></label><label>天灾模式<select v-model="form.defaultRoomConfig.disasterMode"><option value="all">全部天灾</option><option value="random">随机天灾</option><option value="season">赛季天灾</option><option value="none">不使用天灾</option></select></label><p class="wide contract-note">这里定义新房间的初始值；房主修改后的配置仍须通过当前模式和维护策略校验。</p></fieldset>
        </template>
        <fieldset v-else-if="activeSection === 'announcements'" class="wide announcement-editor" data-ui-contract="independent-long-term-announcements"><legend>大厅长期公告</legend><p class="wide field-help">独立于维护提示。启用且处于有效时间的公告按顺序固定显示在大厅“更换牌库”区域上方；不设置时间表示长期有效。</p><button class="wide" type="button" @click="addAnnouncement">＋ 新增公告</button><article v-for="(item,index) in form.announcements" :key="item.id" class="wide announcement-row"><header><b>公告 {{ index + 1 }}</b><span><button type="button" :disabled="index === 0" @click="moveAnnouncement(index,-1)">上移</button><button type="button" :disabled="index === form.announcements.length - 1" @click="moveAnnouncement(index,1)">下移</button><button type="button" @click="removeAnnouncement(index)">删除</button></span></header><label class="wide">内容<textarea v-model="item.content" rows="3" placeholder="输入长期公告内容"/></label><label>开始时间（可选）<input v-model="item.startsAt" type="datetime-local"/></label><label>结束时间（可选）<input v-model="item.endsAt" type="datetime-local"/></label><label class="toggle-row wide"><span><b>启用此公告</b><small>空内容或无效时间范围无法保存。</small></span><input v-model="item.enabled" type="checkbox"/></label></article><span v-if="!form.announcements.length" class="wide">暂无长期公告。</span></fieldset>
        <fieldset v-else-if="activeSection === 'features'" class="wide"><legend>模块功能开关</legend><p class="field-help">格式：key=true/false。关闭后前端入口会灰置，服务端仍进行权威校验。</p><label class="wide"><textarea v-model="featureFlags" rows="16"/></label></fieldset>
        <fieldset v-else-if="activeSection === 'maintenance'" class="wide"><legend>预约维护计划</legend><label class="toggle-row"><span><b>启用维护计划</b><small>到达开始前1小时自动关闭所有新开局入口。</small></span><input v-model="form.maintenance.enabled" type="checkbox"/></label><label class="wide">维护提示<textarea v-model="form.maintenance.message" rows="4" placeholder="启用维护时必填"/></label><label>开始时间<input v-model="form.maintenance.startsAt" type="datetime-local"/></label><label>结束时间（可选）<input v-model="form.maintenance.endsAt" type="datetime-local" :disabled="!form.maintenance.endsAt"/></label><label class="toggle-row wide"><span><b>不设置结束时间</b><small>预约维护持续到管理员点击下方解除按钮。</small></span><input type="checkbox" :checked="!form.maintenance.endsAt" @change="toggleMaintenanceEnd"/></label><label>提前广播（小时）<input v-model.number="form.maintenance.advanceBroadcastHours" type="number" min="1" max="168"/></label><label>预计维护时长（小时）<input v-model.number="form.maintenance.expectedDurationHours" type="number" min="1" max="168"/></label><p class="wide contract-note">下方按钮只解除预约维护，不会结束上方即时维护，也不提交本页其他未保存编辑；重复点击不会再次增加配置版本。两种维护均解除后才开放新对局。</p><button class="confirm wide" type="button" :disabled="startingServer || !loadedMaintenanceEnabled" data-ui-contract="idempotent-server-start" @click="startServer">{{ startingServer ? '正在解除…' : loadedMaintenanceEnabled ? '启动服务器（解除预约维护）' : '预约维护未开启' }}</button></fieldset>
      </div>
      <footer v-if="isSeasonSection && selectedSeasonState" class="config-actions"><input v-model="selectedSeasonState.reason" placeholder="此槽位的变更理由（必填）"/><button @click="previewSeasonDefinition">预览赛季定义</button><button class="confirm" @click="applySeasonDefinition">{{ selectedSeasonSlot === 'current' ? '保存当前配置' : '保存下赛季草稿' }}</button></footer>
      <footer v-else-if="!isSeasonSection" class="config-actions"><input v-model="reason" placeholder="变更或回滚理由（必填）"/><button @click="previewChanges">预览差异</button><button class="confirm" @click="applyChanges">保存配置</button></footer>
      <div v-if="isSeasonSection && selectedSeasonState?.preview" class="preview-box"><b>{{ selectedSeasonState.preview.valid ? (selectedSeasonPreviewMatches ? '预览通过' : '配置已编辑，请重新预览') : '预览未通过' }} · {{ selectedSeasonState.preview.slot }} r{{ selectedSeasonState.preview.currentRevision }} → r{{ selectedSeasonState.preview.nextRevision }} · v{{ selectedSeasonState.preview.operationsVersion }}</b><ul><li v-for="item in selectedSeasonState.preview.changes" :key="item">{{ item }}</li></ul><p v-for="item in selectedSeasonState.preview.warnings" :key="item">警告：{{ item }}</p></div>
      <div v-else-if="!isSeasonSection && preview" class="preview-box"><b>{{ preview.valid ? (previewMatchesForm ? '预览通过' : '配置已编辑，请重新预览') : '预览未通过' }} · v{{ preview.currentVersion }} → v{{ preview.nextVersion }}</b><ul><li v-for="item in preview.changes" :key="item">{{ item }}</li></ul><p v-for="item in preview.warnings" :key="item">警告：{{ item }}</p></div>
      </fieldset>
    </section>

    <template v-else>
      <section class="panel runtime-panel">
        <header><div><h2>后端运行状态</h2><p>运行探针只在此处展示，不与运营配置字段混排。</p></div><button :disabled="loading" @click="load()">刷新</button></header>
        <div v-if="runtime" class="runtime-grid"><article><small>服务版本</small><b>{{ runtime.serviceVersion }}</b><span>{{ observedAt }}</span></article><article><small>在线账号 / WS</small><b>{{ runtime.onlineAccountCount }} / {{ runtime.webSocketConnectionCount }}</b><span>账号 / 连接</span></article><article><small>房间 / 对局</small><b>{{ runtime.roomCount }} / {{ runtime.activeGameCount }}</b><span>房间 / 进行中</span></article><article><small>卡牌数据</small><b>{{ runtime.cardCount }}</b><span>当前加载卡牌</span></article><article><small>卡图 CDN</small><b>{{ runtime.cdn.state }}</b><span>{{ runtime.cdn.configured ? '已配置' : '未配置' }} · {{ runtime.cdn.detail || runtime.cdn.name }}</span></article><article class="http-budget" :class="{ failed: runtime.httpPerformance.withinBudget === false }"><small>HTTP 低卡顿预算</small><b>{{ httpBudgetState }}</b><span>最近 {{ runtime.httpPerformance.windowSeconds }} 秒 · {{ runtime.httpPerformance.sampleCount }} 个有效样本</span></article><article><small>样本构成</small><b>{{ runtime.httpPerformance.readSampleCount }} / {{ runtime.httpPerformance.mutationSampleCount }}</b><span>读取 / 写入 · 状态探针 {{ runtime.httpPerformance.diagnosticRequestCount }}</span></article><article><small>应用延迟</small><b>{{ runtime.httpPerformance.averageDurationMilliseconds }} ms</b><span>P95 {{ runtime.httpPerformance.p95LatencyBand }}</span></article><article><small>在途 / 峰值</small><b>{{ runtime.httpPerformance.inFlight }} / {{ runtime.httpPerformance.peakInFlight }}</b><span>当前 / 最近一分钟峰值</span></article><article><small>慢请求</small><b>{{ runtime.httpPerformance.slowRequestCount }}</b><span>{{ runtime.httpPerformance.slowRequestPercent }}% · 超过 {{ runtime.httpPerformance.slowRequestThresholdMilliseconds }} ms</span></article><article><small>429 / 5xx</small><b>{{ runtime.httpPerformance.rateLimitedCount }} / {{ runtime.httpPerformance.serverErrorCount }}</b><span>{{ runtime.httpPerformance.rateLimitedPercent }}% / {{ runtime.httpPerformance.serverErrorPercent }}%</span></article><article><small>计划性 503 / 客户端断开</small><b>{{ runtime.httpPerformance.expectedUnavailableCount }} / {{ runtime.httpPerformance.clientCancelledCount }}</b><span>独立观察，不计入 5xx 故障率</span></article></div>
      </section>
      <section class="panel history-panel">
        <header><div><h2>配置版本历史</h2><p>当前由 {{ updatedBy || '系统' }} 于 {{ updatedAt ? new Date(updatedAt).toLocaleString() : '未知时间' }} 更新。每次应用和回滚均保存完整快照。</p></div></header>
        <PagedCollection :items="history" v-slot="{ items: paged27988 }"><article v-for="item in paged27988" :key="item.id"><span><b>v{{ item.version }} · {{ item.action }}</b><small>{{ item.actorName }} · {{ new Date(item.createdAt).toLocaleString() }}</small></span><p>{{ item.reason || '无备注' }}</p><button :disabled="!canWrite || item.version === version" @click="rollback(item)">回滚到此版本</button></article></PagedCollection>
        <span v-if="!history.length">暂无配置历史</span>
      </section>
    </template>
  </div>
</template>

<style scoped>
.operations-write-scope{min-width:0;max-width:100%;margin:0;padding:0;border:0}.operations-write-scope:disabled{opacity:.72}
.season-slot-switcher{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;margin-top:14px}.season-slot-switcher button{display:flex;min-width:0;min-height:52px;flex-direction:column;align-items:flex-start;gap:4px;white-space:normal;overflow-wrap:anywhere}.season-slot-switcher button.active{border-color:#c29c3d;background:#2c2512;color:#f5d775}.season-slot-switcher small{color:#7f8b91}.season-slot-meta{display:flex;min-width:0;align-items:center;flex-wrap:wrap;gap:10px;margin-top:10px;padding:10px;border:1px solid #35424a;overflow-wrap:anywhere}.season-slot-meta small{margin-right:auto}.slot-badge{padding:5px 8px;border:1px solid #b7953f;color:#e6ca77!important}.slot-error,.empty-next{margin-top:10px;padding:12px;border:1px solid #9c3e47;background:#2a1014;color:#ffc8ce;overflow-wrap:anywhere}.empty-next{display:grid;gap:10px}.empty-next input{width:100%}
.ranked-shared-tiers{display:grid;grid-template-columns:1fr;gap:16px}.ranked-shared-tiers article{min-width:0;padding:18px;border:1px solid #44515b;background:#0a1117}.ranked-shared-tiers h3{margin:0 0 14px;font-size:18px}.tier-name-fields,.tier-number-fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:12px;margin-top:12px}.ranked-shared-tiers input{width:100%;min-width:0}
.operations-workbench{box-sizing:border-box;display:grid;min-width:0;max-width:100%;grid-template-columns:minmax(0,1fr);gap:14px}.operations-header,.operations-header>div,.operations-version,.operations-nav,.panel,.config-grid,.config-grid fieldset,.runtime-grid,.config-actions{min-width:0;max-width:100%}.operations-header{box-sizing:border-box;display:flex;align-items:flex-end;justify-content:space-between;border:1px solid #4a4030;background:linear-gradient(110deg,#14130f,#171b1f);padding:20px}.operations-header h2{margin:3px 0;font-size:24px}.operations-header p{margin:0;color:#8d989e;font-size:14px;overflow-wrap:anywhere}.operations-header>div>small{color:#c8a84f;letter-spacing:.16em}.operations-version{display:grid;grid-template-columns:auto auto;gap:3px 10px;align-items:center;text-align:right}.operations-version span,.operations-version small{color:#89959a;font-size:14px}.operations-version b{color:#efd16f;font-size:20px}.operations-version button{grid-column:1/-1}.load-error{display:grid;min-width:0;grid-template-columns:auto minmax(0,1fr) auto;gap:12px;align-items:center;margin:0;border:1px solid #9c3e47;background:#2a1014;padding:12px;color:#ffc8ce}.load-error span{font-size:14px}.operations-nav{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.operations-nav button{display:flex;min-width:0;min-height:66px;flex-direction:column;gap:5px;align-items:flex-start;border:1px solid #354249;background:#0c1318;padding:12px;color:#d8e0e2;text-align:left;white-space:normal;overflow-wrap:anywhere}.operations-nav button small{color:#77858b;font-size:14px}.operations-nav button.active{border-color:#c29c3d;background:linear-gradient(120deg,#2c2512,#13191d);color:#f5d775}.panel{box-sizing:border-box;border:1px solid #35424a;background:#101821;padding:20px}.panel>header{display:flex;min-width:0;align-items:center;justify-content:space-between;border-bottom:1px solid #36434a;padding-bottom:13px}.panel h2{margin:0}.panel p,.panel span{color:#87949a;font-size:14px}.panel button,.panel select,.panel input,.panel textarea,.operations-header button,.load-error button{box-sizing:border-box;max-width:100%;border:1px solid #4c5961;background:#080e13;color:#fff;font:700 14px 'Microsoft YaHei';padding:9px}.config-grid input,.config-grid select,.config-grid textarea{width:100%;min-width:0}.runtime-grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:9px;margin-top:14px}.runtime-grid article{display:flex;min-width:0;flex-direction:column;gap:5px;padding:13px;border:1px solid #34424a;background:#0b1218}.runtime-grid small{color:#8c999f}.runtime-grid b{font-size:18px;overflow-wrap:anywhere}.version-badge{padding:6px 9px;border:1px solid #b7953f;color:#e6ca77!important}.config-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:14px}.config-grid fieldset{box-sizing:border-box;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;align-content:start;border:1px solid #334049;padding:14px}.config-grid legend{max-width:100%;padding:0 6px;color:#e0c36e;font-weight:900;white-space:normal}.config-grid label{display:flex;min-width:0;flex-direction:column;gap:6px;color:#b5bfc3;font-size:14px}.config-grid .wide{grid-column:1/-1}.locked-note{color:#e3c76e!important}.field-help,.contract-note{margin:0;color:#8f9da3!important}.toggle-row{display:flex!important;min-width:0;flex-direction:row!important;align-items:center;justify-content:space-between;padding:8px;border:1px solid #2f3b42}.toggle-row span{display:flex;min-width:0;flex-direction:column}.toggle-row input{width:auto}.config-actions{display:grid;grid-template-columns:minmax(0,1fr) auto auto;gap:8px;margin-top:14px}.config-actions button{min-height:44px}.confirm{border-color:#b9953f!important;background:#2c2411!important;color:#f0d582!important}.preview-box{margin-top:12px;padding:12px;border:1px solid #866f35;background:#1f1a0d}.preview-box li,.preview-box p{font-size:14px}.history-panel>article{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:8px;padding:12px 0;border-bottom:1px solid #303c43}.history-panel>article span{display:flex;min-width:0;flex-direction:column}.history-panel>article p{grid-column:1/-1;margin:0}.history-panel button{grid-row:1;grid-column:2}.history-panel small{color:#748087}.panel button:disabled{cursor:not-allowed;opacity:.45}
@media(max-width:1100px){.operations-nav,.config-grid{grid-template-columns:1fr 1fr}.runtime-grid{grid-template-columns:repeat(2,1fr)}}@media(max-width:650px){.operations-header{align-items:flex-start;flex-direction:column;gap:12px}.operations-version{text-align:left}.operations-nav,.runtime-grid,.config-grid,.config-grid fieldset,.config-actions{grid-template-columns:1fr}.config-grid .wide{grid-column:auto}.load-error{grid-template-columns:1fr}.operations-nav button{min-height:56px}}
.ranked-tier-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:8px}.ranked-tier-grid article{display:flex;flex-direction:column;gap:6px;padding:10px;border:1px solid #303c43;background:#0a1117}.ranked-tier-grid article>b{color:#e1c36d}.ranked-master-title-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:8px}.ranked-master-title-grid label{display:grid;grid-template-columns:minmax(0,1fr) minmax(150px,1fr);align-items:center;gap:8px;padding:9px;border:1px solid #303c43;background:#0a1117}.ranked-master-title-grid label span,.ranked-master-title-grid label small{display:block}.ranked-master-title-grid label small{margin-top:2px;color:#69777d;font:700 14px monospace}.broadcast-row{display:grid;grid-template-columns:1fr auto;align-items:center;gap:8px;padding:8px;border-bottom:1px solid #303c43}.broadcast-row span,.broadcast-row small{display:block}.broadcast-row small{margin-top:4px;color:#69777d}@media(max-width:1200px){.ranked-master-title-grid{grid-template-columns:1fr 1fr}}@media(max-width:1000px){.ranked-tier-grid{grid-template-columns:1fr 1fr}}@media(max-width:760px){.ranked-master-title-grid{grid-template-columns:1fr}.ranked-master-title-grid label{grid-template-columns:1fr}}
.ranked-time-control label{min-width:0}.ranked-time-control label small{color:#78868c;line-height:1.45}.ranked-time-control input{min-width:0;width:100%}
.announcement-editor>button{border-style:dashed!important}.announcement-row{display:grid;grid-template-columns:1fr 1fr;gap:9px;padding:12px;border:1px solid #35424a;background:#0a1117}.announcement-row>header{grid-column:1/-1;display:flex;align-items:center;justify-content:space-between}.announcement-row>header span{display:flex;gap:5px}.announcement-row>header button{padding:6px}.announcement-row .wide{grid-column:1/-1}
.runtime-grid .http-budget{border-color:#59633d}.runtime-grid .http-budget.failed{border-color:#9c3e47;background:#2a1014}
@media(max-width:1100px){.config-grid{grid-template-columns:1fr}.config-grid fieldset{min-width:0}.config-grid .wide{grid-column:1/-1}}
@media(max-width:650px){.panel{padding:14px}.config-grid fieldset{grid-template-columns:1fr;padding:10px}.config-grid .wide{grid-column:auto}.config-actions button{width:100%}.announcement-row{grid-template-columns:1fr}.announcement-row>header,.announcement-row .wide{grid-column:auto}.announcement-row>header{align-items:flex-start;flex-direction:column;gap:8px}.announcement-row>header span{width:100%;flex-wrap:wrap}.announcement-row>header button{flex:1}.toggle-row{align-items:flex-start;gap:8px}.operations-version{grid-template-columns:minmax(0,1fr) auto}.operations-version button{width:100%}}
@media(max-width:650px){.season-slot-switcher{grid-template-columns:1fr}.season-slot-meta{align-items:stretch;flex-direction:column}.season-slot-meta button{width:100%}}
@container (max-width:650px){.config-grid :deep(.pool-picker){min-width:0;max-width:100%}.config-grid :deep(.pool-picker > header){min-width:0;flex-wrap:wrap}.config-grid :deep(.pool-picker > header input){width:100%;min-width:0;flex-basis:100%}.config-grid :deep(.pool-picker > header span){max-width:100%;white-space:normal;overflow-wrap:anywhere}.config-grid :deep(.pool-grid){min-width:0;max-width:100%;grid-template-columns:repeat(auto-fill,minmax(min(100%,132px),1fr))}}
</style>
