<script lang="ts">
const callbacks = new WeakMap<Element, () => void>()
let sharedObserver: IntersectionObserver | null = null

function observer() {
  if (!sharedObserver) {
    sharedObserver = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (!entry.isIntersecting) continue
        callbacks.get(entry.target)?.()
        callbacks.delete(entry.target)
        sharedObserver?.unobserve(entry.target)
      }
    }, { root: null, rootMargin: '640px 0px' })
  }
  return sharedObserver
}
</script>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import CardImage from './CardImage.vue'

const props = defineProps<{
  cardId?: string
  legacyUrl?: string
  alt: string
}>()

const root = ref<HTMLElement | null>(null)
const visible = ref(false)
let stopObserving: (() => void) | null = null

onMounted(() => {
  const deferOnThisDevice = window.matchMedia('(max-width: 900px), (pointer: coarse)').matches
  if (!deferOnThisDevice || !root.value || typeof IntersectionObserver === 'undefined') {
    visible.value = true
    return
  }
  const target = root.value
  callbacks.set(target, () => { visible.value = true })
  observer().observe(target)
  stopObserving = () => {
    callbacks.delete(target)
    sharedObserver?.unobserve(target)
  }
})

onBeforeUnmount(() => stopObserving?.())
</script>

<template>
  <span ref="root" class="mobile-deferred-card-image">
    <CardImage v-if="visible" :card-id="props.cardId" :legacy-url="props.legacyUrl" :alt="props.alt" intent="thumb"/>
  </span>
</template>

<style scoped>
.mobile-deferred-card-image{display:block;width:100%;height:100%;background:#090d0e}
.mobile-deferred-card-image :deep(.l12-card-image){width:100%;height:100%}
</style>
