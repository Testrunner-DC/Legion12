<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { adminApi, hasPermission, type ContentBatch } from '@/l12/platform'
import SingleCardPicker, { type SingleCardPickerItem } from '@/l12/SingleCardPicker.vue'
import { LEGACY_FAQ_SOURCES, RULE_TOPIC_DEFINITIONS, coreRuleBlockId, createRuleCenterDraft, createRulingsDraft, mergedRulings, parsePublishedRuleCenter, parseRulingDocument, serializeRulingDocument, withPendingRulingSeeds, type RuleCenterDocument, type RuleRuling, type RuleTopicId } from '@/l12/data/ruleCenterData'

const emit = defineEmits<{ notice: [value: string] }>()
const entries = ref<RuleRuling[]>([])
const ruleCenterDocument = ref<RuleCenterDocument>(createRuleCenterDraft())
const ruleCenterDraft = computed(() => JSON.stringify(ruleCenterDocument.value, null, 2))
const publishedRulings = ref<RuleRuling[]>([])
const publishedCenter = ref<RuleCenterDocument>(parsePublishedRuleCenter('{}'))
const rulingVersion = ref(0)
const centerVersion = ref(0)
const history = ref<ContentBatch[]>([])
const pickerTarget = ref<RuleRuling | null>(null)
const busy = ref(false)
const publishingId = ref('')
const selectedSource = ref('')
const sourceQuery = ref('')
const sourceStates = { 'pending-review': '待梳理', replaced: '已替代', 'conflict-flagged': '发现冲突' } as const
const visibleSources = computed(() => LEGACY_FAQ_SOURCES.filter(source => !sourceQuery.value.trim() || [source.id, source.question, source.answer].join(' ').includes(sourceQuery.value.trim())))
const reviewedSources = computed(() => new Set(mergedRulings(entries.value).flatMap(item => [...item.sourceIds, ...item.supersedes])))
const reviewCount = computed(() => LEGACY_FAQ_SOURCES.filter(source => source.reviewState === 'replaced' || reviewedSources.value.has(source.id)).length)
const centerItems = computed(() => {
  const document = ruleCenterDocument.value as unknown as Record<string, Array<Record<string, any>>>
  return ['coreBlocks', 'quickStart', 'terms', 'tournament', 'versions'].flatMap(collection =>
    (Array.isArray(document[collection]) ? document[collection] : []).filter(item => item.id)
      .map(item => ({ collection, row: item, id: String(item.id), title: String(item.title || item.topic || item.chapter || item.id), status: String(item.status || 'pending') })))
})
const ruleHistory = computed(() => history.value.filter(batch => batch.action === 'rule-item-publish'))
const canDraft = computed(() => hasPermission('admin.content.draft'))
function notice(value: string) { emit('notice', value) }
function split(value: string) { return value.split(',').map(item => item.trim()).filter(Boolean) }
function join(value: string[]) { return value.join(', ') }
function toggleTopic(item: RuleRuling, topic: RuleTopicId) {
  item.topics = item.topics.includes(topic) ? item.topics.filter(value => value !== topic) : [...item.topics, topic]
}
function effectiveInput(value?: string) { return value ? value.slice(0, 16) : '' }
function setEffective(item: RuleRuling, value: string) { item.effectiveAt = value ? `${value}:00+08:00` : undefined }
function setCenterEffective(item: Record<string, unknown>, value: string) { item.effectiveAt = value ? `${value}:00+08:00` : undefined }
function selectCard(card: SingleCardPickerItem) {
  if (pickerTarget.value && !pickerTarget.value.cardIds.includes(card.cardId)) pickerTarget.value.cardIds.push(card.cardId)
  pickerTarget.value = null
}
function removeCard(item: RuleRuling, cardId: string) { item.cardIds = item.cardIds.filter(value => value !== cardId) }
function publicationState(item: RuleRuling) {
  const live = publishedRulings.value.find(row => row.id.toLocaleLowerCase() === item.id.toLocaleLowerCase())
  if (!live) return '草稿'
  if (live.effectiveAt && new Date(live.effectiveAt).getTime() > Date.now()) return '已审核 · 定时生效'
  return JSON.stringify(live) === JSON.stringify(item) ? '已生效' : '已有生效版本 · 含未发布修改'
}
function centerPublicationState(collection: string, itemId: string, draft: Record<string, unknown>) {
  const document = publishedCenter.value as unknown as Record<string, Array<Record<string, unknown> & { id?: string; effectiveAt?: string }>>
  const live = document[collection]?.find(item => item.id?.toLocaleLowerCase() === itemId.toLocaleLowerCase())
  if (!live) return '草稿'
  if (live.effectiveAt && new Date(live.effectiveAt).getTime() > Date.now()) return '已审核 · 定时生效'
  const comparable = (value: Record<string, unknown>) => { const clone = { ...value }; delete clone.status; return clone }
  return JSON.stringify(comparable(live)) === JSON.stringify(comparable(draft)) ? '已生效' : '已有生效版本 · 含未发布修改'
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
  if (!row) return side === 'before' ? '无公开版本' : '无公开版本'
  return String(row.question || row.title || row.chapter || row.topic || row.id || '未命名条目')
}
function addRuling(sourceId = '') {
  const id = `RULING-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}-${entries.value.length + 1}`
  entries.value.unshift({ id, scope: 'card', question: '', answer: '', category: '单卡裁定', sourceKind: 'user-ruling', sourceRef: '待补充确认来源', recordedAt: new Date().toISOString().slice(0, 10), status: 'pending', cardIds: [], productIds: [], tags: [], topics: ['effects-stack'], sourceIds: sourceId ? [sourceId] : [], supersedes: sourceId ? [sourceId] : [] })
}
function startFromSource(sourceId: string) { selectedSource.value = sourceId; addRuling(sourceId) }
function removeRuling(index: number) { entries.value.splice(index, 1) }
function normalizeList(item: RuleRuling, key: 'cardIds' | 'productIds' | 'tags' | 'sourceIds' | 'supersedes', value: string) { item[key] = split(value) }
async function load() {
  busy.value = true
  try {
    const [rulings, center, batches] = await Promise.all([adminApi.getContent('rules.rulings'), adminApi.getContent('rules.center'), adminApi.contentBatches()])
    const parsedRulings = parseRulingDocument(rulings.draftValue)
    entries.value = !parsedRulings.length && !rulings.draftValue.trim()
      ? createRulingsDraft()
      : withPendingRulingSeeds(parsedRulings)
    publishedRulings.value = parseRulingDocument(rulings.publishedValue)
    publishedCenter.value = parsePublishedRuleCenter(center.publishedValue)
    rulingVersion.value = rulings.version
    centerVersion.value = center.version
    history.value = batches
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
  }
  catch (error) { notice(error instanceof Error ? error.message : '裁定草稿读取失败') }
  finally { busy.value = false }
}
async function save(show = true) {
  try {
    const [rulings, center] = await Promise.all([
      adminApi.saveContentDraft('rules.rulings', serializeRulingDocument(entries.value)),
      adminApi.saveContentDraft('rules.center', ruleCenterDraft.value),
    ])
    rulingVersion.value = rulings.version
    centerVersion.value = center.version
    if (show) notice('规则中心与裁定草稿已保存；玩家只会看到审核后正式发布的快照')
    return true
  } catch (error) { notice(error instanceof Error ? error.message : '规则草稿保存失败；请检查规则中心资料是否为有效 JSON'); return false }
}
async function preview() { if (await save(false)) try { const result = await adminApi.previewContent(['rules.center', 'rules.rulings']); notice(`发布预览完成：${result.items.filter(item => item.wouldChange).length} 项将更新；未写入玩家端`) } catch (error) { notice(error instanceof Error ? error.message : '发布预览失败') } }
async function publishRuling(item: RuleRuling) {
  publishingId.value = item.id
  try {
    const saved = await adminApi.saveContentDraft('rules.rulings', serializeRulingDocument(entries.value))
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
    const saved = await adminApi.saveContentDraft('rules.center', ruleCenterDraft.value)
    const published = await adminApi.publishRuleItem('rules.center', collection, itemId, saved.version)
    centerVersion.value = published.version
    const document = JSON.parse(published.draftValue) as Record<string, Array<Record<string, unknown>>>
    const item = document[collection]?.find(row => row.id === itemId)
    if (item) item.status = 'published'
    ruleCenterDocument.value = document as unknown as RuleCenterDocument
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
    <header><div><small>RULE CENTER REVIEW</small><h3>规则中心与 FAQ 裁定库</h3><p>每条内容独立审核、独立发布。公开页只读取已发布条目的快照，发布一条不会连带公开其他草稿。</p></div><div class="actions"><button @click="load">{{ busy ? '读取中…' : '刷新' }}</button><button v-if="hasPermission('admin.content.draft')" @click="addRuling()">＋ 新建裁定</button><button v-if="hasPermission('admin.content.draft')" @click="save()">保存草稿</button><button v-if="hasPermission('admin.content.draft')" @click="preview">预览草稿差异</button></div></header>
    <details class="rule-center-document">
      <summary>规则中心资料草稿（核心规则、快速入门、术语、赛事与版本记录）</summary>
      <p>每项独立编辑、保存和发布；区域变化须使用《十二军团》规则书中的具体动作名称。当前修订：规则中心 {{ centerVersion }}，裁定库 {{ rulingVersion }}。</p>
      <div class="center-review-list">
        <details v-for="item in centerItems" :key="`${item.collection}-${item.id}`" class="center-editor">
          <summary><span><b>{{ item.title }}</b><small>{{ item.id }} · {{ centerPublicationState(item.collection, item.id, item.row) }}</small></span></summary>
          <fieldset :disabled="!canDraft"><div class="form-grid">
            <label>稳定 ID<input v-model.trim="item.row.id" maxlength="100" :disabled="item.status === 'published'"></label><label>栏目<input :value="item.collection" disabled></label>
            <label v-if="item.collection === 'coreBlocks'">页码（可留空）<input v-model="item.row.page" maxlength="20"></label><label v-if="item.collection === 'coreBlocks'">主题<input v-model.trim="item.row.topic" maxlength="100"></label>
            <label v-if="item.collection === 'coreBlocks'" class="wide">章节<input v-model.trim="item.row.chapter" maxlength="100"></label><label v-if="item.collection !== 'coreBlocks'" class="wide">标题<input v-model.trim="item.row.title" maxlength="300"></label>
            <label v-if="item.collection === 'coreBlocks'" class="wide">规则正文<textarea v-model.trim="item.row.text" rows="5" maxlength="12000"></textarea></label><label v-else-if="item.collection === 'versions'" class="wide">版本摘要<textarea v-model.trim="item.row.summary" rows="5" maxlength="12000"></textarea></label><label v-else class="wide">正文<textarea v-model.trim="item.row.body" rows="5" maxlength="12000"></textarea></label>
            <label v-if="item.collection !== 'coreBlocks'">来源说明<input v-model.trim="item.row.sourceRef" maxlength="300"></label><label>生效时间（北京时间，可留空）<input :value="effectiveInput(String(item.row.effectiveAt || ''))" type="datetime-local" @input="setCenterEffective(item.row, ($event.target as HTMLInputElement).value)"></label><label>草稿状态<select v-model="item.row.status"><option value="pending">待审核</option><option value="published">已有发布版本</option><option v-if="item.collection !== 'versions'" value="superseded">已替代</option></select></label>
          </div></fieldset>
          <button v-if="hasPermission('admin.content.publish')" class="publish item-publish" :disabled="publishingId === item.id" @click="publishCenterItem(item.collection, item.id)">{{ publishingId === item.id ? '发布中…' : '审核并发布此项' }}</button>
        </details>
      </div>
      <details class="raw-rule-document"><summary>高级：查看原始结构（只读）</summary><pre>{{ ruleCenterDraft }}</pre></details>
    </details>
    <div class="review-summary"><b>原始来源复核：{{ reviewCount }} / {{ LEGACY_FAQ_SOURCES.length }}</b><span>已确认的用户／设计者裁定优先于旧表。发现冲突时先标记、再改写；不能从卡效实现或其他游戏术语反推答案。</span></div>
    <details class="source-inbox"><summary>原始 FAQ 复核收件箱（{{ LEGACY_FAQ_SOURCES.length }} 条）</summary><input v-model="sourceQuery" placeholder="搜索原始问题或关键词"><article v-for="source in visibleSources" :key="source.id" :data-state="source.reviewState"><div><small>{{ source.id }} · {{ source.type }} · {{ sourceStates[source.reviewState] }}</small><b>{{ source.question }}</b><p>{{ source.answer }}</p><em v-if="source.note">{{ source.note }}</em></div><button v-if="hasPermission('admin.content.draft') && (source.reviewState === 'pending-review' || source.reviewState === 'conflict-flagged')" @click="startFromSource(source.id)">据此新建草稿</button></article></details>
    <p v-if="selectedSource" class="selected-source">最近选用来源：{{ selectedSource }}</p>
    <article v-for="(item, index) in entries" :key="`${item.id}-${index}`" class="ruling-editor"><header><span><b>裁定草稿 {{ index + 1 }}</b><small>{{ publicationState(item) }}</small></span><button v-if="hasPermission('admin.content.draft')" class="danger" @click="removeRuling(index)">移除</button></header><fieldset :disabled="!canDraft"><div class="form-grid"><label>稳定 ID<input v-model.trim="item.id" maxlength="100"></label><label>草稿状态<select v-model="item.status"><option value="pending">待复核</option><option value="published">已有发布版本</option><option value="superseded">已替代</option></select></label><label>内容类型<select v-model="item.scope"><option value="general">通用规则</option><option value="card">单卡裁定</option><option value="errata">勘误</option><option value="construction">构筑与限制</option><option value="tournament">赛事规则</option></select></label><label>来源类型<select v-model="item.sourceKind"><option value="rulebook">规则书</option><option value="official-faq">官方 FAQ</option><option value="user-ruling">用户确认裁定</option><option value="designer-ruling">设计者确认裁定</option></select></label><label>展示分类<input v-model.trim="item.category" maxlength="100"></label><label>记录日期<input v-model="item.recordedAt" type="date"></label><label>生效时间（北京时间，可留空）<input :value="effectiveInput(item.effectiveAt)" type="datetime-local" @input="setEffective(item, ($event.target as HTMLInputElement).value)"></label><label>来源说明<input v-model.trim="item.sourceRef" maxlength="300"></label><fieldset class="wide topic-selector"><legend>规则主题</legend><button v-for="topic in RULE_TOPIC_DEFINITIONS" :key="topic.id" type="button" :class="{ active: item.topics.includes(topic.id) }" @click="toggleTopic(item, topic.id)">{{ topic.label }}</button></fieldset><label class="wide">问题<input v-model.trim="item.question" maxlength="500" placeholder="以完整、可检索的问题描述填写"></label><label class="wide">规范裁定<textarea v-model.trim="item.answer" rows="5" maxlength="8000" placeholder="使用十二军团术语，先给出明确结论，再说明必要条件与结果。"></textarea></label><div class="wide linked-cards"><span>关联卡牌</span><div><button v-for="cardId in item.cardIds" :key="cardId" type="button" class="card-chip" @click="removeCard(item, cardId)">{{ cardId }} ×</button><button type="button" @click="pickerTarget = item">＋ 从卡牌图鉴选择</button></div></div><label>产品（逗号分隔，可留空）<input :value="join(item.productIds)" @change="normalizeList(item, 'productIds', ($event.target as HTMLInputElement).value)"></label><label>搜索标签（逗号分隔）<input :value="join(item.tags)" @change="normalizeList(item, 'tags', ($event.target as HTMLInputElement).value)"></label><label>原始来源 ID（逗号分隔）<input :value="join(item.sourceIds)" @change="normalizeList(item, 'sourceIds', ($event.target as HTMLInputElement).value)"></label><label class="wide">替代的旧条目 ID（逗号分隔；如 LEGACY-FAQ-47）<input :value="join(item.supersedes)" @change="normalizeList(item, 'supersedes', ($event.target as HTMLInputElement).value)"></label></div></fieldset></article>
    <section v-if="entries.length" class="publish-queue"><h4>逐项审核发布</h4><p>核对上方内容后，只发布选中的这一条；其余条目继续保留为后台草稿。</p><article v-for="item in entries" :key="`publish-${item.id}`"><div><b>{{ item.question || item.id }}</b><small>{{ item.id }} · {{ item.status === 'pending' ? '待审核' : item.status === 'superseded' ? '已替代' : '可重新发布修订' }}</small></div><button v-if="hasPermission('admin.content.publish')" class="publish" :disabled="publishingId === item.id" @click="publishRuling(item)">{{ publishingId === item.id ? '发布中…' : '审核并发布此项' }}</button></article></section>
    <section class="rule-history"><h4>规则发布历史</h4><p>保留每次逐项发布的发布前后完整快照，供人工核对差异；历史不会一键覆盖当前草稿。</p><details v-for="batch in ruleHistory" :key="batch.id"><summary><span><b>{{ historySummary(batch) }}</b><small>{{ batch.actorName }} · {{ new Date(batch.createdAt).toLocaleString('zh-CN') }}</small></span><code>{{ batch.sourceBatchId }}</code></summary><div class="history-diff"><p>{{ historyChanges(batch) }}</p><p><b>发布前</b>{{ historyPreview(batch, 'before') }}</p><p><b>发布后</b>{{ historyPreview(batch, 'after') }}</p></div></details><p v-if="!ruleHistory.length" class="empty">尚无逐项发布历史</p></section>
    <p v-if="!entries.length" class="empty">尚无后台裁定草稿。请从原始 FAQ 收件箱开始逐条改写，或新建一条已有确认来源的裁定。</p>
    <SingleCardPicker v-if="pickerTarget" title="选择关联规则的卡牌" @select="selectCard" @close="pickerTarget = null" />
  </section>
</template>

<style scoped>
.ruling-admin{padding:22px;border:1px solid #35424a;background:#0e161d}.ruling-admin>header,.ruling-editor>header{display:flex;align-items:center;justify-content:space-between;gap:20px}.ruling-admin>header{margin:-22px -22px 20px;padding:22px;border-bottom:1px solid #35424a;background:#101821}.ruling-admin small{color:#55c6cd;font:900 13px monospace;letter-spacing:.15em}.ruling-admin h3{margin:6px 0}.ruling-admin p{color:#98a4a9;font-size:14px;line-height:1.65}.actions{display:flex;flex-wrap:wrap;justify-content:flex-end;gap:8px}.ruling-admin button,.ruling-admin input,.ruling-admin textarea,.ruling-admin select{box-sizing:border-box;min-height:40px;padding:9px 11px;border:1px solid #4a5860;background:#070d12;color:#fff;font-size:14px}.actions .publish{border-color:#2f785e;background:#0d251c;color:#7fe0b9}.review-summary{display:flex;gap:14px;padding:14px;border-left:3px solid #d1ad54;background:#171811;color:#c9c4b6;font-size:14px;line-height:1.7}.review-summary b{flex:0 0 auto;color:#efd170}.source-inbox{margin-top:16px}.source-inbox summary{cursor:pointer;color:#d8bc69;font-weight:900}.source-inbox>input{width:100%;margin:12px 0}.source-inbox article{display:flex;justify-content:space-between;gap:14px;margin-top:8px;padding:13px;border:1px solid #35424a;background:#091016}.source-inbox article[data-state="conflict-flagged"]{border-color:#a46145;background:#241713}.source-inbox article[data-state="replaced"]{opacity:.65}.source-inbox article div{display:grid;gap:6px}.source-inbox article small{color:#d7bc69;letter-spacing:0}.source-inbox article b{color:#f1f3ef}.source-inbox article p{margin:0;white-space:pre-line}.source-inbox article em{color:#e2b37e;font-size:13px;font-style:normal}.source-inbox article button{height:max-content;flex:0 0 auto}.selected-source{color:#d8bc69!important}.ruling-editor{margin-top:14px;padding:16px;border:1px solid #35424a;background:#091016}.ruling-editor>header{padding-bottom:12px;border-bottom:1px solid #303c43}.danger{border-color:#81434c!important;background:#281117!important;color:#eea6ae!important}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;margin-top:14px}.form-grid label{display:grid;gap:6px;color:#c0c8ca;font-size:13px;font-weight:800}.form-grid input,.form-grid textarea,.form-grid select{width:100%}.form-grid .wide{grid-column:1/-1}.empty{padding:32px;text-align:center}@media(max-width:760px){.ruling-admin>header,.ruling-editor>header,.source-inbox article{align-items:flex-start;flex-direction:column}.actions{justify-content:flex-start}.form-grid{grid-template-columns:1fr}}
.rule-center-document{margin:16px 0;padding:14px;border:1px solid #3d4a52;background:#091016}.rule-center-document summary{cursor:pointer;color:#d8bc69;font-weight:900}.rule-center-document p{margin:10px 0}.rule-center-document textarea{width:100%;resize:vertical}.raw-rule-document pre{max-height:360px;overflow:auto;padding:12px;background:#05090c;color:#aeb9bd;font:12px/1.55 Consolas,'Courier New',monospace;white-space:pre-wrap}.center-review-list,.publish-queue,.rule-history{display:grid;gap:8px;margin-top:12px}.center-editor{padding:10px;border:1px solid #344149;background:#0d151b}.center-editor>summary span{display:grid;gap:3px}.center-editor fieldset,.ruling-editor>fieldset{margin:0;padding:0;border:0}.item-publish{margin-top:10px}.publish-queue article,.rule-history>details{padding:10px;border:1px solid #344149;background:#0d151b}.publish-queue article,.rule-history>details>summary{display:flex;align-items:center;justify-content:space-between;gap:12px}.publish-queue article div,.rule-history>details>summary span{display:grid;gap:3px}.history-diff{display:grid;gap:5px;margin-top:10px;padding:10px;background:#081016}.history-diff p{display:grid;grid-template-columns:72px 1fr;gap:8px;color:#c6ced0}.history-diff p:first-child{display:block;color:#e2c878}.center-review-list small,.publish-queue small,.rule-history small{letter-spacing:0}.publish-queue,.rule-history{margin-top:18px;padding:14px;border:1px solid #4b4731;background:#10140e}.publish-queue h4,.publish-queue p,.rule-history h4,.rule-history p{margin:0}.topic-selector{display:flex;flex-wrap:wrap;gap:7px;padding:10px;border:1px solid #35424a}.topic-selector legend,.linked-cards>span{color:#c0c8ca;font-size:13px;font-weight:800}.topic-selector button.active{border-color:#d1ad54;background:#2b2515;color:#efd170}.linked-cards{display:grid;gap:7px}.linked-cards>div{display:flex;flex-wrap:wrap;gap:7px}.card-chip{border-color:#427278!important;background:#10272a!important;color:#9ee4e8!important}.rule-history code{color:#d8bc69;font-size:12px;overflow-wrap:anywhere}
</style>
