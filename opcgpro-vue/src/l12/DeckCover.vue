<script setup lang="ts">
import { computed } from 'vue'
import CardImage from './CardImage.vue'
import { getFactionPresentation } from './factionPresentation'
import { selectDeckCoverCardIds, validateDeckCoverCardIds } from './deckCover'
import type { DeckCard } from './decks'

const props = withDefaults(defineProps<{
  name: string
  masterId?: string
  masterName?: string
  faction?: string
  context?: string
  meta?: string
  badge?: string
  variant?: 'regular' | 'featured' | 'compact'
  selected?: boolean
  catalog?: readonly DeckCard[]
  coverCardIds?: readonly string[]
  sourceCardIds?: readonly string[]
}>(), {
  masterId: '', masterName: '', faction: '', context: '', meta: '', badge: '',
  variant: 'regular', selected: false, catalog: () => [], coverCardIds: undefined,
  sourceCardIds: undefined,
})

const byId = computed(() => {
  const cards = new Map<string, DeckCard>()
  for (const card of props.catalog) {
    cards.set(card.id, card)
    cards.set(card.id.toLocaleUpperCase('en-US'), card)
  }
  return cards
})
const master = computed(() => byId.value.get(props.masterId) ?? byId.value.get(props.masterId.toLocaleUpperCase('en-US')))
const presentation = computed(() => getFactionPresentation(props.faction || master.value?.faction))
const cards = computed(() => {
  const ids = props.coverCardIds === undefined
    ? selectDeckCoverCardIds(props.sourceCardIds ?? [], byId.value)
    : validateDeckCoverCardIds(props.coverCardIds, byId.value) ? [...props.coverCardIds] : []
  return ids.map(id => byId.value.get(id) ?? byId.value.get(id.toLocaleUpperCase('en-US')))
    .filter((card): card is DeckCard => Boolean(card))
})
const resolvedMasterName = computed(() => props.masterName || master.value?.nameZh || '未选择主宰')
const coverStyle = computed(() => ({ '--deck-cover-accent': presentation.value.color }))
</script>

<template>
  <div class="deck-cover" :class="[`deck-cover--${variant}`, { 'deck-cover--selected': selected }]"
    :style="coverStyle" :data-cover-variant="variant" :data-cover-cards="cards.map(card => card.id).join(',')">
    <div class="deck-cover__wash" aria-hidden="true">
      <span>{{ resolvedMasterName.slice(0, 1) }}</span>
    </div>
    <div class="deck-cover__cards" aria-hidden="true">
      <div v-for="card in cards" :key="card.id" class="deck-cover__card"
        :class="`deck-cover__card--${card.cardType}`">
        <CardImage :card-id="card.id" :legacy-url="card.imageUrl" alt="" intent="thumb" fit="contain"/>
      </div>
    </div>
    <div class="deck-cover__copy">
      <small v-if="context">{{ context }}</small>
      <b>{{ name }}</b>
      <span>{{ resolvedMasterName }}<template v-if="presentation.label"> · {{ presentation.label }}</template></span>
      <em v-if="meta">{{ meta }}</em>
    </div>
    <strong v-if="badge" class="deck-cover__badge">{{ badge }}</strong>
  </div>
</template>

