<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import { deckLibraryApi, type PublicDeckSummary, type PublicDeckSummaryQuery } from '@/l12/platform'
import DeckCover from '@/l12/DeckCover.vue'
import { landscapeTeleportTarget } from '@/l12/mobileViewport'
import { articlePublicDeckReference } from './articleBlocks'
import { createSummaryRequestGate, validateSummaryPage } from './publicDeckSummary'

const props = withDefaults(defineProps<{ title?: string; selectedReference?: string }>(), {
  title: '选择公共牌库', selectedReference: '',
})
const emit = defineEmits<{ select: [reference: string, deck: PublicDeckSummary]; close: [] }>()
const PAGE_SIZE = 12
const catalog = ref<DeckCard[]>([])
const catalogUnavailable = ref(false)
const items = ref<PublicDeckSummary[]>([])
const total = ref(0)
const page = ref(1)
const searchDraft = ref('')
const keyword = ref('')
const sort = ref<'trend' | 'latest' | 'name'>('trend')
const state = ref<'loading' | 'available' | 'unavailable'>('loading')
const errorText = ref('')
let disposed = false

const query = (): PublicDeckSummaryQuery => ({
  source: 'public', page: page.value, pageSize: PAGE_SIZE,
  keyword: keyword.value || undefined, sort: sort.value,
})
const requestGate = createSummaryRequestGate(() => JSON.stringify(query()))
const pageCount = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))

function publicReference(deck: PublicDeckSummary) {
  return deck.source === 'public' ? articlePublicDeckReference(deck.publicCode || '') : ''
}
function choose(deck: PublicDeckSummary) {
  const reference = publicReference(deck)
  if (reference) emit('select', reference, deck)
}
async function loadPage() {
  const request = query()
  const current = requestGate.begin()
  state.value = 'loading'; errorText.value = ''; items.value = []
  try {
    const response = validateSummaryPage(await deckLibraryApi.summaries(request), request)
    if (!current()) return
    items.value = response.items.filter(deck => Boolean(publicReference(deck)))
    total.value = response.total
    state.value = 'available'
    if (page.value > Math.max(1, Math.ceil(response.total / PAGE_SIZE))) page.value = Math.max(1, Math.ceil(response.total / PAGE_SIZE))
  } catch (error) {
    if (current()) {
      state.value = 'unavailable'
      errorText.value = error instanceof Error ? error.message : '公共牌库当前无法读取'
    }
  }
}
function applySearch() {
  const next = searchDraft.value.trim()
  if (page.value === 1 && keyword.value === next) { void loadPage(); return }
  keyword.value = next
  page.value = 1
}
function changeSort() { page.value = 1 }
function retry() { void loadPage() }
function close() { requestGate.invalidate(); emit('close') }

watch([page, keyword, sort], () => { void loadPage() }, { immediate: true })
void loadDeckCatalog().then(cards => { if (!disposed) catalog.value = cards })
  .catch(() => { if (!disposed) catalogUnavailable.value = true })
