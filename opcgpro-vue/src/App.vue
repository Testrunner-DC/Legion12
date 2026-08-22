<script setup lang="ts">
import { computed, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { l12State, startAutomaticConnection, stopAutomaticConnection } from '@/l12/net'
import { platformState } from '@/l12/platform'
import SiteShell from '@/l12/site/SiteShell.vue'
import GlobalBugFeedback from '@/l12/site/GlobalBugFeedback.vue'

const route = useRoute()
const router = useRouter()
const immersive = computed(() => route.meta.immersive === true)
watch(() => l12State.game, (game) => {
  if (game && route.path !== '/game') router.push('/game')
})
watch(() => platformState.token, token => {
  if (token) startAutomaticConnection()
  else stopAutomaticConnection()
}, { immediate: true })
</script>

<template>
  <router-view v-if="immersive" />
  <SiteShell v-else><router-view /></SiteShell>
  <GlobalBugFeedback />
</template>
