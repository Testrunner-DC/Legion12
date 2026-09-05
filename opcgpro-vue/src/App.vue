<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { l12State, startAutomaticConnection, stopAutomaticConnection } from '@/l12/net'
import { authState, platformState } from '@/l12/platform'
import SiteShell from '@/l12/site/SiteShell.vue'
import GlobalBugFeedback from '@/l12/site/GlobalBugFeedback.vue'
import { applyAudioPreferences, audioPreferences } from '@/l12/audioPreferences'

const route = useRoute()
const router = useRouter()
const immersive = computed(() => route.meta.immersive === true)
let backgroundAudio: HTMLAudioElement | null = null
let battleTrack = 0
let primed = false
const battleTracks = ['/audio/legion12-battle-1.mp3', '/audio/legion12-battle-2.mp3']
const desiredTrack = () => route.path === '/game' ? battleTracks[battleTrack] : '/audio/legion12-site.mp3'
function refreshBackgroundMusic() {
  if (!primed || !audioPreferences.musicEnabled || audioPreferences.musicVolume <= 0) {
    backgroundAudio?.pause()
    return
  }
  const src = desiredTrack()
  if (!backgroundAudio || !backgroundAudio.src.endsWith(src)) {
    backgroundAudio?.pause()
    backgroundAudio = new Audio(src)
    backgroundAudio.loop = route.path !== '/game'
    backgroundAudio.addEventListener('ended', () => {
      if (route.path !== '/game') return
      battleTrack = (battleTrack + 1) % battleTracks.length
      refreshBackgroundMusic()
    }, { once: true })
  }
  backgroundAudio.volume = audioPreferences.musicVolume
  void backgroundAudio.play().catch(() => undefined)
}
function primeMusic() { primed = true; refreshBackgroundMusic() }
watch(() => route.path, () => refreshBackgroundMusic())
watch(audioPreferences, () => refreshBackgroundMusic(), { deep: true })
watch(() => platformState.account?.audioPreferences, value => applyAudioPreferences(value), { immediate: true, deep: true })
onMounted(() => window.addEventListener('pointerdown', primeMusic, { once: true }))
onBeforeUnmount(() => { backgroundAudio?.pause(); window.removeEventListener('pointerdown', primeMusic) })
watch(() => [l12State.game, l12State.recoveryPhase] as const, ([game, recoveryPhase]) => {
  if (game && recoveryPhase === 'snapshot-acknowledged' && !l12State.leavingRoom
    && route.path !== '/game' && route.meta.replay !== true) router.push('/game')
})
watch(() => [platformState.token, authState.verified] as const, ([token, verified]) => {
  if (token && verified) startAutomaticConnection()
  else stopAutomaticConnection()
}, { immediate: true })
</script>

<template>
  <router-view v-if="immersive" />
  <SiteShell v-else><router-view /></SiteShell>
  <GlobalBugFeedback />
</template>