<style scoped>
.deck-cover{--deck-cover-accent:#b4b2af;position:relative;isolation:isolate;display:block;box-sizing:border-box;width:100%;height:178px;overflow:hidden;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 42%,#526069);border-radius:16px;background:linear-gradient(128deg,color-mix(in srgb,var(--deck-cover-accent) 34%,#101820) 0%,#0b1218 52%,#070b0f 100%);box-shadow:0 12px 26px -20px #000;color:#f4f1e8;text-align:left;transition:transform .3s cubic-bezier(.4,0,.2,1),border-color .3s cubic-bezier(.4,0,.2,1),box-shadow .3s cubic-bezier(.4,0,.2,1)}
.deck-cover::before{content:'';position:absolute;z-index:-1;inset:0;background:radial-gradient(circle at 14% 12%,color-mix(in srgb,var(--deck-cover-accent) 34%,transparent),transparent 39%),linear-gradient(105deg,rgba(255,255,255,.055),transparent 30%);pointer-events:none}
.deck-cover::after{content:'';position:absolute;z-index:2;inset:auto 0 0;height:62%;background:linear-gradient(0deg,rgba(3,6,8,.88),rgba(3,6,8,.02));pointer-events:none}
.deck-cover__wash{position:absolute;z-index:0;inset:0 42% 0 0;display:grid;place-items:center;overflow:hidden;color:color-mix(in srgb,var(--deck-cover-accent) 35%,transparent);font:900 128px/1 'Microsoft YaHei','微软雅黑',sans-serif;opacity:.42;transform:translate(-8%,-4%);pointer-events:none}
.deck-cover__cards{position:absolute;z-index:1;top:7px;right:5px;width:66%;height:92%;transform-origin:72% 52%;transition:transform .3s cubic-bezier(.4,0,.2,1)}
.deck-cover__card{position:absolute;right:4%;top:1px;width:43%;max-width:104px;aspect-ratio:5/7;overflow:visible;filter:drop-shadow(0 9px 10px rgba(0,0,0,.58));transform:rotate(4deg);transform-origin:50% 86%}
.deck-cover__card:nth-child(1){z-index:3}.deck-cover__card:nth-child(2){z-index:2;right:26%;top:7px;transform:rotate(0)}.deck-cover__card:nth-child(3){z-index:1;right:48%;top:13px;transform:rotate(-4deg)}
.deck-cover__card :deep(.l12-card-image),.deck-cover__card :deep(.l12-card-image__resolving),.deck-cover__card :deep(.l12-card-image__img){overflow:visible;background:transparent!important}
.deck-cover__copy{position:absolute;z-index:3;left:15px;right:44%;bottom:14px;display:grid;min-width:0;gap:3px;text-shadow:0 2px 8px #000}
.deck-cover__copy small{overflow:hidden;color:color-mix(in srgb,var(--deck-cover-accent) 72%,#fff);font:800 11px/1.2 'Microsoft YaHei','微软雅黑',sans-serif;letter-spacing:.08em;text-overflow:ellipsis;white-space:nowrap}
.deck-cover__copy b{overflow:hidden;color:#f3f0e8;font:900 18px/1.25 'Microsoft YaHei','微软雅黑',sans-serif;text-overflow:ellipsis;white-space:nowrap;transition:color .3s cubic-bezier(.4,0,.2,1)}
.deck-cover__copy span,.deck-cover__copy em{overflow:hidden;color:#aeb9bb;font:normal 12px/1.35 'Microsoft YaHei','微软雅黑',sans-serif;text-overflow:ellipsis;white-space:nowrap}.deck-cover__copy em{color:#7f8d92;font-style:normal}
.deck-cover__badge{position:absolute;z-index:4;top:11px;left:12px;max-width:46%;overflow:hidden;padding:5px 8px;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 66%,#efe4bd);border-radius:999px;background:rgba(7,11,15,.82);color:#f1dfaa;font:900 11px/1 'Microsoft YaHei','微软雅黑',sans-serif;letter-spacing:.06em;text-overflow:ellipsis;white-space:nowrap}
.deck-cover--featured{height:148px;border-color:color-mix(in srgb,var(--deck-cover-accent) 58%,#d6b75d);background:linear-gradient(125deg,color-mix(in srgb,var(--deck-cover-accent) 40%,#101820),#0b1117 55%,#05080b)}
.deck-cover--featured .deck-cover__cards{right:2px;width:62%}.deck-cover--featured .deck-cover__card{max-width:86px}.deck-cover--featured .deck-cover__copy{right:45%;bottom:12px}.deck-cover--featured .deck-cover__copy b{font-size:16px}.deck-cover--featured .deck-cover__copy em{font-size:11px}
.deck-cover--compact{height:94px;border-radius:12px}.deck-cover--compact::after{height:80%;background:linear-gradient(90deg,rgba(3,6,8,.9),rgba(3,6,8,.38) 62%,rgba(3,6,8,.05))}.deck-cover--compact .deck-cover__wash{inset:0 57% 0 0;font-size:76px}.deck-cover--compact .deck-cover__cards{top:3px;right:4px;width:46%;height:91%}.deck-cover--compact .deck-cover__card{right:2%;top:0;width:39%;max-width:52px}.deck-cover--compact .deck-cover__card:nth-child(2){right:23%;top:3px}.deck-cover--compact .deck-cover__card:nth-child(3){right:44%;top:6px}.deck-cover--compact .deck-cover__copy{left:11px;right:44%;bottom:9px;gap:2px}.deck-cover--compact .deck-cover__copy small{font-size:9px}.deck-cover--compact .deck-cover__copy b{font-size:14px}.deck-cover--compact .deck-cover__copy span,.deck-cover--compact .deck-cover__copy em{font-size:10px}.deck-cover--compact .deck-cover__badge{top:7px;left:9px;padding:4px 6px;font-size:9px}
.deck-cover--selected{border-color:#e6c766;box-shadow:0 0 0 1px rgba(230,199,102,.44),0 16px 36px -20px rgba(230,199,102,.72)}
@media(hover:hover) and (pointer:fine){.deck-cover:hover,:global(button:hover)>.deck-cover,:global(a:hover)>.deck-cover,:global(button:focus-visible)>.deck-cover,:global(a:focus-visible)>.deck-cover{border-color:color-mix(in srgb,var(--deck-cover-accent) 72%,#eee6c9);box-shadow:0 18px 40px -16px color-mix(in srgb,var(--deck-cover-accent) 55%,transparent);transform:translateY(-4px)}.deck-cover:hover .deck-cover__cards,:global(button:hover)>.deck-cover .deck-cover__cards,:global(a:hover)>.deck-cover .deck-cover__cards,:global(button:focus-visible)>.deck-cover .deck-cover__cards,:global(a:focus-visible)>.deck-cover .deck-cover__cards{transform:scale(1.04)}.deck-cover:hover .deck-cover__copy b,:global(button:hover)>.deck-cover .deck-cover__copy b,:global(a:hover)>.deck-cover .deck-cover__copy b,:global(button:focus-visible)>.deck-cover .deck-cover__copy b,:global(a:focus-visible)>.deck-cover .deck-cover__copy b{color:color-mix(in srgb,var(--deck-cover-accent) 64%,#fff)}}
@media(max-width:520px){.deck-cover{height:154px}.deck-cover__copy{left:12px;right:43%;bottom:11px}.deck-cover__copy b{font-size:16px}.deck-cover__cards{width:65%}.deck-cover--featured{height:122px}.deck-cover--featured .deck-cover__copy{right:42%}.deck-cover--compact{height:86px}.deck-cover--compact .deck-cover__copy b{font-size:13px}}
@media(prefers-reduced-motion:reduce){.deck-cover,.deck-cover__cards,.deck-cover__copy b{transition:none!important}.deck-cover:hover,:global(button:hover)>.deck-cover,:global(a:hover)>.deck-cover,:global(button:focus-visible)>.deck-cover,:global(a:focus-visible)>.deck-cover{transform:none}.deck-cover:hover .deck-cover__cards,:global(button:hover)>.deck-cover .deck-cover__cards,:global(a:hover)>.deck-cover .deck-cover__cards,:global(button:focus-visible)>.deck-cover .deck-cover__cards,:global(a:focus-visible)>.deck-cover .deck-cover__cards{transform:none}}
</style>
