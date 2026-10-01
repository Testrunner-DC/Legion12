<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { adminApi, hasPermission, type ContentBatch, type ContentEntry, type SiteMedia } from '@/l12/platform'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import MediaUploadField from './MediaUploadField.vue'
import AdminRiskActionDialog from './AdminRiskActionDialog.vue'
import { useAdminRiskAction } from './useAdminRiskAction'
import { cardProductsForIds, displayCardNumber, loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { LEGACY_FAQ_SOURCES, RULE_TOPIC_DEFINITIONS, coreRuleBlockId, createRuleCenterDraft, createRulingsDraft, mergedRulings, parsePublishedRuleCenter, parseRulingDocument, serializeRulingDocument, withPendingRulingSeeds, type RuleCenterDocument, type RuleRuling, type RuleTopicId } from '@/l12/data/ruleCenterData'

type Workspace = 'drafts' | 'sources' | 'published' | 'history'
type PublicationState = 'draft' | 'changed' | 'scheduled' | 'published' | 'superseded'

const emit = defineEmits<{ notice: [value: string] }>()
const { riskAction, riskBusy, riskError, requestRiskAction, cancelRiskAction, confirmRiskAction } = useAdminRiskAction()
const workspace = ref<Workspace>('drafts')
const entries = ref<RuleRuling[]>([])
const ruleCenterDocument = ref<RuleCenterDocument>(createRuleCenterDraft())
const ruleCenterDraft = computed(() => JSON.stringify(ruleCenterDocument.value, null, 2))
const publishedRulings = ref<RuleRuling[]>([])
const publishedCenter = ref<RuleCenterDocument>(parsePublishedRuleCenter('{}'))
const rulingVersion = ref(0)
const centerVersion = ref(0)
const history = ref<ContentBatch[]>([])
const pickerTarget = ref<RuleRuling | null>(null)
const cardCatalog = ref<DeckCard[]>([])
const detailCard = ref<DeckCard | null>(null)
const ruleMedia = ref<SiteMedia[]>([])
const openRulingEditors = ref<Set<string>>(new Set())
const busy = ref(false)
const publishingId = ref('')
const selectedSource = ref('')
const sourceQuery = ref('')
const sourceStates = { 'pending-review': '待梳理', replaced: '已替代', 'conflict-flagged': '发现冲突' } as const
const workspaceLabels: Record<Workspace, string> = {
  drafts: '待审核 / 草稿', sources: '来源', published: '已发布', history: '历史与已替代',
}
const stateLabels: Record<PublicationState, string> = {
  draft: '待审核草稿', changed: '已发布后修改', scheduled: '已审核 · 定时生效',
  published: '已生效', superseded: '已替代',
}
const visibleSources = computed(() => LEGACY_FAQ_SOURCES.filter(source => !sourceQuery.value.trim()
  || [source.id, source.question, source.answer].join(' ').includes(sourceQuery.value.trim())))
const reviewedSources = computed(() => new Set(mergedRulings(entries.value).flatMap(item => [...item.sourceIds, ...item.supersedes])))
const reviewCount = computed(() => LEGACY_FAQ_SOURCES.filter(source => source.reviewState === 'replaced' || reviewedSources.value.has(source.id)).length)
const supersededIds = computed(() => new Set([...entries.value, ...publishedRulings.value].flatMap(item => item.supersedes)))
const ruleHistory = computed(() => history.value.filter(batch => batch.action === 'rule-item-publish'))
const canDraft = computed(() => hasPermission('admin.content.draft'))
const cardById = computed(() => new Map(cardCatalog.value.map(card => [card.id.toLocaleLowerCase(), card])))
const centerCollectionDefinitions = [
  { id: 'coreBlocks', label: '核心规则子板块' }, { id: 'quickStart', label: '快速入门' },
  { id: 'terms', label: '术语' }, { id: 'tournament', label: '赛事规则' }, { id: 'versions', label: '版本记录' },
] as const

function findPublishedRuling(item: RuleRuling) {
  return publishedRulings.value.find(row => row.id.toLocaleLowerCase() === item.id.toLocaleLowerCase())
}
function rulingState(item: RuleRuling): PublicationState {
  if (item.status === 'superseded' || supersededIds.value.has(item.id)) return 'superseded'
  const live = findPublishedRuling(item)
  if (!live) return 'draft'
  if (JSON.stringify(live) !== JSON.stringify(item)) return 'changed'
  if (live.effectiveAt && new Date(live.effectiveAt).getTime() > Date.now()) return 'scheduled'
  return 'published'
}
function findPublishedCenter(collection: string, itemId: string) {
  const document = publishedCenter.value as unknown as Record<string, Array<Record<string, unknown> & { id?: string }>>
  return document[collection]?.find(item => item.id?.toLocaleLowerCase() === itemId.toLocaleLowerCase())
}
function centerState(collection: string, itemId: string, draft: Record<string, unknown>): PublicationState {
  if (draft.status === 'superseded') return 'superseded'
  const live = findPublishedCenter(collection, itemId)
  if (!live) return 'draft'
  if (draft.status === 'pending') return 'changed'
  const draftDocument = ruleCenterDocument.value as unknown as Record<string, Array<Record<string, unknown>>>
  const publishedDocument = publishedCenter.value as unknown as Record<string, Array<Record<string, unknown>>>
  const liveIds = new Set((publishedDocument[collection] || []).map(item => String(item.id || '').toLocaleLowerCase()))
  const draftOrder = (draftDocument[collection] || []).map(item => String(item.id || '').toLocaleLowerCase()).filter(id => liveIds.has(id))
  const publishedOrder = (publishedDocument[collection] || []).map(item => String(item.id || '').toLocaleLowerCase())
  if (draftOrder.indexOf(itemId.toLocaleLowerCase()) !== publishedOrder.indexOf(itemId.toLocaleLowerCase())) return 'changed'
  const comparable = (value: Record<string, unknown>) => { const copy = { ...value }; delete copy.status; return copy }
  if (JSON.stringify(comparable(live)) !== JSON.stringify(comparable(draft))) return 'changed'
  const effectiveAt = typeof live.effectiveAt === 'string' ? live.effectiveAt : ''
  if (effectiveAt && new Date(effectiveAt).getTime() > Date.now()) return 'scheduled'
  return 'published'
}
const centerItems = computed(() => {
  const document = ruleCenterDocument.value as unknown as Record<string, Array<Record<string, any>>>
  return ['coreBlocks', 'quickStart', 'terms', 'tournament', 'versions'].flatMap(collection =>
    (Array.isArray(document[collection]) ? document[collection] : []).filter(item => item.id).map((item, index) => ({
      collection, row: item, id: String(item.id), index,
      title: String(item.title || item.topic || item.chapter || item.id),
      state: centerState(collection, String(item.id), item),
    })))
})
const rulingItems = computed(() => entries.value.map((item, index) => ({ item, index, state: rulingState(item) })))
const activeCenterItems = computed(() => centerItems.value.filter(item => workspace.value === 'drafts'
  ? item.state === 'draft' || item.state === 'changed' : item.state === 'published' || item.state === 'scheduled'))
const activeRulingItems = computed(() => rulingItems.value.filter(item => workspace.value === 'drafts'
  ? item.state === 'draft' || item.state === 'changed' : item.state === 'published' || item.state === 'scheduled'))
const supersededCenterItems = computed(() => centerItems.value.filter(item => item.state === 'superseded'))
const supersededRulingItems = computed(() => rulingItems.value.filter(item => item.state === 'superseded'))
const activeCenterGroups = computed(() => centerCollectionDefinitions.map(group => ({ ...group,
  items: activeCenterItems.value.filter(item => item.collection === group.id),
})).filter(group => group.items.length))
const workspaceCounts = computed<Record<Workspace, number>>(() => ({
  drafts: centerItems.value.filter(item => item.state === 'draft' || item.state === 'changed').length
    + rulingItems.value.filter(item => item.state === 'draft' || item.state === 'changed').length,
  sources: LEGACY_FAQ_SOURCES.length,
  published: centerItems.value.filter(item => item.state === 'published' || item.state === 'scheduled').length
    + rulingItems.value.filter(item => item.state === 'published' || item.state === 'scheduled').length,
  history: supersededCenterItems.value.length + supersededRulingItems.value.length + ruleHistory.value.length,
}))

function notice(value: string) { emit('notice', value) }
function split(value: string) { return value.split(',').map(item => item.trim()).filter(Boolean) }
function join(value: string[]) { return value.join(', ') }
function toggleTopic(item: RuleRuling, topic: RuleTopicId) {
  item.topics = item.topics.includes(topic) ? item.topics.filter(value => value !== topic) : [...item.topics, topic]
}
function effectiveInput(value?: string) { return value ? value.slice(0, 16) : '' }
function setEffective(item: RuleRuling, value: string) { item.effectiveAt = value ? `${value}:00+08:00` : undefined }
function setCenterEffective(item: Record<string, unknown>, value: string) { item.effectiveAt = value ? `${value}:00+08:00` : undefined }
function normalizeRulingProducts(item: RuleRuling) {
  if (item.scope === 'card' || item.scope === 'errata') item.productIds = cardProductsForIds(item.cardIds)
}
function normalizeAllRulingProducts() { entries.value.forEach(normalizeRulingProducts) }
function linkedCards(item: RuleRuling) {
  return [...new Set(item.cardIds)].map(id => ({ id, card: cardById.value.get(id.toLocaleLowerCase()) }))
    .sort((left, right) => (left.card ? displayCardNumber(left.card) : left.id)
      .localeCompare(right.card ? displayCardNumber(right.card) : right.id, 'zh-CN', { numeric: true }))
}
function rulingHeading(item: RuleRuling) {
  if (item.scope !== 'card' && item.scope !== 'errata') return item.category
  const cards = linkedCards(item)
  if (!cards.length) return '待补关联·裁定'
  return `${cards.map(({ id, card }) => card ? `${displayCardNumber(card)}·${card.nameZh}` : `${id}·待识别卡牌`).join(' / ')}·裁定`
}
function selectCard(card: SingleCardPickerItem) {
  if (pickerTarget.value && !pickerTarget.value.cardIds.includes(card.cardId)) pickerTarget.value.cardIds.push(card.cardId)
  if (pickerTarget.value) normalizeRulingProducts(pickerTarget.value)
  pickerTarget.value = null
}
function removeCard(item: RuleRuling, cardId: string) {
  item.cardIds = item.cardIds.filter(value => value !== cardId)
  normalizeRulingProducts(item)
}
function trackRulingEditor(itemId: string, event: Event) {
  const next = new Set(openRulingEditors.value)
  if ((event.currentTarget as HTMLDetailsElement).open) next.add(itemId)
  else next.delete(itemId)
  openRulingEditors.value = next
}
function historySummary(batch: ContentBatch) {
  const target = batch.sourceBatchId?.split('/').at(-1) || '规则条目'
  const item = batch.items[0]
  if (!item) return `${target} · 无差异资料`
  try {
    const before = JSON.parse(item.previousValue || '{}') as Record<string, unknown>
    const after = JSON.parse(item.publishedValue || '{}') as Record<string, unknown>
    return `${target} · ${JSON.stringify(before) === JSON.stringify(after) ? '快照未变化' : '已记录发布前后完整快照'}`
  } catch { return `${target} · 已记录发布快照` }
}
function historyItem(batch: ContentBatch, value: string) {
  const [, collection, itemId] = (batch.sourceBatchId || '').split('/')
  try {
    const document = JSON.parse(value || '{}') as Record<string, Array<Record<string, unknown>>>
    return document[collection]?.find(item => String(item.id).toLocaleLowerCase() === itemId?.toLocaleLowerCase())
  } catch { return undefined }
}
function historyChanges(batch: ContentBatch) {
  const item = batch.items[0]
  if (!item) return '无可读差异'
  const before = historyItem(batch, item.previousValue)
  const after = historyItem(batch, item.publishedValue)
  if (!before) return '首次发布'
  if (!after) return '条目已从公开快照移除'
  const fields = [...new Set([...Object.keys(before), ...Object.keys(after)])]
    .filter(key => JSON.stringify(before[key]) !== JSON.stringify(after[key]))
  return fields.length ? `变更字段：${fields.join('、')}` : '内容未变化'
}
function historyPreview(batch: ContentBatch, side: 'before' | 'after') {
  const item = batch.items[0]
  const row = item ? historyItem(batch, side === 'before' ? item.previousValue : item.publishedValue) : undefined
  return row ? String(row.question || row.title || row.chapter || row.topic || row.id || '未命名条目') : '无公开版本'
}
function addRuling(sourceId = '') {
  const id = `RULING-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}-${entries.value.length + 1}`
  entries.value.unshift({ id, scope: 'card', question: '', answer: '', category: '单卡裁定',
    sourceKind: 'user-ruling', sourceRef: '待补充确认来源', recordedAt: new Date().toISOString().slice(0, 10),
    status: 'pending', cardIds: [], productIds: [], tags: [], topics: ['effects-stack'],
    sourceIds: sourceId ? [sourceId] : [], supersedes: sourceId ? [sourceId] : [] })
  workspace.value = 'drafts'
}
function startFromSource(sourceId: string) { selectedSource.value = sourceId; addRuling(sourceId) }
function removeRuling(index: number) { entries.value.splice(index, 1) }
function mediaFor(id: unknown) { return ruleMedia.value.find(item => item.id === id) }
function centerCollectionLength(collection: string) {
  const document = ruleCenterDocument.value as unknown as Record<string, unknown[]>
  return document[collection]?.length || 0
}
function ruleMediaUploaded(media: SiteMedia, row: Record<string, any>) {
  ruleMedia.value = [media, ...ruleMedia.value.filter(item => item.id !== media.id)]
  row.mediaAssetId = media.id
}
function applyCenterEntry(entry: ContentEntry) {
  centerVersion.value = entry.version
  ruleCenterDocument.value = JSON.parse(entry.draftValue) as RuleCenterDocument
  publishedCenter.value = parsePublishedRuleCenter(entry.publishedValue)
}
async function addCenterItem(collection: string) {
  if (!canDraft.value) return
  try {
    const saved = await saveCenter('', false)
    if (!saved) return
    const created = await adminApi.createRuleItem(collection, saved.version)
    applyCenterEntry(created)
    const document = ruleCenterDocument.value as unknown as Record<string, Array<Record<string, unknown>>>
    const itemId = String(document[collection]?.at(-1)?.id || '')
    workspace.value = 'drafts'
    notice('已新建规则资料子板块；稳定编号由系统自动分配')
    if (itemId) await revealDraftItem(itemId)
  } catch (error) { notice(error instanceof Error ? error.message : '规则资料子板块新建失败') }
}
async function moveCenterItem(item: { collection: string; id: string; row: Record<string, any> }, direction: -1 | 1) {
  const document = ruleCenterDocument.value as unknown as Record<string, Array<Record<string, any>>>
  const items = document[item.collection] || []
  const index = items.findIndex(row => String(row.id) === item.id)
  const target = index + direction
  if (index < 0 || target < 0 || target >= items.length) return
  if (workspace.value === 'published') item.row.status = 'pending'
  const [moved] = items.splice(index, 1)
  items.splice(target, 0, moved)
  try {
    await saveCenter(`规则资料顺序已保存为草稿；稳定编号 ${item.id} 未改变`)
    await revealDraftItem(item.id)
  } catch (error) { notice(error instanceof Error ? error.message : '规则资料调序失败') }
}
function deleteCenterItem(item: { collection: string; id: string; title: string; state: PublicationState }) {
  const published = item.state !== 'draft'
  requestRiskAction({
    title: published ? '删除已发布规则资料' : '删除规则资料草稿',
    target: item.title,
    impact: published ? '此项会从玩家端公开规则中移除；审计历史保留。' : '此草稿将删除；编号不会复用。',
    confirmLabel: published ? '删除并取消公开' : '删除草稿',
    severity: 'danger',
    run: async () => {
      const deleted = await adminApi.deleteRuleItem(item.collection, item.id, centerVersion.value)
      applyCenterEntry(deleted)
      history.value = await adminApi.contentBatches()
      notice(published ? '规则资料已删除并同步从玩家端移除；审计历史已保留' : '规则资料草稿已删除；稳定编号不会复用')
    },
  })
}
function normalizeList(item: RuleRuling, key: 'tags' | 'sourceIds' | 'supersedes', value: string) { item[key] = split(value) }
async function revealDraftItem(itemId: string) {
  workspace.value = 'drafts'
  await nextTick()
  const editor = document.getElementById(`admin-rule-item-${itemId}`) as HTMLDetailsElement | null
  if (!editor) return
  editor.open = true
  editor.scrollIntoView({ block: 'center' })
}
async function load() {
  busy.value = true
  try {
    const [rulings, center, batches] = await Promise.all([
      adminApi.getContent('rules.rulings'), adminApi.getContent('rules.center'), adminApi.contentBatches(),
    ])
    const media = await adminApi.siteMedia('rule')
    cardCatalog.value = await loadDeckCatalog().catch(() => [] as DeckCard[])
    const parsedRulings = parseRulingDocument(rulings.draftValue)
    entries.value = !parsedRulings.length && !rulings.draftValue.trim() ? createRulingsDraft() : withPendingRulingSeeds(parsedRulings)
    normalizeAllRulingProducts()
    publishedRulings.value = parseRulingDocument(rulings.publishedValue)
    publishedCenter.value = parsePublishedRuleCenter(center.publishedValue)
    rulingVersion.value = rulings.version
    centerVersion.value = center.version
    history.value = batches
    ruleMedia.value = media
    const centerDocument = JSON.parse(center.draftValue.trim() || JSON.stringify(createRuleCenterDraft())) as Record<string, unknown>
    centerDocument.schemaVersion = 2
    const collections = centerDocument as Record<string, Array<Record<string, unknown>>>
    collections.coreBlocks = (collections.coreBlocks || []).map((block, index) => ({ ...block,
      id: typeof block.id === 'string' && block.id ? block.id : coreRuleBlockId(index),
      status: typeof block.status === 'string' ? block.status : 'pending' }))
    for (const collection of ['quickStart', 'terms', 'tournament', 'versions'])
      collections[collection] = (collections[collection] || []).map(item => ({ ...item,
        status: typeof item.status === 'string' ? item.status : 'pending' }))
    ruleCenterDocument.value = centerDocument as unknown as RuleCenterDocument
  } catch (error) { notice(error instanceof Error ? error.message : '裁定草稿读取失败') }
  finally { busy.value = false }
}
async function saveRulings(message = '裁定草稿已保存') {
  normalizeAllRulingProducts()
  const saved = await adminApi.saveContentDraft('rules.rulings', serializeRulingDocument(entries.value), rulingVersion.value)
  rulingVersion.value = saved.version
  notice(message)
}
async function saveCenter(message = '规则资料草稿已保存', show = true) {
  const saved = await adminApi.saveContentDraft('rules.center', ruleCenterDraft.value, centerVersion.value)
  centerVersion.value = saved.version
  if (show && message) notice(message)
  return saved
}
async function save(show = true) {
  try {
    normalizeAllRulingProducts()
    const [rulings, center] = await Promise.all([
      adminApi.saveContentDraft('rules.rulings', serializeRulingDocument(entries.value), rulingVersion.value),
      adminApi.saveContentDraft('rules.center', ruleCenterDraft.value, centerVersion.value),
    ])
    rulingVersion.value = rulings.version
    centerVersion.value = center.version
    if (show) notice('规则中心与裁定草稿已保存；玩家只会看到审核后正式发布的快照')
    return true
  } catch (error) { notice(error instanceof Error ? error.message : '规则草稿保存失败'); return false }
}
async function saveRulingItem(item: RuleRuling) {
  try { await saveRulings(`裁定 ${item.id} 已保存为草稿；公开版本未改变`) }
  catch (error) { notice(error instanceof Error ? error.message : '裁定草稿保存失败') }
}
async function saveCenterItem(itemId: string) {
  try { await saveCenter(`规则资料 ${itemId} 已保存为草稿；公开版本未改变`) }
  catch (error) { notice(error instanceof Error ? error.message : '规则资料草稿保存失败') }
}
async function returnRuling(item: RuleRuling) { item.status = 'pending'; await saveRulingItem(item); await revealDraftItem(item.id) }
async function returnCenter(item: Record<string, unknown>, itemId: string) { item.status = 'pending'; await saveCenterItem(itemId); await revealDraftItem(itemId) }
async function preview() {
  if (await save(false)) try {
    const result = await adminApi.previewContent(['rules.center', 'rules.rulings'])
    notice(`发布预览完成：${result.items.filter(item => item.wouldChange).length} 项将更新；未写入玩家端`)
  } catch (error) { notice(error instanceof Error ? error.message : '发布预览失败') }
}
async function publishRuling(item: RuleRuling) {
  publishingId.value = item.id
  try {
    normalizeAllRulingProducts()
    const saved = await adminApi.saveContentDraft('rules.rulings', serializeRulingDocument(entries.value), rulingVersion.value)
    const published = await adminApi.publishRuleItem('rules.rulings', 'entries', item.id, saved.version)
    rulingVersion.value = published.version
    entries.value = parseRulingDocument(published.draftValue)
    publishedRulings.value = parseRulingDocument(published.publishedValue)
    history.value = await adminApi.contentBatches()
    notice(`裁定 ${item.id} 已单独审核并发布；其他草稿未受影响`)
  } catch (error) { notice(error instanceof Error ? error.message : '裁定逐项发布失败') }
  finally { publishingId.value = '' }
}
async function publishCenterItem(collection: string, itemId: string) {
  publishingId.value = itemId
  try {
    const saved = await adminApi.saveContentDraft('rules.center', ruleCenterDraft.value, centerVersion.value)
    const published = await adminApi.publishRuleItem('rules.center', collection, itemId, saved.version)
    centerVersion.value = published.version
    ruleCenterDocument.value = JSON.parse(published.draftValue) as RuleCenterDocument
    publishedCenter.value = parsePublishedRuleCenter(published.publishedValue)
    history.value = await adminApi.contentBatches()
    notice(`规则中心条目 ${itemId} 已单独审核并发布；其他草稿未受影响`)
  } catch (error) { notice(error instanceof Error ? error.message : '规则条目逐项发布失败') }
  finally { publishingId.value = '' }
}
onMounted(load)
</script>

<template>
  <section class="ruling-admin">
    <AdminRiskActionDialog v-if="riskAction" :title="riskAction.title" :target="riskAction.target" :impact="riskAction.impact" :confirm-label="riskAction.confirmLabel" :severity="riskAction.severity" :busy="riskBusy" :error="riskError" @cancel="cancelRiskAction" @confirm="confirmRiskAction" />
    <header><div><small>RULE CENTER REVIEW</small><h3>规则中心与 FAQ 裁定库</h3><p>每条内容的预览、草稿保存和审核发布集中在同一工作区。玩家只读取已发布快照。</p></div><div class="actions"><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button><button v-if="canDraft" @click="addRuling()">＋ 新建裁定</button><button v-if="canDraft" @click="save()">保存全部草稿</button><button v-if="canDraft" @click="preview">预览全部差异</button></div></header>
    <nav class="workspace-tabs" aria-label="规则审核工作区"><button v-for="(_, id) in workspaceLabels" :key="id" :class="{ active: workspace === id }" @click="workspace = id"><b>{{ workspaceLabels[id] }}</b><span>{{ workspaceCounts[id] }}</span></button></nav>
    <div class="review-summary"><b>规则中心 {{ centerVersion }} · 裁定库 {{ rulingVersion }}</b><span>状态由草稿、已发布快照、生效时间、字段差异和替代关系自动派生。</span></div>

    <section v-if="workspace === 'drafts' || workspace === 'published'" class="workspace-panel">
      <header><div><small>{{ workspace === 'drafts' ? 'DRAFT REVIEW' : 'PUBLISHED' }}</small><h4>{{ workspaceLabels[workspace] }}</h4></div><p>{{ workspace === 'drafts' ? '待审核和已发布后修改的对象在这里处理。' : '当前已生效和已审核待生效的对象在这里查看。' }}</p></header>
      <section v-if="canDraft && workspace === 'drafts'" class="center-create-bar"><span>新建规则资料子板块</span><div><button v-for="group in centerCollectionDefinitions" :key="group.id" type="button" @click="addCenterItem(group.id)">＋ {{ group.label }}</button></div></section>
      <section v-for="group in activeCenterGroups" :key="group.id" class="item-group"><h4>{{ group.label }} · {{ group.items.length }} 项</h4>
        <details v-for="item in group.items" :id="`admin-rule-item-${item.id}`" :key="`${item.collection}-${item.id}`" class="admin-item-card center-editor">
          <summary><span><b>{{ item.title }}</b><small>{{ stateLabels[item.state] }}</small></span><em>{{ workspace === 'published' ? '查看已发布内容' : '查看与操作' }}</em></summary>
          <template v-if="workspace === 'published'"><div class="admin-item-preview published-preview"><small>玩家端预览 · {{ stateLabels[item.state] }}</small><b>{{ item.title }}</b><p>{{ item.row.text || item.row.body || item.row.summary }}</p><span>{{ item.row.effectiveAt ? `${item.state === 'scheduled' ? '计划生效' : '生效时间'}：${new Date(String(item.row.effectiveAt)).toLocaleString('zh-CN')}` : '当前已生效' }}</span></div><div class="item-actions published-actions"><button v-if="canDraft" @click="returnCenter(item.row, item.id)">退回修改</button></div></template>
          <template v-else><div class="admin-item-preview"><small>玩家端预览</small><b>{{ item.title }}</b><p>{{ item.row.text || item.row.body || item.row.summary }}</p><span v-if="item.row.effectiveAt">计划生效：{{ new Date(item.row.effectiveAt).toLocaleString('zh-CN') }}</span></div>
          <fieldset :disabled="!canDraft"><div class="form-grid">
            <label v-if="item.collection === 'coreBlocks'" class="wide">章节<input v-model.trim="item.row.chapter" maxlength="100"></label><label v-else class="wide">标题<input v-model.trim="item.row.title" maxlength="300"></label>
            <label v-if="item.collection === 'coreBlocks'" class="wide">规则正文<textarea v-model.trim="item.row.text" rows="5" maxlength="12000"></textarea></label><label v-else-if="item.collection === 'versions'" class="wide">版本摘要<textarea v-model.trim="item.row.summary" rows="5" maxlength="12000"></textarea></label><label v-else class="wide">正文<textarea v-model.trim="item.row.body" rows="5" maxlength="12000"></textarea></label>
            <div v-if="item.collection === 'coreBlocks'" class="wide rule-media-editor"><span>子板块图片（可选）</span><select v-model="item.row.mediaAssetId"><option value="">不插入图片</option><option v-for="media in ruleMedia" :key="media.id" :value="media.id">{{ media.altText || media.contentHash.slice(0, 12) }}</option></select><img v-if="mediaFor(item.row.mediaAssetId)" :src="mediaFor(item.row.mediaAssetId)?.thumbnailUrl" :alt="mediaFor(item.row.mediaAssetId)?.altText"><details><summary>上传新的规则图片</summary><MediaUploadField kind="rule" :initial-alt="item.row.chapter || '规则示意图'" @uploaded="ruleMediaUploaded($event, item.row)" @notice="notice"/></details></div>
            <label v-if="item.collection !== 'coreBlocks'">来源说明<input v-model.trim="item.row.sourceRef" maxlength="300"></label><label>生效时间（北京时间，可留空）<input :value="effectiveInput(String(item.row.effectiveAt || ''))" type="datetime-local" @input="setCenterEffective(item.row, ($event.target as HTMLInputElement).value)"></label>
          </div></fieldset>
          <div class="item-actions"><button v-if="canDraft" :disabled="item.index === 0" @click="moveCenterItem(item, -1)">上移</button><button v-if="canDraft" :disabled="item.index === centerCollectionLength(item.collection) - 1" @click="moveCenterItem(item, 1)">下移</button><button v-if="canDraft" @click="saveCenterItem(item.id)">保存此项</button><button v-if="canDraft" class="danger" @click="deleteCenterItem(item)">删除</button><button v-if="hasPermission('admin.content.publish')" class="publish" :disabled="publishingId === item.id" @click="publishCenterItem(item.collection, item.id)">{{ publishingId === item.id ? '发布中…' : '审核并发布此项' }}</button></div></template>
          <div v-if="workspace === 'published'" class="item-actions published-order-actions"><button v-if="canDraft" :disabled="item.index === 0" @click="moveCenterItem(item, -1)">退回并上移</button><button v-if="canDraft" :disabled="item.index === centerCollectionLength(item.collection) - 1" @click="moveCenterItem(item, 1)">退回并下移</button><button v-if="canDraft" class="danger" @click="deleteCenterItem(item)">删除并取消公开</button></div>
        </details>
      </section>
      <section v-if="activeRulingItems.length" class="item-group"><h4>裁定问答</h4>
        <details v-for="row in activeRulingItems" :id="`admin-rule-item-${row.item.id}`" :key="row.item.id" class="admin-item-card ruling-editor" @toggle="trackRulingEditor(row.item.id, $event)">
          <summary><span><b>{{ row.item.question || row.item.id }}</b><small>{{ rulingHeading(row.item) }} · {{ row.item.id }} · {{ stateLabels[row.state] }}</small></span><em>{{ workspace === 'published' ? '查看已发布内容' : '查看与操作' }}</em></summary>
          <template v-if="openRulingEditors.has(row.item.id)"><template v-if="workspace === 'published'"><div class="admin-item-preview published-preview"><small>玩家端预览 · {{ stateLabels[row.state] }}</small><b>问：{{ row.item.question || '尚未填写问题' }}</b><p>答：{{ row.item.answer || '尚未填写裁定' }}</p><span>{{ row.item.effectiveAt ? `${row.state === 'scheduled' ? '计划生效' : '生效时间'}：${new Date(row.item.effectiveAt).toLocaleString('zh-CN')}` : '当前已生效' }}</span></div><div class="item-actions published-actions"><button v-if="canDraft" @click="returnRuling(row.item)">退回修改</button></div></template><template v-else><div class="admin-item-preview"><small>玩家端预览 · {{ rulingHeading(row.item) }}</small><b>问：{{ row.item.question || '尚未填写问题' }}</b><p>答：{{ row.item.answer || '尚未填写裁定' }}</p><span>{{ row.item.category }}<template v-if="row.item.effectiveAt"> · 计划生效：{{ new Date(row.item.effectiveAt).toLocaleString('zh-CN') }}</template></span></div>
          <fieldset :disabled="!canDraft"><div class="form-grid">
            <label>稳定 ID<input v-model.trim="row.item.id" maxlength="100"></label><label>草稿状态<select v-model="row.item.status"><option value="pending">待复核</option><option value="published">已有发布版本</option><option value="superseded">已替代</option></select></label>
            <label>内容类型<select v-model="row.item.scope" @change="normalizeRulingProducts(row.item)"><option value="general">通用规则</option><option value="card">单卡裁定</option><option value="errata">勘误</option><option value="construction">构筑与限制</option><option value="tournament">赛事规则</option></select></label><label>来源类型<select v-model="row.item.sourceKind"><option value="rulebook">规则书</option><option value="official-faq">官方 FAQ</option><option value="user-ruling">用户确认裁定</option><option value="designer-ruling">设计者确认裁定</option></select></label>
            <label>展示分类<input v-model.trim="row.item.category" maxlength="100"></label><label>记录日期<input v-model="row.item.recordedAt" type="date"></label><label>生效时间（北京时间，可留空）<input :value="effectiveInput(row.item.effectiveAt)" type="datetime-local" @input="setEffective(row.item, ($event.target as HTMLInputElement).value)"></label><label>来源说明<input v-model.trim="row.item.sourceRef" maxlength="300"></label>
            <fieldset class="wide topic-selector"><legend>规则主题</legend><button v-for="topic in RULE_TOPIC_DEFINITIONS" :key="topic.id" type="button" :class="{ active: row.item.topics.includes(topic.id) }" @click="toggleTopic(row.item, topic.id)">{{ topic.label }}</button></fieldset>
            <label class="wide">问题<input v-model.trim="row.item.question" maxlength="500"></label><label class="wide">规范裁定<textarea v-model.trim="row.item.answer" rows="5" maxlength="8000"></textarea></label>
            <div class="wide linked-cards"><span>关联卡牌</span><div class="linked-card-grid"><article v-for="linked in linkedCards(row.item)" :key="linked.id"><button v-if="linked.card" type="button" class="linked-card-image" :aria-label="`查看${linked.card.nameZh}卡牌详情`" @click="detailCard = linked.card"><CardImage :card-id="linked.card.id" :legacy-url="linked.card.imageUrl" :alt="linked.card.nameZh" intent="thumb"/></button><div><b>{{ linked.card?.nameZh || '待识别卡牌' }}</b><small>{{ linked.card ? displayCardNumber(linked.card) : linked.id }}</small><button type="button" class="card-chip" @click="removeCard(row.item, linked.id)">移除关联</button></div></article><p v-if="!row.item.cardIds.length && (row.item.scope === 'card' || row.item.scope === 'errata')" class="missing-link">待补关联：单卡裁定必须选择规范卡牌，不会从问题正文猜测。</p></div><button type="button" @click="pickerTarget = row.item">＋ 从卡牌图鉴选择</button></div>
            <div class="derived-products"><span>自动归属产品</span><div><b v-for="product in row.item.productIds" :key="product">{{ product }}</b><em v-if="!row.item.productIds.length">选择关联卡牌后自动生成</em></div><small>保存与发布时按规范卡牌目录重新计算，不可手工修改。</small></div>
            <label>搜索标签（逗号分隔）<input :value="join(row.item.tags)" @change="normalizeList(row.item, 'tags', ($event.target as HTMLInputElement).value)"></label><label>原始来源 ID（逗号分隔）<input :value="join(row.item.sourceIds)" @change="normalizeList(row.item, 'sourceIds', ($event.target as HTMLInputElement).value)"></label><label class="wide">替代的旧条目 ID（逗号分隔）<input :value="join(row.item.supersedes)" @change="normalizeList(row.item, 'supersedes', ($event.target as HTMLInputElement).value)"></label>
          </div></fieldset>
          <div class="item-actions"><button v-if="canDraft" @click="saveRulingItem(row.item)">保存此项</button><button v-if="canDraft && row.state === 'draft'" class="danger" @click="removeRuling(row.index)">移除草稿</button><button v-if="hasPermission('admin.content.publish')" class="publish" :disabled="publishingId === row.item.id" @click="publishRuling(row.item)">{{ publishingId === row.item.id ? '发布中…' : '审核并发布此项' }}</button></div></template></template>
        </details>
      </section>
      <p v-if="!activeCenterItems.length && !activeRulingItems.length" class="empty">此工作区暂无对象</p>
    </section>

    <section v-else-if="workspace === 'sources'" class="workspace-panel source-workspace"><header><div><small>SOURCES</small><h4>来源</h4></div><p>原始资料只供复核，不会直接成为公开答案。</p></header>
      <div class="review-summary"><b>原始来源复核：{{ reviewCount }} / {{ LEGACY_FAQ_SOURCES.length }}</b><span>发现冲突时先标记、再改写；不能从卡效实现或其他游戏术语反推答案。</span></div>
      <input v-model="sourceQuery" class="source-search" placeholder="搜索原始问题或关键词">
      <div class="source-list"><article v-for="source in visibleSources" :key="source.id" :data-state="source.reviewState"><div><small>{{ source.id }} · {{ source.type }} · {{ sourceStates[source.reviewState] }}</small><b>{{ source.question }}</b><p>{{ source.answer }}</p><em v-if="source.note">{{ source.note }}</em></div><button v-if="canDraft && (source.reviewState === 'pending-review' || source.reviewState === 'conflict-flagged')" @click="startFromSource(source.id)">据此新建草稿</button></article></div>
      <p v-if="selectedSource" class="selected-source">最近选用来源：{{ selectedSource }}</p>
    </section>

    <section v-else class="workspace-panel history-workspace"><header><div><small>HISTORY</small><h4>历史与已替代</h4></div><p>已替代对象与逐项发布记录集中核对，历史不会一键覆盖当前草稿。</p></header>
      <section v-if="supersededCenterItems.length || supersededRulingItems.length" class="superseded-list"><h4>已替代对象</h4><article v-for="item in supersededCenterItems" :key="item.id"><b>{{ item.title }}</b><span>{{ item.id }} · 规则资料</span></article><article v-for="row in supersededRulingItems" :key="row.item.id"><b>{{ row.item.question || row.item.id }}</b><span>{{ row.item.id }} · 裁定问答</span></article></section>
      <section class="rule-history"><h4>规则发布历史</h4><details v-for="batch in ruleHistory" :key="batch.id"><summary><span><b>{{ historySummary(batch) }}</b><small>{{ batch.actorName }} · {{ new Date(batch.createdAt).toLocaleString('zh-CN') }}</small></span><code>{{ batch.sourceBatchId }}</code></summary><div class="history-diff"><p>{{ historyChanges(batch) }}</p><p><b>发布前</b>{{ historyPreview(batch, 'before') }}</p><p><b>发布后</b>{{ historyPreview(batch, 'after') }}</p></div></details><p v-if="!ruleHistory.length" class="empty">尚无逐项发布历史</p></section>
      <details class="raw-rule-document"><summary>高级：查看原始结构（只读）</summary><pre>{{ ruleCenterDraft }}</pre></details>
    </section>
    <SingleCardPicker v-if="pickerTarget" title="选择关联规则的卡牌" @select="selectCard" @close="pickerTarget = null" />
    <CatalogCardDetails v-if="detailCard" :card="detailCard" @close="detailCard = null"/>
  </section>
</template>

<style scoped>
.ruling-admin{box-sizing:border-box;width:min(100%,1680px);margin:0 auto;padding:22px;border:1px solid #35424a;background:#0e161d;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.ruling-admin>header{display:flex;align-items:center;justify-content:space-between;gap:20px;margin:-22px -22px 18px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.ruling-admin small{color:#55c6cd;font-size:12px;font-weight:900;letter-spacing:.12em}.ruling-admin h3{margin:6px 0}.ruling-admin p{color:#98a4a9;font-size:14px;line-height:1.65}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px}.ruling-admin button,.ruling-admin input,.ruling-admin textarea,.ruling-admin select{box-sizing:border-box;min-height:40px;padding:9px 11px;border:1px solid #4a5860;background:#070d12;color:#fff;font:14px 'Microsoft YaHei','微软雅黑',sans-serif}.workspace-tabs{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:7px;margin-bottom:12px}.workspace-tabs button{display:flex;align-items:center;justify-content:space-between;gap:8px;text-align:left}.workspace-tabs button.active{border-color:#d1ad54;background:#2b2515;color:#f0d47b}.workspace-tabs span{display:grid;min-width:24px;height:24px;place-items:center;border-radius:12px;background:#15242a;color:#9adadd;font-size:12px}.review-summary{display:flex;gap:14px;padding:14px;border-left:3px solid #d1ad54;background:#171811;color:#c9c4b6;font-size:14px;line-height:1.7}.review-summary b{flex:0 0 auto;color:#efd170}.workspace-panel{margin-top:14px;padding:16px;border:1px solid #3d4a52;background:#091016}.workspace-panel>header{display:flex;align-items:flex-end;justify-content:space-between;gap:18px;margin-bottom:12px}.workspace-panel>header h4{margin:4px 0 0;font-size:18px}.workspace-panel>header p{margin:0}.item-group{display:grid;gap:8px;margin-top:14px}.item-group>h4,.rule-history>h4,.superseded-list>h4{margin:0 0 4px;color:#d8bc69}.admin-item-card{border:1px solid #344149;background:#0d151b}.admin-item-card>summary{display:flex;align-items:center;justify-content:space-between;gap:14px;padding:12px;cursor:pointer}.admin-item-card>summary span{display:grid;gap:4px}.admin-item-card>summary small{letter-spacing:0}.admin-item-card>summary em{color:#8f9ba0;font-size:12px;font-style:normal}.admin-item-preview{display:grid;gap:7px;margin:0 12px;padding:12px;border-left:3px solid #47747a;background:#091116}.admin-item-preview small{letter-spacing:0}.admin-item-preview b{color:#e7eceb}.admin-item-preview p{margin:0;white-space:pre-line}.admin-item-preview span{color:#87969c;font-size:12px}.published-preview{border-left-color:#b09147;background:#11150f}.admin-item-card fieldset{margin:0;padding:0;border:0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;padding:12px}.form-grid label{display:grid;gap:6px;color:#c0c8ca;font-size:13px;font-weight:800}.form-grid input,.form-grid textarea,.form-grid select{width:100%}.form-grid textarea{resize:vertical}.form-grid .wide{grid-column:1/-1}.item-actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px;padding:12px;border-top:1px solid #303c43}.published-actions{border-top:0}.item-actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}.topic-selector{display:flex;flex-wrap:wrap;gap:7px;padding:10px;border:1px solid #35424a}.topic-selector legend,.linked-cards>span{color:#c0c8ca;font-size:13px;font-weight:800}.topic-selector button.active{border-color:#d1ad54;background:#2b2515;color:#efd170}.linked-cards{display:grid;gap:7px}.linked-cards>div{display:flex;flex-wrap:wrap;gap:7px}.card-chip{border-color:#427278!important;background:#10272a!important;color:#9ee4e8!important}.source-search{width:100%;margin:12px 0}.source-list{display:grid;gap:8px}.source-list article{display:flex;justify-content:space-between;gap:14px;padding:13px;border:1px solid #35424a;background:#0d151b}.source-list article[data-state="conflict-flagged"]{border-color:#a46145;background:#241713}.source-list article[data-state="replaced"]{opacity:.65}.source-list article div{display:grid;gap:6px}.source-list article small{color:#d7bc69;letter-spacing:0}.source-list article p{margin:0;white-space:pre-line}.source-list article em{color:#e2b37e;font-size:13px;font-style:normal}.source-list article button{height:max-content;flex:0 0 auto}.selected-source{color:#d8bc69!important}.superseded-list,.rule-history{display:grid;gap:8px}.superseded-list article,.rule-history>details{padding:10px;border:1px solid #344149;background:#0d151b}.superseded-list article{display:grid;gap:4px}.superseded-list span{color:#8d999f;font-size:12px}.rule-history{margin-top:18px}.rule-history>details>summary{display:flex;align-items:center;justify-content:space-between;gap:12px}.rule-history>details>summary span{display:grid;gap:3px}.history-diff{display:grid;gap:5px;margin-top:10px;padding:10px;background:#081016}.history-diff p{display:grid;grid-template-columns:72px 1fr;gap:8px;color:#c6ced0}.history-diff p:first-child{display:block;color:#e2c878}.rule-history code{color:#d8bc69;font-size:12px;overflow-wrap:anywhere}.raw-rule-document{margin-top:16px}.raw-rule-document summary{cursor:pointer;color:#d8bc69;font-weight:900}.raw-rule-document pre{max-height:360px;overflow:auto;padding:12px;background:#05090c;color:#aeb9bd;font:12px/1.55 Consolas,'Courier New',monospace;white-space:pre-wrap}.empty{padding:32px;text-align:center}
.linked-cards{align-content:start}.linked-card-grid{display:grid!important;grid-template-columns:repeat(auto-fill,minmax(220px,1fr));gap:9px!important}.linked-card-grid article{display:grid;grid-template-columns:80px minmax(0,1fr);gap:10px;padding:8px;border:1px solid #35424a;background:#091116}.linked-card-image{width:80px;min-height:112px!important;padding:0!important;overflow:hidden}.linked-card-image :deep(.l12-card-image),.linked-card-image :deep(img){width:100%;height:100%;object-fit:contain}.linked-card-grid article>div{display:flex!important;min-width:0;flex-direction:column;align-items:flex-start;gap:5px!important}.linked-card-grid article b{overflow-wrap:anywhere}.linked-card-grid article small{color:#91a0a5;letter-spacing:0}.linked-card-grid .card-chip{margin-top:auto}.missing-link{grid-column:1/-1;margin:0;padding:10px;border:1px dashed #765e34;color:#d8bc69;font-size:12px}.derived-products{display:grid;align-content:start;gap:7px}.derived-products>span{color:#c0c8ca;font-size:13px;font-weight:800}.derived-products>div{display:flex;min-height:40px;flex-wrap:wrap;align-items:center;gap:6px;padding:7px;border:1px solid #35424a;background:#070d12}.derived-products b{padding:4px 7px;background:#16242a;color:#8ad5d8;font-size:12px}.derived-products em,.derived-products small{color:#849197;font-size:12px;font-style:normal;line-height:1.5}
.center-create-bar{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-top:14px;padding:12px;border:1px solid #3d4a52;background:#0d151b}.center-create-bar>span,.rule-media-editor>span{color:#d8bc69;font-size:13px;font-weight:900}.center-create-bar>div{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:7px}.rule-media-editor{display:grid;gap:9px;padding:12px;border:1px solid #35424a;background:#091116}.rule-media-editor>select{width:100%}.rule-media-editor>img{display:block;max-width:min(100%,520px);max-height:300px;object-fit:contain;border:1px solid #45535b;background:#04080b}.rule-media-editor>details{border:1px solid #35424a}.rule-media-editor>details>summary{padding:10px;cursor:pointer;color:#8ad5d8;font-weight:900}.rule-media-editor>details :deep(.media-upload-field){margin:10px}.published-order-actions{border-top:1px solid #303c43}
.ruling-admin,.ruling-admin>*,.workspace-panel,.workspace-panel>*,.item-group,.admin-item-card,.admin-item-card>*,.form-grid,.form-grid>*,.admin-item-card fieldset{box-sizing:border-box;min-width:0;max-width:100%}.ruling-admin input,.ruling-admin textarea,.ruling-admin select{min-width:0;max-width:100%}
@media(max-width:900px){.workspace-tabs{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:760px){.ruling-admin>header,.workspace-panel>header,.source-list article,.center-create-bar{align-items:flex-start;flex-direction:column}.actions,.center-create-bar>div{justify-content:flex-start}.form-grid{grid-template-columns:1fr}.workspace-tabs{grid-template-columns:1fr}.review-summary{flex-direction:column}.item-actions{justify-content:flex-start}.rule-media-editor>details :deep(.media-upload-field){margin:6px}}
@media(max-width:380px){.ruling-admin{padding:14px}.ruling-admin>header{margin:-14px -14px 14px;padding:14px}.workspace-panel{padding:10px}.form-grid{padding:8px}.admin-item-preview{margin:0 8px;padding:8px}}
</style>
