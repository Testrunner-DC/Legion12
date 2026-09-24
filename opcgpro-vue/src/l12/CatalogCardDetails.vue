<script setup lang="ts">
import { onBeforeUnmount, onMounted } from 'vue'
import type { DeckCard } from './decks'
import CardDetailContent from './CardDetailContent.vue'
import { landscapeTeleportTarget } from './mobileViewport'

withDefaults(defineProps<{ card: DeckCard; showCatalogOnly?: boolean }>(), { showCatalogOnly: true })
const emit = defineEmits<{ close: [] }>()
function keydown(event: KeyboardEvent) { if (event.key === 'Escape') { event.stopPropagation(); emit('close') } }
onMounted(() => window.addEventListener('keydown', keydown))
onBeforeUnmount(() => window.removeEventListener('keydown', keydown))
</script>

<template>
  <Teleport :to="landscapeTeleportTarget()"><div class="catalog-detail-mask" @click.self="emit('close')">
    <section class="catalog-detail-dialog" role="dialog" aria-modal="true" :aria-label="card.nameZh">
      <header><h2>卡牌详情</h2><button aria-label="关闭卡牌详情" autofocus @click="emit('close')">×</button></header>
      <div class="catalog-detail-body"><CardDetailContent :card="card" layout="modal" :show-catalog-only="showCatalogOnly"/></div>
    </section>
  </div></Teleport>
</template>

<style scoped>
.catalog-detail-mask{position:fixed;inset:0;z-index:4500;display:grid;place-items:center;padding:24px;background:#010407d9}.catalog-detail-dialog{box-sizing:border-box;width:min(960px,100%);max-height:90dvh;overflow:auto;border:1px solid #526066;background:#101821;color:#eef1ed;padding:24px;font-size:14px}.catalog-detail-dialog header{display:flex;justify-content:space-between;align-items:center;gap:16px}.catalog-detail-dialog h2{margin:0 0 20px;font-size:24px;overflow-wrap:anywhere}.catalog-detail-dialog button{background:transparent;border:1px solid #53616a;color:white;font-size:24px;min-width:40px;min-height:40px}.catalog-detail-body{display:grid;grid-template-columns:minmax(0,360px) minmax(0,1fr);gap:28px}.catalog-detail-body :deep(.archive-modal-image){width:100%}.catalog-detail-dialog p{line-height:1.75}@media(max-width:680px){.catalog-detail-mask{padding:max(10px,env(safe-area-inset-top)) max(10px,env(safe-area-inset-right)) max(10px,env(safe-area-inset-bottom)) max(10px,env(safe-area-inset-left))}.catalog-detail-dialog{max-height:100%;padding:14px}.catalog-detail-body{grid-template-columns:1fr}.catalog-detail-body :deep(.archive-modal-image){max-width:340px;margin:auto}}
</style>
