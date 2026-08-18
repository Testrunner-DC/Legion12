<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RULE_BLOCKS } from '@/l12/data/officialRules'
import { QA_ENTRIES, type QAEntry } from '@/l12/data/officialFaq'
import { getPublicContent } from '@/l12/platform'

type MainTab = 'rulebook' | 'faq'
type FaqMode = 'general' | 'card'

const tab = ref<MainTab>('rulebook')
const faqMode = ref<FaqMode>('general')
const query = ref('')
const topic = ref('all')
const faqType = ref('all')
const faqCategory = ref('all')
const openIds = ref<Set<number>>(new Set())
const ruleNotice = ref('')
onMounted(async () => { try { ruleNotice.value = (await getPublicContent('rules.notice')).value.trim() } catch {} })

const topics = computed(() => [...new Set(RULE_BLOCKS.map(block => block.topic).filter(Boolean))] as string[])
const faqTypes = computed(() => [...new Set(QA_ENTRIES.map(entry => entry.type))])

const categoryDefinitions = [
  { id: 'setup', label: '准备与基本规则', description: '游戏准备、构筑、调度与回合流程等基础问题', keywords: ['游戏准备', '对局准备', '构筑', '牌库数量', '手牌上限', '调度', '先手', '后手', '士气上限', '回合流程'] },
  { id: 'battle', label: '进攻与战斗', description: '进攻目标、距离、支援、击杀与伤害结算', keywords: ['进攻', '支援', '击杀', '兵力', '伤害', '强攻', '震击', '远程'] },
  { id: 'effects', label: '效果与响应', description: '发动、费用、时点、触发、堆叠与无效', keywords: ['效果', '响应', '堆叠', '触发', '发动', '无效', '持续', '登场'] },
  { id: 'zones', label: '区域与状态', description: '战场、墓地、圣物区、活跃、休整与离场', keywords: ['区域', '战场', '墓地', '圣物', '活跃', '休整', '离场', '置入', '位置'] },
  { id: 'rulings', label: '单卡裁定与勘误', description: '卡牌调整、文字勘误及具体卡牌互动', keywords: ['勘误', '卡牌调整', '规则书问题'] },
] as const

const popularKeywords = ['堆叠', '进攻', '士气', '天灾', '登场', '阵亡']

function entryText(entry: QAEntry) {
  return [entry.type, entry.question, entry.answer, entry.status, entry.note].filter(Boolean).join(' ')
}

function entryCategory(entry: QAEntry) {
  if (entry.type !== 'q&a') return 'rulings'
  const text = entryText(entry)
  return categoryDefinitions.find(category => category.id !== 'rulings' && category.keywords.some(keyword => text.includes(keyword)))?.id ?? 'effects'
}

const ruleResults = computed(() => RULE_BLOCKS.filter(block =>
  (topic.value === 'all' || block.topic === topic.value)
  && (!query.value.trim() || [block.topic, block.chapter, block.text].some(value => value?.includes(query.value.trim()))),
))

const faqResults = computed(() => QA_ENTRIES.filter(entry => {
  const matchesType = faqType.value === 'all' || entry.type === faqType.value
  const matchesCategory = faqCategory.value === 'all' || entryCategory(entry) === faqCategory.value
  const matchesQuery = !query.value.trim() || entryText(entry).toLocaleLowerCase().includes(query.value.trim().toLocaleLowerCase())
  return matchesType && matchesCategory && matchesQuery
}))

const categoryCards = computed(() => categoryDefinitions.map(category => ({
  ...category,
  count: QA_ENTRIES.filter(entry => entryCategory(entry) === category.id).length,
})))

watch([tab, faqMode, faqType, faqCategory, query], () => {
  openIds.value = new Set()
})

function switchTab(next: MainTab) {
  tab.value = next
  query.value = ''
  topic.value = 'all'
  faqType.value = 'all'
  faqCategory.value = 'all'
}

function switchFaqMode(next: FaqMode) {
  faqMode.value = next
  query.value = ''
  faqType.value = 'all'
  faqCategory.value = 'all'
}

function selectKeyword(keyword: string) {
  query.value = keyword
}

function toggleEntry(id: number) {
  const next = new Set(openIds.value)
  next.has(id) ? next.delete(id) : next.add(id)
  openIds.value = next
}

function setAllExpanded(expanded: boolean) {
  openIds.value = expanded ? new Set(faqResults.value.map(entry => entry.id)) : new Set()
}

function printRules() { window.print() }
</script>

