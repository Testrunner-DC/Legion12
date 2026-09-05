<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import { cardTypeFilterKey, cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { compareArchiveVersions, groupArchiveCards, type LogicalArchiveCard } from './cardArchiveVersions'
import { cardArchiveProducts, displayCardNumber, loadCardArchiveCatalog, type DeckCard } from './decks'
import CardImage from './CardImage.vue'

type CatalogCard = DeckCard
type ArchivePage = 'catalog' | 'gallery'

const typeLabels: Record<string, string> = {
  legion: '军团', tactic: '战术', rune: '士气卡', artifact: '圣物',
  divinity: '主城', master: '主宰', destruction: '天灾',
  token: '衍生卡牌', trial: '试炼卡', unknown: '待识别',
}
const factionLabels: Record<string, string> = {
  universal: '通用', tianting: '天廷', gaotianyuan: '高天原',
  asgard: '阿斯加德', taiyangcheng: '太阳城',
  olympus: '奥林匹斯', bijie: '彼界', otherworld: '彼界', disaster: '天灾',
}

// Gallery membership is intentionally auditable. Alternate art must match one
// of the approved ID shapes and come from the presentation archive, except for
// S02-05C1A, whose explicit product/lookup registration predates that archive.
// The Olympus B face is a separate rules identity, never gallery artwork.
const galleryVariantPatterns = [
  /^S\d{2}-\d{4}[a-z]$/,
  /^S\d{2}-\d{2}[CM]1A$/,
  /^ST\d{2}-C1st$/,
]
const cards = ref<CatalogCard[]>([])
const loading = ref(true)
const loadError = ref('')
const page = ref<ArchivePage>('catalog')
const query = ref('')
const type = ref('all')
const faction = ref('all')
const cost = ref('all')
const disaster = ref('all')
const product = ref('all')
const sort = ref<'number' | 'cost' | 'troops' | 'name'>('number')
const selectedLogicalId = ref('')
const selectedVersionId = ref('')
const selectedGalleryId = ref('')
const modalCard = ref<CatalogCard | null>(null)
const modalVersions = ref<CatalogCard[]>([])
const modalCloseButton = ref<HTMLButtonElement | null>(null)
let modalTrigger: HTMLElement | null = null

const logicalCards = computed(() => groupArchiveCards(cards.value))
const galleryCards = computed(() => cards.value.filter(isGalleryVariant).sort(compareArchiveVersions))
const productOptions = computed(() => cardArchiveProducts.filter(value => cards.value.some(card => card.products?.includes(value))))

onMounted(async () => {
  window.addEventListener('keydown', onWindowKeydown)
  try {
    cards.value = await loadCardArchiveCatalog()
    const first = logicalCards.value[0]
    if (first) selectLogical(first)
    selectedGalleryId.value = galleryCards.value[0]?.id ?? ''
  } catch (error) {
    loadError.value = error instanceof Error ? error.message : '卡牌图鉴加载失败'
  } finally {
    loading.value = false
  }
})

onBeforeUnmount(() => window.removeEventListener('keydown', onWindowKeydown))

function hasCostDimension(card: CatalogCard) {
  return card.cardType !== 'master' && card.cost !== undefined
}

function isGalleryVariant(card: CatalogCard) {
  return card.id !== 'S02-05C1B'
    && galleryVariantPatterns.some(pattern => pattern.test(card.id))
    && (Boolean(card.archiveBaseCardId) || card.id === 'S02-05C1A')
}

function matchesFilters(card: CatalogCard, keyword: string) {
  const matchesQuery = !keyword || [card.nameZh, displayCardNumber(card), ...(card.products ?? []), card.rarity, card.effect, card.profession, ...(card.traits ?? [])]
    .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))
  return matchesQuery
    && (type.value === 'all' || cardTypeFilterKey(card.cardType) === type.value)
    && (faction.value === 'all' || card.faction === faction.value)
    && (product.value === 'all' || card.products?.includes(product.value))
    && (cost.value === 'all' || (hasCostDimension(card)
      && (cost.value === '7+' ? card.cost! >= 7 : card.cost === Number(cost.value))))
    && (disaster.value === 'all'
      || (disaster.value === 'none' ? !card.disasterLevel : card.disasterLevel === Number(disaster.value)))
}

