<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  CARD_IMAGE_PLACEHOLDER,
  fallbackCardAsset,
  peekCardAsset,
  resolveCardAsset,
  type CardAssetSource,
  type CardImageIntent,
} from './cardAssets'

const props = withDefaults(defineProps<{
  cardId?: string
  legacyUrl?: string
  alt: string
  intent?: CardImageIntent
  eager?: boolean
  fit?: 'contain' | 'cover'
  objectPosition?: string
  nativeOrientation?: boolean
}>(), {
  cardId: '',
  legacyUrl: undefined,
  intent: 'thumb',
  eager: false,
  fit: 'contain',
  objectPosition: 'center',
})

const emit = defineEmits<{
  load: [source: CardAssetSource]
  fallback: [kind: CardAssetSource['kind']]
}>()

const cachedAsset = peekCardAsset(props.cardId, props.legacyUrl, props.intent)
const resolved = ref(cachedAsset ?? fallbackCardAsset(props.cardId, props.legacyUrl, props.intent))
const resolutionComplete = ref(Boolean(cachedAsset
  || resolved.value.sources.some(source => source.kind !== 'placeholder')))
const sourceIndex = ref(0)
const highRequested = ref(false)
const avifDisabled = ref(false)
const renderKey = ref(0)
const sourceRetryCounts = new Map<string, number>()
const MAX_SOURCE_RETRIES = 2
const SOURCE_RETRY_DELAY_MS = 300
let resolvedIdentity = `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`
let refreshGeneration = 0
let highResolutionGeneration = 0
let highResolutionPending = false
let disposed = false

const activeSource = computed(() => resolved.value.sources[sourceIndex.value]
  ?? { kind: 'placeholder', lowWebp: CARD_IMAGE_PLACEHOLDER, webp: CARD_IMAGE_PLACEHOLDER } as CardAssetSource)
const useHigh = computed(() => props.intent === 'detail' && highRequested.value)
const imageUrl = computed(() => useHigh.value ? activeSource.value.webp : activeSource.value.lowWebp)
const avifUrl = computed(() => useHigh.value && !avifDisabled.value ? activeSource.value.avif : undefined)
const landscapeImage = computed(() => resolved.value.orientation === 'landscape')
const imageReady = computed(() => resolutionComplete.value || activeSource.value.kind !== 'placeholder')

async function refresh() {
  const expected = `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`
  const generation = ++refreshGeneration
  highResolutionGeneration += 1
  highResolutionPending = false
  const identityChanged = expected !== resolvedIdentity
  resolvedIdentity = expected
  sourceIndex.value = 0
  highRequested.value = false
  avifDisabled.value = false
  sourceRetryCounts.clear()
  // A real card/source identity change must never leave the previous card's
  // decoded pixels in the reused element. The mount-time refresh below keeps
  // the node because its identity and URL are already correct.
  if (identityChanged) renderKey.value += 1
  const cached = peekCardAsset(props.cardId, props.legacyUrl, props.intent)
  if (cached) {
    resolved.value = cached
    resolutionComplete.value = true
  } else {
    // 外部旧图地址不会被信任为展示源；清单仍在读取时只保留稳定卡位，
    // 不先闪出卡背，也不在组件复用时短暂显示上一张卡。
    resolved.value = fallbackCardAsset(props.cardId, props.legacyUrl, props.intent)
    resolutionComplete.value = resolved.value.sources.some(source => source.kind !== 'placeholder')
  }
  const next = await resolveCardAsset(props.cardId, props.legacyUrl, props.intent)
  if (disposed || generation !== refreshGeneration
    || expected !== `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`) return
  resolved.value = next
  resolutionComplete.value = true
}

function wait(delay: number) {
  return new Promise<void>(resolve => window.setTimeout(resolve, delay))
}

function loadDecodedImage(url: string) {
  return new Promise<boolean>(resolve => {
    const image = new Image()
    image.decoding = 'async'
    image.onload = async () => {
      try { await image.decode() } catch { /* onload already confirmed a drawable resource */ }
      resolve(true)
    }
    image.onerror = () => resolve(false)
    image.src = url
  })
}

async function preloadWithRetries(url: string, generation: number) {
  for (let attempt = 0; attempt <= MAX_SOURCE_RETRIES; attempt += 1) {
    if (disposed || generation !== highResolutionGeneration) return false
    if (await loadDecodedImage(url)) return true
    if (attempt < MAX_SOURCE_RETRIES) await wait(SOURCE_RETRY_DELAY_MS * (attempt + 1))
  }
  return false
}

