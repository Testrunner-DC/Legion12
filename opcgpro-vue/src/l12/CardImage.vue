<script lang="ts">
// Stagger only recovery triggers; native images and the existing source retry budget own loading.
const cardRecoveryQueue = new Map<object, () => void>()
let cardRecoveryTimer: number | null = null
function drainCardRecovery() {
  cardRecoveryTimer = null
  const next = cardRecoveryQueue.entries().next().value
  if (next) { cardRecoveryQueue.delete(next[0]); next[1]() }
  if (cardRecoveryQueue.size) cardRecoveryTimer = window.setTimeout(drainCardRecovery, 200)
}
function enqueueCardRecovery(owner: object, run: () => void) {
  if (cardRecoveryQueue.has(owner)) return
  cardRecoveryQueue.set(owner, run)
  if (cardRecoveryTimer === null) cardRecoveryTimer = window.setTimeout(drainCardRecovery, 200)
}
function cancelCardRecovery(owner: object) {
  cardRecoveryQueue.delete(owner)
  if (!cardRecoveryQueue.size && cardRecoveryTimer !== null) {
    window.clearTimeout(cardRecoveryTimer); cardRecoveryTimer = null
  }
}
</script>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch, type VNode } from 'vue'
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
const sourceRetryTimers = new Set<number>()
const imageRoot = ref<HTMLElement | null>(null)
const recoveryOwner = {}
let recoveryObserver: IntersectionObserver | null = null
let recoveryWatchGeneration = 0
let recoverableFailure = false
let recoveryRequested = false
let recoveryVisible = false
let lastRecoveryAt = Number.NEGATIVE_INFINITY
let delayedRecoveryTimer: number | null = null
let delayedRecoveryRequested = false
let delayedRecoveryAttempts = 0
const RECOVERY_COOLDOWN_MS = 30_000
// Native image errors expose neither status nor Retry-After. Wait beyond the
// current 60s admission window without adding a second fetch for every image.
const DELAYED_RECOVERY_BASE_MS = 65_000
const MAX_DELAYED_RECOVERIES = 2
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

function clearSourceRetryTimers() {
  for (const timer of sourceRetryTimers) window.clearTimeout(timer)
  sourceRetryTimers.clear()
}
function clearDelayedRecoveryTimer() {
  if (delayedRecoveryTimer !== null) window.clearTimeout(delayedRecoveryTimer)
  delayedRecoveryTimer = null
}
function scheduleDelayedRecovery() {
  if (disposed || !recoverableFailure || delayedRecoveryTimer !== null || delayedRecoveryRequested
    || delayedRecoveryAttempts >= MAX_DELAYED_RECOVERIES || activeSource.value.kind !== 'placeholder') return
  const generation = refreshGeneration, watchGeneration = recoveryWatchGeneration, identity = resolvedIdentity
  delayedRecoveryTimer = window.setTimeout(() => {
    delayedRecoveryTimer = null
    if (disposed || generation !== refreshGeneration || watchGeneration !== recoveryWatchGeneration
      || identity !== resolvedIdentity || !recoverableFailure || activeSource.value.kind !== 'placeholder') return
    delayedRecoveryRequested = true
    recoveryRequested = true
    maybeRecoverImage()
  }, DELAYED_RECOVERY_BASE_MS * (delayedRecoveryAttempts + 1))
}
function stopRecoveryMonitoring() {
  recoveryWatchGeneration++
  recoveryObserver?.disconnect(); recoveryObserver = null
  window.removeEventListener('online', onNetworkRecovery)
  document.removeEventListener('visibilitychange', onRecoveryVisibility)
  cancelCardRecovery(recoveryOwner)
  clearDelayedRecoveryTimer(); delayedRecoveryRequested = false
  recoverableFailure = false; recoveryRequested = false; recoveryVisible = false
}
function maybeRecoverImage() {
  if (disposed || !recoverableFailure || !recoveryRequested || !recoveryVisible
    || document.visibilityState !== 'visible' || navigator.onLine === false) return
  const generation = refreshGeneration, identity = resolvedIdentity
  enqueueCardRecovery(recoveryOwner, () => {
    if (disposed || generation !== refreshGeneration || identity !== resolvedIdentity
      || !recoverableFailure || !recoveryRequested || !recoveryVisible
      || document.visibilityState !== 'visible' || navigator.onLine === false) return
    if (performance.now() - lastRecoveryAt < RECOVERY_COOLDOWN_MS) return
    if (delayedRecoveryRequested) {
      if (delayedRecoveryAttempts >= MAX_DELAYED_RECOVERIES) return
      delayedRecoveryAttempts++
    }
    recoveryRequested = false; delayedRecoveryRequested = false
    clearDelayedRecoveryTimer()
    lastRecoveryAt = performance.now()
    clearSourceRetryTimers(); sourceRetryCounts.clear()
    highResolutionGeneration++; highResolutionPending = false
    highRequested.value = false; avifDisabled.value = false; sourceIndex.value = 0
    renderKey.value++
  })
}
function onNetworkRecovery() {
  if (!recoverableFailure || performance.now() - lastRecoveryAt < RECOVERY_COOLDOWN_MS) return
  recoveryRequested = true
  maybeRecoverImage()
}
function onRecoveryVisibility() {
  if (document.visibilityState !== 'visible') cancelCardRecovery(recoveryOwner)
  else maybeRecoverImage()
}
function monitorFailedImage() {
  if (disposed) return
  if (recoverableFailure) { scheduleDelayedRecovery(); return }
  recoverableFailure = true
  const generation = ++recoveryWatchGeneration
  window.addEventListener('online', onNetworkRecovery)
  document.addEventListener('visibilitychange', onRecoveryVisibility)
  if (imageRoot.value && typeof IntersectionObserver !== 'undefined') {
    recoveryObserver = new IntersectionObserver(entries => {
      if (disposed || generation !== recoveryWatchGeneration) return
      recoveryVisible = entries.some(entry => entry.target === imageRoot.value && entry.isIntersecting
        && entry.intersectionRect.width > 0 && entry.intersectionRect.height > 0)
      if (!recoveryVisible) cancelCardRecovery(recoveryOwner)
      else maybeRecoverImage()
    })
    recoveryObserver.observe(imageRoot.value)
  }
  scheduleDelayedRecovery()
  // Without an observer fail closed: don't wake offscreen images as a whole-pool fallback.
}

