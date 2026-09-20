<script setup lang="ts">
import { computed, ref } from 'vue'
import { cardTypeLabel } from '@/l12/cardPresentation'
import CardImage from '@/l12/CardImage.vue'

export interface FilteredSingleCardItem {
  id: string
  cardId: string
  number: string
  name: string
  imageUrl?: string
  cardImageId?: string
  cardType?: string
  faction?: string
  product?: string
  cost?: number
  subtitle?: string
}

const props = defineProps<{ title: string; items: FilteredSingleCardItem[] }>()
const emit = defineEmits<{ select: [item: FilteredSingleCardItem]; close: [] }>()
const query = ref('')
const type = ref('')
const faction = ref('')
const product = ref('')
const types = computed(() => [...new Set(props.items.map(item => item.cardType).filter(Boolean) as string[])].sort())
const factions = computed(() => [...new Set(props.items.map(item => item.faction).filter(Boolean) as string[])].sort())
const products = computed(() => [...new Set(props.items.map(item => item.product).filter(Boolean) as string[])].sort())
const visible = computed(() => {
  const keyword = query.value.trim().toLocaleLowerCase('zh-CN')
  return props.items.filter(item => (!type.value || item.cardType === type.value)
    && (!faction.value || item.faction === faction.value)
    && (!product.value || item.product === product.value)
    && (!keyword || [item.number, item.name, item.cardId, item.subtitle, item.product, item.faction]
      .some(value => value?.toLocaleLowerCase('zh-CN').includes(keyword))))
})
</script>

<template>
  <div class="single-card-mask" @click.self="emit('close')">
    <section class="single-card-picker" role="dialog" aria-modal="true" :aria-label="title">
      <header><div><small>SINGLE CARD SEARCH</small><h2>{{ title }}</h2></div><button type="button" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="filters">
        <input v-model="query" type="search" autofocus placeholder="搜索编号、卡名、阵营或产品">
        <select v-model="type"><option value="">全部类型</option><option v-for="value in types" :key="value" :value="value">{{ cardTypeLabel(value) }}</option></select>
        <select v-model="faction"><option value="">全部阵营</option><option v-for="value in factions" :key="value" :value="value">{{ value }}</option></select>
        <select v-model="product"><option value="">全部产品</option><option v-for="value in products" :key="value" :value="value">{{ value }}</option></select>
      </div>
      <div class="result-count">{{ visible.length }} 张符合条件</div>
      <div class="card-grid">
        <button v-for="item in visible" :key="item.id" type="button" class="card-choice" @click="emit('select', item)">
          <CardImage v-if="item.cardImageId" :card-id="item.cardImageId" :alt="item.name" intent="thumb"/>
          <img v-else-if="item.imageUrl" :src="item.imageUrl" :alt="item.name"><span v-else class="image-empty">暂无卡图</span>
          <b>{{ item.name }}</b><small>{{ item.number || item.cardId }} · {{ cardTypeLabel(item.cardType || '') }}</small><span>{{ item.subtitle || [item.faction, item.product].filter(Boolean).join(' · ') }}</span>
        </button>
        <p v-if="!visible.length">没有符合条件的卡牌。</p>
      </div>
    </section>
  </div>
</template>

<style scoped>
.single-card-mask{position:fixed;z-index:3400;inset:0;display:grid;padding:30px;background:#020507dc;place-items:center}.single-card-picker{display:grid;width:min(1120px,96vw);height:min(820px,92vh);grid-template-rows:auto auto auto minmax(0,1fr);border:1px solid #66572b;background:#0b1116;color:#eff2ef;box-shadow:0 30px 100px #000}.single-card-picker>header{display:flex;align-items:center;justify-content:space-between;padding:15px 18px;border-bottom:1px solid #374147}.single-card-picker h2{margin:4px 0 0;font-size:22px}.single-card-picker small{color:#cfad43;font-size:12px;font-weight:900;letter-spacing:.08em}.single-card-picker>header button{border:0;background:transparent;color:#fff;font-size:28px}.filters{display:grid;grid-template-columns:minmax(220px,2fr) repeat(3,minmax(130px,1fr));gap:8px;padding:12px}.filters input,.filters select{min-width:0;padding:9px;border:1px solid #445159;background:#070c10;color:#fff}.result-count{padding:0 14px 7px;color:#87959b;font-size:12px}.card-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(142px,1fr));gap:11px;overflow:auto;padding:14px;align-content:start}.card-choice{display:grid;gap:5px;min-width:0;padding:8px;border:1px solid #39464d;background:#111a21;color:#fff;text-align:left}.card-choice:hover,.card-choice:focus-visible{border-color:#e1bd50;box-shadow:0 0 14px #c598383d}.card-choice img,.card-choice :deep(.l12-card-image),.image-empty{width:100%;aspect-ratio:5/7;object-fit:cover;background:#050708}.image-empty{display:grid;color:#647078;place-items:center}.card-choice b,.card-choice small,.card-choice span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.card-choice span{color:#93a0a6;font-size:12px}@media(max-width:720px){.single-card-mask{padding:6px}.single-card-picker{width:100%;height:97vh}.filters{grid-template-columns:1fr 1fr}.filters input{grid-column:1/-1}.card-grid{grid-template-columns:repeat(auto-fill,minmax(106px,1fr));gap:7px;padding:9px}}
</style>
