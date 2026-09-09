<script setup lang="ts" generic="T">
import { computed, ref, watch } from 'vue'
const props = withDefaults(defineProps<{ items: readonly T[]; pageSize?: number; label?: string }>(), { pageSize: 10, label: '记录' })
const page = ref(1)
const size = computed(() => Math.max(1, props.pageSize))
const pages = computed(() => Math.max(1, Math.ceil(props.items.length / size.value)))
const visible = computed(() => props.items.slice((page.value - 1) * size.value, page.value * size.value))
watch(() => props.items, () => { page.value = 1 })
watch(pages, count => { page.value = Math.min(page.value, count) })
</script>

<template>
  <slot :items="visible" :offset="(page - 1) * size"/>
  <nav v-if="items.length > size" class="collection-pagination" :aria-label="`${label}分页`">
    <button type="button" :disabled="page === 1" @click="page--">上一页</button>
    <span aria-live="polite">第 {{ page }} / {{ pages }} 页 · 共 {{ items.length }} 条</span>
    <button type="button" :disabled="page === pages" @click="page++">下一页</button>
  </nav>
</template>

<style scoped>
.collection-pagination{grid-column:1/-1;display:flex;flex-wrap:wrap;justify-content:center;align-items:center;gap:12px;padding:12px;min-width:0;color:#bbc6cd;font-size:14px}
.collection-pagination button{padding:8px 12px;background:#14212a;border:1px solid #53616b;color:#e9d58e;font:inherit;cursor:pointer}
.collection-pagination button:disabled{opacity:.4;cursor:default}
</style>
