<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import CardImage from './CardImage.vue'
import { deploymentPath } from './deploymentBase'
import { getFactionPresentation } from './factionPresentation'
import { selectDeckCoverCardIds, validateDeckCoverCardIds } from './deckCover'
import type { DeckCard } from './decks'

const deckBannerMasterIds = new Set([
  'S01-01M1', 'S01-01M2', 'S01-02M1', 'S01-02M3', 'S01-03M1', 'S01-03M2', 'S01-04M1', 'S01-04M2',
  'S02-01M1', 'S02-02M1', 'S02-03M1', 'S02-04M1', 'S02-05M1', 'S02-05M2', 'S02-06M1', 'S02-06M2',
  'ST01-M1', 'ST02-M1', 'ST03-M1', 'ST04-M1', 'ST05-M1', 'ST06-M1',
])

const props = withDefaults(defineProps<{
  name: string
  masterId?: string
  masterName?: string
  faction?: string
  context?: string
  meta?: string
  metaPrefix?: string
  badge?: string
  variant?: 'regular' | 'featured' | 'compact'
  selected?: boolean
  catalog?: readonly DeckCard[]
  coverCardIds?: readonly string[]
  sourceCardIds?: readonly string[]
}>(), {
  masterId: '', masterName: '', faction: '', context: '', meta: '', metaPrefix: '', badge: '',
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
const normalizedMasterId = computed(() => props.masterId.trim().toLocaleUpperCase('en-US'))
const failedBannerId = ref('')
watch(normalizedMasterId, () => { failedBannerId.value = '' })
const masterBannerUrl = computed(() => deckBannerMasterIds.has(normalizedMasterId.value)
  && failedBannerId.value !== normalizedMasterId.value
  ? deploymentPath(`/assets/l12/deck-banners/${normalizedMasterId.value}.png`)
  : '')
function rejectMasterBanner() { failedBannerId.value = normalizedMasterId.value }
</script>

<template>
  <div class="deck-cover" :class="[`deck-cover--${variant}`, { 'deck-cover--selected': selected, 'deck-cover--has-banner': masterBannerUrl }]"
    :style="coverStyle" :data-cover-variant="variant" :data-cover-cards="cards.map(card => card.id).join(',')"
    :data-master-banner="masterBannerUrl ? normalizedMasterId : undefined">
    <img v-if="masterBannerUrl" class="deck-cover__master-banner" :src="masterBannerUrl" alt="" aria-hidden="true"
      loading="lazy" decoding="async" fetchpriority="low" @error="rejectMasterBanner"/>
    <div v-else class="deck-cover__wash" aria-hidden="true">
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
      <em v-if="meta && !metaPrefix">{{ meta }}</em>
    </div>
    <strong v-if="badge" class="deck-cover__badge">{{ badge }}</strong>
  </div>
  <div v-if="metaPrefix" class="deck-cover__footer" :style="coverStyle" data-cover-footer>
    <i class="deck-environment-badge">{{ metaPrefix }}</i>
    <em v-if="meta">{{ meta }}</em>
  </div>
</template>

<style scoped>
.deck-cover{--deck-cover-accent:#b4b2af;position:relative;isolation:isolate;display:block;box-sizing:border-box;width:100%;height:auto;aspect-ratio:12/5;overflow:hidden;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 42%,#526069);border-radius:0;background:linear-gradient(128deg,color-mix(in srgb,var(--deck-cover-accent) 34%,#101820) 0%,#0b1218 52%,#070b0f 100%);box-shadow:0 12px 26px -20px #000;color:#f4f1e8;text-align:left;transition:transform .3s cubic-bezier(.4,0,.2,1),border-color .3s cubic-bezier(.4,0,.2,1),box-shadow .3s cubic-bezier(.4,0,.2,1)}

.deck-cover::before{content:'';position:absolute;z-index:1;inset:0;background:radial-gradient(circle at 14% 12%,color-mix(in srgb,var(--deck-cover-accent) 44%,transparent),transparent 42%),linear-gradient(105deg,rgba(255,255,255,.055),transparent 30%);opacity:.72;pointer-events:none;transition:opacity .3s cubic-bezier(.4,0,.2,1)}

.deck-cover::after{content:'';position:absolute;z-index:2;inset:auto 0 0;height:62%;background:linear-gradient(0deg,rgba(3,6,8,.88),rgba(3,6,8,.02));pointer-events:none}

.deck-cover__master-banner{position:absolute;z-index:0;inset:0;width:100%;height:100%;object-fit:contain;pointer-events:none}

.deck-cover__wash{position:absolute;z-index:0;inset:0 42% 0 0;display:grid;place-items:center;overflow:hidden;color:color-mix(in srgb,var(--deck-cover-accent) 35%,transparent);font:900 128px/1 'Microsoft YaHei','微软雅黑',sans-serif;opacity:.42;transform:translate(-8%,-4%);pointer-events:none}

.deck-cover{container-type:inline-size}
.deck-cover__cards{position:absolute;z-index:1;top:auto;bottom:0;right:-2%;width:55%;height:100%;transform-origin:100% 100%;transition:transform .3s cubic-bezier(.4,0,.2,1)}

.deck-cover__card{position:absolute;right:4%;top:auto;bottom:0;width:43%;aspect-ratio:5/7;overflow:visible;filter:drop-shadow(0 9px 10px rgba(0,0,0,.58));transform:rotate(4deg);transform-origin:50% 86%}

.deck-cover__card:nth-child(1){z-index:3}
.deck-cover__card:nth-child(2){z-index:2;right:26%;bottom:-4%;transform:rotate(0)}
.deck-cover__card:nth-child(3){z-index:1;right:48%;bottom:-8%;transform:rotate(-4deg)}

.deck-cover__card :deep(.l12-card-image),.deck-cover__card :deep(.l12-card-image__resolving),.deck-cover__card :deep(.l12-card-image__img){overflow:visible;background:transparent!important}

.deck-cover__copy{position:absolute;z-index:3;left:15px;right:44%;bottom:14px;display:grid;min-width:0;gap:3px;text-shadow:0 2px 8px #000}

.deck-cover__copy small{overflow:hidden;color:#f4f1e8;font:800 11px/1.2 'Microsoft YaHei','微软雅黑',sans-serif;letter-spacing:.08em;text-overflow:ellipsis;white-space:nowrap}

.deck-cover__copy b{overflow:hidden;color:#f3f0e8;font:900 18px/1.25 'Microsoft YaHei','微软雅黑',sans-serif;text-overflow:ellipsis;white-space:nowrap;transition:color .3s cubic-bezier(.4,0,.2,1)}

.deck-cover__copy span,.deck-cover__copy em{overflow:hidden;color:#f4f1e8;font:normal 12px/1.35 'Microsoft YaHei','微软雅黑',sans-serif;text-overflow:ellipsis;white-space:nowrap}
.deck-cover__copy em{font-style:normal}

.deck-cover__footer{--deck-cover-accent:#b4b2af;display:flex;box-sizing:border-box;width:100%;height:30px;min-width:0;align-items:center;flex-wrap:nowrap;gap:6px;overflow:hidden;padding:5px 10px;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 42%,#526069);border-top:0;border-radius:0;background:linear-gradient(90deg,color-mix(in srgb,var(--deck-cover-accent) 16%,#0b1218),#080d12);color:#7f8d92;text-align:left;white-space:nowrap;transition:border-color .3s cubic-bezier(.4,0,.2,1),box-shadow .3s cubic-bezier(.4,0,.2,1)}
.deck-cover__footer em{min-width:0;overflow:hidden;flex:1;color:#f4f1e8;font:normal 11px/1.35 'Microsoft YaHei','微软雅黑',sans-serif;font-style:normal;text-overflow:ellipsis;white-space:nowrap}
.deck-cover__footer .deck-environment-badge{display:inline-flex;min-height:18px;box-sizing:border-box;align-items:center;flex:none;padding:2px 5px;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 48%,#59666e);background:rgba(8,14,18,.84);color:#f4f1e8;font:900 9px/1 'Microsoft YaHei','微软雅黑',sans-serif;font-style:normal;white-space:nowrap}

.deck-cover__badge{position:absolute;z-index:4;top:11px;left:12px;max-width:46%;overflow:hidden;padding:5px 8px;border:1px solid color-mix(in srgb,var(--deck-cover-accent) 66%,#efe4bd);border-radius:999px;background:rgba(7,11,15,.82);color:#f4f1e8;font:900 11px/1 'Microsoft YaHei','微软雅黑',sans-serif;letter-spacing:.06em;text-overflow:ellipsis;white-space:nowrap}

.deck-cover--featured{border-color:color-mix(in srgb,var(--deck-cover-accent) 58%,#d6b75d);background:linear-gradient(125deg,color-mix(in srgb,var(--deck-cover-accent) 40%,#101820),#0b1117 55%,#05080b)}

.deck-cover--featured .deck-cover__copy{right:45%;bottom:12px}
.deck-cover--featured .deck-cover__copy b{font-size:16px}
.deck-cover--featured .deck-cover__copy em{font-size:11px}

.deck-cover--compact{height:auto;aspect-ratio:12/5;border-radius:0}
.deck-cover--compact::after{height:80%;background:linear-gradient(90deg,rgba(3,6,8,.9),rgba(3,6,8,.38) 62%,rgba(3,6,8,.05))}
.deck-cover--compact .deck-cover__wash{inset:0 57% 0 0;font-size:76px}
.deck-cover--compact .deck-cover__copy{left:11px;right:44%;bottom:9px;gap:2px}
.deck-cover--compact .deck-cover__copy small{font-size:9px}
.deck-cover--compact .deck-cover__copy b{font-size:14px}
.deck-cover--compact .deck-cover__copy span,.deck-cover--compact .deck-cover__copy em{font-size:10px}
.deck-cover--compact .deck-cover__badge{top:7px;left:9px;padding:4px 6px;font-size:9px}

.deck-cover--selected{border-color:#e6c766;box-shadow:0 0 0 1px rgba(230,199,102,.44),0 16px 36px -20px rgba(230,199,102,.72)}

@media(min-width:701px){.deck-cover .deck-cover__cards{bottom:0;right:-2%}
}

@media(hover:hover) and (pointer:fine){.deck-cover:hover,:global(button:hover > .deck-cover),:global(a:hover > .deck-cover),:global(article:hover > .deck-cover),:global(button:focus-visible > .deck-cover),:global(a:focus-visible > .deck-cover),:global(article:focus-within > .deck-cover){border-color:color-mix(in srgb,var(--deck-cover-accent) 88%,#f4efe1);box-shadow:0 0 0 1px color-mix(in srgb,var(--deck-cover-accent) 54%,transparent),0 20px 46px -14px color-mix(in srgb,var(--deck-cover-accent) 76%,transparent),inset 0 0 28px color-mix(in srgb,var(--deck-cover-accent) 14%,transparent);transform:translateY(-4px)}
:global(button:hover > .deck-cover__footer),:global(a:hover > .deck-cover__footer),:global(button:focus-visible > .deck-cover__footer),:global(a:focus-visible > .deck-cover__footer){border-color:color-mix(in srgb,var(--deck-cover-accent) 72%,#f4efe1);box-shadow:0 12px 28px -18px color-mix(in srgb,var(--deck-cover-accent) 72%,transparent)}
.deck-cover:hover::before,:global(button:hover > .deck-cover::before),:global(a:hover > .deck-cover::before),:global(article:hover > .deck-cover::before),:global(button:focus-visible > .deck-cover::before),:global(a:focus-visible > .deck-cover::before),:global(article:focus-within > .deck-cover::before){opacity:1}
.deck-cover:hover .deck-cover__cards,:global(button:hover > .deck-cover .deck-cover__cards),:global(a:hover > .deck-cover .deck-cover__cards),:global(article:hover > .deck-cover .deck-cover__cards),:global(button:focus-visible > .deck-cover .deck-cover__cards),:global(a:focus-visible > .deck-cover .deck-cover__cards),:global(article:focus-within > .deck-cover .deck-cover__cards){transform:scale(1.08)}
.deck-cover:hover .deck-cover__copy b,:global(button:hover > .deck-cover .deck-cover__copy b),:global(a:hover > .deck-cover .deck-cover__copy b),:global(article:hover > .deck-cover .deck-cover__copy b),:global(button:focus-visible > .deck-cover .deck-cover__copy b),:global(a:focus-visible > .deck-cover .deck-cover__copy b),:global(article:focus-within > .deck-cover .deck-cover__copy b){color:#f4f1e8}
}

@media(max-width:520px){.deck-cover__copy{left:12px;right:43%;bottom:11px}
.deck-cover__copy b{font-size:16px}
.deck-cover--featured .deck-cover__copy{right:42%}
.deck-cover--compact .deck-cover__copy b{font-size:13px}
.deck-cover__footer{padding:5px 8px}
}


@container(max-width:240px){.deck-cover .deck-cover__copy{left:8px;right:40%;bottom:6px;gap:2px}.deck-cover .deck-cover__copy small{display:none}.deck-cover .deck-cover__copy b{font-size:12px}.deck-cover .deck-cover__copy span,.deck-cover .deck-cover__copy em{font-size:9px;line-height:1.2}}
</style>
