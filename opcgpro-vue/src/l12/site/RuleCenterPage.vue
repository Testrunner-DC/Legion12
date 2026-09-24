<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { getEffectiveOperationsPolicy, getPublicContentBatch, type EffectiveOperationsPolicy } from '@/l12/platform'
import { mergedRulings, parsePublishedRuleCenter, parsePublishedRulings, RULE_TOPIC_DEFINITIONS, type RuleCenterDocument, type RuleCenterEntry, type RuleRuling, type RuleTopicId } from '@/l12/data/ruleCenterData'
import MobileFilterSheet from './MobileFilterSheet.vue'

type MainTab = 'core' | 'quick-start' | 'terms' | 'faq' | 'construction' | 'tournament' | 'versions'
type FaqMode = 'general' | 'card'
const route = useRoute()
const router = useRouter()
const tabIds = new Set<MainTab>(['core', 'quick-start', 'terms', 'faq', 'construction', 'tournament', 'versions'])
const topicIds = new Set<RuleTopicId>(RULE_TOPIC_DEFINITIONS.map(item => item.id))
const tab = ref<MainTab>('core')
const faqMode = ref<FaqMode>('general')
const query = ref('')
const coreTopic = ref('all')
const selectedTopics = ref<RuleTopicId[]>([])
const coreFiltersOpen = ref(false)
const faqFiltersOpen = ref(false)
const openIds = ref<Set<string>>(new Set())
const ruleNotice = ref('')
const dynamicRulings = ref<RuleRuling[]>([])
const ruleCenter = ref<RuleCenterDocument>({ schemaVersion: 2, coreBlocks: [], quickStart: [], terms: [], tournament: [], versions: [] })
const policy = ref<EffectiveOperationsPolicy>()
const contentError = ref('')
const policyError = ref('')
const loading = ref(false)
const cardCatalog = ref<DeckCard[]>([])
const catalogState = ref<'idle' | 'loading' | 'ready' | 'error'>('idle')
const nextRuleTransitionAt = ref('')
let transitionTimer: number | undefined
let applyingRoute = false
let contentRefresh: Promise<void> | undefined
const tabs: Array<{ id: MainTab; label: string }> = [
  { id: 'core', label: '核心规则' }, { id: 'quick-start', label: '快速入门' }, { id: 'terms', label: '术语' }, { id: 'faq', label: 'FAQ' },
  { id: 'construction', label: '构筑与限制' }, { id: 'tournament', label: '赛事规则' }, { id: 'versions', label: '版本记录' },
]
const popularKeywords = ['登场', '响应', '费用', '进攻', '兵力', '天灾']
const coreTopics = computed(() => [...new Set(ruleCenter.value.coreBlocks.map(block => block.topic).filter(Boolean))] as string[])
const allRulings = computed(() => mergedRulings(dynamicRulings.value).filter(item => item.status === 'published'))
const constructionRulings = computed(() => allRulings.value.filter(item => item.scope === 'construction'))
const tournamentRulings = computed(() => allRulings.value.filter(item => item.scope === 'tournament'))
const cardById = computed(() => new Map(cardCatalog.value.map(card => [card.id.toLowerCase(), card])))
function textSearch(value: string, queryText: string) { return !queryText || value.toLowerCase().includes(queryText.toLowerCase()) }
function cardText(item: RuleRuling) { return item.cardIds.map(id => { const card = cardById.value.get(id.toLowerCase()); return [id, card?.number, card?.nameZh].filter(Boolean).join(' ') }).join(' ') }
function rulingText(item: RuleRuling) { return [item.id, item.question, item.answer, item.category, item.tags.join(' '), item.cardIds.join(' '), cardText(item), item.productIds.join(' ')].join(' ') }
function displayRuleText(text: string) { return text.split('\n').map(line => line.replace(/（?\s*P\.[^\s\n）]*\s*）?/gi, '').replace(/[.。]{2,}\s*\d+\s*$/, '').trimEnd()).filter(line => line.trim()).join('\n') }
function searchScore(item: RuleRuling, queryText: string) {
  const term = queryText.trim().toLowerCase()
  if (!term) return 0
  const cards = item.cardIds.flatMap(id => { const card = cardById.value.get(id.toLowerCase()); return [id, card?.number || '', card?.nameZh || ''] }).map(value => value.toLowerCase())
  if ([item.id, ...cards].some(value => value === term)) return 4
  if (cards.some(value => value.startsWith(term))) return 3
  if ([item.question, ...cards].some(value => value.toLowerCase().includes(term))) return 2
  return rulingText(item).toLowerCase().includes(term) ? 1 : -1
}
const ruleResults = computed(() => ruleCenter.value.coreBlocks.filter(block => block.topic !== '目录' && (coreTopic.value === 'all' || block.topic === coreTopic.value) && textSearch([block.id, block.topic, block.chapter, displayRuleText(block.text)].filter(Boolean).join(' '), query.value.trim())))
const filteredEntries = computed(() => {
  const entries: RuleCenterEntry[] = tab.value === 'quick-start' ? ruleCenter.value.quickStart : ruleCenter.value.terms
  return entries.filter(item => textSearch([item.id, item.title, item.body, item.tags.join(' ')].join(' '), query.value.trim()))
})
const faqResults = computed(() => allRulings.value.filter(item => {
  const matchesMode = faqMode.value === 'general' ? item.scope === 'general' : item.scope === 'card' || item.scope === 'errata'
  return matchesMode && (!selectedTopics.value.length || selectedTopics.value.some(topic => item.topics.includes(topic))) && searchScore(item, query.value) >= 0
}).sort((left, right) => searchScore(right, query.value) - searchScore(left, query.value) || right.recordedAt.localeCompare(left.recordedAt)))
const topicCards = computed(() => RULE_TOPIC_DEFINITIONS.map(topic => ({ ...topic, count: allRulings.value.filter(item => item.topics.includes(topic.id) && (faqMode.value === 'general' ? item.scope === 'general' : item.scope === 'card' || item.scope === 'errata')).length })))
function scheduleTransition(value?: string | null) {
  if (transitionTimer !== undefined) window.clearTimeout(transitionTimer)
  nextRuleTransitionAt.value = value || ''
  if (!value) return
  const delay = new Date(value).getTime() - Date.now() + 250
  if (delay <= 0) return
  transitionTimer = window.setTimeout(() => { void loadRulesContent() }, Math.min(delay, 2_147_000_000))
}
function loadRulesContent() {
  if (contentRefresh) return contentRefresh
  contentRefresh = (async () => {
    try {
      const result = await getPublicContentBatch(['rules.notice', 'rules.center', 'rules.rulings'])
      ruleNotice.value = (result.values['rules.notice'] || '').trim()
      ruleCenter.value = parsePublishedRuleCenter(result.values['rules.center'] || '')
      dynamicRulings.value = parsePublishedRulings(result.values['rules.rulings'] || '')
      contentError.value = ''
      scheduleTransition(result.nextRuleTransitionAt)
      await revealRouteEntry()
    } catch { contentError.value = '规则资料暂时无法读取。已保留当前页面内容，可点击重试。' }
  })().finally(() => { contentRefresh = undefined })
  return contentRefresh
}
async function loadDynamicContent() {
  loading.value = true
  const [contentResult, operationsResult] = await Promise.allSettled([
    getPublicContentBatch(['rules.notice', 'rules.center', 'rules.rulings']), getEffectiveOperationsPolicy(),
  ])
  if (contentResult.status === 'fulfilled') {
    const values = contentResult.value.values
    ruleNotice.value = (values['rules.notice'] || '').trim()
    ruleCenter.value = parsePublishedRuleCenter(values['rules.center'] || '')
    dynamicRulings.value = parsePublishedRulings(values['rules.rulings'] || '')
    contentError.value = ''
    scheduleTransition(contentResult.value.nextRuleTransitionAt)
  } else contentError.value = '规则资料暂时无法读取。已保留当前页面内容，可点击重试。'
  if (operationsResult.status === 'fulfilled') { policy.value = operationsResult.value; policyError.value = '' }
  else policyError.value = '当前运营限制暂时无法读取；请以稍后重新加载的公开版本为准。'
  loading.value = false
  await revealRouteEntry()
}
async function ensureCardCatalog() {
  if (catalogState.value === 'loading' || catalogState.value === 'ready') return
  catalogState.value = 'loading'
  try { cardCatalog.value = await loadDeckCatalog(); catalogState.value = 'ready' }
  catch { catalogState.value = 'error' }
}
function toggleTopic(topic: RuleTopicId) { selectedTopics.value = selectedTopics.value.includes(topic) ? selectedTopics.value.filter(value => value !== topic) : [...selectedTopics.value, topic] }
async function switchTab(next: MainTab) {
  tab.value = next; query.value = ''; coreTopic.value = 'all'; selectedTopics.value = []; openIds.value = new Set()
  await router.push({ query: { tab: next === 'core' ? undefined : next } })
}
function toggleEntry(id: string) { const next = new Set(openIds.value); next.has(id) ? next.delete(id) : next.add(id); openIds.value = next }
function setAllExpanded(expanded: boolean) { openIds.value = expanded ? new Set(faqResults.value.map(item => item.id)) : new Set() }
function selectKeyword(keyword: string) { query.value = keyword }
function resetCoreFilters() { coreTopic.value = 'all' }
function resetFaqFilters() { selectedTopics.value = [] }
function printRules() { window.print() }
function firstQuery(value: unknown) { return Array.isArray(value) ? String(value[0] || '') : typeof value === 'string' ? value : '' }
async function revealRouteEntry() {
  let entry = firstQuery(route.query.entry)
  if (!entry) return
  const replacement = allRulings.value.find(item => item.supersedes.includes(entry))
  if (replacement) entry = replacement.id
  openIds.value = new Set([entry])
  await nextTick()
  document.getElementById(`rule-entry-${entry}`)?.scrollIntoView({ block: 'center' })
}
function applyRoute() {
  applyingRoute = true
  const requestedTab = firstQuery(route.query.tab) as MainTab
  tab.value = tabIds.has(requestedTab) ? requestedTab : 'core'
  faqMode.value = firstQuery(route.query.mode) === 'card' ? 'card' : 'general'
  query.value = firstQuery(route.query.q)
  selectedTopics.value = firstQuery(route.query.topics).split(',').filter((value): value is RuleTopicId => topicIds.has(value as RuleTopicId))
  const entry = firstQuery(route.query.entry)
  openIds.value = entry ? new Set([entry]) : new Set()
  applyingRoute = false
  void revealRouteEntry()
}
function syncQuery() {
  if (applyingRoute) return
  const entry = [...openIds.value][0]
  void router.replace({ query: {
    tab: tab.value === 'core' ? undefined : tab.value,
    mode: tab.value === 'faq' && faqMode.value === 'card' ? 'card' : undefined,
    q: query.value.trim() || undefined,
    topics: selectedTopics.value.length ? selectedTopics.value.join(',') : undefined,
    entry: entry || undefined,
  } })
}
function onRulesResource() { void loadRulesContent() }
function onVisibility() { if (document.visibilityState === 'visible' && nextRuleTransitionAt.value && new Date(nextRuleTransitionAt.value).getTime() <= Date.now()) void loadRulesContent() }
watch(() => route.query, applyRoute, { immediate: true })
watch([tab, faqMode, query, selectedTopics, openIds], syncQuery, { deep: true })
watch([tab, faqMode, query], () => { if (tab.value === 'faq' && faqMode.value === 'card' && query.value.trim()) void ensureCardCatalog() })
onMounted(() => { void loadDynamicContent(); window.addEventListener('l12-resource-rulesContent', onRulesResource); document.addEventListener('visibilitychange', onVisibility) })
onBeforeUnmount(() => { if (transitionTimer !== undefined) window.clearTimeout(transitionTimer); window.removeEventListener('l12-resource-rulesContent', onRulesResource); document.removeEventListener('visibilitychange', onVisibility) })
</script>

