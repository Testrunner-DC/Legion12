<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import DeckProfile from './DeckProfile.vue'
import { deckCountSummary, loadPrivateDeckSummaryPage, validateDeck, type DeckCard, type L12DeckSelectionScope,
  type PrivateDeckSummary, type PrivateDeckSummaryPage, type SavedL12Deck } from './decks'
import { platformState, type OperationsCardRestriction } from './platform'
import { landscapeTeleportTarget } from './mobileViewport'

type DeckSelectionCandidate = PrivateDeckSummary | SavedL12Deck

const props = withDefaults(defineProps<{
  open: boolean
  mode: L12DeckSelectionScope
  guestDecks?: SavedL12Deck[]
  catalog: DeckCard[]
  currentDeckId?: string
  currentDeckRevision?: number
  currentDeckName?: string
  restrictions?: readonly OperationsCardRestriction[]
  usesSeasonRestrictions?: boolean
  loading?: boolean
  disabled?: boolean
  confirming?: boolean
  actionError?: string
}>(), {
  guestDecks: () => [],
  currentDeckId: '',
  currentDeckRevision: 0,
  currentDeckName: '',
  restrictions: () => [],
  usesSeasonRestrictions: false,
  loading: false,
  disabled: false,
  confirming: false,
  actionError: '',
})

const emit = defineEmits<{
  cancel: []
  confirm: [deck: DeckSelectionCandidate]
}>()

const draftName = ref('')
const draftId = ref('')
const draftRevision = ref(0)
const keywordDraft = ref('')
const keyword = ref('')
const masterId = ref('')
const legal = ref<'' | 'legal' | 'illegal'>('')
const sort = ref<'latest' | 'name'>('latest')
const directoryPage = ref<PrivateDeckSummaryPage | null>(null)
const directoryLoading = ref(false)
const directoryError = ref('')
const pageSize = 30
let selectorEpoch = 0
let requestEpoch = 0
let selectorAlive = true

const authenticated = computed(() => Boolean(platformState.account?.id && platformState.token))

function resetDraftSelection() {
  draftName.value = props.currentDeckName
  draftId.value = props.currentDeckId
  draftRevision.value = props.currentDeckRevision
}

function selectorContextCurrent(epoch: number, accountId: string | undefined, token: string,
    mode: L12DeckSelectionScope) {
  return selectorAlive && props.open && selectorEpoch === epoch && props.mode === mode
    && platformState.account?.id === accountId && platformState.token === token
}

async function loadDirectoryPage(page = 1) {
  if (!props.open || !authenticated.value) return
  const epoch = selectorEpoch
  const call = ++requestEpoch
  const accountId = platformState.account?.id
  const token = platformState.token
  const mode = props.mode
  directoryLoading.value = true
  directoryError.value = ''
  try {
    const result = await loadPrivateDeckSummaryPage({
      page,
      pageSize,
      keyword: keyword.value || undefined,
      masterId: masterId.value || undefined,
      legal: legal.value ? legal.value === 'legal' : undefined,
      sort: sort.value,
    })
    if (call === requestEpoch && selectorContextCurrent(epoch, accountId, token, mode)) directoryPage.value = result
  } catch (error) {
    if (call === requestEpoch && selectorContextCurrent(epoch, accountId, token, mode))
      directoryError.value = error instanceof Error ? error.message : '牌库目录暂不可读取'
  } finally {
    if (call === requestEpoch && selectorContextCurrent(epoch, accountId, token, mode)) directoryLoading.value = false
  }
}

watch(() => [props.open, props.mode, platformState.account?.id, platformState.token] as const, ([open]) => {
  selectorEpoch++
  requestEpoch++
  directoryLoading.value = false
  directoryError.value = ''
  directoryPage.value = null
  resetDraftSelection()
  if (open && authenticated.value) void loadDirectoryPage(1)
}, { immediate: true, flush: 'sync' })

onBeforeUnmount(() => {
  selectorAlive = false
  selectorEpoch++
  requestEpoch++
})

