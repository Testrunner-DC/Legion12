<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { authState, platformState, rankedApi } from '@/l12/platform'
import {
  claimNextRankedBroadcast,
  completeCurrentRankedBroadcast,
  configureRankedBroadcastPlayback,
  rankedBroadcastAnimationStyle,
  rankedBroadcastIntervalMs,
  rankedBroadcastLobbyDelayMs,
  rankedBroadcastPlayback,
} from './rankedBroadcastPlayback'

let timer: number | undefined
let mounted = false
let lifecycleGeneration = 0
let lobbyReadyAt = 0
let lobbyDelayInitialized = false
const route = useRoute()
const animationStyle = computed(() => rankedBroadcastAnimationStyle())
const visibleBroadcast = computed(() => rankedBroadcastPlayback.claim?.broadcast ?? null)

function foregroundOwned(generation = lifecycleGeneration) {
  return mounted && generation === lifecycleGeneration
    && (typeof document === 'undefined' || !document.hidden)
}

function clearScheduledLoad() {
  if (timer !== undefined) window.clearTimeout(timer)
  timer = undefined
}

function schedule(delay = rankedBroadcastIntervalMs(), generation = lifecycleGeneration, refreshSettings = true) {
  clearScheduledLoad()
  if (!foregroundOwned(generation)) return
  timer = window.setTimeout(() => {
    timer = undefined
    if (foregroundOwned(generation)) void load(generation, refreshSettings)
  }, delay)
}

async function load(generation = lifecycleGeneration, refreshSettings = true, initializeLobbyDelay = false) {
  if (!foregroundOwned(generation)) return
  if (refreshSettings) {
    try {
      const settings = await rankedApi.broadcastSettings()
      if (!foregroundOwned(generation)) return
      configureRankedBroadcastPlayback(settings)
    } catch {
      if (!foregroundOwned(generation)) return
      // Keep safe defaults while this owned foreground cycle remains active.
    }
  }
  if (initializeLobbyDelay) {
    lobbyDelayInitialized = true
    lobbyReadyAt = Date.now() + rankedBroadcastLobbyDelayMs()
    const remaining = lobbyReadyAt - Date.now()
    if (remaining > 0) {
      schedule(remaining, generation, false)
      return
    }
  }
  const claim = await claimNextRankedBroadcast()
  if (!foregroundOwned(generation)) return
  if (!claim) schedule(rankedBroadcastIntervalMs(), generation)
}

function restartLoading() {
  lifecycleGeneration += 1
  clearScheduledLoad()
  const generation = lifecycleGeneration
  if (!foregroundOwned(generation)) return
  if (!lobbyDelayInitialized) {
    void load(generation, true, true)
    return
  }
  const remaining = lobbyReadyAt - Date.now()
  if (remaining > 0) schedule(remaining, generation)
  else void load(generation)
}

function complete(event: AnimationEvent) {
  if (event.target !== event.currentTarget) return
  const completed = completeCurrentRankedBroadcast()
  schedule(completed ? rankedBroadcastIntervalMs() : 5_000)
}

watch(() => `${authState.verified}:${platformState.account?.id ?? ''}`, () => {
  if (mounted) restartLoading()
})
watch(() => visibleBroadcast.value?.id ?? '', (id, previous) => {
  if (!id && previous) schedule(rankedBroadcastIntervalMs())
})
function handleVisibilityChange() { restartLoading() }
onMounted(() => {
  mounted = true
  lobbyReadyAt = 0
  lobbyDelayInitialized = route.path !== '/lobby'
  if (typeof document !== 'undefined') document.addEventListener('visibilitychange', handleVisibilityChange)
  restartLoading()
})
onBeforeUnmount(() => {
  mounted = false
  lifecycleGeneration += 1
  clearScheduledLoad()
  if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', handleVisibilityChange)
})
</script>
<template><div v-if="visibleBroadcast" class="ranked-ticker" aria-label="排位快讯"><b>排位快讯</b><div><span :key="visibleBroadcast.id" :style="animationStyle" @animationend="complete">📣 {{ visibleBroadcast.message }}</span></div></div></template>
<style scoped>
.ranked-ticker{position:relative;z-index:30;display:grid;grid-template-columns:auto 1fr;align-items:center;min-height:30px;overflow:hidden;border:1px solid #66572f;background:#12120ef2;color:#f0dda0;font-size:14px;box-shadow:0 7px 24px #0009}.ranked-ticker>b{position:relative;z-index:2;height:100%;padding:0 12px;background:#b9953f;color:#111;line-height:30px}.ranked-ticker>div{min-width:0;overflow:hidden}.ranked-ticker span{display:block;width:max-content;padding:0 42px;white-space:nowrap;animation:ranked-message-once 16s linear 1 both}@keyframes ranked-message-once{from{transform:translateX(100vw)}to{transform:translateX(-110%)}}
@media(prefers-reduced-motion:reduce){.ranked-ticker span{animation-duration:4s}}
</style>
