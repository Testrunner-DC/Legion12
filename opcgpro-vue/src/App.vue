<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { l12State, startAutomaticConnection, stopAutomaticConnection } from '@/l12/net'
import { authState, platformState } from '@/l12/platform'
import SiteShell from '@/l12/site/SiteShell.vue'
import GlobalBugFeedback from '@/l12/site/GlobalBugFeedback.vue'

const route = useRoute()
const router = useRouter()
const immersive = computed(() => route.meta.immersive === true)
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
