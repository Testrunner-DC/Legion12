<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { adminApi, type SiteMedia, type SiteMediaKind, type SiteMediaSearch } from '@/l12/platform'

const props = withDefaults(defineProps<{
  modelValue?: string; kind: SiteMediaKind; selectedMedia?: SiteMedia; emptyLabel?: string
}>(), { modelValue: '', emptyLabel: '选择已上传素材' })
const emit = defineEmits<{ 'update:modelValue': [value: string]; selected: [value: SiteMedia] }>()
const open = ref(false)
const busy = ref(false)
const error = ref('')
const search = ref('')
const appliedSearch = ref('')
const requestedPage = ref(1)
const trigger = ref<HTMLButtonElement | null>(null)
const searchInput = ref<HTMLInputElement | null>(null)
const dialog = ref<HTMLElement | null>(null)
const result = ref<SiteMediaSearch>({ items: [], page: 1, pageSize: 24, total: 0, hasMore: false })
const label = computed(() => props.selectedMedia?.id === props.modelValue
  ? props.selectedMedia.altText || props.selectedMedia.contentHash.slice(0, 12)
  : props.modelValue ? `已绑定素材 ${props.modelValue.slice(0, 12)}…` : props.emptyLabel)
let generation = 0
async function load(page = 1) {
  const attempt = ++generation
  requestedPage.value = page
  busy.value = true; error.value = ''
  try {
    const next = await adminApi.searchSiteMedia({ kind: props.kind, search: appliedSearch.value, page, pageSize: 24 })
    if (attempt === generation && open.value) result.value = next
  } catch (reason) {
    if (attempt === generation && open.value) error.value = reason instanceof Error ? reason.message : '素材读取失败'
  } finally { if (attempt === generation) busy.value = false }
}
function applySearch() { appliedSearch.value = search.value.trim(); void load(1) }
async function show() {
  open.value = true; search.value = ''; appliedSearch.value = ''
  void load(1)
  await nextTick(); searchInput.value?.focus()
}
function close() { generation++; open.value = false; busy.value = false; void nextTick(() => trigger.value?.focus()) }
function choose(item: SiteMedia) {
  if (busy.value || error.value || item.kind !== props.kind) return
  emit('update:modelValue', item.id); emit('selected', item); close()
}
function clear() { emit('update:modelValue', '') }
function keydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); return }
  if (event.key !== 'Tab') return
  const targets = Array.from(dialog.value?.querySelectorAll<HTMLElement>('button:not(:disabled),input') || [])
  const first = targets[0]; const last = targets.at(-1)
  if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
  else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
}
watch(() => props.kind, () => { generation++; if (open.value) { search.value = ''; appliedSearch.value = ''; void load(1) } })
onBeforeUnmount(() => { generation++ })
</script>

<template>
  <span class="media-choice">
    <button ref="trigger" type="button" class="media-choice-trigger" aria-haspopup="dialog" :aria-expanded="open" @click="show">{{ label }}</button>
    <button v-if="modelValue" type="button" class="media-choice-clear" aria-label="清除素材选择" @click="clear">清除</button>
  </span>
  <Teleport to="body">
    <div v-if="open" class="site-media-picker-overlay" @click.self="close" @keydown="keydown">
      <section ref="dialog" class="site-media-picker" role="dialog" aria-modal="true" aria-label="选择已上传素材">
        <header><h2>选择已上传素材</h2><button type="button" aria-label="关闭素材选择" @click="close">关闭</button></header>
        <form @submit.prevent="applySearch"><input ref="searchInput" v-model="search" maxlength="160" aria-label="搜索素材" placeholder="搜索说明、素材编号或哈希"><button type="submit" :disabled="busy">搜索</button></form>
        <p v-if="error" role="alert">{{ error }} <button type="button" @click="load(requestedPage)">重试</button></p>
        <p v-else-if="busy" role="status">正在读取…</p>
        <div class="site-media-picker-grid" :aria-busy="busy">
          <button v-for="item in result.items" :key="item.id" type="button" :disabled="busy || Boolean(error)" :aria-pressed="item.id === modelValue" @click="choose(item)">
            <img :src="item.thumbnailUrl" :alt="item.thumbnailAltText || item.altText" loading="lazy">
            <b>{{ item.altText || item.contentHash.slice(0, 12) }}</b><small>{{ item.contentHash.slice(0, 12) }} · 引用 {{ item.referenceCount }}</small>
          </button>
        </div>
        <p v-if="!busy && !error && !result.items.length">没有符合条件的素材。</p>
        <footer><button type="button" :disabled="busy || Boolean(error) || result.page <= 1" @click="load(result.page - 1)">上一页</button><span>第 {{ result.page }} 页 · 共 {{ result.total }} 项</span><button type="button" :disabled="busy || Boolean(error) || !result.hasMore" @click="load(result.page + 1)">下一页</button></footer>
      </section>
    </div>
  </Teleport>
</template>

<style scoped>
.media-choice{display:flex;gap:8px;align-items:center;min-width:0}.media-choice-trigger{flex:1;min-width:0;overflow-wrap:anywhere;text-align:left}.media-choice button,.site-media-picker button{min-height:40px;border:1px solid #46545d;background:#121e25;color:#e3e9e9;padding:8px 12px;font:inherit;cursor:pointer}.site-media-picker-overlay{position:fixed;inset:0;z-index:3600;display:grid;place-items:center;padding:16px;background:#000b}.site-media-picker{width:min(900px,100%);max-height:calc(100dvh - 32px);display:flex;flex-direction:column;gap:14px;padding:18px;border:1px solid #52616a;background:#0c151c;color:#e3e9e9;box-sizing:border-box;overflow:auto}.site-media-picker header,.site-media-picker footer{display:flex;align-items:center;justify-content:space-between;gap:12px}.site-media-picker h2{font-size:18px;margin:0}.site-media-picker form{display:flex;gap:8px}.site-media-picker input{flex:1;min-width:0;min-height:40px;padding:8px 10px;box-sizing:border-box;border:1px solid #46545d;background:#050a0e;color:#fff;font:inherit}.site-media-picker-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;overflow:auto;min-height:0}.site-media-picker-grid button{display:flex;flex-direction:column;gap:6px;align-items:stretch;text-align:left;min-width:0}.site-media-picker-grid button[aria-pressed=true]{border-color:#e8c15c}.site-media-picker-grid img{height:120px;width:100%;object-fit:contain;background:#05090c}.site-media-picker-grid b,.site-media-picker-grid small{overflow-wrap:anywhere}.site-media-picker-grid small{color:#aab7bd;font-size:12px}.site-media-picker button:disabled{opacity:.45;cursor:default}.site-media-picker button:focus-visible,.media-choice button:focus-visible{outline:2px solid #65d1dc;outline-offset:2px}@media(max-width:650px){.site-media-picker-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.site-media-picker{padding:12px}.site-media-picker footer{flex-wrap:wrap}.site-media-picker-grid img{height:100px}}@media(max-height:450px){.site-media-picker-grid img{height:70px}.site-media-picker{gap:8px}}
</style>
