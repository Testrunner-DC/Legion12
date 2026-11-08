<script setup lang="ts">
import { computed } from 'vue'
import { cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { displayCardNumber, type DeckCard } from './decks'
import { factionLabel, isOfficialFaction } from './factionPresentation'
import CardImage from './CardImage.vue'

type DetailCard = DeckCard & { galleryStandalone?: boolean; artist?: string }

const props = withDefaults(defineProps<{
  card: DetailCard
  layout?: 'panel' | 'modal'
  showCatalogOnly?: boolean
  titleId?: string
}>(), {
  layout: 'panel',
  showCatalogOnly: true,
})

const standalone = computed(() => props.card.galleryStandalone === true)
const traits = computed(() => {
  const values = (props.card.traits ?? []).map(value => {
    const trimmed = value.trim()
    return isOfficialFaction(trimmed) ? factionLabel(trimmed) : trimmed
  }).filter(Boolean)
  const faction = factionLabel(props.card.faction)
  const needsCollaboration = standalone.value || Boolean(props.card.collaboration?.trim()) || values.includes('联动')
  return [...new Set([faction, needsCollaboration ? '联动' : '', ...values].filter(Boolean))]
})
const tags = computed(() => {
  const values = [...traits.value]
  if (!standalone.value || props.card.cardType !== 'gallery-card') values.push(cardTypeLabel(props.card.cardType, props.card.isCounterTactic))
  if (props.card.profession) values.push(props.card.profession)
  if (props.card.rarity) values.push(props.card.rarity)
  return [...new Set(values.filter(Boolean))]
})
const products = computed(() => props.card.products ?? [])

function hasCostDimension(card: DeckCard) {
  return (standalone.value || card.cardType !== 'master') && card.cost !== undefined
}
</script>

<template>
  <div :class="[layout === 'modal' ? 'archive-modal-image' : 'archive-detail-image', { horizontal: isHorizontalCardType(card.cardType) }]">
    <CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="detail" eager/>
    <slot name="image-overlay"/>
  </div>
  <div class="card-detail-copy" :class="{ 'archive-modal-detail': layout === 'modal' }"
    :data-card-detail-context="showCatalogOnly ? 'catalog' : 'builder'" :data-card-detail-layout="layout">
    <p class="archive-number">
      {{ displayCardNumber(card) }}<template v-if="showCatalogOnly && card.product"> · {{ card.product }}</template>
    </p>
    <h2 :id="titleId">{{ card.nameZh }}</h2>
    <div class="archive-tags">
      <span v-for="tag in tags" :key="tag">{{ tag }}</span>
    </div>
    <p v-if="card.artist" class="archive-artist" data-gallery-artist><b>画师</b><span>{{ card.artist }}</span></p>
    <p v-if="card.collaboration" class="archive-artist" data-gallery-collaboration><b>联动</b><span>{{ card.collaboration }}</span></p>
    <dl v-if="!standalone || [card.cost, card.troops, card.hp, card.disasterLevel, card.trialValue, card.deckLimit].some(value => value !== undefined)">
      <template v-if="hasCostDimension(card)"><dt>费用</dt><dd>{{ card.cost }}</dd></template>
      <template v-if="card.troops !== undefined"><dt>兵力</dt><dd>{{ card.troops }}</dd></template>
      <template v-if="card.hp !== undefined"><dt>血量</dt><dd>{{ card.hp }}</dd></template>
      <template v-if="card.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ card.disasterLevel }}</dd></template>
      <template v-if="card.trialValue !== undefined"><dt>试炼值</dt><dd>{{ card.trialValue }}</dd></template>
      <template v-if="standalone && card.deckLimit !== undefined"><dt>同名卡上限</dt><dd>{{ card.deckLimit }}</dd></template>
    </dl>
    <section class="archive-effect" :data-gallery-card-effect="standalone ? '' : undefined"><b>效果</b><p class="l12-effect-body">{{ card.effect || '无效果文字' }}</p></section>
    <template v-if="showCatalogOnly">
      <section v-if="products.length" class="archive-decks"><b>收录产品</b><p v-for="(name, index) in products" :key="`${index}:${name}`">{{ name }}</p></section>
      <slot name="catalog-extra"/>
    </template>
  </div>
</template>

<style scoped>
/* 同一图鉴详情复用于窄侧栏时，横卡的固定理想宽度不能超过宿主容器。 */
.archive-detail-image,.archive-modal-image{box-sizing:border-box;max-width:100%;min-width:0;overflow:hidden}
.archive-artist{display:flex;align-items:baseline;gap:8px;margin:10px 0;color:#cbd3d4}.archive-artist b{color:#54c7cf}.archive-artist span{overflow-wrap:anywhere}
</style>