<template>
  <div class="rules-page">
    <header class="rules-head"><div><small>RULES CENTER</small><h1>规则中心</h1><p>浏览当前生效的规则、术语、裁定与赛事规则。</p></div><button v-if="tab === 'core'" @click="printRules">打印 / 保存 PDF</button></header>
    <aside v-if="ruleNotice" class="admin-rule-notice"><b>规则公告</b><span>{{ ruleNotice }}</span></aside>
    <aside v-if="contentError || (tab === 'construction' && policyError)" class="load-error"><span>{{ contentError || policyError }}</span><button :disabled="loading" @click="loadDynamicContent">{{ loading ? '重试中…' : '重试' }}</button></aside>
    <nav class="rule-tabs" aria-label="规则中心栏目"><button v-for="item in tabs" :key="item.id" :class="{ active: tab === item.id }" @click="switchTab(item.id)">{{ item.label }}</button></nav>
    <template v-if="tab === 'core'">
      <section class="rule-tools">
        <input v-model="query" type="search" placeholder="搜索规则章节或关键词">
        <MobileFilterSheet v-model="coreFiltersOpen" title="规则章节筛选" :active-count="coreTopic === 'all' ? 0 : 1" @reset="resetCoreFilters">
          <div class="rule-filter-fields"><label><span>章节</span><select v-model="coreTopic"><option value="all">全部章节</option><option v-for="value in coreTopics" :key="value" :value="value">{{ value }}</option></select></label></div>
          <template #apply-label>查看 {{ ruleResults.length }} 项结果</template>
        </MobileFilterSheet>
        <select v-model="coreTopic" class="rule-desktop-filter" aria-label="按章节筛选"><option value="all">全部章节</option><option v-for="value in coreTopics" :key="value" :value="value">{{ value }}</option></select>
      </section>
      <div class="rule-layout"><aside><b>规则手册</b><span>{{ ruleCenter.coreBlocks.length }} 个规则内容块</span><p>按主题与章节浏览现行规则。</p></aside><main><article v-for="block in ruleResults" :id="`rule-entry-${block.id}`" :key="block.id"><header><span v-if="block.topic">{{ block.topic }}</span><b v-if="block.chapter">{{ block.chapter }}</b></header><p>{{ displayRuleText(block.text) }}</p></article><div v-if="!ruleCenter.coreBlocks.length" class="empty">核心规则资料尚未发布</div><div v-else-if="!ruleResults.length" class="empty">没有匹配的规则内容</div></main></div>
    </template>
    <template v-else-if="tab === 'quick-start' || tab === 'terms'">
      <section class="section-lead"><small>{{ tab === 'quick-start' ? 'QUICK START' : 'GLOSSARY' }}</small><h2>{{ tab === 'quick-start' ? '快速入门' : '术语' }}</h2><p>{{ tab === 'quick-start' ? '从胜利条件、对局准备、回合与进攻开始阅读。具体争议以现行 FAQ 或规则书为准。' : '术语采用《十二军团》规则书的本地用语，不借用其他游戏的术语体系。' }}</p><input v-model="query" :placeholder="tab === 'quick-start' ? '搜索入门主题' : '搜索术语'" /></section>
      <section class="entry-grid"><article v-for="item in filteredEntries" :id="`rule-entry-${item.id}`" :key="item.id" :data-status="item.status"><h3>{{ item.title }}</h3><p>{{ item.body }}</p><div><span v-for="tag in item.tags" :key="tag">{{ tag }}</span></div></article><div v-if="!filteredEntries.length" class="empty">没有匹配的内容</div></section>
    </template>
    <template v-else-if="tab === 'faq'">
      <nav class="faq-mode-tabs" aria-label="FAQ 类型"><button :class="{ active: faqMode === 'general' }" @click="faqMode = 'general'; selectedTopics = []; query = ''"><b>常见问题</b><span>规则主题与通用裁定</span></button><button :class="{ active: faqMode === 'card' }" @click="faqMode = 'card'; selectedTopics = []; query = ''"><b>单卡问答</b><span>已确认的单卡裁定与勘误</span></button></nav>
      <section class="faq-search-panel"><div><small>{{ faqMode === 'general' ? 'FAQ SEARCH' : 'CARD Q&A SEARCH' }}</small><h2>{{ faqMode === 'general' ? '从现行规则裁定中搜索' : '从已确认的单卡裁定中搜索' }}</h2><p>搜索已经发布的通用规则与单卡裁定。</p></div><div class="faq-search-row"><input v-model="query" type="search" :placeholder="faqMode === 'general' ? '输入规则关键词' : '输入官方卡名、卡号或关键词'"><MobileFilterSheet v-model="faqFiltersOpen" title="规则主题筛选" :active-count="selectedTopics.length" @reset="resetFaqFilters"><div class="rule-filter-fields"><div class="topic-checks"><button v-for="topicItem in topicCards" :key="topicItem.id" type="button" :class="{ active: selectedTopics.includes(topicItem.id) }" @click="toggleTopic(topicItem.id)">{{ topicItem.label }}（{{ topicItem.count }}）</button></div><div class="popular-keywords mobile-popular-keywords"><span>常用关键词</span><button v-for="keyword in popularKeywords" :key="keyword" @click="selectKeyword(keyword); faqFiltersOpen = false">{{ keyword }}</button></div></div><template #apply-label>查看 {{ faqResults.length }} 项结果</template></MobileFilterSheet><span v-if="faqMode === 'card' && catalogState === 'loading'" class="catalog-state">正在载入官方卡名…</span><button v-else-if="faqMode === 'card' && catalogState === 'error'" class="catalog-retry" @click="ensureCardCatalog">卡名读取失败，重试</button></div><div class="popular-keywords desktop-popular-keywords"><span>常用关键词</span><button v-for="keyword in popularKeywords" :key="keyword" @click="selectKeyword(keyword)">{{ keyword }}</button></div></section>
      <section class="faq-category-section"><header><h2>按规则主题查看</h2><span>可组合多个固定主题；主题用于检索，不改变裁定效力。</span></header><div class="faq-category-grid"><button v-for="topicItem in topicCards" :key="topicItem.id" :class="{ active: selectedTopics.includes(topicItem.id) }" @click="toggleTopic(topicItem.id)"><small>{{ topicItem.count }} 条</small><b>{{ topicItem.label }}</b></button></div></section>
      <div class="faq-result-bar"><b>{{ faqResults.length }} 条现行裁定</b><div><button @click="setAllExpanded(false)">全部收起</button><button @click="setAllExpanded(true)">全部展开</button></div></div>
      <div class="faq-list"><article v-for="item in faqResults" :id="`rule-entry-${item.id}`" :key="item.id" :class="{ open: openIds.has(item.id) }"><button class="faq-question" :aria-expanded="openIds.has(item.id)" @click="toggleEntry(item.id)"><span class="faq-number">裁定</span><span class="faq-title"><small>{{ item.category }}</small><b>{{ item.question }}</b></span><span class="faq-toggle">{{ openIds.has(item.id) ? '−' : '+' }}</span></button><div v-if="openIds.has(item.id)" class="faq-answer"><strong>答</strong><div><p>{{ item.answer }}</p><div class="ruling-meta"><span>记录：{{ item.recordedAt }}</span><span v-if="item.effectiveAt">生效：{{ new Date(item.effectiveAt).toLocaleString('zh-CN') }}</span></div><div v-if="item.cardIds.length" class="ruling-cards"><span v-for="cardId in item.cardIds" :key="cardId">{{ cardById.get(cardId.toLowerCase())?.nameZh || cardId }} · {{ cardById.get(cardId.toLowerCase())?.number || cardId }}</span></div><div v-if="item.tags.length" class="ruling-tags"><span v-for="tag in item.tags" :key="tag">{{ tag }}</span></div></div></div></article><div v-if="!faqResults.length" class="empty">当前没有符合条件的已确认裁定</div></div>
    </template>
    <template v-else-if="tab === 'construction'">
      <section class="section-lead"><small>DECK CONSTRUCTION</small><h2>构筑与限制</h2><p>查看当前生效的构筑规则与卡牌限制。</p></section>
      <section v-if="policy" class="policy-grid"><article><small>规则版本</small><h3>运营规则 #{{ policy.version }}</h3><p>{{ policy.season.name }} · {{ policy.season.status }}</p></article><article><small>默认对局</small><h3>{{ policy.defaultRoomConfig.matchModeId }}</h3><p>天灾模式：{{ policy.defaultRoomConfig.disasterMode }}</p></article><article class="policy-restrictions"><small>当前卡牌限制</small><h3>{{ policy.cardRestrictions.length ? `${policy.cardRestrictions.length} 项` : '无额外限制' }}</h3><ul v-if="policy.cardRestrictions.length"><li v-for="restriction in policy.cardRestrictions" :key="`${restriction.cardId}-${restriction.masterId || ''}`"><b>{{ restriction.cardId }}</b><span>最多 {{ restriction.maxCopies }} 张</span><em v-if="restriction.reason">{{ restriction.reason }}</em></li></ul><p v-else>仍须遵守核心规则与卡牌自身的构筑文字。</p></article></section><div v-else class="empty compact">{{ policyError || '正在读取当前运营限制…' }}</div>
      <section v-if="constructionRulings.length" class="entry-grid supplemental-rulings"><article v-for="item in constructionRulings" :id="`rule-entry-${item.id}`" :key="item.id"><h3>{{ item.question }}</h3><p>{{ item.answer }}</p></article></section>
    </template>
    <template v-else-if="tab === 'tournament'">
      <section class="section-lead"><small>TOURNAMENT RULES</small><h2>赛事规则</h2><p>查看当前赛事采用的赛制、时限、裁判与申诉规则。</p></section><section class="entry-grid"><article v-for="item in ruleCenter.tournament" :id="`rule-entry-${item.id}`" :key="item.id"><h3>{{ item.title }}</h3><p>{{ item.body }}</p></article><article v-for="item in tournamentRulings" :id="`rule-entry-${item.id}`" :key="item.id"><h3>{{ item.question }}</h3><p>{{ item.answer }}</p></article><div v-if="!ruleCenter.tournament.length && !tournamentRulings.length" class="empty">赛事规则尚未发布</div></section>
    </template>
    <template v-else>
      <section class="section-lead"><small>VERSION HISTORY</small><h2>版本记录</h2><p>查看现行规则的版本记录与生效日期。</p></section><section class="version-list"><article v-for="item in ruleCenter.versions" :id="`rule-entry-${item.id}`" :key="item.id"><div><small>{{ item.kind }} · 已发布</small><h3>{{ item.title }}<span v-if="item.version">{{ item.version }}</span></h3><p>{{ item.summary }}</p></div><aside><span v-if="item.recordedAt">记录：{{ item.recordedAt }}</span><span v-if="item.effectiveAt">生效：{{ item.effectiveAt }}</span></aside></article><div v-if="!ruleCenter.versions.length" class="empty">版本记录尚未发布</div></section>
    </template>
  </div>