function compareVisibleCards(left: CatalogCard, right: CatalogCard) {
  if (sort.value === 'name') return left.nameZh.localeCompare(right.nameZh, 'zh-CN')
  if (sort.value === 'cost') return (left.cost ?? 99) - (right.cost ?? 99) || left.number.localeCompare(right.number)
  if (sort.value === 'troops') return (right.troops ?? -1) - (left.troops ?? -1) || left.number.localeCompare(right.number)
  return left.number.localeCompare(right.number)
}

const filteredCatalog = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return logicalCards.value.filter(entry => entry.versions.some(card => matchesFilters(card, keyword)))
    .sort((a, b) => compareVisibleCards(a.defaultVersion, b.defaultVersion))
})

const filteredGallery = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return galleryCards.value.filter(card => matchesFilters(card, keyword)).sort(compareVisibleCards)
})

const types = computed(() => [...new Set(cards.value.map(card => cardTypeFilterKey(card.cardType)))]
  .filter(key => typeLabels[key]))
const factions = computed(() => Object.keys(factionLabels).filter(key => cards.value.some(card => card.faction === key)))
const selectedLogical = computed(() => logicalCards.value.find(entry => entry.logicalId === selectedLogicalId.value) ?? null)
const selectedCatalogCard = computed(() => selectedLogical.value?.versions.find(card => card.id === selectedVersionId.value)
  ?? selectedLogical.value?.defaultVersion
  ?? null)
const selectedGalleryCard = computed(() => galleryCards.value.find(card => card.id === selectedGalleryId.value) ?? null)
const selected = computed(() => page.value === 'gallery' ? selectedGalleryCard.value : selectedCatalogCard.value)
const selectedProducts = computed(() => selected.value?.products ?? [])
const modalProducts = computed(() => modalCard.value?.products ?? [])
const modalVersionIndex = computed(() => modalCard.value
  ? modalVersions.value.findIndex(card => card.id === modalCard.value?.id)
  : -1)
const visibleCount = computed(() => page.value === 'gallery' ? filteredGallery.value.length : filteredCatalog.value.length)
const totalCount = computed(() => page.value === 'gallery' ? galleryCards.value.length : logicalCards.value.length)

function selectLogical(entry: LogicalArchiveCard) {
  if (selectedLogicalId.value === entry.logicalId) return
  selectedLogicalId.value = entry.logicalId
  selectedVersionId.value = entry.defaultVersion.id
}

function selectGallery(card: CatalogCard) {
  selectedGalleryId.value = card.id
}

function displayedVersion(entry: LogicalArchiveCard) {
  if (selectedLogicalId.value !== entry.logicalId) return entry.defaultVersion
  return entry.versions.find(card => card.id === selectedVersionId.value) ?? entry.defaultVersion
}

function displayedVersionIndex(entry: LogicalArchiveCard) {
  const index = entry.versions.findIndex(card => card.id === displayedVersion(entry).id)
  return index < 0 ? 0 : index
}

function cycleVersion(entry: LogicalArchiveCard, direction: -1 | 1) {
  if (entry.versions.length < 2) return
  const nextIndex = (displayedVersionIndex(entry) + direction + entry.versions.length) % entry.versions.length
  selectedLogicalId.value = entry.logicalId
  selectedVersionId.value = entry.versions[nextIndex].id
}

function showPage(nextPage: ArchivePage) {
  page.value = nextPage
  if (nextPage === 'gallery' && !selectedGalleryCard.value) selectedGalleryId.value = galleryCards.value[0]?.id ?? ''
}

