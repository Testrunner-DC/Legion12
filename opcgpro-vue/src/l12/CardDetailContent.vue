<script setup lang="ts">
import { computed } from 'vue'
import { cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import { displayCardNumber, type DeckCard } from './decks'
import CardImage from './CardImage.vue'

const props = withDefaults(defineProps<{
  card: DeckCard
  layout?: 'panel' | 'modal'
  showCatalogOnly?: boolean
  titleId?: string
}>(), {
  layout: 'panel',
  showCatalogOnly: true,
})

const products = computed(() => props.card.products ?? [])

function hasCostDimension(card: DeckCard) {
  return card.cardType !== 'master' && card.cost !== undefined
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
      <span v-for="trait in card.traits" :key="trait">{{ trait }}</span>
      <span>{{ cardTypeLabel(card.cardType) }}</span>
      <span v-if="card.profession">{{ card.profession }}</span>
      <span v-if="card.rarity">{{ card.rarity }}</span>
    </div>
    <dl>
      <template v-if="hasCostDimension(card)"><dt>费用</dt><dd>{{ card.cost }}</dd></template>
      <template v-if="card.troops !== undefined"><dt>兵力</dt><dd>{{ card.troops }}</dd></template>
      <template v-if="card.hp !== undefined"><dt>血量</dt><dd>{{ card.hp }}</dd></template>
      <template v-if="card.disasterLevel !== undefined"><dt>天灾等级</dt><dd>{{ card.disasterLevel }}</dd></template>
      <template v-if="card.trialValue !== undefined"><dt>试炼值</dt><dd>{{ card.trialValue }}</dd></template>
    </dl>
    <section class="archive-effect"><b>效果</b><p class="l12-effect-body">{{ card.effect || '无效果文字' }}</p></section>
    <template v-if="showCatalogOnly">
      <section v-if="products.length" class="archive-decks"><b>收录产品</b><p v-for="name in products" :key="name">{{ name }}</p></section>
      <slot name="catalog-extra"/>
    </template>
  </div>
</template>
