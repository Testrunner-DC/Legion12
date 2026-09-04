<script setup lang="ts">
import { onBeforeUnmount, onMounted, watch } from 'vue'
import { authState, platformState } from '@/l12/platform'
import {
  claimNextRankedBroadcast,
  completeCurrentRankedBroadcast,
  rankedBroadcastPlayback,
} from './rankedBroadcastPlayback'

let timer: number | undefined

function schedule(delay = 15_000) {
  if (timer) window.clearTimeout(timer)
  timer = window.setTimeout(() => { void load() }, delay)
}

async function load() {
  if (!authState.verified || !platformState.account) return schedule()
  const claim = await claimNextRankedBroadcast()
  if (!claim) schedule()
}

async function complete(event: AnimationEvent) {
  if (event.target !== event.currentTarget) return
  const completed = await completeCurrentRankedBroadcast()
  schedule(completed ? 500 : 5_000)
}

watch(() => `${authState.verified}:${platformState.account?.id ?? ''}`, () => { void load() })
onMounted(() => { void load() })
onBeforeUnmount(() => { if (timer) window.clearTimeout(timer) })
</script>
<template><div v-if="rankedBroadcastPlayback.claim" class="ranked-ticker" aria-label="排位快讯"><b>排位快讯</b><div><span :key="rankedBroadcastPlayback.claim.broadcast.id" @animationend="complete">📣 {{ rankedBroadcastPlayback.claim.broadcast.message }}</span></div></div></template>
<style scoped>
.ranked-ticker{position:relative;z-index:30;display:grid;grid-template-columns:auto 1fr;align-items:center;min-height:30px;overflow:hidden;border:1px solid #66572f;background:#12120ef2;color:#f0dda0;font-size:10px;box-shadow:0 7px 24px #0009}.ranked-ticker>b{position:relative;z-index:2;height:100%;padding:0 12px;background:#b9953f;color:#111;line-height:30px}.ranked-ticker>div{min-width:0;overflow:hidden}.ranked-ticker span{display:block;width:max-content;padding:0 42px;white-space:nowrap;animation:ranked-message-once 16s linear 1 both}@keyframes ranked-message-once{from{transform:translateX(100vw)}to{transform:translateX(-110%)}}
@media(prefers-reduced-motion:reduce){.ranked-ticker span{animation-duration:4s}}
</style>