async function refresh() {
  const expected = `${props.cardId}\n${props.legacyUrl ?? ''}\n${props.intent}`
  const generation = ++refreshGeneration
  stopRecoveryMonitoring(); clearSourceRetryTimers()
  lastRecoveryAt = Number.NEGATIVE_INFINITY
  delayedRecoveryAttempts = 0
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
  const loadedImage = event.currentTarget as HTMLImageElement | null
  if (disposed || !loadedImage || imageRoot.value?.querySelector('img') !== loadedImage) return
  const loadedUrl = absoluteSourceUrl(loadedImage?.currentSrc || loadedImage?.src)
  if (loadedUrl && ![imageUrl.value, avifUrl.value].map(absoluteSourceUrl).includes(loadedUrl)) return
  if (activeSource.value.kind !== 'placeholder') {
    if (loadedImage?.naturalWidth) {
      stopRecoveryMonitoring(); clearSourceRetryTimers()
    }
  }
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
  if (disposed || !image || imageRoot.value?.querySelector('img') !== image) return
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
    const generation = refreshGeneration
    const timer = window.setTimeout(() => {
      sourceRetryTimers.delete(timer)
      if (disposed || generation !== refreshGeneration || imageUrl.value !== expectedUrl) return
      renderKey.value += 1
    }, SOURCE_RETRY_DELAY_MS * (retryCount + 1))
    sourceRetryTimers.add(timer)
    return
  }
  if (sourceIndex.value < resolved.value.sources.length - 1) {
    sourceIndex.value += 1
    avifDisabled.value = false
    renderKey.value += 1
    emit('fallback', activeSource.value.kind)
    if (activeSource.value.kind === 'placeholder') monitorFailedImage()
  }
}

function retireImage(vnode: VNode) {
  // Retiring a picture child with its old src can start an unwanted native reread.
  // Clear only this vnode, after Vue decides to replace/unmount it, never the live successor.
  const image = vnode.el as HTMLImageElement | null
  image?.removeAttribute('src')
  image?.removeAttribute('srcset')
}
function retireAvifSource(vnode: VNode) {
  ;(vnode.el as HTMLSourceElement | null)?.removeAttribute('srcset')
}

watch(() => [props.cardId, props.legacyUrl, props.intent] as const, refresh, { flush: 'sync' })
onMounted(refresh)
onBeforeUnmount(() => {
  disposed = true
  stopRecoveryMonitoring(); clearSourceRetryTimers()
  refreshGeneration += 1
  highResolutionGeneration += 1
  highResolutionPending = false
})
</script>

<template>
  <picture
    ref="imageRoot"
    class="l12-card-image"
    :class="{ 'l12-card-image--landscape': landscapeImage }"
    :data-source="activeSource.kind"
    :data-orientation="resolved.orientation || 'unknown'"
    @mouseenter="requestHighResolution"
    @focusin="requestHighResolution"
  >
    <source v-if="avifUrl" :key="`avif-${renderKey}`" type="image/avif" :srcset="avifUrl" @vue:before-unmount="retireAvifSource" />
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
      @vue:before-unmount="retireImage"
    />
    <span v-else class="l12-card-image__resolving" aria-hidden="true"></span>
  </picture>
</template>

<style scoped>
.l12-card-image{--l12-card-image-default-radius:4% / 2.86%;display:block;width:100%;height:100%;overflow:hidden;border-radius:var(--l12-card-image-radius,var(--l12-card-image-default-radius));background:#090d0e;line-height:0}
.l12-card-image__img{display:block;width:100%;height:100%;border-radius:var(--l12-card-image-radius,var(--l12-card-image-default-radius));clip-path:inset(0 round var(--l12-card-image-radius,var(--l12-card-image-default-radius)));background:#090d0e}
.l12-card-image__resolving{display:block;width:100%;height:100%;border-radius:var(--l12-card-image-radius,var(--l12-card-image-default-radius));clip-path:inset(0 round var(--l12-card-image-radius,var(--l12-card-image-default-radius)));background:#090d0e}
.l12-card-image--landscape{--l12-card-image-default-radius:4% / 6.4%;position:static!important;left:auto!important;top:auto!important;width:100%!important;height:100%!important;transform:none!important}
.l12-card-image--landscape .l12-card-image__img{object-position:center}
</style>