async function requestHighResolution() {
  if (disposed || props.intent !== 'detail' || highRequested.value || highResolutionPending
    || activeSource.value.kind === 'placeholder') return
  const generation = ++highResolutionGeneration
  const expectedIdentity = resolvedIdentity
  const expectedSource = activeSource.value
  highResolutionPending = true
  let nextAvifDisabled = false
  let ready = false
  if (expectedSource.avif) {
    ready = await preloadWithRetries(expectedSource.avif, generation)
    nextAvifDisabled = !ready
  }
  if (!ready && expectedSource.webp) ready = await preloadWithRetries(expectedSource.webp, generation)
  if (disposed || generation !== highResolutionGeneration) return
  highResolutionPending = false
  if (expectedIdentity !== resolvedIdentity || expectedSource !== activeSource.value) return
  if (!ready) return
  avifDisabled.value = nextAvifDisabled
  // The candidate is already decoded. Updating the existing element lets the
  // browser atomically replace its pixels instead of exposing the dark slot.
  highRequested.value = true
}

function onLoad(event: Event) {
  emit('load', activeSource.value)
  if (props.intent === 'detail' && !highRequested.value && activeSource.value.kind !== 'placeholder') {
    const image = event.currentTarget as HTMLImageElement | null
    if (image?.naturalWidth) window.setTimeout(() => { void requestHighResolution() }, 0)
  }
}

function absoluteSourceUrl(value: string | undefined) {
  if (!value) return ''
  try {
    return new URL(value, window.location.href).href
  } catch {
    return value
  }
}

function onError(event: Event) {
  const image = event.currentTarget as HTMLImageElement | null
  // A late/synthetic error must not evict pixels that the browser has already
  // decoded successfully for the active element.
  if (image?.complete && image.naturalWidth > 0) return
  const failedUrl = absoluteSourceUrl(image?.currentSrc || image?.src)
  const activeUrls = [avifUrl.value, imageUrl.value].map(absoluteSourceUrl).filter(Boolean)
  // A replaced <img> can finish reporting the previous source after the next
  // fallback has already been selected. Ignore that stale event so one failed
  // CDN request cannot skip the valid same-origin source.
  if (failedUrl && !activeUrls.includes(failedUrl)) return
  if (avifUrl.value) {
    avifDisabled.value = true
    return
  }
  const retryKey = `${activeSource.value.kind}\n${imageUrl.value}`
  const retryCount = sourceRetryCounts.get(retryKey) ?? 0
  if (activeSource.value.kind !== 'placeholder' && retryCount < MAX_SOURCE_RETRIES) {
    sourceRetryCounts.set(retryKey, retryCount + 1)
    const expectedUrl = imageUrl.value
    window.setTimeout(() => {
      if (imageUrl.value !== expectedUrl) return
      renderKey.value += 1
    }, SOURCE_RETRY_DELAY_MS * (retryCount + 1))
    return
  }
  if (sourceIndex.value < resolved.value.sources.length - 1) {
    sourceIndex.value += 1
    avifDisabled.value = false
    renderKey.value += 1
    emit('fallback', activeSource.value.kind)
  }
}

watch(() => [props.cardId, props.legacyUrl, props.intent] as const, refresh)
onMounted(refresh)
onBeforeUnmount(() => {
  disposed = true
  refreshGeneration += 1
  highResolutionGeneration += 1
  highResolutionPending = false
})
</script>

<template>
  <picture
    class="l12-card-image"
    :class="{ 'l12-card-image--landscape': landscapeImage }"
    :data-source="activeSource.kind"
    :data-orientation="resolved.orientation || 'unknown'"
    @mouseenter="requestHighResolution"
    @focusin="requestHighResolution"
  >
    <source v-if="avifUrl" :key="`avif-${renderKey}`" type="image/avif" :srcset="avifUrl" />
    <img
      v-if="imageReady"
      :key="`img-${renderKey}`"
      class="l12-card-image__img"
      :src="imageUrl"
      :alt="alt"
      :loading="eager ? 'eager' : 'lazy'"
      decoding="async"
      :fetchpriority="eager ? 'high' : 'auto'"
      :style="{ objectFit: fit, objectPosition }"
      @load="onLoad"
      @error="onError"
    />
    <span v-else class="l12-card-image__resolving" aria-hidden="true"></span>
  </picture>
</template>

<style scoped>
.l12-card-image{display:block;width:100%;height:100%;overflow:hidden;background:#090d0e;line-height:0}
.l12-card-image__img{display:block;width:100%;height:100%;background:#090d0e}
.l12-card-image__resolving{display:block;width:100%;height:100%;background:#090d0e}
.l12-card-image--landscape{position:static!important;left:auto!important;top:auto!important;width:100%!important;height:100%!important;transform:none!important}
.l12-card-image--landscape .l12-card-image__img{object-position:center}
</style>
