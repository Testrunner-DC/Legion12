<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import type { DeckCard } from './decks'
import { cardTypeLabel, isHorizontalCardType } from './cardPresentation'
import CardImage from './CardImage.vue'
defineProps<{ card: DeckCard }>()
const emit = defineEmits<{ close: [] }>()
function keydown(event: KeyboardEvent) { if (event.key === 'Escape') { event.stopPropagation(); emit('close') } }
onMounted(() => window.addEventListener('keydown', keydown))
onBeforeUnmount(() => window.removeEventListener('keydown', keydown))
</script>
<template>
  <Teleport to="body"><div class="catalog-detail-mask" @click.self="emit('close')">
    <section class="catalog-detail-dialog" role="dialog" aria-modal="true" :aria-label="card.nameZh">
      <header><h2>{{ card.nameZh }}</h2><button aria-label="关闭卡牌详情" autofocus @click="emit('close')">×</button></header>
      <div class="catalog-detail-body"><div class="catalog-detail-image" :class="{ horizontal: isHorizontalCardType(card.cardType) }"><CardImage :card-id="card.id" :legacy-url="card.imageUrl" :alt="card.nameZh" intent="detail" native-orientation/></div>
        <div><p>{{ card.number }} · {{ cardTypeLabel(card.cardType) }}</p><dl>
          <template v-if="card.cost != null"><dt>费用</dt><dd>{{ card.cost }}</dd></template>
          <template v-if="card.troops != null"><dt>兵力</dt><dd>{{ card.troops }}</dd></template>
          <template v-if="card.hp != null"><dt>血量</dt><dd>{{ card.hp }}</dd></template>
          <template v-if="card.disasterLevel"><dt>天灾等级</dt><dd>{{ card.disasterLevel }}</dd></template>
          <template v-if="card.traits?.length"><dt>特征</dt><dd>{{ card.traits.join(' / ') }}</dd></template>
          <template v-if="card.profession"><dt>职介</dt><dd>{{ card.profession }}</dd></template>
          <dt>收录</dt><dd>{{ (card.products?.length ? card.products : [card.product]).join(' / ') }}</dd>
        </dl><h3>效果</h3><p class="l12-effect-body">{{ card.effect || '无效果文字' }}</p></div>
      </div>
    </section>
  </div></Teleport>
</template>
<style scoped>
.catalog-detail-mask{position:fixed;inset:0;z-index:4500;display:grid;place-items:center;padding:24px;background:#010407d9}.catalog-detail-dialog{width:min(960px,100%);max-height:90vh;overflow:auto;border:1px solid #526066;background:#101821;color:#eef1ed;padding:24px;font-size:14px}.catalog-detail-dialog header{display:flex;justify-content:space-between;align-items:center;gap:16px}.catalog-detail-dialog h2{margin:0 0 20px;font-size:24px}.catalog-detail-dialog button{background:transparent;border:1px solid #53616a;color:white;font-size:24px;min-width:40px;min-height:40px}.catalog-detail-body{display:grid;grid-template-columns:minmax(0,360px) minmax(0,1fr);gap:28px}.catalog-detail-image{width:100%;aspect-ratio:5/7;align-self:start}.catalog-detail-image.horizontal{aspect-ratio:8/5}.catalog-detail-dialog dl{display:grid;grid-template-columns:80px minmax(0,1fr);gap:10px}.catalog-detail-dialog dt{color:#9eaab0}.catalog-detail-dialog dd{margin:0;overflow-wrap:anywhere}.catalog-detail-dialog p{line-height:1.75}@media(max-width:680px){.catalog-detail-body{grid-template-columns:1fr}.catalog-detail-image{max-width:340px;margin:auto}}
</style>
