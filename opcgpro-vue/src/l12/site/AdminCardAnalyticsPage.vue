<script setup lang="ts">
import { useRouter } from 'vue-router'
import AdminCardAnalyticsPanel from './AdminCardAnalyticsPanel.vue'
const router = useRouter()
const emit = defineEmits<{ notice: [message: string] }>()
function openMatch(matchId: string) { void router.push(`/admin/matches/archive/${encodeURIComponent(matchId)}`) }
</script>
<template><AdminCardAnalyticsPanel @notice="emit('notice', $event)" @open-match="openMatch"/></template>
