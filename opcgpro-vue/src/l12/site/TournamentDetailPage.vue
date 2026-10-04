<script setup lang="ts">
import { computed, defineAsyncComponent } from 'vue'
import { useRoute } from 'vue-router'
import { authState, platformState } from '@/l12/platform'
import TournamentPublicDetail from './TournamentPublicDetail.vue'

const TournamentAccountDetail = defineAsyncComponent(() => import('./TournamentAccountDetail.vue'))
const route = useRoute()
const verifiedAccount = computed(() => authState.verified && platformState.account && !platformState.account.mustChangePassword && !platformState.account.mustChangeUsername)
// Remount on identity/code changes: privileged responses cannot survive logout or navigation.
const identityKey = computed(() => `${String(route.params.code)}:${verifiedAccount.value ? platformState.token : 'public'}`)
</script>

<template>
  <TournamentAccountDetail v-if="verifiedAccount" :key="identityKey" />
  <TournamentPublicDetail v-else :key="identityKey" />
</template>
