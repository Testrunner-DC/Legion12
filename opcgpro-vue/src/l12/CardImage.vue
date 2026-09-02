<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
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
const resolutionComplete = ref(Boolean(cachedAsset || props.legacyUrl))
const sourceIndex = ref(0)
const highRequested = ref(false)
const avifDisabled = ref(false)
const renderKey = ref(0)

const activeSource = computed(() => resolved.value.sources[sourceIndex.value]
  ?? { kind: 'placeholder', lowWebp: CARD_IMAGE_PLACEHOLDER, webp: CARD_IMAGE_PLACEHOLDER } as CardAssetSource)
const useHigh = computed(() => props.intent === 'detail' && highRequested.value)
const imageUrl = computed(() => useHigh.value ? activeSource.value.webp : activeSource.value.lowWebp)
const avifUrl = computed(() => useHigh.value && !avifDisabled.value ? activeSource.value.avif : undefined)
const landscapeThumbnail = computed(() => props.intent === 'thumb' && resolved.value.orientation === 'landscape')
const imageReady = computed(() => resolutionComplete.value || activeSource.value.kind !== 'placeholder')

async function refresh() {
  const expected = `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`
  const cached = peekCardAsset(props.cardId, props.legacyUrl, props.intent)
  if (cached) {
    resolved.value = cached
    resolutionComplete.value = true
  } else if (!props.legacyUrl) {
    // Keep the stable card-sized shell, but never expose the XII placeholder
    // while a real manifest-backed image is still resolving.
    resolutionComplete.value = false
  }
  const next = await resolveCardAsset(props.cardId, props.legacyUrl, props.intent)
  if (expected !== `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`) return
  resolved.value = next
  resolutionComplete.value = true
  sourceIndex.value = 0
  highRequested.value = false
  avifDisabled.value = false
  renderKey.value += 1
}

function requestHighResolution() {
  if (props.intent === 'detail' && !highRequested.value) {
    highRequested.value = true
    avifDisabled.value = false
    renderKey.value += 1
  }
}

function onLoad() {
  emit('load', activeSource.value)
  if (props.intent === 'detail' && !highRequested.value && activeSource.value.kind !== 'placeholder') {
    setTimeout(requestHighResolution, 0)
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
  const failedUrl = absoluteSourceUrl(image?.currentSrc || image?.src)
  const activeUrls = [avifUrl.value, imageUrl.value].map(absoluteSourceUrl).filter(Boolean)
  // A replaced <img> can finish reporting the previous source after the next
  // fallback has already been selected. Ignore that stale event so one failed
  // CDN request cannot skip the valid same-origin source.
  if (failedUrl && !activeUrls.includes(failedUrl)) return
  if (avifUrl.value) {
    avifDisabled.value = true
    renderKey.value += 1
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
</script>

<template>
  <picture
    class="l12-card-image"
    :class="{ 'landscape-thumbnail-image': landscapeThumbnail }"
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
.l12-card-image.landscape-thumbnail-image{position:relative;left:50%;top:50%;width:140%;height:71.43%;transform:translate(-50%,-50%) rotate(90deg);transform-origin:center}
</style>