const modeLabel = computed(() => ({
  ranked: '排位匹配',
  casual: '休闲匹配',
  friendly: '好友房',
  'sandbox-player': '沙盒 · 我方',
  'sandbox-opponent': '沙盒 · 对手',
})[props.mode])
const byId = computed(() => new Map(props.catalog.map(card => [card.id, card])))
const guestRows = computed(() => props.guestDecks.map(deck => ({
  deck,
  error: props.loading ? '正在加载牌库规则' : validateDeck(deck, props.catalog, props.restrictions),
})))
const serverRows = computed(() => (directoryPage.value?.items ?? []).map(deck => ({ deck, error: '' })))
const rows = computed(() => authenticated.value ? serverRows.value : guestRows.value)
const noUsableGuestDeck = computed(() => !authenticated.value && !props.loading
  && rows.value.length > 0 && rows.value.every(row => Boolean(row.error)))
const selected = computed(() => authenticated.value
  ? serverRows.value.find(row => row.deck.id === draftId.value && row.deck.revision === draftRevision.value)
  : guestRows.value.find(row => row.deck.name === draftName.value))
const pageCount = computed(() => Math.max(1, Math.ceil((directoryPage.value?.total ?? 0) / pageSize)))
const selectedOutsidePage = computed(() => authenticated.value && Boolean(draftId.value) && !selected.value)
const masterFacets = computed(() => directoryPage.value?.facets.masters ?? [])

function summaryCountLabel(deck: PrivateDeckSummary) {
  return deck.counts.uncountedMain ? `${deck.counts.main}(${deck.counts.uncountedMain})` : String(deck.counts.main)
}

function rowMeta(deck: DeckSelectionCandidate) {
  return 'counts' in deck
    ? `${summaryCountLabel(deck)} 张主牌 · ${deck.counts.morale} 张士气${deck.counts.special ? ` · ${deck.counts.special} 张特殊区` : ''}`
    : `${deckCountSummary(deck.cardIds, byId.value).label} 张主牌 · ${deck.moraleIds.length} 张士气`
}

function rowStatus(deck: DeckSelectionCandidate, error: string) {
  if (error) return error
  if (!('counts' in deck)) return '符合当前模式规则'
  if (!props.usesSeasonRestrictions) return '当前赛季禁限不用于此模式'
  if (deck.legal) return '当前赛季可用'
  return deck.legalityReason
    ? `当前赛季：${deck.legalityReason}`
    : '当前赛季不可用'
}

function choose(deck: DeckSelectionCandidate) {
  if (props.disabled || props.confirming) return
  draftName.value = deck.name
  if ('counts' in deck) {
    draftId.value = deck.id
    draftRevision.value = deck.revision
  }
}

function applySearch() {
  keyword.value = keywordDraft.value.trim()
  void loadDirectoryPage(1)
}

function cancel() {
  resetDraftSelection()
  emit('cancel')
}

