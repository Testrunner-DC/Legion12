<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import CardImage from '@/l12/CardImage.vue'
import CatalogCardDetails from '@/l12/CatalogCardDetails.vue'
import { loadDeckCatalog, type DeckCard } from '@/l12/decks'
import type { OperationsCardRestriction } from '@/l12/platform'

const props = withDefaults(defineProps<{
  cards?: readonly DeckCard[]
  disasterCardIds?: readonly string[]
  cardRestrictions?: readonly OperationsCardRestriction[]
  disasterMode?: string
  showDisasters?: boolean
  showRestrictions?: boolean
  disasterTitle?: string
  restrictionTitle?: string
}>(), {
  disasterCardIds: () => [], cardRestrictions: () => [],
  showDisasters: true, showRestrictions: true,
  disasterTitle: '天灾卡池', restrictionTitle: '禁限卡规则',
})
const fallbackCatalog = ref<DeckCard[]>([])
const loading = ref(false)
const catalogError = ref('')
const detailCard = ref<DeckCard | null>(null)
const catalog = computed(() => props.cards?.length ? props.cards : fallbackCatalog.value)
const byId = computed(() => new Map(catalog.value.map(card => [card.id, card])))
const groups = computed(() => {
  const grouped = new Map<string, OperationsCardRestriction[]>()
  for (const rule of props.cardRestrictions) {
    const key = rule.masterId || ''
    const entries = grouped.get(key) ?? []
    entries.push(rule); grouped.set(key, entries)
  }
  return [...grouped].sort(([left], [right]) => !left ? -1 : !right ? 1 : 0)
    .map(([masterId, rules]) => ({ masterId, master: byId.value.get(masterId), rules }))
})
const cardName = (id: string) => byId.value.get(id)?.nameZh || id
const cardNumber = (id: string) => byId.value.get(id)?.number || id
function inspect(id: string) { detailCard.value = byId.value.get(id) ?? null }
let disposed = false
async function loadCatalog() {
  if (loading.value) return
  loading.value = true; catalogError.value = ''
  try { const cards = await loadDeckCatalog(); if (!disposed) fallbackCatalog.value = cards }
  catch { if (!disposed) catalogError.value = '卡牌资料暂不可读取，已保留卡号和规则。' }
  finally { if (!disposed) loading.value = false }
}
watch(() => Boolean((props.showDisasters && props.disasterMode !== 'none' && props.disasterCardIds.length)
  || (props.showRestrictions && props.cardRestrictions.length)) && !props.cards?.length,
needsCatalog => { if (needsCatalog && !fallbackCatalog.value.length) void loadCatalog() }, { immediate: true })
onBeforeUnmount(() => { disposed = true })
</script>

<template>
  <div class="rules-card-summary" data-ui-contract="rules-card-summary">
    <p v-if="loading" class="catalog-status" role="status">正在读取卡牌资料…</p>
    <p v-else-if="catalogError" class="catalog-status" role="alert">{{ catalogError }} <button type="button" @click="loadCatalog">重试</button></p>
    <section v-if="showDisasters" class="rules-card-section" aria-label="天灾卡池规则">
      <h3>{{ disasterTitle }}<small v-if="disasterMode !== 'none'">{{ disasterCardIds.length }} 张</small></h3>
      <p v-if="disasterMode === 'none'" class="empty-rule">本场不使用天灾。</p>
      <template v-else>
        <p v-if="disasterMode === 'random'" class="rule-explanation">从以下卡池随机选取天灾；湮灭固定末位。</p>
        <p v-if="!disasterCardIds.length" class="empty-rule">尚未提供天灾卡池，请以已发布的规则为准。</p>
        <div v-else class="rules-card-grid disaster-grid">
          <button v-for="(id,index) in disasterCardIds" :key="`${index}:${id}`" class="rules-card" type="button"
            :disabled="!byId.has(id)" :aria-label="`查看${cardName(id)}卡牌详情`" @click="inspect(id)">
            <CardImage v-if="byId.has(id)" :card-id="id" :legacy-url="byId.get(id)?.imageUrl" :alt="cardName(id)" intent="thumb" fit="contain"/>
            <span v-else class="missing-art">卡牌资料待补</span>
            <b>{{ cardName(id) }}</b><small>{{ cardNumber(id) }}</small>
            <span class="rule-limit">{{ id === 'S01-DS10' ? '湮灭 · 固定末位' : `卡池顺序 ${index + 1}` }}</span>
          </button>
        </div>
      </template>
    </section>
    <section v-if="showRestrictions" class="rules-card-section" aria-label="禁限卡规则">
      <h3>{{ restrictionTitle }}<small>{{ cardRestrictions.length }} 条</small></h3>
      <p v-if="!cardRestrictions.length" class="empty-rule">无额外禁限卡规则，仍须遵守基础构筑规则。</p>
      <section v-for="group in groups" :key="group.masterId || '*'" class="restriction-group"
        :aria-label="group.masterId ? `主宰为${cardName(group.masterId)}时的禁限卡` : '所有主宰的通用禁限卡'">
        <header class="restriction-scope">
          <button v-if="group.masterId" class="scope-master" type="button" :disabled="!group.master"
            :aria-label="`查看${cardName(group.masterId)}主宰详情`" @click="inspect(group.masterId)">
            <CardImage v-if="group.master" :card-id="group.masterId" :legacy-url="group.master.imageUrl" :alt="cardName(group.masterId)" intent="thumb" fit="contain"/>
            <span><b>主宰：{{ cardName(group.masterId) }}</b><small>{{ cardNumber(group.masterId) }}</small></span>
          </button>
          <b v-else>所有主宰 · 通用限制</b>
          <p>{{ group.masterId ? '使用这位主宰时，以下专属规则优先于同卡的通用限制。' : '适用于所有主宰；同一卡牌另有主宰专属规则时，以专属规则为准。' }}</p>
        </header>
        <div class="rules-card-grid restriction-grid">
          <article v-for="(rule,index) in group.rules" :key="`${rule.cardId}:${index}`" class="restriction-entry">
            <button class="rules-card" type="button" :disabled="!byId.has(rule.cardId)"
              :aria-label="`查看${cardName(rule.cardId)}卡牌详情`" @click="inspect(rule.cardId)">
              <CardImage v-if="byId.has(rule.cardId)" :card-id="rule.cardId" :legacy-url="byId.get(rule.cardId)?.imageUrl" :alt="cardName(rule.cardId)" intent="thumb" fit="contain"/>
              <span v-else class="missing-art">卡牌资料待补</span>
              <b>{{ cardName(rule.cardId) }}</b><small>{{ cardNumber(rule.cardId) }}</small>
              <strong class="rule-limit" :class="{ banned: rule.maxCopies === 0 }">{{ rule.maxCopies === 0 ? '禁止携带（0 张）' : `最多携带 ${rule.maxCopies} 张` }}</strong>
            </button>
            <p v-if="rule.reason" class="rule-reason">{{ rule.reason }}</p>
            <slot name="restriction-actions" :rule="rule"/>
          </article>
        </div>
      </section>
    </section>
    <CatalogCardDetails v-if="detailCard" :card="detailCard" :show-catalog-only="false" @close="detailCard = null"/>
  </div>
