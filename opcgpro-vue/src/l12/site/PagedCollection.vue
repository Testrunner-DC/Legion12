<script setup lang="ts" generic="T">
import { computed, ref, watch } from 'vue'
const props = withDefaults(defineProps<{ items: readonly T[]; pageSize?: number; label?: string; serverPage?: number; serverTotal?: number; busy?: boolean }>(), { pageSize: 10, label: '记录', busy: false })
const emit = defineEmits<{ page: [page: number] }>()
const page = ref(1)
const size = computed(() => Math.max(1, props.pageSize))
const serverMode = computed(() => props.serverPage !== undefined && props.serverTotal !== undefined)
const total = computed(() => serverMode.value ? Math.max(0, props.serverTotal!) : props.items.length)
const pages = computed(() => Math.max(1, Math.ceil(total.value / size.value)))
const currentPage = computed(() => serverMode.value ? Math.max(1, props.serverPage!) : page.value)
const visible = computed(() => serverMode.value ? props.items : props.items.slice((page.value - 1) * size.value, page.value * size.value))
function changePage(next: number) {
  if (props.busy) return
  const target = Math.max(1, Math.min(pages.value, next))
  if (serverMode.value) emit('page', target)
  else page.value = target
}
watch(() => props.items, () => { if (!serverMode.value) page.value = 1 })
watch(pages, count => { if (!serverMode.value) page.value = Math.min(page.value, count) })
</script>

<template>
  <slot :items="visible" :offset="(currentPage - 1) * size"/>
  <nav v-if="total > size" class="collection-pagination" :aria-label="`${label}分页`">
    <button type="button" :disabled="busy || currentPage === 1" @click="changePage(currentPage - 1)">上一页</button>
    <span aria-live="polite">第 {{ currentPage }} / {{ pages }} 页 · 共 {{ total }} 条</span>
    <button type="button" :disabled="busy || currentPage === pages" @click="changePage(currentPage + 1)">下一页</button>
  </nav>
</template>

<style scoped>
.collection-pagination{grid-column:1/-1;display:flex;flex-wrap:wrap;justify-content:center;align-items:center;gap:12px;padding:12px;min-width:0;color:#bbc6cd;font-size:14px}
.collection-pagination button{padding:8px 12px;background:#14212a;border:1px solid #53616b;color:#e9d58e;font:inherit;cursor:pointer}
.collection-pagination button:disabled{opacity:.4;cursor:default}
</style>