function confirm() {
  if (props.disabled || props.loading || props.confirming || !selected.value || selected.value.error) return
  emit('confirm', selected.value.deck)
}
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()">
    <div v-if="open" class="saved-deck-selector-mask" data-ui-contract="l12-saved-deck-selector"
      @click.self="cancel">
      <section class="saved-deck-selector" role="dialog" aria-modal="true" :aria-label="`${modeLabel}更换牌库`">
        <header>
          <div><small>SAVED DECKS · {{ modeLabel }}</small><h2>更换牌库</h2>
            <p>这里只选择已保存牌库；确认前不会改变当前模式。</p></div>
          <button type="button" aria-label="取消更换牌库" @click="cancel">×</button>
        </header>

        <div class="selector-content">
        <form v-if="authenticated" class="selector-filters" @submit.prevent="applySearch">
          <label><span>搜索牌库</span><input v-model="keywordDraft" type="search" maxlength="64" placeholder="按名称搜索"></label>
          <label><span>主宰</span><select v-model="masterId" :disabled="directoryLoading" @change="loadDirectoryPage(1)">
            <option value="">全部主宰</option>
            <option v-if="masterId && !masterFacets.some(item => item.masterId === masterId)" :value="masterId">{{ byId.get(masterId)?.nameZh || masterId }}</option>
            <option v-for="item in masterFacets" :key="item.masterId" :value="item.masterId">{{ byId.get(item.masterId)?.nameZh || item.masterId }}（{{ item.count }}）</option>
          </select></label>
          <label><span>当前赛季</span><select v-model="legal" :disabled="directoryLoading" @change="loadDirectoryPage(1)"><option value="">全部</option><option value="legal">可用</option><option value="illegal">不可用</option></select></label>
          <label><span>排序</span><select v-model="sort" :disabled="directoryLoading" @change="loadDirectoryPage(1)"><option value="latest">最近更新</option><option value="name">名称</option></select></label>
          <button type="submit" :disabled="directoryLoading">搜索</button>
        </form>
        <p v-if="directoryError" class="directory-error">{{ directoryError }} <button type="button" :disabled="directoryLoading" @click="loadDirectoryPage(directoryPage?.page || 1)">重新读取</button></p>
        <p v-if="actionError" class="directory-error">{{ actionError }} <button v-if="authenticated" type="button" :disabled="directoryLoading" @click="loadDirectoryPage(directoryPage?.page || 1)">刷新当前页</button></p>
        <p v-if="selectedOutsidePage" class="selection-outside-page">当前牌库不在本页；取消不会更换已选牌库。</p>
        <div v-if="directoryLoading && !rows.length" class="selector-empty"><b>正在读取牌库…</b></div>
        <div v-else-if="!rows.length && !directoryError" class="selector-empty">
          <b>没有已保存牌库</b><span>请先在牌库页建立并保存牌库，再返回此大厅选择。</span>
        </div>
        <p v-if="noUsableGuestDeck" class="no-usable-deck">当前模式没有可用牌库，请调整构筑后重试。</p>
        <div v-else class="selector-list">
          <button v-for="row in rows" :key="'counts' in row.deck ? `${row.deck.id}:${row.deck.revision}` : row.deck.name" type="button"
            :class="{ selected: 'counts' in row.deck ? draftId === row.deck.id && draftRevision === row.deck.revision : draftName === row.deck.name, invalid: !!row.error }"
            :disabled="!!row.error || disabled || confirming" :aria-pressed="'counts' in row.deck ? draftId === row.deck.id && draftRevision === row.deck.revision : draftName === row.deck.name"
            @click="choose(row.deck)">
            <DeckProfile compact :master-id="row.deck.masterId" :master-name="byId.get(row.deck.masterId)?.nameZh"
              :fallback-url="byId.get(row.deck.masterId)?.imageUrl" :name="row.deck.name"
              :meta="rowMeta(row.deck)"
              :selected="'counts' in row.deck ? draftId === row.deck.id && draftRevision === row.deck.revision : draftName === row.deck.name"/>
            <span class="legality" :class="{ valid: !row.error && (!('counts' in row.deck) || !usesSeasonRestrictions || row.deck.legal) }">{{ rowStatus(row.deck, row.error) }}</span>
          </button>
        </div>

        <nav v-if="authenticated && directoryPage && directoryPage.total" class="selector-pages" aria-label="私人牌库分页">
          <button type="button" :disabled="directoryLoading || directoryPage.page <= 1" @click="loadDirectoryPage(directoryPage.page - 1)">上一页</button>
          <span>第 {{ directoryPage.page }} / {{ pageCount }} 页 · 共 {{ directoryPage.total }} 副</span>
          <button type="button" :disabled="directoryLoading || directoryPage.page >= pageCount" @click="loadDirectoryPage(directoryPage.page + 1)">下一页</button>
        </nav>
        </div>
        <footer>
          <span>{{ authenticated ? '选择要使用的牌库' : `${rows.length} 副本地牌库` }}</span>
          <button type="button" @click="cancel">取消</button>
          <button class="confirm" type="button" :disabled="disabled || loading || confirming || !selected || !!selected.error" @click="confirm">{{ confirming ? '正在校验…' : '确认使用' }}</button>
        </footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.saved-deck-selector>.selector-content{flex:1;min-height:0;overflow-y:auto;overscroll-behavior:contain}.selector-content>.selector-list{overflow:visible}.saved-deck-selector>header,.saved-deck-selector>footer{flex:none}
