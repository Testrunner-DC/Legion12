<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AdminMatchesPanel from './AdminMatchesPanel.vue'
const route = useRoute()
const emit = defineEmits<{ notice: [message: string] }>()
const matchId = computed(() => typeof route.params.matchId === 'string' ? route.params.matchId : '')
</script>
<template><AdminMatchesPanel :initial-match-id="matchId" @notice="emit('notice', $event)"/></template>