function openDetail(card: CatalogCard, trigger: Event, versions: readonly CatalogCard[] = []) {
  modalTrigger = trigger.currentTarget instanceof HTMLElement ? trigger.currentTarget : null
  modalCard.value = card
  modalVersions.value = versions.length > 1 ? [...versions] : []
  nextTick(() => modalCloseButton.value?.focus())
}

function closeDetail() {
  modalCard.value = null
  modalVersions.value = []
  nextTick(() => modalTrigger?.focus())
}

function cycleModalVersion(direction: -1 | 1) {
  if (!modalCard.value || modalVersions.value.length < 2) return
  const current = modalVersionIndex.value < 0 ? 0 : modalVersionIndex.value
  modalCard.value = modalVersions.value[(current + direction + modalVersions.value.length) % modalVersions.value.length]
}

function onWindowKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && modalCard.value) closeDetail()
}

function resetFilters() {
  query.value = ''
  type.value = faction.value = cost.value = disaster.value = product.value = 'all'
  sort.value = 'number'
}
</script>

<template>
  <section class="card-archive grand-panel">
    <i class="corner tl"/><i class="corner tr"/><i class="corner bl"/><i class="corner br"/>
    <header class="archive-header">
      <div><p class="kicker">CARD CATALOG · ART GALLERY</p><h1>卡牌图鉴</h1></div>
      <div class="archive-count"><b>{{ visibleCount }}</b><span>/ {{ totalCount }} 张</span></div>
    </header>

    <div class="archive-tabs" role="tablist" aria-label="卡牌图鉴子页">
      <button id="archive-catalog-tab" type="button" role="tab" :aria-selected="page === 'catalog'" aria-controls="archive-results" :class="{ active: page === 'catalog' }" @click="showPage('catalog')">全卡池</button>
      <button id="archive-gallery-tab" type="button" role="tab" :aria-selected="page === 'gallery'" aria-controls="archive-results" :class="{ active: page === 'gallery' }" @click="showPage('gallery')">画廊</button>
    </div>

    <div class="archive-toolbar">
      <label class="archive-search"><span>搜索</span><input v-model="query" type="search" placeholder="卡名、编号或效果文字"/></label>
      <label><span>类型</span><select v-model="type"><option value="all">全部类型</option><option v-for="key in types" :key="key" :value="key">{{ typeLabels[key] }}</option></select></label>
      <label><span>阵营</span><select v-model="faction"><option value="all">全部阵营</option><option v-for="key in factions" :key="key" :value="key">{{ factionLabels[key] }}</option></select></label>
      <label><span>收录产品</span><select v-model="product"><option value="all">全部产品</option><option v-for="value in productOptions" :key="value" :value="value">{{ value }}</option></select></label>
      <label><span>费用</span><select v-model="cost"><option value="all">全部费用</option><option v-for="value in ['0','1','2','3','4','5','6','7+']" :key="value" :value="value">{{ value }}</option></select></label>
      <label><span>天灾等级</span><select v-model="disaster"><option value="all">全部</option><option value="none">无</option><option v-for="value in [1,2,3,4,5,6,7,8]" :key="value" :value="String(value)">{{ value }}</option></select></label>
      <label><span>排序</span><select v-model="sort"><option value="number">编号</option><option value="cost">费用</option><option value="troops">兵力</option><option value="name">名称</option></select></label>
      <button class="archive-reset" @click="resetFilters">重置</button>
    </div>

    <div v-if="loading" class="archive-empty">正在载入卡牌数据…</div>
    <div v-else-if="loadError" class="archive-empty error">{{ loadError }}</div>
    <div v-else class="archive-workspace">
      <div id="archive-results" class="archive-grid" role="list" :aria-labelledby="page === 'gallery' ? 'archive-gallery-tab' : 'archive-catalog-tab'">
        <template v-if="page === 'catalog'">
          <article v-for="entry in filteredCatalog" :key="entry.logicalId" role="listitem" tabindex="0" class="archive-card"
            :aria-label="`${displayedVersion(entry).nameZh}，按回车打开详情`"
            :class="[{ selected: selectedLogicalId === entry.logicalId, 'landscape-thumbnail': isHorizontalCardType(displayedVersion(entry).cardType) }, `faction-${displayedVersion(entry).faction}`]"
            @click="selectLogical(entry)" @keydown.enter.prevent="selectLogical(entry); openDetail(displayedVersion(entry), $event, entry.versions)" @keydown.space.prevent="selectLogical(entry)">
            <div class="archive-card-image">
              <div class="archive-image-open" @dblclick.stop="openDetail(displayedVersion(entry), $event, entry.versions)">
                <CardImage :card-id="displayedVersion(entry).id" :legacy-url="displayedVersion(entry).imageUrl" :alt="displayedVersion(entry).nameZh" intent="thumb"/>
              </div>
              <b v-if="hasCostDimension(displayedVersion(entry))" class="archive-cost">{{ displayedVersion(entry).cost }}</b>
              <b v-if="displayedVersion(entry).disasterLevel" class="archive-disaster">{{ displayedVersion(entry).disasterLevel }}</b>
              <b v-if="displayedVersion(entry).troops" class="archive-troops">{{ displayedVersion(entry).troops }}</b>
              <template v-if="entry.versions.length > 1">
                <button class="archive-version-arrow previous" type="button" aria-label="上一版本" title="上一版本" @click.stop="cycleVersion(entry, -1)" @keydown.enter.stop @keydown.space.stop>‹</button>
                <button class="archive-version-arrow next" type="button" aria-label="下一版本" title="下一版本" @click.stop="cycleVersion(entry, 1)" @keydown.enter.stop @keydown.space.stop>›</button>
                <b class="archive-version-count">{{ displayedVersionIndex(entry) + 1 }}/{{ entry.versions.length }}</b>
              </template>
            </div>
            <span>{{ displayedVersion(entry).nameZh }}</span><small>{{ displayCardNumber(displayedVersion(entry)) }} · {{ cardTypeLabel(displayedVersion(entry).cardType) }}</small>
          </article>
          <div v-if="!filteredCatalog.length" class="archive-empty">没有符合条件的卡牌。</div>
        </template>

        <template v-else>
          <article v-for="card in filteredGallery" :key="card.id" role="listitem" tabindex="0" class="archive-card archive-gallery-card"
            :aria-label="`${card.nameZh}，按回车打开详情`"
            :class="[{ selected: selectedGalleryId === card.id, 'landscape-thumbnail': isHorizontalCardType(card.cardType) }, `faction-${card.faction}`]"
            @click="selectGallery(card)" @keydown.enter.prevent="selectGallery(card); openDetail(card, $event)" @keydown.space.prevent="selectGallery(card)">
            <div class="archive-card-image">
              <div class="archive-image-open" @dblclick.stop="openDetail(card, $event)">
                <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="thumb"/>
              </div>
              <b v-if="hasCostDimension(card)" class="archive-cost">{{ card.cost }}</b>
              <b v-if="card.disasterLevel" class="archive-disaster">{{ card.disasterLevel }}</b>
              <b v-if="card.troops" class="archive-troops">{{ card.troops }}</b>
            </div>
            <span>{{ card.nameZh }}</span><small>{{ displayCardNumber(card) }} · {{ cardTypeLabel(card.cardType) }}</small>
          </article>
          <div v-if="!filteredGallery.length" class="archive-empty">没有符合条件的展示版本。</div>
        </template>
      </div>

      <aside v-if="selected" class="archive-detail">
        <div class="archive-detail-image" :class="{ horizontal: isHorizontalCardType(selected.cardType) }">
          <CardImage :card-id="selected.id" :legacy-url="selected.imageUrl" :alt="selected.nameZh" intent="detail" eager/>
        </div>
        <p class="archive-number">{{ displayCardNumber(selected) }} · {{ selected.product }}</p>
        <h2>{{ selected.nameZh }}</h2>
        <div class="archive-tags"><span v-for="trait in selected.traits" :key="trait">{{ trait }}</span><span>{{ cardTypeLabel(selected.cardType) }}</span><span v-if="selected.profession">{{ selected.profession }}</span><span v-if="selected.rarity">{{ selected.rarity }}</span></div>
        <dl>
          <template v-if="hasCostDimension(selected)"><dt>费用</dt><dd>{{ selected.cost }}</dd></template>
          <template v-if="selected.troops !== undefined"><dt>兵力</dt><dd>{{ selected.troops }}</dd></template>
          <template v-if="selected.hp !== undefined"><dt>血量</dt><dd>{{ selected.hp }}</dd></template>
          <template v-if="selected.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ selected.disasterLevel }}</dd></template>
          <template v-if="selected.trialValue !== undefined"><dt>试炼值</dt><dd>{{ selected.trialValue }}</dd></template>
        </dl>
        <section class="archive-effect"><b>效果</b><p class="l12-effect-body">{{ selected.effect || '无效果文字' }}</p></section>
        <section v-if="selectedProducts.length" class="archive-decks"><b>收录产品</b><p v-for="name in selectedProducts" :key="name">{{ name }}</p></section>
      </aside>
    </div>

    <Teleport to="body">
      <div v-if="modalCard" class="archive-modal-backdrop" @click.self="closeDetail">
        <section class="archive-modal" role="dialog" aria-modal="true" aria-labelledby="archive-modal-title">
          <button ref="modalCloseButton" class="archive-modal-close" type="button" aria-label="关闭卡牌详情" @click="closeDetail">×</button>
          <div class="archive-modal-image" :class="{ horizontal: isHorizontalCardType(modalCard.cardType) }">
            <CardImage :card-id="modalCard.id" :legacy-url="modalCard.imageUrl" :alt="modalCard.nameZh" intent="detail" eager/>
            <template v-if="modalVersions.length > 1">
              <button class="archive-modal-version-arrow previous" type="button" aria-label="大图上一版本" title="上一版本" @click="cycleModalVersion(-1)">‹</button>
              <button class="archive-modal-version-arrow next" type="button" aria-label="大图下一版本" title="下一版本" @click="cycleModalVersion(1)">›</button>
              <b class="archive-modal-version-count">{{ modalVersionIndex + 1 }}/{{ modalVersions.length }}</b>
            </template>
          </div>
          <div class="archive-modal-detail">
            <p class="archive-number">{{ displayCardNumber(modalCard) }} · {{ modalCard.product }}</p>
            <h2 id="archive-modal-title">{{ modalCard.nameZh }}</h2>
            <div class="archive-tags"><span v-for="trait in modalCard.traits" :key="trait">{{ trait }}</span><span>{{ cardTypeLabel(modalCard.cardType) }}</span><span v-if="modalCard.profession">{{ modalCard.profession }}</span><span v-if="modalCard.rarity">{{ modalCard.rarity }}</span></div>
            <dl>
              <template v-if="hasCostDimension(modalCard)"><dt>费用</dt><dd>{{ modalCard.cost }}</dd></template>
              <template v-if="modalCard.troops !== undefined"><dt>兵力</dt><dd>{{ modalCard.troops }}</dd></template>
              <template v-if="modalCard.hp !== undefined"><dt>血量</dt><dd>{{ modalCard.hp }}</dd></template>
              <template v-if="modalCard.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ modalCard.disasterLevel }}</dd></template>
              <template v-if="modalCard.trialValue !== undefined"><dt>试炼值</dt><dd>{{ modalCard.trialValue }}</dd></template>
            </dl>
            <section class="archive-effect"><b>效果</b><p class="l12-effect-body">{{ modalCard.effect || '无效果文字' }}</p></section>
            <section v-if="modalProducts.length" class="archive-decks"><b>收录产品</b><p v-for="name in modalProducts" :key="name">{{ name }}</p></section>
          </div>
        </section>
      </div>
    </Teleport>
  </section>
</template>