</template>

<style scoped>
.rules-card-summary{display:grid;min-width:0;gap:18px;color:var(--l12-ui-text,#eef1ed);font-family:var(--l12-ui-font,'Microsoft YaHei','微软雅黑',sans-serif)}.rules-card-summary *{box-sizing:border-box;min-width:0}.rules-card-section{display:grid;gap:12px}.rules-card-section h3{display:flex;align-items:baseline;flex-wrap:wrap;gap:10px;margin:0;font-size:17px;color:var(--l12-ui-accent,#e7ce72)}.rules-card-section h3 small{font-size:12px;font-weight:500;color:var(--l12-ui-text-muted,#91a0a6)}.rules-card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(144px,100%),1fr));gap:10px}.rules-card-summary .rules-card{display:grid;align-content:start;gap:6px;width:100%;height:100%;padding:9px;border:1px solid var(--l12-ui-line,#3b4a55);border-radius:4px;background:var(--l12-ui-control,#0a1118);color:inherit;text-align:left;cursor:pointer;font-family:inherit;white-space:normal}.rules-card .l12-card-image,.missing-art{display:block;width:100%;aspect-ratio:5/7;background:#05090c}.disaster-grid .l12-card-image,.disaster-grid .missing-art{aspect-ratio:8/5}.missing-art{display:grid;place-items:center;color:#91a0a6;font-size:12px}.rules-card b,.rules-card small,.rule-limit,.rule-reason,.restriction-scope p{overflow-wrap:anywhere}.rules-card b{font-size:14px;line-height:1.5}.rules-card small,.scope-master small{color:var(--l12-ui-text-muted,#91a0a6);font-size:12px}.rule-limit{color:var(--l12-ui-accent,#e7ce72);font-size:13px;line-height:1.5}.rule-limit.banned{color:#f0a4aa}.restriction-group{display:grid;gap:12px;padding:14px;border:1px solid var(--l12-ui-line,#3b4a55);background:var(--l12-ui-panel,#101b25)}.restriction-scope{display:grid;gap:8px}.restriction-scope p,.rule-explanation,.empty-rule,.catalog-status{margin:0;color:var(--l12-ui-text-soft,#b5c0c4);font-size:13px;line-height:1.7}.rules-card-summary .scope-master{display:flex;align-items:center;gap:12px;justify-self:start;max-width:100%;min-height:44px;padding:6px;border:1px solid var(--l12-ui-line,#3b4a55);background:var(--l12-ui-control,#0a1118);color:inherit;text-align:left;cursor:pointer;font-family:inherit}.scope-master .l12-card-image{flex:none;width:48px;aspect-ratio:5/7}.scope-master span{display:grid;gap:5px;overflow-wrap:anywhere}.restriction-entry{display:flex;flex-direction:column;gap:6px}.restriction-entry>.rules-card{height:auto;flex:1}.rule-reason{margin:0;font-size:12px;line-height:1.6;color:var(--l12-ui-text-soft,#b5c0c4)}.rules-card-summary button:focus-visible{outline:2px solid #58c6cd;outline-offset:2px}.rules-card-summary button:disabled{cursor:default}.catalog-status button{min-height:44px;padding:6px 12px;border:1px solid #52606a;background:#121b23;color:inherit;font-family:inherit}
@media(max-width:640px){.restriction-group{padding:10px}.rules-card-grid{grid-template-columns:repeat(auto-fill,minmax(min(126px,100%),1fr));gap:8px}.scope-master b{font-size:14px}.rules-card-summary .rules-card{padding:7px}}
</style>