.saved-deck-selector-mask{position:fixed;z-index:120;inset:0;display:grid;place-items:center;padding:20px;background:rgba(1,4,6,.82);backdrop-filter:blur(8px)}
.saved-deck-selector{display:flex;width:min(760px,96vw);max-height:min(760px,92vh);flex-direction:column;overflow:hidden;border:1px solid #596770;background:#101821;box-shadow:0 30px 90px #000b;color:#f1eee5;font-family:'Microsoft YaHei','微软雅黑',sans-serif}
.saved-deck-selector>header{display:flex;align-items:flex-start;justify-content:space-between;gap:18px;padding:22px;border-bottom:1px solid #354149}.saved-deck-selector small{color:#55c6cd;font:900 14px monospace;letter-spacing:.18em}.saved-deck-selector h2{margin:5px 0 4px;font-size:24px}.saved-deck-selector header p{margin:0;color:#829097;font-size:14px}.saved-deck-selector header>button{width:34px;height:34px;border:1px solid #4c5a63;background:#0a1117;color:#fff;font-size:20px}
.selector-filters{display:grid;grid-template-columns:minmax(180px,1.6fr) repeat(3,minmax(120px,1fr)) auto;gap:8px;padding:14px 18px;border-bottom:1px solid #354149;background:#0b131a}.selector-filters label{display:grid;gap:5px;min-width:0}.selector-filters span{color:#91a0a8;font-size:12px;font-weight:800}.selector-filters input,.selector-filters select{box-sizing:border-box;width:100%;min-height:40px;padding:8px 10px;border:1px solid #46545c;background:#070c10;color:#fff;outline:none}.selector-filters input:focus,.selector-filters select:focus{border-color:#53c4cb}.selector-filters>button,.directory-error button,.selector-pages button{padding:9px 12px;border:1px solid #52606a;background:#121b23;color:#fff;font-weight:900}.selector-filters>button{align-self:end;min-height:40px}.selector-filters button:disabled,.selector-pages button:disabled{opacity:.4}.directory-error,.selection-outside-page{margin:12px 18px 0;padding:10px;border-left:3px solid #b53a47;background:#241118;color:#efb0b6;font-size:14px;line-height:1.55}.selection-outside-page{border-left-color:#c9a958;background:#211c10;color:#e8d28e}
.selector-list{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;padding:18px;overflow:auto}.selector-list>button{display:grid;min-width:0;padding:0;border:1px solid #39464f;background:#0a1117;color:#fff;text-align:left}.selector-list>button:not(:disabled):hover,.selector-list>button.selected{border-color:#e1c36f}.selector-list>button.invalid{opacity:.56;cursor:not-allowed}.selector-list :deep(.deck-profile){width:100%;border:0;background:transparent}.legality{display:block;min-height:30px;padding:8px 12px;border-top:1px solid #303b42;color:#e79ca3;font-size:14px;line-height:1.45}.legality.valid{color:#7ed4ad}.selector-empty{display:grid;min-height:260px;place-items:center;align-content:center;gap:8px;padding:30px;color:#7e8b92;text-align:center}.selector-empty b{color:#d9dde0}.selector-empty span{font-size:14px}.no-usable-deck{margin:0 18px 14px;padding:10px;border-left:3px solid #b53a47;background:#241118;color:#efb0b6;font-size:14px}.saved-deck-selector>footer{display:flex;align-items:center;justify-content:flex-end;gap:9px;padding:15px 18px;border-top:1px solid #354149}.saved-deck-selector footer>span{margin-right:auto;color:#7e8b92;font-size:14px}.saved-deck-selector footer button{padding:10px 15px;border:1px solid #52606a;background:#121b23;color:#fff;font-weight:900}.saved-deck-selector footer .confirm{border-color:#e0c16c;background:#e0c16c;color:#090d0f}.saved-deck-selector footer button:disabled{opacity:.38;cursor:not-allowed}
.selector-pages{display:flex;align-items:center;justify-content:center;gap:12px;padding:0 18px 14px;color:#91a0a8;font-size:13px}.selector-pages span{min-width:0;text-align:center}.selector-pages button{flex:none;white-space:nowrap}
@media(max-width:760px){.selector-filters{grid-template-columns:repeat(2,minmax(0,1fr))}.selector-filters>button{grid-column:span 2}}@media(max-width:640px){.saved-deck-selector-mask{padding:10px}.selector-list{grid-template-columns:1fr}.saved-deck-selector>footer{flex-wrap:wrap}.saved-deck-selector footer>span{width:100%;margin-right:0}.selector-pages{justify-content:space-between}.selector-filters{grid-template-columns:1fr}.selector-filters>button{grid-column:auto}}
</style>