</template>

<style scoped>
.rules-page{min-height:100%;max-width:100%;overflow-x:clip;padding:0 clamp(18px,4vw,64px) 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.rules-head{display:flex;align-items:flex-end;justify-content:space-between;gap:20px;padding:32px 0 20px}.rules-head small,.section-lead>small{color:#52c4cb;font:900 14px monospace;letter-spacing:.18em}.rules-head h1{margin:6px 0;font-size:32px}.rules-head p,.section-lead p{margin:0;color:#8d999f;font-size:14px;line-height:1.75}.rules-head button,.faq-result-bar button{padding:10px 14px;border:1px solid #d8ba68;background:#d8ba68;color:#080b0d;font-weight:900}.admin-rule-notice{display:flex;gap:12px;margin:0 0 12px;padding:14px 18px;border:1px solid #6d5a2d;border-left:4px solid #d1ad54;background:#171811}.admin-rule-notice b{color:#edd58d}.admin-rule-notice span{color:#d5d0c2;font-size:14px;line-height:1.7;white-space:pre-line}.rule-tabs{display:flex;overflow-x:auto;border:1px solid #35424a;background:#091016}.rule-tabs button{flex:1 0 max-content;padding:14px 16px;border:0;border-bottom:3px solid transparent;background:transparent;color:#8d989e;font-weight:900}.rule-tabs button.active{border-bottom-color:#d7b85f;background:#41131c;color:#fff}.rule-tools,.faq-search-row{display:grid;grid-template-columns:minmax(0,1fr) 230px;gap:8px;margin:14px 0}.rule-tools input,.rule-tools select,.faq-search-row input,.faq-search-row select,.section-lead input{box-sizing:border-box;min-width:0;padding:12px;border:1px solid #46545c;background:#090f14;color:#fff;font:700 14px 'Microsoft YaHei','微软雅黑',sans-serif}.rule-layout{display:grid;grid-template-columns:240px minmax(0,1fr);gap:12px}.rule-layout>aside,.rule-layout article,.entry-grid article,.policy-grid article,.version-list article{border:1px solid #35424a;background:#101821}.rule-layout>aside{height:max-content;padding:20px}.rule-layout aside b,.rule-layout aside span{display:block}.rule-layout aside b{color:#e0bf6b}.rule-layout aside span{margin-top:5px;color:#7d898e;font-size:14px}.rule-layout aside p{color:#89949a;font-size:14px;line-height:1.8}.rule-layout main{display:flex;flex-direction:column;gap:8px}.rule-layout article{padding:20px}.rule-layout article header{display:flex;gap:8px}.rule-layout article header span{color:#52c4cb;font-size:14px;font-weight:900}.rule-layout article header b{padding:3px 7px;background:#272014;color:#e0c271;font-size:14px}.rule-layout article p{margin:13px 0 0;color:#c0c5c5;font-size:14px;line-height:1.95;white-space:pre-line}.section-lead{margin:20px 0;padding:25px;border:1px solid #35424a;background:linear-gradient(135deg,#121b22,#0c1217)}.section-lead h2{margin:6px 0;font-size:24px}.section-lead input{width:min(680px,100%);margin-top:16px}.entry-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(300px,100%),1fr));gap:12px}.entry-grid article{padding:19px}.entry-grid article[data-status="pending"],.version-list article[data-status="pending"]{border-color:#6d5a2d;background:#171811}.entry-grid small,.version-list small{color:#d7bc69;font-size:13px}.entry-grid h3,.policy-grid h3,.version-list h3{margin:8px 0;font-size:18px}.entry-grid p,.policy-grid p,.version-list p{margin:0;color:#bac3c5;font-size:14px;line-height:1.85;white-space:pre-line}.entry-grid article>div,.ruling-tags{display:flex;flex-wrap:wrap;gap:6px;margin-top:14px}.entry-grid article>div span,.ruling-tags span{padding:3px 7px;background:#16242a;color:#8ad5d8;font-size:12px}.faq-mode-tabs{display:grid;grid-template-columns:1fr 1fr;margin:16px 0;border:1px solid #35424a;background:#090f14}.faq-mode-tabs button{display:flex;flex-direction:column;align-items:flex-start;gap:4px;padding:16px 20px;border:0;border-bottom:3px solid transparent;background:transparent;color:#849198}.faq-mode-tabs button.active{border-bottom-color:#d7b85f;background:#151b20;color:#fff}.faq-search-panel{padding:22px;border:1px solid #35424a;background:linear-gradient(135deg,#121b22,#0c1217)}.faq-search-panel>div:first-child small{color:#cfac55;font:900 14px monospace;letter-spacing:.18em}.faq-search-panel h2{margin:5px 0;font-size:20px}.faq-search-panel p{margin:0;color:#95a1a7;font-size:14px;line-height:1.75}.popular-keywords{display:flex;align-items:center;flex-wrap:wrap;gap:7px;margin-top:13px}.popular-keywords span{color:#839097;font-size:14px}.popular-keywords button,.faq-result-bar button{padding:6px 10px;border:1px solid #43515a;background:#101820;color:#c7cdcf;font-weight:800;font-size:14px}.faq-category-section{margin-top:18px}.faq-category-section>header,.faq-result-bar{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:9px}.faq-category-section h2{margin:0;font-size:16px}.faq-category-section>header span{color:#7c898f;font-size:14px}.faq-category-grid{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}.faq-category-grid button{display:flex;min-height:92px;flex-direction:column;align-items:flex-start;padding:16px;border:1px solid #35424a;background:#101821;color:#fff;text-align:left}.faq-category-grid button.active{border-color:#c5a553;background:#1c1b17}.faq-category-grid small{align-self:flex-end;color:#d6b85e}.faq-category-grid b{margin-top:12px}.faq-result-bar{margin-top:20px}.faq-result-bar>div{display:flex;gap:6px}.faq-list{display:flex;flex-direction:column;gap:7px}.faq-list article{border:1px solid #35424a;background:#101821}.faq-list article.open{border-color:#62614f}.faq-question{display:grid;width:100%;grid-template-columns:64px minmax(0,1fr) 32px;align-items:center;gap:12px;padding:16px;border:0;background:transparent;color:#fff;text-align:left}.faq-number{color:#dfc46f;font:900 14px monospace}.faq-title{display:flex;min-width:0;flex-direction:column;gap:5px}.faq-title small{color:#68cbd0;font-size:13px;font-weight:900}.faq-title b{font-size:14px;line-height:1.55}.faq-toggle{display:grid;width:28px;height:28px;place-items:center;border:1px solid #47555d;color:#d7bd6a;font-size:18px}.faq-answer{display:grid;grid-template-columns:64px minmax(0,1fr);gap:12px;padding:17px 16px;border-top:1px solid #2e3940;background:#0b1116}.faq-answer>strong{color:#62c6cc;font:900 14px monospace}.faq-answer p{margin:0;color:#c0c6c7;font-size:14px;line-height:1.9;white-space:pre-line}.ruling-meta{display:flex;flex-wrap:wrap;gap:8px 16px;margin-top:12px;color:#87969d;font-size:12px}.policy-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.policy-grid article{padding:20px}.policy-grid small{color:#d6b85e}.policy-restrictions{grid-column:span 2}.policy-restrictions ul{display:grid;gap:8px;margin:12px 0 0;padding:0;list-style:none}.policy-restrictions li{display:grid;grid-template-columns:110px 120px 1fr;gap:8px;padding:10px;background:#0a1116;color:#c5cdcf;font-size:13px}.policy-restrictions li b{color:#e3c56d}.policy-restrictions li em{color:#9aa7aa;font-style:normal}.version-list{display:flex;flex-direction:column;gap:10px}.version-list article{display:flex;justify-content:space-between;gap:24px;padding:20px}.version-list h3 span{margin-left:8px;color:#d7bc69;font-size:14px}.version-list aside{display:flex;min-width:230px;flex-direction:column;gap:6px;color:#8f9ba0;font-size:13px}.version-list aside b{color:#d3bb75;font-weight:700}.empty{display:grid;min-height:220px;place-items:center;border:1px dashed #35424a;color:#718087}.empty.compact{min-height:110px}
@media(max-width:700px){.rule-layout,.rule-tools,.faq-search-row,.policy-grid{grid-template-columns:1fr}.rule-layout>aside{position:static}.faq-category-grid{grid-template-columns:repeat(2,1fr)}.policy-restrictions{grid-column:auto}.version-list article{flex-direction:column}.version-list aside{min-width:0}.rules-head{align-items:flex-start;flex-direction:column}.policy-restrictions li{grid-template-columns:1fr}}
@media(max-width:520px){.rules-page{padding:0 12px 48px}.rule-tabs{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));overflow:visible}.rule-tabs button{min-width:0;padding:12px 8px;line-height:1.35;white-space:normal}.faq-mode-tabs button{padding:13px 12px}.faq-category-grid{grid-template-columns:1fr}.faq-question{grid-template-columns:48px minmax(0,1fr) 28px;padding:14px 10px;gap:8px}.faq-answer{grid-template-columns:48px minmax(0,1fr);padding:15px 10px;gap:8px}}
@media(max-width:520px){.rules-head{gap:10px;padding:20px 0 12px}.rules-head small,.section-lead>small{font-size:11px}.rules-head h1{margin:4px 0;font-size:26px}.rules-head p,.section-lead p{font-size:12px;line-height:1.65}.rules-head button{padding:9px 12px;font-size:13px}.rule-tabs button{min-height:42px;padding:9px 6px;font-size:12px}.rule-tools{gap:7px;margin:10px 0}.rule-tools input,.rule-tools select,.faq-search-row input,.faq-search-row select,.section-lead input{padding:10px;font-size:12px}.rule-layout>aside,.rule-layout article,.entry-grid article{padding:15px}.rule-layout aside span,.rule-layout aside p,.rule-layout article header span,.rule-layout article header b,.rule-layout article p{font-size:12px}.section-lead{margin:14px 0;padding:17px}.section-lead h2{font-size:20px}.faq-mode-tabs{margin:12px 0}.faq-mode-tabs button{padding:11px 10px;font-size:12px}.faq-search-panel{padding:16px}.faq-search-panel>div:first-child small,.faq-search-panel p,.popular-keywords span,.popular-keywords button,.faq-category-section>header span,.faq-result-bar button{font-size:12px}.faq-search-panel h2{font-size:18px}.faq-category-grid button{min-height:70px;padding:12px}.faq-category-grid small{font-size:11px}.faq-category-grid b{margin-top:7px;font-size:13px}.faq-result-bar{margin-top:14px}.faq-title b,.faq-title small,.faq-number,.faq-answer p{font-size:12px}}
.rule-filter-fields{display:grid;gap:12px}.rule-filter-fields label{display:grid;gap:6px;color:#aeb8ba;font-size:12px;font-weight:900}.rule-filter-fields select{box-sizing:border-box;min-width:0;width:100%;padding:10px;border:1px solid #46545c;background:#090f14;color:#fff;font-size:12px}.mobile-popular-keywords{margin-top:2px}.mobile-popular-keywords>span{width:100%}
@media(max-width:700px){.rule-tools,.faq-search-row{grid-template-columns:minmax(0,1fr) auto}.rule-desktop-filter,.desktop-popular-keywords{display:none}.rule-tools input,.faq-search-row input{min-width:0}.faq-search-panel{overflow-x:clip}}
@media print{.rules-page{padding:0;color:#111}.rules-head button,.rule-tabs,.rule-tools,.rule-layout>aside{display:none}.rule-layout{display:block}.rule-layout article{break-inside:avoid;border:0;border-bottom:1px solid #ccc;background:#fff}.rule-layout article p{color:#111}}
.load-error{display:flex;align-items:center;justify-content:space-between;gap:12px;margin:0 0 12px;padding:14px 18px;border:1px solid #7b4747;border-left:4px solid #d36b6b;background:#211316}.load-error span{color:#e4caca;font-size:14px;line-height:1.7}.load-error button{padding:8px 14px;border:1px solid #9a6262;background:#281417;color:#f0c0c0;font-weight:900}.topic-checks{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:7px}.topic-checks button{padding:9px;border:1px solid #43515a;background:#101820;color:#c7cdcf}.topic-checks button.active{border-color:#c5a553;background:#2b2515;color:#f0d47b}.catalog-state{align-self:center;color:#92a0a5;font-size:12px}.catalog-retry{padding:8px;border:1px solid #8c5a5a;background:#241316;color:#efbcbc;font-size:12px;font-weight:800}.ruling-cards{display:flex;flex-wrap:wrap;gap:6px;margin-top:12px}.ruling-cards span{padding:4px 8px;border:1px solid #35565c;color:#98dce0;font-size:12px}.supplemental-rulings{margin-top:12px}
</style>