onBeforeUnmount(() => { disposed = true; requestGate.dispose() })
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div class="public-deck-picker-mask" @click.self="close">
      <section class="public-deck-picker" role="dialog" aria-modal="true" :aria-label="title">
        <header><div><small>公共牌库</small><h2>{{ title }}</h2></div><button type="button" aria-label="关闭" @click="close">×</button></header>
        <form class="public-deck-picker-tools" role="search" @submit.prevent="applySearch">
          <input v-model="searchDraft" type="search" aria-label="搜索公共牌库" placeholder="搜索牌库名称、作者或主宰" autofocus>
          <button type="submit">搜索</button>
          <select v-model="sort" aria-label="公共牌库排序" @change="changeSort"><option value="trend">综合热度</option><option value="latest">最新发布</option><option value="name">按名称</option></select>
        </form>
        <p class="public-deck-picker-result" :role="state === 'unavailable' ? 'alert' : 'status'">
          <template v-if="state === 'loading'">正在读取公共牌库…</template>
          <template v-else-if="state === 'unavailable'">{{ errorText }} <button type="button" @click="retry">重试</button></template>
          <template v-else>{{ total }} 个公开牌库<span v-if="catalogUnavailable"> · 卡面暂不可用，仍可选择</span></template>
        </p>
        <div v-if="state === 'available'" class="public-deck-picker-grid">
          <button v-for="deck in items" :key="deck.id" type="button" class="public-deck-picker-option"
            :class="{ selected: publicReference(deck) === selectedReference }"
            :aria-label="`${publicReference(deck) === selectedReference ? '当前引用，' : ''}选择公共牌库《${deck.name}》，作者：${deck.author}，短码：${deck.publicCode}`"
            :aria-current="publicReference(deck) === selectedReference ? 'true' : undefined" @click="choose(deck)">
            <DeckCover :name="deck.name" :master-id="deck.masterId" :master-name="deck.masterName" :faction="deck.faction"
              :catalog="catalog" :cover-card-ids="deck.coverCardIds" :context="deck.author"
              :meta="`${deck.counts.main} 主牌 · ${deck.counts.morale} 士气`" badge="公共牌库"/>
            <span class="public-deck-picker-copy">
              <b>{{ deck.name }}</b><span>作者：{{ deck.author }}</span><span>短码：{{ deck.publicCode }}</span>
            </span>
            <span v-if="publicReference(deck) === selectedReference" class="public-deck-picker-current">当前引用</span>
          </button>
          <p v-if="!items.length" class="public-deck-picker-empty">没有符合条件的公共牌库。</p>
        </div>
        <nav v-if="state === 'available' && total > PAGE_SIZE" class="public-deck-picker-pages" aria-label="公共牌库分页">
          <button type="button" :disabled="page === 1" @click="page--">上一页</button><span>第 {{ page }} / {{ pageCount }} 页</span><button type="button" :disabled="page === pageCount" @click="page++">下一页</button>
        </nav>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.public-deck-picker-mask{position:fixed;z-index:5200;inset:0;display:grid;padding:clamp(8px,3vw,36px);background:#020507dc;place-items:center}.public-deck-picker{display:grid;box-sizing:border-box;width:min(1120px,96vw);height:min(820px,92dvh);grid-template-rows:auto auto auto minmax(0,1fr) auto;overflow:hidden;border:1px solid var(--l12-ui-accent-line,#6a5a2a);border-radius:var(--l12-ui-radius-lg,8px);background:var(--l12-ui-panel,#0b1116);color:var(--l12-ui-text,#eff2ef);box-shadow:var(--l12-ui-shadow-dialog,0 30px 100px #000)}.public-deck-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid var(--l12-ui-line,#374147)}.public-deck-picker h2{margin:4px 0 0;font-size:22px}.public-deck-picker small{color:var(--l12-ui-accent,#cfad43);font-size:12px;font-weight:900;letter-spacing:.08em}.public-deck-picker>header button{width:40px;height:40px;border:1px solid var(--l12-ui-line-strong,#48545c);background:var(--l12-ui-control,#080d11);color:#fff;font-size:24px}.public-deck-picker-tools{display:grid;grid-template-columns:minmax(180px,1fr) auto minmax(130px,auto);gap:8px;padding:12px;border-bottom:1px solid var(--l12-ui-line,#303a40)}.public-deck-picker :is(input,select,button){box-sizing:border-box;min-height:42px;padding:8px 11px;border:1px solid var(--l12-ui-line-strong,#445159);background:var(--l12-ui-control,#070c10);color:#fff;font:inherit}.public-deck-picker select{color-scheme:dark}.public-deck-picker button{font-weight:900;cursor:pointer}.public-deck-picker button:disabled{cursor:not-allowed;opacity:.45}.public-deck-picker-result{min-height:20px;margin:0;padding:8px 14px;color:var(--l12-ui-text-muted,#87959b);font-size:12px}.public-deck-picker-result button{min-height:30px;margin-left:8px;padding:4px 9px}.public-deck-picker-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));align-content:start;gap:10px;overflow-y:auto;overflow-x:hidden;padding:12px}.public-deck-picker-option{position:relative;min-width:0;padding:0!important;border-color:var(--l12-ui-line,#39464d)!important;background:transparent!important;text-align:left}.public-deck-picker-option:hover,.public-deck-picker-option:focus-visible,.public-deck-picker-option.selected{border-color:var(--l12-ui-accent,#e1bd50)!important;outline:0;box-shadow:0 0 14px #c598383d}.public-deck-picker-option :deep(.deck-cover){width:100%}.public-deck-picker-option>span{position:absolute;top:8px;right:8px;padding:4px 7px;background:#d4b34e;color:#090c0e;font-size:11px;font-weight:900}.public-deck-picker-empty{grid-column:1/-1;color:var(--l12-ui-text-muted,#87959b);text-align:center}.public-deck-picker-pages{display:flex;align-items:center;justify-content:center;gap:10px;padding:10px 12px;border-top:1px solid var(--l12-ui-line,#303a40)}.public-deck-picker-pages span{color:var(--l12-ui-text-muted,#87959b);font-size:13px}
.public-deck-picker-option>.public-deck-picker-copy{position:static;display:grid;min-width:0;gap:4px;padding:10px 12px;background:transparent;color:inherit;white-space:normal;overflow-wrap:anywhere}.public-deck-picker-copy>b{font-size:15px;line-height:1.4}.public-deck-picker-copy>span{font-size:12px;line-height:1.5;color:var(--l12-ui-text-muted,#a8b4ba)}
@media(max-width:700px){.public-deck-picker-mask{padding:max(6px,env(safe-area-inset-top)) max(6px,env(safe-area-inset-right)) max(6px,env(safe-area-inset-bottom)) max(6px,env(safe-area-inset-left))}.public-deck-picker{width:100%;height:100%;max-height:100%;border-radius:0}.public-deck-picker>header{min-height:44px;padding:4px 7px}.public-deck-picker>header small{display:none}.public-deck-picker h2{margin:0;font-size:16px}.public-deck-picker>header button{width:44px;height:44px;padding:0}.public-deck-picker-tools{grid-template-columns:minmax(0,1fr) auto;padding:6px}.public-deck-picker-tools select{grid-column:1/-1}.public-deck-picker-grid{grid-template-columns:1fr;gap:7px;padding:7px}.public-deck-picker-result{padding:5px 8px}.public-deck-picker-pages{padding:6px}.public-deck-picker-pages button{min-height:44px}}
</style>
