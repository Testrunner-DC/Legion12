<script setup lang="ts">
import { nextTick, ref, watch } from 'vue'

const props = withDefaults(defineProps<{
  modelValue: boolean
  title?: string
  activeCount?: number
}>(), { title: '筛选与排序', activeCount: 0 })

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  reset: []
}>()

const closeButton = ref<HTMLButtonElement | null>(null)
let trigger: HTMLElement | null = null

function openFrom(event: Event) {
  trigger = event.currentTarget instanceof HTMLElement ? event.currentTarget : null
  emit('update:modelValue', true)
}
function close() { emit('update:modelValue', false) }
function reset() { emit('reset') }

watch(() => props.modelValue, visible => {
  if (visible) nextTick(() => closeButton.value?.focus())
  else nextTick(() => trigger?.focus())
})
</script>

<template>
  <button class="mobile-filter-trigger" type="button" :aria-expanded="modelValue" @click="openFrom">
    筛选<span v-if="activeCount"> {{ activeCount }}</span>
  </button>
  <Teleport to="body">
    <section v-if="modelValue" class="mobile-filter-mask" role="dialog" aria-modal="true" :aria-label="title" @click.self="close" @keydown.esc="close">
      <div class="mobile-filter-sheet">
        <header><div><small>FILTERS</small><h2>{{ title }}</h2></div><button ref="closeButton" type="button" aria-label="关闭筛选" @click="close">×</button></header>
        <div class="mobile-filter-content"><slot /></div>
        <footer><button type="button" @click="reset">重置</button><button type="button" class="primary" @click="close"><slot name="apply-label">查看结果</slot></button></footer>
      </div>
    </section>
  </Teleport>
</template>

<style scoped>
.mobile-filter-trigger{display:none}
.mobile-filter-mask{position:fixed;z-index:5000;inset:0;display:grid;align-items:end;padding-top:max(10px,env(safe-area-inset-top));padding-right:env(safe-area-inset-right);padding-left:env(safe-area-inset-left);background:#020609b8;backdrop-filter:blur(7px)}.mobile-filter-sheet{box-sizing:border-box;display:grid;width:100%;max-height:min(82dvh,720px);grid-template-rows:auto minmax(0,1fr) auto;border:1px solid #59666b;border-bottom:0;background:#10191f;color:#f1f0e8;box-shadow:0 -22px 68px #000}.mobile-filter-sheet header{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:15px max(18px,env(safe-area-inset-right)) 15px max(18px,env(safe-area-inset-left));border-bottom:1px solid #36434a}.mobile-filter-sheet small{color:#e1bd5d;font-size:11px;font-weight:900;letter-spacing:.13em}.mobile-filter-sheet h2{margin:3px 0 0;font-size:18px}.mobile-filter-sheet header button{display:grid;width:38px;height:38px;place-items:center;border:1px solid #627078;background:#080e12;color:#fff;font-size:24px}.mobile-filter-content{min-height:0;overflow:auto;padding:16px max(18px,env(safe-area-inset-right)) calc(16px + env(safe-area-inset-bottom)) max(18px,env(safe-area-inset-left));overscroll-behavior:contain}.mobile-filter-sheet footer{display:grid;grid-template-columns:1fr 1.4fr;gap:9px;padding:12px max(18px,env(safe-area-inset-right)) max(12px,env(safe-area-inset-bottom)) max(18px,env(safe-area-inset-left));border-top:1px solid #36434a;background:#0c1318}.mobile-filter-sheet footer button{min-height:42px;border:1px solid #5b6970;background:#172129;color:#f1f1eb;font-size:14px;font-weight:900}.mobile-filter-sheet footer .primary{border-color:#e0bb5b;background:#e0bb5b;color:#0c1012}
@media(max-width:700px){.mobile-filter-trigger{display:inline-flex;min-height:40px;align-items:center;justify-content:center;padding:8px 12px;border:1px solid #62737b;background:#17242b;color:#e7eadf;font-size:13px;font-weight:900;white-space:nowrap}.mobile-filter-trigger span{margin-left:4px;color:#f3cf6f}}
</style>
