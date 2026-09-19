<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { l12State, startAutomaticConnection, stopAutomaticConnection } from '@/l12/net'
import { authState, platformState, updateAudioPreferences } from '@/l12/platform'
import SiteShell from '@/l12/site/SiteShell.vue'
import GlobalBugFeedback from '@/l12/site/GlobalBugFeedback.vue'
import FriendRequestNotifications from '@/l12/site/FriendRequestNotifications.vue'
import RankedIntegrityNotice from '@/l12/site/RankedIntegrityNotice.vue'
import { applyAudioPreferences, audioPreferences, l12MusicOutputVolume, syncAudioStore } from '@/l12/audioPreferences'
import { BackgroundMusicController } from '@/l12/backgroundMusic'
import { useLandscapeViewport } from '@/l12/mobileViewport'
import '@/l12/mobileViewport.css'

const route = useRoute()
const immersive = computed(() => route.meta.immersive === true)
const landscapeExperience = computed(() => route.path === '/game' || route.meta.replay === true)
useLandscapeViewport(landscapeExperience)
const portraitHandset = ref(false)
function updatePortraitHandset() {
  const viewport = window.visualViewport
  const width = viewport?.width ?? window.innerWidth
  const height = viewport?.height ?? window.innerHeight
  const coarseTouch = window.matchMedia?.('(pointer: coarse) and (hover: none)').matches ?? false
  portraitHandset.value = coarseTouch && Math.min(width, height) <= 820 && height > width
}
async function requestLandscapeExperience() {
  try { await document.documentElement.requestFullscreen?.() } catch { /* optional browser enhancement */ }
  try { await (screen.orientation as ScreenOrientation & { lock?: (value: string) => Promise<void> }).lock?.('landscape') } catch { /* iOS Safari may require manual rotation */ }
  updatePortraitHandset()
}
const backgroundMusic = new BackgroundMusicController()
let battleTrack = 0
let primed = false
let applyingAccountPreferences = false
let audioSaveTimer = 0
let audioSaveGeneration = 0
const battleTracks = ['/audio/legion12-battle-1.mp3', '/audio/legion12-battle-2.mp3']
const desiredTrack = () => route.path === '/game' ? battleTracks[battleTrack] : '/audio/legion12-site.mp3'
function refreshBackgroundMusic() {
  if (!primed || !audioPreferences.musicEnabled || audioPreferences.musicVolume <= 0) {
    backgroundMusic.setTarget(null)
    return
  }
  const src = desiredTrack()
  backgroundMusic.setTarget({
    src,
    loop: route.path !== '/game',
    volume: l12MusicOutputVolume(),
    onEnded: () => {
      if (route.path !== '/game') return
      battleTrack = (battleTrack + 1) % battleTracks.length
      refreshBackgroundMusic()
    },
  })
}
function primeMusic() { primed = true; refreshBackgroundMusic() }
watch(() => route.path, () => refreshBackgroundMusic())
watch(landscapeExperience, () => updatePortraitHandset())
watch(audioPreferences, value => {
  syncAudioStore()
  refreshBackgroundMusic()
  const generation = ++audioSaveGeneration
  window.clearTimeout(audioSaveTimer)
  if (!applyingAccountPreferences && platformState.account) {
    const snapshot = { ...value }
    audioSaveTimer = window.setTimeout(() => {
      void updateAudioPreferences(snapshot).then(saved => {
        if (generation !== audioSaveGeneration) return
        if (platformState.account) platformState.account.audioPreferences = saved
      }).catch(() => undefined)
    }, 500)
  }
}, { deep: true })
watch(() => platformState.account?.audioPreferences, async value => {
  if (!value) return
  applyingAccountPreferences = true
  applyAudioPreferences(value)
  await nextTick()
  applyingAccountPreferences = false
}, { immediate: true, deep: true })
onMounted(() => {
  window.addEventListener('pointerdown', primeMusic, { once: true })
  window.addEventListener('resize', updatePortraitHandset)
  window.visualViewport?.addEventListener('resize', updatePortraitHandset)
  updatePortraitHandset()
})
onBeforeUnmount(() => {
  audioSaveGeneration++
  window.clearTimeout(audioSaveTimer)
  backgroundMusic.destroy()
  window.removeEventListener('pointerdown', primeMusic)
  window.removeEventListener('resize', updatePortraitHandset)
  window.visualViewport?.removeEventListener('resize', updatePortraitHandset)
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
  <FriendRequestNotifications />
  <RankedIntegrityNotice />
  <Teleport to="body">
    <section v-if="landscapeExperience && portraitHandset" class="l12-rotate-device" role="dialog" aria-modal="true" aria-labelledby="rotate-device-title">
      <div><i aria-hidden="true">↻</i><h1 id="rotate-device-title">请横置设备</h1><p>横屏后会以真实安全区显示完整战场与操作区，不会再旋转或压缩整页界面。</p><button type="button" @click="requestLandscapeExperience">尝试进入横屏</button></div>
    </section>
  </Teleport>
</template>

<style>
:root[data-l12-card-size]:not([data-l12-card-size="auto"]) .archive-grid,
:root[data-l12-card-size]:not([data-l12-card-size="auto"]) .detail-grid{font-size:calc(1em * var(--l12-card-scale))}
:root[data-l12-card-size="small"] .archive-grid{grid-template-columns:repeat(auto-fill,minmax(96px,1fr))}
:root[data-l12-card-size="medium"] .archive-grid{grid-template-columns:repeat(auto-fill,minmax(112px,1fr))}
:root[data-l12-card-size="large"] .archive-grid{grid-template-columns:repeat(auto-fill,minmax(132px,1fr))}
:root[data-l12-card-size="small"] .detail-grid{grid-template-columns:repeat(6,1fr)}
:root[data-l12-card-size="medium"] .detail-grid{grid-template-columns:repeat(5,1fr)}
:root[data-l12-card-size="large"] .detail-grid{grid-template-columns:repeat(4,1fr)}
:root[data-l12-card-size="small"] .construction-grid{grid-template-columns:repeat(auto-fill,minmax(78px,1fr))}
:root[data-l12-card-size="medium"] .construction-grid{grid-template-columns:repeat(auto-fill,minmax(92px,1fr))}
:root[data-l12-card-size="large"] .construction-grid{grid-template-columns:repeat(auto-fill,minmax(108px,1fr))}
:root[data-l12-card-size="small"] .deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(100px,1fr))}
:root[data-l12-card-size="medium"] .deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(118px,1fr))}
:root[data-l12-card-size="large"] .deck-card-grid{grid-template-columns:repeat(auto-fill,minmax(138px,1fr))}
@media(min-width:701px){
  :root[data-l12-card-size="small"] .mine-grid,:root[data-l12-card-size="small"] .plaza-grid{grid-template-columns:repeat(4,minmax(0,1fr))}
  :root[data-l12-card-size="medium"] .mine-grid,:root[data-l12-card-size="medium"] .plaza-grid{grid-template-columns:repeat(3,minmax(0,1fr))}
  :root[data-l12-card-size="large"] .mine-grid,:root[data-l12-card-size="large"] .plaza-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
}
:root[data-l12-card-size="small"] .hand-card-wrap{width:64px;flex-basis:64px;height:89px}
:root[data-l12-card-size="small"] .hand-card-wrap .card-tile{width:64px;height:89px;flex-basis:64px}
:root[data-l12-card-size="medium"] .hand-card-wrap{width:74px;flex-basis:74px;height:103px}
:root[data-l12-card-size="medium"] .hand-card-wrap .card-tile{width:74px;height:103px;flex-basis:74px}
:root[data-l12-card-size="large"] .hand-card-wrap{width:88px;flex-basis:88px;height:123px}
:root[data-l12-card-size="large"] .hand-card-wrap .card-tile{width:88px;height:123px;flex-basis:88px}
:root[data-l12-card-size="small"] .formation-slot .card-tile{width:70px;height:98px;flex-basis:70px}
:root[data-l12-card-size="medium"] .formation-slot .card-tile{width:82px;height:115px;flex-basis:82px}
:root[data-l12-card-size="large"] .formation-slot .card-tile{width:92px;height:129px;flex-basis:92px}
:root[data-l12-animation="fast"] *{--l12-motion-duration:.55s}
:root[data-l12-animation="off"] *{--l12-motion-duration:0s}
:root[data-l12-animation="off"] *:not([data-essential-motion]){animation-duration:.001ms!important;animation-iteration-count:1!important;transition-duration:.001ms!important;scroll-behavior:auto!important}
.l12-rotate-device{position:fixed;z-index:2147483647;inset:0;display:grid;padding:max(24px,env(safe-area-inset-top)) max(24px,env(safe-area-inset-right)) max(24px,env(safe-area-inset-bottom)) max(24px,env(safe-area-inset-left));place-items:center;background:#070b0df2;color:#f1ede2;text-align:center}.l12-rotate-device>div{width:min(340px,100%);padding:28px 22px;border:1px solid #7c6939;background:#10191e;box-shadow:0 24px 70px #000}.l12-rotate-device i{display:grid;width:58px;height:58px;margin:auto;place-items:center;border:1px solid #d7b75d;border-radius:50%;color:#f0d27b;font-size:34px;font-style:normal}.l12-rotate-device h1{margin:17px 0 8px;font-size:22px}.l12-rotate-device p{margin:0;color:#abb6b6;font-size:13px;line-height:1.7}.l12-rotate-device button{min-height:42px;margin-top:20px;padding:9px 16px;border:1px solid #e2c36b;background:#e2c36b;color:#101416;font-size:14px;font-weight:900}
</style>