<template>
  <div class="rules-page">
    <header>
      <div>
        <small>RULES CENTER</small>
        <h1>规则</h1>
        <p>原文逐页文字版与 FAQ 裁定集中检索；不对术语进行二次翻译。</p>
      </div>
      <button v-if="tab === 'rulebook'" @click="printRules">打印 / 保存 PDF</button>
    </header>

    <aside v-if="ruleNotice" class="admin-rule-notice"><b>规则公告</b><span>{{ ruleNotice }}</span></aside>
    <div class="rule-tabs">
      <button :class="{ active: tab === 'rulebook' }" @click="switchTab('rulebook')">最新规则书</button>
      <button :class="{ active: tab === 'faq' }" @click="switchTab('faq')">FAQ</button>
    </div>

    <template v-if="tab === 'rulebook'">
      <section class="rule-tools">
        <input v-model="query" placeholder="搜索规则章节或原文">
        <select v-model="topic">
          <option value="all">全部章节</option>
          <option v-for="value in topics" :key="value" :value="value">{{ value }}</option>
        </select>
      </section>
      <div class="rule-layout">
        <aside>
          <b>Handbook Ver2.0</b>
          <span>{{ RULE_BLOCKS.length }} 个原文内容块</span>
          <p>来源为《L12-规则书-文本(0215)》逐页转录。当前项目尚未取得原始规则书 PDF，因此这里提供可搜索、可打印的最新文字版。</p>
        </aside>
        <main>
          <article v-for="(block,index) in ruleResults" :key="`${block.page}-${index}`">
            <header>
              <span v-if="block.topic">{{ block.topic }}</span>
              <b v-if="block.chapter">{{ block.chapter }}</b>
              <small v-if="block.page">P.{{ block.page }}</small>
            </header>
            <p>{{ block.text }}</p>
          </article>
          <div v-if="!ruleResults.length" class="empty">没有匹配的规则内容</div>
        </main>
      </div>
    </template>

    <template v-else>
      <nav class="faq-mode-tabs" aria-label="FAQ 类型">
        <button :class="{ active: faqMode === 'general' }" @click="switchFaqMode('general')">
          <b>常见问题</b><span>按规则主题检索</span>
        </button>
        <button :class="{ active: faqMode === 'card' }" @click="switchFaqMode('card')">
          <b>单卡问答</b><span>按卡名与卡牌文本检索</span>
        </button>
      </nav>

      <section class="faq-search-panel">
        <div>
          <small>{{ faqMode === 'general' ? 'FAQ SEARCH' : 'CARD Q&A SEARCH' }}</small>
          <h2>{{ faqMode === 'general' ? '从常见问题中搜索' : '从单卡问答中搜索' }}</h2>
        </div>
        <div class="faq-search-row">
          <input v-model="query" :placeholder="faqMode === 'general' ? '输入规则关键词' : '输入卡名；卡号映射完成后可按卡号搜索'">
          <select v-if="faqMode === 'general'" v-model="faqCategory">
            <option value="all">全部分类</option>
            <option v-for="category in categoryCards" :key="category.id" :value="category.id">{{ category.label }}</option>
          </select>
          <select v-else v-model="faqType">
            <option value="all">全部资料类型</option>
            <option v-for="value in faqTypes" :key="value" :value="value">{{ value }}</option>
          </select>
        </div>
        <div class="popular-keywords">
          <span>常用关键词</span>
          <button v-for="keyword in popularKeywords" :key="keyword" @click="selectKeyword(keyword)">{{ keyword }}</button>
        </div>
      </section>

      <section v-if="faqMode === 'general'" class="faq-category-section">
        <header><h2>按分类查看 FAQ</h2><span>分类仅用于检索，不改变任何原始裁定文字</span></header>
        <div class="faq-category-grid">
          <button v-for="category in categoryCards" :key="category.id" :class="{ active: faqCategory === category.id }" @click="faqCategory = faqCategory === category.id ? 'all' : category.id">
            <small>{{ category.count }} 条</small>
            <b>{{ category.label }}</b>
            <span>{{ category.description }}</span>
          </button>
        </div>
      </section>

      <section v-else class="card-data-notice">
        <b>单卡问答资料结构已建立</b>
        <p>当前已导入 {{ QA_ENTRIES.length }} 条原始 FAQ，尚未包含统一的卡号与收录季字段，因此先提供卡名与全文检索。待卡牌档案完成映射后，将按 S1、S2 与后续产品分组显示；这里不会猜测卡号或收录信息。</p>
      </section>

      <div class="faq-result-bar">
        <b>{{ faqResults.length }} 条结果</b>
        <div><button @click="setAllExpanded(false)">全部收起</button><button @click="setAllExpanded(true)">全部展开</button></div>
      </div>

      <div class="faq-list">
        <article v-for="entry in faqResults" :key="entry.id" :class="{ open: openIds.has(entry.id) }">
          <button class="faq-question" :aria-expanded="openIds.has(entry.id)" @click="toggleEntry(entry.id)">
            <span class="faq-number">Q {{ entry.id }}</span>
            <span class="faq-title"><small>{{ entry.type }}</small><b>{{ entry.question }}</b></span>
            <span class="faq-toggle">{{ openIds.has(entry.id) ? '−' : '+' }}</span>
          </button>
          <div v-if="openIds.has(entry.id)" class="faq-answer">
            <strong>A</strong>
            <div><p>{{ entry.answer }}</p><small v-if="entry.status && entry.status !== '/'">{{ entry.status }}</small></div>
          </div>
        </article>
        <div v-if="!faqResults.length" class="empty">没有匹配的 FAQ</div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.admin-rule-notice{display:flex;align-items:center;gap:12px;margin:0 0 12px;padding:14px 18px;border:1px solid #6d5a2d;border-left:4px solid #d1ad54;background:#171811}.admin-rule-notice b{flex:0 0 auto;color:#edd58d}.admin-rule-notice span{color:#d5d0c2;font-size:12px;line-height:1.7;white-space:pre-line}.rules-page{min-height:100%;padding:0 clamp(18px,4vw,64px) 60px;font-family:'Microsoft YaHei','微软雅黑',sans-serif}.rules-page>header{display:flex;align-items:flex-end;justify-content:space-between;padding:32px 0 20px}.rules-page>header small{color:#52c4cb;font:900 9px monospace;letter-spacing:.2em}.rules-page h1{margin:6px 0;font-size:32px}.rules-page>header p{margin:0;color:#8d999f;font-size:12px}.rules-page>header button{padding:10px 14px;border:1px solid #d8ba68;background:#d8ba68;color:#080b0d;font-weight:900}.rule-tabs{display:grid;grid-template-columns:1fr 1fr;border:1px solid #35424a;background:#091016}.rule-tabs button{padding:14px;border:0;background:transparent;color:#8d989e;font-weight:900}.rule-tabs button.active{background:linear-gradient(135deg,#7e1825,#aa2b37);color:#fff}.rule-tools{display:grid;grid-template-columns:1fr 230px;gap:8px;margin:12px 0}.rule-tools input,.rule-tools select,.faq-search-row input,.faq-search-row select{min-width:0;padding:12px;border:1px solid #46545c;background:#090f14;color:#fff;font:700 12px 'Microsoft YaHei','微软雅黑',sans-serif}.rule-layout{display:grid;grid-template-columns:240px minmax(0,1fr);gap:12px}.rule-layout>aside{height:max-content;padding:20px;border:1px solid #35424a;background:#101821}.rule-layout aside b,.rule-layout aside span{display:block}.rule-layout aside b{color:#e0bf6b}.rule-layout aside span{margin-top:5px;color:#7d898e;font-size:9px}.rule-layout aside p{color:#89949a;font-size:10px;line-height:1.8}.rule-layout main{display:flex;flex-direction:column;gap:8px}.rule-layout article{padding:20px;border:1px solid #35424a;background:#101821}.rule-layout article header{display:flex;align-items:center;gap:8px}.rule-layout article header span{color:#52c4cb;font-size:9px;font-weight:900}.rule-layout article header b{padding:3px 7px;background:#272014;color:#e0c271;font-size:10px}.rule-layout article header small{margin-left:auto;color:#68757c}.rule-layout article p{margin:13px 0 0;color:#c0c5c5;font-size:12px;line-height:1.95;white-space:pre-line}.faq-mode-tabs{display:grid;grid-template-columns:1fr 1fr;margin:12px 0;border:1px solid #35424a;background:#090f14}.faq-mode-tabs button{display:flex;flex-direction:column;align-items:flex-start;gap:4px;padding:16px 20px;border:0;border-bottom:3px solid transparent;background:transparent;color:#849198}.faq-mode-tabs button b{font-size:14px}.faq-mode-tabs button span{font-size:10px}.faq-mode-tabs button.active{border-bottom-color:#d7b85f;background:#151b20;color:#fff}.faq-search-panel{padding:22px;border:1px solid #35424a;background:linear-gradient(135deg,#121b22,#0c1217)}.faq-search-panel>div:first-child small{color:#cfac55;font:900 9px monospace;letter-spacing:.18em}.faq-search-panel h2{margin:5px 0 16px;font-size:20px}.faq-search-row{display:grid;grid-template-columns:minmax(0,1fr) 240px;gap:8px}.popular-keywords{display:flex;align-items:center;flex-wrap:wrap;gap:7px;margin-top:13px}.popular-keywords span{margin-right:4px;color:#839097;font-size:10px}.popular-keywords button,.faq-result-bar button{padding:6px 10px;border:1px solid #43515a;background:#101820;color:#c7cdcf;font-weight:800;font-size:10px}.popular-keywords button:hover,.faq-result-bar button:hover{border-color:#d4b45b;color:#f1d985}.faq-category-section{margin-top:18px}.faq-category-section>header{display:flex;align-items:baseline;justify-content:space-between;margin-bottom:9px}.faq-category-section h2{margin:0;font-size:16px}.faq-category-section>header span{color:#7c898f;font-size:10px}.faq-category-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:8px}.faq-category-grid button{position:relative;display:flex;min-height:112px;flex-direction:column;align-items:flex-start;padding:16px;border:1px solid #35424a;background:#101821;color:#fff;text-align:left}.faq-category-grid button:hover,.faq-category-grid button.active{border-color:#c5a553;background:#1c1b17}.faq-category-grid small{align-self:flex-end;color:#d6b85e;font-size:9px}.faq-category-grid b{margin:10px 0 5px;font-size:12px}.faq-category-grid span{color:#849197;font-size:9px;line-height:1.6}.card-data-notice{margin-top:16px;padding:17px 20px;border-left:3px solid #d1ad54;background:#171811}.card-data-notice b{color:#edd58d;font-size:13px}.card-data-notice p{margin:7px 0 0;color:#aab0ac;font-size:10px;line-height:1.8}.faq-result-bar{display:flex;align-items:center;justify-content:space-between;margin:20px 0 8px}.faq-result-bar>b{font-size:12px}.faq-result-bar>div{display:flex;gap:6px}.faq-list{display:flex;flex-direction:column;gap:7px}.faq-list article{border:1px solid #35424a;background:#101821}.faq-list article.open{border-color:#62614f}.faq-question{display:grid;width:100%;grid-template-columns:64px minmax(0,1fr) 32px;align-items:center;gap:12px;padding:16px;border:0;background:transparent;color:#fff;text-align:left}.faq-number{color:#dfc46f;font:900 12px monospace}.faq-title{display:flex;min-width:0;flex-direction:column;gap:5px}.faq-title small{color:#68cbd0;font-size:9px;font-weight:900}.faq-title b{font-size:13px;line-height:1.55}.faq-toggle{display:grid;width:28px;height:28px;place-items:center;border:1px solid #47555d;color:#d7bd6a;font-size:18px}.faq-answer{display:grid;grid-template-columns:64px minmax(0,1fr);gap:12px;padding:17px 16px;border-top:1px solid #2e3940;background:#0b1116}.faq-answer>strong{color:#62c6cc;font:900 14px monospace}.faq-answer p{margin:0;color:#c0c6c7;font-size:11px;line-height:1.9;white-space:pre-line}.faq-answer small{display:block;margin-top:10px;color:#e0afb3}.empty{display:grid;min-height:250px;place-items:center;border:1px dashed #35424a;color:#718087}
@media(max-width:1050px){.faq-category-grid{grid-template-columns:repeat(3,1fr)}}
@media(max-width:760px){.rules-page{padding:0 12px 48px}.rules-page>header{align-items:flex-start;flex-direction:column;gap:14px}.rule-tools,.rule-layout,.faq-search-row{grid-template-columns:1fr}.rule-layout>aside{position:static}.faq-category-grid{grid-template-columns:1fr 1fr}.faq-category-section>header{align-items:flex-start;flex-direction:column;gap:5px}.faq-question{grid-template-columns:48px minmax(0,1fr) 28px;padding:14px 10px;gap:8px}.faq-answer{grid-template-columns:48px minmax(0,1fr);padding:15px 10px;gap:8px}}
@media(max-width:440px){.faq-category-grid{grid-template-columns:1fr}.faq-mode-tabs button{padding:13px 12px}.faq-search-panel{padding:16px}.popular-keywords span{width:100%}}
@media print{.rules-page{padding:0;color:#111}.official-nav,.rules-page>header button,.rule-tabs,.rule-tools,.rule-layout>aside{display:none}.rule-layout{display:block}.rule-layout article{break-inside:avoid;border:0;border-bottom:1px solid #ccc;background:#fff}.rule-layout article p{color:#111}}
</style>
