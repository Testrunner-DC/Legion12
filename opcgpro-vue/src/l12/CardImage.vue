<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import {
  CARD_IMAGE_PLACEHOLDER,
  fallbackCardAsset,
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

const resolved = ref(fallbackCardAsset(props.cardId, props.legacyUrl, props.intent))
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

async function refresh() {
  const expected = `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`
  const next = await resolveCardAsset(props.cardId, props.legacyUrl, props.intent)
  if (expected !== `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`) return
  resolved.value = next
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

function onError() {
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
    <source :key="`webp-${renderKey}`" type="image/webp" :srcset="imageUrl" />
    <img
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
  </picture>
</template>

<style scoped>
.l12-card-image{display:block;width:100%;height:100%;overflow:hidden;background:#090d0e;line-height:0}
.l12-card-image__img{display:block;width:100%;height:100%;background:#090d0e}
.l12-card-image.landscape-thumbnail-image{position:relative;left:50%;top:50%;width:140%;height:71.43%;transform:translate(-50%,-50%) rotate(90deg);transform-origin:center}
</style>
